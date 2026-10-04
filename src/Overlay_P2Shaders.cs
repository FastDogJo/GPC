// CLAUDE : new file. PHASE 2 HLSL (compiled at runtime with D3DCompile, ps_5_0/vs_5_0).
// Pipeline : PSScene (captured picture -> HDR scene) -> bloom chain (prefilter / 13-tap down / tent up, Kawase glare) -> PSGlass (port of
// P:/SRC/C#/VfdEngine VFDGlass.hlsl PSGlass) -> PSComposite (post : chromatic aberration, bloom/glare/halation, exposure, tone map, vignette, dither).
// Bloom/post follow VFDBloom.hlsl / VFDPost.hlsl. All passes work in OUTPUT (overlay-window) pixel space so glow can spill over the bezel.
// Colours are STRAIGHT alpha in the HDR targets (alpha = coverage); PSComposite writes premultiplied BGRA for UpdateLayeredWindow.
namespace FastDog
{
  public static class P2Shaders
  {
    public const string Source = @"
cbuffer P2 : register(b0)
{
  float4 SrcRect;      // xy = uv offset, zw = uv scale of the emulator CLIENT area inside the captured frame
  float4 ScreenRect;   // emulator client area (the 'screen') in output pixels : x, y, w, h
  float4 OutSize;      // output w, h, 1/w, 1/h
  float4 Look1;        // brightness, scanlineOpacity, scanlinePeriod(px), vignette
  float4 Look2;        // phosphor tint rgb (normalised), tintAmount
  float4 Look3;        // curvature, cornerRadius (fraction of the shorter side), screen rotation (rad), glassOnTop
  float4 HoleRect;     // the bezel's detected screen hole (bbox) in output pixels : x, y, w, h
  float4 Misc;         // hasBezel, hasGlass, rimMirror (CLAUDE), backgroundLevel (linear, CLAUDE)
  float4 Bloom1;       // bloom strength, threshold, radius, softKnee
  float4 Post1;        // glare, halation, halationWarmth, chromaticAberration
  float4 Post2;        // exposure (linear), toneMap, dither (LSB), blackLevel (CLAUDE)
  float4 GlassA;       // thickness, roughness, reflectivity, fresnelStrength
  float4 GlassTint;    // tint rgb, absorptionScale
  float4 GlassMisc;    // internalReflection, distortion, scratches, dust
  float4 GlassBorder;  // borderWidthX (core-half units), borderBrightness, bevelHighlight, ghostFalloff
  float4 GlassBorder2; // transparencyExtent, borderWidthY, hasRimMask, discolouration
  float4 GlassRim;     // measured cover rim fractions L, R, T, B
  float4 GlassSurround;// surround strength (0 = off), blur, reach, lightBox intensity (0 = off)
  float4 GlassLight;   // light box x, y, size, softness
  float4 GlassRectPx;  // glass core centre x,y and half w,h in output pixels
  float4 GlassMisc2;   // borderTransparency, rimSampleWidth, glassPresence, seed
  float4 Env;          // ambient, keyLight, fillLight, envRotation
  float4 PowerA;       // CRT power animation : X, Y (raster size 0..1), Fill (glow), Content (raster visibility)
  float4 PowerB;       // core (white-hot dot), halo, active (0/1), unused
  float4 Pic;          // CLAUDE : picture offset (xy, screen half-size units) and scale (zw, 1 = fills the screen) inside the screen opening
};
Texture2D texFrame  : register(t0); // captured emulator frame
Texture2D texBloom  : register(t1); // finished bloom (level 0)
Texture2D texBezel  : register(t2); // bezel image (premultiplied), stretched over the whole output
Texture2D texCover  : register(t3); // glass cover (premultiplied, G = inside mask, B = rim depth, mips)
Texture2D texHdr    : register(t4); // HDR scene / glass result
Texture2D texIn     : register(t5); // generic input of the bloom/streak passes
Texture2D texHal    : register(t6); // wide (halation) bloom level
Texture2D texStreak : register(t7); // glare streaks
SamplerState sampLinear : register(s0);

struct VSOut { float4 Pos : SV_POSITION; float2 Uv : TEXCOORD0; };
VSOut VSFullscreen(uint id : SV_VertexID)
{
  VSOut o;
  float2 uv = float2(((id << 1) & 2), (id & 2));
  o.Uv = uv;
  o.Pos = float4(((uv * float2(2, -2)) + float2(-1, 1)), 0, 1);
  return o;
}

// ---- helpers (VFDCommon.hlsli) ----
float Hash21(float2 p)
{
  float3 p3 = frac((float3(p.xyx) * 0.1031));
  p3 += dot(p3, (p3.yzx + 33.33));
  return frac(((p3.x + p3.y) * p3.z));
}
float ValueNoise2D(float2 p)
{
  float2 i = floor(p); float2 f = frac(p);
  float a = Hash21(i); float b = Hash21(i + float2(1.0, 0.0)); float c = Hash21(i + float2(0.0, 1.0)); float d = Hash21(i + float2(1.0, 1.0));
  float2 u = (f * f * (3.0 - (2.0 * f)));
  return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}
float Fbm2(float2 p)
{
  float v = (ValueNoise2D(p) * 0.55);
  v += (ValueNoise2D((p * 2.13) + 17.0) * 0.30);
  v += (ValueNoise2D((p * 4.7) + 41.0) * 0.15);
  return v;
}
float SdRoundBox(float2 p, float2 b, float r)
{
  float2 q = (abs(p) - (b - r));
  return (length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r);
}
float3 AcesFilm(float3 x)
{
  const float a = 2.51; const float b = 0.03; const float c = 2.43; const float d = 0.59; const float e = 0.14;
  return saturate(((x * ((a * x) + b)) / ((x * ((c * x) + d)) + e)));
}
float3 AcesFilmHuePreserving(float3 x)
{
  float m = max(max(x.r, x.g), x.b);
  if (m <= 1e-6) { return float3(0.0, 0.0, 0.0); }
  return (x * (AcesFilm(float3(m, m, m)).x / m));
}
float3 Encode(float3 lin) { return pow(saturate(lin), (1.0 / 2.2)); }

// ---- PSScene : the captured emulator picture as an HDR scene --------------------------------------------------------------
// CRT barrel curvature, rounded corners, content rotation ; brightness, scanlines, phosphor tint ; alpha = the bezel's hole (or the screen rect).
float4 PSScene(VSOut i) : SV_Target
{
  float2 uv = i.Uv;
  float2 p = (uv * OutSize.xy);
  float2 hs = (ScreenRect.zw * 0.5);
  float2 local = ((p - ScreenRect.xy) / ScreenRect.zw);
  float2 c = ((local * 2.0) - 1.0);
  float k = Look3.x;
  float r2 = dot(c, c);
  float2 cw = (c * ((1.0 + (k * r2)) / (1.0 + k)));
  float2 cwRaw = cw;                                        // before the power-animation squash (the glow rect lives in this space)
  float2 pRect = float2(1.0, 1.0);
  if (PowerB.z > 0.5)
    {
      pRect = max(float2(PowerA.x, PowerA.y), (float2(1.5, 1.5) / hs));
      cw = (cw / pRect);                                    // the raster squashed into the shrinking rect
    }
  float rad = (Look3.y * min(ScreenRect.z, ScreenRect.w));
  float2 q = (cw * hs);
  float2 dd = (abs(q) - (hs - rad));
  float dist = (length(max(dd, 0.0)) - rad);
  float shape = ((((abs(cw.x) <= 1.0) && (abs(cw.y) <= 1.0)) ? 1.0 : 0.0) * ((dist <= 0.0) ? 1.0 : 0.0));
  float cs = cos(Look3.z); float sn = sin(Look3.z);
  float2 qr = float2(((q.x * cs) - (q.y * sn)), ((q.x * sn) + (q.y * cs)));
  float2 cr = (qr / hs);
  float2 crP = ((cr - Pic.xy) / Pic.zw);                    // CLAUDE : the picture's own offset / scale, independent of the opening
  float2 s = (SrcRect.xy + (((crP * 0.5) + 0.5) * SrcRect.zw));
  // CLAUDE : keep the bilinear tap inside the client area - at the picture edge it blended in the window frame / non-client texels (bright edge pixels)
  uint fw, fh; texFrame.GetDimensions(fw, fh);
  float2 ht = (1.5 / float2(fw, fh));                       // 1.5 texels = centre of the 2nd texel : also safe against a 1px client-rect mismatch
  s = clamp(s, (SrcRect.xy + ht), ((SrcRect.xy + SrcRect.zw) - ht));
  float inside = (shape * (((abs(cr.x) <= 1.0) && (abs(cr.y) <= 1.0)) ? 1.0 : 0.0));
  float picIn = (((abs(crP.x) <= 1.0) && (abs(crP.y) <= 1.0)) ? 1.0 : 0.0);
  float3 col = (pow(max(texFrame.SampleLevel(sampLinear, s, 0).rgb, 0.0), 2.2) * inside * picIn * Look1.x);

  float inRect = (((local.x >= 0.0) && (local.x <= 1.0) && (local.y >= 0.0) && (local.y <= 1.0)) ? 1.0 : 0.0);
  float row = floor((p.y - ScreenRect.y));
  float period = max(Look1.z, 2.0);
  float sl = (((row - (period * floor((row / period)))) >= (period * 0.5)) ? Look1.y : 0.0);
  col *= (1.0 - sl);
  float l = dot(col, float3(0.299, 0.587, 0.114));           // phosphor tint : the luminance takes the tint colour, black stays black
  col = lerp(col, (l * pow(Look2.rgb, 2.2)), Look2.a);
  col += (Misc.w * (1.0 - saturate(col)) * inside);          // CLAUDE : background level - the picture's black becomes grey (screen blend), text stays bright
  if (PowerB.z > 0.5)
    {
      col *= PowerA.w;                                      // raster visibility (fades as it collapses)
      float3 gcol = lerp(Look2.rgb, float3(1.0, 1.0, 1.0), (PowerB.x * 0.6)); // the phosphor tint colour (Look2.rgb is normalised), white-hot core
      float inP = (((abs(cwRaw.x) <= pRect.x) && (abs(cwRaw.y) <= pRect.y)) ? 1.0 : 0.0);
      float2 dp = (max((abs(cwRaw) - pRect), 0.0) * hs);
      float halo = (exp(-(length(dp) / 10.0)) * PowerA.z * PowerB.y);
      col += (gcol * ((PowerA.z * inP) + halo));
    }

  float m = inRect;
  if (Misc.x > 0.5)
    {
      float2 hl = ((p - HoleRect.xy) / HoleRect.zw);
      float inHole = (((hl.x >= 0.0) && (hl.x <= 1.0) && (hl.y >= 0.0) && (hl.y <= 1.0)) ? 1.0 : 0.0);
      m = (inHole * (1.0 - texBezel.SampleLevel(sampLinear, uv, 0).a));
    }
  return float4(col, m);
}

// ---- bloom chain -----------------------------------------------------------------------------------------------------------
float4 PSPrefilter(VSOut i) : SV_Target
{
  float4 h = texHdr.SampleLevel(sampLinear, i.Uv, 0);
  float3 c = (h.rgb * h.a);
  float thr = Bloom1.y;
  float br = max(max(c.r, c.g), c.b);
  float knee = max((thr * Bloom1.w), 1e-4);
  float soft = clamp(((br - thr) + knee), 0.0, (2.0 * knee));
  soft = ((soft * soft) / (4.0 * knee));
  float contribution = (max(soft, (br - thr)) / max(br, 1e-5));
  return float4((c * contribution), 1.0);
}
float4 PSDownsample(VSOut i) : SV_Target
{
  uint w, h; texIn.GetDimensions(w, h);
  float2 t = (1.0 / float2(w, h));
  float2 uv = i.Uv;
  float3 a = texIn.Sample(sampLinear, (uv + (t * float2(-2, -2)))).rgb;
  float3 b = texIn.Sample(sampLinear, (uv + (t * float2( 0, -2)))).rgb;
  float3 c = texIn.Sample(sampLinear, (uv + (t * float2( 2, -2)))).rgb;
  float3 d = texIn.Sample(sampLinear, (uv + (t * float2(-1, -1)))).rgb;
  float3 e = texIn.Sample(sampLinear, (uv + (t * float2( 1, -1)))).rgb;
  float3 f = texIn.Sample(sampLinear, (uv + (t * float2(-2,  0)))).rgb;
  float3 g = texIn.Sample(sampLinear, uv).rgb;
  float3 h2 = texIn.Sample(sampLinear, (uv + (t * float2( 2,  0)))).rgb;
  float3 i2 = texIn.Sample(sampLinear, (uv + (t * float2(-1,  1)))).rgb;
  float3 j = texIn.Sample(sampLinear, (uv + (t * float2( 1,  1)))).rgb;
  float3 k = texIn.Sample(sampLinear, (uv + (t * float2(-2,  2)))).rgb;
  float3 l = texIn.Sample(sampLinear, (uv + (t * float2( 0,  2)))).rgb;
  float3 m = texIn.Sample(sampLinear, (uv + (t * float2( 2,  2)))).rgb;
  float3 r = (g * 0.125);
  r += ((d + e + i2 + j) * 0.125);
  r += ((b + f + h2 + l) * 0.0625);
  r += ((a + c + k + m) * 0.03125);
  return float4(r, 1.0);
}
float4 PSUpsample(VSOut i) : SV_Target
{
  uint w, h; texIn.GetDimensions(w, h);
  float2 o = ((1.0 / float2(w, h)) * max(Bloom1.z, 0.01));
  float2 uv = i.Uv;
  float3 r = (texIn.Sample(sampLinear, uv).rgb * 4.0);
  r += (texIn.Sample(sampLinear, (uv + float2(-o.x, 0))).rgb * 2.0);
  r += (texIn.Sample(sampLinear, (uv + float2( o.x, 0))).rgb * 2.0);
  r += (texIn.Sample(sampLinear, (uv + float2(0, -o.y))).rgb * 2.0);
  r += (texIn.Sample(sampLinear, (uv + float2(0,  o.y))).rgb * 2.0);
  r += texIn.Sample(sampLinear, (uv + float2(-o.x, -o.y))).rgb;
  r += texIn.Sample(sampLinear, (uv + float2( o.x, -o.y))).rgb;
  r += texIn.Sample(sampLinear, (uv + float2(-o.x,  o.y))).rgb;
  r += texIn.Sample(sampLinear, (uv + float2( o.x,  o.y))).rgb;
  return float4((r / 16.0), 1.0);
}
float3 Kawase(float2 uv, int stride)
{
  uint w, h; texIn.GetDimensions(w, h);
  float2 t = (1.0 / float2(w, h));
  float3 c = texIn.Sample(sampLinear, uv).rgb;
  c += texIn.Sample(sampLinear, (uv + (t * float2(stride, 0)))).rgb;
  c += texIn.Sample(sampLinear, (uv - (t * float2(stride, 0)))).rgb;
  return (c / 3.0);
}
float4 PSStreak1(VSOut i) : SV_Target  { return float4(Kawase(i.Uv, 1), 1.0); }
float4 PSStreak4(VSOut i) : SV_Target  { return float4(Kawase(i.Uv, 4), 1.0); }
float4 PSStreak16(VSOut i) : SV_Target { return float4(Kawase(i.Uv, 16), 1.0); }

// ---- PSGlass : port of VFDGlass.hlsl ---------------------------------------------------------------------------------------
static const float cGlassIor = 1.52;
static const float cEyeDistance = 2.5;
static const float cThicknessToPixels = 1.2;
static const int cRimTaps = 4;
static const float cRimGlowStrength = 0.8;

float RemapGlassAxis(float x, float borderWidth, float rimFrac)
{
  float s = sign(x);
  float ax = abs(x);
  float coreOffset = (ax * (0.5 - rimFrac));
  float bandT = saturate(((ax - 1.0) / max(borderWidth, 1e-4)));
  float borderOffset = lerp((0.5 - rimFrac), 0.5, bandT);
  float offset = ((ax <= 1.0) ? coreOffset : borderOffset);
  return (0.5 + (s * offset));
}
float ScratchField(float2 p, float seed)
{
  float2 cellP = (p * 6.0); float2 cellId = floor(cellP); float2 cellF = frac(cellP);
  float presence = step(0.85, Hash21(cellId + seed + 501.0));
  float angle = (Hash21(cellId + seed + 733.0) * 3.14159);
  float2 dir = float2(cos(angle), sin(angle)); float2 perp = float2(-dir.y, dir.x);
  float lineDist = abs(dot((cellF - 0.5), perp));
  float w = length(float2(ddx(lineDist), ddy(lineDist)));
  return (presence * saturate(((0.02 - lineDist) / max(w, 1e-5))));
}
float DustField(float2 p, float seed)
{
  float2 cellP = (p * 9.0); float2 cellId = floor(cellP); float2 cellF = frac(cellP);
  float presence = step(0.90, Hash21(cellId + seed + 901.0));
  float2 center = float2(Hash21(cellId + seed + 953.0), Hash21(cellId + seed + 977.0));
  float r = (0.08 + (0.10 * Hash21(cellId + seed + 991.0)));
  float d = (length(cellF - center) - r);
  float w = length(float2(ddx(d), ddy(d)));
  return (presence * saturate((0.5 - (d / max(w, 1e-5)))));
}
// glass-local (core = +-1, y up) -> output uv
float2 LocalToUv(float2 l)
{
  return ((GlassRectPx.xy + (float2(l.x, -l.y) * GlassRectPx.zw)) * OutSize.zw);
}
float3 SurroundReflection(float2 p, float3 n, float2 outerHalf)
{
  float blur = GlassSurround.y;
  float reach = max(GlassSurround.z, 1e-3);
  float2 q = (abs(p) / outerHalf);
  bool xDom = (q.x >= q.y);
  float edge = (xDom ? outerHalf.x : outerHalf.y);
  float coord = (xDom ? p.x : p.y);
  float dist = max((edge - abs(coord)), 0.0);
  float mirrored = (sign(coord) * (edge + dist));
  float2 mirLocal = (xDom ? float2(mirrored, p.y) : float2(p.x, mirrored));
  mirLocal += (n.xy * 0.5);
  float3 sum = float3(0.0, 0.0, 0.0);
  [unroll]
  for (int i = 0; i < 8; i++)
    {
      float a = (float(i) * 0.785398);
      float2 dir = float2(cos(a), sin(a));
      float2 l0 = (mirLocal + (dir * blur));
      float2 l1 = (mirLocal + (dir * (blur * 0.5)));
      sum += texHdr.SampleLevel(sampLinear, saturate(LocalToUv(l0)), 0).rgb;
      sum += texHdr.SampleLevel(sampLinear, saturate(LocalToUv(l1)), 0).rgb;
    }
  return ((sum / 16.0) * exp(-(dist / reach)));
}
float LightBoxSpec(float2 p, float3 n, float3 viewDir, float4 light)
{
  float3 r = reflect(-viewDir, n);
  if (r.z <= 1e-3) { return 0.0; }
  float2 hit = (p + (r.xy * (cEyeDistance / r.z)));
  float2 center = (light.xy * cEyeDistance);
  float halfSize = max((light.z * cEyeDistance), 1e-3);
  float soft = max((light.w * halfSize), 1e-3);
  float d = SdRoundBox((hit - center), float2(halfSize, halfSize), (halfSize * 0.3));
  return (1.0 - smoothstep(-soft, soft, d));
}

float4 PSGlass(VSOut input) : SV_Target
{
  float2 uv = input.Uv;
  float2 pxp = (uv * OutSize.xy);
  float2 pc = ((pxp - GlassRectPx.xy) / GlassRectPx.zw);
  float2 p = float2(pc.x, -pc.y);                 // glass-local, y up (VSGlass's Local)
  float borderWidthX = GlassBorder.x;
  float borderWidthY = GlassBorder2.y;
  bool inQuad = ((abs(p.x) <= (1.0 + borderWidthX)) && (abs(p.y) <= (1.0 + borderWidthY)));

  float thickness = GlassA.x;
  float roughness = GlassA.y;
  float reflectivity = GlassA.z;
  float fresnelStrength = GlassA.w;
  float3 tint = GlassTint.rgb;
  float absorptionScale = GlassTint.w;
  float internalReflection = GlassMisc.x;
  float distortion = GlassMisc.y;
  float scratches = GlassMisc.z;
  float dust = GlassMisc.w;
  float discolouration = GlassBorder2.w;
  float seed = GlassMisc2.w;

  float rimFracX = ((p.x < 0.0) ? GlassRim.x : GlassRim.y);
  float rimFracY = ((-p.y < 0.0) ? GlassRim.z : GlassRim.w);
  float2 glassUv = float2(RemapGlassAxis(p.x, borderWidthX, rimFracX), RemapGlassAxis(-p.y, borderWidthY, rimFracY));
  float4 glassTex = texCover.Sample(sampLinear, glassUv);
  float glassLum = glassTex.r;
  float glassCoverA = glassTex.a;
  float glassInside = glassTex.g;
  float glassRimDepth = glassTex.b;
  float hasRimMask = step(0.5, GlassBorder2.z);

  float2 glassGrad = float2(ddx_fine(glassLum), ddy_fine(glassLum));
  float2 normalXY = (clamp((glassGrad * 45.0), -1.5, 1.5) * (0.5 + distortion));
  float3 normal = normalize(float3(normalXY, 1.0));

  float3 viewDir = normalize(float3(-p, cEyeDistance));
  float cosTheta = saturate(dot(viewDir, normal));
  float f0 = pow(((cGlassIor - 1.0) / (cGlassIor + 1.0)), 2.0);
  float fresnel0 = (f0 + ((1.0 - f0) * pow((1.0 - cosTheta), 5.0)));

  float2 texel = OutSize.zw;
  float2 refractOffset = (normal.xy * ((cGlassIor - 1.0) * thickness * cThicknessToPixels) * texel);
  float2 sampleUv = saturate((uv + refractOffset));

  float4 hdrSample = texHdr.SampleLevel(sampLinear, sampleUv, 0);
  float3 hdr = hdrSample.rgb;

  if (roughness > 0.001)
    {
      float ringR = (roughness * 5.0);
      float3 blur = float3(0.0, 0.0, 0.0);
      blur += texHdr.SampleLevel(sampLinear, (sampleUv + (texel * float2( ringR, 0.0))), 0).rgb;
      blur += texHdr.SampleLevel(sampLinear, (sampleUv + (texel * float2(-ringR, 0.0))), 0).rgb;
      blur += texHdr.SampleLevel(sampLinear, (sampleUv + (texel * float2(0.0,  ringR))), 0).rgb;
      blur += texHdr.SampleLevel(sampLinear, (sampleUv + (texel * float2(0.0, -ringR))), 0).rgb;
      hdr = lerp(hdr, (blur * 0.25), saturate((roughness * 1.5)));
    }

  float3 sigma = -log(max(tint, 1e-4));
  float3 transmittance = exp(-(sigma * thickness * absorptionScale));
  float3 transmitted = (hdr * transmittance);

  float discolNoise = Fbm2(((p * 0.45) + (seed * 1.7)));
  float3 discolTint = lerp(float3(1.0, 1.0, 1.0), float3(1.0, 0.85, 0.5), (discolouration * discolNoise));
  transmitted *= discolTint;

  float dustMask = (DustField(p, seed) * dust);
  transmitted *= (1.0 - (dustMask * 0.5));

  float theta = acos(max(cosTheta, 0.15));
  float ghostPixels = min((((2.0 * thickness) * tan(theta)) * cThicknessToPixels), 40.0);
  float2 ghostOffset = (normalize((p + 1e-4)) * float2(ghostPixels, -ghostPixels) * texel);
  float3 ghost = texHdr.SampleLevel(sampLinear, saturate((sampleUv + ghostOffset)), 0).rgb;
  transmitted += (ghost * internalReflection * fresnel0);

  float3 floorTint = (Env.z * 0.12).xxx;
  float3 env = (((glassLum * Env.y)).xxx + floorTint);
  env = lerp(env, floorTint, saturate(roughness));

  float fresnel = (fresnel0 * fresnelStrength);
  float3 reflection = (env * (reflectivity + fresnel));

  float scratchMask = (ScratchField(p, seed) * scratches);
  reflection += (scratchMask * env * 0.6);
  reflection += (dustMask * Env.y * float3(1.0, 0.95, 0.85) * 0.4);

  float reflWeight = (reflectivity + fresnel);
  if (GlassSurround.x > 0.0)
    {
      float2 outerHalf = float2((1.0 + borderWidthX), (1.0 + borderWidthY));
      reflection += (SurroundReflection(p, normal, outerHalf) * GlassSurround.x * reflWeight * (1.0 - (0.5 * saturate(roughness))));
    }
  if (GlassSurround.w > 0.0)
    {
      float4 light = GlassLight;
      light.w = (light.w + roughness);
      reflection += (LightBoxSpec(p, normal, viewDir, light) * GlassSurround.w * Env.y * reflWeight);
    }

  float3 finalColor = ((transmitted * (1.0 - fresnel)) + reflection);

  float borderBrightness = GlassBorder.y;
  float borderMask = 0.0;
  float bandT = 0.0;
  float3 paneColor = finalColor;
  if ((borderWidthX > 0.0) || (borderWidthY > 0.0))
    {
      float edgeDist = max(abs(p.x), abs(p.y));
      float edgeAa = length(float2(ddx(edgeDist), ddy(edgeDist)));
      borderMask = saturate(((edgeDist - 1.0) / max(edgeAa, 1e-5)));

      float bandTx = saturate(((abs(p.x) - 1.0) / max(borderWidthX, 1e-4)));
      float bandTy = saturate(((abs(p.y) - 1.0) / max(borderWidthY, 1e-4)));
      bandT = max(bandTx, bandTy);

      float tilt = sin((bandT * 1.5708));
      float2 radialDir = normalize((p + 1e-5));
      float3 bevelNormal = normalize(float3((radialDir * tilt), sqrt(saturate((1.0 - (tilt * tilt))))));
      float bevelCos = saturate(dot(bevelNormal, viewDir));
      float bevelFresnel = pow((1.0 - bevelCos), 3.0);
      float bevelHighlight = GlassBorder.z;
      float3 bevelColor = (lerp(reflection, float3(1.0, 1.0, 1.0), (bevelFresnel * bevelHighlight)) * borderBrightness);

      float rimSampleWidth = GlassMisc2.y;
      float2 rimLocal = (p / max(edgeDist, 1e-4));
      float3 rimSceneSum = float3(0.0, 0.0, 0.0);
      [unroll]
      for (int rt = 0; rt < cRimTaps; rt++)
        {
          float tapT = (float(rt) / float(cRimTaps - 1));
          float depthFrac = (rimSampleWidth * tapT);
          // CLAUDE : mirror mode - the sampled depth grows with the position across the bevel (bandT 0 at the display edge -> 1 at the outer edge),
          // so the sample width is fitted onto the bevel width, mirrored about the edge. The taps only spread +-5% of the width to smooth it.
          if (Misc.z > 0.5) { depthFrac = max((rimSampleWidth * (bandT + (0.1 * (tapT - 0.5)))), 0.0); }
          float2 tapLocal = (rimLocal * (1.0 - depthFrac));
          rimSceneSum += texHdr.SampleLevel(sampLinear, saturate(LocalToUv(tapLocal)), 0).rgb;
        }
      float3 rimScene = (rimSceneSum / float(cRimTaps));
      float rimFade = saturate((1.0 - (bandT * GlassBorder.w)));
      bevelColor += (rimScene * glassLum * borderBrightness * cRimGlowStrength * rimFade);

      float transparencyExtent = saturate(GlassBorder2.x);
      float depthT = lerp(bandT, glassRimDepth, hasRimMask);
      float depthAa = max(fwidth(depthT), 1e-5);
      float borderTransparency = (saturate(GlassMisc2.x) * saturate(((((transparencyExtent - depthT) / depthAa)) + 0.5)));
      float rimRegion = lerp(borderMask, 1.0, hasRimMask);
      finalColor = lerp(finalColor, bevelColor, (rimRegion * (1.0 - borderTransparency)));
    }

  float coverRegion = lerp(borderMask, 1.0, hasRimMask);
  float coverA = lerp(1.0, glassCoverA, coverRegion);
  float outerSide = (coverRegion * (1.0 - glassInside));
  float4 sceneRaw = texHdr.SampleLevel(sampLinear, uv, 0);
  finalColor = lerp(lerp(paneColor, sceneRaw.rgb, outerSide), finalColor, coverA);

  float glassPresence = GlassMisc2.z;
  float paneAlpha = lerp(sceneRaw.a, 1.0, glassPresence);
  float glassAlpha = lerp(lerp(paneAlpha, sceneRaw.a, outerSide), paneAlpha, coverA);
  return (inQuad ? float4(finalColor, glassAlpha) : sceneRaw);
}

// ---- PSComposite : post processing -> premultiplied BGRA ------------------------------------------------------------------
float4 PSComposite(VSOut i) : SV_Target
{
  float2 uv = i.Uv;
  float2 p = (uv * OutSize.xy);
  float2 centred = (uv - 0.5);
  float r2c = dot(centred, centred);
  float4 h0 = texHdr.SampleLevel(sampLinear, uv, 0);
  float3 hdr = h0.rgb;
  float a = h0.a;
  float caAmount = (Post1.w * 0.004 * r2c);
  if (caAmount > 0.0)
    {
      hdr.r = texHdr.SampleLevel(sampLinear, (uv + (centred * caAmount)), 0).r;
      hdr.b = texHdr.SampleLevel(sampLinear, (uv - (centred * caAmount)), 0).b;
    }
  // Glass behind the bezel (Glass On Top off) : only what shows through the bezel's hole survives.
  if ((Look3.w < 0.5) && (Misc.y > 0.5) && (Misc.x > 0.5))
    {
      float2 hl = ((p - HoleRect.xy) / HoleRect.zw);
      float inHole = (((hl.x >= 0.0) && (hl.x <= 1.0) && (hl.y >= 0.0) && (hl.y <= 1.0)) ? 1.0 : 0.0);
      a *= (inHole * (1.0 - texBezel.SampleLevel(sampLinear, uv, 0).a));
    }
  float3 warm = lerp(float3(1.0, 1.0, 1.0), float3(1.0, 0.55, 0.30), Post1.z);
  float3 glow = ((texBloom.SampleLevel(sampLinear, uv, 0).rgb * Bloom1.x)
               + (texStreak.SampleLevel(sampLinear, uv, 0).rgb * Post1.x)
               + (texHal.SampleLevel(sampLinear, uv, 0).rgb * warm * Post1.y));
  float exposure = Post2.x;
  int toneMap = (int) Post2.y;
  float3 lin = ((hdr + glow) * exposure);
  float3 linG = (glow * exposure);
  if (toneMap == 1) { lin = (lin / (1.0 + lin)); linG = (linG / (1.0 + linG)); }
  else if (toneMap == 2) { lin = AcesFilm(lin); linG = AcesFilm(linG); }
  else if (toneMap == 3) { lin = AcesFilmHuePreserving(lin); linG = AcesFilmHuePreserving(linG); }
  float2 local = ((p - ScreenRect.xy) / ScreenRect.zw);
  float2 cl = ((local * 2.0) - 1.0);
  float vig = saturate((1.0 - (Look1.w * dot(cl, cl) * 0.5)));
  float3 pic = (Encode(lin) * vig);
  pic += ((Hash21(i.Pos.xy) - 0.5) * (Post2.z / 255.0));
  pic = (max((pic - Post2.w), 0.0) / max((1.0 - Post2.w), 1e-3));   // CLAUDE : black level - darks below it go to true black, the rest is rescaled
  // CLAUDE : glow spill (encoded on its own, so a tiny linear value becomes a visible grey) only where the bezel face is solid -
  //   partial-alpha pixels along the bezel's hole edge no longer pick it up (bright edge pixels).
  float spill = (1.0 - a);
  if (Misc.x > 0.5) { spill = min(spill, smoothstep(0.85, 1.0, texBezel.SampleLevel(sampLinear, uv, 0).a)); }
  float3 rgb = ((saturate(pic) * a) + (Encode(linG) * spill));
  // CLAUDE : stacking is emulator < bezel window < this overlay. A partial-alpha pixel on the bezel's hole edge (bezel alpha b, overlay a = 1-b) leaves
  //   the real emulator output showing through at weight b(1-b) (it moves with the Screen Fit offset). Make those pixels opaque : the overlay itself
  //   composites the bezel (premultiplied) over black, so nothing underneath can show. Solid bezel (b = 1) stays transparent - the bezel window shows.
  if (Misc.x > 0.5)
    {
      float2 hl2 = ((p - HoleRect.xy) / HoleRect.zw);
      float inHole2 = (((hl2.x >= 0.0) && (hl2.x <= 1.0) && (hl2.y >= 0.0) && (hl2.y <= 1.0)) ? 1.0 : 0.0);
      float4 bz = texBezel.SampleLevel(sampLinear, uv, 0);
      if ((inHole2 > 0.5) && (bz.a < 0.999) && (a < 1.0))
        {
          return float4((rgb + ((1.0 - a) * bz.rgb)), 1.0);
        }
    }
  return float4(rgb, a);
}
";
  }
}
