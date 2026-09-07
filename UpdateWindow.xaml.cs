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
        InitializeComponent();[cite: 3]
        DownloadUrl = downloadUrl;[cite: 3]
        TargetExePath = targetExePath;[cite: 3]

        Loaded += UpdateWindow_Loaded;[cite: 3]
    }

    private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await PerformUpdateAsync();[cite: 3]
    }

    private async Task PerformUpdateAsync()
    {
        try
        {
            StatusText.Text = "Lade Launcher-Update herunter...";[cite: 3]
            string tempZip = Path.Combine(Path.GetTempPath(), "RFG_Launcher_Update.zip");[cite: 3]
            string extractPath = Path.Combine(Path.GetTempPath(), "RFG_Launcher_Extracted");[cite: 3]

            if (File.Exists(tempZip)) File.Delete(tempZip);[cite: 3]
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);[cite: 3]

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher-Updater");[cite: 3]
                using HttpResponseMessage response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);[cite: 3]
                response.EnsureSuccessStatusCode();[cite: 3]

                if (response.Content.Headers.ContentType?.MediaType?.Contains("html") == true)
                {
                    throw new Exception("Der Download-Link verweist auf eine Webseite statt auf eine ZIP-Datei.");
                }

                long? totalBytes = response.Content.Headers.ContentLength;[cite: 3]
                await using Stream input = await response.Content.ReadAsStreamAsync();[cite: 3]
                await using FileStream output = new(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);[cite: 3]

                byte[] buffer = new byte[81920];[cite: 3]
                long totalRead = 0;[cite: 3]
                int bytesRead;[cite: 3]

                while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await output.WriteAsync(buffer, 0, bytesRead);[cite: 3]
                    totalRead += bytesRead;[cite: 3]
                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double percentage = Math.Min(100, totalRead * 100.0 / totalBytes.Value);[cite: 3]
                        Dispatcher.Invoke(() =>
                        {
                            ProgressBar.Value = percentage;[cite: 3]
                        });
                    }
                }
            }

            FileInfo fi = new FileInfo(tempZip);
            if (!fi.Exists || fi.Length < 100)
            {
                throw new Exception("Die heruntergeladene Datei ist leer oder beschädigt.");
            }

            using (var zipCheck = ZipFile.OpenRead(tempZip))
            {
                if (zipCheck.Entries.Count == 0)
                {
                    throw new Exception("Die ZIP-Datei enthält keine Einträge.");
                }
            }

            StatusText.Text = "Entpacke Update...";[cite: 3]
            Directory.CreateDirectory(extractPath);[cite: 3]
            ZipFile.ExtractToDirectory(tempZip, extractPath, true);[cite: 3]

            StatusText.Text = "Wende Update an...";[cite: 3]
            await Task.Delay(1000);[cite: 3]

            string targetDir = Path.GetDirectoryName(TargetExePath) ?? AppContext.BaseDirectory;[cite: 3]

            foreach (string filePath in Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories))[cite: 3]
            {
                string relativePath = Path.GetRelativePath(extractPath, filePath);[cite: 3]
                string destinationPath = Path.Combine(targetDir, relativePath);[cite: 3]

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);[cite: 3]

                if (string.Equals(Path.GetFileName(destinationPath), Path.GetFileName(TargetExePath), StringComparison.OrdinalIgnoreCase))[cite: 3]
                {
                    string backupPath = TargetExePath + ".bak";[cite: 3]
                    if (File.Exists(backupPath)) File.Delete(backupPath);[cite: 3]
                    if (File.Exists(TargetExePath)) File.Move(TargetExePath, backupPath, true);[cite: 3]
                }

                File.Copy(filePath, destinationPath, true);[cite: 3]
            }

            StatusText.Text = "Update erfolgreich! Starte neu...";[cite: 3]
            await Task.Delay(1000);[cite: 3]

            Process.Start(new ProcessStartInfo
            {
                FileName = TargetExePath,
                UseShellExecute = true
            });[cite: 3]

            Application.Current.Shutdown();[cite: 3]
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler beim Update des Launchers: " + ex.Message, "Update-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 3]
            Close();[cite: 3]
        }
    }
}
