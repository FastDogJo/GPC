// CLAUDE : new file. Launch / relaunch / close of trs80gp.exe.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace FastDog
{
  public partial class FD
  {
    // FIELDS & PROPERTIES {{{
    public static Process? gEmuProc = null;
    public static bool gEmuLaunchedByUs = false;
    public static string gEmuState = "not launched";
    public static string gLaunchedCommandLine = ""; // CLAUDE : the ini command line the running emulator was started with
    // }}}
    private static string ResolvePath(string p, bool wantFile) {{{
      if (Path.IsPathRooted(p)) { return p; }
      string pa = Path.GetFullPath(Path.Combine(IniDir(), p)); // CLAUDE : relative to the preset file first
      if (wantFile ? File.Exists(pa) : Directory.Exists(pa)) { return pa; }
      string a = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, p));
      if (wantFile ? File.Exists(a) : Directory.Exists(a)) { return a; }
      return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), p));
    }}}
    private static IntPtr FindMainWindow(Process p) {{{
      IntPtr found = IntPtr.Zero;
      uint pid = (uint) p.Id;
      Native.EnumWindows((h, l) =>
        {
          uint wp; Native.GetWindowThreadProcessId(h, out wp);
          if ((wp == pid) && Native.IsWindowVisible(h) && (Native.GetWindow(h, Native.GW_OWNER) == IntPtr.Zero)) { found = h; return false; }
          return true;
        }, IntPtr.Zero);
      return found;
    }}}
    private static async Task<IntPtr> WaitForWindow(Process p, int timeoutMs) {{{
      int waited = 0;
      while (waited < timeoutMs)
        {
          if (p.HasExited) { return IntPtr.Zero; }
          p.Refresh();
          IntPtr h = p.MainWindowHandle;
          if (h == IntPtr.Zero) { h = FindMainWindow(p); }
          if (h != IntPtr.Zero) { return h; }
          await Task.Delay(50);
          waited += 50;
        }
      return IntPtr.Zero;
    }}}
    // CLAUDE : trs80gp warps the mouse cursor at startup - remember it before launch and put it back afterwards
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetCursorPos(out NPoint p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    public static async Task LaunchAsync() {{{
      NPoint savedCursor; bool haveCursor = GetCursorPos(out savedCursor); // CLAUDE
      if (gStartMonitorOff) { PowerSetOffNow(); } // CLAUDE : Launch.StartMonitorOff
      try
        {
          gEmuState = "launching";
          string exe = ResolvePath(gTrs80gpPath, true);
          string dir = ResolvePath(gWorkingDir, false);
          if (!File.Exists(exe)) { WL($"ERROR : [Launch] not found : {exe}\n"); gEmuState = "exe not found"; return; }
          string args = (gCommandLine + (((gCommandLine.Length > 0) && (gExtraArgs.Length > 0)) ? " " : "") + gExtraArgs);
          // CLAUDE : without the GPU renderer the phosphor tint is applied by trs80gp itself (-vc), unless the command line already sets a colour
          if ((!gGlowEnabled) && System.Text.RegularExpressions.Regex.IsMatch(gTint.Trim().TrimStart('#'), "^[0-9A-Fa-f]{6}$") && (!(" " + args + " ").Contains(" -vc "))) { args += (" -vc " + gTint.Trim().TrimStart('#')); }
          if ((!(" " + args + " ").Contains(" -vol ")) && (!(" " + args + " ").Contains(" -sv "))) { args += (" -vol " + Math.Clamp(gVolume, 0, 100)); } // CLAUDE : audio volume %
          ProcessStartInfo psi = new ProcessStartInfo { FileName = exe, Arguments = args, WorkingDirectory = (Directory.Exists(dir) ? dir : Path.GetDirectoryName(exe)!), UseShellExecute = false };
          WL($"[Launch] \"{exe}\" {psi.Arguments}\n");
          OffscreenStartBegin(); // CLAUDE
          Process p = (OffscreenStartProcess(psi) ?? Process.Start(psi)!); // CLAUDE : created off-screen (STARTF_USEPOSITION) when the setting is on
          OffscreenStartPid((uint) p.Id); // CLAUDE
          gEmuLaunchedByUs = true;
          gLaunchedCommandLine = gCommandLine; // CLAUDE
          IntPtr h = await WaitForWindow(p, 5000);
          if (haveCursor) // CLAUDE : restore now, and again once trs80gp has finished starting up (its warp can land after the window appears)
            {
              SetCursorPos(savedCursor.X, savedCursor.Y);
              await Task.Delay(500);
              SetCursorPos(savedCursor.X, savedCursor.Y);
            }
          if (h == IntPtr.Zero) { WL("ERROR : [Launch] no emulator window found\n"); gEmuState = "no window"; return; }
          gEmuProc = p;
          try { p.EnableRaisingEvents = true; p.Exited += (s, e) => { try { gSettingsForm?.BeginInvoke(new Action(() => TrackPoll())); } catch { } }; }
          catch (Exception ex) { WL($"WARNING : [Launch] Exited event unavailable : {ex.Message}\n"); }
          gEmuState = "attached";
          TrackAttach(h, (uint) p.Id);
        }
      catch (Exception ex) { WL($"ERROR : [Launch] {ex.Message}\n"); gEmuState = "launch failed"; }
      finally { OffscreenStartEnd(); } // CLAUDE
    }}}
    // Close the emulator (kill & restart relaunch / exit path). Only closes processes we launched unless force.
    public static void EmulatorClose(bool force) {{{
      Process? p = gEmuProc;
      if ((p is null) || ((!gEmuLaunchedByUs) && (!force))) { return; }
      try
        {
          if (!p.HasExited)
            {
              p.CloseMainWindow();
              if (!p.WaitForExit(1500)) { p.Kill(); p.WaitForExit(1000); }
            }
        }
      catch (Exception ex) { WL($"WARNING : [Launch] close : {ex.Message}\n"); }
    }}}
    public static async Task RelaunchAsync() {{{
      WL("[Launch] relaunch\n");
      KeysCancel(); // CLAUDE
      TrackStop();
      GlowStop(); // CLAUDE
      gFrameStripped = false; // the old window is about to die; nothing to restore
      EmulatorClose(true);
      gEmuProc = null; gEmuHwnd = IntPtr.Zero;
      TrackApplyVisibility();
      await LaunchAsync();
    }}}
  }
}
