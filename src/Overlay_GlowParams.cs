// CLAUDE : new file. GPU-renderer parameters carried over from P:/SRC/C#/TRS80 (VfdControlParams.cs / VfdConfig.cs) : the GLASS, SCENE
// (reflection lighting) and POST groups, same names, ranges and steps. Every parameter is one float in gF, one ini key ([Glass]/[Scene]/[Post]
// section, profile scope) and one UI control - the UI builds itself from this table (init message "params").
// NaN default = "auto" (computed per bezel, see GlassPlacement) ; the UI then shows the effective value.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace FastDog
{
  public enum GP
  {
    // GLASS
    GlassEnabled, GlassOnTop, GlassType, Thickness, Roughness, Reflectivity, FresnelStrength, AbsorptionScale, InternalReflection, Distortion,
    Scratches, Dust, Discolouration, BorderWidth, BorderHeight, BorderBrightness, BevelHighlight, GhostFalloff, RimSampleWidth, RimMirror, // CLAUDE
    BorderTransparency, BorderTransparencyExtent, ScaleX, ScaleY, OffsetX, OffsetY,
    // SCENE
    KeyLight, FillLight, SurroundEnabled, SurroundStrength, SurroundBlur, SurroundReach,
    LightBoxEnabled, LightX, LightY, LightSize, LightSoftness, LightIntensity,
    // POST
    ExposureEV, ToneMap, Bloom, BloomRadius, Threshold, SoftKnee, Glare, Halation, HalationWarmth, ChromaticAberration, Dither, BlackLevel, // CLAUDE
    Count
  }

  public sealed class GParam
  {
    public GP Id; public string Group = ""; public string Section = ""; public string Key = ""; public string Label = "";
    public float Def, Min, Max, Step; public string Kind = "slider"; public string[]? Options = null;
    public GParam(GP id, string group, string section, string key, string label, float def, float min, float max, float step, string kind = "slider", string[]? options = null)
    { Id = id; Group = group; Section = section; Key = key; Label = label; Def = def; Min = min; Max = max; Step = step; Kind = kind; Options = options; }
  }

  public partial class FD
  {
    public static readonly string[] cGlassTypes = { "Clear", "Smoked", "Green", "Blue", "Amber", "Custom" };
    public static readonly string[] cToneMaps = { "None (clip)", "Reinhard", "ACES", "ACES (hue preserving)" };
    // Glass tint presets (VfdConfig.GlassTintFor)
    public static readonly float[][] cGlassTints = { new[] { 0.96f, 0.97f, 0.95f }, new[] { 0.55f, 0.55f, 0.58f }, new[] { 0.50f, 0.85f, 0.55f }, new[] { 0.50f, 0.60f, 0.90f }, new[] { 0.90f, 0.65f, 0.35f }, new[] { 0.96f, 0.97f, 0.95f } };

    public static readonly GParam[] gGP = new GParam[]
    {
      new GParam(GP.GlassEnabled, "glass", "Glass", "Enabled", "Glass enabled", 0f, 0f, 1f, 1f, "toggle"),
      new GParam(GP.GlassOnTop, "glass", "Glass", "OnTop", "Glass on top of the bezel", 1f, 0f, 1f, 1f, "toggle"),
      new GParam(GP.GlassType, "glass", "Glass", "Type", "Glass type", 0f, 0f, 5f, 1f, "select", cGlassTypes),
      new GParam(GP.Thickness, "glass", "Glass", "Thickness", "Glass thickness", 2.4f, 0.1f, 8f, 0.01f),
      new GParam(GP.Roughness, "glass", "Glass", "Roughness", "Glass roughness", 0.15f, 0f, 1f, 0.001f),
      new GParam(GP.Reflectivity, "glass", "Glass", "Reflectivity", "Glass reflectivity", 0.6f, 0f, 2f, 0.001f),
      new GParam(GP.FresnelStrength, "glass", "Glass", "FresnelStrength", "Fresnel strength", 1.0f, 0f, 2f, 0.001f),
      new GParam(GP.AbsorptionScale, "glass", "Glass", "AbsorptionScale", "Absorption scale", 1.0f, 0f, 3f, 0.001f),
      new GParam(GP.InternalReflection, "glass", "Glass", "InternalReflection", "Internal reflection", 0.25f, 0f, 1f, 0.001f),
      new GParam(GP.Distortion, "glass", "Glass", "Distortion", "Glass distortion", 0.15f, 0f, 1f, 0.001f),
      new GParam(GP.Scratches, "glass", "Glass", "Scratches", "Scratches", 0f, 0f, 1f, 0.001f),
      new GParam(GP.Dust, "glass", "Glass", "Dust", "Dust", 0f, 0f, 1f, 0.001f),
      new GParam(GP.Discolouration, "glass", "Glass", "Discolouration", "Discolouration", 0f, 0f, 1f, 0.001f),
      new GParam(GP.BorderWidth, "glass", "Glass", "BorderWidth", "Glass border width", 1f, 0f, 3f, 0.001f),
      new GParam(GP.BorderHeight, "glass", "Glass", "BorderHeight", "Glass border height", 1f, 0f, 3f, 0.001f),
      new GParam(GP.BorderBrightness, "glass", "Glass", "BorderBrightness", "Border brightness", 1f, 0f, 3f, 0.001f),
      new GParam(GP.BevelHighlight, "glass", "Glass", "BevelHighlight", "Bevel highlight", 0.85f, 0f, 1f, 0.001f),
      new GParam(GP.GhostFalloff, "glass", "Glass", "GhostFalloff", "Reflection trail-off", 1f, 0f, 4f, 0.001f),
      new GParam(GP.RimSampleWidth, "glass", "Glass", "RimSampleWidth", "Edge sample width", 0.08f, 0f, 0.5f, 0.001f), // CLAUDE : max 0.3 -> 0.5
      new GParam(GP.RimMirror, "glass", "Glass", "RimMirror", "Mirror edge sample across the bevel", 0f, 0f, 1f, 1f, "toggle"), // CLAUDE
      new GParam(GP.BorderTransparency, "glass", "Glass", "BorderTransparency", "Border transparency", 0f, 0f, 1f, 0.001f),
      new GParam(GP.BorderTransparencyExtent, "glass", "Glass", "BorderTransparencyExtent", "Border transparency extent", 1f, 0f, 1f, 0.001f),
      new GParam(GP.ScaleX, "glass", "Glass", "ScaleX", "Glass X scale (auto = fit the screen)", float.NaN, 0.1f, 2.5f, 0.001f),
      new GParam(GP.ScaleY, "glass", "Glass", "ScaleY", "Glass Y scale (auto = fit the screen)", float.NaN, 0.1f, 2.5f, 0.001f),
      new GParam(GP.OffsetX, "glass", "Glass", "OffsetX", "Glass X offset (auto = centred on the screen)", float.NaN, -1f, 1f, 0.001f),
      new GParam(GP.OffsetY, "glass", "Glass", "OffsetY", "Glass Y offset (auto = centred on the screen)", float.NaN, -1f, 1f, 0.001f),

      new GParam(GP.KeyLight, "scene", "Scene", "KeyLight", "Key light", 0.8f, 0f, 2f, 0.001f),
      new GParam(GP.FillLight, "scene", "Scene", "FillLight", "Fill light", 0.35f, 0f, 2f, 0.001f),
      new GParam(GP.SurroundEnabled, "scene", "Scene", "SurroundEnabled", "Surround reflection", 0f, 0f, 1f, 1f, "toggle"),
      new GParam(GP.SurroundStrength, "scene", "Scene", "SurroundStrength", "Surround strength", 0.35f, 0f, 2f, 0.001f),
      new GParam(GP.SurroundBlur, "scene", "Scene", "SurroundBlur", "Surround blur", 0.15f, 0f, 0.5f, 0.001f),
      new GParam(GP.SurroundReach, "scene", "Scene", "SurroundReach", "Surround reach", 0.4f, 0.02f, 2f, 0.001f),
      new GParam(GP.LightBoxEnabled, "scene", "Scene", "LightBoxEnabled", "Light box", 0f, 0f, 1f, 1f, "toggle"),
      new GParam(GP.LightX, "scene", "Scene", "LightX", "Light X", -0.5f, -1f, 1f, 0.001f),
      new GParam(GP.LightY, "scene", "Scene", "LightY", "Light Y", 0.6f, -1f, 1f, 0.001f),
      new GParam(GP.LightSize, "scene", "Scene", "LightSize", "Light size", 0.35f, 0.02f, 1f, 0.001f),
      new GParam(GP.LightSoftness, "scene", "Scene", "LightSoftness", "Light softness", 0.5f, 0f, 2f, 0.001f),
      new GParam(GP.LightIntensity, "scene", "Scene", "LightIntensity", "Light intensity", 1f, 0f, 4f, 0.001f),

      new GParam(GP.ExposureEV, "post", "Post", "ExposureEV", "Exposure EV", 0f, -4f, 4f, 0.01f),
      new GParam(GP.ToneMap, "post", "Post", "ToneMap", "Tone map", 2f, 0f, 3f, 1f, "select", cToneMaps),
      new GParam(GP.Bloom, "post", "Post", "Bloom", "Bloom", 0.35f, 0f, 2f, 0.001f),
      new GParam(GP.BloomRadius, "post", "Post", "BloomRadius", "Bloom radius", 1f, 0.5f, 3f, 0.001f),
      new GParam(GP.Threshold, "post", "Post", "Threshold", "Threshold", 0.6f, 0f, 4f, 0.001f),
      new GParam(GP.SoftKnee, "post", "Post", "SoftKnee", "Soft knee", 0.5f, 0f, 1f, 0.001f),
      new GParam(GP.Glare, "post", "Post", "Glare", "Glare", 0.2f, 0f, 2f, 0.001f),
      new GParam(GP.Halation, "post", "Post", "Halation", "Halation", 0.15f, 0f, 2f, 0.001f),
      new GParam(GP.HalationWarmth, "post", "Post", "HalationWarmth", "Halation warmth", 0.5f, 0f, 1f, 0.001f),
      new GParam(GP.ChromaticAberration, "post", "Post", "ChromaticAberration", "Chromatic aberration", 0.15f, 0f, 2f, 0.001f),
      new GParam(GP.Dither, "post", "Post", "Dither", "Dither", 0.5f, 0f, 2f, 0.001f),
      new GParam(GP.BlackLevel, "post", "Post", "BlackLevel", "Black level (subtracts glass / exposure haze from the darks)", 0f, 0f, 0.3f, 0.001f), // CLAUDE
    };

    public static float[] gF = new float[(int) GP.Count];
    public static float GPv(GP p) { return gF[(int) p]; }
    public static bool GPon(GP p) { return (gF[(int) p] >= 0.5f); }

    private static string GFmt(float v) { return (float.IsNaN(v) ? "" : v.ToString("0.####", CultureInfo.InvariantCulture)); }
    // One SettingEntry per parameter (called from the static constructor, before defaults are captured).
    private static List<SettingEntry> GlowSettingEntries() {{{
      List<SettingEntry> list = new List<SettingEntry>();
      for (int i = 0; i < gGP.Length; i++) { gF[(int) gGP[i].Id] = gGP[i].Def; }
      foreach (GParam p in gGP)
        {
          GParam q = p;
          list.Add(new SettingEntry
          {
            Section = q.Section, Key = q.Key,
            Get = () => GFmt(gF[(int) q.Id]),
            Ui = () => GFmt(GlowEffective(q.Id)),
            Set = (v) =>
            {
              if (string.IsNullOrWhiteSpace(v)) { gF[(int) q.Id] = q.Def; return; }
              if (float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) { gF[(int) q.Id] = Math.Clamp(f, q.Min, q.Max); }
            }
          });
        }
      return list;
    }}}
    // The parameter table for the UI (sent once in the init message).
    public static object[] GlowParamSpecs() {{{
      List<object> list = new List<object>();
      foreach (GParam p in gGP)
        { list.Add(new { id = (p.Section + "." + p.Key), group = p.Group, label = p.Label, min = p.Min, max = p.Max, step = p.Step, kind = p.Kind, options = p.Options }); }
      return list.ToArray();
    }}}
  }
}
