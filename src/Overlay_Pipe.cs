// CLAUDE : new file. Named pipe server "<ExeName>.<pid>" (current user only). MCP-style protocol : newline-delimited JSON-RPC 2.0, one message per line.
// Methods : initialize, ping, tools/list, tools/call, resources/list, resources/read. Every tool runs on the UI thread.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FastDog
{
  public sealed class PipeRpcException : Exception
  {
    public int Code;
    public PipeRpcException(int code, string msg) : base(msg) { Code = code; }
  }
  public sealed class PipeToolException : Exception
  {
    public PipeToolException(string msg) : base(msg) { }
  }

  public partial class FD
  {
    // FIELDS & PROPERTIES {{{
    public static bool gPipeEnabled = true; // [Pipe] Enabled
    private static CancellationTokenSource? gPipeCts = null;
    private const string PipeProtocolVersion = "2025-06-18";
    // }}}

    public static string PipeName(int pid) {{{
      return (ExeName + "." + pid);
    }}}
    // CLAUDE : instance registry for an MCP relay (e.g. P:/SRC/C#/MCPPAR) : %LOCALAPPDATA%\MCPPipes\<app>.<pid>.json while the pipe is up. The relay removes the file of a crashed instance.
    private static string PipeRegistryFile() {{{
      return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCPPipes", (PipeName(Environment.ProcessId) + ".json")); // CLAUDE
    }}}
    private static void PipeRegistryWrite() {{{
      try
        {
          string file = PipeRegistryFile();
          Directory.CreateDirectory(Path.GetDirectoryName(file)!);
          File.WriteAllText(file, new JsonObject { ["app"] = ExeName, ["pid"] = Environment.ProcessId, ["pipe"] = PipeName(Environment.ProcessId) }.ToJsonString());
        }
      catch (Exception ex) { WL($"WARNING : [Pipe] registry write : {ex.Message}\n"); }
    }}}
    private static void PipeRegistryDelete() {{{
      try { File.Delete(PipeRegistryFile()); } catch { }
    }}}
    // Start / stop to match gPipeEnabled (startup, preset load, UI checkbox).
    public static void PipeApply() {{{
      if (gPipeEnabled && (gPipeCts is null)) { PipeStart(); }
      else if ((!gPipeEnabled) && (gPipeCts is not null)) { PipeStop(); }
    }}}
    public static void PipeStart() {{{
      if (gPipeCts is not null) { return; }
      CancellationTokenSource cts = new CancellationTokenSource();
      gPipeCts = cts;
      _ = Task.Run(() => PipeAcceptLoop(cts.Token));
      PipeRegistryWrite(); // CLAUDE
    }}}
    public static void PipeStop() {{{
      CancellationTokenSource? cts = gPipeCts;
      gPipeCts = null;
      if (cts is null) { return; }
      PipeRegistryDelete(); // CLAUDE
      try { cts.Cancel(); } catch { }
      WL("[Pipe] stopped\n");
    }}}

    // SERVER {{{
    private static async Task PipeAcceptLoop(CancellationToken ct) {{{
      string name = PipeName(Environment.ProcessId);
      WL($"[Pipe] listening on \\\\.\\pipe\\{name}\n");
      while (!ct.IsCancellationRequested)
        {
          NamedPipeServerStream? srv = null;
          try
            {
              srv = new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, (PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly));
              await srv.WaitForConnectionAsync(ct);
              NamedPipeServerStream conn = srv;
              srv = null;
              _ = Task.Run(() => PipeServeClient(conn, ct));
            }
          catch (OperationCanceledException) { }
          catch (Exception ex)
            {
              WL($"ERROR : [Pipe] accept : {ex.Message}\n");
              try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { }
            }
          finally { srv?.Dispose(); }
        }
    }}}
    private static async Task PipeServeClient(NamedPipeServerStream conn, CancellationToken ct) {{{
      try
        {
          using (conn)
            {
              UTF8Encoding utf8 = new UTF8Encoding(false);
              using StreamReader rd = new StreamReader(conn, utf8);
              StreamWriter wr = new StreamWriter(conn, utf8) { AutoFlush = true, NewLine = "\n" };
              while ((conn.IsConnected) && (!ct.IsCancellationRequested))
                {
                  string? line = await rd.ReadLineAsync(ct);
                  if (line is null) { break; }
                  if (line.Trim().Length == 0) { continue; }
                  string? reply = await PipeHandle(line);
                  if (reply is not null) { await wr.WriteLineAsync(reply); }
                }
            }
        }
      catch (OperationCanceledException) { }
      catch (IOException) { } // client went away
      catch (Exception ex) { WL($"ERROR : [Pipe] client : {ex.Message}\n"); }
    }}}
    // One request line -> one reply line (null for a notification).
    private static async Task<string?> PipeHandle(string line) {{{
      JsonNode? id = null;
      try
        {
          JsonObject? o = (JsonNode.Parse(line) as JsonObject);
          if (o is null) { return PipeError(null, -32600, "Invalid Request"); }
          id = o["id"]?.DeepClone();
          string method = ((string?)o["method"] ?? "");
          JsonObject p = ((o["params"] as JsonObject) ?? new JsonObject());
          if (id is null) { return null; } // notification (e.g. notifications/initialized) : no reply
          JsonNode result = await PipeDispatch(method, p);
          JsonObject r = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
          return r.ToJsonString();
        }
      catch (System.Text.Json.JsonException) { return PipeError(null, -32700, "Parse error"); }
      catch (PipeRpcException ex) { return PipeError(id, ex.Code, ex.Message); }
      catch (Exception ex) { WL($"ERROR : [Pipe] request : {ex.Message}\n"); return PipeError(id, -32603, ex.Message); }
    }}}
    private static string PipeError(JsonNode? id, int code, string msg) {{{
      JsonObject r = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = msg } };
      return r.ToJsonString();
    }}}
    private static async Task<JsonNode> PipeDispatch(string method, JsonObject p) {{{
      switch (method)
        {
          case "initialize": return await PipeOnUi(PipeInitResult);
          case "ping": return new JsonObject();
          case "tools/list": return new JsonObject { ["tools"] = PipeToolList() };
          case "tools/call":
            {
              string name = ((string?)p["name"] ?? "");
              JsonObject args = ((p["arguments"] as JsonObject) ?? new JsonObject());
              try
                {
                  JsonNode data = await PipeOnUi(() => PipeToolRun(name, args));
                  return PipeToolResult(data.ToJsonString(), false);
                }
              catch (PipeToolException ex) { return PipeToolResult(ex.Message, true); }
            }
          case "resources/list":
            return new JsonObject
            {
              ["resources"] = new JsonArray
              {
                new JsonObject { ["uri"] = "gpc://status", ["name"] = "status", ["description"] = "Emulator / overlay state", ["mimeType"] = "application/json" },
                new JsonObject { ["uri"] = "gpc://settings", ["name"] = "settings", ["description"] = "Every setting id and its current value", ["mimeType"] = "application/json" },
              }
            };
          case "resources/read":
            {
              string uri = ((string?)p["uri"] ?? "");
              JsonNode data;
              if (uri == "gpc://status") { data = await PipeOnUi(PipeStatus); }
              else if (uri == "gpc://settings") { data = await PipeOnUi(() => PipeSettingsList("")); }
              else { throw new PipeRpcException(-32002, ("Unknown resource " + uri)); }
              return new JsonObject { ["contents"] = new JsonArray { new JsonObject { ["uri"] = uri, ["mimeType"] = "application/json", ["text"] = data.ToJsonString() } } };
            }
          default: throw new PipeRpcException(-32601, ("Method not found : " + method));
        }
    }}}
    private static JsonNode PipeToolResult(string text, bool isError) {{{
      return new JsonObject { ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } }, ["isError"] = isError };
    }}}
    // Run f on the UI thread (settings / renderer / windows are not thread safe).
    private static Task<JsonNode> PipeOnUi(Func<JsonNode> f) {{{
      SettingsForm? form = gSettingsForm;
      if ((form is null) || form.IsDisposed || (!form.IsHandleCreated)) { throw new PipeRpcException(-32000, "GPC is not ready or is shutting down"); }
      TaskCompletionSource<JsonNode> tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
      form.BeginInvoke(new Action(() => { try { tcs.SetResult(f()); } catch (Exception ex) { tcs.SetException(ex); } }));
      return tcs.Task;
    }}}
    // }}}

    // DATA (UI thread) {{{
    private static JsonNode PipeInitResult() {{{
      return new JsonObject
      {
        ["protocolVersion"] = PipeProtocolVersion,
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject(), ["resources"] = new JsonObject() },
        ["serverInfo"] = new JsonObject { ["name"] = ExeName, ["version"] = "1.0" },
        ["instance"] = new JsonObject
        {
          ["pid"] = Environment.ProcessId,
          ["pipe"] = PipeName(Environment.ProcessId),
          ["preset"] = gIniPath,
          ["profile"] = gProfileName,
          ["description"] = gDescription,
        },
      };
    }}}
    private static JsonNode PipeStatus() {{{
      return new JsonObject
      {
        ["state"] = gEmuState,
        ["hwnd"] = ((gEmuHwnd == IntPtr.Zero) ? "-" : ("0x" + gEmuHwnd.ToInt64().ToString("X"))),
        ["emu"] = ((gEmuHwnd == IntPtr.Zero) ? "-" : gLastE.ToString()),
        ["overlay"] = ((gEmuHwnd == IntPtr.Zero) ? "-" : gLastO.ToString()),
        ["frame"] = (gFrameStripped ? "stripped" : "normal"),
        ["overlayVisible"] = gOverlayVisible,
        ["monitorOn"] = gPower.IsOn,
        ["winW"] = gWinW,
        ["winH"] = gWinH,
        ["profile"] = gProfileName,
        ["preset"] = gIniPath,
        ["dirty"] = SettingsDirty(),
        ["pid"] = Environment.ProcessId,
      };
    }}}
    private static JsonNode PipeSettingsList(string section) {{{
      JsonArray arr = new JsonArray();
      foreach (SettingEntry e in gSettingTable)
        {
          if ((section.Length > 0) && (e.Section != section)) { continue; }
          arr.Add(new JsonObject { ["id"] = e.Id, ["value"] = SettingsUiValue(e), ["profile"] = e.Profile });
        }
      return arr;
    }}}
    // }}}

    // TOOLS {{{
    private static JsonObject PipeTool(string name, string desc, params (string Name, string Type, string Desc, bool Required)[] props) {{{
      JsonObject properties = new JsonObject();
      JsonArray required = new JsonArray();
      foreach (var pr in props)
        {
          properties[pr.Name] = new JsonObject { ["type"] = pr.Type, ["description"] = pr.Desc };
          if (pr.Required) { required.Add(pr.Name); }
        }
      JsonObject schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
      if (required.Count > 0) { schema["required"] = required; }
      return new JsonObject { ["name"] = name, ["description"] = desc, ["inputSchema"] = schema };
    }}}
    private static JsonArray PipeToolList() {{{
      return new JsonArray
      {
        PipeTool("gpc_get_status", "Emulator / overlay state, current preset and profile, unsaved-changes flag."),
        PipeTool("gpc_list_settings", "All settings (id = Section.Key, current value as the UI shows it, profile = stored per bezel profile).", ("section", "string", "Optional section filter, e.g. Look, Glow, Power, Launch", false)),
        PipeTool("gpc_get_setting", "Current value of one setting.", ("id", "string", "Section.Key", true)),
        PipeTool("gpc_set_setting", "Set one setting (live-applied, same as the settings window). Values are strings: numbers as text, booleans as true/false. Returns the value now in effect.", ("id", "string", "Section.Key", true), ("value", "string", "New value", true)),
        PipeTool("gpc_preset_load", "Load a .gpc preset file and relaunch the emulator with it.", ("path", "string", "Preset file path", true), ("discard", "boolean", "Discard unsaved changes (otherwise refused when there are any)", false)),
        PipeTool("gpc_preset_save", "Save the current preset file (fails when no preset file is set; use gpc_preset_save_as)."),
        PipeTool("gpc_preset_save_as", "Save the settings to a new .gpc file and make it the current preset.", ("path", "string", "Preset file path", true)),
        PipeTool("gpc_toggle_frame", "Strip / restore the emulator frame and menu."),
        PipeTool("gpc_toggle_overlay", "Toggle the overlay."),
        PipeTool("gpc_relaunch", "Kill and restart the emulator."),
        PipeTool("gpc_power_click", "Click the power rect: single = CRT power animation only, double = animation plus emulator pause (off) / cold restart (on). Toggles the monitor.", ("double", "boolean", "Double-click instead of single click", false)),
        PipeTool("gpc_power_set", "Switch the monitor on or off (no-op when already in that state).", ("on", "boolean", "true = on, false = off", true), ("full", "boolean", "Also pause / cold restart the emulator (double-click behaviour)", false)),
        PipeTool("gpc_reset_power", "Reset the power rectangle."),
        PipeTool("gpc_show_settings", "Show the settings window."),
        // CLAUDE : keyboard input script (see Overlay_Keys.cs)
        PipeTool("gpc_send_keys", "Type a script into the emulator, in the background (poll gpc_keys_status). Unshifted keys only. One line = text (A-Z 0-9 space) + Enter; ';' at line start = comment; {ENTER} {BREAK} {UP} {DOWN} {LEFT} {RIGHT} {SPACE} [count], {KEY hex [count]} raw virtual key, {DELAY ms} {CHARDELAY ms} {LINEDELAY ms}, {NOEOL} = no Enter for this line, {LABEL name} {GOTO name} {LOOP name n} (jump back to name n times then continue) {REPEAT n}...{END} (nestable). Whole script is validated before any key is sent.", ("path", "string", "Script file path (read by GPC)", false), ("text", "string", "Script text (instead of path)", false), ("charDelayMs", "integer", "Delay after each key (default [Keys] CharDelayMs)", false), ("lineDelayMs", "integer", "Delay after each line's Enter (default [Keys] LineDelayMs)", false)),
        PipeTool("gpc_keys_status", "State of the key script : idle / running / done / cancelled / error, current line, error text."),
        PipeTool("gpc_keys_cancel", "Stop the running key script (any held key is released)."),
        PipeTool("gpc_exit", "Quit GPC (closes the emulator per Launch.CloseEmulatorOnExit).", ("discard", "boolean", "Discard unsaved changes (otherwise refused when there are any)", false)),
      };
    }}}
    private static string PipeArg(JsonObject a, string key, bool required) {{{
      string? v = null;
      try { v = a[key]?.ToString(); } catch { }
      if (required && string.IsNullOrEmpty(v)) { throw new PipeToolException("Missing argument : " + key); }
      return (v ?? "");
    }}}
    private static bool PipeArgBool(JsonObject a, string key) {{{
      return (PipeArg(a, key, false).ToLowerInvariant() == "true");
    }}}
    // CLAUDE : integer argument, -1 when absent
    private static int PipeArgInt(JsonObject a, string key) {{{
      string v = PipeArg(a, key, false);
      if (v.Length == 0) { return -1; }
      if ((!int.TryParse(v, out int n)) || (n < 0)) { throw new PipeToolException("Bad argument : " + key); }
      return n;
    }}}
    private static void PipeNeedClean(JsonObject a) {{{
      if (PipeArgBool(a, "discard")) { SettingsMarkClean(); return; }
      if (SettingsDirty()) { throw new PipeToolException("Unsaved changes : save first (gpc_preset_save) or pass discard=true"); }
    }}}
    private static JsonNode PipeToolRun(string name, JsonObject a) {{{
      switch (name)
        {
          case "gpc_get_status": return PipeStatus();
          case "gpc_list_settings": return PipeSettingsList(PipeArg(a, "section", false));
          case "gpc_get_setting":
            {
              SettingEntry? e = SettingsFind(PipeArg(a, "id", true));
              if (e is null) { throw new PipeToolException("Unknown setting : " + PipeArg(a, "id", true)); }
              return new JsonObject { ["id"] = e.Id, ["value"] = SettingsUiValue(e) };
            }
          case "gpc_set_setting":
            {
              string id = PipeArg(a, "id", true);
              SettingEntry? e = SettingsFind(id);
              if (e is null) { throw new PipeToolException("Unknown setting : " + id); }
              if (!SettingsSetById(id, PipeArg(a, "value", false))) { throw new PipeToolException("Value rejected for " + id); }
              SettingsPushValue(id);
              return new JsonObject { ["id"] = e.Id, ["value"] = SettingsUiValue(e) };
            }
          case "gpc_preset_load":
            {
              string path = PipeArg(a, "path", true);
              if (!File.Exists(path)) { throw new PipeToolException("File not found : " + path); }
              PipeNeedClean(a);
              PresetLoadFile(path); // async void : returns once the relaunch is under way
              return new JsonObject { ["started"] = true };
            }
          case "gpc_preset_save":
            {
              if (gIniPath is null) { throw new PipeToolException("No preset file yet : use gpc_preset_save_as"); }
              PresetSave();
              return new JsonObject { ["saved"] = gIniPath };
            }
          case "gpc_preset_save_as":
            {
              PresetSaveAsFile(PipeArg(a, "path", true));
              return new JsonObject { ["saved"] = gIniPath };
            }
          case "gpc_toggle_frame": FrameToggle(); return new JsonObject { ["frame"] = (gFrameStripped ? "stripped" : "normal") };
          case "gpc_toggle_overlay": OverlayToggle(); return new JsonObject { ["overlayVisible"] = gOverlayVisible };
          case "gpc_relaunch": _ = RelaunchAsync(); return new JsonObject { ["started"] = true };
          case "gpc_power_click": PowerClick(PipeArgBool(a, "double")); return new JsonObject { ["monitorOn"] = gPower.IsOn };
          case "gpc_power_set": PowerSet(PipeArgBool(a, "on"), PipeArgBool(a, "full")); return new JsonObject { ["monitorOn"] = gPower.IsOn };
          case "gpc_reset_power": PowerRectReset(); return new JsonObject { ["done"] = true };
          case "gpc_show_settings": SettingsShow(); return new JsonObject { ["done"] = true };
          // CLAUDE : key script
          case "gpc_send_keys": return KeysStart(PipeArg(a, "path", false), PipeArg(a, "text", false), PipeArgInt(a, "charDelayMs"), PipeArgInt(a, "lineDelayMs"));
          case "gpc_keys_status": return KeysStatus();
          case "gpc_keys_cancel": KeysCancel(); return new JsonObject { ["cancelling"] = true };
          case "gpc_exit":
            {
              PipeNeedClean(a);
              Task.Delay(200).ContinueWith((t) => { try { gSettingsForm?.BeginInvoke(new Action(AppQuit)); } catch { } }); // let the reply go out first
              return new JsonObject { ["quitting"] = true };
            }
          default: throw new PipeToolException("Unknown tool : " + name);
        }
    }}}
    // }}}
  }
}
