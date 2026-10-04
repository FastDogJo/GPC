// CLAUDE : new file. WebView2 settings window (OverlayControls.html/.css/.js). JS <-> C# via WebMessage JSON.
//   JS -> C# : {type:'ready'} {type:'set',key:'Look.Brightness',value:'55'} {type:'browse'} {type:'relaunch'}
//              {type:'toggleFrame'} {type:'toggleOverlay'} {type:'exit'}
//   C# -> JS : {type:'init',values:{...}} {type:'value',key,value} {type:'result',key,ok} {type:'status',...}
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FastDog
{
  public sealed class SettingsForm : Form
  {
    private readonly WebView2 wv = new WebView2 { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer { Interval = 500 };
    private int lastSyncCount = 0;
    private DateTime lastStatusTime = DateTime.Now;

    public SettingsForm() {{{
      Text = "GPC";
      AutoScaleMode = AutoScaleMode.None;
      Controls.Add(wv);
      StartPosition = FormStartPosition.Manual;
      Size = new Size(620, 900);
      if ((FD.gSetW > 0) && (FD.gSetH > 0) && (FD.gSetX != int.MinValue) && (FD.gSetY != int.MinValue))
        {
          Rectangle r = new Rectangle(FD.gSetX, FD.gSetY, FD.gSetW, FD.gSetH);
          if (Screen.AllScreens.Any((s) => s.WorkingArea.IntersectsWith(r))) { Bounds = r; }
        }
      else { StartPosition = FormStartPosition.CenterScreen; }
      Load += async (s, e) => await InitWebView();
      FormClosing += (s, e) => // CLAUDE : X quits
        {
          if ((!FD.gShowUiAtStart) && (!FD.gQuitting) && (e.CloseReason == CloseReason.UserClosing)) { e.Cancel = true; Hide(); return; } // CLAUDE : UI not enabled at start -> X just hides it
          if (!FD.gQuitting) { e.Cancel = true; FD.AppQuit(); } // CLAUDE : AppQuit asks about unsaved changes, then ends the app
        };
      Move += (s, e) => CaptureBounds();
      Resize += (s, e) => CaptureBounds();
      statusTimer.Tick += (s, e) => { if (Visible) { PostStatus(); } }; // CLAUDE : nothing to update while hidden
      statusTimer.Start();
    }}}
    private void CaptureBounds() {{{
      if (WindowState != FormWindowState.Normal) { return; }
      FD.gSetX = Left; FD.gSetY = Top; FD.gSetW = Width; FD.gSetH = Height;
      FD.SettingsSaveSoon();
    }}}
    private async System.Threading.Tasks.Task InitWebView() {{{
      try
        {
          string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPC", "WebView2");
          CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, data);
          await wv.EnsureCoreWebView2Async(env);
          CoreWebView2 c = wv.CoreWebView2;
          c.SetVirtualHostNameToFolderMapping("overlay.local", ExtractUi(), CoreWebView2HostResourceAccessKind.Allow); // CLAUDE : UI files are embedded in the exe
          c.Settings.AreDefaultContextMenusEnabled = false;
          c.WebMessageReceived += OnMessage;
          c.Navigate("https://overlay.local/OverlayControls.html");
        }
      catch (Exception ex) { FD.WL($"ERROR : [UI] WebView2 init : {ex.Message}\n"); }
    }}}
    // CLAUDE : new method. Writes the embedded OverlayControls.* files to LocalAppData\GPC\ui (WebView2 needs a real folder) and returns it.
    private static string ExtractUi() {{{
      string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPC", "ui");
      Directory.CreateDirectory(dir);
      System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
      foreach (string name in new string[] { "OverlayControls.html", "OverlayControls.css", "OverlayControls.js" })
        {
          using Stream? s = asm.GetManifestResourceStream(name);
          if (s is null) { FD.WL($"WARNING : [UI] missing embedded resource {name}\n"); continue; }
          using FileStream f = File.Create(Path.Combine(dir, name));
          s.CopyTo(f);
        }
      return dir;
    }}}
    private void Post(object o) {{{
      try { if (wv.CoreWebView2 is not null) { wv.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(o)); } }
      catch (Exception ex) { FD.WL($"WARNING : [UI] post : {ex.Message}\n"); }
    }}}
    public void PostValue(string id) {{{
      SettingEntry? e = FD.SettingsFind(id);
      if (e is not null) { Post(new { type = "value", key = id, value = FD.SettingsUiValue(e) }); }
    }}}
    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {{{
      try
        {
          using JsonDocument doc = JsonDocument.Parse(e.WebMessageAsJson);
          JsonElement root = doc.RootElement;
          string type = root.GetProperty("type").GetString() ?? "";
          switch (type)
            {
              case "ready": PostInit(); PostStatus(); break;
              case "presetLoad": FD.PresetLoadDialog(); break; // CLAUDE
              case "presetSave": FD.PresetSave(); break;
              case "presetSaveAs": FD.PresetSaveAs(); break;
              case "set":
                {
                  string key = root.GetProperty("key").GetString() ?? "";
                  string val = root.GetProperty("value").GetString() ?? "";
                  bool ok = FD.SettingsSetById(key, val);
                  if (key.StartsWith("Hotkeys.")) { Post(new { type = "result", key = key, ok = ok }); }
                  break;
                }
              case "browse":
                {
                  using OpenFileDialog dlg = new OpenFileDialog { Filter = "trs80gp|trs80gp*.exe|Executables|*.exe", Title = "trs80gp.exe" };
                  if (dlg.ShowDialog(this) == DialogResult.OK) { FD.SettingsSetById("Launch.Trs80gpPath", dlg.FileName); PostValue("Launch.Trs80gpPath"); }
                  break;
                }
              case "resetPower": FD.PowerRectReset(); break; // CLAUDE
              case "relaunch": _ = FD.RelaunchAsync(); break;
              case "toggleFrame": FD.FrameToggle(); break;
              case "toggleOverlay": FD.OverlayToggle(); break;
              case "exit": FD.AppQuit(); break; // CLAUDE
            }
        }
      catch (Exception ex) { FD.WL($"ERROR : [UI] message : {ex.Message}\n"); }
    }}}
    // CLAUDE : every setting value + the preset list (also after a profile switch / preset load)
    public void PostInit() {{{
      Dictionary<string, string> values = new Dictionary<string, string>();
      foreach (SettingEntry s in FD.gSettingTable) { values[s.Id] = FD.SettingsUiValue(s); }
      Post(new { type = "init", values = values, @params = FD.GlowParamSpecs() });
      PostPresets("");
    }}}
    public void PostPresets(string selected) {{{
      Post(new { type = "presets", profile = FD.gProfileName, file = ((FD.gIniPath is null) ? "(none)" : FD.gIniPath) }); // CLAUDE
    }}}
    private void PostStatus() {{{
      DateTime now = DateTime.Now;
      double secs = Math.Max(0.001, (now - lastStatusTime).TotalSeconds);
      double rate = ((FD.gSyncCount - lastSyncCount) / secs);
      lastSyncCount = FD.gSyncCount; lastStatusTime = now;
      Post(new
      {
        type = "status",
        state = FD.gEmuState,
        hwnd = ((FD.gEmuHwnd == IntPtr.Zero) ? "-" : ("0x" + FD.gEmuHwnd.ToInt64().ToString("X"))),
        emu = ((FD.gEmuHwnd == IntPtr.Zero) ? "-" : FD.gLastE.ToString()),
        overlay = ((FD.gEmuHwnd == IntPtr.Zero) ? "-" : FD.gLastO.ToString()),
        frame = (FD.gFrameStripped ? "stripped" : "normal"),
        mode = "WinEvent + 16ms poll",
        syncsPerSec = Math.Round(rate, 1),
        winW = FD.gWinW, winH = FD.gWinH, profile = FD.gProfileName
      });
    }}}
  }

  public partial class FD
  {
    public static SettingsForm? gSettingsForm = null;
    public static bool gQuitting = false;
    public static void SettingsPushValue(string id) { gSettingsForm?.PostValue(id); }
    public static void SettingsShow() {{{
      if (gSettingsForm is null) { return; }
      if (gSettingsForm.WindowState == FormWindowState.Minimized) { gSettingsForm.WindowState = FormWindowState.Normal; }
      gSettingsForm.Show();
      Native.SetForegroundWindow(gSettingsForm.Handle);
    }}}
  }
}
