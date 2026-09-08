using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace CliManager.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("UI Dispatcher Error", e.Exception);
        MessageBox.Show($"应用程序发生未捕获异常：\n\n{e.Exception.Message}\n\n详细信息已记录至 crash.log", "CliManager 错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException("AppDomain Unhandled Error", ex);
            MessageBox.Show($"应用程序发生严重异常：\n\n{ex.Message}\n\n详细信息已记录至 crash.log", "CliManager 严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("Unobserved Task Error", e.Exception);
        e.SetObserved();
    }

    private static void LogException(string category, Exception ex)
    {
        try
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{category}]\n{ex}\n\n";
            File.AppendAllText(logPath, text);
        }
        catch
        {
            // Ignore logging failures
        }
    }
}

