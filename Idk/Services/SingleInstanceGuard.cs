using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Idk.Services;

/// <summary>Ensures only one idk process runs; focuses the existing window on duplicate launch.</summary>
public static class SingleInstanceGuard
{
    private const string MutexName = @"Global\idk-desktop-single-instance";
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var createdNew);
        return createdNew;
    }

    public static void TryFocusExistingInstance()
    {
        var selfId = Environment.ProcessId;
        foreach (var proc in Process.GetProcessesByName("idk"))
        {
            if (proc.Id == selfId)
            {
                proc.Dispose();
                continue;
            }

            try
            {
                var hwnd = proc.MainWindowHandle;
                if (hwnd == IntPtr.Zero)
                    continue;

                if (IsIconic(hwnd))
                    ShowWindow(hwnd, SwRestore);

                SetForegroundWindow(hwnd);
                return;
            }
            catch
            {
                /* ignore stale / inaccessible process */
            }
            finally
            {
                proc.Dispose();
            }
        }
    }

    public static void Release()
    {
        if (_mutex == null) return;
        try
        {
            _mutex.ReleaseMutex();
        }
        catch
        {
            /* already released */
        }

        _mutex.Dispose();
        _mutex = null;
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
}
