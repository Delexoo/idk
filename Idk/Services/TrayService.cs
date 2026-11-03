using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using Idk.Views;

namespace Idk.Services;

public static class TrayService
{
    private static NotifyIcon? _ni;
    private static ContextMenuStrip? _menu;
    private static Icon? _trayIconOwned;

    public static void Initialize()
    {
        if (_ni != null) return;

        _menu = new ContextMenuStrip();
        _menu.Items.Add("Open dashboard", null, (_, _) => OpenDashboard());

        _menu.Items.Add(new ToolStripSeparator());

        var launch = new ToolStripMenuItem("Launch at login")
        {
            Checked = SettingsStore.IsLaunchAtLoginEnabled(),
            CheckOnClick = true
        };
        launch.Click += (_, _) => SettingsStore.SetLaunchAtLogin(launch.Checked);

        _menu.Items.Add(launch);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Quit idk", null, (_, _) =>
        {
            global::Idk.App.StopIdkAssistant();
            Application.Current.Shutdown();
        });

        _trayIconOwned = TryLoadEmbeddedTrayIcon() ?? TryExeTrayIcon();
        _ni = new NotifyIcon
        {
            Visible = true,
            Icon = _trayIconOwned ?? SystemIcons.Application,
            Text = "idk — double-click to open dashboard",
            ContextMenuStrip = _menu
        };
        _ni.DoubleClick += (_, _) => OpenDashboard();
    }

    private static Icon? TryLoadEmbeddedTrayIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/idk.ico", UriKind.Absolute));
            if (info?.Stream == null) return null;
            using (info.Stream)
            {
                using var ms = new MemoryStream();
                info.Stream.CopyTo(ms);
                ms.Position = 0;
                using var src = new Icon(ms);
                return (Icon)src.Clone();
            }
        }
        catch
        {
            return null;
        }
    }

    private static Icon? TryExeTrayIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return null;
            return Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            return null;
        }
    }

    private static void OpenDashboard()
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Application.Current.MainWindow is AppShellWindow shell)
            {
                shell.PresentFromTray();
                return;
            }

            var w = new AppShellWindow();
            Application.Current!.MainWindow = w;
            w.PresentFromTray();
        }));
    }

    public static void Dispose()
    {
        if (_ni != null)
        {
            _ni.Icon = null;
            _ni.Dispose();
        }

        _menu?.Dispose();
        _trayIconOwned?.Dispose();
        _ni = null;
        _menu = null;
        _trayIconOwned = null;
    }
}
