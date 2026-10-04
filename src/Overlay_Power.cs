// CLAUDE : new file. Power rect (ported from P:/SRC/C#/TRS80 ScreenVfdPowerRect.cs + MainForm power-rect handling).
// A small rect over the bezel's power button, in % of the bezel window (top-left origin).
//   Shift + double-click            : quit the application (implemented)
//   double-click                    : CRT power animation + emulator pause / cold restart
//   single click (after DoubleClickTime, no double-click followed) : CRT power animation only
// PowerLine / PowerDot / PowerFade (ms + brightness) and Halo drive the animation (Overlay_PowerAnim.cs).
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace FastDog
{
  public partial class FD
  {
    private static System.Windows.Forms.Timer? gPowerClickTimer = null;

    // CLAUDE : the power rect comes only from the preset / UI (X, Y, W, H in %) ; blank = 0 = no power button
    public static RectangleF PowerRectEffective() {{{
      return new RectangleF((gPowerRectX ?? 0f), (gPowerRectY ?? 0f), (gPowerRectW ?? 0f), (gPowerRectH ?? 0f));
    }}}
    // The UI shows the effective power-rect values (0 when blank).
    public static string SettingsUiValue(SettingEntry e) {{{
      if (e.Section == "Power")
        {
          RectangleF r = PowerRectEffective();
          switch (e.Key)
            {
              case "RectX": return r.X.ToString("0.0", CultureInfo.InvariantCulture);
              case "RectY": return r.Y.ToString("0.0", CultureInfo.InvariantCulture);
              case "RectW": return r.Width.ToString("0.0", CultureInfo.InvariantCulture);
              case "RectH": return r.Height.ToString("0.0", CultureInfo.InvariantCulture);
            }
        }
      return ((e.Ui is not null) ? e.Ui() : e.Get());
    }}}
    public static void PowerRectPushValues() {{{
      foreach (string k in new string[] { "RectX", "RectY", "RectW", "RectH" }) { SettingsPushValue("Power." + k); }
    }}}
    public static void PowerRectReset() {{{
      gPowerRectX = gPowerRectY = gPowerRectW = gPowerRectH = null;
      PowerRectPushValues();
      gBezelRenderer?.Render();
      SettingsSaveSoon();
    }}}
    // Screen point inside the power rect of the bezel window?
    public static bool PowerHit(IntPtr bezelHwnd, int x, int y) {{{
      RectangleF pr = PowerRectEffective();
      if ((pr.Width <= 0f) || (pr.Height <= 0f)) { return false; }
      NRect r; Native.GetWindowRect(bezelHwnd, out r);
      if ((r.Width <= 0) || (r.Height <= 0)) { return false; }
      float px = (((x - r.Left) * 100f) / r.Width); float py = (((y - r.Top) * 100f) / r.Height);
      return ((px >= pr.Left) && (px <= pr.Right) && (py >= pr.Top) && (py <= pr.Bottom));
    }}}
    // Bezel left-button down. Returns true when it was consumed by the power rect.
    public static bool PowerMouseDown(IntPtr bezelHwnd, int clicks, bool shift) {{{
      Point p = Control.MousePosition;
      if (!PowerHit(bezelHwnd, p.X, p.Y)) { return false; }
      if (clicks == 1)
        {
          if (!shift)
            {
              if (gPowerClickTimer is null)
                {
                  gPowerClickTimer = new System.Windows.Forms.Timer();
                  gPowerClickTimer.Tick += (s, e) => { gPowerClickTimer!.Stop(); PowerAnimate(); };
                }
              gPowerClickTimer.Interval = SystemInformation.DoubleClickTime; // wait out a possible double-click
              gPowerClickTimer.Start();
            }
        }
      else
        {
          gPowerClickTimer?.Stop();
          if (shift) { WL("[Power] Shift+double-click : quit\n"); gBezelForm?.BeginInvoke(new Action(AppQuit)); }
          else { PowerToggle(); }
        }
      return true;
    }}}
    // single click : the CRT power animation only (Overlay_PowerAnim.cs)
    public static void PowerAnimate() {{{ PowerVisualToggle(); }}}
    // double-click : animation + the emulator is paused (off) / cold restarted (on)
    public static void PowerToggle() {{{ PowerFullToggle(); }}}
  }
}
