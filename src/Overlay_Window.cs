// CLAUDE : new file. The two borderless layered overlay windows.
//   DimForm   : sits exactly over the emulator client area, fully click-through (WS_EX_TRANSPARENT) - dim/scanlines/vignette/tint.
//   BezelForm : bezel PNG; the screen opening is alpha 0 (click-through); the rest drags (HTCAPTION) / resizes (edges).
// Z-order (bottom->top) : emulator, DimForm, BezelForm.
using System;
using System.Windows.Forms;

namespace FastDog
{
  public class OverlayFormBase : Form
  {
    protected virtual int ExtraExStyle { get { return 0; } }
    public OverlayFormBase() {{{
      FormBorderStyle = FormBorderStyle.None;
      ShowInTaskbar = false;
      StartPosition = FormStartPosition.Manual;
      AutoScaleMode = AutoScaleMode.None;
    }}}
    protected override CreateParams CreateParams {
      get
        {
          CreateParams cp = base.CreateParams;
          cp.ExStyle |= (Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | ExtraExStyle);
          return cp;
        }
    }
    protected override bool ShowWithoutActivation { get { return true; } }
  }

  public sealed class DimForm : OverlayFormBase
  {
    protected override int ExtraExStyle { get { return Native.WS_EX_TRANSPARENT; } }
    protected override void WndProc(ref Message m) {{{
      if (m.Msg == Native.WM_NCHITTEST) { m.Result = (IntPtr) Native.HTTRANSPARENT; return; }
      if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr) 3; return; }
      base.WndProc(ref m);
    }}}
  }

  public sealed class BezelForm : OverlayFormBase
  {
    protected override CreateParams CreateParams {
      get
        {
          CreateParams cp = base.CreateParams;
          cp.Style |= unchecked((int) Native.WS_THICKFRAME); // lets DefWindowProc run the edge-resize loop; the layered bitmap hides the frame
          return cp;
        }
    }
    protected override void OnMouseWheel(MouseEventArgs e) {{{ // CLAUDE : wheel over the bezel = screen brightness (TRS80 project : CRT / sprite brightness)
      if ((Control.ModifierKeys & Keys.Control) != 0) { FD.BehindWheel(e.Delta); return; } // CLAUDE
      FD.BrightnessWheel(e.Delta);
    }}}
    protected override void OnMouseDown(MouseEventArgs e) {{{ // CLAUDE : power rect clicks
      if ((e.Button == MouseButtons.Left) && FD.PowerMouseDown(Handle, e.Clicks, (Control.ModifierKeys == Keys.Shift))) { return; }
      base.OnMouseDown(e);
    }}}
    protected override void WndProc(ref Message m) {{{
      switch (m.Msg)
        {
          case Native.WM_NCHITTEST:
            m.Result = (IntPtr) FD.BezelHitTest(Handle, m.LParam);
            return;
          case Native.WM_SIZING:
            FD.BezelSizing(ref m);
            m.Result = (IntPtr) 1;
            return;
          case Native.WM_MOUSEACTIVATE:
            m.Result = (IntPtr) 3;
            return;
          case 0xA3: // WM_NCLBUTTONDBLCLK : never maximise / fullscreen from a double-click on the bezel
            return;
          case Native.WM_NCLBUTTONDOWN:   // CLAUDE : clicking / dragging the bezel raises everything
          case Native.WM_LBUTTONDOWN:
            // CLAUDE : raising the assembly puts the emulator above the overlays for an instant (visible flash) - not needed for a power-rect click when it is already in front
            if ((m.Msg == Native.WM_LBUTTONDOWN) && (Native.GetForegroundWindow() == FD.gEmuHwnd) && FD.PowerHit(Handle, Control.MousePosition.X, Control.MousePosition.Y)) { break; }
            FD.BringAssemblyToFront();
            break;
          case Native.WM_WINDOWPOSCHANGING: // CLAUDE : keep the bezel where our stacking put it (below the presenter) - no system raise on click / move loop
            {
              unsafe
                {
                  NWindowPos* wp = (NWindowPos*) m.LParam;
                  if ((!FD.gZAllow) && ((wp->Flags & Native.SWP_NOZORDER) == 0)) { wp->Flags |= Native.SWP_NOZORDER; }
                }
              break;
            }
          case Native.WM_ENTERSIZEMOVE:
            if (FD.gBezelRenderer is not null) { FD.gBezelRenderer.Fast = true; }
            break;
          case Native.WM_EXITSIZEMOVE:
            if (FD.gBezelRenderer is not null) { FD.gBezelRenderer.Fast = false; FD.gBezelRenderer.Render(); }
            break;
          case Native.WM_WINDOWPOSCHANGED:
            base.WndProc(ref m);
            FD.TrackOnBezelChanged();
            return;
        }
      base.WndProc(ref m);
    }}}
  }

  public partial class FD
  {
    public const int cResizeBand = 8;
    // Hit test in screen coords : edges resize, opening passes through, everything else drags the whole assembly.
    public static int BezelHitTest(IntPtr hwnd, IntPtr lParam) {{{
      long lp = lParam.ToInt64();
      int x = (short) (lp & 0xFFFF); int y = (short) ((lp >> 16) & 0xFFFF);
      NRect r; Native.GetWindowRect(hwnd, out r);
      bool l = (x < (r.Left + cResizeBand)); bool rt = (x >= (r.Right - cResizeBand));
      bool t = (y < (r.Top + cResizeBand)); bool b = (y >= (r.Bottom - cResizeBand));
      if (BezelModel.Loaded)
        {
          if (t && l) { return Native.HTTOPLEFT; } if (t && rt) { return Native.HTTOPRIGHT; }
          if (b && l) { return Native.HTBOTTOMLEFT; } if (b && rt) { return Native.HTBOTTOMRIGHT; }
          if (l) { return Native.HTLEFT; } if (rt) { return Native.HTRIGHT; }
          if (t) { return Native.HTTOP; } if (b) { return Native.HTBOTTOM; }
          if (PowerHit(hwnd, x, y)) { return 1; } // CLAUDE : HTCLIENT - power rect gets mouse clicks (no window drag from there)
          // CLAUDE : the screen opening is no longer click-through - it drags the window like the rest of the bezel (see BezelRenderer.BlockOpeningClicks)
        }
      return Native.HTCAPTION;
    }}}
    // WM_SIZING : keep the bezel image aspect ratio.
    public static unsafe void BezelSizing(ref Message m) {{{
      if ((!BezelModel.Loaded) || (BezelModel.Width <= 0)) { return; }
      NRect* r = (NRect*) m.LParam;
      int edge = (int) m.WParam.ToInt64();
      double ratio = ((double) BezelModel.Height / (double) BezelModel.Width);
      // CLAUDE : never go below what the emulator window can actually be (learned in TrackOnBezelChanged)
      double sMin = Math.Max(((double) FD.gMinClientW / (BezelModel.OpenR - BezelModel.OpenL)), ((double) FD.gMinClientH / (BezelModel.OpenB - BezelModel.OpenT)));
      int minW = Math.Max(160, (int) Math.Ceiling((BezelModel.Width * sMin)));
      int w = Math.Max(minW, (r->Right - r->Left)); int h = Math.Max((int) Math.Ceiling((minW * ratio)), (r->Bottom - r->Top));
      bool vertOnly = ((edge == 3) || (edge == 6)); // WMSZ_TOP / WMSZ_BOTTOM
      bool left = ((edge == 1) || (edge == 4) || (edge == 7)); // LEFT, TOPLEFT, BOTTOMLEFT
      if (vertOnly) { w = (int) Math.Round((h / ratio)); }
      else
        {
          h = (int) Math.Round((w * ratio));
          bool top = ((edge == 3) || (edge == 4) || (edge == 5)); // TOP, TOPLEFT, TOPRIGHT
          if (top) { r->Top = (r->Bottom - h); } else { r->Bottom = (r->Top + h); }
        }
      if (left) { r->Left = (r->Right - w); } else { r->Right = (r->Left + w); }
    }}}
  }
}
