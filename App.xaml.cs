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

        // Fängt WPF UI-Thread-Fehler ab
        DispatcherUnhandledException += (sender, args) =>
        {
            ShowError(args.Exception);
            args.Handled = true;
        };

        // Fängt allgemeine AppDomain-Fehler ab
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                ShowError(ex);
            }
        };

        // Fängt unbestimmte Task-Fehler ab
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            ShowError(args.Exception);
            args.SetObserved();
        };

        // Startet das Hauptfenster
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
                "Der Kirmes Beta Launcher konnte nicht gestartet werden oder ist unerwartet abgestürzt.\n\n" +
                ex.Message +
                "\n\nDetails:\n" +
                ex;

            // Optional: Logdatei im lokalen Anwendungsordner schreiben
            string logFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RealisticFunfairGames"
            );

            Directory.CreateDirectory(logFolder);
            string logPath = Path.Combine(logFolder, "crashlog.txt");

            File.WriteAllText(logPath, $"[{DateTime.Now}] CRASH LOG:\n{ex}\n\n");

            MessageBox.Show(
                message,
                "Kirmes Beta Launcher - Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Fallback, falls die MessageBox oder das Dateisystem fehlschlägt
        }
    }
}
