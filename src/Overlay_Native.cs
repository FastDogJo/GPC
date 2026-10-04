// CLAUDE : new file. All P/Invoke + native structs for GPC.
using System;
using System.Runtime.InteropServices;

namespace FastDog
{
  [StructLayout(LayoutKind.Sequential)]
  public struct NRect
  {
    public int Left, Top, Right, Bottom;
    public int Width { get { return (Right - Left); } }
    public int Height { get { return (Bottom - Top); } }
    public bool Same(NRect o) { return ((Left == o.Left) && (Top == o.Top) && (Right == o.Right) && (Bottom == o.Bottom)); }
    public override string ToString() { return $"{Left},{Top} {Width}x{Height}"; }
  }
  [StructLayout(LayoutKind.Sequential)] public struct NPoint { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct NSize { public int W, H; }
  [StructLayout(LayoutKind.Sequential)]
  public struct NBlend { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
  [StructLayout(LayoutKind.Sequential)]
  public struct NBitmapInfoHeader
  {
    public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage;
    public int XPels, YPels; public uint ClrUsed, ClrImportant;
  }
  [StructLayout(LayoutKind.Sequential)]
  public struct NWindowPos { public IntPtr Hwnd, HwndInsertAfter; public int X, Y, Cx, Cy; public uint Flags; }

  public static class Native
  {
    // CONSTANTS {{{
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000;
    public const long WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
    public const long WS_EX_DLGMODALFRAME = 0x00000001, WS_EX_CLIENTEDGE = 0x00000200, WS_EX_WINDOWEDGE = 0x00000100, WS_EX_STATICEDGE = 0x00020000;
    public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;
    public const int SW_HIDE = 0, SW_SHOWNA = 8, SW_RESTORE = 9;
    public const uint GW_HWNDNEXT = 2, GW_HWNDPREV = 3, GW_OWNER = 4;
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    public const uint ULW_ALPHA = 2;
    public const int WM_NCHITTEST = 0x84, WM_WINDOWPOSCHANGED = 0x47, WM_SIZING = 0x214, WM_MOUSEACTIVATE = 0x21, WM_HOTKEY = 0x312;
    public const int WM_ENTERSIZEMOVE = 0x231, WM_EXITSIZEMOVE = 0x232;
    public const int HTTRANSPARENT = -1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    // }}}
    public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    // USER32 {{{
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out NRect r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags); // CLAUDE
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out NRect r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref NPoint p);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr GetMenu(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetMenu(IntPtr hwnd, IntPtr menu);
    [DllImport("user32.dll")] public static extern bool DrawMenuBar(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NPoint pptDst, ref NSize psize, IntPtr hdcSrc, ref NPoint pptSrc, uint crKey, ref NBlend pblend, uint flags);
    // }}}
    // MENUS (frame-menu popup while the emulator's menu bar is stripped)
    public const uint MF_STRING = 0, MF_GRAYED = 1, MF_DISABLED = 2, MF_CHECKED = 8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800, MF_BYPOSITION = 0x400;
    public const uint TPM_LEFTALIGN = 0, TPM_TOPALIGN = 0, TPM_RETURNCMD = 0x100;
    public const int WM_COMMAND = 0x111, WM_NULL = 0, WM_WINDOWPOSCHANGING = 0x46, WM_NCLBUTTONDOWN = 0xA1, WM_LBUTTONDOWN = 0x201;
    [DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetMenuStringW(IntPtr menu, uint item, System.Text.StringBuilder s, int max, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetSubMenu(IntPtr menu, int pos);
    [DllImport("user32.dll")] public static extern uint GetMenuItemID(IntPtr menu, int pos);
    [DllImport("user32.dll")] public static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr idOrSub, string? text);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] public static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr owner, IntPtr rect);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    // GDI32 {{{
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref NBitmapInfoHeader bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    // }}}

    // CLAUDE : the VISIBLE window bounds (no invisible resize border) - this is what a Windows Graphics Capture frame of the window covers. Falls back to GetWindowRect.
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out NRect r, int size);
    public static NRect CaptureFrameRect(IntPtr hwnd) {{{
      NRect r;
      if ((DwmGetWindowAttribute(hwnd, 9, out r, System.Runtime.InteropServices.Marshal.SizeOf<NRect>()) == 0) && (r.Width > 0) && (r.Height > 0)) { return r; } // 9 = DWMWA_EXTENDED_FRAME_BOUNDS
      GetWindowRect(hwnd, out r);
      return r;
    }}}
    public static NRect ClientScreenRect(IntPtr hwnd) {{{
      NRect c; GetClientRect(hwnd, out c);
      NPoint p = new NPoint();
      ClientToScreen(hwnd, ref p);
      return new NRect { Left = p.X, Top = p.Y, Right = (p.X + c.Width), Bottom = (p.Y + c.Height) };
    }}}
    // Resize/move the window so its CLIENT area equals e (frame deltas are measured from the live window, so menus/borders are handled).
    public static void SetClientRect(IntPtr hwnd, NRect e) {{{
      NRect w; GetWindowRect(hwnd, out w);
      NRect c = ClientScreenRect(hwnd);
      int l = (c.Left - w.Left); int t = (c.Top - w.Top); int r = (w.Right - c.Right); int b = (w.Bottom - c.Bottom);
      SetWindowPos(hwnd, IntPtr.Zero, (e.Left - l), (e.Top - t), (e.Width + l + r), (e.Height + t + b), (SWP_NOACTIVATE | SWP_NOZORDER));
    }}}
    public static long GetStyle(IntPtr hwnd) { return GetWindowLongPtrW(hwnd, GWL_STYLE).ToInt64(); }
    public static long GetExStyle(IntPtr hwnd) { return GetWindowLongPtrW(hwnd, GWL_EXSTYLE).ToInt64(); }
  }
}
