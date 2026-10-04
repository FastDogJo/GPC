// CLAUDE : new file. Strip / restore the emulator's caption, border and menu.
using System;

namespace FastDog
{
  public partial class FD
  {
    // FIELDS & PROPERTIES {{{
    public static bool gFrameStripped = false;
    private static long gSavedStyle = 0, gSavedExStyle = 0;
    private static IntPtr gSavedMenu = IntPtr.Zero;
    // CLAUDE : with the menu bar stripped Alt+<mnemonic> (Alt+F ...) does nothing in the emulator - these map Alt+letter to a popup replica of that menu
    public static readonly System.Collections.Generic.List<(int Pos, char Key, string Text)> gMenuMnemonics = new System.Collections.Generic.List<(int, char, string)>();
    // }}}
    public static void FrameStrip() {{{
      IntPtr h = gEmuHwnd;
      if ((gFrameStripped) || (h == IntPtr.Zero)) { return; }
      NRect e = Native.ClientScreenRect(h);
      gSavedStyle = Native.GetStyle(h); gSavedExStyle = Native.GetExStyle(h); gSavedMenu = Native.GetMenu(h);
      FrameMenuScan();
      long ns = (gSavedStyle & ~(Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX));
      long nx = (gSavedExStyle & ~(Native.WS_EX_DLGMODALFRAME | Native.WS_EX_CLIENTEDGE | Native.WS_EX_WINDOWEDGE | Native.WS_EX_STATICEDGE));
      Native.SetWindowLongPtrW(h, Native.GWL_STYLE, (IntPtr) ns);
      Native.SetWindowLongPtrW(h, Native.GWL_EXSTYLE, (IntPtr) nx);
      Native.SetMenu(h, IntPtr.Zero);
      Native.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, (Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED));
      Native.SetClientRect(h, e); // client area unchanged, frame/menu gone
      gFrameStripped = true;
      WL($"[Frame] stripped (style 0x{gSavedStyle:X} -> 0x{ns:X}, menu 0x{gSavedMenu.ToInt64():X}, {gMenuMnemonics.Count} Alt-menus)\n");
      HotkeyContextRefresh(); // CLAUDE
    }}}
    public static void FrameRestore() {{{
      IntPtr h = gEmuHwnd;
      if ((!gFrameStripped) || (h == IntPtr.Zero) || (!Native.IsWindow(h))) { gFrameStripped = false; return; }
      NRect e = Native.ClientScreenRect(h);
      Native.SetWindowLongPtrW(h, Native.GWL_STYLE, (IntPtr) gSavedStyle);
      Native.SetWindowLongPtrW(h, Native.GWL_EXSTYLE, (IntPtr) gSavedExStyle);
      if (gSavedMenu != IntPtr.Zero) { Native.SetMenu(h, gSavedMenu); }
      Native.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, (Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED));
      Native.DrawMenuBar(h);
      Native.SetClientRect(h, e);
      gFrameStripped = false;
      gMenuMnemonics.Clear();
      HotkeyContextRefresh(); // CLAUDE
      WL("[Frame] restored\n");
    }}}
    // Make the emulator match gStripFrame (called after the setting changes, hotkey, or attach).
    public static void FrameApply() {{{
      if (gEmuHwnd == IntPtr.Zero) { return; }
      if (gStripFrame) { FrameStrip(); } else { FrameRestore(); }
      TrackForceLayout();
    }}}
    public static void FrameToggle() {{{
      gStripFrame = (!gStripFrame);
      FrameApply();
      SettingsSaveSoon();
      SettingsPushValue("Frame.StripFrame");
    }}}
    // top-level items of the saved menu that have an &mnemonic
    private static void FrameMenuScan() {{{
      gMenuMnemonics.Clear();
      if (gSavedMenu == IntPtr.Zero) { return; }
      int n = Native.GetMenuItemCount(gSavedMenu);
      for (int i = 0; i < n; i++)
        {
          System.Text.StringBuilder sb = new System.Text.StringBuilder(128);
          Native.GetMenuStringW(gSavedMenu, (uint) i, sb, 128, Native.MF_BYPOSITION);
          string t = sb.ToString();
          int a = t.IndexOf('&');
          while ((a >= 0) && ((a + 1) < t.Length) && (t[a + 1] == '&')) { a = t.IndexOf('&', (a + 2)); }
          if ((a >= 0) && ((a + 1) < t.Length) && char.IsLetterOrDigit(t[a + 1])) { gMenuMnemonics.Add((i, char.ToUpperInvariant(t[a + 1]), t.Replace("&", ""))); }
        }
    }}}
    // Copy of the emulator's (foreign) menu with the current checked / disabled state, so it can be shown from our process.
    private static IntPtr FrameMenuClone(IntPtr src) {{{
      IntPtr dst = Native.CreatePopupMenu();
      int n = Native.GetMenuItemCount(src);
      for (int i = 0; i < n; i++)
        {
          uint st = Native.GetMenuState(src, (uint) i, Native.MF_BYPOSITION);
          if (st == 0xFFFFFFFF) { continue; }
          if ((st & Native.MF_SEPARATOR) != 0) { Native.AppendMenuW(dst, Native.MF_SEPARATOR, UIntPtr.Zero, null); continue; }
          System.Text.StringBuilder sb = new System.Text.StringBuilder(160);
          Native.GetMenuStringW(src, (uint) i, sb, 160, Native.MF_BYPOSITION);
          uint flags = (st & (Native.MF_GRAYED | Native.MF_DISABLED | Native.MF_CHECKED));
          IntPtr sub = Native.GetSubMenu(src, i);
          if (sub != IntPtr.Zero) { Native.AppendMenuW(dst, (Native.MF_POPUP | flags), (UIntPtr) (ulong) FrameMenuClone(sub).ToInt64(), sb.ToString()); }
          else { Native.AppendMenuW(dst, (Native.MF_STRING | flags), (UIntPtr) Native.GetMenuItemID(src, i), sb.ToString()); }
        }
      return dst;
    }}}
    // the registered menu position -> next menu with the same mnemonic letter (Alt+F : File, then FreHD, then File ...)
    private static readonly System.Collections.Generic.Dictionary<char, int> gMenuCycle = new System.Collections.Generic.Dictionary<char, int>();
    public static int FrameMenuCycle(int firstPos) {{{
      char key = '\0';
      foreach ((int Pos, char Key, string Text) m in gMenuMnemonics) { if (m.Pos == firstPos) { key = m.Key; break; } }
      if (key == '\0') { return firstPos; }
      System.Collections.Generic.List<int> same = new System.Collections.Generic.List<int>();
      foreach ((int Pos, char Key, string Text) m in gMenuMnemonics) { if (m.Key == key) { same.Add(m.Pos); } }
      gMenuCycle.TryGetValue(key, out int n);
      gMenuCycle[key] = (n + 1);
      return same[(n % same.Count)];
    }}}
    public static void FrameShowMenu(int pos) {{{
      if ((!gFrameStripped) || (gEmuHwnd == IntPtr.Zero) || (gSavedMenu == IntPtr.Zero) || (gSettingsForm is null)) { return; }
      IntPtr top = Native.GetSubMenu(gSavedMenu, pos);
      if (top == IntPtr.Zero) { return; }
      IntPtr clone = FrameMenuClone(top);
      NRect c = Native.ClientScreenRect(gEmuHwnd);
      IntPtr owner = gSettingsForm.Handle;
      Native.SetForegroundWindow(owner);
      int cmd = Native.TrackPopupMenu(clone, (Native.TPM_RETURNCMD | Native.TPM_LEFTALIGN | Native.TPM_TOPALIGN), c.Left, c.Top, 0, owner, IntPtr.Zero);
      Native.PostMessageW(owner, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);
      Native.DestroyMenu(clone);
      Native.SetForegroundWindow(gEmuHwnd);
      if (cmd != 0) { Native.PostMessageW(gEmuHwnd, Native.WM_COMMAND, (IntPtr) cmd, IntPtr.Zero); WL($"[Frame] menu command {cmd}\n"); }
    }}}
    // Find a menu item by (part of its) text anywhere in the emulator's menu (live menu bar, or the saved one while the frame is stripped).
    private static bool FindMenuItem(IntPtr menu, string text, out uint id, out bool isChecked) {{{
      id = 0; isChecked = false;
      if (menu == IntPtr.Zero) { return false; }
      int n = Native.GetMenuItemCount(menu);
      for (int i = 0; i < n; i++)
        {
          IntPtr sub = Native.GetSubMenu(menu, i);
          if (sub != IntPtr.Zero) { if (FindMenuItem(sub, text, out id, out isChecked)) { return true; } continue; }
          System.Text.StringBuilder sb = new System.Text.StringBuilder(160);
          Native.GetMenuStringW(menu, (uint) i, sb, 160, Native.MF_BYPOSITION);
          if (sb.ToString().Replace("&", "").Contains(text, StringComparison.OrdinalIgnoreCase))
            {
              id = Native.GetMenuItemID(menu, i);
              isChecked = ((Native.GetMenuState(menu, (uint) i, Native.MF_BYPOSITION) & Native.MF_CHECKED) != 0);
              return (id != 0);
            }
        }
      return false;
    }}}
    private static IntPtr EmuMenu() {{{ return ((gSavedMenu != IntPtr.Zero) ? gSavedMenu : Native.GetMenu(gEmuHwnd)); }}}
    // Runs a trs80gp menu command by its text. False when the emulator has no such item.
    public static bool EmuMenuCommand(string text) {{{
      if (gEmuHwnd == IntPtr.Zero) { return false; }
      if (!FindMenuItem(EmuMenu(), text, out uint id, out bool chk)) { WL($"WARNING : [Emu] no menu item \"{text}\"\n"); return false; }
      Native.PostMessageW(gEmuHwnd, Native.WM_COMMAND, (IntPtr) id, IntPtr.Zero);
      WL($"[Emu] menu command \"{text}\" ({id})\n");
      return true;
    }}}
    // trs80gp starts in whole-number display scales ; View -> Authentic Display scales smoothly with the window (and so with the bezel).
    public static async void EmuApplySmoothScaling() {{{
      if ((!gSmoothScaling) || (gEmuHwnd == IntPtr.Zero)) { return; }
      string cl = (" " + gCommandLine + " " + gExtraArgs + " ").ToLowerInvariant();
      if (cl.Contains(" -vs ") || cl.Contains(" -vi ") || cl.Contains(" -vh ") || cl.Contains(" -va ") || System.Text.RegularExpressions.Regex.IsMatch(cl, @" -v\d+ ")) { return; } // the user chose a display mode
      await System.Threading.Tasks.Task.Delay(600);
      if (FindMenuItem(EmuMenu(), "Authentic Display", out uint id, out bool chk) && (!chk))
        { Native.PostMessageW(gEmuHwnd, Native.WM_COMMAND, (IntPtr) id, IntPtr.Zero); WL("[Emu] display mode -> Authentic (smooth scaling)\n"); }
    }}}
    // trs80gp may re-add style bits (mode change / its own fullscreen). Log only, do not fight it.
    private static bool gFrameMismatchLogged = false;
    public static void FrameVerify() {{{
      if ((!gFrameStripped) || (gEmuHwnd == IntPtr.Zero)) { return; }
      bool bad = ((Native.GetStyle(gEmuHwnd) & Native.WS_CAPTION) != 0);
      if (bad && (!gFrameMismatchLogged)) { WL("WARNING : [Frame] emulator re-added its caption/border - toggle the frame hotkey twice to re-strip\n"); }
      gFrameMismatchLogged = bad;
    }}}
  }
}
