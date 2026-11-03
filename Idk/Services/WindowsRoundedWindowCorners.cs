using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Idk.Services;

/// <summary>
/// Prefer rounded window chrome on Windows 11+ (reduces the default sharp rectangle frame).
/// </summary>
internal static class WindowsRoundedWindowCorners
{
    private const int DwmwaWindowCornerPreference = 33;
    /// <summary>DWMWA_BORDER_COLOR — removes the default light frame around small-rounded layered windows.</summary>
    private const int DwmwaBorderColor = 34;

    /// <summary>DWMWINDOWATTRIBUTE_COLOR_NONE (dwmapi.h).</summary>
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    /// <summary>DWM_WINDOW_CORNER_PREFERENCE: DWMWCP_ROUND</summary>
    private const int DwmwcpRound = 2;

    /// <summary>DWM_WINDOW_CORNER_PREFERENCE: DWMWCP_ROUNDSMALL</summary>
    private const int DwmwcpRoundSmall = 3;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <param name="suppressDwmBorder">When true, clear the system border color (overlay pill: avoid gray rectangle around rounded chrome).</param>
    public static void TryApply(Window window, bool smallRadius = false, bool suppressDwmBorder = false)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        void Apply()
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            if (hwnd == IntPtr.Zero) return;
            var pref = smallRadius ? DwmwcpRoundSmall : DwmwcpRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref pref, sizeof(int));
            if (suppressDwmBorder)
            {
                var none = DwmColorNone;
                _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(int));
            }
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Apply();
        else
            window.SourceInitialized += OnSource;

        void OnSource(object? o, EventArgs e)
        {
            window.SourceInitialized -= OnSource;
            Apply();
        }
    }
}
