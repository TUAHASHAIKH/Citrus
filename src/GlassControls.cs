using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Citrus
{
    class GlassWindow : Form
    {
        public Palette GlassPalette;
        bool backdropAvailable;
        [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref Margins margins);
        public static void Tint(Control control, Palette palette)
        {
            control.BackColor = control is Panel || control is Label || control is CheckBox ? Color.Transparent : palette.Background;
            control.ForeColor = palette.Text;
            if (control is Button) control.BackColor = palette.Segment;
            foreach (Control child in control.Controls) Tint(child, palette);
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attribute, ref int value, int size);
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RefreshGlass();
        }
        public void RefreshGlass()
        {
            if (!IsHandleCreated) return;
            backdropAvailable = false;
            try {
                int corners = 2, backdrop = 3, dark = GlassPalette != null && GlassPalette.Background.GetBrightness() > .5 ? 0 : 1;
                DwmSetWindowAttribute(Handle, 33, ref corners, 4);
                if (!SystemInformation.HighContrast && DwmSetWindowAttribute(Handle, 38, ref backdrop, 4) == 0) {
                    var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                    backdropAvailable = DwmExtendFrameIntoClientArea(Handle, ref margins) == 0;
                }
                DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x31E) RefreshGlass();
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnPaintBackground(e); return; }
            Color background = GlassPalette == null ? BackColor : GlassPalette.Background;
            Rectangle area = ClientRectangle;
            if (area.Width < 1 || area.Height < 1) return;
            Color top = ControlPaint.Light(background, .18f), bottom = ControlPaint.Dark(background, .13f);
            if (backdropAvailable) { e.Graphics.Clear(Color.Transparent); top = Color.FromArgb(210, top); bottom = Color.FromArgb(225, bottom); }
            using (var wash = new LinearGradientBrush(area, top, bottom, 65f)) e.Graphics.FillRectangle(wash, area);
            using (var glow = new GraphicsPath()) {
                glow.AddEllipse(-area.Width / 2, -area.Height / 2, area.Width * 2, area.Height);
                using (var light = new PathGradientBrush(glow)) {
                    light.CenterColor = Color.FromArgb(26, 255, 195, 140);
                    light.SurroundColors = new Color[] { Color.Transparent };
                    e.Graphics.FillPath(light, glow);
                }
            }
            using (var edge = new Pen(Color.FromArgb(42, Color.White))) e.Graphics.DrawRectangle(edge, 0, 0, area.Width - 1, area.Height - 1);
        }
    }

    class GlassComboBox : ComboBox
    {
        public GlassComboBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Color surface = SystemInformation.HighContrast ? SystemColors.Window : ControlPaint.Light(BackColor, .12f);
            using (var shape = GlassButton.Round(new RectangleF(1, 1, Width - 3, Height - 3), 6)) {
                using (var fill = new SolidBrush(surface)) g.FillPath(fill, shape);
                using (var border = new Pen(SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(70, 170, 180, 200))) g.DrawPath(border, shape);
            }
            string label = SelectedItem == null ? "Choose files first" : SelectedItem.ToString();
            Color ink = Enabled ? ForeColor : Color.FromArgb(150, 156, 168);
            TextRenderer.DrawText(g, label, Font, new Rectangle(9, 0, Math.Max(1, Width - 34), Height), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (var pen = new Pen(ink, 1.4f)) {
                float x = Width - 17, y = Height / 2f;
                g.DrawLines(pen, new PointF[] { new PointF(x - 4, y - 2), new PointF(x, y + 2), new PointF(x + 4, y - 2) });
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4));
        }
    }

    class GlassButton : Button
    {
        bool hover;
        public GlassButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; DoubleBuffered = true; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(new RectangleF(1, 1, Width - 3, Height - 3), 10)) {
                Color top = hover && Enabled ? ControlPaint.Light(BackColor, .25f) : ControlPaint.Light(BackColor, .08f);
                using (var fill = new LinearGradientBrush(ClientRectangle, top, ControlPaint.Dark(BackColor, .08f), 90f)) g.FillPath(fill, path);
                using (var border = new Pen(Color.FromArgb(Enabled ? 65 : 25, Color.White))) g.DrawPath(border, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(150, 156, 168),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5));
        }
        public static GraphicsPath Round(RectangleF box, float radius)
        {
            var path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(box.Left, box.Top, d, d, 180, 90); path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
            path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90); path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
    }
}
