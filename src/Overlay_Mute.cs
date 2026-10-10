// CLAUDE : new file. Mutes / unmutes the emulator process's audio session (Core Audio, per-process: the rest of the system is untouched).
using System;
using System.Runtime.InteropServices;

namespace FastDog
{
  public partial class FD
  {
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumeratorCom { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
      int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
      int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
      int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
      int GetAudioSessionControl(IntPtr sessionGuid, int streamFlags, out IntPtr control);
      int GetSimpleAudioVolume(IntPtr sessionGuid, int streamFlags, out IntPtr volume);
      int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
      int GetCount(out int count);
      int GetSession(int index, out IAudioSessionControl2 session);
    }
    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
      int GetState(out int state);
      int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
      int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid ctx);
      int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
      int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid ctx);
      int GetGroupingParam(out Guid param);
      int SetGroupingParam(ref Guid param, ref Guid ctx);
      int RegisterAudioSessionNotification(IntPtr client);
      int UnregisterAudioSessionNotification(IntPtr client);
      int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
      int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
      int GetProcessId(out uint pid);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
      int SetMasterVolume(float level, ref Guid ctx);
      int GetMasterVolume(out float level);
      int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
      int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private static bool gMutedByPower = false;

    // Mute / unmute every audio session owned by the emulator process.
    public static void EmuAudioMute(bool mute) {{{
      gMutedByPower = mute; // wanted state : PowerTick re-applies it, since the emulator's audio session / pid may not exist yet (start-off) or may be created later
      if (gEmuPid == 0) { return; }
      try
        {
          IMMDeviceEnumerator en = (IMMDeviceEnumerator) new MMDeviceEnumeratorCom();
          IMMDevice dev; en.GetDefaultAudioEndpoint(0, 1, out dev); // eRender, eMultimedia
          Guid iid = typeof(IAudioSessionManager2).GUID; object o;
          dev.Activate(ref iid, 23, IntPtr.Zero, out o); // CLSCTX_ALL
          IAudioSessionEnumerator se; ((IAudioSessionManager2) o).GetSessionEnumerator(out se);
          int n; se.GetCount(out n);
          Guid ctx = Guid.Empty;
          for (int i = 0; (i < n); i++)
            {
              IAudioSessionControl2 sc; se.GetSession(i, out sc);
              uint pid; sc.GetProcessId(out pid);
              if (pid != gEmuPid) { continue; }
              ((ISimpleAudioVolume) sc).SetMute(mute, ref ctx);
            }
        }
      catch (Exception ex) { WL($"[Power] audio {(mute ? "mute" : "unmute")} failed : {ex.Message}\n"); }
    }}}
  }
}
