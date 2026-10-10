// CLAUDE : new file. Entry point, init order, shutdown.
using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FastDog
{
  public static class Program
  {
    [STAThread]
    public static int Main(string[] args) {{{
      Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); // physical pixels everywhere
      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      FD.ParseArgs(args); // CLAUDE
      FD.StartPresetCheck(); // CLAUDE : warn about a bad preset before anything starts
      FD.SettingsLoadAll();  // CLAUDE : preset file (if any); also loads the [Log] overrides, so the first log line comes after it
      if (FD.gIniPath is null) { FD.WLOutputToFile = false; } // CLAUDE : no preset file -> logging off by default
      FD.WL($"---- {FD.ExeName} start ----\n");
      AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { FD.FrameRestore(); } catch { } };
      AppDomain.CurrentDomain.UnhandledException += (s, e) => { try { FD.WL($"ERROR : unhandled : {e.ExceptionObject}\n"); FD.FrameRestore(); } catch { } };
      Application.ThreadException += (s, e) => { FD.WL($"ERROR : UI thread : {e.Exception}\n"); };
      // CLAUDE : the settings form always exists (it hosts the message pump helpers) and is shown at start unless Launch.ShowUiAtStart is off
      FD.gSettingsForm = new SettingsForm();
      IntPtr unused = FD.gSettingsForm.Handle;
      EventHandler? start = null;
      start = (s, e) => { Application.Idle -= start; FD.AppStart(); };
      Application.Idle += start;
      if (FD.gShowUiAtStart) { FD.gSettingsForm.Show(); } // CLAUDE : Launch.ShowUiAtStart
      Application.Run();
      return 0;
    }}}
  }

  public partial class FD
  {
    private static bool gShutDone = false;
    public static string gExtraArgs = ""; // CLAUDE : command-line args not consumed by the overlay; appended to trs80gp's args for this run only (not saved)
    // CLAUDE : first argument (if it does not start with '-') = preset file (.gpc, Windows file association). Every other argument is appended to the trs80gp command line for this run only.
    public static void ParseArgs(string[] args) {{{
      System.Collections.Generic.List<string> rest = new System.Collections.Generic.List<string>();
      int i = 0; // CLAUDE
      // CLAUDE : an unquoted preset path with spaces arrives split into several args ; rejoin the leading non-option args
      if ((args.Length > 0) && (!args[0].StartsWith("-")))
        {
          System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
          // CLAUDE : stop at the first arg that completes the preset path (ends in .gpc, or the joined path exists) so a following file arg (e.g. a .cas) is not glued on
          while ((i < args.Length) && (!args[i].StartsWith("-")))
            {
              parts.Add(args[i]); i++;
              string joined = string.Join(" ", parts);
              if ((joined.EndsWith(".gpc", StringComparison.OrdinalIgnoreCase)) || (System.IO.File.Exists(joined))) { break; }
            }
          gStartPreset = string.Join(" ", parts);
        }
      for (; i < args.Length; i++)
        {
          string a = args[i];
          rest.Add((a.Contains(' ') ? ("\"" + a + "\"") : a));
        }
      gExtraArgs = string.Join(" ", rest);
    }}}
    public static string? gStartPreset = null; // CLAUDE : preset file given on the command line
    // CLAUDE : a missing / unreadable preset file -> warn, then carry on with the default options
    public static void StartPresetCheck() {{{
      if (gStartPreset is null) { return; }
      string full = System.IO.Path.GetFullPath(gStartPreset);
      string? err = null;
      if (!System.IO.File.Exists(full)) { err = "File not found."; }
      else { try { new Nini.Config.IniConfigSource(full); } catch (Exception ex) { err = ex.Message; } }
      if (err is not null)
        {
          MessageBox.Show(($"Preset file \"{full}\" is not valid:\n\n{err}\n\nStarting with the default options."), "GPC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
          gStartPreset = null;
          return;
        }
      SetPresetPath(full);
    }}}
    // CLAUDE : make path the current preset file (settings + default log names follow it). Logs only move if they were not overridden.
    public static void SetPresetPath(string full) {{{
      string oldLog = WLDefaultLogName, oldLast = WLDefaultLastName;
      gIniPath = full;
      string baseName = System.IO.Path.GetFileNameWithoutExtension(full);
      WLDefaultLogName = System.IO.Path.Combine(IniDir(), (baseName + ".log"));
      WLDefaultLastName = System.IO.Path.Combine(IniDir(), (baseName + ".Last.log"));
      if (WLLogFileName == oldLog) { WLLogFileName = WLDefaultLogName; }
      if (WLFileNameLast == oldLast) { WLFileNameLast = WLDefaultLastName; }
    }}}
    private static bool gQuitAsking = false; // CLAUDE
    public static void AppQuit() {{{
      if (gQuitting || gQuitAsking) { return; }
      gQuitAsking = true; // CLAUDE : the prompt runs a nested message loop ; a second quit request (e.g. repeated power-rect click) must not open another prompt
      bool go;
      try { go = PresetConfirmDiscard(); } finally { gQuitAsking = false; }
      if (!go) { return; } // CLAUDE
      gQuitting = true;
      AppShutdown();
      Application.ExitThread();
    }}}
    // Init order : overlay windows -> bezel -> hotkeys -> launch/attach (called when the settings window is shown).
    public static async void AppStart() {{{
      try
        {
          BezelModel.Load();
          TrackCreateWindows();
          gBezelRenderer!.Render();
          HotkeyRegisterAll();
          MouseHookStart(); // CLAUDE
          PipeApply(); // CLAUDE : named pipe MCP server
          if (gAutoLaunch) { await LaunchAsync(); }
          else { gEmuState = "AutoLaunch off - press Relaunch"; }
          SettingsMarkClean(); // CLAUDE : tracking may have adjusted the window size while attaching
          if ((gEmuHwnd == IntPtr.Zero) && (!gQuitting)) { SettingsShow(); } // CLAUDE : nothing attached -> show the UI so the user is not stuck
        }
      catch (Exception ex) { WL($"ERROR : AppStart : {ex}\n"); }
    }}}
    // Close/restore the emulator per CloseEmulatorOnExit, then persist.
    public static void AppShutdown() {{{
      if (gShutDone) { return; }
      gShutDone = true;
      try
        {
          MouseHookStop(); // CLAUDE
          PipeStop(); // CLAUDE
          KeysCancel(); // CLAUDE
          TrackShutdown();
          if (gCloseEmulatorOnExit && gEmuLaunchedByUs) { gFrameStripped = false; EmulatorClose(false); }
          else { FrameRestore(); }
          HotkeyShutdown();
        }
      catch (Exception ex) { WL($"ERROR : shutdown : {ex.Message}\n"); }
      WL($"---- {ExeName} exit ----\n");
      WLClose();
    }}}
  }
}
