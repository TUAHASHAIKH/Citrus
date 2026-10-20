using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Citrus
{
    static class RingGeometry
    {
        public const float Center = 120, Inner = 46, Outer = 114, Gap = 4;
        public static int HitTest(PointF point, int count)
        {
            if (count <= 0) return -1;
            double x = point.X - Center, y = point.Y - Center;
            double radius = Math.Sqrt(x * x + y * y);
            if (radius < Inner || radius > Outer) return -1;
            if (count == 1) return 0;
            double sweep = 360.0 / count;
            double angle = (Math.Atan2(y, x) * 180 / Math.PI + 90 + sweep / 2 + 360) % 360;
            double within = angle % sweep;
            if (within < Gap / 2 || within > sweep - Gap / 2) return -1;
            return (int)(angle / sweep);
        }
        public static PointF LabelPoint(int index, int count)
        {
            double radians = (-90 + index * 360.0 / count) * Math.PI / 180;
            return new PointF(Center + (float)Math.Cos(radians) * 81, Center + (float)Math.Sin(radians) * 81);
        }
        public static GraphicsPath Wedge(int index, int count)
        {
            var path = new GraphicsPath();
            if (count == 1) {
                path.FillMode = FillMode.Alternate;
                path.AddEllipse(Center - Outer, Center - Outer, Outer * 2, Outer * 2);
                path.AddEllipse(Center - Inner, Center - Inner, Inner * 2, Inner * 2);
                return path;
            }
            float sweep = 360f / count;
            float start = -90 - sweep / 2 + index * sweep + Gap / 2;
            float visible = sweep - Gap;
            path.AddArc(Center - Outer, Center - Outer, Outer * 2, Outer * 2, start, visible);
            path.AddArc(Center - Inner, Center - Inner, Inner * 2, Inner * 2, start + visible, -visible);
            path.CloseFigure();
            return path;
        }
    }
}
