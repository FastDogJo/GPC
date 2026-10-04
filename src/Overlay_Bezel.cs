// CLAUDE : new file. Bezel PNG loading, screen-opening detection (alpha scan / colour flood, ini override) and the
// emulator-client-rect <-> overlay-rect mapping.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace FastDog
{
  public static class BezelModel
  {
    // FIELDS & PROPERTIES {{{
    public static Bitmap? Image = null; // 32bppPArgb, screen opening is fully transparent
    public static int Width = 0, Height = 0;
    public static int OpenL = 0, OpenT = 0, OpenR = 0, OpenB = 0; // screen opening in bezel image pixels
    private static double seedFx = 0.5, seedFy = 0.5; // CLAUDE : where inside the screen to start the colour flood (fraction of image)
    public static int detL = 0, detT = 0, detR = 0, detB = 0; // CLAUDE : opening before the user's offset/scale sliders
    public static bool Loaded { get { return (Image is not null); } }
    // }}}

    public static string? ResolveFile() {{{
      if (string.IsNullOrWhiteSpace(FD.gBezelFile)) { return null; } // CLAUDE : blank = no bezel
      string name = FD.gBezelFile;
      if (Path.IsPathRooted(name)) { return (File.Exists(name) ? name : null); }
      string a = Path.Combine(FD.IniDir(), name);
      if (File.Exists(a)) { return a; }
      a = Path.Combine(AppContext.BaseDirectory, name);
      if (File.Exists(a)) { return a; }
      a = Path.Combine(AppContext.BaseDirectory, "..", "assets", name); // CLAUDE : ./bin -> ./assets
      if (File.Exists(a)) { return a; }
      string b = Path.Combine(Directory.GetCurrentDirectory(), name);
      return (File.Exists(b) ? b : null);
    }}}
    public static void Load() {{{
      Image?.Dispose();
      Image = null;
      string? file = ResolveFile();
      if (file is null) { FD.WL($"[Bezel] none - overlay = emulator rect\n"); return; }
      try
        {
          using (Bitmap src = new Bitmap(file))
            {
              Width = src.Width; Height = src.Height;
              Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
              using (Graphics g = Graphics.FromImage(bmp))
                {
                  g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                  g.DrawImage(src, new Rectangle(0, 0, Width, Height), 0, 0, Width, Height, GraphicsUnit.Pixel);
                }
              Image = bmp;
            }
          // CLAUDE : opaque DX art : the image centre is not inside the screen on every model
          string fn = Path.GetFileName(file).ToUpperInvariant();
          if (fn.StartsWith("M1DX")) { seedFx = 0.414; seedFy = 0.27; } else { seedFx = 0.5; seedFy = 0.5; }
          DetectOpening();
          FD.WL($"[Bezel] \"{Path.GetFileName(file)}\" {Width}x{Height} opening {OpenL},{OpenT} - {OpenR},{OpenB}\n");
        }
      catch (Exception ex) { FD.WL($"ERROR : [Bezel] Load \"{file}\" : {ex.Message}\n"); Image?.Dispose(); Image = null; }
    }}}
    // Screen opening: ini override wins; else transparent hole (largest alpha-0 region not touching the border);
    // else (opaque "DX" art) flood the screen colour from the image centre and punch it out.
    // CLAUDE : detected opening -> working opening (Open*) with the offset/scale sliders applied around its centre.
    public static void ApplyAdjust() {{{
      double cx = (((detL + detR) / 2.0) + FD.gScreenOffsetX); double cy = (((detT + detB) / 2.0) + FD.gScreenOffsetY);
      double w = ((detR - detL) * (FD.gScreenScaleX / 100.0)); double h = ((detB - detT) * (FD.gScreenScaleY / 100.0));
      OpenL = (int) Math.Round((cx - (w / 2.0))); OpenR = (int) Math.Round((cx + (w / 2.0)));
      OpenT = (int) Math.Round((cy - (h / 2.0))); OpenB = (int) Math.Round((cy + (h / 2.0)));
      if (OpenR <= OpenL) { OpenR = (OpenL + 1); } if (OpenB <= OpenT) { OpenB = (OpenT + 1); }
    }}}
    private static unsafe void DetectOpening() {{{
      bool ini = ((FD.gScreenRight > FD.gScreenLeft) && (FD.gScreenBottom > FD.gScreenTop) && (FD.gScreenLeft >= 0) && (FD.gScreenTop >= 0));
      Bitmap bmp = Image!;
      BitmapData bd = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
      try
        {
          byte* p = (byte*) bd.Scan0;
          int l, t, r, b;
          bool found = FindTransparentHole(p, bd.Stride, Width, Height, out l, out t, out r, out b);
          if (!found)
            {
              found = FloodScreenColor(p, bd.Stride, out l, out t, out r, out b);
              if (found) { FD.WL("[Bezel] opaque art : screen colour flooded and punched out\n"); }
            }
          if (!found) { l = (Width / 4); t = (Height / 4); r = ((Width * 3) / 4); b = ((Height * 3) / 4); FD.WL("WARNING : [Bezel] no screen opening found, using centre half. Set Look.Screen* in the ini\n"); }
          detL = l; detT = t; detR = r; detB = b;
          if (ini) { detL = FD.gScreenLeft; detT = FD.gScreenTop; detR = FD.gScreenRight; detB = FD.gScreenBottom;FD.WL("[Bezel] opening overridden by ini\n"); }
          ApplyAdjust(); // CLAUDE
        }
      finally { bmp.UnlockBits(bd); }
    }}}
    public static unsafe bool FindTransparentHole(byte* p, int stride, int w, int h, out int bl, out int bt, out int br, out int bb) {{{ // CLAUDE : public + explicit size (ImageUtil)
      bool[] seen = new bool[(w * h)];
      int[] stack = new int[(w * h)];
      long bestArea = 0; bl = bt = br = bb = 0;
      for (int y0 = 0; y0 < h; y0++)
        for (int x0 = 0; x0 < w; x0++)
          {
            int idx0 = ((y0 * w) + x0);
            if (seen[idx0] || (p[((y0 * stride) + (x0 * 4) + 3)] > 8)) { continue; }
            int sp = 0; stack[sp++] = idx0; seen[idx0] = true;
            long area = 0; int minx = x0, maxx = x0, miny = y0, maxy = y0; bool touches = false;
            while (sp > 0)
              {
                int i = stack[--sp]; int cx = (i % w); int cy = (i / w);
                area++;
                if (cx < minx) { minx = cx; } if (cx > maxx) { maxx = cx; } if (cy < miny) { miny = cy; } if (cy > maxy) { maxy = cy; }
                if ((cx == 0) || (cy == 0) || (cx == (w - 1)) || (cy == (h - 1))) { touches = true; }
                for (int d = 0; d < 4; d++)
                  {
                    int nx = (cx + ((d == 0) ? 1 : ((d == 1) ? -1 : 0))); int ny = (cy + ((d == 2) ? 1 : ((d == 3) ? -1 : 0)));
                    if ((nx < 0) || (ny < 0) || (nx >= w) || (ny >= h)) { continue; }
                    int ni = ((ny * w) + nx);
                    if (seen[ni] || (p[((ny * stride) + (nx * 4) + 3)] > 8)) { continue; }
                    seen[ni] = true; stack[sp++] = ni;
                  }
              }
            if ((!touches) && (area > bestArea)) { bestArea = area; bl = minx; bt = miny; br = (maxx + 1); bb = (maxy + 1); }
          }
      return (bestArea > ((long) w * h / 200));
    }}}
    private static unsafe bool FloodScreenColor(byte* p, int stride, out int bl, out int bt, out int br, out int bb) {{{
      int w = Width, h = Height; bl = bt = br = bb = 0;
      int sx = (int) (w * seedFx), sy = (int) (h * seedFy); // CLAUDE
      byte* sp0 = (p + (sy * stride) + (sx * 4));
      int sb = sp0[0], sg = sp0[1], sr = sp0[2];
      bool[] seen = new bool[(w * h)];
      int[] stack = new int[(w * h)];
      int sp = 0; stack[sp++] = ((sy * w) + sx); seen[((sy * w) + sx)] = true;
      int minx = sx, maxx = sx, miny = sy, maxy = sy; long area = 0;
      while (sp > 0)
        {
          int i = stack[--sp]; int cx = (i % w); int cy = (i / w);
          area++;
          if (cx < minx) { minx = cx; } if (cx > maxx) { maxx = cx; } if (cy < miny) { miny = cy; } if (cy > maxy) { maxy = cy; }
          for (int d = 0; d < 4; d++)
            {
              int nx = (cx + ((d == 0) ? 1 : ((d == 1) ? -1 : 0))); int ny = (cy + ((d == 2) ? 1 : ((d == 3) ? -1 : 0)));
              if ((nx < 0) || (ny < 0) || (nx >= w) || (ny >= h)) { continue; }
              int ni = ((ny * w) + nx);
              if (seen[ni]) { continue; }
              byte* q = (p + (ny * stride) + (nx * 4));
              int diff = (Math.Abs(q[0] - sb) + Math.Abs(q[1] - sg) + Math.Abs(q[2] - sr));
              if ((q[3] < 255) || (diff > 40)) { continue; }
              seen[ni] = true; stack[sp++] = ni;
            }
        }
      if (area < ((long) w * h / 200)) { return false; }
      for (int y = miny; y <= maxy; y++)
        for (int x = minx; x <= maxx; x++)
          { if (seen[((y * w) + x)]) { *((uint*) (p + (y * stride) + (x * 4))) = 0; } }
      bl = minx; bt = miny; br = (maxx + 1); bb = (maxy + 1);
      return true;
    }}}

    // Emulator client rect -> overlay (bezel window) rect.
    public static NRect ToOverlay(NRect e) {{{
      if ((!Loaded) || (e.Width <= 0) || (OpenR <= OpenL)) { return e; }
      double s = ((double) e.Width / (double) (OpenR - OpenL));
      int l = (e.Left - (int) Math.Round((OpenL * s)));
      int t = (e.Top - (int) Math.Round((OpenT * s)));
      return new NRect { Left = l, Top = t, Right = (l + (int) Math.Round((Width * s))), Bottom = (t + (int) Math.Round((Height * s))) };
    }}}
    // Overlay rect -> emulator client rect.
    public static NRect ToClient(NRect o) {{{
      if ((!Loaded) || (o.Width <= 0)) { return o; }
      double s = ((double) o.Width / (double) Width);
      int l = (o.Left + (int) Math.Round((OpenL * s)));
      int t = (o.Top + (int) Math.Round((OpenT * s)));
      return new NRect { Left = l, Top = t, Right = (l + (int) Math.Round(((OpenR - OpenL) * s))), Bottom = (t + (int) Math.Round(((OpenB - OpenT) * s))) };
    }}}
  }
}

namespace FastDog
{
  // CLAUDE : small helpers shared with the Phase 2 glass image.
  public static unsafe class ImageUtil
  {
    // Largest fully transparent region not touching the image border (bbox) of a 32bppPArgb bitmap.
    public static bool FindTransparentHole(System.Drawing.Bitmap bmp, out int l, out int t, out int r, out int b) {{{
      System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
      try { return BezelModel.FindTransparentHole((byte*) bd.Scan0, bd.Stride, bmp.Width, bmp.Height, out l, out t, out r, out b); }
      finally { bmp.UnlockBits(bd); }
    }}}
  }
}
