// CLAUDE : new file. All persisted settings live in ONE table (gSettingTable): load, save and the WebView2 UI all go through it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace FastDog
{
  public sealed class SettingEntry
  {
    public string Section = "";
    public string Key = "";
    public Func<string> Get = () => "";
    public Action<string> Set = (v) => { };
    public string Default = "";   // CLAUDE : value at startup (field initializer), used to reset a profile
    public Func<string>? Ui = null; // CLAUDE : value shown by the UI when it differs from the stored text (blank = default)
    public bool Profile = false;  // CLAUDE : true = stored in the per-model/style profile ini, false = global ini
    public string Id { get { return (Section + "." + Key); } }
  }

  public partial class FD
  {
    // FIELDS & PROPERTIES {{{
    // [Launch]
    public static string gTrs80gpPath = "trs80gp.exe";
    public static string gCommandLine = "";
    public static string gWorkingDir = ".";
    public static bool gAutoLaunch = true;
    public static bool gCloseEmulatorOnExit = true;
    public static bool gShowUiAtStart = true; // CLAUDE : settings window shown at startup (otherwise only via the ShowSettings hotkey)
    public static string gDescription = ""; // CLAUDE : free text describing this preset
    public static bool gSmoothScaling = true; // CLAUDE : switch trs80gp to View -> Authentic Display (scales continuously with the window) instead of whole-number scales
    // [Window]  (int.MinValue = not saved yet)
    public static int gWinX = int.MinValue, gWinY = int.MinValue, gWinW = 0, gWinH = 0;
    public static int gSetX = int.MinValue, gSetY = int.MinValue, gSetW = 0, gSetH = 0;
    public static bool gOverlayVisible = true;
    // [Look]  (screen opening -1 = auto-detect from the bezel image)
    public static string gBezelFile = "";
    public static string gGlassFile = "";
    // CLAUDE : live fine-tuning of the (detected) screen opening : offsets in bezel-image px, scale in %
    public static int gScreenOffsetX = 0, gScreenOffsetY = 0, gScreenScaleX = 100, gScreenScaleY = 100;
    // CLAUDE : the emulator picture inside the screen opening (GPU rendering only) : offsets in bezel-image px, scale in %
    public static int gPictureOffsetX = 0, gPictureOffsetY = 0, gPictureScaleX = 100, gPictureScaleY = 100;
    public static int gBehindLevel = 0; // CLAUDE : 0 = black ... 100 = white ; grey level of that fill (Ctrl + mouse wheel over the bezel)
    public static bool gBlackBehind = false; // CLAUDE : black fill of the bezel opening not covered by the emulator (Screen fit < 100%)
    public static int gBezelBrightness = 100; // CLAUDE : replaces BezelOpacity - darkens the bezel like the TRS80 project's Bezel Brightness
    public static int gScanlineSize = 0; // scanline period in px, 0 = auto
    public static int gScreenLeft = -1, gScreenTop = -1, gScreenRight = -1, gScreenBottom = -1;
    public static int gBrightness = 100;
    public static bool gScanlines = false;
    public static int gScanlineOpacity = 30;
    public static int gVignette = 0;
    public static string gTint = "80FFA0";
    public static int gTintOpacity = 0;
    // [Power] CLAUDE : power-rect (see Overlay_Power.cs). Rect values are % of the bezel window, null = per-bezel default.
    public static bool gPowerShowRect = false;
    public static float? gPowerRectX = null, gPowerRectY = null, gPowerRectW = null, gPowerRectH = null;
    public static int gPowerLineMs = 150, gPowerDotMs = 200, gPowerFadeMs = 350, gPowerLineBright = 80, gPowerDotBright = 100, gPowerHalo = 30; // CLAUDE : defaults of the TRS80 project's CrtPower
    // [UI] CLAUDE : which collapsible sections are open (comma list of section ids)
    public static string gUiOpenSections = "Look";
    // [Frame]
    public static bool gStripFrame = true;
    public static bool gBlockDoubleClick = true; // CLAUDE : swallow the 2nd click of a double-click on the emulator (trs80gp's double-click = fullscreen)
    // [Hotkeys]
    public static string gHotkeyToggleFrame = "Ctrl+Alt+F";
    public static string gHotkeyToggleOverlay = "Ctrl+Alt+O";
    public static string gHotkeyShowSettings = "Ctrl+Alt+S";
    // CLAUDE : look hotkeys carried over from the TRS80 project (Ctrl+1 / Ctrl+2 bezel brightness, Ctrl+= / Ctrl+- zoom, Alt+T scanlines).
    //   Registered only while the emulator / overlay is the foreground window (HotkeyContextUpdate). Blank = disabled.
    public static string gHotkeyBezelDown = "Ctrl+1", gHotkeyBezelUp = "Ctrl+2", gHotkeyZoomIn = "Ctrl+Oemplus", gHotkeyZoomOut = "Ctrl+OemMinus", gHotkeyScanlines = "Alt+T";

    public static readonly List<SettingEntry> gSettingTable = new List<SettingEntry>()
    {
      SStr("Launch", "Trs80gpPath", () => gTrs80gpPath, (v) => gTrs80gpPath = v),
      SStr("Launch", "CommandLine", () => gCommandLine, (v) => gCommandLine = v),
      SStr("Launch", "WorkingDir", () => gWorkingDir, (v) => gWorkingDir = v),
      SBool("Launch", "AutoLaunch", () => gAutoLaunch, (v) => gAutoLaunch = v),
      SBool("Launch", "CloseEmulatorOnExit", () => gCloseEmulatorOnExit, (v) => gCloseEmulatorOnExit = v),
      SBool("Launch", "ShowUiAtStart", () => gShowUiAtStart, (v) => gShowUiAtStart = v), // CLAUDE
      SStr("Launch", "Description", () => gDescription, (v) => gDescription = v), // CLAUDE
      SBool("Launch", "SmoothScaling", () => gSmoothScaling, (v) => gSmoothScaling = v),
      SInt("Window", "X", () => gWinX, (v) => gWinX = v, int.MinValue, int.MinValue, int.MaxValue),
      SInt("Window", "Y", () => gWinY, (v) => gWinY = v, int.MinValue, int.MinValue, int.MaxValue),
      SInt("Window", "Width", () => gWinW, (v) => gWinW = v, 0, 0, 20000),
      SInt("Window", "Height", () => gWinH, (v) => gWinH = v, 0, 0, 20000),
      SInt("Window", "SettingsX", () => gSetX, (v) => gSetX = v, int.MinValue, int.MinValue, int.MaxValue),
      SInt("Window", "SettingsY", () => gSetY, (v) => gSetY = v, int.MinValue, int.MinValue, int.MaxValue),
      SInt("Window", "SettingsWidth", () => gSetW, (v) => gSetW = v, 0, 0, 20000),
      SInt("Window", "SettingsHeight", () => gSetH, (v) => gSetH = v, 0, 0, 20000),
      SBool("Window", "OverlayVisible", () => gOverlayVisible, (v) => gOverlayVisible = v),
      SStr("Look", "BezelFile", () => gBezelFile, (v) => gBezelFile = v),
      SInt("Look", "ScreenLeft", () => gScreenLeft, (v) => gScreenLeft = v, -1, -1, 100000),
      SInt("Look", "ScreenTop", () => gScreenTop, (v) => gScreenTop = v, -1, -1, 100000),
      SInt("Look", "ScreenRight", () => gScreenRight, (v) => gScreenRight = v, -1, -1, 100000),
      SInt("Look", "ScreenBottom", () => gScreenBottom, (v) => gScreenBottom = v, -1, -1, 100000),
      SInt("Look", "ScreenOffsetX", () => gScreenOffsetX, (v) => gScreenOffsetX = v, int.MinValue, -400, 400),
      SInt("Look", "ScreenOffsetY", () => gScreenOffsetY, (v) => gScreenOffsetY = v, int.MinValue, -400, 400),
      SInt("Look", "ScreenScaleX", () => gScreenScaleX, (v) => gScreenScaleX = v, int.MinValue, 50, 150),
      SInt("Look", "ScreenScaleY", () => gScreenScaleY, (v) => gScreenScaleY = v, int.MinValue, 50, 150),
      SInt("Look", "PictureOffsetX", () => gPictureOffsetX, (v) => gPictureOffsetX = v, int.MinValue, -400, 400), // CLAUDE
      SInt("Look", "PictureOffsetY", () => gPictureOffsetY, (v) => gPictureOffsetY = v, int.MinValue, -400, 400),
      SInt("Look", "PictureScaleX", () => gPictureScaleX, (v) => gPictureScaleX = v, int.MinValue, 50, 300),
      SInt("Look", "PictureScaleY", () => gPictureScaleY, (v) => gPictureScaleY = v, int.MinValue, 50, 300),
      SInt("Look", "BezelBrightness", () => gBezelBrightness, (v) => gBezelBrightness = v, int.MinValue, 0, 100),
      SBool("Look", "BlackBehind", () => gBlackBehind, (v) => gBlackBehind = v), // CLAUDE
      SInt("Look", "BehindLevel", () => gBehindLevel, (v) => gBehindLevel = v, int.MinValue, 0, 100), // CLAUDE
      SInt("Look", "ScanlineSize", () => gScanlineSize, (v) => gScanlineSize = v, int.MinValue, 0, 12),
      SInt("Look", "Brightness", () => gBrightness, (v) => gBrightness = v, 100, 0, 200), // CLAUDE : >100 = brighten (Phase 2 only)
      SBool("Look", "Scanlines", () => gScanlines, (v) => gScanlines = v),
      SInt("Look", "ScanlineOpacity", () => gScanlineOpacity, (v) => gScanlineOpacity = v, 30, 0, 100),
      SInt("Look", "Vignette", () => gVignette, (v) => gVignette = v, 0, 0, 100),
      SStr("Look", "Tint", () => gTint, (v) => gTint = v),
      SStr("Glass", "File", () => gGlassFile, (v) => gGlassFile = v), // CLAUDE : glass cover png ("" = none)
      SInt("Look", "TintOpacity", () => gTintOpacity, (v) => gTintOpacity = v, 0, 0, 100),
      SBool("Glow", "Enabled", () => gGlowEnabled, (v) => gGlowEnabled = v),
      SInt("Glow", "Curvature", () => gGlowCurvature, (v) => gGlowCurvature = v, int.MinValue, 0, 100),
      SInt("Glow", "ScreenRotation", () => gGlowRotation, (v) => gGlowRotation = v, int.MinValue, -150, 150),
      SInt("Glow", "CornerRadius", () => gGlowCornerRadius, (v) => gGlowCornerRadius = v, int.MinValue, 0, 50),
      SBool("Power", "ShowRect", () => gPowerShowRect, (v) => gPowerShowRect = v),
      SFlt("Power", "RectX", () => gPowerRectX, (v) => gPowerRectX = v),
      SFlt("Power", "RectY", () => gPowerRectY, (v) => gPowerRectY = v),
      SFlt("Power", "RectW", () => gPowerRectW, (v) => gPowerRectW = v),
      SFlt("Power", "RectH", () => gPowerRectH, (v) => gPowerRectH = v),
      SInt("Power", "LineMs", () => gPowerLineMs, (v) => gPowerLineMs = v, int.MinValue, 0, 2000),
      SInt("Power", "DotMs", () => gPowerDotMs, (v) => gPowerDotMs = v, int.MinValue, 0, 2000),
      SInt("Power", "FadeMs", () => gPowerFadeMs, (v) => gPowerFadeMs = v, int.MinValue, 0, 3000),
      SInt("Power", "LineBright", () => gPowerLineBright, (v) => gPowerLineBright = v, int.MinValue, 0, 100),
      SInt("Power", "DotBright", () => gPowerDotBright, (v) => gPowerDotBright = v, int.MinValue, 0, 100),
      SInt("Power", "Halo", () => gPowerHalo, (v) => gPowerHalo = v, int.MinValue, 0, 100),
      SBool("Log", "OutputToFile", () => WLOutputToFile, (v) => WLOutputToFile = v),
      SStr("Log", "FileName", () => ((WLLogFileName == WLDefaultLogName) ? "" : WLLogFileName), (v) => WLLogFileName = (string.IsNullOrWhiteSpace(v) ? WLDefaultLogName : v)),
      SStr("Log", "LastFileName", () => ((WLFileNameLast == WLDefaultLastName) ? "" : WLFileNameLast), (v) => WLFileNameLast = (string.IsNullOrWhiteSpace(v) ? WLDefaultLastName : v)),
      SInt("Log", "MaxFileSize", () => WLMaxLogFileSize, (v) => WLMaxLogFileSize = v, int.MinValue, 0, 1000000000),
      SBool("Log", "ShowTimeStamp", () => WLShowTimeStamp, (v) => WLShowTimeStamp = v),
      SStr("Log", "TimeFormat", () => WLTimeFormat, (v) => WLTimeFormat = v),
      SStr("UI", "OpenSections", () => gUiOpenSections, (v) => gUiOpenSections = v),
      SBool("Frame", "StripFrame", () => gStripFrame, (v) => gStripFrame = v),
      SBool("Frame", "BlockDoubleClick", () => gBlockDoubleClick, (v) => gBlockDoubleClick = v),
      SStr("Hotkeys", "ToggleFrame", () => gHotkeyToggleFrame, (v) => gHotkeyToggleFrame = v),
      SStr("Hotkeys", "ToggleOverlay", () => gHotkeyToggleOverlay, (v) => gHotkeyToggleOverlay = v),
      SStr("Hotkeys", "ShowSettings", () => gHotkeyShowSettings, (v) => gHotkeyShowSettings = v),
      SStr("Hotkeys", "BezelBrightnessDown", () => gHotkeyBezelDown, (v) => gHotkeyBezelDown = v),
      SStr("Hotkeys", "BezelBrightnessUp", () => gHotkeyBezelUp, (v) => gHotkeyBezelUp = v),
      SStr("Hotkeys", "ZoomIn", () => gHotkeyZoomIn, (v) => gHotkeyZoomIn = v),
      SStr("Hotkeys", "ZoomOut", () => gHotkeyZoomOut, (v) => gHotkeyZoomOut = v),
      SStr("Hotkeys", "ToggleScanlines", () => gHotkeyScanlines, (v) => gHotkeyScanlines = v),
    };
    // CLAUDE : runs after every static field initializer of every FD partial : capture defaults + classify global/profile entries
    static FD() {{{
      gSettingTable.AddRange(GlowSettingEntries()); // CLAUDE : the GLASS / SCENE / POST parameter set carried over from the TRS80 project
      foreach (SettingEntry e in gSettingTable) { e.Default = e.Get(); e.Profile = SettingIsProfile(e); }
    }}}
    // Profile (per model + style ini) : the look of one bezel. Everything else is global.
    private static bool SettingIsProfile(SettingEntry e) {{{
      switch (e.Section)
        {
          case "Look": return (e.Key != "BezelFile");
          case "Glow": return (e.Key != "Enabled");
          case "Glass": case "Scene": case "Post": return true;
          case "Power": return true;
          case "Window": return ((e.Key == "X") || (e.Key == "Y") || (e.Key == "Width") || (e.Key == "Height"));
          default: return false;
        }
    }}}
    // }}}

    // ENTRY BUILDERS {{{
    private static SettingEntry SStr(string section, string key, Func<string> get, Action<string> set) {{{
      return new SettingEntry { Section = section, Key = key, Get = get, Set = set };
    }}}
    private static SettingEntry SBool(string section, string key, Func<bool> get, Action<bool> set) {{{
      return new SettingEntry
      {
        Section = section, Key = key,
        Get = () => (get() ? "1" : "0"),
        Set = (v) => { string t = v.Trim().ToLowerInvariant(); set((t == "1") || (t == "true") || (t == "yes") || (t == "on")); }
      };
    }}}
    // CLAUDE : nullable float 0..100, blank = null ("use the default")
    private static SettingEntry SFlt(string section, string key, Func<float?> get, Action<float?> set) {{{
      return new SettingEntry
      {
        Section = section, Key = key,
        Get = () => (get().HasValue ? get()!.Value.ToString("0.0", CultureInfo.InvariantCulture) : ""),
        Set = (v) =>
        {
          if (string.IsNullOrWhiteSpace(v)) { set(null); return; }
          if (float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) { set(Math.Clamp(f, 0f, 100f)); }
        }
      };
    }}}
    // blank = the value that is stored as an empty string ("not set").
    private static SettingEntry SInt(string section, string key, Func<int> get, Action<int> set, int blank, int min, int max) {{{
      return new SettingEntry
      {
        Section = section, Key = key,
        Get = () => ((get() == blank) ? "" : get().ToString(CultureInfo.InvariantCulture)),
        Ui = () => ((get() == int.MinValue) ? "" : get().ToString(CultureInfo.InvariantCulture)),
        Set = (v) =>
        {
          if (string.IsNullOrWhiteSpace(v)) { set(blank); return; }
          if (int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) { set(Math.Clamp(n, min, max)); }
        }
      };
    }}}
    // }}}

    public static string? gIniPath = null; // CLAUDE : current preset file (.gpc), null = defaults only, nothing is written until Save As
    public static string IniDir() { return ((gIniPath is not null) ? System.IO.Path.GetDirectoryName(gIniPath)! : AppContext.BaseDirectory); }
    public static string SettingsIniPath() {{{
      return (gIniPath ?? ""); // CLAUDE
    }}}
    public static void SettingsLoad() {{{
      NINISetup(SettingsIniPath());
      if (NINISource is null) { WL("[Settings] no preset file, using defaults\n"); return; } // CLAUDE
      foreach (SettingEntry e in gSettingTable)
        {
          Nini.Config.IConfig? cfg = NINISource.Configs[e.Section];
          if (cfg is null) { continue; }
          string? v = cfg.Get(e.Key);
          if (v is not null) { e.Set(v); }
        }
      WL($"[Settings] Loaded \"{NINIFileName}\"\n");
    }}}
    // CLAUDE : load the ini (and, once, any older per-bezel ini).
    public static void SettingsLoadAll() {{{
      SettingsLoad();
      ProfileMigrate();
      SettingsMarkClean();
    }}}
    public static void SettingsSave() {{{
      if (gIniPath is null) { return; } // CLAUDE : defaults only, no file yet
      foreach (SettingEntry e in gSettingTable) { NINIWriteValue(e.Section, e.Key, e.Get(), false); } // CLAUDE : one ini for everything
      try { NINISource?.Save(); } catch (Exception ex) { WL($"ERROR : [Settings] Save : {ex.Message}\n"); }
      SettingsMarkClean(); // CLAUDE
    }}}
    // CLAUDE : the preset file is only written by Save current / Save As (SettingsSave). Callers that used to trigger an auto-save now do nothing ; "dirty" is found by comparing snapshots.
    public static void SettingsSaveSoon() { }
    // CLAUDE : text of every setting that is saved with the preset, minus the pure UI/window-position ones (moving a window is not an edit)
    private static string gSavedSnapshot = "";
    private static string SettingsSnapshot() {{{
      System.Text.StringBuilder sb = new System.Text.StringBuilder();
      foreach (SettingEntry e in gSettingTable)
        {
          if (((e.Section == "Window") && (e.Key != "Width") && (e.Key != "Height") && (e.Key != "OverlayVisible")) || ((e.Section == "UI"))) { continue; }
          sb.Append(e.Id).Append('=').Append(e.Get()).Append("\n");
        }
      return sb.ToString();
    }}}
    public static void SettingsMarkClean() { gSavedSnapshot = SettingsSnapshot(); }
    public static bool SettingsDirty() { return (SettingsSnapshot() != gSavedSnapshot); }
    // CLAUDE : true = carry on (nothing to save, saved, or the changes were ignored) ; false = cancelled
    public static bool PresetConfirmDiscard() {{{
      if (!SettingsDirty()) { return true; }
      string name = ((gIniPath is null) ? "the current settings" : System.IO.Path.GetFileName(gIniPath));
      DialogResult r = MessageBox.Show(gSettingsForm, ($"{name} has unsaved changes.\n\nYes = Save, No = Ignore the changes, Cancel = go back."), "GPC", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
      if (r == DialogResult.Cancel) { return false; }
      if (r == DialogResult.Yes) { PresetSave(); return (!SettingsDirty()); } // Save As dialog cancelled -> still dirty -> cancel
      return true;
    }}}
    public static SettingEntry? SettingsFind(string id) {{{
      return gSettingTable.Find((x) => (x.Id == id));
    }}}
    // UI -> app. Returns false when the value is rejected (invalid hotkey / unknown id).
    public static bool SettingsSetById(string id, string value) {{{
      SettingEntry? e = SettingsFind(id);
      if (e is null) { return false; }
      if ((e.Section == "Hotkeys") && (!(string.IsNullOrWhiteSpace(value) && (e.Key != "ToggleFrame") && (e.Key != "ToggleOverlay") && (e.Key != "ShowSettings"))) && (!HotkeyParse(value, out uint m, out uint vk))) { return false; }
      if ((e.Section == "Look") && (e.Key == "BezelFile") && (e.Get() == value)) { return true; } // CLAUDE : no change, no switch
      if ((e.Section == "Look") && (e.Key == "BezelFile")) { ProfileSwitch(() => e.Set(value)); return true; } // CLAUDE : bezel file switch
      if ((e.Section == "Window") && ((e.Key == "Width") || (e.Key == "Height"))) { if (int.TryParse(value, out int px)) { OverlaySetSize(px, (e.Key == "Height")); } return true; } // CLAUDE
      e.Set(value);
      SettingsApply(e);
      SettingsSaveSoon();
      return true;
    }}}
    // Live-apply side effects of a changed setting.
    public static void SettingsApply(SettingEntry e) {{{
      if (e.Section == "Look")
        {
          if ((e.Key == "ScreenOffsetX") || (e.Key == "ScreenOffsetY") || (e.Key == "ScreenScaleX") || (e.Key == "ScreenScaleY")) { ScreenFitApply(); return; } // CLAUDE
          if (e.Key.StartsWith("Picture")) { gGlow?.SetLook(); return; } // CLAUDE : GPU picture transform only
          if ((e.Key == "BezelBrightness") || (e.Key == "BlackBehind") || (e.Key == "BehindLevel")) { gBezelRenderer?.Render(); return; } // CLAUDE
          bool bezelChanged = ((e.Key == "BezelFile") || e.Key.StartsWith("Screen"));
          LookApply(bezelChanged);
        }
      else if (e.Section == "Power") { if (e.Key.StartsWith("Rect") || (e.Key == "ShowRect")) { gBezelRenderer?.Render(); } } // CLAUDE
      else if ((e.Section == "Glass") || (e.Section == "Scene") || (e.Section == "Post")) { GlowLookChanged(e.Key); } // CLAUDE
      else if (e.Section == "Glow") { if (e.Key == "Enabled") { GlowApply(); } else { GlowLookChanged(e.Key); } } // CLAUDE : Phase 2
      else if (e.Section == "Frame") { FrameApply(); }
      else if (e.Section == "Hotkeys") { HotkeyRegisterAll(); }
      else if ((e.Section == "Window") && (e.Key == "OverlayVisible")) { TrackApplyVisibility(); }
    }}}
  }
}
