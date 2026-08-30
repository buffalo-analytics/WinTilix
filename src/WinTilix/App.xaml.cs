using System.Windows;
using System.Windows.Threading;
using WinTilix.Diagnostics;

namespace WinTilix;

public partial class App : Application
{
    private static readonly Version Version =
        typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        DebugLog.Info($"Starting WinTilix {Version}");
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DebugLog.Error("Dispatcher unhandled exception", e.Exception);
        e.Handled = true;
        PromptDebugReport(e.Exception);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            DebugLog.Error("Domain unhandled exception", exception);
            try
            {
                DebugLog.WriteReport(Version, exception, "UnhandledException");
            }
            catch
            {
            }
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        DebugLog.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private void PromptDebugReport(Exception exception)
    {
        var send = MessageBox.Show(
            MainWindow,
            "WinTilix hit an unexpected error.\n\n" + exception.Message +
            "\n\nSend a debug report to GitHub so it can be inspected?",
            "WinTilix",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (send != MessageBoxResult.Yes)
        {
            return;
        }

        new DebugReportWindow(Version, exception, "Unhandled UI exception")
        {
            Owner = MainWindow,
        }.ShowDialog();
    }
}
