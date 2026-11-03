using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Idk.Services;

[SupportedOSPlatform("windows")]
public static class ScreenCaptureService
{
    public static string? CapturePrimaryAsBase64Png(int maxEdge = 1280)
    {
        try
        {
            var screen = Screen.PrimaryScreen;
            if (screen == null) return null;
            var bounds = screen.Bounds;
            var w = bounds.Width;
            var h = bounds.Height;
            var scale = Math.Min(1.0, maxEdge / (double)Math.Max(w, h));
            var tw = Math.Max(320, (int)(w * scale));
            var th = Math.Max(240, (int)(h * scale));

            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            }

            using var scaled = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
            using (var g2 = Graphics.FromImage(scaled))
            {
                g2.DrawImage(bmp, 0, 0, tw, th);
            }

            using var ms = new MemoryStream();
            scaled.Save(ms, ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }
}
