// CLAUDE : new file. PHASE 2 : Windows.Graphics.Capture of the emulator HWND -> an ID3D11Texture2D we own.
// The capture is of the WINDOW (not the screen), so our own overlay windows on top of it never get captured (no recursion).
using System;
using System.Runtime.InteropServices;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using IDirect3DDevice = Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice;
using IDirect3DSurface = Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface;

namespace FastDog
{
  [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  internal interface IGraphicsCaptureItemInterop
  {
    IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
    IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
  }
  [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  internal interface IDirect3DDxgiInterfaceAccess
  {
    IntPtr GetInterface([In] ref Guid iid);
  }

  public unsafe delegate void FrameCopy(ID3D11Texture2D* tex, int w, int h);

  public sealed unsafe class WgcCapture : IDisposable
  {
    // FIELDS & PROPERTIES {{{
    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
    [DllImport("combase.dll")] private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string src, int length, out IntPtr hstring);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

    private static Guid IID_IGraphicsCaptureItem = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static Guid IID_IGraphicsCaptureItemInterop = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static Guid IID_IDXGIDevice = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    private static Guid IID_ID3D11Texture2D = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    private readonly ID3D11Device* device;
    private IDirect3DDevice? winrtDevice = null;
    private GraphicsCaptureItem? item = null;
    private Direct3D11CaptureFramePool? pool = null;
    private GraphicsCaptureSession? session = null;
    private int poolW = 0, poolH = 0;
    public bool Closed = false;
    public int FrameW = 0, FrameH = 0; // size of the last delivered frame
    // }}}

    public WgcCapture(ID3D11Device* dev) { device = dev; }

    // Starts capturing hwnd. Returns false (and logs) when capture is unavailable.
    public bool Start(IntPtr hwnd) {{{
      try
        {
          if (!GraphicsCaptureSession.IsSupported()) { FD.WL("ERROR : [P2] Windows.Graphics.Capture is not supported on this system\n"); return false; }
          // IDirect3DDevice (WinRT) from our D3D11 device
          IDXGIDevice* dxgi = null;
          Guid gDxgi = IID_IDXGIDevice;
          device->QueryInterface(&gDxgi, (void**) &dxgi);
          IntPtr rawDev;
          int hr = CreateDirect3D11DeviceFromDXGIDevice((IntPtr) dxgi, out rawDev);
          dxgi->Release();
          if (hr < 0) { FD.WL($"ERROR : [P2] CreateDirect3D11DeviceFromDXGIDevice 0x{hr:X}\n"); return false; }
          winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(rawDev);
          Marshal.Release(rawDev);
          // GraphicsCaptureItem for the window
          IntPtr hs; WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem", "Windows.Graphics.Capture.GraphicsCaptureItem".Length, out hs);
          IntPtr factory; Guid gi = IID_IGraphicsCaptureItemInterop;
          hr = RoGetActivationFactory(hs, ref gi, out factory);
          WindowsDeleteString(hs);
          if (hr < 0) { FD.WL($"ERROR : [P2] RoGetActivationFactory 0x{hr:X}\n"); return false; }
          IGraphicsCaptureItemInterop interop = (IGraphicsCaptureItemInterop) Marshal.GetObjectForIUnknown(factory);
          Guid gItem = IID_IGraphicsCaptureItem;
          IntPtr rawItem = interop.CreateForWindow(hwnd, ref gItem);
          Marshal.Release(factory);
          item = WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(rawItem);
          Marshal.Release(rawItem);
          item.Closed += (s, e) => { Closed = true; };
          poolW = item.Size.Width; poolH = item.Size.Height;
          pool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
          session = pool.CreateCaptureSession(item);
          try { session.IsCursorCaptureEnabled = false; } catch { }
          try { session.IsBorderRequired = false; } catch (Exception ex) { FD.WL($"WARNING : [P2] capture border cannot be disabled : {ex.Message}\n"); }
          session.StartCapture();
          FD.WL($"[P2] capture started {poolW}x{poolH}\n");
          return true;
        }
      catch (Exception ex) { FD.WL($"ERROR : [P2] capture start : {ex}\n"); return false; }
    }}}

    // Copies the newest frame (if any) into dst (created/sized by the caller via EnsureScene). Returns true when a new frame was copied.
    // The callback gets the frame's texture so the caller can create/resize its scene texture and CopyResource.
    public bool TryGetFrame(FrameCopy copy) {{{
      if (pool is null) { return false; }
      Direct3D11CaptureFrame? frame = null;
      Direct3D11CaptureFrame? last = null;
      while ((frame = pool.TryGetNextFrame()) is not null) { last?.Dispose(); last = frame; } // drain, keep the newest
      if (last is null) { return false; }
      try
        {
          int w = last.ContentSize.Width, h = last.ContentSize.Height;
          IntPtr surfPtr = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(last.Surface);
          IDirect3DDxgiInterfaceAccess access = (IDirect3DDxgiInterfaceAccess) Marshal.GetObjectForIUnknown(surfPtr);
          Guid gTex = IID_ID3D11Texture2D;
          IntPtr tex = access.GetInterface(ref gTex);
          FrameW = w; FrameH = h;
          copy((ID3D11Texture2D*) tex, w, h);
          Marshal.Release(tex);
          Marshal.Release(surfPtr);
          if ((w != poolW) || (h != poolH)) { poolW = w; poolH = h; pool.Recreate(winrtDevice, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, new Windows.Graphics.SizeInt32 { Width = w, Height = h }); }
          return true;
        }
      finally { last.Dispose(); }
    }}}

    public void Dispose() {{{
      try { session?.Dispose(); } catch { }
      try { pool?.Dispose(); } catch { }
      session = null; pool = null; item = null; winrtDevice = null;
    }}}
  }
}
