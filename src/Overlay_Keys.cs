// CLAUDE : new file. Keyboard input script -> trs80gp : WM_KEYDOWN / WM_KEYUP are posted to the emulator window (no focus needed).
// Unshifted keys only : trs80gp takes Shift from the real keyboard state, which posted messages cannot change.
// Script : one line = typed text (A-Z a-z 0-9 space) + Enter.  ; at the start of a line = comment.  {NAME [count]} special key, {KEY hex [count]} raw virtual key,
//   {DELAY ms} {CHARDELAY ms} {LINEDELAY ms} timing, {NOEOL} = no Enter at the end of this line.  Names : ENTER BREAK(Esc) UP DOWN LEFT RIGHT SPACE.
//   {KEYDOWN key} / {KEYUP key} (key = name or hex) hold / release a key ; a held key is released when the script ends or is cancelled. Add {NOEOL} or the line ends with Enter.
//   Flow : {LABEL name}  {GOTO name}  {LOOP name n} (jump back to name n times, then continue)  {REPEAT n} ... {END} (blocks nest).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FastDog
{
  public partial class FD
  {
    // FIELDS {{{
    public static int gKeyCharDelayMs = 40, gKeyLineDelayMs = 300, gKeyHoldMs = 60; // [Keys]
    private enum KeyOp { Key, Delay, CharDelay, LineDelay, Goto, Loop, Down, Up } // CLAUDE : Goto / Loop / Down / Up
    private sealed class KeyStep
    {
      public KeyOp Op;
      public int Value;   // virtual key, or ms, or (Loop) number of jumps back
      public int Target;  // CLAUDE : (Goto / Loop) index of the step to jump to
      public string Label = ""; // CLAUDE : (Goto / Loop) label name, resolved to Target after the parse
      public bool Eol;    // the Enter that ends a line : followed by the line delay instead of the character delay
      public int Line;    // 1-based line in the script
    }
    private static CancellationTokenSource? gKeysCts = null;
    private static string gKeysState = "idle"; // idle / running / done / cancelled / error
    private static string gKeysError = "";
    private static int gKeysLine = 0, gKeysLines = 0;
    private static readonly Dictionary<string, int> gKeyNames = new Dictionary<string, int>()
    {
      { "ENTER", 0x0D }, { "BREAK", 0x1B }, { "UP", 0x26 }, { "DOWN", 0x28 }, { "LEFT", 0x25 }, { "RIGHT", 0x27 }, { "SPACE", 0x20 },
    };
    // }}}

    // PARSE {{{
    private static int KeysInt(string s, string what, int line, int min, int max, bool hex) {{{
      string t = s.Trim();
      if (hex && t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { t = t.Substring(2); }
      if ((!int.TryParse(t, (hex ? NumberStyles.HexNumber : NumberStyles.Integer), CultureInfo.InvariantCulture, out int n)) || (n < min) || (n > max))
        { throw new PipeToolException($"Line {line} : bad {what} \"{s}\" (expected {min}..{max})"); }
      return n;
    }}}
    // Whole script -> steps ; any error is reported (line + column) before a single key is sent.
    private static List<KeyStep> KeysParse(string script) {{{
      List<KeyStep> steps = new List<KeyStep>();
      List<string> src = new List<string>(script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
      if ((src.Count > 0) && (src[src.Count - 1].Length == 0)) { src.RemoveAt(src.Count - 1); } // trailing newline is not an extra empty line
      gKeysLines = src.Count;
      Dictionary<string, int> labels = new Dictionary<string, int>(); // CLAUDE : label -> index of the next step
      List<(int Index, int Count, int Line)> repeats = new List<(int Index, int Count, int Line)>(); // CLAUDE : open {REPEAT n} blocks
      for (int i = 0; i < src.Count; i++)
        {
          int n = (i + 1);
          string line = src[i];
          if (line.StartsWith(";")) { continue; }
          bool noEol = false;
          int c = 0;
          while (c < line.Length)
            {
              char ch = line[c];
              if (ch == '{')
                {
                  int end = line.IndexOf('}', c);
                  if (end < 0) { throw new PipeToolException($"Line {n} col {(c + 1)} : missing }}"); }
                  string[] parts = line.Substring((c + 1), (end - c - 1)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                  if (parts.Length == 0) { throw new PipeToolException($"Line {n} col {(c + 1)} : empty {{}}"); }
                  string name = parts[0].ToUpperInvariant();
                  if (name == "NOEOL") { noEol = true; }
                  // CLAUDE : flow control. LABEL name / GOTO name / LOOP name n (jump back n times, then continue) / REPEAT n ... END
                  else if (name == "LABEL")
                    {
                      if (parts.Length < 2) { throw new PipeToolException($"Line {n} col {(c + 1)} : LABEL needs a name"); }
                      if (!labels.TryAdd(parts[1].ToUpperInvariant(), steps.Count)) { throw new PipeToolException($"Line {n} : duplicate label {parts[1]}"); }
                    }
                  else if ((name == "GOTO") || (name == "LOOP"))
                    {
                      if (parts.Length < ((name == "GOTO") ? 2 : 3)) { throw new PipeToolException($"Line {n} col {(c + 1)} : {name} needs " + ((name == "GOTO") ? "a label" : "a label and a count")); }
                      int count = ((name == "LOOP") ? KeysInt(parts[2], "count", n, 1, 1000000, false) : 0);
                      steps.Add(new KeyStep { Op = ((name == "GOTO") ? KeyOp.Goto : KeyOp.Loop), Value = count, Label = parts[1].ToUpperInvariant(), Line = n });
                    }
                  else if (name == "REPEAT")
                    {
                      if (parts.Length < 2) { throw new PipeToolException($"Line {n} col {(c + 1)} : REPEAT needs a count"); }
                      repeats.Add((steps.Count, KeysInt(parts[1], "count", n, 1, 1000000, false), n));
                    }
                  else if (name == "END")
                    {
                      if (repeats.Count == 0) { throw new PipeToolException($"Line {n} col {(c + 1)} : END without REPEAT"); }
                      (int Index, int Count, int Line) r = repeats[repeats.Count - 1];
                      repeats.RemoveAt(repeats.Count - 1);
                      if (r.Count > 1) { steps.Add(new KeyStep { Op = KeyOp.Loop, Value = (r.Count - 1), Target = r.Index, Line = n }); } // the first pass is the block itself
                    }
                  else if ((name == "DELAY") || (name == "CHARDELAY") || (name == "LINEDELAY"))
                    {
                      if (parts.Length < 2) { throw new PipeToolException($"Line {n} col {(c + 1)} : {name} needs milliseconds"); }
                      KeyOp op = ((name == "DELAY") ? KeyOp.Delay : ((name == "CHARDELAY") ? KeyOp.CharDelay : KeyOp.LineDelay));
                      steps.Add(new KeyStep { Op = op, Value = KeysInt(parts[1], "milliseconds", n, 0, 600000, false), Line = n });
                    }
                  // CLAUDE : {KEYDOWN key} / {KEYUP key} : key = a name (ENTER ...) or a hex virtual key. A key still down when the script ends or is cancelled is released.
                  else if ((name == "KEYDOWN") || (name == "KEYUP"))
                    {
                      if (parts.Length < 2) { throw new PipeToolException($"Line {n} col {(c + 1)} : {name} needs a key name or hex virtual key"); }
                      int dvk;
                      if (!gKeyNames.TryGetValue(parts[1].ToUpperInvariant(), out dvk)) { dvk = KeysInt(parts[1], "virtual key", n, 1, 0xFE, true); }
                      steps.Add(new KeyStep { Op = ((name == "KEYDOWN") ? KeyOp.Down : KeyOp.Up), Value = dvk, Line = n });
                    }
                  else
                    {
                      int vk, countAt;
                      if (name == "KEY")
                        {
                          if (parts.Length < 2) { throw new PipeToolException($"Line {n} col {(c + 1)} : KEY needs a hex virtual key"); }
                          vk = KeysInt(parts[1], "virtual key", n, 1, 0xFE, true); countAt = 2;
                        }
                      else if (gKeyNames.TryGetValue(name, out vk)) { countAt = 1; }
                      else { throw new PipeToolException($"Line {n} col {(c + 1)} : unknown key {{{parts[0]}}}"); }
                      int count = ((parts.Length > countAt) ? KeysInt(parts[countAt], "count", n, 1, 1000, false) : 1);
                      for (int k = 0; k < count; k++) { steps.Add(new KeyStep { Op = KeyOp.Key, Value = vk, Line = n }); }
                    }
                  c = (end + 1);
                  continue;
                }
              int code;
              if (((ch >= 'A') && (ch <= 'Z')) || ((ch >= '0') && (ch <= '9')) || (ch == ' ')) { code = ch; }
              else if ((ch >= 'a') && (ch <= 'z')) { code = (ch - 32); }
              else { throw new PipeToolException($"Line {n} col {(c + 1)} : unsupported character '{ch}' (use {{KEY hex}} for other keys)"); }
              steps.Add(new KeyStep { Op = KeyOp.Key, Value = code, Line = n });
              c++;
            }
          if (!noEol) { steps.Add(new KeyStep { Op = KeyOp.Key, Value = 0x0D, Eol = true, Line = n }); }
        }
      // CLAUDE : resolve the jumps ; unclosed blocks and unknown labels are errors
      if (repeats.Count > 0) { throw new PipeToolException($"Line {repeats[repeats.Count - 1].Line} : REPEAT without END"); }
      foreach (KeyStep s in steps)
        {
          if ((s.Op != KeyOp.Goto) && ((s.Op != KeyOp.Loop) || (s.Label.Length == 0))) { continue; }
          if (!labels.TryGetValue(s.Label, out int target)) { throw new PipeToolException($"Line {s.Line} : unknown label {s.Label}"); }
          s.Target = target;
        }
      return steps;
    }}}
    // }}}

    // SEND {{{
    private static IntPtr KeyLParam(int vk, bool up) {{{
      uint v = (1u | (Native.MapVirtualKeyW((uint) vk, 0) << 16));
      if ((vk >= 0x21) && (vk <= 0x2E)) { v |= (1u << 24); } // extended : arrows, home / end, ins / del
      if (up) { v |= ((1u << 30) | (1u << 31)); }
      return (IntPtr) unchecked((int) v);
    }}}
    private static async Task KeysRun(List<KeyStep> steps, int charDelay, int lineDelay, CancellationToken ct) {{{
      int held = 0; // key currently down : always released again (a stuck key auto-repeats)
      HashSet<int> downKeys = new HashSet<int>(); // CLAUDE : keys held by {KEYDOWN}, released by {KEYUP} or at the end of the script
      try
        {
          Dictionary<int, int> jumpsLeft = new Dictionary<int, int>(); // CLAUDE : per {LOOP} step : jumps still to do
          int at = 0;
          while (at < steps.Count)
            {
              ct.ThrowIfCancellationRequested();
              int idx = at;
              KeyStep s = steps[at++];
              gKeysLine = s.Line;
              switch (s.Op)
                {
                  case KeyOp.Goto: at = s.Target; await Task.Delay(1, ct); break; // the 1 ms keeps a key-less GOTO loop from spinning a core
                  case KeyOp.Loop:
                    {
                      if (!jumpsLeft.TryGetValue(idx, out int left)) { left = s.Value; }
                      if (left > 0) { jumpsLeft[idx] = (left - 1); at = s.Target; await Task.Delay(1, ct); }
                      else { jumpsLeft.Remove(idx); } // finished : a later visit starts the count again
                      break;
                    }
                  case KeyOp.Delay: await Task.Delay(s.Value, ct); break;
                  case KeyOp.CharDelay: charDelay = s.Value; break;
                  case KeyOp.LineDelay: lineDelay = s.Value; break;
                  case KeyOp.Down:
                  case KeyOp.Up:
                    {
                      IntPtr dh = gEmuHwnd;
                      if ((dh == IntPtr.Zero) || (!Native.IsWindow(dh))) { throw new InvalidOperationException("Emulator window is gone"); }
                      if (s.Op == KeyOp.Down) { if (downKeys.Add(s.Value)) { Native.PostMessageW(dh, Native.WM_KEYDOWN, (IntPtr) s.Value, KeyLParam(s.Value, false)); } }
                      else { if (downKeys.Remove(s.Value)) { Native.PostMessageW(dh, Native.WM_KEYUP, (IntPtr) s.Value, KeyLParam(s.Value, true)); } }
                      await Task.Delay(charDelay, ct);
                      break;
                    }
                  case KeyOp.Key:
                    {
                      IntPtr h = gEmuHwnd;
                      if ((h == IntPtr.Zero) || (!Native.IsWindow(h))) { throw new InvalidOperationException("Emulator window is gone"); }
                      held = s.Value;
                      Native.PostMessageW(h, Native.WM_KEYDOWN, (IntPtr) s.Value, KeyLParam(s.Value, false));
                      await Task.Delay(gKeyHoldMs, ct);
                      Native.PostMessageW(h, Native.WM_KEYUP, (IntPtr) s.Value, KeyLParam(s.Value, true));
                      held = 0;
                      await Task.Delay((s.Eol ? lineDelay : charDelay), ct);
                      break;
                    }
                }
            }
          gKeysState = "done";
        }
      catch (OperationCanceledException) { gKeysState = "cancelled"; }
      catch (Exception ex) { gKeysState = "error"; gKeysError = ex.Message; WL($"ERROR : [Keys] {ex.Message}\n"); }
      finally
        {
          IntPtr h = gEmuHwnd;
          if ((held != 0) && (h != IntPtr.Zero)) { Native.PostMessageW(h, Native.WM_KEYUP, (IntPtr) held, KeyLParam(held, true)); }
          if (h != IntPtr.Zero) { foreach (int k in downKeys) { Native.PostMessageW(h, Native.WM_KEYUP, (IntPtr) k, KeyLParam(k, true)); } } // CLAUDE
        }
    }}}
    // }}}

    // API {{{
    // Parse everything, then send in the background (one job at a time). charDelay / lineDelay < 0 = use the [Keys] settings.
    public static JsonObject KeysStart(string path, string text, int charDelay, int lineDelay) {{{
      if ((path.Length > 0) && (text.Length > 0)) { throw new PipeToolException("Give path or text, not both"); }
      if ((path.Length == 0) && (text.Length == 0)) { throw new PipeToolException("Missing argument : path or text"); }
      if (gEmuHwnd == IntPtr.Zero) { throw new PipeToolException("The emulator is not running"); }
      if (gKeysCts is not null) { throw new PipeToolException("A key script is already running : gpc_keys_cancel first"); }
      if (path.Length > 0)
        {
          if (!File.Exists(path)) { throw new PipeToolException("File not found : " + path); }
          text = File.ReadAllText(path);
        }
      List<KeyStep> steps = KeysParse(text);
      CancellationTokenSource cts = new CancellationTokenSource();
      gKeysCts = cts;
      gKeysState = "running"; gKeysError = ""; gKeysLine = 0;
      int cd = ((charDelay >= 0) ? charDelay : gKeyCharDelayMs), ld = ((lineDelay >= 0) ? lineDelay : gKeyLineDelayMs);
      _ = Task.Run(async () =>
      {
        await KeysRun(steps, cd, ld, cts.Token);
        gKeysCts = null; cts.Dispose();
      });
      return new JsonObject { ["started"] = true, ["steps"] = steps.Count };
    }}}
    public static void KeysCancel() {{{
      try { gKeysCts?.Cancel(); } catch (ObjectDisposedException) { }
    }}}
    public static JsonObject KeysStatus() {{{
      JsonObject r = new JsonObject { ["state"] = gKeysState, ["line"] = gKeysLine, ["lines"] = gKeysLines };
      if (gKeysError.Length > 0) { r["error"] = gKeysError; }
      return r;
    }}}
    // }}}
  }
}
