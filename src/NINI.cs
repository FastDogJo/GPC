using System;

namespace FastDog
{
  public partial class FD
  {
    // FIELDS & PROPERTIES {{{
    public static bool NINIFound { get; set; } = false;
    public static string NINIFileName { get; set; } = "";
    public static Nini.Config.IConfigSource? NINISource = null;
    // }}}
    // Opens fileName if it exists (NINISource stays null otherwise, NINIFileName is always set).
    public static void NINISetup(string fileName) {{{
      NINIFileName = fileName;
      NINIFound = System.IO.File.Exists(fileName);
      NINISource = (NINIFound ? new Nini.Config.IniConfigSource(fileName) : null);
    }}}
    // CLAUDE : independent ini files (per-model profiles / presets) : the main NINISource is left alone.
    public static Nini.Config.IniConfigSource? NINIOpenFile(string path) {{{
      return (System.IO.File.Exists(path) ? new Nini.Config.IniConfigSource(path) : null);
    }}}
    public static void NINISetIn(Nini.Config.IConfigSource src, string section, string key, string value) {{{
      Nini.Config.IConfig cfg = src.Configs[section] ?? src.AddConfig(section);
      cfg.Set(key, value);
    }}}
    // Single-key writer. Creates the file/section if absent; saves to disk unless save:false (caller then calls NINISource.Save()).
    public static void NINIWriteValue(string objectName, string key, string value, bool save = true) {{{
      try
        {
          if (NINISource is null)
            {
              if (string.IsNullOrEmpty(NINIFileName)) { return; }
              new Nini.Config.IniConfigSource().Save(NINIFileName); // create empty file so Save() has a path
              NINISource = new Nini.Config.IniConfigSource(NINIFileName);
              NINIFound = true;
            }
          Nini.Config.IConfig cfg = NINISource.Configs[objectName] ?? NINISource.AddConfig(objectName);
          cfg.Set(key, value);
          if (save) { NINISource.Save(); }
        }
      catch (Exception ex) { WL($"ERROR : [NINI] NINIWriteValue {objectName}.{key} : {ex.Message}\n"); }
    }}}
  }
}
