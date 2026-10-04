// CLAUDE : bezel switching, size presets and the overlay window sizing helpers. Everything is stored in the single ini (see ProfileMigrate / PresetSection).
// SettingEntry.Profile marks the "look" settings (look, glow/post/glass, power rect, overlay window rect) : those are what a size preset snapshots.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FastDog
{
  public partial class FD
  {
    public static string gProfileName = "";

    public static string ProfileNameFor() {{{
      if (!string.IsNullOrWhiteSpace(gBezelFile)) { return Path.GetFileNameWithoutExtension(gBezelFile); }
      return "None";
    }}}

    private static void ProfileReadInto(Nini.Config.IConfigSource src, bool skipPosition) {{{
      foreach (SettingEntry e in gSettingTable)
        {
          if (!e.Profile) { continue; }
          if (skipPosition && (e.Section == "Window") && ((e.Key == "X") || (e.Key == "Y"))) { continue; }
          Nini.Config.IConfig? cfg = src.Configs[e.Section];
          string? v = cfg?.Get(e.Key);
          if (v is not null) { e.Set(v); }
        }
    }}}
    // CLAUDE : ONE ini holds everything. Older versions kept the look of each bezel in <Bezel><Style>.ini beside the main ini : on the first run
    //   (no [Look] Brightness key yet in the main ini) that file is read once, so existing tuning carries over. It is never written again.
    public static void ProfileMigrate() {{{
      gProfileName = ProfileNameFor();
      if ((NINISource?.Configs["Look"]?.Get("Brightness")) is not null) { return; }
      string path = Path.Combine(IniDir(), (gProfileName + ".ini"));
      Nini.Config.IniConfigSource? src = NINIOpenFile(path);
      if (src is null) { return; }
      ProfileReadInto(src, false);
      WL($"[Settings] migrated the look settings of \"{Path.GetFileName(path)}\" into the main ini\n");
    }}}

    // Change the bezel / style / file : the look settings stay as they are, the new bezel is loaded and everything re-applied.
    public static void ProfileSwitch(Action change) {{{
      NRect oldO = gLastO;
      change();
      gProfileName = ProfileNameFor();
      WL($"[Profile] bezel -> \"{gProfileName}\"\n");
      ProfileApplyAll(oldO);
      SettingsSaveSoon();
    }}}
    // Push every profile setting into the live app (after a profile switch or a preset load).
    // oldO : the overlay rect before the switch (used for position/width when the profile has no saved rect).
    public static void ProfileApplyAll(NRect oldO) {{{
      if (gBezelRenderer is null) { return; }
      BezelModel.Load();
      gConformSkipReset();
      gDimRenderer!.SetLook();
      GlowBezelChanged();
      gGlow?.SetLook();
      if (gEmuHwnd != IntPtr.Zero)
        {
          NRect o = oldO;
          bool saved = ((gWinW > 0) && (gWinH > 0) && (gWinX != int.MinValue) && (gWinY != int.MinValue));
          if (saved) { o = new NRect { Left = gWinX, Top = gWinY, Right = (gWinX + gWinW), Bottom = (gWinY + gWinH) }; if (!OnAnyScreen(o)) { o = oldO; saved = false; } }
          if (BezelModel.Loaded && (BezelModel.Width > 0) && (o.Width > 0))
            { int h = (int) Math.Round((o.Width * ((double) BezelModel.Height / BezelModel.Width))); o.Bottom = (o.Top + h); }
          if (o.Width > 0) { OverlaySetRect(o); } else { TrackForceLayout(); }
        }
      else { gBezelRenderer.Render(); }
      TrackApplyVisibility();
      gSettingsForm?.PostInit();
    }}}
    private static void gConformSkipReset() { TrackResetConform(); }

    // Move/size the overlay (bezel window) to o; the emulator follows through the normal overlay -> emulator sync.
    public static void OverlaySetRect(NRect o) {{{
      if (gEmuHwnd == IntPtr.Zero) { return; }
      if (BezelModel.Loaded && (gBezelForm is not null))
        {
          gLastO = new NRect(); // defeat the "unchanged" early-out
          Native.SetWindowPos(gBezelForm.Handle, IntPtr.Zero, o.Left, o.Top, o.Width, o.Height, (Native.SWP_NOZORDER | Native.SWP_NOACTIVATE));
          TrackOnBezelChanged();
        }
      else { Native.SetClientRect(gEmuHwnd, o); TrackForceLayout(); }
    }}}
    // UI : set the overlay width (or height), the other follows the bezel aspect. Top-left stays.
    public static void OverlaySetSize(int value, bool isHeight) {{{
      if ((gEmuHwnd == IntPtr.Zero) || (value < 16)) { return; }
      NRect cur = gLastO;
      if ((cur.Width <= 0) || (cur.Height <= 0)) { return; }
      int w = value, h;
      if (BezelModel.Loaded && (BezelModel.Width > 0))
        {
          double ratio = ((double) BezelModel.Height / BezelModel.Width);
          if (isHeight) { w = (int) Math.Round((value / ratio)); }
          double sMin = Math.Max(((double) gMinClientW / Math.Max(1, (BezelModel.OpenR - BezelModel.OpenL))), ((double) gMinClientH / Math.Max(1, (BezelModel.OpenB - BezelModel.OpenT))));
          w = Math.Max(w, Math.Max(160, (int) Math.Ceiling((BezelModel.Width * sMin))));
          h = (int) Math.Round((w * ratio));
        }
      else { h = (isHeight ? value : (int) Math.Round((w * ((double) cur.Height / cur.Width)))); }
      OverlaySetRect(new NRect { Left = cur.Left, Top = cur.Top, Right = (cur.Left + w), Bottom = (cur.Top + h) });
    }}}

    // PRESET FILES {{{ : a preset is one .gpc ini file (the whole settings table). CLAUDE
    // Load : start again from the defaults, read the file, re-apply everything and relaunch the emulator with the file's command line.
    public static async void PresetLoadFile(string path) {{{
      try
        {
          if (!PresetConfirmDiscard()) { return; }
          NRect oldO = gLastO;
          foreach (SettingEntry e in gSettingTable) { e.Set(e.Default); }
          SetPresetPath(System.IO.Path.GetFullPath(path));
          SettingsLoad();
          gProfileName = ProfileNameFor();
          WL($"[Preset] loaded \"{path}\"\n");
          ProfileApplyAll(oldO);
          HotkeyRegisterAll();
          FrameApply();
          await RelaunchAsync();
          SettingsMarkClean();
          gSettingsForm?.PostInit();
        }
      catch (Exception ex) { WL($"ERROR : [Preset] load : {ex.Message}\n"); }
    }}}
    // Save current : write the current preset file (no file yet -> Save As)
    public static void PresetSave() {{{
      if (gIniPath is null) { PresetSaveAs(); return; }
      SettingsSave();
      WL($"[Preset] saved \"{gIniPath}\"\n");
    }}}
    public static void PresetSaveAs() {{{
      using System.Windows.Forms.SaveFileDialog dlg = new System.Windows.Forms.SaveFileDialog { Filter = "GPC preset (*.gpc)|*.gpc", DefaultExt = "gpc", AddExtension = true, Title = "Save preset as", FileName = ((gIniPath is null) ? "" : Path.GetFileName(gIniPath)), InitialDirectory = IniDir() };
      if (dlg.ShowDialog(gSettingsForm) != System.Windows.Forms.DialogResult.OK) { return; }
      NINISource = null; // the old file stays as it is; a fresh source is created for the new one
      NINISetup(dlg.FileName);
      SetPresetPath(dlg.FileName);
      SettingsSave();
      WL($"[Preset] saved as \"{dlg.FileName}\"\n");
      gSettingsForm?.PostInit();
    }}}
    public static void PresetLoadDialog() {{{
      using System.Windows.Forms.OpenFileDialog dlg = new System.Windows.Forms.OpenFileDialog { Filter = "GPC preset (*.gpc)|*.gpc|All files|*.*", DefaultExt = "gpc", Title = "Load preset", InitialDirectory = IniDir() };
      if (dlg.ShowDialog(gSettingsForm) == System.Windows.Forms.DialogResult.OK) { PresetLoadFile(dlg.FileName); }
    }}}
    // }}}
  }
}
