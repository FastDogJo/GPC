// CLAUDE : new file. Glass cover loading (port of P:/SRC/C#/VfdEngine VFDRenderer.LoadGlassCoverTexture and its helpers) and the glass placement maths
// (port of P:/SRC/C#/TRS80 ScreenVfd.ResetVfdPlacement), expressed in the overlay window's pixel space.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace FastDog
{
  // premultiplied RGBA, G = "inside the glass" mask, B = depth into the rim (0 inner edge .. 1 outer edge), full mip chain
  public sealed class GlassCoverData
  {
    public readonly List<byte[]> Mips = new List<byte[]>();
    public readonly List<int> MipW = new List<int>(), MipH = new List<int>();
    public int W, H;
    public bool HasRimDepth;
    public float RimL, RimR, RimT, RimB, RimX, RimY;
    public string Name = "";
  }

  public static class GlassCover
  {
    private const float cRimFracDefault = 0.15f;

    public static GlassCoverData? Load(string path) {{{
      if (!File.Exists(path)) { FD.WL($"WARNING : [Glass] cover image not found : {path}\n"); return null; }
      using Bitmap bmp = new Bitmap(path);
      int w = bmp.Width, h = bmp.Height;
      byte[] level0 = new byte[(w * h) * 4];
      BitmapData bits = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      try { for (int y = 0; y < h; y++) { Marshal.Copy((bits.Scan0 + (y * bits.Stride)), level0, ((y * w) * 4), (w * 4)); } }
      finally { bmp.UnlockBits(bits); }
      // BGRA -> premultiplied RGBA
      for (int i = 0; i < level0.Length; i += 4)
        {
          int b = level0[i], g = level0[i + 1], r = level0[i + 2], a = level0[i + 3];
          level0[i] = (byte) (((r * a) + 127) / 255);
          level0[i + 1] = (byte) (((g * a) + 127) / 255);
          level0[i + 2] = (byte) (((b * a) + 127) / 255);
        }
      bool[] outside = MarkInside(level0, w, h);
      GlassCoverData d = new GlassCoverData { W = w, H = h, Name = Path.GetFileName(path) };
      d.HasRimDepth = MarkRimDepth(level0, outside, w, h);
      float fl = RimFrac(level0, ((((h / 2) * w)) * 4), 4, w);
      float fr = RimFrac(level0, ((((h / 2) * w) + (w - 1)) * 4), -4, w);
      float ft = RimFrac(level0, ((w / 2) * 4), (w * 4), h);
      float fb = RimFrac(level0, (((((h - 1) * w)) + (w / 2)) * 4), -(w * 4), h);
      if ((fl <= 0f) || (fr <= 0f)) { fl = cRimFracDefault; fr = cRimFracDefault; }
      if ((ft <= 0f) || (fb <= 0f)) { ft = cRimFracDefault; fb = cRimFracDefault; }
      d.RimL = fl; d.RimR = fr; d.RimT = ft; d.RimB = fb; d.RimX = ((fl + fr) * 0.5f); d.RimY = ((ft + fb) * 0.5f);
      d.Mips.Add(level0); d.MipW.Add(w); d.MipH.Add(h);
      while ((d.MipW[^1] > 1) || (d.MipH[^1] > 1))
        { MipDown(d.Mips[^1], d.MipW[^1], d.MipH[^1], out byte[] nd, out int nw, out int nh); d.Mips.Add(nd); d.MipW.Add(nw); d.MipH.Add(nh); }
      FD.WL($"[Glass] cover {d.Name} {w}x{h} rim L={fl:F4} R={fr:F4} T={ft:F4} B={fb:F4} rimDepth={d.HasRimDepth}\n");
      return d;
    }}}

    private static float RimFrac(byte[] rgba, int start, int step, int n) {{{
      int i = 0;
      while ((i < n) && (rgba[(start + (i * step)) + 3] < 128)) { i++; }
      while ((i < n) && (rgba[(start + (i * step)) + 3] >= 128)) { i++; }
      return ((i < (n / 2)) ? ((float) i / n) : -1.0f);
    }}}
    private static void Seed(byte[] rgba, bool[] outside, int[] queue, ref int tail, int i) {{{
      if (outside[i] || (rgba[(i * 4) + 3] >= 128)) { return; }
      outside[i] = true; queue[tail++] = i;
    }}}
    private static bool[] MarkInside(byte[] rgba, int w, int h) {{{
      int n = (w * h);
      bool[] outside = new bool[n]; int[] queue = new int[n]; int head = 0, tail = 0;
      for (int x = 0; x < w; x++) { Seed(rgba, outside, queue, ref tail, x); Seed(rgba, outside, queue, ref tail, (((h - 1) * w) + x)); }
      for (int y = 0; y < h; y++) { Seed(rgba, outside, queue, ref tail, (y * w)); Seed(rgba, outside, queue, ref tail, ((y * w) + (w - 1))); }
      while (head < tail)
        {
          int i = queue[head++]; int x = (i % w), y = (i / w);
          if (x > 0) { Seed(rgba, outside, queue, ref tail, (i - 1)); }
          if (x < (w - 1)) { Seed(rgba, outside, queue, ref tail, (i + 1)); }
          if (y > 0) { Seed(rgba, outside, queue, ref tail, (i - w)); }
          if (y < (h - 1)) { Seed(rgba, outside, queue, ref tail, (i + w)); }
        }
      for (int i = 0; i < n; i++) { rgba[(i * 4) + 1] = (byte) (outside[i] ? 0 : 255); }
      return outside;
    }}}
    private static bool MarkRimDepth(byte[] rgba, bool[] outside, int w, int h) {{{
      int n = (w * h);
      bool[] interior = new bool[n]; bool any = false;
      for (int i = 0; i < n; i++) { interior[i] = ((!outside[i]) && (rgba[(i * 4) + 3] < 128)); any |= interior[i]; }
      if (!any) { return false; }
      int[] dIn = Chamfer(interior, w, h); int[] dOut = Chamfer(outside, w, h);
      for (int i = 0; i < n; i++)
        {
          float depth = (outside[i] ? 1.0f : (interior[i] ? 0.0f : ((float) dIn[i] / Math.Max((dIn[i] + dOut[i]), 1))));
          rgba[(i * 4) + 2] = (byte) ((depth * 255.0f) + 0.5f);
        }
      return true;
    }}}
    private static int[] Chamfer(bool[] seed, int w, int h) {{{
      const int INF = (int.MaxValue / 4);
      int[] d = new int[w * h];
      for (int i = 0; i < d.Length; i++) { d[i] = (seed[i] ? 0 : INF); }
      for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
          {
            int i = ((y * w) + x), v = d[i];
            if (x > 0) { v = Math.Min(v, (d[i - 1] + 3)); }
            if (y > 0)
              {
                v = Math.Min(v, (d[i - w] + 3));
                if (x > 0) { v = Math.Min(v, (d[(i - w) - 1] + 4)); }
                if (x < (w - 1)) { v = Math.Min(v, (d[(i - w) + 1] + 4)); }
              }
            d[i] = v;
          }
      for (int y = (h - 1); y >= 0; y--)
        for (int x = (w - 1); x >= 0; x--)
          {
            int i = ((y * w) + x), v = d[i];
            if (x < (w - 1)) { v = Math.Min(v, (d[i + 1] + 3)); }
            if (y < (h - 1))
              {
                v = Math.Min(v, (d[i + w] + 3));
                if (x < (w - 1)) { v = Math.Min(v, (d[(i + w) + 1] + 4)); }
                if (x > 0) { v = Math.Min(v, (d[(i + w) - 1] + 4)); }
              }
            d[i] = v;
          }
      return d;
    }}}
    private static void MipDown(byte[] src, int w, int h, out byte[] dst, out int nw, out int nh) {{{
      nw = Math.Max((w / 2), 1); nh = Math.Max((h / 2), 1);
      dst = new byte[(nw * nh) * 4];
      for (int y = 0; y < nh; y++)
        {
          int y0 = Math.Min((y * 2), (h - 1)), y1 = Math.Min(((y * 2) + 1), (h - 1));
          for (int x = 0; x < nw; x++)
            {
              int x0 = Math.Min((x * 2), (w - 1)), x1 = Math.Min(((x * 2) + 1), (w - 1));
              for (int c = 0; c < 4; c++)
                {
                  int sum = (src[(((y0 * w) + x0) * 4) + c] + src[(((y0 * w) + x1) * 4) + c] + src[(((y1 * w) + x0) * 4) + c] + src[(((y1 * w) + x1) * 4) + c]);
                  dst[(((y * nw) + x) * 4) + c] = (byte) ((sum + 2) / 4);
                }
            }
        }
    }}}
  }

  // Where the glass sits in the overlay window (all in output pixels), the same chain as ScreenVfd.ResetVfdPlacement.
  public struct GlassPlace
  {
    public float ScaleX, ScaleY, OffsetX, OffsetY;     // effective (user value or auto-fit)
    public float BorderX, BorderY;                      // rim width in core-half units
    public float CenterPxX, CenterPxY, CorePxHalfW, CorePxHalfH;
  }

  public partial class FD
  {
    public static GlassCoverData? gGlassCover = null;
    // set by GlowRenderer : overlay (output) size and the screen (emulator client) rect inside it
    public static int gGeomOutW = 0, gGeomOutH = 0;
    public static float gGeomEx = 0, gGeomEy = 0, gGeomEw = 0, gGeomEh = 0;

    public static GlassPlace GlassPlacementCalc() {{{
      GlassPlace gp = new GlassPlace { ScaleX = 1f, ScaleY = 1f, OffsetX = 0f, OffsetY = 0f };
      GlassCoverData? c = gGlassCover;
      if ((c is null) || (gGeomOutW <= 0) || (gGeomOutH <= 0)) { return gp; }
      float bw = (BezelModel.Loaded ? BezelModel.Width : gGeomOutW), bh = (BezelModel.Loaded ? BezelModel.Height : gGeomOutH);
      gp.BorderX = ((c.RimX / (0.5f - c.RimX)) * GPv(GP.BorderWidth));
      gp.BorderY = ((c.RimY / (0.5f - c.RimY)) * GPv(GP.BorderHeight));
      float sx = GPv(GP.ScaleX), sy = GPv(GP.ScaleY), ox = GPv(GP.OffsetX), oy = GPv(GP.OffsetY);
      // auto : fit the glass core to the screen rect
      if (float.IsNaN(sx)) { sx = (((gGeomEw * (1f + gp.BorderX)) / gGeomOutW) * (bw / c.W)); }
      if (float.IsNaN(sy)) { sy = (((gGeomEh * (1f + gp.BorderY)) / gGeomOutH) * (bh / c.H)); }
      if (float.IsNaN(ox)) { ox = ((((gGeomEx + (gGeomEw / 2f)) / gGeomOutW) * 2f) - 1f); }
      if (float.IsNaN(oy)) { oy = (1f - (((gGeomEy + (gGeomEh / 2f)) / gGeomOutH) * 2f)); }
      gp.ScaleX = sx; gp.ScaleY = sy; gp.OffsetX = ox; gp.OffsetY = oy;
      float totalHalfW = ((c.W / bw) * sx), totalHalfH = ((c.H / bh) * sy); // NDC half extents of the whole cover (rim included)
      gp.CorePxHalfW = ((totalHalfW / (1f + gp.BorderX)) * (gGeomOutW / 2f));
      gp.CorePxHalfH = ((totalHalfH / (1f + gp.BorderY)) * (gGeomOutH / 2f));
      gp.CenterPxX = (((ox * 0.5f) + 0.5f) * gGeomOutW);
      gp.CenterPxY = ((0.5f - (oy * 0.5f)) * gGeomOutH);
      return gp;
    }}}
    // UI value of a parameter : auto (NaN) glass scale/offset show what the renderer actually uses.
    public static float GlowEffective(GP id) {{{
      float v = gF[(int) id];
      if (!float.IsNaN(v)) { return v; }
      GlassPlace p = GlassPlacementCalc();
      switch (id) { case GP.ScaleX: return p.ScaleX; case GP.ScaleY: return p.ScaleY; case GP.OffsetX: return p.OffsetX; case GP.OffsetY: return p.OffsetY; }
      return 0f;
    }}}
    // Glass cover png : the ini's [Glass] File (blank = none). Absolute, else beside the ini, else beside the exe.
    public static string GlassCoverFileFor() {{{
      string name = gGlassFile.Trim();
      return ResolveAsset(name);
    }}}
    public static string ResolveAsset(string name) {{{
      if (System.IO.Path.IsPathRooted(name)) { return name; }
      string a = System.IO.Path.Combine(IniDir(), name);
      if (System.IO.File.Exists(a)) { return a; }
      a = System.IO.Path.Combine(AppContext.BaseDirectory, name);
      if (System.IO.File.Exists(a)) { return a; }
      a = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "assets", name); // CLAUDE : ./bin -> ./assets
      return (System.IO.File.Exists(a) ? a : System.IO.Path.Combine(AppContext.BaseDirectory, name));
    }}}
  }
}
