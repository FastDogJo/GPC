// CLAUDE : new file. PHASE 2 wiring : the presenter window (click-through, above the bezel) + GlowRenderer lifetime.
// Enabled by [Glow] Enabled=1. While active the Phase 1 dim window is hidden (the shader does brightness/scanlines/vignette/tint itself).
using System;
using System.Windows.Forms;

namespace FastDog
{
  // Click-through layered window covering the bezel window; shows the GPU-processed picture, glow and glass.
  public sealed class PresenterForm : OverlayFormBase
  {
    protected override int ExtraExStyle { get { return Native.WS_EX_TRANSPARENT; } }
    protected override void WndProc(ref Message m) {{{
      if (m.Msg == Native.WM_NCHITTEST) { m.Result = (IntPtr) Native.HTTRANSPARENT; return; }
      if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr) 3; return; }
      base.WndProc(ref m);
    }}}
  }

  public partial class FD
  {
    // FIELDS & PROPERTIES {{{
    // [Glow]  (the GLASS / SCENE / POST parameters live in Overlay_GlowParams.cs)
    public static bool gGlowEnabled = false;
    public static int gGlowCurvature = 0;      // 0..100
    public static int gGlowRotation = 0;       // tenths of a degree
    public static int gGlowCornerRadius = 0;   // % of the shorter screen side
    public static PresenterForm? gPresenterForm = null;
    public static GlowRenderer? gGlow = null;
    public static bool GlowActive { get { return ((gGlow is not null) && gGlow.Ready); } }
    // }}}

    // Start/stop to match gGlowEnabled and the attach state.
    public static void GlowApply() {{{
      bool want = (gGlowEnabled && (gEmuHwnd != IntPtr.Zero) && (!gQuitting));
      if (want && (gGlow is null))
        {
          if (gPresenterForm is null) { gPresenterForm = new PresenterForm(); IntPtr h = gPresenterForm.Handle; }
          GlowRenderer g = new GlowRenderer(gPresenterForm.Handle, gEmuHwnd);
          if (g.Init()) { gGlow = g; }
          else { g.Dispose(); gGlowEnabled = false; WL("ERROR : [P2] could not start - enhanced rendering disabled\n"); SettingsPushValue("Glow.Enabled"); }
          if (gGlow is not null) { TrackForceLayout(); }
        }
      else if ((!want) && (gGlow is not null)) { GlowStop(); }
      TrackApplyVisibility();
    }}}
    public static void GlowStop() {{{
      GlowRenderer? g = gGlow;
      gGlow = null;
      g?.Dispose();
      if (gPresenterForm is not null) { Native.ShowWindow(gPresenterForm.Handle, Native.SW_HIDE); }
      gShownPresenter = false;
      TrackApplyVisibility();
    }}}
    // Called by TrackLayout : presenter covers the overlay rect o, the emulator client rect is e.
    public static void GlowLayout(NRect o, NRect e) {{{
      if (gGlow is null) { return; }
      gGlow.SetScreen(e);
      gGlow.Resize(o);
    }}}
    public static void GlowLookChanged(string key) {{{
      if (gGlow is null) { return; }
      gGlow.SetLook();
      // auto glass scale/offset follow the border widths / screen : refresh what the UI shows
      foreach (string k in new string[] { "Glass.ScaleX", "Glass.ScaleY", "Glass.OffsetX", "Glass.OffsetY" }) { SettingsPushValue(k); }
    }}}
    public static void GlowBezelChanged() {{{
      if (gGlow is null) { return; }
      gGlow.UploadBezel();
      gGlow.UploadGlass(); // glass image follows the bezel model
    }}}
  }
}
