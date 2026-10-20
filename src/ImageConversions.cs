using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace Citrus
{
    enum ImageTarget { Png, Jpeg, Bmp, Tiff }

    static class ImageRoutes
    {
        public static readonly ImageTarget[] All = { ImageTarget.Png, ImageTarget.Jpeg, ImageTarget.Bmp, ImageTarget.Tiff };
        public static string Label(ImageTarget target)
        {
            return target == ImageTarget.Jpeg ? "JPG" : target == ImageTarget.Tiff ? "TIFF" : target.ToString().ToUpperInvariant();
        }
        public static string Extension(ImageTarget target)
        {
            return "." + (target == ImageTarget.Jpeg ? "jpg" : target == ImageTarget.Tiff ? "tiff" : target.ToString().ToLowerInvariant());
        }
        public static ImageFormat Format(ImageTarget target)
        {
            switch (target) {
                case ImageTarget.Png: return ImageFormat.Png;
                case ImageTarget.Jpeg: return ImageFormat.Jpeg;
                case ImageTarget.Bmp: return ImageFormat.Bmp;
                case ImageTarget.Tiff: return ImageFormat.Tiff;
                default: throw new ArgumentOutOfRangeException("target");
            }
        }
        public static bool TrySource(string path, out ImageTarget format)
        {
            switch (Path.GetExtension(path).ToLowerInvariant()) {
                case ".jpg": case ".jpeg": format = ImageTarget.Jpeg; return true;
                case ".png": format = ImageTarget.Png; return true;
                case ".bmp": format = ImageTarget.Bmp; return true;
                case ".tif": case ".tiff": format = ImageTarget.Tiff; return true;
                default: format = ImageTarget.Png; return false;
            }
        }
        // Intersection: every file in the selection must support the chosen target.
        public static ImageTarget[] Targets(string[] files)
        {
            if (files == null || files.Length == 0) return new ImageTarget[0];
            var sources = new HashSet<ImageTarget>();
            foreach (string file in files) {
                ImageTarget source;
                if (!TrySource(file, out source)) return new ImageTarget[0];
                sources.Add(source);
            }
            return All.Where(target => !sources.Contains(target)).ToArray();
        }
    }

    static class Converter
    {
        public static string Convert(string source, CancellationToken token)
        {
            return Convert(source, ImageTarget.Png, 90, token);
        }

        public static string Convert(string source, ImageTarget target, int jpegQuality, CancellationToken token)
        {
            source = Path.GetFullPath(source);
            ImageTarget sourceFormat;
            if (!ImageRoutes.TrySource(source, out sourceFormat))
                throw new InvalidDataException("Choose a JPG, PNG, BMP, or single-page TIFF image.");
            if (sourceFormat == target) throw new InvalidDataException("Choose a different output format.");
            if (jpegQuality < 1 || jpegQuality > 100) throw new ArgumentOutOfRangeException("jpegQuality");
            var targetFormat = ImageRoutes.Format(target);
            string directory = Path.GetDirectoryName(source);
            string staging = Path.Combine(directory, ".citrus-" + Guid.NewGuid().ToString("N") + ".tmp");
            int width = 0, height = 0;
            try {
                token.ThrowIfCancellationRequested();
                using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var image = Decode(stream)) {
                    if (image.RawFormat.Guid != ImageRoutes.Format(sourceFormat).Guid)
                        throw new InvalidDataException("The file contents do not match its " + ImageRoutes.Label(sourceFormat) + " extension.");
                    if ((long)image.Width * image.Height > 80000000)
                        throw new InvalidDataException("Images above 80 megapixels are not supported yet.");
                    foreach (Guid dimension in image.FrameDimensionsList)
                        if (image.GetFrameCount(new FrameDimension(dimension)) > 1)
                            throw new InvalidDataException("Multi-page and animated images are not supported yet. No pages were discarded.");
                    ApplyOrientation(image);
                    width = image.Width; height = image.Height;
                    token.ThrowIfCancellationRequested();
                    bool preserveAlpha = target == ImageTarget.Png || target == ImageTarget.Tiff;
                    using (var bitmap = new Bitmap(width, height,
                        preserveAlpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb)) {
                        if (image.HorizontalResolution > 0 && image.VerticalResolution > 0)
                            bitmap.SetResolution(image.HorizontalResolution, image.VerticalResolution);
                        using (var graphics = Graphics.FromImage(bitmap)) {
                            graphics.Clear(preserveAlpha ? Color.Transparent : Color.White);
                            graphics.DrawImage(image, new Rectangle(0, 0, width, height));
                        }
                        if (target == ImageTarget.Jpeg) {
                            var encoder = ImageCodecInfo.GetImageEncoders().First(info => info.FormatID == ImageFormat.Jpeg.Guid);
                            using (var options = new EncoderParameters(1)) {
                                options.Param[0] = new EncoderParameter(Encoder.Quality, (long)jpegQuality);
                                bitmap.Save(staging, encoder, options);
                            }
                        }
                        else bitmap.Save(staging, targetFormat);
                    }
                }
                token.ThrowIfCancellationRequested();
                using (var check = Image.FromFile(staging))
                    if (check.RawFormat.Guid != targetFormat.Guid || check.Width != width || check.Height != height)
                        throw new InvalidDataException("Output validation failed.");
                string stem = Path.GetFileNameWithoutExtension(source);
                for (int i = 0; i < 10000; i++) {
                    token.ThrowIfCancellationRequested();
                    string output = Path.Combine(directory, stem + (i == 0 ? "" : " (" + i + ")") + ImageRoutes.Extension(target));
                    try { File.Move(staging, output); return output; }
                    catch (IOException) { if (!File.Exists(output)) throw; }
                }
                throw new IOException("Too many files with the same name.");
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }

        static Image Decode(Stream stream)
        {
            try { return Image.FromStream(stream, true, true); }
            catch (ArgumentException ex) {
                throw new InvalidDataException("This file is damaged or is not a valid image. Choose another image.", ex);
            }
        }

        static void ApplyOrientation(Image image)
        {
            if (Array.IndexOf(image.PropertyIdList, 0x112) < 0) return;
            byte[] value = image.GetPropertyItem(0x112).Value;
            if (value.Length < 2) return;
            int orientation = BitConverter.ToUInt16(value, 0);
            RotateFlipType[] transforms = {
                RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipNone,
                RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate180FlipNone,
                RotateFlipType.Rotate180FlipX, RotateFlipType.Rotate90FlipX,
                RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX,
                RotateFlipType.Rotate270FlipNone
            };
            if (orientation > 0 && orientation < transforms.Length) image.RotateFlip(transforms[orientation]);
        }
    }
}
