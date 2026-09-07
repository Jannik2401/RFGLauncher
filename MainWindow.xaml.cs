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

public partial class MainWindow : Window[cite: 7]
{
    private static readonly string CurrentLauncherVersion = 
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";[cite: 7]

    private const string LauncherVersionUrl = "https://raw.githubusercontent.com/Jannik2401/RFGLauncher/main/version.json";[cite: 7]

    private const string GitHubOwner = "Jannik2401";[cite: 7]
    private const string GitHubRepo = "RFGLauncher";[cite: 7]
    private const string GameExeName = "kirmes.exe";[cite: 7]
    private const string AccountServerUrl = "http://node1.waifly.com:25433";[cite: 7]

    private static readonly string[] ProtectedAdminUsernames = { "admin" };[cite: 7]

    private const string DiscordUrl = "https://discord.gg/qaxg7UdafU";[cite: 7]
    private const string TwitchUrl = "https://www.twitch.tv/realistic_funfair_games";[cite: 7]
    private const string InstagramUrl = "https://www.instagram.com/realistic_funfair_games/";[cite: 7]
    private const string TikTokUrl = "https://www.tiktok.com/@realisticfunfairgames";[cite: 7]

    private string GameDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RealisticFunfairGames",
        "Game"
    );[cite: 7]

    private string VersionFile => Path.Combine(GameDirectory, "version.txt");[cite: 7]
    private string DigestFile => Path.Combine(GameDirectory, "game.digest");[cite: 7]
    private string SessionFile => Path.Combine(GameDirectory, "session.json");[cite: 7]

    private readonly HttpClient Http = new();[cite: 7]
    private DispatcherTimer? PerformanceTimer;[cite: 7]
    private DispatcherTimer? StatusCheckTimer;[cite: 7]

    private string? LoggedInUsername;[cite: 7]
    private string? LoggedInPassword;[cite: 7]
    private string? LoggedInRole;[cite: 7]
    private bool HasBetaAccess;[cite: 7]

    private bool _isPasswordVisible = false;[cite: 7]
    private string _rawPassword = "";[cite: 7]

    private bool _inlinePasswordVisible = false;[cite: 7]
    private string _inlineRawPassword = string.Empty;[cite: 7]

    public MainWindow()[cite: 7]
    {
        InitializeComponent();[cite: 7]

        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher/1.0");[cite: 7]
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");[cite: 7]
        Http.Timeout = TimeSpan.FromMinutes(30);[cite: 7]

        Loaded += MainWindow_Loaded;[cite: 7]
        Closed += MainWindow_Closed;[cite: 7]
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)[cite: 7]
    {
        try
        {
            Directory.CreateDirectory(GameDirectory);[cite: 7]

            ShowPage(HomePage);[cite: 7]
            UpdateHomeInformation();[cite: 7]
            StartPerformanceMonitor();[cite: 7]

            LauncherVersionText.Text = $"Version: {CurrentLauncherVersion}";[cite: 7]

            await SilentCheckLauncherUpdateAsync();[cite: 7]
            await CheckForUpdatesAsync();[cite: 7]
            await TryAutoLoginAsync();[cite: 7]
            UpdateAccountUIVisibility();[cite: 7]
        }
        catch (Exception ex)
        {
            StatusText.Text = "Launcher-Fehler: " + ex.Message;[cite: 7]
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)[cite: 7]
    {
        PerformanceTimer?.Stop();[cite: 7]
        StatusCheckTimer?.Stop();[cite: 7]
        Http.Dispose();[cite: 7]
    }

    private void OpenUrl(string url)[cite: 7]
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });[cite: 7]
        }
        catch (Exception ex)
        {
            MessageBox.Show("Link konnte nicht geöffnet werden:\n" + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 7]
        }
    }

    private void DiscordButton_Click(object sender, RoutedEventArgs e) => OpenUrl(DiscordUrl);[cite: 7]
    private void TwitchButton_Click(object sender, RoutedEventArgs e) => OpenUrl(TwitchUrl);[cite: 7]
    private void InstagramButton_Click(object sender, RoutedEventArgs e) => OpenUrl(InstagramUrl);[cite: 7]
    private void TikTokButton_Click(object sender, RoutedEventArgs e) => OpenUrl(TikTokUrl);[cite: 7]

    private void UpdateAccountUIVisibility()[cite: 7]
    {
        bool isLoggedIn = !string.IsNullOrEmpty(LoggedInUsername);[cite: 7]

        AccountMenuButton.Visibility = isLoggedIn ? Visibility.Collapsed : Visibility.Visible;[cite: 7]
        UserProfileCornerBox.Visibility = isLoggedIn ? Visibility.Visible : Visibility.Collapsed;[cite: 7]

        if (isLoggedIn)
        {
            CornerUsernameText.Text = LoggedInUsername;[cite: 7]
            CornerRoleText.Text = $"Rolle: {LoggedInRole?.ToUpper()}";[cite: 7]

            AccountLoginPanel.Visibility = Visibility.Collapsed;[cite: 7]
            AccountProfilePanel.Visibility = Visibility.Visible;[cite: 7]
            ProfileUsernameDisplay.Text = LoggedInUsername;[cite: 7]
        }
        else
        {
            AccountLoginPanel.Visibility = Visibility.Visible;[cite: 7]
            AccountProfilePanel.Visibility = Visibility.Collapsed;[cite: 7]
        }
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        try
        {
            if (File.Exists(SessionFile))
            {
                File.Delete(SessionFile);[cite: 7]
            }
        }
        catch { }

        LoggedInUsername = null;[cite: 7]
        LoggedInPassword = null;[cite: 7]
        LoggedInRole = null;[cite: 7]
        HasBetaAccess = false;[cite: 7]

        AdminMenuButton.Visibility = Visibility.Collapsed;[cite: 7]

        UpdateHomeInformation();[cite: 7]
        UpdateAccountUIVisibility();[cite: 7]
        ShowPage(HomePage);[cite: 7]
    }

    private void AccountPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)[cite: 7]
    {
        if (!_isPasswordVisible)
        {
            _rawPassword = AccountPasswordBox.Password;[cite: 7]
        }
    }

    private void AccountPasswordVisibleTextBox_TextChanged(object sender, TextChangedEventArgs e)[cite: 7]
    {
        if (_isPasswordVisible)
        {
            _rawPassword = AccountPasswordVisibleTextBox.Text;[cite: 7]
        }
    }

    private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        _isPasswordVisible = !_isPasswordVisible;[cite: 7]

        if (_isPasswordVisible)
        {
            AccountPasswordVisibleTextBox.Text = _rawPassword;[cite: 7]
            AccountPasswordBox.Visibility = Visibility.Collapsed;[cite: 7]
            AccountPasswordVisibleTextBox.Visibility = Visibility.Visible;[cite: 7]
            TogglePasswordBtn.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#38BDF8")!;[cite: 7]
        }
        else
        {
            AccountPasswordBox.Password = _rawPassword;[cite: 7]
            AccountPasswordVisibleTextBox.Visibility = Visibility.Collapsed;[cite: 7]
            AccountPasswordBox.Visibility = Visibility.Visible;[cite: 7]
            TogglePasswordBtn.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#94A3B8")!;[cite: 7]
        }
    }

    private void UserProfileCornerBox_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)[cite: 7]
    {
        if (string.IsNullOrEmpty(LoggedInUsername)) return;[cite: 7]

        InlineTxtLoginUsername.Text = LoggedInUsername;[cite: 7]
        InlineTxtDisplayName.Text = CornerUsernameText.Text;[cite: 7]
        _inlineRawPassword = LoggedInPassword ?? string.Empty;[cite: 7]
        InlinePwdBox.Password = _inlineRawPassword;[cite: 7]
        _inlinePasswordVisible = false;[cite: 7]
        InlinePwdBox.Visibility = Visibility.Visible;[cite: 7]
        InlineTxtVisiblePassword.Visibility = Visibility.Collapsed;[cite: 7]
        InlineBtnTogglePwd.Content = "Show";[cite: 7]

        AccountSettingsPanel.Visibility = Visibility.Visible;[cite: 7]
    }

    private void InlinePwdBox_PasswordChanged(object sender, RoutedEventArgs e)[cite: 7]
    {
        if (!_inlinePasswordVisible)
        {
            _inlineRawPassword = InlinePwdBox.Password;[cite: 7]
        }
    }

    private void InlineTxtVisiblePassword_TextChanged(object sender, TextChangedEventArgs e)[cite: 7]
    {
        if (_inlinePasswordVisible)
        {
            _inlineRawPassword = InlineTxtVisiblePassword.Text;[cite: 7]
        }
    }

    private void InlineBtnTogglePwd_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        _inlinePasswordVisible = !_inlinePasswordVisible;[cite: 7]
        if (_inlinePasswordVisible)
        {
            InlineTxtVisiblePassword.Text = _inlineRawPassword;[cite: 7]
            InlinePwdBox.Visibility = Visibility.Collapsed;[cite: 7]
            InlineTxtVisiblePassword.Visibility = Visibility.Visible;[cite: 7]
            InlineBtnTogglePwd.Content = "Hide";[cite: 7]
        }
        else
        {
            InlinePwdBox.Password = _inlineRawPassword;[cite: 7]
            InlineTxtVisiblePassword.Visibility = Visibility.Collapsed;[cite: 7]
            InlinePwdBox.Visibility = Visibility.Visible;[cite: 7]
            InlineBtnTogglePwd.Content = "Show";[cite: 7]
        }
    }

    private void InlineSaveButton_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        string newName = InlineTxtDisplayName.Text.Trim();[cite: 7]
        if (!string.IsNullOrEmpty(newName))
        {
            NewDisplayNameTextBox.Text = newName;[cite: 7]
            SaveDisplayNameButton_Click(sender, e);[cite: 7]
        }
        AccountSettingsPanel.Visibility = Visibility.Collapsed;[cite: 7]
    }

    private void InlineCancelButton_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        AccountSettingsPanel.Visibility = Visibility.Collapsed;[cite: 7]
    }

    private async Task SilentCheckLauncherUpdateAsync()[cite: 7]
    {
        try
        {
            using HttpClient client = new();[cite: 7]
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher-Updater");[cite: 7]
            var info = await client.GetFromJsonAsync<LauncherVersionInfo>(LauncherVersionUrl);[cite: 7]

            if (info != null && !string.IsNullOrWhiteSpace(info.Version))
            {
                Version onlineVersion = ParseVersion(info.Version);[cite: 7]
                Version installedVersion = ParseVersion(CurrentLauncherVersion);[cite: 7]

                if (onlineVersion > installedVersion)
                {
                    LauncherUpdateStatusText.Text = $"Neues Update: v{onlineVersion}";[cite: 7]
                    LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#38BDF8")!;[cite: 7]
                }
                else
                {
                    LauncherUpdateStatusText.Text = "Launcher ist aktuell.";[cite: 7]
                    LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;[cite: 7]
                }
            }
        }
        catch { }
    }

    private async void CheckLauncherUpdateButton_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        CheckLauncherUpdateButton.IsEnabled = false;[cite: 7]
        try
        {
            LauncherUpdateStatusText.Text = "Suche nach Updates...";[cite: 7]
            using HttpClient client = new();[cite: 7]
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher-Updater");[cite: 7]
            var info = await client.GetFromJsonAsync<LauncherVersionInfo>(LauncherVersionUrl);[cite: 7]

            if (info != null && !string.IsNullOrWhiteSpace(info.Version))
            {
                Version onlineVersion = ParseVersion(info.Version);[cite: 7]
                Version installedVersion = ParseVersion(CurrentLauncherVersion);[cite: 7]

                if (onlineVersion > installedVersion)
                {
                    if (MessageBox.Show($"Update auf v{onlineVersion} durchführen?", "Update", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    {
                        StartAutoUpdater(info.DownloadUrl);[cite: 7]
                    }
                }
                else
                {
                    MessageBox.Show("Du nutzt bereits die neueste Version.", "Aktuell", MessageBoxButton.OK, MessageBoxImage.Information);[cite: 7]
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 7]
        }
        finally
        {
            CheckLauncherUpdateButton.IsEnabled = true;[cite: 7]
        }
    }

    private void StartAutoUpdater(string? downloadUrl)[cite: 7]
    {
        try
        {
            if (string.IsNullOrWhiteSpace(downloadUrl)) return;[cite: 7]
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppContext.BaseDirectory, "BetaLauncher.exe");[cite: 7]
            UpdateWindow updateWindow = new UpdateWindow(downloadUrl, currentExe);[cite: 7]
            updateWindow.ShowDialog();[cite: 7]
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler beim Starten des Updaters: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 7]
        }
    }

    private void ShowPage(UIElement page)[cite: 7]
    {
        HomePage.Visibility = Visibility.Collapsed;[cite: 7]
        UpdatesPage.Visibility = Visibility.Collapsed;[cite: 7]
        AccountPage.Visibility = Visibility.Collapsed;[cite: 7]
        ChangePasswordPage.Visibility = Visibility.Collapsed;[cite: 7]
        AdminPage.Visibility = Visibility.Collapsed;[cite: 7]
        PerformancePage.Visibility = Visibility.Collapsed;[cite: 7]
        CreditsPage.Visibility = Visibility.Collapsed;[cite: 7]
        SettingsPage.Visibility = Visibility.Collapsed;[cite: 7]

        page.Visibility = Visibility.Visible;[cite: 7]
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage);[cite: 7]
    private void UpdatesButton_Click(object sender, RoutedEventArgs e) => ShowPage(UpdatesPage);[cite: 7]
    private void AccountButton_Click(object sender, RoutedEventArgs e) => ShowPage(AccountPage);[cite: 7]
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);[cite: 7]
    private void AdminButton_Click(object sender, RoutedEventArgs e) { ShowPage(AdminPage); _ = LoadAdminUserListAsync(); }[cite: 7]
    private void PerformanceButton_Click(object sender, RoutedEventArgs e) => ShowPage(PerformancePage);[cite: 7]
    private void CreditsButton_Click(object sender, RoutedEventArgs e) => ShowPage(CreditsPage);[cite: 7]
    private void ExitButton_Click(object sender, RoutedEventArgs e) => Close();[cite: 7]
    private void GoToChangePassword_Click(object sender, RoutedEventArgs e) => ShowPage(ChangePasswordPage);[cite: 7]

    private void UpdateHomeInformation()[cite: 7]
    {
        string localVersion = GetLocalVersion();[cite: 7]
        HomeVersionText.Text = string.IsNullOrWhiteSpace(localVersion) ? "Keine Version installiert" : "Version " + localVersion;[cite: 7]

        if (IsGameInstalled())
        {
            HomeStatusText.Text = "INSTALLIERT";[cite: 7]
            HomeStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;[cite: 7]
        }
        else
        {
            HomeStatusText.Text = "NICHT INSTALLIERT";[cite: 7]
            HomeStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;[cite: 7]
        }

        if (string.IsNullOrEmpty(LoggedInUsername))
        {
            HomeBetaAccessText.Text = "NICHT EINGELOGGT";[cite: 7]
            HomeBetaAccessText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;[cite: 7]
        }
        else if (HasBetaAccess)
        {
            HomeBetaAccessText.Text = "ZUGRIFF GEWÄHRT";[cite: 7]
            HomeBetaAccessText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;[cite: 7]
        }
        else
        {
            HomeBetaAccessText.Text = "ZUGRIFF VERWEIGERT";[cite: 7]
            HomeBetaAccessText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;[cite: 7]
        }

        StartButton.IsEnabled = IsGameInstalled() && HasBetaAccess;[cite: 7]
    }

    private void StartStatusCheck()[cite: 7]
    {
        StatusCheckTimer?.Stop();[cite: 7]
        StatusCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };[cite: 7]
        StatusCheckTimer.Tick += async (s, e) =>
        {
            if (string.IsNullOrEmpty(LoggedInUsername)) return;[cite: 7]

            try
            {
                using HttpClient client = new();[cite: 7]
                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/user-status", new { username = LoggedInUsername });[cite: 7]
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 7]

                if (result != null && result.success)
                {
                    bool statusChanged = HasBetaAccess != result.hasBetaAccess || LoggedInRole != result.role || result.isLocked;[cite: 7]
                    
                    HasBetaAccess = result.hasBetaAccess;[cite: 7]
                    LoggedInRole = result.role ?? "user";[cite: 7]

                    if (result.isLocked)
                    {
                        MessageBox.Show("Dein Account wurde gesperrt.", "Sicherheit", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 7]
                        if (File.Exists(SessionFile)) File.Delete(SessionFile);[cite: 7]
                        LoggedInUsername = null;[cite: 7]
                        LoggedInPassword = null;[cite: 7]
                        ShowPage(AccountPage);[cite: 7]
                        UpdateHomeInformation();[cite: 7]
                        UpdateAccountUIVisibility();[cite: 7]
                        StatusCheckTimer?.Stop();[cite: 7]
                        return;
                    }

                    if (statusChanged)
                    {
                        AdminMenuButton.Visibility = LoggedInRole == "admin" ? Visibility.Visible : Visibility.Collapsed;[cite: 7]
                        UpdateHomeInformation();[cite: 7]
                        UpdateAccountUIVisibility();[cite: 7]
                    }
                }
            }
            catch { }
        };
        StatusCheckTimer.Start();[cite: 7]
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        try
        {
            if (string.IsNullOrEmpty(LoggedInUsername))
            {
                MessageBox.Show("Bitte zuerst anmelden.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);[cite: 7]
                ShowPage(AccountPage);[cite: 7]
                return;
            }

            try
            {
                using HttpClient client = new();[cite: 7]
                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/user-status", new { username = LoggedInUsername });[cite: 7]
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 7]
                if (result != null && result.success)
                {
                    HasBetaAccess = result.hasBetaAccess;[cite: 7]
                    if (result.isLocked || !HasBetaAccess)
                    {
                        MessageBox.Show("Kein aktiver Beta-Zugriff oder Account gesperrt.", "Zugriff verweigert", MessageBoxButton.OK, MessageBoxImage.Stop);[cite: 7]
                        UpdateHomeInformation();[cite: 7]
                        return;
                    }
                }
            }
            catch { }

            if (!HasBetaAccess)
            {
                MessageBox.Show("Du hast keinen Beta-Zugriff.", "Zugriff verweigert", MessageBoxButton.OK, MessageBoxImage.Warning);[cite: 7]
                return;
            }

            string? gameExe = FindGameExe();[cite: 7]
            if (gameExe == null)
            {
                MessageBox.Show("Spiel-Executable nicht gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);[cite: 7]
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = gameExe,
                WorkingDirectory = Path.GetDirectoryName(gameExe) ?? GameDirectory,
                UseShellExecute = true
            });[cite: 7]
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler beim Starten: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 7]
        }
    }

    private bool IsGameInstalled() => FindGameExe() != null;[cite: 7]

    private string? FindGameExe()[cite: 7]
    {
        string directPath = Path.Combine(GameDirectory, GameExeName);[cite: 7]
        if (File.Exists(directPath)) return directPath;[cite: 7]
        if (!Directory.Exists(GameDirectory)) return null;[cite: 7]
        try { return Directory.GetFiles(GameDirectory, GameExeName, SearchOption.AllDirectories).FirstOrDefault(); }[cite: 7]
        catch { return null; }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e) => await DownloadAndInstallLatestAsync();[cite: 7]

    private async Task CheckForUpdatesAsync()[cite: 7]
    {
        try
        {
            StatusText.Text = "Suche nach Updates...";[cite: 7]
            var release = await GetLatestGameReleaseAsync();[cite: 7]
            UpdateButton.IsEnabled = true;[cite: 7]

            if (release == null) { StatusText.Text = "Kein Release gefunden."; return; }[cite: 7]

            string remoteVersion = NormalizeVersion(release.TagName);[cite: 7]
            string localVersion = NormalizeVersion(GetLocalVersion());[cite: 7]

            StatusText.Text = !string.Equals(remoteVersion, localVersion, StringComparison.OrdinalIgnoreCase) || !IsGameInstalled()[cite: 7]
                ? $"Update verfügbar: {remoteVersion}" : "Spiel ist aktuell.";[cite: 7]

            VersionText.Text = "Installiert: " + (string.IsNullOrWhiteSpace(localVersion) ? "Keine" : localVersion);[cite: 7]
            ReleaseNotesText.Text = release.Body ?? "Keine Notes.";[cite: 7]
        }
        catch { StatusText.Text = "Fehler bei Update-Prüfung."; }
    }

    private async Task DownloadAndInstallLatestAsync()[cite: 7]
    {
        try
        {
            UpdateButton.IsEnabled = false;[cite: 7]
            var release = await GetLatestGameReleaseAsync();[cite: 7]
            if (release == null) return;[cite: 7]

            var asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, "game.zip", StringComparison.OrdinalIgnoreCase));[cite: 7]
            if (asset == null) { MessageBox.Show("game.zip fehlt im Release.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning); return; }[cite: 7]

            string tempZip = Path.Combine(Path.GetTempPath(), "RFG_game_update.zip");[cite: 7]
            if (File.Exists(tempZip)) File.Delete(tempZip);[cite: 7]

            StatusText.Text = "Lade herunter...";[cite: 7]
            using (HttpClient client = new()) { await DownloadFileWithClientAsync(client, asset.BrowserDownloadUrl, tempZip); }[cite: 7]

            StatusText.Text = "Installiere...";[cite: 7]
            InstallZip(tempZip);[cite: 7]
            File.Delete(tempZip);[cite: 7]

            File.WriteAllText(VersionFile, NormalizeVersion(release.TagName));[cite: 7]
            StatusText.Text = "Erfolgreich installiert!";[cite: 7]
            UpdateHomeInformation();[cite: 7]
        }
        catch (Exception ex)
        {
            StatusText.Text = "Installation fehlgeschlagen.";[cite: 7]
            MessageBox.Show("Fehler: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);[cite: 7]
        }
        finally { UpdateButton.IsEnabled = true; }
    }

    private async Task<GitHubRelease?> GetLatestGameReleaseAsync()[cite: 7]
    {
        string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases?per_page=50";[cite: 7]
        using HttpResponseMessage response = await Http.GetAsync(url);[cite: 7]
        response.EnsureSuccessStatusCode();[cite: 7]
        string json = await response.Content.ReadAsStringAsync();[cite: 7]
        var releases = JsonSerializer.Deserialize<GitHubRelease[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });[cite: 7]
        return releases?.Where(r => !r.Draft && !r.Prerelease && r.Assets.Any(a => string.Equals(a.Name, "game.zip", StringComparison.OrdinalIgnoreCase)))[cite: 7]
                        .OrderByDescending(r => ParseVersion(r.TagName)).FirstOrDefault();[cite: 7]
    }

    private async Task DownloadFileWithClientAsync(HttpClient client, string? url, string destination)[cite: 7]
    {
        if (string.IsNullOrWhiteSpace(url)) return;[cite: 7]
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);[cite: 7]
        response.EnsureSuccessStatusCode();[cite: 7]
        long? totalBytes = response.Content.Headers.ContentLength;[cite: 7]
        await using Stream input = await response.Content.ReadAsStreamAsync();[cite: 7]
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);[cite: 7]
        byte[] buffer = new byte[81920];[cite: 7]
        long totalRead = 0;[cite: 7]
        int bytesRead;[cite: 7]
        while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await output.WriteAsync(buffer, 0, bytesRead);[cite: 7]
            totalRead += bytesRead;[cite: 7]
            if (totalBytes.HasValue && totalBytes.Value > 0) Progress.Value = Math.Min(100, totalRead * 100.0 / totalBytes.Value);[cite: 7]
        }
    }

    private void InstallZip(string zipFile)[cite: 7]
    {
        Directory.CreateDirectory(GameDirectory);[cite: 7]
        using ZipArchive archive = ZipFile.OpenRead(zipFile);[cite: 7]
        string destinationRoot = Path.GetFullPath(GameDirectory) + Path.DirectorySeparatorChar;[cite: 7]
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destinationPath = Path.GetFullPath(Path.Combine(GameDirectory, entry.FullName));[cite: 7]
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase)) continue;[cite: 7]
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destinationPath); continue; }[cite: 7]
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);[cite: 7]
            entry.ExtractToFile(destinationPath, true);[cite: 7]
        }
    }

    private string GetLocalVersion() => File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : "";[cite: 7]
    private string NormalizeVersion(string? v) => string.IsNullOrWhiteSpace(v) ? "" : (v.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? v.Substring(1) : v).Trim();[cite: 7]
    private Version ParseVersion(string? v) => Version.TryParse(NormalizeVersion(v), out Version? res) ? res : new Version(0, 0, 0);[cite: 7]

    private async Task TryAutoLoginAsync()[cite: 7]
    {
        try
        {
            if (!File.Exists(SessionFile)) return;[cite: 7]

            string json = File.ReadAllText(SessionFile);[cite: 7]
            var session = JsonSerializer.Deserialize<SavedSession>(json);[cite: 7]

            if (session != null && !string.IsNullOrWhiteSpace(session.Username) && !string.IsNullOrWhiteSpace(session.Password))
            {
                using HttpClient client = new();[cite: 7]
                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/login", new { username = session.Username, password = session.Password });[cite: 7]
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 7]

                if (result != null && result.success)
                {
                    LoggedInUsername = result.username ?? session.Username;[cite: 7]
                    LoggedInPassword = session.Password;[cite: 7]
                    LoggedInRole = result.role ?? "user";[cite: 7]
                    HasBetaAccess = result.hasBetaAccess;[cite: 7]

                    AdminMenuButton.Visibility = LoggedInRole == "admin" ? Visibility.Visible : Visibility.Collapsed;[cite: 7]
                    UpdateHomeInformation();[cite: 7]
                    UpdateAccountUIVisibility();[cite: 7]
                    StartStatusCheck();[cite: 7]
                }
                else
                {
                    File.Delete(SessionFile);[cite: 7]
                }
            }
        }
        catch { }
    }

    private void SaveSession(string username, string password)[cite: 7]
    {
        try
        {
            var session = new SavedSession { Username = username, Password = password };[cite: 7]
            string json = JsonSerializer.Serialize(session);[cite: 7]
            File.WriteAllText(SessionFile, json);[cite: 7]
        }
        catch { }
    }

    private async void LoginAccountButton_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        string username = AccountUsernameTextBox.Text.Trim();[cite: 7]
        string password = _rawPassword;[cite: 7]

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            AccountStatusText.Text = "Bitte alle Felder ausfüllen.";[cite: 7]
            return;
        }

        try
        {
            AccountLoginButton.IsEnabled = false;[cite: 7]
            AccountStatusText.Text = "Anmeldung läuft...";[cite: 7]

            using HttpClient client = new();[cite: 7]
            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/login", new { username, password });[cite: 7]
            var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 7]

            if (result != null && result.success)
            {
                LoggedInUsername = result.username ?? username;[cite: 7]
                LoggedInPassword = password;[cite: 7]
                LoggedInRole = result.role ?? "user";[cite: 7]
                HasBetaAccess = result.hasBetaAccess;[cite: 7]

                SaveSession(username, password);[cite: 7]

                AccountStatusText.Text = "";[cite: 7]
                AccountPasswordBox.Clear();[cite: 7]
                AccountPasswordVisibleTextBox.Clear();[cite: 7]
                _rawPassword = "";[cite: 7]

                AdminMenuButton.Visibility = LoggedInRole == "admin" ? Visibility.Visible : Visibility.Collapsed;[cite: 7]
                UpdateHomeInformation();[cite: 7]
                UpdateAccountUIVisibility();[cite: 7]
                StartStatusCheck();[cite: 7]

                ShowPage(result.mustChangePassword ? ChangePasswordPage : HomePage);[cite: 7]
            }
            else
            {
                AccountStatusText.Text = result?.message ?? "Login fehlgeschlagen.";[cite: 7]
            }
        }
        catch
        {
            AccountStatusText.Text = "Server nicht erreichbar.";[cite: 7]
        }
        finally
        {
            AccountLoginButton.IsEnabled = true;[cite: 7]
        }
    }

    private async void SaveDisplayNameButton_Click(object sender, RoutedEventArgs e)
    {
        string newDisplayName = NewDisplayNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(newDisplayName))
        {
            MessageBox.Show("Bitte einen gültigen Anzeigenamen eingeben.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using HttpClient client = new();
            if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
            if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);

            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/update-display-name", new { username = LoggedInUsername, newDisplayName = newDisplayName });
            string responseString = await response.Content.ReadAsStringAsync();

            if (responseString.TrimStart().StartsWith("<"))
            {
                MessageBox.Show("Der Server hat unerwartet HTML statt JSON zurückgegeben (Endpunkt-Fehler).", "Server-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = JsonSerializer.Deserialize<AccountResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result != null && result.success)
            {
                MessageBox.Show("Anzeigename erfolgreich geändert!", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                CornerUsernameText.Text = newDisplayName;
                ProfileUsernameDisplay.Text = newDisplayName;
                NewDisplayNameTextBox.Clear();
            }
            else
            {
                MessageBox.Show(result?.message ?? "Fehler beim Ändern des Anzeigenamens.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Server nicht erreichbar: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveNewPasswordButton_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        string newPw = NewPasswordBox.Password;[cite: 7]
        string confirmPw = ConfirmPasswordBox.Password;[cite: 7]

        if (newPw.Length < 6) { ChangePasswordStatusText.Text = "Mindestens 6 Zeichen."; return; }[cite: 7]
        if (newPw != confirmPw) { ChangePasswordStatusText.Text = "Passwörter stimmen nicht überein."; return; }[cite: 7]

        try
        {
            using HttpClient client = new();[cite: 7]
            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/change-first-password", new { username = LoggedInUsername, currentPassword = LoggedInPassword, newPassword = newPw });[cite: 7]
            var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 7]

            if (result != null && result.success)
            {
                LoggedInPassword = newPw;[cite: 7]
                SaveSession(LoggedInUsername ?? "", newPw);[cite: 7]
                MessageBox.Show("Passwort erfolgreich geändert!", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);[cite: 7]
                ShowPage(HomePage);[cite: 7]
            }
            else { ChangePasswordStatusText.Text = result?.message ?? "Fehler."; }
        }
        catch { ChangePasswordStatusText.Text = "Server nicht erreichbar."; }
    }

    private async Task LoadAdminUserListAsync()[cite: 7]
    {
        try
        {
            using HttpClient client = new();[cite: 7]
            if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);[cite: 7]
            if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);[cite: 7]
            
            var response = await client.GetAsync($"{AccountServerUrl}/api/admin/users");[cite: 7]
            
            if (!response.IsSuccessStatusCode)
            {
                AdminActionStatus.Text = $"Server-Fehler: {(int)response.StatusCode} {response.ReasonPhrase}";[cite: 7]
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<AdminUserListResponse>();[cite: 7]
            if (result != null && result.success)
            {
                UsersDataGrid.ItemsSource = result.users;[cite: 7]
                AdminActionStatus.Text = $"Benutzer erfolgreich geladen ({result.users.Count}).";[cite: 7]
            }
            else
            {
                AdminActionStatus.Text = "Server meldet Erfolg = false.";[cite: 7]
            }
        }
        catch (Exception ex) 
        {  
            AdminActionStatus.Text = "Fehler: " + ex.Message;[cite: 7]
        }
    }

    private async void AdminCreateUser_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        string username = AdminNewUsernameBox.Text.Trim();[cite: 7]
        string tempPassword = AdminNewTempPassBox.Text.Trim();[cite: 7]
        string role = (AdminRoleComboBox.SelectedItem as ComboBoxItem)?.Content.ToString()?.ToLower() ?? "user";[cite: 7]

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(tempPassword)) return;[cite: 7]

        try
        {
            using HttpClient client = new();[cite: 7]
            if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);[cite: 7]
            if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);[cite: 7]
            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/create-user", new { username, tempPassword, role });[cite: 7]
            var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 7]
            AdminActionStatus.Text = result?.message ?? "";[cite: 7]
            if (result != null && result.success) { AdminNewUsernameBox.Clear(); AdminNewTempPassBox.Clear(); await LoadAdminUserListAsync(); }[cite: 7]
        }
        catch { AdminActionStatus.Text = "Fehler."; }
    }

    private async void AdminToggleBeta_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        if ((sender as Button)?.DataContext is UserItem user)
        {
            try
            {
                using HttpClient client = new();[cite: 7]
                if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);[cite: 7]
                if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);[cite: 7]
                
                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/toggle-beta", new { username = user.Username });[cite: 7]
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();[cite: 7]
                
                if (result != null && result.success)
                {
                    AdminActionStatus.Text = $"Beta-Zugang für {user.Username} aktualisiert.";[cite: 7]
                    await LoadAdminUserListAsync();[cite: 7]
                }
                else
                {
                    AdminActionStatus.Text = result?.message ?? "Fehler beim Aktualisieren des Beta-Zugangs.";[cite: 7]
                }
            }
            catch (Exception ex) 
            {  
                AdminActionStatus.Text = "Fehler: " + ex.Message;[cite: 7]
            }
        }
    }

    private async void AdminResetPw_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        if ((sender as Button)?.DataContext is UserItem user)
        {
            string newTempPw = "Temp1234!";[cite: 7]
            try
            {
                using HttpClient client = new();[cite: 7]
                if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);[cite: 7]
                if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);[cite: 7]
                await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/reset-password", new { username = user.Username, newTempPassword = newTempPw });[cite: 7]
                MessageBox.Show($"Passwort für {user.Username} zurückgesetzt.\nTemp: {newTempPw}", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);[cite: 7]
                await LoadAdminUserListAsync();[cite: 7]
            }
            catch { }
        }
    }

    private async void AdminToggleLock_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        if ((sender as Button)?.DataContext is UserItem user)
        {
            try
            {
                using HttpClient client = new();[cite: 7]
                if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);[cite: 7]
                if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);[cite: 7]
                await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/toggle-lock", new { username = user.Username });[cite: 7]
                await LoadAdminUserListAsync();[cite: 7]
            }
            catch { }
        }
    }

    private async void AdminDeleteUser_Click(object sender, RoutedEventArgs e)[cite: 7]
    {
        if ((sender as Button)?.DataContext is UserItem user)
        {
            if (ProtectedAdminUsernames.Contains(user.Username, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show($"Der Haupt-Admin '{user.Username}' kann nicht gelöscht werden.", "Gesperrt", MessageBoxButton.OK, MessageBoxImage.Stop);[cite: 7]
                return;
            }

            if (MessageBox.Show($"Benutzer '{user.Username}' löschen?", "Bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    using HttpClient client = new();[cite: 7]
                    if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);[cite: 7]
                    if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);[cite: 7]
                    await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/delete-user", new { username = user.Username });[cite: 7]
                    await LoadAdminUserListAsync();[cite: 7]
                }
                catch { }
            }
        }
    }

    private PerformanceCounterWrapper? PerformanceCounter;[cite: 7]

    private void StartPerformanceMonitor()[cite: 7]
    {
        try
        {
            PerformanceCounter = new PerformanceCounterWrapper();[cite: 7]
            PerformanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };[cite: 7]
            PerformanceTimer.Tick += (s, e) =>
            {
                if (PerformanceCounter == null) return;[cite: 7]
                CpuText.Text = $"CPU: {PerformanceCounter.GetCpuUsage():0}%";[cite: 7]
                RamText.Text = $"RAM: {PerformanceCounter.GetRamUsage():0}%";[cite: 7]
            };
            PerformanceTimer.Start();[cite: 7]
        }
        catch
        {
            CpuText.Text = "CPU: --";[cite: 7]
            RamText.Text = "RAM: --";[cite: 7]
        }
    }

    private sealed class LauncherVersionInfo[cite: 7]
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }[cite: 7]

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; set; }[cite: 7]
    }

    private sealed class SavedSession[cite: 7]
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }[cite: 7]

        [JsonPropertyName("password")]
        public string? Password { get; set; }[cite: 7]
    }

    private sealed class GitHubRelease[cite: 7]
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }[cite: 7]

        [JsonPropertyName("body")]
        public string? Body { get; set; }[cite: 7]

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }[cite: 7]

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }[cite: 7]

        [JsonPropertyName("assets")]
        public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();[cite: 7]
    }

    private sealed class GitHubAsset[cite: 7]
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }[cite: 7]

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }[cite: 7]
    }

    private sealed class AccountResponse[cite: 7]
    {
        [JsonPropertyName("success")]
        public bool success { get; set; }[cite: 7]

        [JsonPropertyName("message")]
        public string? message { get; set; }[cite: 7]

        [JsonPropertyName("username")]
        public string? username { get; set; }[cite: 7]

        [JsonPropertyName("role")]
        public string? role { get; set; }[cite: 7]

        [JsonPropertyName("hasBetaAccess")]
        public bool hasBetaAccess { get; set; }[cite: 7]

        [JsonPropertyName("mustChangePassword")]
        public bool mustChangePassword { get; set; }[cite: 7]

        [JsonPropertyName("isLocked")]
        public bool isLocked { get; set; }[cite: 7]
    }

    private sealed class AdminUserListResponse[cite: 7]
    {
        [JsonPropertyName("success")]
        public bool success { get; set; }[cite: 7]

        [JsonPropertyName("users")]
        public List<UserItem> users { get; set; } = new();[cite: 7]
    }

    public sealed class UserItem[cite: 7]
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }[cite: 7]

        [JsonPropertyName("role")]
        public string? Role { get; set; }[cite: 7]

        [JsonPropertyName("hasBetaAccess")]
        public bool HasBetaAccess { get; set; }[cite: 7]

        [JsonPropertyName("isLocked")]
        public bool IsLocked { get; set; }[cite: 7]

        [JsonPropertyName("mustChangePassword")]
        public bool MustChangePassword { get; set; }[cite: 7]
    }

    private sealed class PerformanceCounterWrapper[cite: 7]
    {
        private readonly PerformanceCounter? cpuCounter;[cite: 7]
        private readonly Process currentProcess;[cite: 7]

        public PerformanceCounterWrapper()[cite: 7]
        {
            currentProcess = Process.GetCurrentProcess();[cite: 7]
            try
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);[cite: 7]
                cpuCounter.NextValue();[cite: 7]
            }
            catch
            {
                cpuCounter = null;[cite: 7]
            }
        }

        public float GetCpuUsage()[cite: 7]
        {
            try
            {
                return cpuCounter?.NextValue() ?? 0f;[cite: 7]
            }
            catch
            {
                return 0f;[cite: 7]
            }
        }

        public float GetRamUsage()[cite: 7]
        {
            try
            {
                currentProcess.Refresh();[cite: 7]
                long workingSet = currentProcess.WorkingSet64;[cite: 7]
                long totalPhysicalMemory = GetTotalMemoryInBytes();[cite: 7]
                if (totalPhysicalMemory <= 0) return 0f;[cite: 7]
                return (float)((double)workingSet / totalPhysicalMemory * 100.0);[cite: 7]
            }
            catch
            {
                return 0f;[cite: 7]
            }
        }

        private static long GetTotalMemoryInBytes()[cite: 7]
        {
            try
            {
                var gcMemoryInfo = GC.GetGCMemoryInfo();[cite: 7]
                return gcMemoryInfo.TotalAvailableMemoryBytes;[cite: 7]
            }
            catch
            {
                return 1024L * 1024L * 1024L * 8L;[cite: 7]
            }
        }
    }
}
