using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace Idk.Services;

public static class MouseInteractionService
{
    private const int WhMouseLl = 14;
    private const int WmLButtonUp = 0x0202;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static IntPtr _hook = IntPtr.Zero;
    private static HookProc? _proc;
    private static DispatcherTimer? _debounce;
    private static string _lastKey = "";
    private static DateTime _lastDup = DateTime.MinValue;
    /// <summary>After the user closes the overlay, ignore the same highlight until it is cleared or changed.</summary>
    private static string? _suppressedSelection;

    public static void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookCallback;
        var h = GetModuleHandle(null);
        _hook = SetWindowsHookEx(WhMouseLl, _proc, h, 0);

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += (_, _) =>
        {
            _debounce!.Stop();
            TryShowOverlayFromSelection();
        };
    }

    public static void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _debounce?.Stop();
        _debounce = null;
        _proc = null;
        _suppressedSelection = null;
    }

    public static void SuppressReshowForSelection(string selectionText)
    {
        var t = selectionText.Trim();
        _suppressedSelection = t.Length >= 2 ? t : null;
    }

    public static void ClearReshowSuppression() => _suppressedSelection = null;

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WmLButtonUp)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    _debounce?.Stop();
                    _debounce?.Start();
                }));
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static void TryShowOverlayFromSelection()
    {
        if (!SettingsStore.IsSelectionActivationSatisfied())
            return;

        var text = SelectionTextService.TryGetSelectedText()?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length < 2)
        {
            ClearReshowSuppression();
            global::Idk.App.Overlay?.TryDismissIfHighlightCleared();
            return;
        }

        if (_suppressedSelection != null
            && string.Equals(text, _suppressedSelection, StringComparison.Ordinal))
            return;

        var now = DateTime.UtcNow;
        var key = text + "\0";
        if (key == _lastKey && (now - _lastDup).TotalMilliseconds < 900) return;
        _lastKey = key;
        _lastDup = now;

        var cursor = new Point(Cursor.Position.X, Cursor.Position.Y);
        SelectionTextService.TryGetSelectionScreenBounds(out var selBounds);
        var sourceEl = AutomationElement.FocusedElement;
        global::Idk.App.Overlay?.ShowForSelection(text, null, selBounds, cursor, sourceEl);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

}
