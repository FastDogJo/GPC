// CLAUDE : new file. Tracks the emulator window (WinEvent hooks + 16 ms poll), lays out the overlay windows and
// pushes overlay moves/resizes back to the emulator (two-way sync).
// Echo guard : gLastE is the last emulator client rect we know/applied. An emulator event with an unchanged rect is
// ignored, and overlay-initiated changes are applied under gApplying, so there is no feedback loop and no drift.
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FastDog
{
  public partial class FD
  {
    // FIELDS & PROPERTIES {{{
    public static BezelForm? gBezelForm = null;
    public static DimForm? gDimForm = null;
    public static BezelRenderer? gBezelRenderer = null;
    public static DimRenderer? gDimRenderer = null;
    public static IntPtr gEmuHwnd = IntPtr.Zero;
    public static uint gEmuPid = 0;
    public static NRect gLastE;
    public static NRect gLastO;
    public static bool gApplying = false;
    // CLAUDE : the capture is started while the emulator is parked off-screen, so its first frame(s) are blank - the tint shader turned that into a solid tint-coloured flash.
    //   After an off-screen start the presenter is not shown until cPresenterSettleMs after the emulator was placed (the bezel, with the plain emulator in its hole, is up meanwhile).
    public static long gPresenterNotBefore = 0;
    public static bool gPresenterPending = false; // CLAUDE : public : DimRenderer.Active
    private const int cPresenterSettleMs = 200;
    public static bool gHoldOverlay = false; // CLAUDE : true while an off-screen-started emulator is being set up : overlay windows are not shown yet
    public static bool gMinimized = false;
    public static int gSyncCount = 0;
    private static bool gShownBezel = false, gShownDim = false, gShownPresenter = false; // CLAUDE : presenter (Phase 2)
    public static int gMinClientW = 0, gMinClientH = 0; // CLAUDE : smallest client size the emulator has accepted (learned when it refuses a shrink)
    private static bool gConform = false;               // CLAUDE : keep the emulator at the bezel opening aspect (off until first layout)
    private static NRect gConformSkip;                  // CLAUDE : rect the emulator refused to conform from - do not fight it
    private static int gZTick = 0;
    private static readonly IntPtr[] gHooks = new IntPtr[4];
    private static readonly Native.WinEventProc gHookProc = TrackWinEvent; // keep the delegate alive
    private static System.Windows.Forms.Timer? gTrackTimer = null;
    // }}}

    public static void TrackCreateWindows() {{{
      gBezelForm = new BezelForm();
      gDimForm = new DimForm();
      IntPtr hb = gBezelForm.Handle; IntPtr hd = gDimForm.Handle; // force native handles (windows stay hidden until ApplyVisibility)
      gBezelRenderer = new BezelRenderer(hb);
      gDimRenderer = new DimRenderer(hd);
      gTrackTimer = new System.Windows.Forms.Timer { Interval = 16 };
      gTrackTimer.Tick += (s, e) => { TrackPoll(); PowerTick(); gGlow?.Tick(); }; // CLAUDE : Phase 2 frame pump
      gTrackTimer.Start();
    }}}
    // Called once the emulator HWND is known.
    public static void TrackAttach(IntPtr hwnd, uint pid) {{{
      TrackStop();
      gEmuHwnd = hwnd; gEmuPid = pid;
      if (Native.IsZoomed(hwnd)) { Native.ShowWindow(hwnd, Native.SW_RESTORE); }
      gHooks[0] = Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, gHookProc, pid, 0, Native.WINEVENT_OUTOFCONTEXT);
      gHooks[1] = Native.SetWinEventHook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, gHookProc, pid, 0, Native.WINEVENT_OUTOFCONTEXT);
      gHooks[2] = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, gHookProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
      gHooks[3] = Native.SetWinEventHook(Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_DESTROY, IntPtr.Zero, gHookProc, pid, 0, Native.WINEVENT_OUTOFCONTEXT);
      NRect e = OffscreenStartShift(hwnd, Native.ClientScreenRect(hwnd)); // CLAUDE : started off-screen -> its natural spot
      if ((gWinW > 0) && (gWinH > 0) && (gWinX != int.MinValue) && (gWinY != int.MinValue) && BezelModel.Loaded)
        {
          NRect o = new NRect { Left = gWinX, Top = gWinY, Right = (gWinX + gWinW), Bottom = (gWinY + gWinH) };
          if (OnAnyScreen(o)) { e = BezelModel.ToClient(o); } else { WL("[Track] saved overlay rect is off-screen, ignoring\n"); }
        }
      bool parked = OffscreenStartActive(hwnd); // CLAUDE : started off-screen : size it, start the capture etc. there, and only move it into place at the end
      OffscreenStartReveal(hwnd); // CLAUDE : opacity back to normal while the window is still parked (before FrameStrip saves the extended style)
      gHoldOverlay = parked; // CLAUDE : the overlay windows stay hidden until the emulator is in place (cleared below, and by OffscreenStartEnd as a safety net)
      if (gStripFrame) { FrameStrip(); }
      if (parked) { Native.SetClientRect(hwnd, OffscreenStartPark(e)); } // CLAUDE
      else if (!e.Same(Native.ClientScreenRect(hwnd))) { Native.SetClientRect(hwnd, e); }
      gMinimized = false;
      TrackForceLayout();
      GlowApply(); // CLAUDE : Phase 2 (no-op unless [Glow] Enabled)
      HotkeyContextUpdate(); // CLAUDE
      EmuApplySmoothScaling(); // CLAUDE : trs80gp scale follows the bezel
      gConform = true; // CLAUDE
      if (parked) // CLAUDE : lay the (hidden) overlay windows out at the FINAL rect, put the emulator there, then show the overlay in one step
        {
          gLastE = e; TrackLayout();
          WL("[Track] overlay laid out (hidden), emulator still parked\n");
          Native.SetClientRect(hwnd, e);
          if (!Native.ClientScreenRect(hwnd).Same(e)) { TrackForceLayout(); } // it did not get exactly e : re-fit the overlay to what it got
          gHoldOverlay = false;
          gPresenterNotBefore = (Environment.TickCount64 + cPresenterSettleMs); gPresenterPending = true;
          WL("[Track] emulator placed, showing the bezel (presenter follows after the settle time)\n");
        }
      TrackApplyVisibility();
      WL($"[Track] attached hwnd=0x{hwnd.ToInt64():X} pid={pid} client={Native.ClientScreenRect(hwnd)} overlay={gLastO}\n");
    }}}
    public static void TrackStop() {{{
      for (int i = 0; i < gHooks.Length; i++) { if (gHooks[i] != IntPtr.Zero) { Native.UnhookWinEvent(gHooks[i]); gHooks[i] = IntPtr.Zero; } }
    }}}
    private static bool OnAnyScreen(NRect o) {{{
      Rectangle r = new Rectangle(o.Left, o.Top, o.Width, o.Height);
      return Screen.AllScreens.Any((s) => s.Bounds.IntersectsWith(r));
    }}}
    private static void TrackWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) {{{
      if (evt == Native.EVENT_SYSTEM_FOREGROUND) { HotkeyContextUpdate(); if ((gEmuHwnd != IntPtr.Zero) && (!gApplying)) { TrackReassertZ(); } } // CLAUDE : re-stack immediately, not on the next 130 ms check
      if ((evt != Native.EVENT_SYSTEM_FOREGROUND) && ((hwnd != gEmuHwnd) || (idObject != 0))) { return; }
      TrackPoll();
    }}}
    public static void TrackResetConform() { gConformSkip = new NRect(); } // CLAUDE
    public static void TrackForceLayout() {{{
      gLastE = new NRect();
      TrackPoll();
    }}}
    // Emulator -> overlay (also the fallback poll).
    public static void TrackPoll() {{{
      if ((gEmuHwnd == IntPtr.Zero) || (gBezelForm is null) || gApplying || gQuitting) { return; } // CLAUDE : gQuitting
      if (!Native.IsWindow(gEmuHwnd)) { EmulatorGone(); return; }
      if (gPresenterPending && (Environment.TickCount64 >= gPresenterNotBefore)) { gPresenterPending = false; TrackApplyVisibility(); WL("[Track] presenter shown\n"); } // CLAUDE
      bool iconic = Native.IsIconic(gEmuHwnd);
      if (iconic != gMinimized) { gMinimized = iconic; TrackApplyVisibility(); }
      if (iconic) { return; }
      NRect e = Native.ClientScreenRect(gEmuHwnd);
      if (!e.Same(gLastE)) { gLastE = e; TrackConform(); TrackLayout(); } // CLAUDE : TrackConform
      else if ((++gZTick % 8) == 0) { TrackCheckZ(); if ((gZTick % 64) == 0) { FrameVerify(); } }
    }}}
    // CLAUDE : emulator-initiated size (e.g. its fullscreen) whose aspect is not the bezel opening's : fit the WHOLE bezel inside that
    // rect and size the emulator to the opening. If the emulator refuses, it wins (remembered so we do not fight it).
    private static void TrackConform() {{{
      if ((!gConform) || (!BezelModel.Loaded) || (gLastE.Width <= 0) || (gLastE.Height <= 0) || gLastE.Same(gConformSkip)) { return; }
      double openAspect = ((double) (BezelModel.OpenR - BezelModel.OpenL) / (double) (BezelModel.OpenB - BezelModel.OpenT));
      double aspect = ((double) gLastE.Width / (double) gLastE.Height);
      if (Math.Abs((aspect / openAspect) - 1.0) < 0.02) { return; }
      NRect box = gLastE;
      double s = Math.Min(((double) box.Width / BezelModel.Width), ((double) box.Height / BezelModel.Height));
      int ow = (int) Math.Round((BezelModel.Width * s)); int oh = (int) Math.Round((BezelModel.Height * s));
      int ol = (box.Left + ((box.Width - ow) / 2)); int ot = (box.Top + ((box.Height - oh) / 2));
      NRect want = BezelModel.ToClient(new NRect { Left = ol, Top = ot, Right = (ol + ow), Bottom = (ot + oh) });
      Native.SetClientRect(gEmuHwnd, want);
      NRect actual = Native.ClientScreenRect(gEmuHwnd);
      if (Math.Abs((((double) actual.Width / actual.Height) / openAspect) - 1.0) >= 0.02) { gConformSkip = actual; WL($"[Track] emulator keeps {actual} (aspect not conformable)\n"); }
      gLastE = actual;
    }}}
    private static void TrackLayout() {{{
      NRect o = BezelModel.ToOverlay(gLastE);
      gApplying = true;
      try
        {
          gDimRenderer!.Resize(gLastE);
          if (BezelModel.Loaded) { gBezelRenderer!.Resize(o); }
          GlowLayout(o, gLastE); // CLAUDE
          TrackReassertZ();
        }
      finally { gApplying = false; }
      gLastO = o; gSyncCount++;
      TrackRememberOverlay(o);
    }}}
    private static void TrackRememberOverlay(NRect o) {{{
      if (BezelModel.Loaded && ((gWinX != o.Left) || (gWinY != o.Top) || (gWinW != o.Width) || (gWinH != o.Height)))
        { gWinX = o.Left; gWinY = o.Top; gWinW = o.Width; gWinH = o.Height; SettingsSaveSoon(); }
    }}}
    // Overlay -> emulator. Called from the bezel's WM_WINDOWPOSCHANGED.
    public static void TrackOnBezelChanged() {{{
      if (gApplying || (gEmuHwnd == IntPtr.Zero) || (gBezelForm is null) || (!BezelModel.Loaded) || (!Native.IsWindow(gEmuHwnd))) { return; }
      NRect o; Native.GetWindowRect(gBezelForm.Handle, out o);
      if ((o.Width < 8) || o.Same(gLastO)) { return; }
      gApplying = true;
      try
        {
          gBezelRenderer!.Resize(o);
          NRect e = BezelModel.ToClient(o);
          gDimRenderer!.Resize(e);
          GlowLayout(o, e); // CLAUDE
          Native.SetClientRect(gEmuHwnd, e);
          NRect actual = Native.ClientScreenRect(gEmuHwnd);
          gLastE = actual;
          if (actual.Width > (e.Width + 1)) { gMinClientW = actual.Width; } // CLAUDE : emulator refused to shrink
          if (actual.Height > (e.Height + 1)) { gMinClientH = actual.Height; }
          if ((Math.Abs(actual.Width - e.Width) > 1) || (Math.Abs(actual.Height - e.Height) > 1))
            { // emulator snapped/refused the size : the emulator wins, re-fit the overlay to it
              o = BezelModel.ToOverlay(actual);
              gDimRenderer.Resize(actual);
              gBezelRenderer.Resize(o);
              GlowLayout(o, actual); // CLAUDE
              Native.SetWindowPos(gBezelForm.Handle, IntPtr.Zero, o.Left, o.Top, o.Width, o.Height, (Native.SWP_NOZORDER | Native.SWP_NOACTIVATE));
            }
        }
      finally { gApplying = false; }
      gLastO = o; gSyncCount++;
      TrackRememberOverlay(o);
    }}}
    // CLAUDE : screen-fit sliders changed : the bezel stays where it is, the emulator is re-fitted into the new opening.
    public static void ScreenFitApply() {{{
      BezelModel.ApplyAdjust();
      if ((gEmuHwnd == IntPtr.Zero) || (!BezelModel.Loaded)) { return; }
      gLastO = new NRect(); // defeat the "unchanged" early-out
      TrackOnBezelChanged();
      if (gBlackBehind) { gBezelRenderer?.Render(); } // CLAUDE : bezel size is unchanged so Resize() does not redraw, but the black fill depends on the emulator rect
    }}}
    // CLAUDE : SetWindowPos(hWndInsertAfter=X) puts a window BELOW X, so "directly above B" means inserting after the window that is currently above B.
    public static bool gZAllow = false;
    // CLAUDE : a click on the bezel raises the whole assembly (emulator + dim + bezel + presenter), like a click on the emulator does.
    public static void BringAssemblyToFront() {{{
      if (gEmuHwnd == IntPtr.Zero) { return; }
      NPoint saved; bool haveCursor = GetCursorPos(out saved); // CLAUDE
      // CLAUDE : the emulator must never be above the overlays, not even for one frame (visible flash) : the overlays go TOPMOST first, the emulator is raised /
      //   activated underneath them, then the overlays drop back to normal in order (dim, bezel, presenter) directly above the emulator.
      IntPtr[] chain = TrackZChain();
      const uint zf = (Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
      gZAllow = true;
      try
        {
          foreach (IntPtr w in chain) { Native.SetWindowPos(w, new IntPtr(-1), 0, 0, 0, 0, zf); } // HWND_TOPMOST
          Native.SetWindowPos(gEmuHwnd, IntPtr.Zero, 0, 0, 0, 0, zf); // HWND_TOP (still below the topmost overlays)
          Native.SetForegroundWindow(gEmuHwnd);
          foreach (IntPtr w in chain) { Native.SetWindowPos(w, new IntPtr(-2), 0, 0, 0, 0, zf); } // HWND_NOTOPMOST : top of the normal windows, in chain order
        }
      finally { gZAllow = false; }
      TrackReassertZ();
      if (haveCursor) { CursorRestoreIfWarped(saved); } // CLAUDE
    }}}
    // CLAUDE : trs80gp warps the cursor to the centre of its display when activated. Watch briefly and put the cursor back, but only if it
    // landed on that centre (so a genuine mouse move / bezel drag is never fought).
    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);
    // CLAUDE : ~1 ms polling on a worker thread (a 50 ms poll left the jump visible) so the cursor is back within about a frame.
    private static void CursorRestoreIfWarped(NPoint saved) {{{
      System.Threading.Tasks.Task.Run(() =>
        {
          timeBeginPeriod(1);
          try
            {
              System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
              while (sw.ElapsedMilliseconds < 500)
                {
                  NRect c = Native.ClientScreenRect(gEmuHwnd);
                  int cx = ((c.Left + c.Right) / 2); int cy = ((c.Top + c.Bottom) / 2);
                  if (GetCursorPos(out NPoint now) && ((Math.Abs(now.X - cx) <= 4) && (Math.Abs(now.Y - cy) <= 4)) && ((Math.Abs(saved.X - cx) > 4) || (Math.Abs(saved.Y - cy) > 4)))
                    { SetCursorPos(saved.X, saved.Y); return; }
                  System.Threading.Thread.Sleep(1);
                }
            }
          finally { timeEndPeriod(1); }
        });
    }}}
    // CLAUDE : next window below w that is visible (the app's own hidden owner/helper windows sit in the z-list and are not a stacking break).
    private static IntPtr NextVisible(IntPtr w) {{{
      IntPtr n = Native.GetWindow(w, Native.GW_HWNDNEXT);
      while ((n != IntPtr.Zero) && (!Native.IsWindowVisible(n))) { n = Native.GetWindow(n, Native.GW_HWNDNEXT); }
      return n;
    }}}
    private static void PlaceAbove(IntPtr a, IntPtr b) {{{
      if (NextVisible(a) == b) { return; } // already directly above b (CLAUDE : hidden helper windows in between do not count)
      IntPtr prev = Native.GetWindow(b, Native.GW_HWNDPREV);
      if (prev == a) { prev = Native.GetWindow(a, Native.GW_HWNDPREV); }
      if ((prev != IntPtr.Zero) && ((Native.GetExStyle(prev) & 0x8) != 0)) { prev = IntPtr.Zero; } // do not become topmost just because the window above b is
      gZAllow = true; // CLAUDE : the bezel's WM_WINDOWPOSCHANGING otherwise vetoes z-order changes (the system raising it during a click / drag)
      Native.SetWindowPos(a, prev, 0, 0, 0, 0, (Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE)); // prev == 0 : HWND_TOP
      gZAllow = false;
    }}}
    // Z order, bottom -> top : emulator, dim, bezel, presenter (only the shown ones).
    private static IntPtr[] TrackZChain() {{{
      System.Collections.Generic.List<IntPtr> c = new System.Collections.Generic.List<IntPtr>();
      if (gShownDim && (gDimForm is not null)) { c.Add(gDimForm.Handle); }
      if (gShownBezel && (gBezelForm is not null)) { c.Add(gBezelForm.Handle); }
      if (gShownPresenter && (gPresenterForm is not null)) { c.Add(gPresenterForm.Handle); }
      return c.ToArray();
    }}}
    public static void TrackReassertZ() {{{
      if (gEmuHwnd == IntPtr.Zero) { return; }
      IntPtr below = gEmuHwnd;
      foreach (IntPtr w in TrackZChain()) { PlaceAbove(w, below); below = w; }
    }}}
    private static void TrackCheckZ() {{{
      IntPtr below = gEmuHwnd;
      foreach (IntPtr w in TrackZChain()) { if (NextVisible(w) != below) { TrackReassertZ(); return; } below = w; }
    }}}
    public static void TrackApplyVisibility() {{{
      if ((gBezelForm is null) || (gDimForm is null)) { return; }
      bool show = (gOverlayVisible && (!gMinimized) && (gEmuHwnd != IntPtr.Zero) && (!gHoldOverlay)); // CLAUDE : gHoldOverlay
      bool sb = (show && BezelModel.Loaded);
      bool sd = (show && DimRenderer.Active);
      if (sd && (!gShownDim)) { Native.ShowWindow(gDimForm.Handle, Native.SW_SHOWNA); gShownDim = true; } // CLAUDE : show before ...
      if (sb != gShownBezel) { Native.ShowWindow(gBezelForm.Handle, (sb ? Native.SW_SHOWNA : Native.SW_HIDE)); gShownBezel = sb; }
      bool sp = (show && GlowActive && (gPresenterForm is not null) && (Environment.TickCount64 >= gPresenterNotBefore)); // CLAUDE : Phase 2 presenter (not before the settle time, see gPresenterNotBefore)
      if (sp != gShownPresenter) { Native.ShowWindow(gPresenterForm!.Handle, (sp ? Native.SW_SHOWNA : Native.SW_HIDE)); gShownPresenter = sp; }
      if ((!sd) && gShownDim) { Native.ShowWindow(gDimForm.Handle, Native.SW_HIDE); gShownDim = false; } // CLAUDE : ... and hide after, so the picture is never uncovered between the two (presenter takes over from this overlay)
      if (sb || sd || sp) { TrackReassertZ(); }
    }}}
    public static void OverlayToggle() {{{
      gOverlayVisible = (!gOverlayVisible);
      TrackApplyVisibility();
      SettingsSaveSoon();
      SettingsPushValue("Window.OverlayVisible");
    }}}
    // Look settings changed (live-apply). reloadBezel : the bezel image/opening changed.
    public static void LookApply(bool reloadBezel) {{{
      if (gBezelRenderer is null) { return; }
      if (reloadBezel) { BezelModel.Load(); PowerRectPushValues(); } // CLAUDE : per-bezel power-rect defaults may have changed
      gDimRenderer!.SetLook();
      if (reloadBezel) { GlowBezelChanged(); } // CLAUDE
      gGlow?.SetLook();
      if (reloadBezel) { if (gEmuHwnd != IntPtr.Zero) { TrackForceLayout(); } else { gBezelRenderer.Render(); } }
      TrackApplyVisibility();
    }}}
    private static void EmulatorGone() {{{
      WL("[Track] emulator window gone\n");
      TrackStop();
      GlowStop(); // CLAUDE
      gEmuHwnd = IntPtr.Zero; gFrameStripped = false;
      TrackApplyVisibility();
      gEmuState = "exited";
      if ((gSettingsForm is not null) && gSettingsForm.Visible) { gSettingsForm.BringToFront(); } // CLAUDE : UI hidden -> nothing left to do, quit
      else { gSettingsForm?.BeginInvoke(new Action(AppQuit)); }
    }}}
    public static void TrackShutdown() {{{
      gTrackTimer?.Stop();
      TrackStop();
      GlowStop(); // CLAUDE
      gBezelForm?.Close(); gDimForm?.Close(); gPresenterForm?.Close();
    }}}
  }
}
