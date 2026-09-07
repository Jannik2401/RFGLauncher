using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace BetaLauncher;

public partial class UpdateWindow : Window
{
    private readonly string DownloadUrl;
    private readonly string TargetExePath;

    public UpdateWindow(string downloadUrl, string targetExePath)
    {
        InitializeComponent();
        DownloadUrl = downloadUrl;
        TargetExePath = targetExePath;

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
            StatusText.Text = "Lade Launcher-Update herunter...";
            string tempZip = Path.Combine(Path.GetTempPath(), "RFG_Launcher_Update.zip");
            string extractPath = Path.Combine(Path.GetTempPath(), "RFG_Launcher_Extracted");

            if (File.Exists(tempZip)) File.Delete(tempZip);
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher-Updater");
                using HttpResponseMessage response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                await using Stream input = await response.Content.ReadAsStreamAsync();
                await using FileStream output = new(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);

                byte[] buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await output.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double percentage = Math.Min(100, totalRead * 100.0 / totalBytes.Value);
                        ProgressBar.Value = percentage;
                    }
                }
            }

            StatusText.Text = "Entpacke Update...";
            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(tempZip, extractPath, true);

            StatusText.Text = "Wende Update an...";
            await Task.Delay(1000);

            string targetDir = Path.GetDirectoryName(TargetExePath) ?? AppContext.BaseDirectory;

            foreach (string filePath in Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(extractPath, filePath);
                string destinationPath = Path.Combine(targetDir, relativePath);

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                if (string.Equals(Path.GetFileName(destinationPath), Path.GetFileName(TargetExePath), StringComparison.OrdinalIgnoreCase))
                {
                    string backupPath = TargetExePath + ".bak";
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    if (File.Exists(TargetExePath)) File.Move(TargetExePath, backupPath, true);
                }

                File.Copy(filePath, destinationPath, true);
            }

            StatusText.Text = "Update erfolgreich! Starte neu...";
            await Task.Delay(1000);

            Process.Start(new ProcessStartInfo
            {
                FileName = TargetExePath,
                UseShellExecute = true
            });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler beim Update des Launchers: " + ex.Message, "Update-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }
}
