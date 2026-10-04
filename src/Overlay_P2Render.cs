// CLAUDE : new file. PHASE 2 renderer (IOverlayRenderer) : D3D11 (Silk.NET) passes over the captured emulator frame
// -> premultiplied BGRA -> staging readback -> UpdateLayeredWindow on the click-through presenter window.
// Passes : PSScene -> [bloom chain + glare] -> PSGlass (port of VFDGlass.hlsl) -> PSComposite (post). See Overlay_P2Shaders.cs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace FastDog
{
  public sealed unsafe class GlowRenderer : IOverlayRenderer, IDisposable
  {
    // 22 float4, same order as the HLSL cbuffer
    [StructLayout(LayoutKind.Sequential)]
    private struct P2Cb
    {
      public Vector4 SrcRect, ScreenRect, OutSize, Look1, Look2, Look3, HoleRect, Misc, Bloom1, Post1, Post2;
      public Vector4 GlassA, GlassTint, GlassMisc, GlassBorder, GlassBorder2, GlassRim, GlassSurround, GlassLight, GlassRectPx, GlassMisc2, Env, PowerA, PowerB, Pic;
    }
    private struct Level { public ID3D11Texture2D* Tex; public ID3D11RenderTargetView* Rtv; public ID3D11ShaderResourceView* Srv; public int W, H; }

    // FIELDS & PROPERTIES {{{
    private readonly IntPtr hwnd;          // presenter window
    private readonly IntPtr emuHwnd;
    private ID3D11Device* dev = null;
    private ID3D11DeviceContext* ctx = null;
    private WgcCapture? cap = null;
    private ID3D11VertexShader* vs = null;
    private ID3D11PixelShader* psScene = null, psPre = null, psDown = null, psUp = null, psGlass = null, psComp = null, psSt1 = null, psSt4 = null, psSt16 = null;
    private ID3D11SamplerState* samp = null;
    private ID3D11BlendState* blendAdd = null;
    private ID3D11Buffer* cb = null;
    private ID3D11Texture2D* frameTex = null; private ID3D11ShaderResourceView* frameSrv = null; private int frameW = 0, frameH = 0; // copy of the captured frame
    private Level hdrScene, hdrGlass;      // O-size HDR targets
    private ID3D11Texture2D* finalTex = null; private ID3D11RenderTargetView* finalRtv = null;
    private ID3D11Texture2D* stagingTex = null;
    private ID3D11Texture2D* bezelTex = null; private ID3D11ShaderResourceView* bezelSrv = null;
    private ID3D11Texture2D* coverTex = null; private ID3D11ShaderResourceView* coverSrv = null;
    private readonly List<Level> levels = new List<Level>();
    private Level streakA, streakB;        // glare ping-pong targets (level 0 size)
    private LayeredSurface? surf = null;
    private NRect rectO, rectE;           // overlay (presenter) rect and emulator client rect, screen coords
    private int outW = 0, outH = 0;
    private bool dirty = true;
    private bool ready = false;
    public bool Ready { get { return ready; } }
    private int frames = 0;
    // }}}

    public GlowRenderer(IntPtr presenterHwnd, IntPtr emulatorHwnd) { hwnd = presenterHwnd; emuHwnd = emulatorHwnd; }

    private static void Rel(void* p) { if (p != null) { Marshal.Release((IntPtr) p); } }
    private static void RelLevel(ref Level l) { Rel(l.Srv); Rel(l.Rtv); Rel(l.Tex); l = default; }

    public bool Init() {{{
      try
        {
          D3D11 d3d = D3D11.GetApi(null, false);
          ID3D11Device* d = null; ID3D11DeviceContext* c = null; D3DFeatureLevel fl;
          int hr = d3d.CreateDevice((IDXGIAdapter*) null, D3DDriverType.Hardware, IntPtr.Zero, (uint) CreateDeviceFlag.BgraSupport, (D3DFeatureLevel*) null, 0, D3D11.SdkVersion, &d, &fl, &c);
          if (hr < 0) { FD.WL($"ERROR : [P2] CreateDevice 0x{hr:X}\n"); return false; }
          dev = d; ctx = c;
          if (!CompileShaders()) { return false; }
          CreateStates();
          cap = new WgcCapture(dev);
          if (!cap.Start(emuHwnd)) { return false; }
          UploadBezel();
          UploadGlass();
          ready = true;
          FD.WL($"[P2] renderer ready (feature level {fl})\n");
          return true;
        }
      catch (Exception ex) { FD.WL($"ERROR : [P2] Init : {ex}\n"); return false; }
    }}}

    // SHADERS & STATES {{{
    private ID3D10Blob* Compile(string entry, string target) {{{
      D3DCompiler comp = D3DCompiler.GetApi();
      byte[] src = System.Text.Encoding.ASCII.GetBytes(P2Shaders.Source);
      IntPtr pe = Marshal.StringToHGlobalAnsi(entry); IntPtr pt = Marshal.StringToHGlobalAnsi(target); IntPtr pn = Marshal.StringToHGlobalAnsi("P2.hlsl");
      ID3D10Blob* code = null; ID3D10Blob* err = null;
      int hr;
      fixed (byte* ps = src) { hr = comp.Compile(ps, (nuint) src.Length, (byte*) pn, (D3DShaderMacro*) null, (ID3DInclude*) null, (byte*) pe, (byte*) pt, 0, 0, &code, &err); }
      Marshal.FreeHGlobal(pe); Marshal.FreeHGlobal(pt); Marshal.FreeHGlobal(pn);
      if ((hr < 0) || (code == null))
        {
          string msg = ((err != null) ? Marshal.PtrToStringAnsi((IntPtr) err->GetBufferPointer()) ?? "" : "");
          FD.WL($"ERROR : [P2] shader {entry} : {msg}\n");
          Rel(err);
          return null;
        }
      Rel(err);
      return code;
    }}}
    private ID3D11PixelShader* MakePs(string entry) {{{
      ID3D10Blob* b = Compile(entry, "ps_5_0"); if (b == null) { return null; }
      ID3D11PixelShader* p = null; dev->CreatePixelShader(b->GetBufferPointer(), b->GetBufferSize(), (ID3D11ClassLinkage*) null, &p); Rel(b);
      return p;
    }}}
    private bool CompileShaders() {{{
      ID3D10Blob* b = Compile("VSFullscreen", "vs_5_0"); if (b == null) { return false; }
      ID3D11VertexShader* v = null; dev->CreateVertexShader(b->GetBufferPointer(), b->GetBufferSize(), (ID3D11ClassLinkage*) null, &v); vs = v; Rel(b);
      psScene = MakePs("PSScene"); psPre = MakePs("PSPrefilter"); psDown = MakePs("PSDownsample"); psUp = MakePs("PSUpsample");
      psGlass = MakePs("PSGlass"); psComp = MakePs("PSComposite");
      psSt1 = MakePs("PSStreak1"); psSt4 = MakePs("PSStreak4"); psSt16 = MakePs("PSStreak16");
      return ((vs != null) && (psScene != null) && (psPre != null) && (psDown != null) && (psUp != null) && (psGlass != null) && (psComp != null) && (psSt1 != null) && (psSt4 != null) && (psSt16 != null));
    }}}
    private void CreateStates() {{{
      SamplerDesc sd = new SamplerDesc { Filter = Filter.MinMagMipLinear, AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp, ComparisonFunc = ComparisonFunc.Never, MaxLOD = float.MaxValue };
      ID3D11SamplerState* s = null; dev->CreateSamplerState(&sd, &s); samp = s;
      BlendDesc bd = new BlendDesc();
      bd.RenderTarget[0] = new RenderTargetBlendDesc
        {
          BlendEnable = true, SrcBlend = Blend.One, DestBlend = Blend.One, BlendOp = BlendOp.Add,
          SrcBlendAlpha = Blend.One, DestBlendAlpha = Blend.One, BlendOpAlpha = BlendOp.Add, RenderTargetWriteMask = (byte) ColorWriteEnable.All
        };
      ID3D11BlendState* b = null; dev->CreateBlendState(&bd, &b); blendAdd = b;
      BufferDesc cd = new BufferDesc { ByteWidth = (uint) sizeof(P2Cb), Usage = Usage.Dynamic, BindFlags = (uint) BindFlag.ConstantBuffer, CPUAccessFlags = (uint) CpuAccessFlag.Write };
      ID3D11Buffer* cbuf = null; dev->CreateBuffer(&cd, (SubresourceData*) null, &cbuf); cb = cbuf;
    }}}
    // }}}

    // TEXTURES {{{
    private ID3D11Texture2D* MakeTex(int w, int h, Format fmt, uint bind, Usage usage, uint cpu, SubresourceData* init) {{{
      Texture2DDesc td = new Texture2DDesc { Width = (uint) w, Height = (uint) h, MipLevels = 1, ArraySize = 1, Format = fmt, SampleDesc = new SampleDesc(1, 0), Usage = usage, BindFlags = bind, CPUAccessFlags = cpu, MiscFlags = 0 };
      ID3D11Texture2D* t = null;
      int hr = dev->CreateTexture2D(&td, init, &t);
      if (hr < 0) { FD.WL($"ERROR : [P2] CreateTexture2D {w}x{h} {fmt} 0x{hr:X}\n"); return null; }
      return t;
    }}}
    private ID3D11ShaderResourceView* MakeSrv(ID3D11Texture2D* t) {{{
      ID3D11ShaderResourceView* v = null; dev->CreateShaderResourceView((ID3D11Resource*) t, (ShaderResourceViewDesc*) null, &v); return v;
    }}}
    private ID3D11RenderTargetView* MakeRtv(ID3D11Texture2D* t) {{{
      ID3D11RenderTargetView* v = null; dev->CreateRenderTargetView((ID3D11Resource*) t, (RenderTargetViewDesc*) null, &v); return v;
    }}}
    private Level MakeLevel(int w, int h) {{{
      ID3D11Texture2D* t = MakeTex(w, h, Format.FormatR16G16B16A16Float, (uint) (BindFlag.RenderTarget | BindFlag.ShaderResource), Usage.Default, 0, null);
      if (t == null) { return default; }
      return new Level { Tex = t, Rtv = MakeRtv(t), Srv = MakeSrv(t), W = w, H = h };
    }}}
    private ID3D11Texture2D* TexFromBitmap(Bitmap bmp) {{{
      BitmapData bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
      try
        {
          SubresourceData sr = new SubresourceData { PSysMem = (void*) bd.Scan0, SysMemPitch = (uint) bd.Stride };
          return MakeTex(bmp.Width, bmp.Height, Format.FormatB8G8R8A8Unorm, (uint) BindFlag.ShaderResource, Usage.Immutable, 0, &sr);
        }
      finally { bmp.UnlockBits(bd); }
    }}}
    public void UploadBezel() {{{
      Rel(bezelSrv); Rel(bezelTex); bezelSrv = null; bezelTex = null;
      if (!BezelModel.Loaded) { dirty = true; return; }
      bezelTex = TexFromBitmap(BezelModel.Image!);
      if (bezelTex != null) { bezelSrv = MakeSrv(bezelTex); }
      dirty = true;
    }}}
    // The glass cover (M?VFDGlass.png) with its inside mask / rim depth channels and full mip chain.
    public void UploadGlass() {{{
      Rel(coverSrv); Rel(coverTex); coverSrv = null; coverTex = null;
      FD.gGlassCover = null;
      string file = FD.GlassCoverFileFor();
      GlassCoverData? d = GlassCover.Load(file);
      if (d is null) { dirty = true; return; }
      int n = d.Mips.Count;
      Texture2DDesc td = new Texture2DDesc { Width = (uint) d.W, Height = (uint) d.H, MipLevels = (uint) n, ArraySize = 1, Format = Format.FormatR8G8B8A8Unorm, SampleDesc = new SampleDesc(1, 0), Usage = Usage.Immutable, BindFlags = (uint) BindFlag.ShaderResource };
      GCHandle[] pins = new GCHandle[n];
      SubresourceData* init = stackalloc SubresourceData[n];
      try
        {
          for (int m = 0; m < n; m++)
            {
              pins[m] = GCHandle.Alloc(d.Mips[m], GCHandleType.Pinned);
              init[m] = new SubresourceData { PSysMem = (void*) pins[m].AddrOfPinnedObject(), SysMemPitch = (uint) (d.MipW[m] * 4) };
            }
          ID3D11Texture2D* t = null;
          int hr = dev->CreateTexture2D(&td, init, &t);
          if (hr < 0) { FD.WL($"ERROR : [Glass] cover texture 0x{hr:X}\n"); dirty = true; return; }
          coverTex = t; coverSrv = MakeSrv(t);
        }
      finally { for (int m = 0; m < n; m++) { if (pins[m].IsAllocated) { pins[m].Free(); } } }
      FD.gGlassCover = d;
      dirty = true;
    }}}
    private void FreeSizeDependent() {{{
      foreach (Level l in levels) { Rel(l.Srv); Rel(l.Rtv); Rel(l.Tex); }
      levels.Clear();
      RelLevel(ref streakA); RelLevel(ref streakB); RelLevel(ref hdrScene); RelLevel(ref hdrGlass);
      Rel(finalRtv); Rel(finalTex); Rel(stagingTex); finalRtv = null; finalTex = null; stagingTex = null;
      surf?.Dispose(); surf = null;
    }}}
    private void BuildSizeDependent() {{{
      FreeSizeDependent();
      finalTex = MakeTex(outW, outH, Format.FormatB8G8R8A8Unorm, (uint) (BindFlag.RenderTarget | BindFlag.ShaderResource), Usage.Default, 0, null);
      if (finalTex != null) { finalRtv = MakeRtv(finalTex); }
      stagingTex = MakeTex(outW, outH, Format.FormatB8G8R8A8Unorm, 0, Usage.Staging, (uint) CpuAccessFlag.Read, null);
      hdrScene = MakeLevel(outW, outH); hdrGlass = MakeLevel(outW, outH);
      int w = Math.Max(1, (outW / 2)), h = Math.Max(1, (outH / 2));
      for (int i = 0; i < 5; i++)
        {
          Level l = MakeLevel(w, h);
          if (l.Tex == null) { break; }
          levels.Add(l);
          if ((w < 16) || (h < 16)) { break; }
          w = Math.Max(1, (w / 2)); h = Math.Max(1, (h / 2));
        }
      if (levels.Count > 0) { streakA = MakeLevel(levels[0].W, levels[0].H); streakB = MakeLevel(levels[0].W, levels[0].H); }
      surf = new LayeredSurface(outW, outH);
    }}}
    // }}}

    // IOverlayRenderer {{{
    public void SetScreen(NRect e) {{{
      if (!e.Same(rectE)) { rectE = e; dirty = true; }
      GeomPublish();
    }}}
    public void Resize(NRect o) {{{
      bool sizeChanged = ((o.Width != outW) || (o.Height != outH));
      rectO = o;
      if (sizeChanged && (o.Width >= 8) && (o.Height >= 8) && ready) { outW = o.Width; outH = o.Height; BuildSizeDependent(); }
      else if (!sizeChanged) { Native.SetWindowPos(hwnd, IntPtr.Zero, o.Left, o.Top, 0, 0, (Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE)); }
      GeomPublish();
      dirty = true;
    }}}
    // the glass placement maths (Overlay_P2Glass.cs) needs the current output size / screen rect
    private void GeomPublish() {{{
      FD.gGeomOutW = outW; FD.gGeomOutH = outH;
      FD.gGeomEx = (rectE.Left - rectO.Left); FD.gGeomEy = (rectE.Top - rectO.Top); FD.gGeomEw = rectE.Width; FD.gGeomEh = rectE.Height;
    }}}
    public void SetLook() { dirty = true; }
    public void Render() { dirty = true; }
    // }}}

    // Per UI tick : take the newest captured frame, and redraw when something changed.
    public void Tick() {{{
      if ((!ready) || (cap is null) || (finalTex == null) || (surf is null)) { return; }
      if (cap.Closed) { return; }
      bool freezeFrame = FD.gPower.Animating; // CLAUDE : power animation : keep the last picture, new frames are drained but not shown
      bool got = cap.TryGetFrame((ID3D11Texture2D* src, int w, int h) =>
        {
          if (freezeFrame && (frameTex != null) && (w == frameW) && (h == frameH)) { return; }
          if ((frameTex == null) || (w != frameW) || (h != frameH))
            {
              Rel(frameSrv); Rel(frameTex);
              frameTex = MakeTex(w, h, Format.FormatB8G8R8A8Unorm, (uint) BindFlag.ShaderResource, Usage.Default, 0, null);
              frameSrv = ((frameTex != null) ? MakeSrv(frameTex) : null); frameW = w; frameH = h;
              FD.WL($"[P2] frame texture {w}x{h}\n");
            }
          if (frameTex != null) { ctx->CopyResource((ID3D11Resource*) frameTex, (ID3D11Resource*) src); }
          WaitGpu(); // CLAUDE : the capture surface goes back to Windows right after this callback - the copy must be finished first
        });
      if (got || FD.gPower.Animating) { dirty = true; }
      if (dirty && (frameSrv != null))
        {
          Draw(); dirty = false;
        }
    }}}

    // CLAUDE : blocks until everything queued so far (the CopyResource of the captured frame) has run on the GPU.
    //   Without it the capture surface could be recycled / overwritten by Windows while the copy was still pending -> an occasional black or torn frame.
    private ID3D11Query* evQuery = null;
    private void WaitGpu() {{{
      if (evQuery == null)
        {
          QueryDesc qd = new QueryDesc { Query = Silk.NET.Direct3D11.Query.Event, MiscFlags = 0 };
          ID3D11Query* q = null; dev->CreateQuery(&qd, &q); evQuery = q;
          if (evQuery == null) { ctx->Flush(); return; }
        }
      ctx->End((ID3D11Asynchronous*) evQuery);
      ctx->Flush();
      int flag = 0; int spins = 0;
      while ((ctx->GetData((ID3D11Asynchronous*) evQuery, &flag, 4, 0) != 0) && (spins < 2000)) { spins++; System.Threading.Thread.SpinWait(50); }
    }}}
    // The bezel's detected screen hole (bbox) in output pixels.
    private Vector4 HoleInOutput() {{{
      if ((!BezelModel.Loaded) || (BezelModel.Width <= 0)) { return Vector4.Zero; }
      float s = (outW / (float) BezelModel.Width);
      return new Vector4((BezelModel.detL * s), (BezelModel.detT * s), ((BezelModel.detR - BezelModel.detL) * s), ((BezelModel.detB - BezelModel.detT) * s));
    }}}
    private void SetViewport(int w, int h) {{{
      Viewport vp = new Viewport { TopLeftX = 0, TopLeftY = 0, Width = w, Height = h, MinDepth = 0, MaxDepth = 1 };
      ctx->RSSetViewports(1, &vp);
    }}}
    private void UnbindRt() { ctx->OMSetRenderTargets(0, (ID3D11RenderTargetView**) null, (ID3D11DepthStencilView*) null); }
    private void BindIn(ID3D11ShaderResourceView* srv) {{{
      UnbindRt(); // the previous pass's target is about to become an input - unbind it first
      ID3D11ShaderResourceView* s = srv; ctx->PSSetShaderResources(5, 1, &s);
    }}}
    private void DrawPass(ID3D11PixelShader* ps, ID3D11RenderTargetView* rtv, int w, int h, bool additive) {{{
      ID3D11RenderTargetView* r = rtv; ctx->OMSetRenderTargets(1, &r, (ID3D11DepthStencilView*) null);
      SetViewport(w, h);
      float* bf = stackalloc float[4] { 0, 0, 0, 0 };
      ctx->OMSetBlendState((additive ? blendAdd : null), bf, 0xffffffffu);
      ctx->PSSetShader(ps, (ID3D11ClassInstance**) null, 0);
      ctx->Draw(3, 0);
    }}}
    // binds t0..t7 (any may be null)
    private void Srvs(ID3D11ShaderResourceView* t0, ID3D11ShaderResourceView* t1, ID3D11ShaderResourceView* t2, ID3D11ShaderResourceView* t3, ID3D11ShaderResourceView* t4, ID3D11ShaderResourceView* t6, ID3D11ShaderResourceView* t7) {{{
      UnbindRt();
      ID3D11ShaderResourceView** a = stackalloc ID3D11ShaderResourceView*[8] { t0, t1, t2, t3, t4, null, t6, t7 };
      ctx->PSSetShaderResources(0, 8, a);
    }}}

    private static Vector4 V4(float x, float y, float z, float w) { return new Vector4(x, y, z, w); }

    // CLAUDE : scene-linear value that comes out of exposure + tone map + gamma as exactly the chosen grey (0..128), so the level reads as that grey on screen.
    private static float BehindLinear() {{{
      double target = (FD.BehindGrey() / 255.0);
      if (target <= 0.0) { return 0f; }
      double bl = FD.GPv(GP.BlackLevel); target = ((target * (1.0 - bl)) + bl); // the composite subtracts the black level and rescales, so pre-compensate to keep the chosen grey
      double exposure = Math.Pow(2.0, FD.GPv(GP.ExposureEV)); int tm = (int) FD.GPv(GP.ToneMap);
      double lo = 0.0, hi = 8.0;
      for (int i = 0; i < 40; i++)
        {
          double x = ((lo + hi) * 0.5); double v = (x * exposure);
          if (tm == 1) { v = (v / (1.0 + v)); }
          else if ((tm == 2) || (tm == 3)) { v = (((v * ((2.51 * v) + 0.03)) / ((v * ((2.43 * v) + 0.59)) + 0.14))); }
          v = Math.Pow(Math.Clamp(v, 0.0, 1.0), (1.0 / 2.2));
          if (v < target) { lo = x; } else { hi = x; }
        }
      return (float) ((lo + hi) * 0.5);
    }}}
    // CLAUDE : Look.PictureOffsetX/Y (bezel-image px) and PictureScaleX/Y (%) -> shader units (offset in screen half-sizes, scale as a factor)
    private Vector4 PictureTransform() {{{
      float k = (BezelModel.Loaded && (BezelModel.Width > 0)) ? ((float) rectO.Width / BezelModel.Width) : 1f;
      float hw = Math.Max(1f, (rectE.Width * 0.5f)), hh = Math.Max(1f, (rectE.Height * 0.5f));
      return V4(((FD.gPictureOffsetX * k) / hw), ((FD.gPictureOffsetY * k) / hh), (Math.Max(10, FD.gPictureScaleX) / 100f), (Math.Max(10, FD.gPictureScaleY) / 100f));
    }}}
    private P2Cb BuildConstants(out bool glassOn) {{{
      NRect w = Native.CaptureFrameRect(emuHwnd); NRect c = Native.ClientScreenRect(emuHwnd); // CLAUDE : visible frame bounds (was GetWindowRect : included the invisible border -> 1px sampling offset)
      float fw = Math.Max(1, frameW), fh = Math.Max(1, frameH);
      float sx = (((c.Left - w.Left) + 0f) / fw), sy = (((c.Top - w.Top) + 0f) / fh);
      float sw = (c.Width / fw), sh = (c.Height / fh);
      if (((sx + sw) > 1.01f) || ((sy + sh) > 1.01f) || (sw <= 0f) || (sh <= 0f)) { sx = 0f; sy = 0f; sw = 1f; sh = 1f; } // frame smaller than expected : use it whole
      float ex = (rectE.Left - rectO.Left), ey = (rectE.Top - rectO.Top);
      DimRenderer.ParseTint(FD.gTint, out float tr, out float tg, out float tb);
      float tn = Math.Max(0.001f, Math.Max(tr, Math.Max(tg, tb)));
      int period = ((FD.gScanlineSize > 0) ? Math.Max(2, FD.gScanlineSize) : Math.Max(2, (int) Math.Round((rectE.Height / 240.0))));
      glassOn = (FD.GPon(GP.GlassEnabled) && (coverSrv != null) && (FD.gGlassCover is not null));
      P2Cb d = new P2Cb
      {
        SrcRect = V4(sx, sy, sw, sh),
        ScreenRect = V4(ex, ey, rectE.Width, rectE.Height),
        OutSize = V4(outW, outH, (1f / outW), (1f / outH)),
        Look1 = V4((FD.gBrightness / 100f), (FD.gScanlines ? (FD.gScanlineOpacity / 100f) : 0f), period, (FD.gVignette / 100f)),
        Look2 = V4((tr / tn), (tg / tn), (tb / tn), (FD.gTintOpacity / 100f)),
        Look3 = V4(((FD.gGlowCurvature / 100f) * 0.35f), (FD.gGlowCornerRadius / 100f), (float) ((FD.gGlowRotation / 10.0) * (Math.PI / 180.0)), (FD.GPon(GP.GlassOnTop) ? 1f : 0f)),
        HoleRect = HoleInOutput(),
        Misc = V4(((bezelSrv != null) ? 1f : 0f), (glassOn ? 1f : 0f), 0f, (FD.gBlackBehind ? BehindLinear() : 0f)), // CLAUDE : w = background level (linear)
        Bloom1 = V4(FD.GPv(GP.Bloom), FD.GPv(GP.Threshold), FD.GPv(GP.BloomRadius), FD.GPv(GP.SoftKnee)),
        Post1 = V4(FD.GPv(GP.Glare), FD.GPv(GP.Halation), FD.GPv(GP.HalationWarmth), FD.GPv(GP.ChromaticAberration)),
        Post2 = V4((float) Math.Pow(2.0, FD.GPv(GP.ExposureEV)), FD.GPv(GP.ToneMap), FD.GPv(GP.Dither), FD.GPv(GP.BlackLevel)), // CLAUDE : w = black level
        Env = V4(0.4f, FD.GPv(GP.KeyLight), FD.GPv(GP.FillLight), 0f),
        Pic = PictureTransform() // CLAUDE
      };
      // CRT power animation (Overlay_PowerAnim.cs)
      if (FD.gPower.NeedsOverlay)
        {
          if (FD.gPower.Evaluate(out CrtPowerFrame pf)) { d.PowerA = V4(pf.X, pf.Y, pf.Fill, pf.Content); d.PowerB = V4(pf.Core, FD.gPower.HaloFill, 1f, 0f); }
          else if (!FD.gPower.IsOn) { d.PowerA = V4(0f, 0f, 0f, 0f); d.PowerB = V4(0f, 0f, 1f, 0f); } // steady off : black screen
        }
      if (glassOn)
        {
          GlassCoverData cv = FD.gGlassCover!;
          GlassPlace gp = FD.GlassPlacementCalc();
          float[] tint = FD.cGlassTints[Math.Clamp((int) FD.GPv(GP.GlassType), 0, 5)];
          d.GlassA = V4(FD.GPv(GP.Thickness), FD.GPv(GP.Roughness), FD.GPv(GP.Reflectivity), FD.GPv(GP.FresnelStrength));
          d.GlassTint = V4(tint[0], tint[1], tint[2], FD.GPv(GP.AbsorptionScale));
          d.GlassMisc = V4(FD.GPv(GP.InternalReflection), FD.GPv(GP.Distortion), FD.GPv(GP.Scratches), FD.GPv(GP.Dust));
          d.GlassBorder = V4(gp.BorderX, FD.GPv(GP.BorderBrightness), FD.GPv(GP.BevelHighlight), FD.GPv(GP.GhostFalloff));
          d.GlassBorder2 = V4(FD.GPv(GP.BorderTransparencyExtent), gp.BorderY, (cv.HasRimDepth ? 1f : 0f), FD.GPv(GP.Discolouration));
          d.GlassRim = V4(cv.RimL, cv.RimR, cv.RimT, cv.RimB);
          d.GlassSurround = V4((FD.GPon(GP.SurroundEnabled) ? FD.GPv(GP.SurroundStrength) : 0f), FD.GPv(GP.SurroundBlur), FD.GPv(GP.SurroundReach), (FD.GPon(GP.LightBoxEnabled) ? FD.GPv(GP.LightIntensity) : 0f));
          d.GlassLight = V4(FD.GPv(GP.LightX), FD.GPv(GP.LightY), FD.GPv(GP.LightSize), FD.GPv(GP.LightSoftness));
          d.GlassRectPx = V4(gp.CenterPxX, gp.CenterPxY, Math.Max(1f, gp.CorePxHalfW), Math.Max(1f, gp.CorePxHalfH));
          d.GlassMisc2 = V4(FD.GPv(GP.BorderTransparency), FD.GPv(GP.RimSampleWidth), 1f, 0f);
          d.Misc.Z = (FD.GPon(GP.RimMirror) ? 1f : 0f); // CLAUDE : Misc.z = mirror the edge sample across the bevel
        }
      return d;
    }}}

    private void Draw() {{{
      P2Cb cbData = BuildConstants(out bool glassOn);
      MappedSubresource ms;
      ctx->Map((ID3D11Resource*) cb, 0, Silk.NET.Direct3D11.Map.WriteDiscard, 0, &ms);
      *(P2Cb*) ms.PData = cbData;
      ctx->Unmap((ID3D11Resource*) cb, 0);

      // common state
      ctx->IASetInputLayout((ID3D11InputLayout*) null);
      ctx->IASetPrimitiveTopology((D3DPrimitiveTopology) 4u);
      ctx->VSSetShader(vs, (ID3D11ClassInstance**) null, 0);
      ID3D11Buffer* cbp = cb; ctx->VSSetConstantBuffers(0, 1, &cbp); ctx->PSSetConstantBuffers(0, 1, &cbp);
      ID3D11SamplerState* sp = samp; ctx->PSSetSamplers(0, 1, &sp);

      // 1. HDR scene
      Srvs(frameSrv, null, bezelSrv, null, null, null, null);
      DrawPass(psScene, hdrScene.Rtv, outW, outH, false);

      // 2. bloom chain (+ glare, halation)
      bool bloom = (((cbData.Bloom1.X > 0.001f) || (cbData.Post1.X > 0.001f) || (cbData.Post1.Y > 0.001f)) && (levels.Count > 0));
      bool glare = ((cbData.Post1.X > 0.001f) && (streakA.Tex != null));
      if (bloom)
        {
          Srvs(null, null, null, null, hdrScene.Srv, null, null);
          DrawPass(psPre, levels[0].Rtv, levels[0].W, levels[0].H, false);
          if (glare)
            {
              BindIn(levels[0].Srv); DrawPass(psSt1, streakA.Rtv, streakA.W, streakA.H, false);
              BindIn(streakA.Srv); DrawPass(psSt4, streakB.Rtv, streakB.W, streakB.H, false);
              BindIn(streakB.Srv); DrawPass(psSt16, streakA.Rtv, streakA.W, streakA.H, false);
            }
          for (int i = 1; i < levels.Count; i++) { BindIn(levels[i - 1].Srv); DrawPass(psDown, levels[i].Rtv, levels[i].W, levels[i].H, false); }
          for (int i = (levels.Count - 1); i >= 1; i--) { BindIn(levels[i].Srv); DrawPass(psUp, levels[i - 1].Rtv, levels[i - 1].W, levels[i - 1].H, true); }
          BindIn(null);
        }

      // 3. glass
      ID3D11ShaderResourceView* hdrFinal = hdrScene.Srv;
      if (glassOn)
        {
          Srvs(null, null, null, coverSrv, hdrScene.Srv, null, null);
          DrawPass(psGlass, hdrGlass.Rtv, outW, outH, false);
          hdrFinal = hdrGlass.Srv;
        }

      // 4. composite (post)
      ID3D11ShaderResourceView* halSrv = ((bloom && (levels.Count > 1)) ? levels[Math.Min(2, (levels.Count - 1))].Srv : null);
      ID3D11ShaderResourceView* stSrv = ((bloom && glare) ? streakA.Srv : null);
      Srvs(null, (bloom ? levels[0].Srv : null), bezelSrv, null, hdrFinal, halSrv, stSrv);
      DrawPass(psComp, finalRtv, outW, outH, false);
      UnbindRt();
      ID3D11ShaderResourceView** none = stackalloc ID3D11ShaderResourceView*[8] { null, null, null, null, null, null, null, null };
      ctx->PSSetShaderResources(0, 8, none);

      // readback -> layered window
      ctx->CopyResource((ID3D11Resource*) stagingTex, (ID3D11Resource*) finalTex);
      MappedSubresource rd;
      if (ctx->Map((ID3D11Resource*) stagingTex, 0, Silk.NET.Direct3D11.Map.Read, 0, &rd) >= 0)
        {
          byte* src = (byte*) rd.PData; byte* dst = (byte*) surf!.Bits; int rowBytes = (outW * 4);
          for (int y = 0; y < outH; y++) { System.Buffer.MemoryCopy((src + ((long) y * rd.RowPitch)), (dst + ((long) y * rowBytes)), rowBytes, rowBytes); }
          ctx->Unmap((ID3D11Resource*) stagingTex, 0);
          surf.Present(hwnd, rectO.Left, rectO.Top);
          if (++frames == 1) { FD.WL($"[P2] first frame presented {outW}x{outH} glass={glassOn} hole={HoleInOutput()}\n"); }
        }
    }}}

    public void Dispose() {{{
      ready = false;
      cap?.Dispose(); cap = null;
      FreeSizeDependent();
      Rel(frameSrv); Rel(frameTex); Rel(bezelSrv); Rel(bezelTex); Rel(coverSrv); Rel(coverTex);
      Rel(evQuery); evQuery = null;
      Rel(cb); Rel(blendAdd); Rel(samp); Rel(psSt16); Rel(psSt4); Rel(psSt1); Rel(psComp); Rel(psGlass); Rel(psUp); Rel(psDown); Rel(psPre); Rel(psScene); Rel(vs);
      if (ctx != null) { ctx->ClearState(); }
      Rel(ctx); Rel(dev);
      frameSrv = null; frameTex = null; ctx = null; dev = null;
      FD.gGlassCover = null;
    }}}
  }
}
