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
            try {
                int corners = 2, backdrop = 3, dark = 1;
                DwmSetWindowAttribute(Handle, 33, ref corners, 4);
                DwmSetWindowAttribute(Handle, 38, ref backdrop, 4);
                DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnPaintBackground(e); return; }
            Color background = GlassPalette == null ? BackColor : GlassPalette.Background;
            Rectangle area = ClientRectangle;
            if (area.Width < 1 || area.Height < 1) return;
            using (var wash = new LinearGradientBrush(area, ControlPaint.Light(background, .18f),
                ControlPaint.Dark(background, .13f), 65f)) e.Graphics.FillRectangle(wash, area);
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
