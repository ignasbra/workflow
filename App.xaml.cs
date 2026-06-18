using System.Windows;
using System.Windows.Threading;

namespace PrReviewHelper;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            MessageBox.Show(args.ExceptionObject?.ToString(), "Unhandled (non-UI)",
                MessageBoxButton.OK, MessageBoxImage.Error);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "Unhandled (task)",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.SetObserved();
        };
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "Unhandled (UI)",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

