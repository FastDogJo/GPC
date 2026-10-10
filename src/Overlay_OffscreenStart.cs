// CLAUDE : new file. Start trs80gp off-screen : its main window is moved to (-32000,-32000) as early as possible (window CREATE, again on SHOW, and again if the
//   emulator moves itself back before we attach), so the user never sees it at its default spot with frame + menu. TrackAttach then strips / sizes / places it
//   under the bezel (OffscreenStartShift keeps its natural spot as the fallback position) and the overlay appears together with it. Setting : [Launch] StartOffscreen.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FastDog
{
  public partial class FD
  {
    // FIELDS & PROPERTIES {{{
    public static bool gStartOffscreen = true;
    private const int cOffscreenXY = -32000;
    private static IntPtr gStartHookCreate = IntPtr.Zero, gStartHookShow = IntPtr.Zero, gStartHookMove = IntPtr.Zero;
    private static uint gStartPid = 0;
    private static IntPtr gStartHwnd = IntPtr.Zero; // the window we moved off-screen
    private static NRect gStartOrig;                // where it was when we last caught it on-screen
    private static volatile bool gStartAttaching = false;   // TrackAttach has begun : from here on the emulator is moved on purpose, leave it alone
    private static readonly Native.WinEventProc gStartProc = OffscreenStartEvent; // keep the delegate alive
    // }}}
    // CLAUDE : the window is CREATED off-screen. A window made with WS_VISIBLE is on-screen the instant it exists, and the WinEvent hooks below only run
    //   afterwards (through our message queue) - a frame of empty title bar + icon was visible. STARTF_USEPOSITION gives the first CW_USEDEFAULT window
    //   (trs80gp's : its default spot cascades 26,156,390,... each launch) this position, so it never appears on-screen at all.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfoW
    {
      public uint cb; public string? lpReserved, lpDesktop, lpTitle;
      public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
      public ushort wShowWindow, cbReserved2;
      public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string? app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string? cwd, ref StartupInfoW si, out ProcessInformation pi);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    private const uint cStartfUsePosition = 0x4;
    // null = not started this way (setting off, or CreateProcess failed) : the caller falls back to Process.Start
    public static Process? OffscreenStartProcess(ProcessStartInfo psi) {{{
      if (!gStartOffscreen) { return null; }
      StartupInfoW si = new StartupInfoW { dwFlags = cStartfUsePosition, dwX = unchecked((uint) cOffscreenXY), dwY = unchecked((uint) cOffscreenXY) };
      si.cb = (uint) Marshal.SizeOf<StartupInfoW>();
      StringBuilder cmd = new StringBuilder("\"" + psi.FileName + "\" " + psi.Arguments);
      if (!CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, psi.WorkingDirectory, ref si, out ProcessInformation pi))
        { WL($"WARNING : [Launch] CreateProcess with a start position failed ({Marshal.GetLastWin32Error()}), using Process.Start\n"); return null; }
      CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
      try { return Process.GetProcessById((int) pi.dwProcessId); }
      catch (Exception ex) { WL($"WARNING : [Launch] cannot attach to pid {pi.dwProcessId} : {ex.Message}\n"); return null; }
    }}}
    // before Process.Start : hook every window CREATE / SHOW (the pid is not known yet ; OffscreenStartPid sets it before any message is pumped)
    public static void OffscreenStartBegin() {{{
      OffscreenStartEnd();
      if (!gStartOffscreen) { return; }
      gStartHookCreate = Native.SetWinEventHook(Native.EVENT_OBJECT_CREATE, Native.EVENT_OBJECT_CREATE, IntPtr.Zero, gStartProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
      gStartHookShow = Native.SetWinEventHook(Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_SHOW, IntPtr.Zero, gStartProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
    }}}
    // right after Process.Start : from now on only this process' LOCATIONCHANGE is watched too (cheap, pid-scoped)
    public static void OffscreenStartPid(uint pid) {{{
      if (gStartHookCreate == IntPtr.Zero) { return; }
      gStartPid = pid;
      gStartHookMove = Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, gStartProc, pid, 0, Native.WINEVENT_OUTOFCONTEXT);
      OffscreenStartWatch(pid); // CLAUDE
    }}}
    private static void OffscreenStartEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) {{{
      if ((gStartPid == 0) || gStartAttaching || (idObject != 0) || (idChild != 0)) { return; } // OBJID_WINDOW, the window itself
      Native.GetWindowThreadProcessId(hwnd, out uint pid);
      if (pid != gStartPid) { return; }
      if (!IsStartCandidate(hwnd)) { return; }
      OffscreenStartCatch(hwnd, ((evt == Native.EVENT_OBJECT_CREATE) ? "create" : ((evt == Native.EVENT_OBJECT_SHOW) ? "show" : "move")));
    }}}
    // top-level window with a caption and no owner only
    private static bool IsStartCandidate(IntPtr hwnd) {{{
      return ((Native.GetWindow(hwnd, Native.GW_OWNER) == IntPtr.Zero) && ((Native.GetStyle(hwnd) & Native.WS_CAPTION) == Native.WS_CAPTION));
    }}}
    // CLAUDE : zero opacity. trs80gp moves / resizes itself on-screen shortly after it is created (it restores its own saved window rect, HKCU\SOFTWARE\Phillips\trs80gp\
    //   model_N\win_main) - a framed window was visible for a frame before any hook / poll could push it away. From the first moment we see the window it is layered with
    //   alpha 0, so wherever it goes nothing is visible. OffscreenStartReveal (start of TrackAttach, window still parked) puts the original extended style back
    //   BEFORE FrameStrip saves it, so the layered bit never leaks into FrameRestore.
    private static IntPtr gStartAlphaHwnd = IntPtr.Zero;
    private static long gStartExStyle = 0;
    private static void OffscreenStartMakeInvisible(IntPtr hwnd) {{{ // caller holds gStartLock
      if (gStartAlphaHwnd == hwnd) { return; }
      gStartAlphaHwnd = hwnd;
      gStartExStyle = Native.GetExStyle(hwnd);
      Native.SetWindowLongPtrW(hwnd, Native.GWL_EXSTYLE, (IntPtr) (gStartExStyle | Native.WS_EX_LAYERED));
      Native.SetLayeredWindowAttributes(hwnd, 0, 0, 2); // LWA_ALPHA, alpha 0
    }}}
    public static void OffscreenStartReveal(IntPtr hwnd) {{{
      lock (gStartLock)
        {
          if ((gStartAlphaHwnd == IntPtr.Zero) || (gStartAlphaHwnd != hwnd)) { return; }
          gStartAlphaHwnd = IntPtr.Zero;
          if (!Native.IsWindow(hwnd)) { return; }
          Native.SetLayeredWindowAttributes(hwnd, 0, 255, 2);
          Native.SetWindowLongPtrW(hwnd, Native.GWL_EXSTYLE, (IntPtr) gStartExStyle);
          Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, (Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED));
          WL($"[Launch] emulator window 0x{hwnd.ToInt64():X} opacity restored\n");
        }
    }}}
    // shared by the WinEvent hooks (UI thread) and the 1 ms watcher (worker thread) : park the window off-screen if it is on-screen, remember where it was
    private static readonly object gStartLock = new object();
    private static void OffscreenStartCatch(IntPtr hwnd, string why) {{{
      lock (gStartLock)
        {
          if ((gStartPid == 0) || gStartAttaching) { return; }
          if ((gStartHwnd != IntPtr.Zero) && (hwnd != gStartHwnd)) { return; } // the first caption window of the process is the main one
          OffscreenStartMakeInvisible(hwnd); // CLAUDE
          NRect r; Native.GetWindowRect(hwnd, out r);
          if (r.Width <= 0) { return; }
          if (r.Left <= (cOffscreenXY + 1000)) // already off-screen (created there by OffscreenStartProcess, or moved by us) : adopt it, its natural spot is unknown -> a fixed one
            {
              if (gStartHwnd == IntPtr.Zero) { gStartHwnd = hwnd; gStartOrig = new NRect { Left = 100, Top = 100, Right = (100 + r.Width), Bottom = (100 + r.Height) }; WL($"[Launch] emulator window 0x{hwnd.ToInt64():X} was created off-screen ({r})\n"); }
              return;
            }
          gStartHwnd = hwnd; gStartOrig = r;
          Native.SetWindowPos(hwnd, IntPtr.Zero, cOffscreenXY, cOffscreenXY, 0, 0, (Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE));
          WL($"[Launch] emulator window 0x{hwnd.ToInt64():X} moved off-screen on {why} (was {r})\n");
        }
    }}}
    // CLAUDE : the emulator moves/resizes ITSELF on-screen shortly after it is created (it showed up at our final spot, small, for a frame). The WinEvent hooks
    //   only run through the UI thread's message queue - too slow. This ~1 ms worker (same idea as CursorRestoreIfWarped) pushes it back before DWM composes a frame.
    private static void OffscreenStartWatch(uint pid) {{{
      System.Threading.Tasks.Task.Run(() =>
        {
          timeBeginPeriod(1);
          try
            {
              Stopwatch sw = Stopwatch.StartNew();
              while ((sw.ElapsedMilliseconds < 15000) && (!gStartAttaching) && (gStartPid == pid))
                {
                  IntPtr h = gStartHwnd;
                  if (h == IntPtr.Zero)
                    {
                      Native.EnumWindows((w, l) =>
                        {
                          Native.GetWindowThreadProcessId(w, out uint wp);
                          if ((wp == pid) && IsStartCandidate(w)) { h = w; return false; }
                          return true;
                        }, IntPtr.Zero);
                    }
                  if (h != IntPtr.Zero) { OffscreenStartCatch(h, "poll"); }
                  System.Threading.Thread.Sleep(1);
                }
            }
          finally { timeEndPeriod(1); }
        });
    }}}
    // TrackAttach : the client rect the emulator would have at its natural (pre-move) spot
    public static NRect OffscreenStartShift(IntPtr hwnd, NRect e) {{{
      if ((gStartHwnd == IntPtr.Zero) || (hwnd != gStartHwnd)) { return e; }
      gStartAttaching = true;
      NRect w; Native.GetWindowRect(hwnd, out w);
      if (w.Left > (cOffscreenXY + 1000)) { return e; } // not moved after all
      int dx = (gStartOrig.Left - w.Left), dy = (gStartOrig.Top - w.Top);
      return new NRect { Left = (e.Left + dx), Top = (e.Top + dy), Right = (e.Right + dx), Bottom = (e.Bottom + dy) };
    }}}
    // TrackAttach : true while this emulator window is still parked off-screen (started off-screen and not placed yet)
    public static bool OffscreenStartActive(IntPtr hwnd) {{{
      if ((gStartHwnd == IntPtr.Zero) || (hwnd != gStartHwnd)) { return false; }
      NRect w; Native.GetWindowRect(hwnd, out w);
      return (w.Left <= (cOffscreenXY + 1000));
    }}}
    // the same size as e, parked off-screen : lets the emulator be sized (and the capture started) without ever being seen
    public static NRect OffscreenStartPark(NRect e) {{{
      return new NRect { Left = cOffscreenXY, Top = cOffscreenXY, Right = (cOffscreenXY + e.Width), Bottom = (cOffscreenXY + e.Height) };
    }}}
    // after the launch (attached or not) : unhook ; a window we hid but never attached goes back to where it was
    public static void OffscreenStartEnd() {{{
      if (gStartHookCreate != IntPtr.Zero) { Native.UnhookWinEvent(gStartHookCreate); gStartHookCreate = IntPtr.Zero; }
      if (gStartHookShow != IntPtr.Zero) { Native.UnhookWinEvent(gStartHookShow); gStartHookShow = IntPtr.Zero; }
      if (gStartHookMove != IntPtr.Zero) { Native.UnhookWinEvent(gStartHookMove); gStartHookMove = IntPtr.Zero; }
      OffscreenStartReveal(gStartAlphaHwnd); // CLAUDE : safety net : never leave the emulator transparent (no-op once TrackAttach revealed it)
      if ((gStartHwnd != IntPtr.Zero) && (gStartHwnd != gEmuHwnd) && Native.IsWindow(gStartHwnd))
        { Native.SetWindowPos(gStartHwnd, IntPtr.Zero, gStartOrig.Left, gStartOrig.Top, 0, 0, (Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE)); }
      gStartHwnd = IntPtr.Zero; gStartPid = 0; gStartAttaching = false;
      if (gHoldOverlay) { gHoldOverlay = false; TrackApplyVisibility(); } // safety net : never leave the overlay held
    }}}
  }
}
