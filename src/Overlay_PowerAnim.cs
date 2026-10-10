// CLAUDE : new file. CRT power off/on animation, ported from P:/SRC/C#/TRS80 DirectX/CrtPower.cs.
// OFF timeline : raster collapses vertically into a horizontal line (glow rises to LineBright), the line collapses horizontally into a dot
// (glow LineBright -> DotBright), the dot fades out. ON plays the same timeline backwards. GPU mode squashes the real picture (PSScene);
// the non-GPU overlay (DimRenderer) can only fade the raster and draw the glowing line / dot over black.
// Power click  (single, after the double-click delay) : animation only.   Power double-click : animation + pause / cold restart of the emulator.
using System;
using System.Diagnostics;

namespace FastDog
{
  public readonly record struct CrtPowerFrame(float X, float Y, float Fill, float Content, float Core);

  public sealed class CrtPower
  {
    public float LineSeconds = 0.15f, DotSeconds = 0.20f, FadeSeconds = 0.35f;
    public float LineFill = 0.8f, DotFill = 1.0f, HaloFill = 0.3f;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double start = 0.0;
    private bool animating = false;
    public bool IsOn { get; private set; } = true;   // target state
    public bool Animating { get { return animating; } }
    public bool NeedsOverlay { get { return ((!IsOn) || animating); } } // anything other than the plain powered-on picture
    private float Total { get { return (Math.Max(0f, LineSeconds) + Math.Max(0f, DotSeconds) + Math.Max(0f, FadeSeconds)); } }

    // the live settings (Power section) -> timing / brightness
    public void LoadSettings() {{{
      LineSeconds = (FD.gPowerLineMs / 1000f); DotSeconds = (FD.gPowerDotMs / 1000f); FadeSeconds = (FD.gPowerFadeMs / 1000f);
      LineFill = (FD.gPowerLineBright / 100f); DotFill = (FD.gPowerDotBright / 100f); HaloFill = (FD.gPowerHalo / 100f);
    }}}
    public void SetOff() {{{ IsOn = false; animating = false; }}} // CLAUDE : steady off, no animation
    public void Toggle() {{{
      LoadSettings();
      double now = clock.Elapsed.TotalSeconds;
      float total = Total;
      double t = animating ? Math.Min((now - start), total) : total; // mid-sequence : continue from the mirrored point
      start = (now - (total - t));
      IsOn = (!IsOn);
      animating = (total > 0f);
    }}}
    // true while a sequence is running (frame valid)
    public bool Evaluate(out CrtPowerFrame frame) {{{
      frame = new CrtPowerFrame(1f, 1f, 0f, 1f, 0f);
      if (!animating) { return false; }
      float lineS = Math.Max(0f, LineSeconds), dotS = Math.Max(0f, DotSeconds), fadeS = Math.Max(0f, FadeSeconds);
      float total = (lineS + dotS + fadeS);
      float t = (float) (clock.Elapsed.TotalSeconds - start);
      if (t >= total) { animating = false; return false; }
      float u = (IsOn ? (total - t) : t);
      if (u < lineS) { float p = Ease(u / lineS); frame = new CrtPowerFrame(1f, (1f - p), (LineFill * p), (1f - p), 0f); }
      else if (u < (lineS + dotS)) { float p = Ease((u - lineS) / dotS); frame = new CrtPowerFrame((1f - p), 0f, (LineFill + ((DotFill - LineFill) * p)), 0f, p); }
      else { float p = ((u - lineS - dotS) / fadeS); frame = new CrtPowerFrame(0f, 0f, (DotFill * (1f - p)), 0f, (1f - p)); }
      return true;
    }}}
    private static float Ease(float p) { return (p * p); }
  }

  public partial class FD
  {
    public static readonly CrtPower gPower = new CrtPower();
    private static bool gPowerWasActive = false;
    private static bool gEmuPausedByPower = false;
    private static long gMuteRetryMs = 0; // CLAUDE

    // called every UI tick : keeps the non-GPU overlay redrawing while the animation runs / the screen is "off"
    public static void PowerTick() {{{
      bool need = gPower.NeedsOverlay;
      if ((!need) && (!gPowerWasActive)) { return; }
      if (gMutedByPower && ((Environment.TickCount64 - gMuteRetryMs) > 500)) { gMuteRetryMs = Environment.TickCount64; EmuAudioMute(true); } // CLAUDE : keep muting until the sessions show up
      if (!GlowActive) { gDimRenderer?.SetLook(); }
      if ((!gPower.Animating) && (!GlowActive)) { gDimRenderer?.FreezeEnd(); } // CLAUDE
      if (need != gPowerWasActive) { gPowerWasActive = need; TrackApplyVisibility(); if (!need) { gDimRenderer?.SetLook(); if (gMutedByPower) { EmuAudioMute(false); } } } // CLAUDE : unmute once the power-on animation is done
    }}}
    // CLAUDE : screen off immediately (no animation) : Launch.StartMonitorOff
    public static void PowerSetOffNow() {{{
      if (!gPower.IsOn) { return; }
      gPower.SetOff();
      EmuAudioMute(true); // CLAUDE
      gGlow?.SetLook();
      WL("[Power] screen off (start)\n");
    }}}
    // CLAUDE : MCP : the power rect click. double = animation + emulator pause / cold restart, otherwise the single-click animation only.
    public static void PowerClick(bool dbl) {{{
      if (dbl) { PowerToggle(); } else { PowerAnimate(); }
    }}}
    // CLAUDE : MCP : drive the screen to on / off (no-op when already there)
    public static void PowerSet(bool on, bool full) {{{
      if (gPower.IsOn == on) { return; }
      PowerClick(full);
    }}}
    // power rect : single click
    public static void PowerVisualToggle() {{{
      if ((!gPower.Animating) && (!GlowActive)) { gDimRenderer?.FreezeBegin(); } // CLAUDE : plain overlay : snapshot the picture first (GPU mode just stops taking frames)
      gPower.Toggle();
      if (!gPower.IsOn) { EmuAudioMute(true); } // CLAUDE : muted from power-off until the power-on animation finishes (PowerTick)
      gGlow?.SetLook();
      WL($"[Power] screen {(gPower.IsOn ? "on" : "off")}\n");
    }}}
    // power rect : double-click = animation + the emulator follows (off : pause, on : cold restart)
    public static void PowerFullToggle() {{{
      PowerVisualToggle();
      if (!gPower.IsOn)
        {
          if ((!gEmuPausedByPower) && EmuMenuCommand("Pause/Resume")) { gEmuPausedByPower = true; }
        }
      else
        {
          if (gEmuPausedByPower) { EmuMenuCommand("Pause/Resume"); gEmuPausedByPower = false; }
          EmuMenuCommand("Cold Restart");
        }
    }}}
  }
}
