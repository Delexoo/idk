using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Idk.Tools;

/// <summary>Builds a multi-size .ico with PNG payloads (Vista+) from a source image, clipped to a rounded square.</summary>
internal static class Program
{
    private const float CornerRadiusFraction = 0.22f;

    private static string? FindDirectoryContaining(string relativeFile)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 14 && d != null; i++)
        {
            var candidate = Path.Combine(d.FullName, relativeFile);
            if (File.Exists(candidate))
                return Path.GetDirectoryName(candidate)!;
            d = d.Parent;
        }

        return null;
    }

    private static int Main(string[] args)
    {
        var assetsDir = FindDirectoryContaining(Path.Combine("Assets", "idk.png"));
        if (assetsDir is null)
        {
            Console.Error.WriteLine("Could not find Assets/idk.png by walking up from the tool output directory.");
            return 1;
        }

        var input = args.Length > 0 ? args[0] : Path.Combine(assetsDir, "idk.png");
        var output = args.Length > 1 ? args[1] : Path.Combine(assetsDir, "idk.ico");

        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"Input not found: {input}");
            return 1;
        }

        var sizes = new[] { 256, 48, 32, 16 };
        var pngChunks = new List<byte[]>(sizes.Length);
        foreach (var s in sizes)
            pngChunks.Add(RenderRoundedPng(input, s));

        WriteIcoWithPngImages(output, pngChunks);
        Console.WriteLine($"Wrote {output}");
        return 0;
    }

    private static byte[] RenderRoundedPng(string inputPath, int size)
    {
        using var src = Image.FromFile(inputPath);
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;

        var radius = Math.Max(2f, size * CornerRadiusFraction);
        using var clip = RoundedRectPath(new RectangleF(0, 0, size, size), radius);
        g.SetClip(clip);
        g.DrawImage(src, new Rectangle(0, 0, size, size));

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static GraphicsPath RoundedRectPath(RectangleF b, float r)
    {
        var d = Math.Min(r * 2, Math.Min(b.Width, b.Height));
        var path = new GraphicsPath();
        path.AddArc(b.Left, b.Top, d, d, 180, 90);
        path.AddArc(b.Right - d, b.Top, d, d, 270, 90);
        path.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
        path.AddArc(b.Left, b.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>ICO containing one or more PNG images (Windows Vista+).</summary>
    private static void WriteIcoWithPngImages(string path, IReadOnlyList<byte[]> pngImages)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        ushort idCount = (ushort)pngImages.Count;
        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write(idCount);

        var headerSize = 6 + 16 * idCount;
        var offset = headerSize;
        var entries = new List<(byte w, byte h, int len, int off)>();
        foreach (var png in pngImages)
        {
            var dim = PngPixelDimensions(png);
            byte wb = dim <= 0 || dim > 255 ? (byte)0 : (byte)dim;
            byte hb = dim <= 0 || dim > 255 ? (byte)0 : (byte)dim;
            entries.Add((wb, hb, png.Length, offset));
            offset += png.Length;
        }

        foreach (var (wb, hb, len, off) in entries)
        {
            bw.Write(wb);
            bw.Write(hb);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)0);
            bw.Write(len);
            bw.Write(off);
        }

        foreach (var png in pngImages)
            bw.Write(png);
    }

    /// <summary>Reads width/height from minimal PNG IHDR (first IDAT not needed).</summary>
    private static int PngPixelDimensions(ReadOnlySpan<byte> png)
    {
        // PNG signature + IHDR chunk: width at 16–19, height at 20–23
        if (png.Length < 24) return 0;
        if (png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71) return 0;
        return (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
    }
}
