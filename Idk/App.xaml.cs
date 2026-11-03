using System.Windows;
using Idk.Services;
using Idk.Views;

namespace Idk;

public partial class App : System.Windows.Application
{
    internal static OverlayWindow? Overlay { get; private set; }

    internal static bool IsIdkAssistantRunning { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstanceGuard.TryAcquire())
        {
            SingleInstanceGuard.TryFocusExistingInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        HookGlobalExceptionHandlers();
        WebsiteBotEnvLoader.ApplyIfPresent();
        SettingsStore.ApplyToProcess();
        ThemeManager.ApplyFromSettings();
        LicenseService.Changed += () =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (LicenseService.IsLicensed)
                    EnsureIdkAssistantRunning();
            });
        };
        LicenseService.Load();

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var shell = new AppShellWindow();
        MainWindow = shell;
        shell.Show();
        shell.Activate();
        EnsureIdkAssistantRunning();
    }

    private void HookGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write(args.Exception, "UI thread");
            args.Handled = true;
            try
            {
                ModernDialog.Alert(
                    MainWindow,
                    "idk",
                    "idk hit an error and kept running. Details were saved to crash.log in your idk data folder.");
            }
            catch
            {
                /* ignore */
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                CrashLog.Write(ex, "AppDomain");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write(args.Exception, "Task");
            args.SetObserved();
        };
    }

    /// <summary>Starts overlay + selection listener when an API key is stored.</summary>
    internal static void EnsureIdkAssistantRunning()
    {
        if (!OpenRouterService.IsReady) return;
        StartIdkAssistant();
    }

    internal static void StartIdkAssistant()
    {
        if (IsIdkAssistantRunning) return;
        Overlay ??= new OverlayWindow();
        TrayService.Initialize();
        MouseInteractionService.Start();
        IsIdkAssistantRunning = true;
    }

    internal static void NotifyOverlaySettingsChanged()
    {
        Overlay?.RefreshChromeSettings();
        ScreenShareVisibility.ApplyAll();
    }

    internal static void StopIdkAssistant()
    {
        if (!IsIdkAssistantRunning) return;
        MouseInteractionService.Stop();
        TrayService.Dispose();
        Overlay?.HideOverlay();
        Overlay?.Close();
        Overlay = null;
        IsIdkAssistantRunning = false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopIdkAssistant();
        SingleInstanceGuard.Release();
        base.OnExit(e);
    }
}
