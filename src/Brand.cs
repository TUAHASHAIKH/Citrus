using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;

namespace Citrus
{
    static class Brand
    {
        public static Icon LoadIcon()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Citrus.Icon"))
            using (var icon = new Icon(stream, new Size(32, 32))) return (Icon)icon.Clone();
        }

        // Original vector artwork shared by the app, tray icon and icon generator.
        public static void Draw(Graphics g, RectangleF bounds)
        {
            var state = g.Save();
            g.TranslateTransform(bounds.X, bounds.Y);
            g.ScaleTransform(bounds.Width / 256f, bounds.Height / 256f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var fruit = new GraphicsPath())
            {
                fruit.AddBezier(128, 69, 74, 47, 30, 83, 32, 142);
                fruit.AddBezier(32, 142, 33, 200, 75, 235, 128, 230);
                fruit.AddBezier(128, 230, 181, 235, 223, 200, 224, 142);
                fruit.AddBezier(224, 142, 226, 83, 182, 47, 128, 69);
                using (var fill = new LinearGradientBrush(new Point(55, 63), new Point(191, 230),
                    Color.FromArgb(255, 195, 76), Color.FromArgb(246, 91, 34))) g.FillPath(fill, fruit);
            }
            using (var leaf = new GraphicsPath())
            {
                leaf.AddBezier(128, 62, 126, 29, 157, 12, 197, 23);
                leaf.AddBezier(197, 23, 184, 62, 155, 71, 128, 62);
                using (var fill = new LinearGradientBrush(new Point(130, 24), new Point(184, 64),
                    Color.FromArgb(113, 211, 144), Color.FromArgb(27, 135, 88))) g.FillPath(fill, leaf);
            }
            // A continuous C-shaped turn ending in an arrow: citrus + conversion.
            using (var pen = new Pen(Color.FromArgb(255, 247, 222), 16))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, 77, 99, 102, 102, 48, 269);
            }
            using (var arrow = new GraphicsPath())
            using (var white = new SolidBrush(Color.FromArgb(255, 247, 222)))
            {
                arrow.AddPolygon(new PointF[] { new PointF(151, 114), new PointF(186, 106), new PointF(180, 141) });
                g.FillPath(white, arrow);
            }
            g.Restore(state);
        }
    }
}
