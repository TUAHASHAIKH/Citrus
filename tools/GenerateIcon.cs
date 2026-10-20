using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Citrus;

static class GenerateIcon
{
    static void Main(string[] args)
    {
        Directory.CreateDirectory(args[0]);
        int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };
        var frames = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using (var bitmap = new Bitmap(sizes[i], sizes[i], PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var memory = new MemoryStream())
            {
                graphics.Clear(Color.Transparent);
                Brand.Draw(graphics, new RectangleF(0, 0, sizes[i], sizes[i]));
                bitmap.Save(memory, ImageFormat.Png);
                frames[i] = memory.ToArray();
                if (sizes[i] == 256) bitmap.Save(Path.Combine(args[0], "citrus.png"), ImageFormat.Png);
            }
        }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(args[0], "citrus.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset);
                offset += frames[i].Length;
            }
            foreach (byte[] frame in frames) writer.Write(frame);
        }
    }
}
