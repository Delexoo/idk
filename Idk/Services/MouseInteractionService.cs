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
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const byte VkControl = 0x11;
    private const byte VkC = 0x43;
    private const uint KeyeventfKeyup = 0x0002;
    private const int MinDragPixels = 12;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static IntPtr _hook = IntPtr.Zero;
    private static HookProc? _proc;
    private static DispatcherTimer? _debounce;
    private static DispatcherTimer? _copyTimer;
    private static int _captureGeneration;
    private static string _lastKey = "";
    private static DateTime _lastDup = DateTime.MinValue;
    private static Point _downPoint;
    private static bool _sawDown;
    private static Point _upPoint;
    private static double _dragPixels;
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
        _copyTimer?.Stop();
        _copyTimer = null;
        _proc = null;
        _suppressedSelection = null;
        _sawDown = false;
    }

    public static void SuppressReshowForSelection(string selectionText)
    {
        var t = selectionText.Trim();
        _suppressedSelection = t.Length >= 2 ? t : null;
    }

    public static void ClearReshowSuppression() => _suppressedSelection = null;

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmLButtonDown || wParam == (IntPtr)WmLButtonUp))
        {
            var pt = ReadHookPoint(lParam);
            var isDown = wParam == (IntPtr)WmLButtonDown;
            SettingsStore.NoteSelectionGesture(isDown);
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (isDown)
                    {
                        _downPoint = pt;
                        _sawDown = true;
                        _captureGeneration++;
                        _copyTimer?.Stop();
                        return;
                    }

                    _upPoint = pt;
                    _dragPixels = _sawDown ? Distance(_downPoint, pt) : 0;
                    _debounce?.Stop();
                    _debounce?.Start();
                }));
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static void TryShowOverlayFromSelection()
    {
        if (!SettingsStore.SelectionGestureWasActivated)
            return;

        var cursor = _upPoint;
        var text = SelectionTextService.TryGetSelectedTextNear(cursor)?.Trim();
        if (!string.IsNullOrEmpty(text) && text.Length >= 2)
        {
            PresentSelection(text, cursor);
            return;
        }

        if (_dragPixels < MinDragPixels || IsIdkForeground())
        {
            NoteSelectionCleared();
            return;
        }

        if (!TryBackupClipboard(out var backup))
        {
            NoteSelectionCleared();
            return;
        }

        var generation = ++_captureGeneration;
        SendCtrlC();
        var attempts = 0;
        void ReadCopiedSelection()
        {
            _copyTimer?.Stop();
            if (generation != _captureGeneration)
            {
                RestoreClipboard(backup);
                return;
            }

            var copied = TryReadClipboardText()?.Trim();
            if (!string.IsNullOrEmpty(copied) && copied.Length >= 2 && copied != backup.Text)
            {
                RestoreClipboard(backup);
                PresentSelection(copied, cursor);
                return;
            }

            if (attempts++ < 2)
            {
                _copyTimer?.Start();
                return;
            }

            RestoreClipboard(backup);
            NoteSelectionCleared();
        }

        _copyTimer?.Stop();
        _copyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _copyTimer.Tick += (_, _) => ReadCopiedSelection();
        _copyTimer.Start();
    }

    private static void NoteSelectionCleared()
    {
        ClearReshowSuppression();
        global::Idk.App.Overlay?.TryDismissIfHighlightCleared();
    }

    private static void PresentSelection(string text, Point cursor)
    {
        if (_suppressedSelection != null
            && string.Equals(text, _suppressedSelection, StringComparison.Ordinal))
            return;

        var now = DateTime.UtcNow;
        var key = text + "\0";
        if (key == _lastKey && (now - _lastDup).TotalMilliseconds < 900) return;
        _lastKey = key;
        _lastDup = now;

        SelectionTextService.TryGetSelectionScreenBounds(out var selBounds);
        AutomationElement? sourceEl = null;
        try
        {
            sourceEl = AutomationElement.FocusedElement;
        }
        catch
        {
            sourceEl = null;
        }

        global::Idk.App.Overlay?.ShowForSelection(text, null, selBounds, cursor, sourceEl);
    }

    private static Point ReadHookPoint(IntPtr lParam)
    {
        try
        {
            var info = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            return new Point(info.X, info.Y);
        }
        catch
        {
            return new Point(Cursor.Position.X, Cursor.Position.Y);
        }
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool IsIdkForeground()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(fg, out var pid);
        return pid == Environment.ProcessId;
    }

    private readonly struct ClipboardBackup(System.Windows.IDataObject? data, string? text)
    {
        public System.Windows.IDataObject? Data { get; } = data;
        public string? Text { get; } = text;
    }

    private static bool TryBackupClipboard(out ClipboardBackup backup)
    {
        backup = default;
        try
        {
            var data = System.Windows.Clipboard.GetDataObject();
            string? text = null;
            if (System.Windows.Clipboard.ContainsText())
                text = System.Windows.Clipboard.GetText();
            backup = new ClipboardBackup(data, text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryReadClipboardText()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText()) return null;
            var text = System.Windows.Clipboard.GetText();
            return text.Length > 20000 ? text[..20000] : text;
        }
        catch
        {
            return null;
        }
    }

    private static void RestoreClipboard(ClipboardBackup backup)
    {
        try
        {
            if (backup.Data != null)
                System.Windows.Clipboard.SetDataObject(backup.Data, true);
        }
        catch
        {
            try
            {
                if (backup.Text != null)
                    System.Windows.Clipboard.SetText(backup.Text);
            }
            catch
            {
                /* the selection is already captured; leave the clipboard if it cannot be restored */
            }
        }
    }

    private static void SendCtrlC()
    {
        var scanCtrl = (byte)MapVirtualKey(VkControl, 0);
        var scanC = (byte)MapVirtualKey(VkC, 0);
        keybd_event(VkControl, scanCtrl, 0, UIntPtr.Zero);
        keybd_event(VkC, scanC, 0, UIntPtr.Zero);
        keybd_event(VkC, scanC, KeyeventfKeyup, UIntPtr.Zero);
        keybd_event(VkControl, scanCtrl, KeyeventfKeyup, UIntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public int X;
        public int Y;
        public int MouseData;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
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

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
