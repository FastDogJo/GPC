// CLAUDE : new file. Hotkeys on a hidden message-only window ("Ctrl+Alt+F" strings from the ini). All are registered only while this instance has the focus (see HotkeyContextUpdate).
using System;
using System.Windows.Forms;

namespace FastDog
{
  public sealed class HotkeyWindow : NativeWindow
  {
    public HotkeyWindow() { CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); } // HWND_MESSAGE
    protected override void WndProc(ref Message m) {{{
      if (m.Msg == Native.WM_HOTKEY) { FD.HotkeyFired((int) m.WParam.ToInt64()); return; }
      base.WndProc(ref m);
    }}}
  }

  public partial class FD
  {
    private static HotkeyWindow? gHotkeyWindow = null;
    private const int cHkFrame = 1, cHkOverlay = 2, cHkSettings = 3;
    private const int cHkBezelDown = 4, cHkBezelUp = 5, cHkZoomIn = 6, cHkZoomOut = 7, cHkScanlines = 8; // CLAUDE : context hotkeys
    private static bool gHkContextOn = false;
    private const int cHkMenuBase = 100; // CLAUDE : Alt+<mnemonic> of the stripped emulator menu : id = cHkMenuBase + menu position

    public static bool HotkeyParse(string s, out uint mods, out uint vk) {{{
      mods = 0; vk = 0;
      if (string.IsNullOrWhiteSpace(s)) { return false; }
      foreach (string part in s.Split('+'))
        {
          string t = part.Trim();
          switch (t.ToUpperInvariant())
            {
              case "ALT": mods |= 1; break;
              case "CTRL": case "CONTROL": mods |= 2; break;
              case "SHIFT": mods |= 4; break;
              case "WIN": mods |= 8; break;
              default:
                if ((vk != 0) || (t.Length == 0)) { return false; }
                if ((t.Length == 1) && char.IsDigit(t[0])) { vk = (uint) (Keys.D0 + (t[0] - '0')); }
                else if (Enum.TryParse<Keys>(t, true, out Keys k)) { vk = (uint) k; }
                else { return false; }
                break;
            }
        }
      return ((vk != 0) && (mods != 0));
    }}}
    private static bool HotkeyRegisterOne(int id, string spec, bool logConflict = true) {{{ // CLAUDE : logConflict
      Native.UnregisterHotKey(gHotkeyWindow!.Handle, id);
      if (!HotkeyParse(spec, out uint mods, out uint vk)) { WL($"WARNING : [Hotkey] invalid \"{spec}\"\n"); return false; }
      if (!Native.RegisterHotKey(gHotkeyWindow.Handle, id, (mods | 0x4000), vk)) { if (logConflict) { WL($"WARNING : [Hotkey] \"{spec}\" is already in use by another app (conflict)\n"); } return false; }
      return true;
    }}}
    // CLAUDE : ALL hotkeys (toggle frame / overlay, settings, look) only exist while this instance's emulator / bezel / presenter / settings window has the focus,
    //   so several GPC instances can use the same keys and Ctrl+1 etc. are not stolen from other apps.
    private static string HotkeySpec(int id) {{{
      switch (id) { case cHkFrame: return gHotkeyToggleFrame; case cHkOverlay: return gHotkeyToggleOverlay; case cHkSettings: return gHotkeyShowSettings; case cHkBezelDown: return gHotkeyBezelDown; case cHkBezelUp: return gHotkeyBezelUp; case cHkZoomIn: return gHotkeyZoomIn; case cHkZoomOut: return gHotkeyZoomOut; case cHkScanlines: return gHotkeyScanlines; }
      return "";
    }}}
    // CLAUDE : the foreground event reaches every GPC instance at about the same time, so the new foreground instance can try to register before the old one has let go.
    //   A failed registration is retried on the next poll (gHkContextOn stays as it was) up to cHkRetryMax times, quietly ; the last try logs the conflict.
    private const int cHkRetryMax = 10;
    private static int gHkRetry = 0;
    public static void HotkeyContextUpdate() {{{
      if (gHotkeyWindow is null) { return; }
      IntPtr fg = Native.GetForegroundWindow();
      bool inCtx = (((gEmuHwnd != IntPtr.Zero) && ((fg == gEmuHwnd) || ((gBezelForm is not null) && (fg == gBezelForm.Handle)) || ((gPresenterForm is not null) && (fg == gPresenterForm.Handle))))
        || ((gSettingsForm is not null) && gSettingsForm.IsHandleCreated && (fg == gSettingsForm.Handle)));
      if (inCtx == gHkContextOn) { return; }
      bool failed = false;
      for (int id = cHkFrame; id <= cHkScanlines; id++)
        {
          Native.UnregisterHotKey(gHotkeyWindow.Handle, id);
          if (inCtx && (!string.IsNullOrWhiteSpace(HotkeySpec(id)))) { if (!HotkeyRegisterOne(id, HotkeySpec(id), (gHkRetry >= cHkRetryMax))) { failed = true; } }
        }
      if (inCtx && failed && (gHkRetry < cHkRetryMax)) { gHkRetry++; return; }
      gHkRetry = 0;
      gHkContextOn = inCtx;
      // Alt+<mnemonic> of the emulator's menu while its menu bar is stripped (a conflict with one of the look hotkeys above just stays unregistered)
      for (int pos = 0; pos < 32; pos++) { Native.UnregisterHotKey(gHotkeyWindow.Handle, (cHkMenuBase + pos)); }
      if (inCtx && gFrameStripped)
        {
          System.Collections.Generic.HashSet<char> done = new System.Collections.Generic.HashSet<char>();
          foreach ((int Pos, char Key, string Text) m in gMenuMnemonics)
            {
              if (!done.Add(m.Key)) { continue; } // same letter on several menus : one registration, pressing it again cycles (see HotkeyFired)
              if (!Native.RegisterHotKey(gHotkeyWindow.Handle, (cHkMenuBase + m.Pos), (0x1 | 0x4000), (uint) m.Key)) { WL($"WARNING : [Hotkey] Alt+{m.Key} ({m.Text} menu) is taken by another app or look hotkey\n"); }
            }
        }
    }}}
    // the stripped / restored frame changed : redo the context registrations
    public static void HotkeyContextRefresh() {{{ gHkContextOn = false; HotkeyContextUpdate(); }}}
    public static void HotkeyRegisterAll() {{{ // CLAUDE : void (was bool, nobody used it) ; every hotkey is a context hotkey now
      if (gHotkeyWindow is null) { gHotkeyWindow = new HotkeyWindow(); }
      gHkContextOn = false; gHkRetry = 0; HotkeyContextUpdate(); // re-apply the hotkeys with the new strings
    }}}
    public static void HotkeyShutdown() {{{
      if (gHotkeyWindow is null) { return; }
      for (int id = 1; id <= cHkScanlines; id++) { Native.UnregisterHotKey(gHotkeyWindow.Handle, id); }
      for (int pos = 0; pos < 32; pos++) { Native.UnregisterHotKey(gHotkeyWindow.Handle, (cHkMenuBase + pos)); }
      gHotkeyWindow.DestroyHandle();
      gHotkeyWindow = null;
    }}}
    // Ctrl+1 / Ctrl+2 (and the mouse wheel : BrightnessWheel) - same 5 % bezel brightness steps as the TRS80 project
    public static void BezelBrightnessStep(int delta) {{{
      int v = Math.Clamp((gBezelBrightness + delta), 0, 100);
      SettingsSetById("Look.BezelBrightness", v.ToString()); SettingsPushValue("Look.BezelBrightness");
    }}}
    public static void BrightnessWheel(int wheelDelta) {{{
      int v = Math.Clamp((gBrightness + ((wheelDelta > 0) ? 2 : -2)), 0, 200);
      SettingsSetById("Look.Brightness", v.ToString()); SettingsPushValue("Look.Brightness");
    }}}
    // CLAUDE : Ctrl + wheel = background level behind the scaled-down screen (turns the fill on if it was off)
    public static int BehindGrey() {{{ return ((Math.Clamp(gBehindLevel, 0, 100) * 64) / 100); }}} // level 0..100 -> grey 0..64 (capped)
    public static void BehindWheel(int wheelDelta) {{{
      int v = Math.Clamp((gBehindLevel + ((wheelDelta > 0) ? 1 : -1)), 0, 100);
      if (!gBlackBehind) { SettingsSetById("Look.BlackBehind", "1"); SettingsPushValue("Look.BlackBehind"); }
      SettingsSetById("Look.BehindLevel", v.ToString()); SettingsPushValue("Look.BehindLevel");
    }}}
    public static void HotkeyFired(int id) {{{
      if (id == cHkFrame) { FrameToggle(); }
      else if (id == cHkOverlay) { OverlayToggle(); }
      else if (id == cHkSettings) { SettingsShow(); }
      else if ((id >= cHkMenuBase) && (id < (cHkMenuBase + 32))) { FrameShowMenu(FrameMenuCycle(id - cHkMenuBase)); }
      else if (id == cHkBezelDown) { BezelBrightnessStep(-5); }
      else if (id == cHkBezelUp) { BezelBrightnessStep(5); }
      else if (id == cHkZoomIn) { OverlaySetSize((int) Math.Round((gLastO.Width * 1.05)), false); }
      else if (id == cHkZoomOut) { OverlaySetSize((int) Math.Round((gLastO.Width * 0.95)), false); }
      else if (id == cHkScanlines) { SettingsSetById("Look.Scanlines", (gScanlines ? "0" : "1")); SettingsPushValue("Look.Scanlines"); }
    }}}
  }
}
