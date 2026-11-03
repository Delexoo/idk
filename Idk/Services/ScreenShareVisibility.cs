using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Idk.Services;

/// <summary>
/// Keeps idk windows out of screenshots, screen shares, and streams.
/// The windows stay visible on this computer.
/// </summary>
internal static class ScreenShareVisibility
{
    private const uint WdaNone = 0x0;
    private const uint WdaExcludeFromCapture = 0x00000011;

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    public static void Apply(Window window)
    {
        if (!window.Dispatcher.CheckAccess())
        {
            window.Dispatcher.Invoke(() => Apply(window));
            return;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            window.SourceInitialized -= OnSource;
            window.SourceInitialized += OnSource;
            return;
        }

        ApplyHandle(hwnd);
        return;

        void OnSource(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSource;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
                ApplyHandle(handle);
        }
    }

    /// <summary>Hide every idk window from the next screen capture, then restore with <see cref="ApplyAll"/>.</summary>
    public static void ForceExcludeAll()
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;
        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(ForceExcludeAll);
            return;
        }

        foreach (Window window in app.Windows)
            ForceExclude(window);
    }

    public static void ApplyAll()
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(ApplyAll);
            return;
        }

        foreach (Window window in app.Windows)
            Apply(window);

        foreach (PresentationSource source in PresentationSource.CurrentSources)
        {
            if (source is HwndSource hwnd && hwnd.Handle != IntPtr.Zero)
                ApplyHandle(hwnd.Handle);
        }
    }

    private static void ForceExclude(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
    }

    private static void ApplyHandle(IntPtr hwnd) =>
        SetWindowDisplayAffinity(
            hwnd,
            SettingsStore.HideFromScreenShare ? WdaExcludeFromCapture : WdaNone);
}
