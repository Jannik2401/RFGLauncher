using System;
using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace BetaLauncher
{
    public partial class UpdateWindow : Window
    {
        private readonly string _downloadUrl;
        private readonly string _currentExePath;

        public UpdateWindow(string downloadUrl, string currentExePath)
        {
            InitializeComponent();
            _downloadUrl = downloadUrl;
            _currentExePath = currentExePath;

            Loaded += UpdateWindow_Loaded;
        }

        private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await PerformUpdateAsync();
        }

        private async Task PerformUpdateAsync()
        {
            try
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "RFG_Launcher_Update.exe");

                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10);
                    using HttpResponseMessage response = await client.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;
                    await using Stream input = await response.Content.ReadAsStreamAsync();
                    await using FileStream output = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);

                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            double percentage = (double)totalRead / totalBytes.Value * 100;
                            Dispatcher.Invoke(() =>
                            {
                                ProgressBar.Value = percentage;
                                PercentageText.Text = $"{percentage:F0}%";
                            });
                        }
                    }
                }

                StatusText.Text = "Aktualisiere Launcher...";

                // Batch-Skript zum Ersetzen der alten EXE nach dem Schließen
                string batchFile = Path.Combine(Path.GetTempPath(), "update_launcher.bat");
                string batchContent = $@"
@echo off
timeout /t 2 /nobreak > nul
move /y ""{tempFile}"" ""{_currentExePath}""
start """" ""{_currentExePath}""
del ""%~f0""
";
                await File.WriteAllTextAsync(batchFile, batchContent);

                Process.Start(new ProcessStartInfo
                {
                    FileName = batchFile,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Aktualisieren des Launchers:\n" + ex.Message, "Update-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }
    }
}
