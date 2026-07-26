using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Eidolon.App.Logging;

namespace Eidolon.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Default logs: %APPDATA%/Eidolon/ (size-capped). --debug uses ./Logs unrestricted.
        var cwd = Directory.GetCurrentDirectory();
        AppLog.Initialize(e.Args, cwd);
        AppSettings.Load();

        // Global exception hooks
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        AppLog.Info($"OnStartup begin. base={AppContext.BaseDirectory}");
        try
        {
            base.OnStartup(e);
            AppLog.Info("OnStartup base completed.");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "OnStartup failed");
            MessageBox.Show(
                Localization.SR.Format("App.StartupFailed", AppLog.LogFilePath, ex.Message),
                "Eidolon",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info($"OnExit code={e.ApplicationExitCode}");
        AppLog.Shutdown();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error(e.Exception, "DispatcherUnhandledException");
        try
        {
            MessageBox.Show(
                Localization.SR.Format("App.UnhandledError", AppLog.LogFilePath, e.Exception.Message),
                "Eidolon",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { /* ignore */ }
        e.Handled = true; // keep process alive when possible
    }

    private static void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            AppLog.Error(ex, $"DomainUnhandledException terminating={e.IsTerminating}");
        else
            AppLog.Error($"DomainUnhandledException: {e.ExceptionObject}", "App");
        AppLog.Shutdown();
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error(e.Exception, "UnobservedTaskException");
        e.SetObserved();
    }
}
