using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Citrus
{
    static class PhaseTwoTests
    {
        public static void Run(string root)
        {
            root = Path.Combine(root, "phase-two");
            Directory.CreateDirectory(root);
            CheckRoutes(root);
            CheckTransparency(root);
            CheckMultiPage(root);
            CheckCapabilities();
            CheckWedges();
            CheckSettings(root);
            CheckDropData(root);
        }
        static void Assert(bool condition, string description)
        {
            if (!condition) throw new Exception("FAIL: " + description);
        }
        static void CheckRoutes(string root)
        {
            foreach (ImageTarget sourceFormat in ImageRoutes.All)
            {
                string source = Path.Combine(root, "original-" + sourceFormat + ImageRoutes.Extension(sourceFormat));
                using (var image = new Bitmap(53, 37)) {
                    using (var g = Graphics.FromImage(image)) { g.Clear(Color.CornflowerBlue); g.FillRectangle(Brushes.Coral, 20, 0, 33, 37); }
                    image.Save(source, ImageRoutes.Format(sourceFormat));
                }
                byte[] original = File.ReadAllBytes(source);
                foreach (ImageTarget target in ImageRoutes.All.Where(target => target != sourceFormat))
                {
                    string output = Converter.Convert(source, target, 90, CancellationToken.None);
                    using (var image = Image.FromFile(output)) {
                        Assert(image.RawFormat.Guid == ImageRoutes.Format(target).Guid, sourceFormat + " → " + target + " format");
                        Assert(image.Width == 53 && image.Height == 37, sourceFormat + " → " + target + " dimensions");
                    }
                    Assert(original.SequenceEqual(File.ReadAllBytes(source)), sourceFormat + " → " + target + " preserves original");
                }
            }
            bool rejected = false;
            try { Converter.Convert(Path.Combine(root, "original-Png.png"), ImageTarget.Png, 90, CancellationToken.None); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "same-format conversion rejected");
        }
        static void CheckTransparency(string root)
        {
            string input = Path.Combine(root, "alpha.png");
            using (var image = new Bitmap(32, 32, PixelFormat.Format32bppArgb)) {
                image.SetPixel(20, 20, Color.FromArgb(128, 200, 100, 50));
                image.Save(input, ImageFormat.Png);
            }
            foreach (ImageTarget target in new ImageTarget[] { ImageTarget.Jpeg, ImageTarget.Bmp }) {
                string output = Converter.Convert(input, target, 95, CancellationToken.None);
                using (var image = new Bitmap(output)) {
                    Color corner = image.GetPixel(0, 0);
                    Assert(corner.A == 255 && corner.R >= 245 && corner.G >= 245 && corner.B >= 245, "white background for " + target);
                }
            }
            string tiff = Converter.Convert(input, ImageTarget.Tiff, 90, CancellationToken.None);
            string png = Converter.Convert(tiff, ImageTarget.Png, 90, CancellationToken.None);
            using (var image = new Bitmap(png)) {
                Assert(image.GetPixel(0, 0).A == 0, "transparent pixels preserved PNG → TIFF → PNG");
                Assert(Math.Abs(image.GetPixel(20, 20).A - 128) <= 1, "partial alpha preserved");
            }
            string qualityInput = Path.Combine(root, "quality.png");
            using (var image = new Bitmap(128, 128)) {
                var random = new Random(123);
                for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                    image.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
                image.Save(qualityInput, ImageFormat.Png);
            }
            string low = Converter.Convert(qualityInput, ImageTarget.Jpeg, 10, CancellationToken.None);
            string high = Converter.Convert(qualityInput, ImageTarget.Jpeg, 95, CancellationToken.None);
            Assert(new FileInfo(high).Length > new FileInfo(low).Length, "JPEG quality changes encoder output");
        }
        static void CheckMultiPage(string root)
        {
            string input = Path.Combine(root, "pages.tiff");
            var encoder = ImageCodecInfo.GetImageEncoders().First(info => info.FormatID == ImageFormat.Tiff.Guid);
            using (var first = new Bitmap(16, 16))
            using (var second = new Bitmap(16, 16))
            using (var options = new EncoderParameters(1)) {
                options.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.MultiFrame);
                first.Save(input, encoder, options);
                options.Param[0].Dispose(); options.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.FrameDimensionPage);
                first.SaveAdd(second, options);
                options.Param[0].Dispose(); options.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.Flush);
                first.SaveAdd(options);
            }
            bool rejected = false;
            try { Converter.Convert(input, ImageTarget.Png, 90, CancellationToken.None); }
            catch (InvalidDataException ex) { rejected = ex.Message.Contains("Multi-page"); }
            Assert(rejected && !File.Exists(Path.Combine(root, "pages.png")), "multi-page TIFF rejected without partial output");
        }
        static void CheckCapabilities()
        {
            Assert(ImageRoutes.Targets(new string[] { "a.jpg" }).SequenceEqual(new ImageTarget[] { ImageTarget.Png, ImageTarget.Bmp, ImageTarget.Tiff }), "JPEG routes");
            Assert(ImageRoutes.Targets(new string[] { "a.jpg", "b.png" }).SequenceEqual(new ImageTarget[] { ImageTarget.Bmp, ImageTarget.Tiff }), "mixed selection uses intersection");
            Assert(ImageRoutes.Targets(new string[] { "a.jpg", "b.png", "c.bmp", "d.tif" }).Length == 0, "no common output handled");
            Assert(ImageRoutes.Targets(new string[] { "a.jpg", "b.pdf" }).Length == 0, "unsupported mixed selection rejected");
            ImageTarget format;
            Assert(ImageRoutes.TrySource("PHOTO.JPEG", out format) && format == ImageTarget.Jpeg, "case-insensitive extensions and JPEG alias");
        }
        static void CheckWedges()
        {
            for (int count = 1; count <= 3; count++) {
                Assert(RingGeometry.HitTest(new PointF(120, 120), count) == -1, "neutral center");
                Assert(RingGeometry.HitTest(new PointF(0, 0), count) == -1, "outside circle");
                for (int i = 0; i < count; i++) {
                    var label = RingGeometry.LabelPoint(i, count);
                    Assert(RingGeometry.HitTest(label, count) == i, "label targets correct wedge " + count + ":" + i);
                    using (var path = RingGeometry.Wedge(i, count)) Assert(path.IsVisible(label), "drawn wedge agrees with hit test");
                }
                if (count > 1) {
                    double radians = (-90 + 180.0 / count) * Math.PI / 180;
                    Assert(RingGeometry.HitTest(new PointF(120 + (float)Math.Cos(radians) * 80, 120 + (float)Math.Sin(radians) * 80), count) == -1, "separator is neutral");
                }
            }
        }
        static void CheckSettings(string root)
        {
            string path = Path.Combine(root, "settings.json");
            var settings = new AppSettings { Trigger = "Alt", Theme = "Light", JpegQuality = 72, ReducedMotion = true, Enabled = false };
            settings.Save(path);
            var restored = AppSettings.Load(path);
            Assert(restored.Trigger == "Alt" && restored.Theme == "Light" && restored.JpegQuality == 72 && restored.ReducedMotion && !restored.Enabled, "settings round trip");
            restored.Trigger = "Control"; restored.Save(path);
            Assert(AppSettings.Load(path).TriggerKey == 0x11, "settings atomically replace existing file");
            Assert(Directory.GetFiles(root, "settings.json.*.tmp").Length == 0, "settings temporary cleanup");
            File.WriteAllText(path, "not JSON");
            restored = AppSettings.Load(path);
            Assert(restored.Trigger == "Shift" && restored.Enabled && restored.JpegQuality == 90, "corrupt settings recover safe defaults");
            Assert(Directory.GetFiles(root, "settings.json.corrupt-*").Length == 1, "corrupt settings preserved");
            File.WriteAllText(path, "{\"Schema\":1}");
            Assert(AppSettings.Load(path).Enabled && AppSettings.Load(path).JpegQuality == 90, "missing fields retain defaults");
        }
        static void CheckDropData(string root)
        {
            using (var window = new MainWindow(true, new AppSettings())) {
                var data = new DataObject(DataFormats.FileDrop, new string[] { Path.GetFullPath(Path.Combine(root, "original-Jpeg.jpg")) });
                var copy = new DragEventArgs(data, 4, 0, 0, DragDropEffects.Copy | DragDropEffects.Move, DragDropEffects.None);
                Assert(window.Accept(copy) == DragDropEffects.Copy, "Shift file drag accepted only as COPY");
                var moveOnly = new DragEventArgs(data, 4, 0, 0, DragDropEffects.Move, DragDropEffects.None);
                Assert(window.Accept(moveOnly) == DragDropEffects.None, "move-only source rejected safely");
                var text = new DragEventArgs(new DataObject(DataFormats.Text, "hello"), 4, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
                Assert(window.Accept(text) == DragDropEffects.None, "text drag rejected");
            }
        }
    }
}
