using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace BetaLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (sender, args) =>
        {
            ShowError(args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                ShowError(ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            ShowError(args.Exception);
            args.SetObserved();
        };

        try
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private static void ShowError(Exception ex)
    {
        try
        {
            string message =
                "Der Kirmes Beta Launcher konnte nicht gestartet werden.\n\n" +
                ex.Message +
                "\n\nDetails:\n" +
                ex;

            MessageBox.Show(
                message,
                "Kirmes Beta Launcher - Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
    }
}
