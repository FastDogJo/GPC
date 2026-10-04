// CLAUDE : new file. Simple stand-in for FastDog FD_WL.cs : same WL("text\n") call style, timestamped, appended to a log file.
// Defaults are named after the exe (<ExeName>.log / <ExeName>.Last.log) and every field below can be overridden in the ini's [Log] section.
using System;
using System.IO;
using System.Text;

namespace FastDog
{
  public partial class FD
  {
    // FIELDS & PROPERTIES {{{
    public static readonly string ExeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "GPC");
    public static bool WLOutputToFile = true;
    public static string WLLogFileName = (ExeName + ".log");
    public static string WLFileNameLast = (ExeName + ".Last.log");
    public static string WLDefaultLogName = (ExeName + ".log"), WLDefaultLastName = (ExeName + ".Last.log"); // CLAUDE : follow the preset file ; the ini only stores a log name that differs from these
    public static int WLMaxLogFileSize = 524288; // bytes, 0 = never rotate
    public static bool WLShowTimeStamp = true;
    public static string WLTimeFormat = "HH:mm:ss.fff";
    private static readonly object WLFileLock = new object();
    // }}}
    private static string WLResolve(string name) { return (Path.IsPathRooted(name) ? name : Path.Combine(IniDir(), name)); } // CLAUDE : relative to the preset file
    public static void WL(string format, params object[] P) {{{
      try
        {
          string msg = (((P is not null) && (P.Length > 0)) ? string.Format(format, P) : format);
          string line = ((WLShowTimeStamp ? (DateTime.Now.ToString(WLTimeFormat) + " ") : "") + msg);
          System.Diagnostics.Debug.Write(line);
          if (!WLOutputToFile) { return; }
          lock (WLFileLock)
            {
              string file = WLResolve(WLLogFileName);
              FileInfo fi = new FileInfo(file);
              if ((WLMaxLogFileSize > 0) && fi.Exists && (fi.Length > WLMaxLogFileSize)) { File.Move(file, WLResolve(WLFileNameLast), true); }
              File.AppendAllText(file, line, Encoding.UTF8);
            }
        }
      catch { }
    }}}
    public static void WLClose() { } // nothing held open; kept so callers match FD_WL
  }
}
