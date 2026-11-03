using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Idk.Services;

/// <summary>
/// Registers a system-wide Escape hotkey bound to this app's overlay HWND while the hook is active.
/// Unregister when the overlay hides so Escape works normally elsewhere.
/// </summary>
public static class GlobalEscapeHotkey
{
    private const int WmHotkey = 0x0312;
    private const int VkEscape = 0x1B;
    private const int HotkeyId = 0x4944;

    private static IntPtr _hwnd = IntPtr.Zero;
    private static HwndSource? _source;
    private static HwndSourceHook? _hook;

    public static void Attach(Window window)
    {
        if (_hook != null) return;
        var helper = new WindowInteropHelper(window);
        var hwnd = helper.EnsureHandle();
        if (hwnd == IntPtr.Zero) return;
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        if (_source == null) return;
        _hook = WndProc;
        _source.AddHook(_hook);
    }

    public static void Detach()
    {
        TryUnregister();
        if (_source != null && _hook != null)
        {
            _source.RemoveHook(_hook);
            _hook = null;
        }

        _source = null;
        _hwnd = IntPtr.Zero;
    }

    public static bool TryRegister()
    {
        if (_hwnd == IntPtr.Zero) return false;
        TryUnregister();
        return RegisterHotKey(_hwnd, HotkeyId, 0, VkEscape);
    }

    public static void TryUnregister()
    {
        if (_hwnd == IntPtr.Zero) return;
        UnregisterHotKey(_hwnd, HotkeyId);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey || wParam.ToInt32() != HotkeyId) return IntPtr.Zero;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (global::Idk.App.Overlay is { IsVisible: true } o)
                    o.EscapeOrDismiss();
            }));
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
