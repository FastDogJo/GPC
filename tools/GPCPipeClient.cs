// CLAUDE : Client helper for the GPC named pipe (not part of APP.csproj ; copy into the controlling program).
// Usage : foreach (int pid in GPCPipeClient.FindPids("GPC")) { using GPCPipeClient c = GPCPipeClient.Connect("GPC", pid); ... c.Call("tools/call", ...); }
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace FastDog
{
  public sealed class GPCPipeClient : IDisposable
  {
    private readonly NamedPipeClientStream pipe;
    private readonly StreamReader rd;
    private readonly StreamWriter wr;
    private int nextId = 1;
    private GPCPipeClient(NamedPipeClientStream p) {{{
      pipe = p;
      UTF8Encoding utf8 = new UTF8Encoding(false);
      rd = new StreamReader(p, utf8);
      wr = new StreamWriter(p, utf8) { AutoFlush = true, NewLine = "\n" };
    }}}
    // Pids of every running instance (process table ; pipe name = "<exe>.<pid>").
    public static List<int> FindPids(string exeName) {{{
      List<int> L = new List<int>();
      foreach (Process p in Process.GetProcessesByName(exeName)) { L.Add(p.Id); p.Dispose(); }
      return L;
    }}}
    public static GPCPipeClient Connect(string exeName, int pid, int timeoutMs = 500) {{{
      NamedPipeClientStream p = new NamedPipeClientStream(".", (exeName + "." + pid), PipeDirection.InOut, PipeOptions.None);
      try { p.Connect(timeoutMs); } catch { p.Dispose(); throw; }
      return new GPCPipeClient(p);
    }}}
    // JSON-RPC call ; returns "result" or throws on "error".
    public JsonNode? Call(string method, JsonObject? p = null) {{{
      JsonObject req = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = (nextId++), ["method"] = method, ["params"] = (p ?? new JsonObject()) };
      wr.WriteLine(req.ToJsonString());
      string? line = rd.ReadLine();
      if (line is null) { throw new IOException("pipe closed"); }
      JsonObject r = (JsonObject)JsonNode.Parse(line)!;
      if (r["error"] is JsonObject err) { throw new InvalidOperationException((string?)err["message"]); }
      return r["result"]?.DeepClone();
    }}}
    // Tool call ; returns the tool's JSON text.
    public string Tool(string name, JsonObject? args = null) {{{
      JsonNode? r = Call("tools/call", new JsonObject { ["name"] = name, ["arguments"] = (args ?? new JsonObject()) });
      string text = ((string?)r?["content"]?[0]?["text"] ?? "");
      if ((bool?)r?["isError"] == true) { throw new InvalidOperationException(text); }
      return text;
    }}}
    public void Dispose() {{{ pipe.Dispose(); }}}
  }
}
