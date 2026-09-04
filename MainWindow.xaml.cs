using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BetaLauncher;

public partial class MainWindow : Window
{
    private static readonly string CurrentLauncherVersion = 
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    private const string LauncherVersionUrl = "https://raw.githubusercontent.com/Jannik2401/RFGLauncher/main/version.json";

    private const string GitHubOwner = "Jannik2401";
    private const string GitHubRepo = "RFGLauncher";
    private const string GameExeName = "kirmes.exe";
    private const string AccountServerUrl = "http://node1.waifly.com:25433";

    private const string ProtectedAdminUsername = "admin";

    // Social Media Links
    private const string DiscordUrl = "https://discord.gg/qaxg7UdafU";
    private const string InstagramUrl = "https://www.instagram.com/realistic_funfair_games/";
    private const string TikTokUrl = "https://www.tiktok.com/@realisticfunfairgames";
    private const string TwitchUrl = "https://www.twitch.tv/realistic_funfair_games";

    private string GameDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RealisticFunfairGames",
        "Game"
    );

    private string VersionFile => Path.Combine(GameDirectory, "version.txt");
    private string DigestFile => Path.Combine(GameDirectory, "game.digest");

    private readonly HttpClient Http = new();
    private DispatcherTimer? PerformanceTimer;

    private string? LoggedInUsername;
    private string? LoggedInPassword;
    private string? LoggedInRole;
    private bool HasBetaAccess;

    public MainWindow()
    {
        InitializeComponent();

        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher/1.0");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        Http.Timeout = TimeSpan.FromMinutes(30);

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(GameDirectory);

            ShowPage(HomePage);
            UpdateHomeInformation();
            StartPerformanceMonitor();

            LauncherVersionText.Text = $"Installierte Version: {CurrentLauncherVersion}";

            await SilentCheckLauncherUpdateAsync();
            await CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Launcher-Fehler: " + ex.Message;
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        PerformanceTimer?.Stop();
        Http.Dispose();
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Link konnte nicht geöffnet werden:\n" + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DiscordButton_Click(object sender, RoutedEventArgs e) => OpenUrl(DiscordUrl);
    private void YouTubeButton_Click(object sender, RoutedEventArgs e) => OpenUrl(TwitchUrl); // Öffnet Twitch
    private void InstagramButton_Click(object sender, RoutedEventArgs e) => OpenUrl(InstagramUrl);
    private void TikTokButton_Click(object sender, RoutedEventArgs e) => OpenUrl(TikTokUrl);
    private void TwitchButton_Click(object sender, RoutedEventArgs e) => OpenUrl(TwitchUrl);

    private async Task SilentCheckLauncherUpdateAsync()
    {
        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher-Updater");

            var info = await client.GetFromJsonAsync<LauncherVersionInfo>(LauncherVersionUrl);

            if (info != null && !string.IsNullOrWhiteSpace(info.Version))
            {
                Version onlineVersion = ParseVersion(info.Version);
                Version installedVersion = ParseVersion(CurrentLauncherVersion);

                if (onlineVersion > installedVersion)
                {
                    LauncherUpdateStatusText.Visibility = Visibility.Visible;
                    LauncherUpdateStatusText.Text = $"Neues Launcher-Update verfügbar: Version {onlineVersion}";
                    LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#38BDF8")!;
                }
            }
        }
        catch
        {
        }
    }

    private async Task ManualCheckLauncherUpdateAsync()
    {
        try
        {
            LauncherUpdateStatusText.Visibility = Visibility.Visible;
            LauncherUpdateStatusText.Text = "Suche nach Launcher-Updates...";
            LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#94A3B8")!;

            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher-Updater");

            var info = await client.GetFromJsonAsync<LauncherVersionInfo>(LauncherVersionUrl);

            if (info != null && !string.IsNullOrWhiteSpace(info.Version))
            {
                Version onlineVersion = ParseVersion(info.Version);
                Version installedVersion = ParseVersion(CurrentLauncherVersion);

                if (onlineVersion > installedVersion)
                {
                    LauncherUpdateStatusText.Text = $"Update verfügbar: Version {onlineVersion}";
                    LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#38BDF8")!;

                    var result = MessageBox.Show(
                        $"Ein neues Launcher-Update ({onlineVersion}) ist verfügbar!\nMöchtest du den Launcher jetzt aktualisieren?",
                        "Launcher Update verfügbar",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        StartAutoUpdater(info.DownloadUrl);
                    }
                }
                else
                {
                    LauncherUpdateStatusText.Text = "Launcher ist auf dem neuesten Stand.";
                    LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;
                }
            }
        }
        catch (Exception ex)
        {
            LauncherUpdateStatusText.Text = "Fehler bei der Update-Prüfung.";
            LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;
            MessageBox.Show("Fehler beim Suchen nach Launcher-Updates:\n" + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CheckLauncherUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckLauncherUpdateButton.IsEnabled = false;
        await ManualCheckLauncherUpdateAsync();
        CheckLauncherUpdateButton.IsEnabled = true;
    }

    private void StartAutoUpdater(string downloadUrl)
    {
        try
        {
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName 
                                ?? Path.Combine(AppContext.BaseDirectory, "BetaLauncher.exe");
            string batchPath = Path.Combine(Path.GetTempPath(), "update_rfg_launcher.bat");
            string tempDownloadPath = Path.Combine(Path.GetTempPath(), "rfg_launcher_update.tmp");
            string tempExtractPath = Path.Combine(Path.GetTempPath(), "rfg_launcher_extract");

            bool isZip = downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

            string batchContent;

            if (isZip)
            {
                batchContent = $@"
@echo off
timeout /t 2 /nobreak > nul
powershell -Command ""[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '{downloadUrl}' -OutFile '{tempDownloadPath}'""
if exist ""{tempExtractPath}"" rd /s /q ""{tempExtractPath}""
powershell -Command ""Expand-Archive -Path '{tempDownloadPath}' -DestinationPath '{tempExtractPath}' -Force""
xcopy /s /y /i ""{tempExtractPath}\*"" ""{AppContext.BaseDirectory.TrimEnd('\\', '/')}""
del /f /q ""{tempDownloadPath}""
rd /s /q ""{tempExtractPath}""
start """" ""{currentExe}""
del ""%~f0""
";
            }
            else
            {
                batchContent = $@"
@echo off
timeout /t 2 /nobreak > nul
powershell -Command ""[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '{downloadUrl}' -OutFile '{tempDownloadPath}'""
move /y ""{tempDownloadPath}"" ""{currentExe}""
start """" ""{currentExe}""
del ""%~f0""
";
            }

            File.WriteAllText(batchPath, batchContent);

            Process.Start(new ProcessStartInfo
            {
                FileName = batchPath,
                CreateNoWindow = true,
                UseShellExecute = false
            });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler beim Starten des Updaters: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowPage(UIElement page)
    {
        HomePage.Visibility = Visibility.Collapsed;
        UpdatesPage.Visibility = Visibility.Collapsed;
        AccountPage.Visibility = Visibility.Collapsed;
        ChangePasswordPage.Visibility = Visibility.Collapsed;
        AdminPage.Visibility = Visibility.Collapsed;
        CommunityPage.Visibility = Visibility.Collapsed;
        PerformancePage.Visibility = Visibility.Collapsed;
        CreditsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;

        page.Visibility = Visibility.Visible;
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage);
    private void UpdatesButton_Click(object sender, RoutedEventArgs e) => ShowPage(UpdatesPage);
    private void AccountButton_Click(object sender, RoutedEventArgs e) => ShowPage(AccountPage);
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);
    private void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(AdminPage);
        _ = LoadAdminUserListAsync();
    }
    private void CommunityButton_Click(object sender, RoutedEventArgs e) => ShowPage(CommunityPage);
    private void PerformanceButton_Click(object sender, RoutedEventArgs e) => ShowPage(PerformancePage);
    private void CreditsButton_Click(object sender, RoutedEventArgs e) => ShowPage(CreditsPage);
    private void ExitButton_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateHomeInformation()
    {
        string localVersion = GetLocalVersion();
        HomeVersionText.Text = string.IsNullOrWhiteSpace(localVersion) ? "Keine Version installiert" : "Version " + localVersion;

        if (IsGameInstalled())
        {
            HomeStatusText.Text = "SPIEL INSTALLIERT";
            HomeStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#38BDF8")!;
        }
        else
        {
            HomeStatusText.Text = "SPIEL NICHT INSTALLIERT";
            HomeStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;
        }

        if (string.IsNullOrEmpty(LoggedInUsername))
        {
            HomeBetaAccessText.Text = "NICHT ANGEMELDET";
            HomeBetaAccessText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;
        }
        else if (HasBetaAccess)
        {
            HomeBetaAccessText.Text = "FREIGESCHALTET";
            HomeBetaAccessText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#38BDF8")!;
        }
        else
        {
            HomeBetaAccessText.Text = "KEIN BETA-ZUGANG";
            HomeBetaAccessText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;
        }

        StartButton.IsEnabled = IsGameInstalled();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(LoggedInUsername))
            {
                MessageBox.Show("Du musst dich zuerst anmelden, um die Beta-Simulation spielen zu können.", "Anmeldung erforderlich", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowPage(AccountPage);
                return;
            }

            if (!HasBetaAccess)
            {
                MessageBox.Show("Dein Account hat aktuell keinen Beta-Zugang. Bitte wende dich an einen Admin, um den Zugang freischalten zu lassen.", "Kein Beta-Zugang", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsGameRunning())
            {
                MessageBox.Show("Das Spiel läuft bereits.", "RFG Beta Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string? gameExe = FindGameExe();
            if (gameExe == null)
            {
                MessageBox.Show("kirmes.exe wurde nicht gefunden.\n\nBitte installiere zuerst die aktuelle Version.", "Spiel nicht gefunden", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = gameExe,
                WorkingDirectory = Path.GetDirectoryName(gameExe) ?? GameDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Das Spiel konnte nicht gestartet werden:\n\n" + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool IsGameInstalled() => FindGameExe() != null;

    private string? FindGameExe()
    {
        string directPath = Path.Combine(GameDirectory, GameExeName);
        if (File.Exists(directPath)) return directPath;
        if (!Directory.Exists(GameDirectory)) return null;

        try
        {
            return Directory.GetFiles(GameDirectory, GameExeName, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch { return null; }
    }

    private bool IsGameRunning()
    {
        string processName = Path.GetFileNameWithoutExtension(GameExeName);
        return Process.GetProcessesByName(processName).Length > 0;
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await DownloadAndInstallLatestAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            StatusText.Text = "Suche nach Updates...";
            var release = await GetLatestGameReleaseAsync();

            UpdateButton.IsEnabled = true;
            BottomUpdateButton.IsEnabled = true;

            if (release == null)
            {
                StatusText.Text = "Kein Release mit 'game.zip' auf GitHub gefunden.";
                return;
            }

            string remoteVersion = NormalizeVersion(release.TagName);
            string localVersion = NormalizeVersion(GetLocalVersion());
            string? remoteDigest = GetGameDigest(release);
            string localDigest = GetLocalDigest();

            bool versionDifferent = !string.Equals(remoteVersion, localVersion, StringComparison.OrdinalIgnoreCase);
            bool digestDifferent = !string.IsNullOrWhiteSpace(remoteDigest) && !string.Equals(remoteDigest, localDigest, StringComparison.OrdinalIgnoreCase);

            if (versionDifferent || digestDifferent || !IsGameInstalled())
            {
                StatusText.Text = $"Update verfügbar: {release.Name} ({remoteVersion})";
                ShowReleaseNotes(release);
            }
            else
            {
                StatusText.Text = "Du hast bereits die aktuelle Version.";
                ShowReleaseNotes(release);
            }

            VersionText.Text = "Installiert: " + (string.IsNullOrWhiteSpace(localVersion) ? "Keine" : localVersion);
            HomeVersionText.Text = "Version " + (string.IsNullOrWhiteSpace(localVersion) ? "Keine" : localVersion);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Update-Prüfung fehlgeschlagen: " + ex.Message;
            UpdateButton.IsEnabled = true;
            BottomUpdateButton.IsEnabled = true;
        }
    }

    private async Task DownloadAndInstallLatestAsync()
    {
        try
        {
            SetBusy(true);
            StatusText.Text = "Suche nach neuester Version...";

            var release = await GetLatestGameReleaseAsync();
            if (release == null)
            {
                MessageBox.Show("Kein Spiel-Release mit einer 'game.zip' Datei auf GitHub gefunden.", "Update nicht möglich", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, "game.zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null)
            {
                MessageBox.Show("In diesem Release befindet sich keine 'game.zip' Datei.", "Datei fehlt", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string remoteVersion = NormalizeVersion(release.TagName);
            string remoteDigest = GetGameDigest(release) ?? "";
            string tempZip = Path.Combine(Path.GetTempPath(), "RFG_game_update.zip");

            if (File.Exists(tempZip)) File.Delete(tempZip);

            StatusText.Text = $"Lade Kirmes Game ({remoteVersion}) herunter...";
            Progress.Value = 0;

            using (HttpClient downloadClient = new HttpClient())
            {
                downloadClient.Timeout = TimeSpan.FromMinutes(30);
                await DownloadFileWithClientAsync(downloadClient, asset.BrowserDownloadUrl, tempZip);
            }

            StatusText.Text = "Installiere Update...";
            InstallZip(tempZip);
            File.Delete(tempZip);

            File.WriteAllText(VersionFile, remoteVersion);
            if (!string.IsNullOrWhiteSpace(remoteDigest)) File.WriteAllText(DigestFile, remoteDigest);

            Progress.Value = 100;
            ShowReleaseNotes(release);
            UpdateHomeInformation();

            StatusText.Text = $"RFG {remoteVersion} erfolgreich installiert.";
            MessageBox.Show($"RFG {remoteVersion} wurde erfolgreich installiert!", "Update erfolgreich", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Update fehlgeschlagen.";
            MessageBox.Show("Das Update konnte nicht installiert werden:\n\n" + ex.Message, "Update-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<GitHubRelease?> GetLatestGameReleaseAsync()
    {
        string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases?per_page=50";
        using HttpResponseMessage response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        var releases = JsonSerializer.Deserialize<GitHubRelease[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (releases == null || releases.Length == 0) return null;

        return releases.Where(r => !r.Draft && !r.Prerelease)
                       .FirstOrDefault(r => r.Assets.Any(a => string.Equals(a.Name, "game.zip", StringComparison.OrdinalIgnoreCase)));
    }

    private async Task DownloadFileWithClientAsync(HttpClient client, string url, string destination)
    {
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        await using Stream input = await response.Content.ReadAsStreamAsync();
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await output.WriteAsync(buffer, 0, bytesRead);
            totalRead += bytesRead;
            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                Progress.Value = Math.Min(100, totalRead * 100.0 / totalBytes.Value);
            }
        }
    }

    private void InstallZip(string zipFile)
    {
        Directory.CreateDirectory(GameDirectory);
        using ZipArchive archive = ZipFile.OpenRead(zipFile);
        string destinationRoot = Path.GetFullPath(GameDirectory) + Path.DirectorySeparatorChar;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destinationPath = Path.GetFullPath(Path.Combine(GameDirectory, entry.FullName));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Ungültiger Pfad in game.zip.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, true);
        }
    }

    private string GetLocalVersion() => File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : "";
    private string GetLocalDigest() => File.Exists(DigestFile) ? File.ReadAllText(DigestFile).Trim() : "";
    private string? GetGameDigest(GitHubRelease release) => release.Assets.FirstOrDefault(a => string.Equals(a.Name, "game.zip", StringComparison.OrdinalIgnoreCase))?.Digest;

    private string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "";
        version = version.Trim();
        return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version.Substring(1) : version;
    }

    private Version ParseVersion(string? version)
    {
        return Version.TryParse(NormalizeVersion(version), out Version? result) ? result : new Version(0, 0, 0);
    }

    private void ShowReleaseNotes(GitHubRelease release)
    {
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(release.Body) ? "Keine Release Notes vorhanden." : release.Body.Trim();
    }

    private async void LoginAccountButton_Click(object sender, RoutedEventArgs e)
    {
        string username = AccountUsernameTextBox.Text.Trim();
        string password = AccountPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            AccountStatusText.Text = "Bitte Benutzername und Passwort eingeben.";
            return;
        }

        try
        {
            AccountLoginButton.IsEnabled = false;
            AccountStatusText.Text = "Anmeldung läuft...";

            using HttpClient client = new();
            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/login", new { username, password });
            var result = await response.Content.ReadFromJsonAsync<AccountResponse>();

            if (result != null && result.success)
            {
                LoggedInUsername = result.username ?? username;
                LoggedInPassword = password;
                LoggedInRole = result.role ?? "user";
                HasBetaAccess = result.hasBetaAccess;

                AccountStatusText.Text = $"✅ Willkommen zurück, {LoggedInUsername}!";
                AccountPasswordBox.Clear();

                if (LoggedInRole == "admin")
                {
                    AdminMenuButton.Visibility = Visibility.Visible;
                }
                else
                {
                    AdminMenuButton.Visibility = Visibility.Collapsed;
                }

                UpdateHomeInformation();

                if (result.mustChangePassword)
                {
                    ShowPage(ChangePasswordPage);
                }
            }
            else
            {
                AccountStatusText.Text = result?.message ?? "Login fehlgeschlagen.";
            }
        }
        catch
        {
            AccountStatusText.Text = "❌ Account-Server nicht erreichbar.";
        }
        finally
        {
            AccountLoginButton.IsEnabled = true;
        }
    }

    private async void SaveNewPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        string newPw = NewPasswordBox.Password;
        string confirmPw = ConfirmPasswordBox.Password;

        if (newPw.Length < 8)
        {
            ChangePasswordStatusText.Text = "Das Passwort muss mindestens 8 Zeichen lang sein.";
            return;
        }

        if (newPw != confirmPw)
        {
            ChangePasswordStatusText.Text = "Die Passwörter stimmen nicht überein.";
            return;
        }

        try
        {
            using HttpClient client = new();
            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/change-first-password", new
            {
                username = LoggedInUsername,
                currentPassword = LoggedInPassword,
                newPassword = newPw
            });

            var result = await response.Content.ReadFromJsonAsync<AccountResponse>();
            if (result != null && result.success)
            {
                LoggedInPassword = newPw;
                MessageBox.Show("Dein Passwort wurde erfolgreich geändert!", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowPage(HomePage);
            }
            else
            {
                ChangePasswordStatusText.Text = result?.message ?? "Fehler beim Ändern des Passworts.";
            }
        }
        catch
        {
            ChangePasswordStatusText.Text = "Server nicht erreichbar.";
        }
    }

    private async Task LoadAdminUserListAsync()
    {
        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
            client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);

            var response = await client.GetAsync($"{AccountServerUrl}/api/admin/users");
            var result = await response.Content.ReadFromJsonAsync<AdminUserListResponse>();

            if (result != null && result.success)
            {
                UsersDataGrid.ItemsSource = result.users;
            }
        }
        catch
        {
            AdminActionStatus.Text = "Fehler beim Laden der Benutzerliste.";
        }
    }

    private async void AdminCreateUser_Click(object sender, RoutedEventArgs e)
    {
        string username = AdminNewUsernameBox.Text.Trim();
        string tempPassword = AdminNewTempPassBox.Text.Trim();
        string role = (AdminRoleComboBox.SelectedItem as ComboBoxItem)?.Content.ToString()?.ToLower() ?? "user";

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(tempPassword))
        {
            AdminActionStatus.Text = "Bitte Benutzername und Temp-Passwort eingeben.";
            return;
        }

        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
            client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);

            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/create-user", new
            {
                username,
                tempPassword,
                role
            });

            var result = await response.Content.ReadFromJsonAsync<AccountResponse>();
            AdminActionStatus.Text = result?.message ?? "";

            if (result != null && result.success)
            {
                AdminNewUsernameBox.Clear();
                AdminNewTempPassBox.Clear();
                await LoadAdminUserListAsync();
            }
        }
        catch
        {
            AdminActionStatus.Text = "Fehler beim Erstellen des Benutzers.";
        }
    }

    private async void AdminToggleBeta_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is UserItem user)
        {
            try
            {
                AdminActionStatus.Text = $"Ändere Beta-Zugang für {user.Username}...";

                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
                client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);

                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/toggle-beta", new { username = user.Username });
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();

                if (result != null && result.success)
                {
                    AdminActionStatus.Text = $"Beta-Zugang für {user.Username} erfolgreich geändert.";
                }
                else
                {
                    AdminActionStatus.Text = result?.message ?? "Fehler beim Ändern des Beta-Zugangs.";
                }

                await LoadAdminUserListAsync();
            }
            catch (Exception ex)
            {
                AdminActionStatus.Text = "Fehler beim Verbinden zum Server.";
                MessageBox.Show("Fehler beim Ändern des Beta-Zugangs:\n" + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void AdminResetPw_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is UserItem user)
        {
            string newTempPw = "Temp1234!";
            try
            {
                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
                client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);

                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/reset-password", new
                {
                    username = user.Username,
                    newTempPassword = newTempPw
                });

                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();
                MessageBox.Show($"{result?.message}\nNeues temporäres Passwort: {newTempPw}", "Passwort Reset", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadAdminUserListAsync();
            }
            catch
            {
                MessageBox.Show("Fehler beim Zurücksetzen.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void AdminToggleLock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is UserItem user)
        {
            try
            {
                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
                client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);

                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/toggle-lock", new { username = user.Username });
                await LoadAdminUserListAsync();
            }
            catch
            {
                MessageBox.Show("Fehler beim Ändern des Status.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void AdminDeleteUser_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is UserItem user)
        {
            if (string.Equals(user.Username, ProtectedAdminUsername, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show($"Der Haupt-Admin-Account '{ProtectedAdminUsername}' ist geschützt und kann nicht gelöscht werden!", "Aktion gesperrt", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (MessageBox.Show($"Möchtest du '{user.Username}' wirklich löschen?", "Löschen bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    using HttpClient client = new();
                    client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
                    client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);

                    var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/delete-user", new { username = user.Username });
                    await LoadAdminUserListAsync();
                }
                catch
                {
                    MessageBox.Show("Fehler beim Löschen.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private PerformanceCounterWrapper? PerformanceCounter;

    private void StartPerformanceMonitor()
    {
        try
        {
            PerformanceCounter = new PerformanceCounterWrapper();
            PerformanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            PerformanceTimer.Tick += PerformanceTimer_Tick;
            PerformanceTimer.Start();
        }
        catch
        {
            CpuText.Text = "CPU: --";
            RamText.Text = "RAM: --";
        }
    }

    private void PerformanceTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (PerformanceCounter == null) return;
            CpuText.Text = $"CPU: {PerformanceCounter.GetCpuUsage():0}%";
            RamText.Text = $"RAM: {PerformanceCounter.GetRamUsage():0}%";
        }
        catch
        {
            CpuText.Text = "CPU: --";
            RamText.Text = "RAM: --";
        }
    }

    private void SetBusy(bool busy)
    {
        UpdateButton.IsEnabled = !busy;
        BottomUpdateButton.IsEnabled = !busy;
        StartButton.IsEnabled = !busy && IsGameInstalled();
        if (busy) Progress.Value = 0;
    }

    private sealed class LauncherVersionInfo
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("downloadUrl")] public string DownloadUrl { get; set; } = "";
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }

    private sealed class AccountResponse
    {
        public bool success { get; set; }
        public string? username { get; set; }
        public string? role { get; set; }
        public bool hasBetaAccess { get; set; }
        public bool mustChangePassword { get; set; }
        public string? message { get; set; }
    }

    private sealed class AdminUserListResponse
    {
        public bool success { get; set; }
        public List<UserItem> users { get; set; } = new();
    }

    public sealed class UserItem
    {
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public bool HasBetaAccess { get; set; }
        public bool MustChangePassword { get; set; }
        public bool IsLocked { get; set; }
        public string CreatedAt { get; set; } = "";
    }
}

public class PerformanceCounterWrapper
{
    private readonly PerformanceCounter? cpuCounter;
    private readonly PerformanceCounter? ramCounter;

    public PerformanceCounterWrapper()
    {
        try
        {
            cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");

            cpuCounter.NextValue();
            ramCounter.NextValue();
        }
        catch
        {
        }
    }

    public float GetCpuUsage() => cpuCounter?.NextValue() ?? 0f;
    public float GetRamUsage() => ramCounter?.NextValue() ?? 0f;
}
