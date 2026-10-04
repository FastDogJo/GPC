// CLAUDE : new file. Phase 1 renderers (CPU + UpdateLayeredWindow). Everything goes through IOverlayRenderer so a
// Phase 2 D3D11 / Windows.Graphics.Capture renderer can replace these without touching tracking/sync code.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace FastDog
{
  public interface IOverlayRenderer
  {
    void Resize(NRect r); // new screen rect for the window (re-renders only when the size changed)
    void SetLook();       // a look setting changed
    void Render();        // force redraw
  }

  // Top-down 32bpp premultiplied DIB that is pushed to a layered window with UpdateLayeredWindow.
  public sealed class LayeredSurface : IDisposable
  {
    public IntPtr Bits = IntPtr.Zero;
    public int W, H;
    private IntPtr hdc = IntPtr.Zero, hbmp = IntPtr.Zero, old = IntPtr.Zero;
    public LayeredSurface(int w, int h) {{{
      W = w; H = h;
      IntPtr screen = Native.GetDC(IntPtr.Zero);
      hdc = Native.CreateCompatibleDC(screen);
      NBitmapInfoHeader bi = new NBitmapInfoHeader { Size = (uint) System.Runtime.InteropServices.Marshal.SizeOf<NBitmapInfoHeader>(), Width = w, Height = -h, Planes = 1, BitCount = 32 };
      hbmp = Native.CreateDIBSection(screen, ref bi, 0, out Bits, IntPtr.Zero, 0);
      old = Native.SelectObject(hdc, hbmp);
      Native.ReleaseDC(IntPtr.Zero, screen);
    }}}
    public void Present(IntPtr hwnd, int x, int y) {{{
      IntPtr screen = Native.GetDC(IntPtr.Zero);
      NPoint dst = new NPoint { X = x, Y = y };
      NSize size = new NSize { W = W, H = H };
      NPoint src = new NPoint();
      NBlend blend = new NBlend { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
      Native.UpdateLayeredWindow(hwnd, screen, ref dst, ref size, hdc, ref src, 0, ref blend, Native.ULW_ALPHA);
      Native.ReleaseDC(IntPtr.Zero, screen);
    }}}
    public void Dispose() {{{
      if (hdc != IntPtr.Zero) { Native.SelectObject(hdc, old); Native.DeleteObject(hbmp); Native.DeleteDC(hdc); hdc = IntPtr.Zero; }
    }}}
  }

  // Bezel PNG scaled to the overlay window.
  public sealed class BezelRenderer : IOverlayRenderer
  {
    private readonly IntPtr hwnd;
    private LayeredSurface? surf = null;
    private NRect rect;
    public bool Fast = false; // cheaper interpolation while the user is dragging a size
    public BezelRenderer(IntPtr h) { hwnd = h; }
    public void Resize(NRect r) {{{
      bool sizeChanged = ((surf is null) || (r.Width != rect.Width) || (r.Height != rect.Height));
      rect = r;
      if (sizeChanged) { Render(); }
      else { Native.SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, 0, 0, (Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE)); }
    }}}
    public void SetLook() { Render(); }
    public void Render() {{{
      if ((!BezelModel.Loaded) || (rect.Width < 8) || (rect.Height < 8)) { return; }
      if ((surf is null) || (surf.W != rect.Width) || (surf.H != rect.Height)) { surf?.Dispose(); surf = new LayeredSurface(rect.Width, rect.Height); }
      using (Bitmap target = new Bitmap(surf.W, surf.H, (surf.W * 4), PixelFormat.Format32bppPArgb, surf.Bits))
      using (Graphics g = Graphics.FromImage(target))
      using (ImageAttributes ia = new ImageAttributes())
        {
          ia.SetWrapMode(WrapMode.TileFlipXY);
          if (FD.gBezelBrightness < 100) { float o = (FD.gBezelBrightness / 100f); ia.SetColorMatrix(new ColorMatrix(new float[][] { new float[] { o, 0, 0, 0, 0 }, new float[] { 0, o, 0, 0, 0 }, new float[] { 0, 0, o, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 } })); } // CLAUDE : colour only (premultiplied rgb), alpha untouched
          g.CompositingMode = CompositingMode.SourceCopy;
          g.InterpolationMode = (Fast ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic);
          g.PixelOffsetMode = PixelOffsetMode.Half;
          g.Clear(Color.Transparent);
          g.DrawImage(BezelModel.Image!, new Rectangle(0, 0, surf.W, surf.H), 0, 0, BezelModel.Width, BezelModel.Height, GraphicsUnit.Pixel, ia);
          if (FD.gPowerShowRect) // CLAUDE : tuning aid - red outline + faint fill over the power rect
            {
              RectangleF pr = FD.PowerRectEffective();
              RectangleF px = new RectangleF(((pr.X * surf.W) / 100f), ((pr.Y * surf.H) / 100f), ((pr.Width * surf.W) / 100f), ((pr.Height * surf.H) / 100f));
              g.CompositingMode = CompositingMode.SourceOver;
              using (SolidBrush fill = new SolidBrush(Color.FromArgb(40, 255, 51, 51))) { g.FillRectangle(fill, px); }
              using (Pen pen = new Pen(Color.FromArgb(255, 255, 51, 51), 2f)) { g.DrawRectangle(pen, px.X, px.Y, px.Width, px.Height); }
            }
        }
      if (FD.gBlackBehind) { FillBehindScreen(); } // CLAUDE
      BlockOpeningClicks(); // CLAUDE
      surf.Present(hwnd, rect.Left, rect.Top);
    }}}
    // CLAUDE : Screen fit < 100% leaves part of the original (detected) opening uncovered by the emulator, so the desktop shows through the bezel's alpha-0 pixels.
    // Inside the detected opening and outside the emulator rect : composite over black (premultiplied rgb unchanged, alpha -> 255).
    private unsafe void FillBehindScreen() {{{
      double s = ((double) surf!.W / (double) BezelModel.Width);
      int grey = FD.BehindGrey();
      const int m = 3; // CLAUDE : bicubic scaling smears the opening's anti-aliased edge a pixel or two outside the detected box - harmless on opaque art
      int dl = Math.Max(0, ((int) Math.Floor((BezelModel.detL * s)) - m)), dr = Math.Min(surf.W, ((int) Math.Ceiling((BezelModel.detR * s)) + m));
      int dt = Math.Max(0, ((int) Math.Floor((BezelModel.detT * s)) - m)), db = Math.Min(surf.H, ((int) Math.Ceiling((BezelModel.detB * s)) + m));
      NRect c = BezelModel.ToClient(rect);
      int cl = (c.Left - rect.Left), cr = (c.Right - rect.Left), ct = (c.Top - rect.Top), cb = (c.Bottom - rect.Top);
      for (int y = dt; y < db; y++)
        {
          byte* row = ((byte*) surf.Bits + ((long) y * surf.W * 4));
          bool rowInEmu = ((y >= ct) && (y < cb));
          for (int x = dl; x < dr; x++)
            {
              if (rowInEmu && (x >= cl) && (x < cr)) { x = (cr - 1); continue; } // skip the emulator rect
              byte a = row[(x * 4) + 3];
              if (a == 255) { continue; }
              if (grey > 0) { for (int k = 0; k < 3; k++) { row[(x * 4) + k] = (byte) Math.Min(255, (row[(x * 4) + k] + ((grey * (255 - a)) / 255))); } } // premultiplied "over grey"
              row[(x * 4) + 3] = 255;
            }
        }
    }}}
    // CLAUDE : a layered window passes mouse input through alpha-0 pixels, so give the screen opening alpha 1 (invisible) : clicks / drags there hit the bezel (move the window) instead of the emulator.
    private unsafe void BlockOpeningClicks() {{{
      NRect c = BezelModel.ToClient(rect);
      int x0 = Math.Max(0, (c.Left - rect.Left)), x1 = Math.Min(surf!.W, (c.Right - rect.Left)), y0 = Math.Max(0, (c.Top - rect.Top)), y1 = Math.Min(surf.H, (c.Bottom - rect.Top));
      for (int y = y0; y < y1; y++)
        {
          byte* row = ((byte*) surf.Bits + ((long) y * surf.W * 4));
          for (int x = x0; x < x1; x++) { if (row[(x * 4) + 3] == 0) { row[(x * 4) + 3] = 1; } }
        }
    }}}
  }

  // Dim / scanlines / vignette / tint : one black+tint alpha layer over the emulator's client area (click-through window).
  public sealed class DimRenderer : IOverlayRenderer
  {
    private readonly IntPtr hwnd;
    private LayeredSurface? surf = null;
    private NRect rect;
    public DimRenderer(IntPtr h) { hwnd = h; }
    // CLAUDE : power animation "freeze" : snapshot (premultiplied BGRA, client-area sized) of the emulator picture, shown instead of the live window while the animation runs
    private byte[]? freeze = null;
    private int freezeW = 0, freezeH = 0;
    public void FreezeBegin() {{{
      freeze = null;
      try
        {
          NRect w; Native.GetWindowRect(FD.gEmuHwnd, out w);
          NRect c = Native.ClientScreenRect(FD.gEmuHwnd);
          if ((w.Width < 2) || (w.Height < 2) || (c.Width < 2) || (c.Height < 2)) { return; }
          using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w.Width, w.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
              using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                {
                  IntPtr hdc = g.GetHdc();
                  bool ok = Native.PrintWindow(FD.gEmuHwnd, hdc, 2); // PW_RENDERFULLCONTENT
                  g.ReleaseHdc(hdc);
                  if (!ok) { FD.WL("WARNING : [Power] freeze : PrintWindow failed, picture not frozen\n"); return; }
                }
              int ox = (c.Left - w.Left), oy = (c.Top - w.Top);
              System.Drawing.Rectangle src = new System.Drawing.Rectangle(ox, oy, Math.Min(c.Width, (w.Width - ox)), Math.Min(c.Height, (w.Height - oy)));
              System.Drawing.Imaging.BitmapData bd = bmp.LockBits(src, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
              byte[] buf = new byte[(src.Width * src.Height) * 4];
              bool anyLit = false;
              for (int y = 0; y < src.Height; y++)
                {
                  System.Runtime.InteropServices.Marshal.Copy((bd.Scan0 + (y * bd.Stride)), buf, ((y * src.Width) * 4), (src.Width * 4));
                }
              bmp.UnlockBits(bd);
              for (int i = 0; i < buf.Length; i += 4) { if ((buf[i] | buf[i + 1] | buf[i + 2]) != 0) { anyLit = true; break; } }
              if (!anyLit) { FD.WL("WARNING : [Power] freeze : snapshot is black (window not capturable), picture not frozen\n"); return; }
              freeze = buf; freezeW = src.Width; freezeH = src.Height;
            }
        }
      catch (Exception ex) { FD.WL($"WARNING : [Power] freeze : {ex.Message}\n"); freeze = null; }
    }}}
    public void FreezeEnd() { freeze = null; }
    public static bool Active {
      get { return ((!FD.GlowActive) && (FD.gPower.NeedsOverlay || (FD.gBrightness < 100) || (FD.gScanlines && (FD.gScanlineOpacity > 0)) || (FD.gVignette > 0) )); } // CLAUDE : Phase 2 draws these itself
    }
    public void Resize(NRect r) {{{
      bool sizeChanged = ((surf is null) || (r.Width != rect.Width) || (r.Height != rect.Height));
      rect = r;
      if (sizeChanged) { Render(); }
      else { Native.SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, 0, 0, (Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE)); }
    }}}
    public void SetLook() { Render(); }
    public static void ParseTint(string s, out float r, out float g, out float b) {{{
      r = g = b = 1f;
      string t = s.Trim().TrimStart('#');
      if ((t.Length == 6) && int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
        { r = (((v >> 16) & 255) / 255f); g = (((v >> 8) & 255) / 255f); b = ((v & 255) / 255f); }
    }}}
    public unsafe void Render() {{{
      if ((rect.Width < 2) || (rect.Height < 2)) { return; }
      if ((surf is null) || (surf.W != rect.Width) || (surf.H != rect.Height)) { surf?.Dispose(); surf = new LayeredSurface(rect.Width, rect.Height); }
      int w = surf.W, h = surf.H;
      float dimA = (Math.Max(0, (100 - FD.gBrightness)) / 100f); // CLAUDE : Brightness > 100 only brightens in Phase 2
      float scanA = (FD.gScanlines ? (FD.gScanlineOpacity / 100f) : 0f);
      float vigA = (FD.gVignette / 100f);
      float tintA = 0f; // CLAUDE : the Phase 1 overlay can only wash the whole area (background included) - phosphor tint is GPU-only now
      ParseTint(FD.gTint, out float tr, out float tg, out float tb);
      int period = ((FD.gScanlineSize > 0) ? Math.Max(2, FD.gScanlineSize) : Math.Max(2, (int) Math.Round((h / 240.0)))); // CLAUDE : ScanlineSize 0 = auto
      // CLAUDE : CRT power animation (Overlay_PowerAnim.cs). This overlay cannot squash the emulator's picture, so the raster fades to black
      //   and the glowing line / dot is drawn over it ; steady "off" = black.
      bool pw = FD.gPower.NeedsOverlay; CrtPowerFrame pf = new CrtPowerFrame(0f, 0f, 0f, 0f, 0f);
      byte[]? frz = ((pw && FD.gPower.Animating) ? freeze : null); // CLAUDE : frozen picture while the animation runs
      if (pw) { if (!FD.gPower.Evaluate(out pf)) { pf = (FD.gPower.IsOn ? new CrtPowerFrame(1f, 1f, 0f, 1f, 0f) : new CrtPowerFrame(0f, 0f, 0f, 0f, 0f)); } }
      float pTn = Math.Max(0.001f, Math.Max(tr, Math.Max(tg, tb))); float pTr = (tr / pTn), pTg = (tg / pTn), pTb = (tb / pTn); // CLAUDE : glow colour
      float pHalf_x = Math.Max(pf.X, (1.5f / Math.Max(1f, (w / 2f)))), pHalf_y = Math.Max(pf.Y, (1.5f / Math.Max(1f, (h / 2f))));
      byte* basePtr = (byte*) surf.Bits;
      for (int y = 0; y < h; y++)
        {
          float rowScan = (((y % period) >= (period / 2)) ? scanA : 0f);
          float cy = ((((y + 0.5f) / h) * 2f) - 1f);
          byte* row = (basePtr + ((long) y * w * 4));
          for (int x = 0; x < w; x++)
            {
              float cx = ((((x + 0.5f) / w) * 2f) - 1f);
              float v = (vigA * (((cx * cx) + (cy * cy)) * 0.5f));
              float keep = ((1f - dimA) * (1f - rowScan) * (1f - v)); // black layers combined: transmitted fraction
              float kB = (1f - keep);                                  // black alpha
              float under = (tintA * keep);                            // tint shows through the black layers
              float a = (kB + under);
              float pr = (tr * under), pg = (tg * under), pb = (tb * under);
              if (pw)
                {
                  bool inR = ((Math.Abs(cx) <= pHalf_x) && (Math.Abs(cy) <= pHalf_y));
                  float dx = Math.Max((Math.Abs(cx) - pHalf_x), 0f) * (w / 2f), dy = Math.Max((Math.Abs(cy) - pHalf_y), 0f) * (h / 2f);
                  float halo = (float) (Math.Exp(-Math.Sqrt(((dx * dx) + (dy * dy))) / 10.0) * pf.Fill * FD.gPower.HaloFill);
                  float glow = Math.Min(1f, ((inR ? pf.Fill : 0f) + halo));
                  float black = (inR ? (1f - pf.Content) : 1f);       // raster fades inside the rect, everything outside is black
                  float ba = Math.Max(a, black);
                  a = (glow + (ba * (1f - glow)));
                  pr = glow * pTr; pg = glow * pTg; pb = glow * pTb;   // premultiplied glow colour = the phosphor tint colour
                }
              byte* px = (row + (x * 4));
              if ((frz is not null) && (x < freezeW) && (y < freezeH)) // CLAUDE : opaque frozen picture under the layers (premultiplied over)
                {
                  int si = (((y * freezeW) + x) * 4);
                  px[0] = (byte) Math.Min(255f, ((pb * 255f) + (frz[si] * (1f - a)) + 0.5f));
                  px[1] = (byte) Math.Min(255f, ((pg * 255f) + (frz[si + 1] * (1f - a)) + 0.5f));
                  px[2] = (byte) Math.Min(255f, ((pr * 255f) + (frz[si + 2] * (1f - a)) + 0.5f));
                  px[3] = 255;
                  continue;
                }
              px[0] = (byte) ((pb * 255f) + 0.5f);
              px[1] = (byte) ((pg * 255f) + 0.5f);
              px[2] = (byte) ((pr * 255f) + 0.5f);
              px[3] = (byte) ((a * 255f) + 0.5f);
            }
        }
      surf.Present(hwnd, rect.Left, rect.Top);
    }}}
  }
}
