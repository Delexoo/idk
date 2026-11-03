using System.ComponentModel;
using System.Windows;
using Idk.Services;

namespace Idk.Views;

/// <summary>Single main window: hosts the dashboard.</summary>
public partial class AppShellWindow : Window
{
    internal static AppShellWindow? Instance { get; private set; }

    public AppShellWindow()
    {
        InitializeComponent();
        Instance = this;
        WindowsRoundedWindowCorners.TryApply(this);
        Loaded += OnShellLoaded;
        Closing += Shell_Closing;
        Closed += Shell_Closed;
        Activated += Shell_Activated;
    }

    private void OnShellLoaded(object sender, RoutedEventArgs e)
    {
        Host.Content = new DashboardView();
        SettingsStore.ApplyMainWindowTaskbarVisibility(this);
    }

    private void Shell_Activated(object? sender, EventArgs e)
    {
        if (Host.Content is DashboardView d)
            d.RefreshChatHistory();
    }

    private void Shell_Closing(object? sender, CancelEventArgs e)
    {
        if (Host.Content is not DashboardView dash) return;

        try
        {
            dash.FlushPersistForShellClose();
        }
        catch
        {
            /* ignore */
        }

        if (global::Idk.App.IsIdkAssistantRunning)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void Shell_Closed(object? sender, EventArgs e) =>
        System.Windows.Application.Current.Shutdown();

    /// <summary>Tray: show the single shell maximized.</summary>
    public void PresentFromTray()
    {
        WindowState = WindowState.Maximized;
        Show();
        Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
        base.OnClosed(e);
    }
}
