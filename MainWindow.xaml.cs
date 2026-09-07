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
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";[cite: 1]

    private const string LauncherVersionUrl = "https://raw.githubusercontent.com/Jannik2401/RFGLauncher/main/version.json";[cite: 1]

    private const string GitHubOwner = "Jannik2401";[cite: 1]
    private const string GitHubRepo = "RFGLauncher";[cite: 1]
    private const string GameExeName = "kirmes.exe";[cite: 1]
    private const string AccountServerUrl = "http://node1.waifly.com:25433";[cite: 1]

    private static readonly string[] ProtectedAdminUsernames = { "admin" };[cite: 1]

    private const string DiscordUrl = "https://discord.gg/qaxg7UdafU";[cite: 1]
    private const string TwitchUrl = "https://www.twitch.tv/realistic_funfair_games";[cite: 1]
    private const string InstagramUrl = "https://www.instagram.com/realistic_funfair_games/";[cite: 1]
    private const string TikTokUrl = "https://www.tiktok.com/@realisticfunfairgames";[cite: 1]

    private string GameDirectory = Path.Combine([cite: 1]
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),[cite: 1]
        "RealisticFunfairGames",[cite: 1]
        "Game"[cite: 1]
    );[cite: 1]

    private string VersionFile => Path.Combine(GameDirectory, "version.txt");[cite: 1]
    private string DigestFile => Path.Combine(GameDirectory, "game.digest");[cite: 1]
    private string SessionFile => Path.Combine(GameDirectory, "session.json");[cite: 1]

    private readonly HttpClient Http = new();[cite: 1]
    private DispatcherTimer? PerformanceTimer;[cite: 1]
    private DispatcherTimer? StatusCheckTimer;[cite: 1]

    private string? LoggedInUsername;[cite: 1]
    private string? LoggedInPassword;[cite: 1]
    private string? LoggedInRole;[cite: 1]
    private bool HasBetaAccess;[cite: 1]

    public MainWindow()[cite: 1]
    {
        InitializeComponent();[cite: 1]

        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher/1.0");[cite: 1]
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");[cite: 1]
        Http.Timeout = TimeSpan.FromMinutes(30);[cite: 1]

        Loaded += MainWindow_Loaded;[cite: 1]
        Closed += MainWindow_Closed;[cite: 1]
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)[cite: 1]
    {
        try
        {
            Directory.CreateDirectory(GameDirectory);[cite: 1]

            ShowPage(HomePage);[cite: 1]
            UpdateHomeInformation();[cite: 1]
            StartPerformanceMonitor();[cite: 1]

            LauncherVersionText.Text = $"Version: {CurrentLauncherVersion}";[cite: 1]

            await SilentCheckLauncherUpdateAsync();[cite: 1]
            await CheckForUpdatesAsync();[cite: 1]
            await TryAutoLoginAsync();[cite: 1]
        }
        catch (Exception ex)
        {
            StatusText.Text = "Launcher-Fehler: " + ex.Message;[cite: 1]
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)[cite: 1]
    {
        PerformanceTimer?.Stop();[cite: 1]
        StatusCheckTimer?.Stop();[cite: 1]
        Http.Dispose();[cite: 1]
    }

    private void OpenUrl(string url)[cite: 1]
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });[cite: 1]
        }
        catch (Exception ex)
        {
            MessageBox.Show("Link konnte nicht geöffnet werden:\n" + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 1]
        }
    }

    private void DiscordButton_Click(object sender, RoutedEventArgs e) => OpenUrl(DiscordUrl);[cite: 1]
    private void TwitchButton_Click(object sender, RoutedEventArgs e) => OpenUrl(TwitchUrl);[cite: 1]
    private void InstagramButton_Click(object sender, RoutedEventArgs e) => OpenUrl(InstagramUrl);[cite: 1]
    private void TikTokButton_Click(object sender, RoutedEventArgs e) => OpenUrl(TikTokUrl);[cite: 1]

    private async Task SilentCheckLauncherUpdateAsync()[cite: 1]
    {
        try
        {
            using HttpClient client = new();[cite: 1]
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher-Updater");[cite: 1]
            var info = await client.GetFromJsonAsync<LauncherVersionInfo>(LauncherVersionUrl);[cite: 1]

            if (info != null && !string.IsNullOrWhiteSpace(info.Version))[cite: 1]
            {
                Version onlineVersion = ParseVersion(info.Version);[cite: 1]
                Version installedVersion = ParseVersion(CurrentLauncherVersion);[cite: 1]

                if (onlineVersion > installedVersion)[cite: 1]
                {
                    LauncherUpdateStatusText.Text = $"Neues Update: v{onlineVersion}";[cite: 1]
                    LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#38BDF8")!;[cite: 1]
                }
                else
                {
                    LauncherUpdateStatusText.Text = "Launcher ist aktuell.";[cite: 1]
                    LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;[cite: 1]
                }
            }
        }
        catch { }
    }

    private async void CheckLauncherUpdateButton_Click(object sender, RoutedEventArgs e)[cite: 1]
    {
        CheckLauncherUpdateButton.IsEnabled = false;[cite: 1]
        try
        {
            LauncherUpdateStatusText.Text = "Suche nach Updates...";[cite: 1]
            using HttpClient client = new();[cite: 1]
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher-Updater");[cite: 1]
            var info = await client.GetFromJsonAsync<LauncherVersionInfo>(LauncherVersionUrl);[cite: 1]

            if (info != null && !string.IsNullOrWhiteSpace(info.Version))[cite: 1]
            {
                Version onlineVersion = ParseVersion(info.Version);[cite: 1]
                Version installedVersion = ParseVersion(CurrentLauncherVersion);[cite: 1]

                if (onlineVersion > installedVersion)[cite: 1]
                {
                    if (MessageBox.Show($"Update auf v{onlineVersion} durchführen?", "Update", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)[cite: 1]
                    {
                        StartAutoUpdater(info.DownloadUrl);[cite: 1]
                    }
                }
                else
                {
                    MessageBox.Show("Du nutzt bereits die neueste Version.", "Aktuell", MessageBoxButton.OK, MessageBoxImage.Information);[cite: 1]
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 1]
        }
        finally
        {
            CheckLauncherUpdateButton.IsEnabled = true;[cite: 1]
        }
    }

    private void StartAutoUpdater(string? downloadUrl)[cite: 1]
    {
        try
        {
            if (string.IsNullOrWhiteSpace(downloadUrl)) return;[cite: 1]
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppContext.BaseDirectory, "BetaLauncher.exe");[cite: 1]
            UpdateWindow updateWindow = new UpdateWindow(downloadUrl, currentExe);[cite: 1]
            updateWindow.ShowDialog();[cite: 1]
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler beim Starten des Updaters: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 1]
        }
    }

    private void ShowPage(UIElement page)[cite: 1]
    {
        HomePage.Visibility = Visibility.Collapsed;[cite: 1]
        UpdatesPage.Visibility = Visibility.Collapsed;[cite: 1]
        AccountPage.Visibility = Visibility.Collapsed;[cite: 1]
        ChangePasswordPage.Visibility = Visibility.Collapsed;[cite: 1]
        AdminPage.Visibility = Visibility.Collapsed;[cite: 1]
        PerformancePage.Visibility = Visibility.Collapsed;[cite: 1]
        CreditsPage.Visibility = Visibility.Collapsed;[cite: 1]
        SettingsPage.Visibility = Visibility.Collapsed;[cite: 1]

        page.Visibility = Visibility.Visible;[cite: 1]
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage);[cite: 1]
    private void UpdatesButton_Click(object sender, RoutedEventArgs e) => ShowPage(UpdatesPage);[cite: 1]
    private void AccountButton_Click(object sender, RoutedEventArgs e) => ShowPage(AccountPage);[cite: 1]
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);[cite: 1]
    private void AdminButton_Click(object sender, RoutedEventArgs e) { ShowPage(AdminPage); _ = LoadAdminUserListAsync(); }[cite: 1]
    private void PerformanceButton_Click(object sender, RoutedEventArgs e) => ShowPage(PerformancePage);[cite: 1]
    private void CreditsButton_Click(object sender, RoutedEventArgs e) => ShowPage(CreditsPage);[cite: 1]
    private void ExitButton_Click(object sender, RoutedEventArgs e) => Close();[cite: 1]

    private void UpdateHomeInformation()[cite: 1]
    {
        string localVersion = GetLocalVersion();[cite: 1]
        HomeVersionText.Text = string.IsNullOrWhiteSpace(localVersion) ? "Keine Version installiert" : "Version " + localVersion;[cite: 1]

        if (IsGameInstalled())[cite: 1]
        {
            HomeStatusText.Text = "INSTALLIERT";[cite: 1]
            HomeStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;[cite: 1]
        }
        else
        {
            HomeStatusText.Text = "NICHT INSTALLIERT";[cite: 1]
            HomeStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;[cite: 1]
        }

        if (string.IsNullOrEmpty(LoggedInUsername))[cite: 1]
        {
            HomeBetaAccessText.Text = "NICHT EINGELOGGT";[cite: 1]
            HomeBetaAccessText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;[cite: 1]
        }
        else if (HasBetaAccess)[cite: 1]
        {
            HomeBetaAccessText.Text = "ZUGRIFF GEWÄHRT";[cite: 1]
            HomeBetaAccessText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;[cite: 1]
        }
        else
        {
            HomeBetaAccessText.Text = "ZUGRIFF VERWEIGERT";[cite: 1]
            HomeBetaAccessText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;[cite: 1]
        }

        StartButton.IsEnabled = IsGameInstalled() && HasBetaAccess;[cite: 1]
    }

    private void StartStatusCheck()[cite: 1]
    {
        StatusCheckTimer?.Stop();[cite: 1]
        StatusCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };[cite: 1]
        StatusCheckTimer.Tick += async (s, e) =>[cite: 1]
        {
            if (string.IsNullOrEmpty(LoggedInUsername)) return;[cite: 1]

            try
            {
                using HttpClient client = new();[cite: 1]
                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/user-status", new { username = LoggedInUsername });[cite: 1]
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 1]

                if (result != null && result.success)[cite: 1]
                {
                    bool statusChanged = HasBetaAccess != result.hasBetaAccess || LoggedInRole != result.role || result.isLocked;[cite: 1]
                    
                    HasBetaAccess = result.hasBetaAccess;[cite: 1]
                    LoggedInRole = result.role ?? "user";[cite: 1]

                    if (result.isLocked)[cite: 1]
                    {
                        MessageBox.Show("Dein Account wurde gesperrt.", "Sicherheit", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 1]
                        if (File.Exists(SessionFile)) File.Delete(SessionFile);[cite: 1]
                        LoggedInUsername = null;[cite: 1]
                        LoggedInPassword = null;[cite: 1]
                        ShowPage(AccountPage);[cite: 1]
                        UpdateHomeInformation();[cite: 1]
                        StatusCheckTimer?.Stop();[cite: 1]
                        return;
                    }

                    if (statusChanged)[cite: 1]
                    {
                        AdminMenuButton.Visibility = LoggedInRole == "admin" ? Visibility.Visible : Visibility.Collapsed;[cite: 1]
                        UpdateHomeInformation();[cite: 1]
                    }
                }
            }
            catch { }
        };
        StatusCheckTimer.Start();[cite: 1]
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)[cite: 1]
    {
        try
        {
            if (string.IsNullOrEmpty(LoggedInUsername))[cite: 1]
            {
                MessageBox.Show("Bitte zuerst anmelden.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);[cite: 1]
                ShowPage(AccountPage);[cite: 1]
                return;
            }

            try
            {
                using HttpClient client = new();[cite: 1]
                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/user-status", new { username = LoggedInUsername });[cite: 1]
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 1]
                if (result != null && result.success)[cite: 1]
                {
                    HasBetaAccess = result.hasBetaAccess;[cite: 1]
                    if (result.isLocked || !HasBetaAccess)[cite: 1]
                    {
                        MessageBox.Show("Kein aktiver Beta-Zugriff oder Account gesperrt.", "Zugriff verweigert", MessageBoxButton.OK, MessageBoxImage.Stop);[cite: 1]
                        UpdateHomeInformation();[cite: 1]
                        return;
                    }
                }
            }
            catch { }

            if (!HasBetaAccess)[cite: 1]
            {
                MessageBox.Show("Du hast keinen Beta-Zugriff.", "Zugriff verweigert", MessageBoxButton.OK, MessageBoxImage.Warning);[cite: 1]
                return;
            }

            string? gameExe = FindGameExe();[cite: 1]
            if (gameExe == null)[cite: 1]
            {
                MessageBox.Show("Spiel-Executable nicht gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);[cite: 1]
                return;
            }

            Process.Start(new ProcessStartInfo[cite: 1]
            {
                FileName = gameExe,[cite: 1]
                WorkingDirectory = Path.GetDirectoryName(gameExe) ?? GameDirectory,[cite: 1]
                UseShellExecute = true[cite: 1]
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler beim Starten: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 1]
        }
    }

    private bool IsGameInstalled() => FindGameExe() != null;[cite: 1]

    private string? FindGameExe()[cite: 1]
    {
        string directPath = Path.Combine(GameDirectory, GameExeName);[cite: 1]
        if (File.Exists(directPath)) return directPath;[cite: 1]
        if (!Directory.Exists(GameDirectory)) return null;[cite: 1]
        try { return Directory.GetFiles(GameDirectory, GameExeName, SearchOption.AllDirectories).FirstOrDefault(); }[cite: 1]
        catch { return null; }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e) => await DownloadAndInstallLatestAsync();[cite: 1]

    private async Task CheckForUpdatesAsync()[cite: 1]
    {
        try
        {
            StatusText.Text = "Suche nach Updates...";[cite: 1]
            var release = await GetLatestGameReleaseAsync();[cite: 1]
            UpdateButton.IsEnabled = true;[cite: 1]

            if (release == null) { StatusText.Text = "Kein Release gefunden."; return; }[cite: 1]

            string remoteVersion = NormalizeVersion(release.TagName);[cite: 1]
            string localVersion = NormalizeVersion(GetLocalVersion());[cite: 1]

            StatusText.Text = !string.Equals(remoteVersion, localVersion, StringComparison.OrdinalIgnoreCase) || !IsGameInstalled() [cite: 1]
                ? $"Update verfügbar: {remoteVersion}" : "Spiel ist aktuell.";[cite: 1]

            VersionText.Text = "Installiert: " + (string.IsNullOrWhiteSpace(localVersion) ? "Keine" : localVersion);[cite: 1]
            ReleaseNotesText.Text = release.Body ?? "Keine Notes.";[cite: 1]
        }
        catch { StatusText.Text = "Fehler bei Update-Prüfung."; }[cite: 1]
    }

    private async Task DownloadAndInstallLatestAsync()[cite: 1]
    {
        try
        {
            UpdateButton.IsEnabled = false;[cite: 1]
            var release = await GetLatestGameReleaseAsync();[cite: 1]
            if (release == null) return;[cite: 1]

            var asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, "game.zip", StringComparison.OrdinalIgnoreCase));[cite: 1]
            if (asset == null) { MessageBox.Show("game.zip fehlt im Release.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning); return; }[cite: 1]

            string tempZip = Path.Combine(Path.GetTempPath(), "RFG_game_update.zip");[cite: 1]
            if (File.Exists(tempZip)) File.Delete(tempZip);[cite: 1]

            StatusText.Text = "Lade herunter...";[cite: 1]
            using (HttpClient client = new()) { await DownloadFileWithClientAsync(client, asset.BrowserDownloadUrl, tempZip); }[cite: 1]

            StatusText.Text = "Installiere...";[cite: 1]
            InstallZip(tempZip);[cite: 1]
            File.Delete(tempZip);[cite: 1]

            File.WriteAllText(VersionFile, NormalizeVersion(release.TagName));[cite: 1]
            StatusText.Text = "Erfolgreich installiert!";[cite: 1]
            UpdateHomeInformation();[cite: 1]
        }
        catch (Exception ex)
        {
            StatusText.Text = "Installation fehlgeschlagen.";[cite: 1]
            MessageBox.Show("Fehler: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 1]
        }
        finally { UpdateButton.IsEnabled = true; }[cite: 1]
    }

    private async Task<GitHubRelease?> GetLatestGameReleaseAsync()[cite: 1]
    {
        string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases?per_page=50";[cite: 1]
        using HttpResponseMessage response = await Http.GetAsync(url);[cite: 1]
        response.EnsureSuccessStatusCode();[cite: 1]
        string json = await response.Content.ReadAsStringAsync();[cite: 1]
        var releases = JsonSerializer.Deserialize<GitHubRelease[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });[cite: 1]
        return releases?.Where(r => !r.Draft && !r.Prerelease && r.Assets.Any(a => string.Equals(a.Name, "game.zip", StringComparison.OrdinalIgnoreCase)))[cite: 1]
                        .OrderByDescending(r => ParseVersion(r.TagName)).FirstOrDefault();[cite: 1]
    }

    private async Task DownloadFileWithClientAsync(HttpClient client, string? url, string destination)[cite: 1]
    {
        if (string.IsNullOrWhiteSpace(url)) return;[cite: 1]
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);[cite: 1]
        response.EnsureSuccessStatusCode();[cite: 1]
        long? totalBytes = response.Content.Headers.ContentLength;[cite: 1]
        await using Stream input = await response.Content.ReadAsStreamAsync();[cite: 1]
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);[cite: 1]
        byte[] buffer = new byte[81920];[cite: 1]
        long totalRead = 0;[cite: 1]
        int bytesRead;[cite: 1]
        while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)[cite: 1]
        {
            await output.WriteAsync(buffer, 0, bytesRead);[cite: 1]
            totalRead += bytesRead;[cite: 1]
            if (totalBytes.HasValue && totalBytes.Value > 0) Progress.Value = Math.Min(100, totalRead * 100.0 / totalBytes.Value);[cite: 1]
        }
    }

    private void InstallZip(string zipFile)[cite: 1]
    {
        Directory.CreateDirectory(GameDirectory);[cite: 1]
        using ZipArchive archive = ZipFile.OpenRead(zipFile);[cite: 1]
        string destinationRoot = Path.GetFullPath(GameDirectory) + Path.DirectorySeparatorChar;[cite: 1]
        foreach (ZipArchiveEntry entry in archive.Entries)[cite: 1]
        {
            string destinationPath = Path.GetFullPath(Path.Combine(GameDirectory, entry.FullName));[cite: 1]
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase)) continue;[cite: 1]
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destinationPath); continue; }[cite: 1]
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);[cite: 1]
            entry.ExtractToFile(destinationPath, true);[cite: 1]
        }
    }

    private string GetLocalVersion() => File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : "";[cite: 1]
    private string NormalizeVersion(string? v) => string.IsNullOrWhiteSpace(v) ? "" : (v.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? v.Substring(1) : v).Trim();[cite: 1]
    private Version ParseVersion(string? v) => Version.TryParse(NormalizeVersion(v), out Version? res) ? res : new Version(0, 0, 0);[cite: 1]

    private async Task TryAutoLoginAsync()[cite: 1]
    {
        try
        {
            if (!File.Exists(SessionFile)) return;[cite: 1]

            string json = File.ReadAllText(SessionFile);[cite: 1]
            var session = JsonSerializer.Deserialize<SavedSession>(json);[cite: 1]

            if (session != null && !string.IsNullOrWhiteSpace(session.Username) && !string.IsNullOrWhiteSpace(session.Password))[cite: 1]
            {
                using HttpClient client = new();[cite: 1]
                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/login", new { username = session.Username, password = session.Password });[cite: 1]
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 1]

                if (result != null && result.success)[cite: 1]
                {
                    LoggedInUsername = result.username ?? session.Username;[cite: 1]
                    LoggedInPassword = session.Password;[cite: 1]
                    LoggedInRole = result.role ?? "user";[cite: 1]
                    HasBetaAccess = result.hasBetaAccess;[cite: 1]

                    AdminMenuButton.Visibility = LoggedInRole == "admin" ? Visibility.Visible : Visibility.Collapsed;[cite: 1]
                    UpdateHomeInformation();[cite: 1]
                    StartStatusCheck();[cite: 1]
                }
                else
                {
                    File.Delete(SessionFile);[cite: 1]
                }
            }
        }
        catch { }
    }

    private void SaveSession(string username, string password)[cite: 1]
    {
        try
        {
            var session = new SavedSession { Username = username, Password = password };[cite: 1]
            string json = JsonSerializer.Serialize(session);[cite: 1]
            File.WriteAllText(SessionFile, json);[cite: 1]
        }
        catch { }
    }

    private async void LoginAccountButton_Click(object sender, RoutedEventArgs e)[cite: 1]
    {
        string username = AccountUsernameTextBox.Text.Trim();[cite: 1]
        string password = AccountPasswordBox.Password;[cite: 1]

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))[cite: 1]
        {
            AccountStatusText.Text = "Bitte alle Felder ausfüllen.";[cite: 1]
            return;
        }

        try
        {
            AccountLoginButton.IsEnabled = false;[cite: 1]
            AccountStatusText.Text = "Anmeldung läuft...";[cite: 1]

            using HttpClient client = new();[cite: 1]
            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/login", new { username, password });[cite: 1]
            var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 1]

            if (result != null && result.success)[cite: 1]
            {
                LoggedInUsername = result.username ?? username;[cite: 1]
                LoggedInPassword = password;[cite: 1]
                LoggedInRole = result.role ?? "user";[cite: 1]
                HasBetaAccess = result.hasBetaAccess;[cite: 1]

                SaveSession(username, password);[cite: 1]

                AccountStatusText.Text = "";[cite: 1]
                AccountPasswordBox.Clear();[cite: 1]

                AdminMenuButton.Visibility = LoggedInRole == "admin" ? Visibility.Visible : Visibility.Collapsed;[cite: 1]
                UpdateHomeInformation();[cite: 1]
                StartStatusCheck();[cite: 1]

                ShowPage(result.mustChangePassword ? ChangePasswordPage : HomePage);[cite: 1]
            }
            else
            {
                AccountStatusText.Text = result?.message ?? "Login fehlgeschlagen.";[cite: 1]
            }
        }
        catch
        {
            AccountStatusText.Text = "Server nicht erreichbar.";[cite: 1]
        }
        finally
        {
            AccountLoginButton.IsEnabled = true;[cite: 1]
        }
    }

    private async void SaveNewPasswordButton_Click(object sender, RoutedEventArgs e)[cite: 1]
    {
        string newPw = NewPasswordBox.Password;[cite: 1]
        string confirmPw = ConfirmPasswordBox.Password;[cite: 1]

        if (newPw.Length < 6) { ChangePasswordStatusText.Text = "Mindestens 6 Zeichen."; return; }[cite: 1]
        if (newPw != confirmPw) { ChangePasswordStatusText.Text = "Passwörter stimmen nicht überein."; return; }[cite: 1]

        try
        {
            using HttpClient client = new();[cite: 1]
            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/change-first-password", new { username = LoggedInUsername, currentPassword = LoggedInPassword, newPassword = newPw });[cite: 1]
            var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 1]

            if (result != null && result.success)[cite: 1]
            {
                LoggedInPassword = newPw;[cite: 1]
                SaveSession(LoggedInUsername ?? "", newPw);[cite: 1]
                MessageBox.Show("Passwort erfolgreich geändert!", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);[cite: 1]
                ShowPage(HomePage);[cite: 1]
            }
            else { ChangePasswordStatusText.Text = result?.message ?? "Fehler."; }[cite: 1]
        }
        catch { ChangePasswordStatusText.Text = "Server nicht erreichbar."; }[cite: 1]
    }

    private async Task LoadAdminUserListAsync()[cite: 1]
    {
        try
        {
            using HttpClient client = new();[cite: 1]
            if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);[cite: 1]
            if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);[cite: 1]
            
            var response = await client.GetAsync($"{AccountServerUrl}/api/admin/users");[cite: 1]
            
            if (!response.IsSuccessStatusCode)
            {
                AdminActionStatus.Text = $"Server-Fehler: {(int)response.StatusCode} {response.ReasonPhrase}";[cite: 1]
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<AdminUserListResponse>();[cite: 1]
            if (result != null && result.success)[cite: 1]
            {
                UsersDataGrid.ItemsSource = result.users;[cite: 1]
                AdminActionStatus.Text = $"Benutzer erfolgreich geladen ({result.users.Count}).";[cite: 1]
            }
            else
            {
                AdminActionStatus.Text = "Server meldet Erfolg = false.";[cite: 1]
            }
        }
        catch (Exception ex) [cite: 1]
        {  [cite: 1]
            AdminActionStatus.Text = "Fehler: " + ex.Message; [cite: 1]
        }
    }

    private async void AdminCreateUser_Click(object sender, RoutedEventArgs e)[cite: 1]
    {
        string username = AdminNewUsernameBox.Text.Trim();[cite: 1]
        string tempPassword = AdminNewTempPassBox.Text.Trim();[cite: 1]
        string role = (AdminRoleComboBox.SelectedItem as ComboBoxItem)?.Content.ToString()?.ToLower() ?? "user";[cite: 1]

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(tempPassword)) return;[cite: 1]

        try
        {
            using HttpClient client = new();[cite: 1]
            if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);[cite: 1]
            if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);[cite: 1]
            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/create-user", new { username, tempPassword, role });[cite: 1]
            var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 1]
            AdminActionStatus.Text = result?.message ?? "";[cite: 1]
            if (result != null && result.success) { AdminNewUsernameBox.Clear(); AdminNewTempPassBox.Clear(); await LoadAdminUserListAsync(); }[cite: 1]
        }
        catch { AdminActionStatus.Text = "Fehler."; }[cite: 1]
    }

    private async void AdminToggleBeta_Click(object sender, RoutedEventArgs e)[cite: 1]
    {
        if ((sender as Button)?.DataContext is UserItem user)[cite: 1]
        {
            try
            {
                using HttpClient client = new();[cite: 1]
                if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);[cite: 1]
                if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);[cite: 1]
                
                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/toggle-beta", new { username = user.Username });[cite: 1]
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 1]
                
                if (result != null && result.success)[cite: 1]
                {
                    AdminActionStatus.Text = $"Beta-Zugang für {user.Username} aktualisiert.";[cite: 1]
                    await LoadAdminUserListAsync();[cite: 1]
                }
                else
                {
                    AdminActionStatus.Text = result?.message ?? "Fehler beim Aktualisieren des Beta-Zugangs.";[cite: 1]
                }
            }
            catch (Exception ex) [cite: 1]
            { [cite: 1]
                AdminActionStatus.Text = "Fehler: " + ex.Message; [cite: 1]
            }
        }
    }

    private async void AdminResetPw_Click(object sender, RoutedEventArgs e)[cite: 1]
    {
        if ((sender as Button)?.DataContext is UserItem user)[cite: 1]
        {
            string newTempPw = "Temp1234!";[cite: 1]
            try
            {
                using HttpClient client = new();[cite: 1]
                if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);[cite: 1]
                if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);[cite: 1]
                await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/reset-password", new { username = user.Username, newTempPassword = newTempPw });[cite: 1]
                MessageBox.Show($"Passwort für {user.Username} zurückgesetzt.\nTemp: {newTempPw}", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);[cite: 1]
                await LoadAdminUserListAsync();[cite: 1]
            }
            catch { }
        }
    }

    private async void AdminToggleLock_Click(object sender, RoutedEventArgs e)[cite: 1]
    {
        if ((sender as Button)?.DataContext is UserItem user)[cite: 1]
        {
            try
            {
                using HttpClient client = new();[cite: 1]
                if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);[cite: 1]
                if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);[cite: 1]
                await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/toggle-lock", new { username = user.Username });[cite: 1]
                await LoadAdminUserListAsync();[cite: 1]
            }
            catch { }
        }
    }

    private async void AdminDeleteUser_Click(object sender, RoutedEventArgs e)[cite: 1]
    {
        if ((sender as Button)?.DataContext is UserItem user)[cite: 1]
        {
            if (ProtectedAdminUsernames.Contains(user.Username, StringComparer.OrdinalIgnoreCase))[cite: 1]
            {
                MessageBox.Show($"Der Haupt-Admin '{user.Username}' kann nicht gelöscht werden.", "Gesperrt", MessageBoxButton.OK, MessageBoxImage.Stop);[cite: 1]
                return;
            }

            if (MessageBox.Show($"Benutzer '{user.Username}' löschen?", "Bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)[cite: 1]
            {
                try
                {
                    using HttpClient client = new();[cite: 1]
                    if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);[cite: 1]
                    if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);[cite: 1]
                    await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/delete-user", new { username = user.Username });[cite: 1]
                    await LoadAdminUserListAsync();[cite: 1]
                }
                catch { }
            }
        }
    }

    private PerformanceCounterWrapper? PerformanceCounter;[cite: 1]

    private void StartPerformanceMonitor()[cite: 1]
    {
        try
        {
            PerformanceCounter = new PerformanceCounterWrapper();[cite: 1]
            PerformanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };[cite: 1]
            PerformanceTimer.Tick += (s, e) =>[cite: 1]
            {
                if (PerformanceCounter == null) return;[cite: 1]
                CpuText.Text = $"CPU: {PerformanceCounter.GetCpuUsage():0}%";[cite: 1]
                RamText.Text = $"RAM: {PerformanceCounter.GetRamUsage():0}%";[cite: 1]
            };
            PerformanceTimer.Start();[cite: 1]
        }
        catch
        {
            CpuText.Text = "CPU: --";[cite: 1]
            RamText.Text = "RAM: --";[cite: 1]
        }
    }

    private sealed class LauncherVersionInfo[cite: 1]
    {
        [JsonPropertyName("version")][cite: 1]
        public string? Version { get; set; }[cite: 1]

        [JsonPropertyName("downloadUrl")][cite: 1]
        public string? DownloadUrl { get; set; }[cite: 1]
    }

    private sealed class SavedSession[cite: 1]
    {
        [JsonPropertyName("username")][cite: 1]
        public string? Username { get; set; }[cite: 1]

        [JsonPropertyName("password")][cite: 1]
        public string? Password { get; set; }[cite: 1]
    }

    private sealed class GitHubRelease[cite: 1]
    {
        [JsonPropertyName("tag_name")][cite: 1]
        public string? TagName { get; set; }[cite: 1]

        [JsonPropertyName("body")][cite: 1]
        public string? Body { get; set; }[cite: 1]

        [JsonPropertyName("draft")][cite: 1]
        public bool Draft { get; set; }[cite: 1]

        [JsonPropertyName("prerelease")][cite: 1]
        public bool Prerelease { get; set; }[cite: 1]

        [JsonPropertyName("assets")][cite: 1]
        public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();[cite: 1]
    }

    private sealed class GitHubAsset[cite: 1]
    {
        [JsonPropertyName("name")][cite: 1]
        public string? Name { get; set; }[cite: 1]

        [JsonPropertyName("browser_download_url")][cite: 1]
        public string? BrowserDownloadUrl { get; set; }[cite: 1]
    }

    private sealed class AccountResponse[cite: 1]
    {
        [JsonPropertyName("success")][cite: 1]
        public bool success { get; set; }[cite: 1]

        [JsonPropertyName("message")][cite: 1]
        public string? message { get; set; }[cite: 1]

        [JsonPropertyName("username")][cite: 1]
        public string? username { get; set; }[cite: 1]

        [JsonPropertyName("role")][cite: 1]
        public string? role { get; set; }[cite: 1]

        [JsonPropertyName("hasBetaAccess")][cite: 1]
        public bool hasBetaAccess { get; set; }[cite: 1]

        [JsonPropertyName("mustChangePassword")][cite: 1]
        public bool mustChangePassword { get; set; }[cite: 1]

        [JsonPropertyName("isLocked")][cite: 1]
        public bool isLocked { get; set; }[cite: 1]
    }

    private sealed class AdminUserListResponse[cite: 1]
    {
        [JsonPropertyName("success")][cite: 1]
        public bool success { get; set; }[cite: 1]

        [JsonPropertyName("users")][cite: 1]
        public List<UserItem> users { get; set; } = new();[cite: 1]
    }

    public sealed class UserItem[cite: 1]
    {
        [JsonPropertyName("username")][cite: 1]
        public string? Username { get; set; }[cite: 1]

        [JsonPropertyName("role")][cite: 1]
        public string? Role { get; set; }[cite: 1]

        [JsonPropertyName("hasBetaAccess")][cite: 1]
        public bool HasBetaAccess { get; set; }[cite: 1]

        [JsonPropertyName("isLocked")][cite: 1]
        public bool IsLocked { get; set; }[cite: 1]

        [JsonPropertyName("mustChangePassword")][cite: 1]
        public bool MustChangePassword { get; set; }[cite: 1]
    }

    private sealed class PerformanceCounterWrapper[cite: 1]
    {
        private readonly PerformanceCounter? cpuCounter;[cite: 1]
        private readonly Process currentProcess;[cite: 1]

        public PerformanceCounterWrapper()[cite: 1]
        {
            currentProcess = Process.GetCurrentProcess();[cite: 1]
            try
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);[cite: 1]
                cpuCounter.NextValue();[cite: 1]
            }
            catch
            {
                cpuCounter = null;[cite: 1]
            }
        }

        public float GetCpuUsage()[cite: 1]
        {
            try
            {
                return cpuCounter?.NextValue() ?? 0f;[cite: 1]
            }
            catch
            {
                return 0f;[cite: 1]
            }
        }

        public float GetRamUsage()[cite: 1]
        {
            try
            {
                currentProcess.Refresh();[cite: 1]
                long workingSet = currentProcess.WorkingSet64;[cite: 1]
                long totalPhysicalMemory = GetTotalMemoryInBytes();[cite: 1]
                if (totalPhysicalMemory <= 0) return 0f;[cite: 1]
                return (float)((double)workingSet / totalPhysicalMemory * 100.0);[cite: 1]
            }
            catch
            {
                return 0f;[cite: 1]
            }
        }

        private static long GetTotalMemoryInBytes()[cite: 1]
        {
            try
            {
                var gcMemoryInfo = GC.GetGCMemoryInfo();[cite: 1]
                return gcMemoryInfo.TotalAvailableMemoryBytes;[cite: 1]
            }
            catch
            {
                return 1024L * 1024L * 1024L * 8L;[cite: 1]
            }
        }
    }
}
