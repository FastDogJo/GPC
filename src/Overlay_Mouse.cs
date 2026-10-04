// CLAUDE : new file. Low-level mouse hook : swallows the SECOND click of a double-click on the emulator window, so trs80gp never sees a
// double-click (its double-click = fullscreen). Single clicks, drags and the bezel are untouched. Setting : Frame.BlockDoubleClick.
using System;
using System.Runtime.InteropServices;

namespace FastDog
{
  public partial class FD
  {
    private delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct MsLlHook { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookExW(int id, LowLevelProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NPoint p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandleW(string? name); // CLAUDE

    private static IntPtr gMouseHook = IntPtr.Zero;
    private static readonly LowLevelProc gMouseProc = MouseHookProc; // keep alive
    private static uint gLastDownTime = 0; private static int gLastDownX = 0, gLastDownY = 0; private static bool gLastDownValid = false, gSwallowUp = false;

    public static void MouseHookStart() {{{
      if (gMouseHook != IntPtr.Zero) { return; }
      gMouseHook = SetWindowsHookExW(14, gMouseProc, GetModuleHandleW(null), 0); // WH_MOUSE_LL ; CLAUDE : GetHINSTANCE returns -1 in a single-file exe
      if (gMouseHook == IntPtr.Zero) { WL($"WARNING : [Mouse] hook failed ({Marshal.GetLastWin32Error()})\n"); }
    }}}
    public static void MouseHookStop() {{{
      if (gMouseHook != IntPtr.Zero) { UnhookWindowsHookEx(gMouseHook); gMouseHook = IntPtr.Zero; }
    }}}
    private static IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam) {{{
      if ((code >= 0) && gBlockDoubleClick && (gEmuHwnd != IntPtr.Zero))
        {
          int msg = (int) wParam.ToInt64();
          if ((msg == 0x201) || (msg == 0x202)) // WM_LBUTTONDOWN / UP
            {
              MsLlHook h = Marshal.PtrToStructure<MsLlHook>(lParam);
              if (msg == 0x202) { if (gSwallowUp) { gSwallowUp = false; return (IntPtr) 1; } }
              else
                {
                  NRect c = Native.ClientScreenRect(gEmuHwnd);
                  bool over = ((h.X >= c.Left) && (h.X < c.Right) && (h.Y >= c.Top) && (h.Y < c.Bottom));
                  if (over)
                    {
                      IntPtr w = WindowFromPoint(new NPoint { X = h.X, Y = h.Y });
                      over = ((w != IntPtr.Zero) && (GetAncestor(w, 2) == gEmuHwnd)); // GA_ROOT : really the emulator under the cursor, not a window on top of it
                    }
                  if (over)
                    {
                      bool dbl = (gLastDownValid && ((h.Time - gLastDownTime) <= GetDoubleClickTime())
                                  && (Math.Abs(h.X - gLastDownX) <= (GetSystemMetrics(36) / 2)) && (Math.Abs(h.Y - gLastDownY) <= (GetSystemMetrics(37) / 2))); // SM_CXDOUBLECLK / CYDOUBLECLK
                      if (dbl) { gLastDownValid = false; gSwallowUp = true; return (IntPtr) 1; }
                      gLastDownValid = true; gLastDownTime = h.Time; gLastDownX = h.X; gLastDownY = h.Y;
                    }
                  else { gLastDownValid = false; }
                }
            }
        }
      return CallNextHookEx(gMouseHook, code, wParam, lParam);
    }}}
  }
}
