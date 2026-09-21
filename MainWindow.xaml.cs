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
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BetaLauncher;

public partial class MainWindow : Window
{
    private static readonly string CurrentLauncherVersion = 
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    private const string LauncherVersionUrl = "https://raw.githubusercontent.com/Jannik2401/RFGLauncher/main/version.json";
    private const string NewsJsonUrl = "https://raw.githubusercontent.com/Jannik2401/RFGLauncher/main/news.json";
    private const string GitHubOwner = "Jannik2401";
    private const string GitHubRepo = "RFGLauncher";
    private const string GameExeName = "kirmes.exe";
    private const string AccountServerUrl = "http://node1.waifly.com:25433";

    private static readonly string[] ProtectedAdminUsernames = { "admin" };

    private const string DiscordUrl = "https://discord.gg/qaxg7UdafU";
    private const string TwitchUrl = "https://www.twitch.tv/realistic_funfair_games";
    private const string InstagramUrl = "https://www.instagram.com/realistic_funfair_games/";
    private const string TikTokUrl = "https://www.tiktok.com/@realisticfunfairgames";

    private readonly string GameDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RealisticFunfairGames",
        "Game"
    );

    private string VersionFile => Path.Combine(GameDirectory, "version.txt");
    private string UpdateDateFile => Path.Combine(GameDirectory, "last_update_date.txt");
    private string SessionFile => Path.Combine(GameDirectory, "session.json");
    private string SettingsFile => Path.Combine(GameDirectory, "settings.json");
    private string LastNewsIdFile => Path.Combine(GameDirectory, "last_news_id.txt");

    private readonly HttpClient Http = new();
    private DispatcherTimer? PerformanceTimer;
    private DispatcherTimer? StatusCheckTimer;
    private DispatcherTimer? LiveUpdateCheckTimer;

    private string? LoggedInUsername;
    private string? LoggedInPassword;
    private string? LoggedInRole;
    private bool HasBetaAccess;

    private string _lastSeenNewsId = string.Empty;

    private bool _isPasswordVisible;
    private string _rawPassword = string.Empty;

    private bool _inlinePasswordVisible;
    private string _inlineRawPassword = string.Empty;

    private bool _isInitializingTheme = true;

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

            LoadSettings();
            _isInitializingTheme = false;

            ShowPage(HomePage);
            UpdateHomeInformation();
            StartPerformanceMonitor();

            LauncherVersionText.Text = $"Version: {CurrentLauncherVersion}";

            // Dreistufige Start-Animation ausführen
            await PlayStartupSequenceAsync();

            await SilentCheckLauncherUpdateAsync();
            await CheckForUpdatesAsync();
            await CheckLiveNewsAsync(); // Live-News beim Start prüfen
            await TryAutoLoginAsync();
            UpdateAccountUIVisibility();

            // Live-Update-Checker & News-Checker im 3-Sekunden-Takt im Hintergrund starten
            StartLiveUpdateChecker();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Launcher-Fehler: " + ex.Message;
        }
    }

    private async Task PlayStartupSequenceAsync()
    {
        // Stufe 1: Hintergrund sanft einblenden
        DoubleAnimation fadeInBg = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(0.4));
        StartupIntroGrid.BeginAnimation(UIElement.OpacityProperty, fadeInBg);
        await Task.Delay(400);

        // Stufe 2: Fortschrittsanzeige mit flüssigem Übergang einblenden
        DoubleAnimation fadeInProgress = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(0.5));
        DoubleAnimation slideProgress = new DoubleAnimation(20, 0, TimeSpan.FromSeconds(0.5)) { DecelerationRatio = 0.3 };
        
        StartupProgressContainer.BeginAnimation(UIElement.OpacityProperty, fadeInProgress);
        StartupProgressContainer.BeginAnimation(TranslateTransform.YProperty, slideProgress);
        StartupPercentageText.BeginAnimation(UIElement.OpacityProperty, fadeInProgress);

        // Ladebalken langsamer und flüssiger laufen lassen (55ms pro 2%-Schritt)
        for (int i = 0; i <= 100; i += 2)
        {
            StartupProgressBar.Value = i;
            StartupPercentageText.Text = $"{i}%";
            await Task.Delay(55);
        }

        // Stufe 3: Intro sanft ausblenden
        DoubleAnimation fadeOutIntro = new DoubleAnimation(1.0, 0.0, TimeSpan.FromSeconds(0.5));
        StartupIntroGrid.BeginAnimation(UIElement.OpacityProperty, fadeOutIntro);
        await Task.Delay(500);
        StartupIntroGrid.Visibility = Visibility.Collapsed;

        // Haupt-Launcher und Logo flüssig einfliegen lassen
        DoubleAnimation fadeInCore = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(0.6));
        LauncherCoreGrid.BeginAnimation(UIElement.OpacityProperty, fadeInCore);

        DoubleAnimation logoSlide = new DoubleAnimation(-25, 0, TimeSpan.FromSeconds(0.6)) { DecelerationRatio = 0.3 };
        LogoTransform.BeginAnimation(TranslateTransform.YProperty, logoSlide);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        PerformanceTimer?.Stop();
        StatusCheckTimer?.Stop();
        LiveUpdateCheckTimer?.Stop();
        Http.Dispose();
    }

    private void StartLiveUpdateChecker()
    {
        LiveUpdateCheckTimer?.Stop();
        LiveUpdateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        LiveUpdateCheckTimer.Tick += async (s, e) =>
        {
            await SilentCheckLauncherUpdateAsync();
            await CheckForUpdatesAsync();
            await CheckLiveNewsAsync();
        };
        LiveUpdateCheckTimer.Start();
    }

    private async Task CheckLiveNewsAsync()
    {
        try
        {
            if (File.Exists(LastNewsIdFile))
            {
                _lastSeenNewsId = File.ReadAllText(LastNewsIdFile).Trim();
            }

            using HttpClient client = new();
            string urlWithCacheBuster = $"{NewsJsonUrl}?t={DateTime.UtcNow.Ticks}";
            
            var response = await client.GetAsync(urlWithCacheBuster);
            if (response.IsSuccessStatusCode)
            {
                var newsList = await response.Content.ReadFromJsonAsync<List<NewsItem>>();
                if (newsList != null && newsList.Count > 0)
                {
                    var latestNews = newsList[0];

                    // Befüllt die News-Ansicht in der Sidebar
                    NewsItemsControl.ItemsSource = newsList;

                    // Popup nur anzeigen, wenn diese News-ID noch nicht lokal bestätigt wurde
                    if (!string.IsNullOrWhiteSpace(latestNews.Id) && latestNews.Id != _lastSeenNewsId)
                    {
                        PopupNewsTitle.Text = latestNews.Title;
                        PopupNewsContent.Text = latestNews.Content;
                        PopupNewsTitle.Tag = latestNews.Id;
                        
                        LiveNewsPopupOverlay.Visibility = Visibility.Visible;
                    }
                }
            }
        }
        catch { }
    }

    private void CloseLiveNewsPopup_Click(object sender, RoutedEventArgs e)
    {
        LiveNewsPopupOverlay.Visibility = Visibility.Collapsed;

        // Speichert lokal auf dem PC ab, dass diese News gelesen wurde
        if (PopupNewsTitle.Tag is string newsId && !string.IsNullOrEmpty(newsId))
        {
            try
            {
                File.WriteAllText(LastNewsIdFile, newsId);
                _lastSeenNewsId = newsId;
            }
            catch { }
        }
    }

    private void NewsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NewsPage);
        _ = CheckLiveNewsAsync();
    }

    private void ApplyTheme(string theme)
    {
        var appResources = Application.Current.Resources;

        if (theme == "Light")
        {
            appResources["WindowBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")!);
            appResources["CardBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")!);
            appResources["TextPrimaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")!);
            appResources["TextSecondaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")!);
            appResources["SidebarBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")!);
            appResources["InputBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")!);
        }
        else
        {
            appResources["WindowBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")!);
            appResources["CardBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")!);
            appResources["TextPrimaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")!);
            appResources["TextSecondaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")!);
            appResources["SidebarBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")!);
            appResources["InputBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")!);
        }

        this.InvalidateVisual();
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingTheme) return;

        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string theme)
        {
            ApplyTheme(theme);
            SaveSettings(theme);
        }
    }

    private void SaveSettings(string theme)
    {
        try
        {
            var settings = new { Theme = theme };
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings));
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            string theme = "Dark";
            if (File.Exists(SettingsFile))
            {
                string json = File.ReadAllText(SettingsFile);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Theme", out var themeProp))
                {
                    theme = themeProp.GetString() ?? "Dark";
                }
            }

            ApplyTheme(theme);

            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Tag?.ToString() == theme)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }
        }
        catch { }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Link konnte nicht geöffnet werden:\n" + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DiscordButton_Click(object sender, RoutedEventArgs e) => OpenUrl(DiscordUrl);
    private void TwitchButton_Click(object sender, RoutedEventArgs e) => OpenUrl(TwitchUrl);
    private void InstagramButton_Click(object sender, RoutedEventArgs e) => OpenUrl(InstagramUrl);
    private void TikTokButton_Click(object sender, RoutedEventArgs e) => OpenUrl(TikTokUrl);

    private void UpdateAccountUIVisibility()
    {
        bool isLoggedIn = !string.IsNullOrEmpty(LoggedInUsername);

        AccountMenuButton.Visibility = isLoggedIn ? Visibility.Collapsed : Visibility.Visible;
        UserProfileCornerBox.Visibility = isLoggedIn ? Visibility.Visible : Visibility.Collapsed;

        if (isLoggedIn)
        {
            CornerUsernameText.Text = LoggedInUsername;
            CornerRoleText.Text = $"Rolle: {LoggedInRole?.ToUpper()}";

            AccountLoginPanel.Visibility = Visibility.Collapsed;
            AccountProfilePanel.Visibility = Visibility.Visible;
            ProfileUsernameDisplay.Text = LoggedInUsername;
        }
        else
        {
            AccountLoginPanel.Visibility = Visibility.Visible;
            AccountProfilePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(SessionFile)) File.Delete(SessionFile);
        }
        catch { }

        LoggedInUsername = null;
        LoggedInPassword = null;
        LoggedInRole = null;
        HasBetaAccess = false;

        AdminMenuButton.Visibility = Visibility.Collapsed;

        UpdateHomeInformation();
        UpdateAccountUIVisibility();
        ShowPage(HomePage);
    }

    private void AccountPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isPasswordVisible) _rawPassword = AccountPasswordBox.Password;
    }

    private void AccountPasswordVisibleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isPasswordVisible) _rawPassword = AccountPasswordVisibleTextBox.Text;
    }

    private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;

        if (_isPasswordVisible)
        {
            AccountPasswordVisibleTextBox.Text = _rawPassword;
            AccountPasswordBox.Visibility = Visibility.Collapsed;
            AccountPasswordVisibleTextBox.Visibility = Visibility.Visible;
            TogglePasswordBtn.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#38BDF8")!;
        }
        else
        {
            AccountPasswordBox.Password = _rawPassword;
            AccountPasswordVisibleTextBox.Visibility = Visibility.Collapsed;
            AccountPasswordBox.Visibility = Visibility.Visible;
            TogglePasswordBtn.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#94A3B8")!;
        }
    }

    private void UserProfileCornerBox_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(LoggedInUsername)) return;

        InlineTxtLoginUsername.Text = LoggedInUsername;
        InlineTxtDisplayName.Text = CornerUsernameText.Text;
        _inlineRawPassword = LoggedInPassword ?? string.Empty;
        InlinePwdBox.Password = _inlineRawPassword;
        _inlinePasswordVisible = false;
        InlinePwdBox.Visibility = Visibility.Visible;
        InlineTxtVisiblePassword.Visibility = Visibility.Collapsed;
        InlineBtnTogglePwd.Content = "Show";

        AccountSettingsPanel.Visibility = Visibility.Visible;
    }

    private void InlinePwdBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_inlinePasswordVisible) _inlineRawPassword = InlinePwdBox.Password;
    }

    private void InlineTxtVisiblePassword_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_inlinePasswordVisible) _inlineRawPassword = InlineTxtVisiblePassword.Text;
    }

    private void InlineBtnTogglePwd_Click(object sender, RoutedEventArgs e)
    {
        _inlinePasswordVisible = !_inlinePasswordVisible;
        if (_inlinePasswordVisible)
        {
            InlineTxtVisiblePassword.Text = _inlineRawPassword;
            InlinePwdBox.Visibility = Visibility.Collapsed;
            InlineTxtVisiblePassword.Visibility = Visibility.Visible;
            InlineBtnTogglePwd.Content = "Hide";
        }
        else
        {
            InlinePwdBox.Password = _inlineRawPassword;
            InlineTxtVisiblePassword.Visibility = Visibility.Collapsed;
            InlinePwdBox.Visibility = Visibility.Visible;
            InlineBtnTogglePwd.Content = "Show";
        }
    }

    private void InlineSaveButton_Click(object sender, RoutedEventArgs e)
    {
        string newName = InlineTxtDisplayName.Text.Trim();
        if (!string.IsNullOrEmpty(newName))
        {
            NewDisplayNameTextBox.Text = newName;
            SaveDisplayNameButton_Click(sender, e);
        }
        AccountSettingsPanel.Visibility = Visibility.Collapsed;
    }

    private void InlineCancelButton_Click(object sender, RoutedEventArgs e)
    {
        AccountSettingsPanel.Visibility = Visibility.Collapsed;
    }

    private async Task SilentCheckLauncherUpdateAsync()
    {
        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher-Updater");
            client.Timeout = TimeSpan.FromSeconds(5);

            string onlineVersionStr = string.Empty;
            string downloadUrl = string.Empty;

            try
            {
                string apiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();
                    if (release != null && !string.IsNullOrWhiteSpace(release.TagName))
                    {
                        onlineVersionStr = release.TagName;
                        var asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, "RFGlauncher.exe", StringComparison.OrdinalIgnoreCase));
                        downloadUrl = asset?.BrowserDownloadUrl ?? $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/latest/RFGlauncher.exe";
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(onlineVersionStr))
            {
                try
                {
                    string urlWithCacheBuster = $"{LauncherVersionUrl}?t={DateTime.UtcNow.Ticks}";
                    var info = await client.GetFromJsonAsync<LauncherVersionInfo>(urlWithCacheBuster);
                    if (info != null && !string.IsNullOrWhiteSpace(info.Version))
                    {
                        onlineVersionStr = info.Version;
                        downloadUrl = info.DownloadUrl ?? downloadUrl;
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(onlineVersionStr))
            {
                Version onlineVersion = ParseVersion(onlineVersionStr);
                Version installedVersion = ParseVersion(CurrentLauncherVersion);

                if (onlineVersion > installedVersion)
                {
                    LauncherUpdateStatusText.Text = $"Neues Update: v{onlineVersion}";
                    LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#38BDF8")!;
                    
                    LauncherUpdateBannerText.Text = $"Version v{onlineVersion} steht bereit.";
                    LauncherUpdateNotificationBanner.Visibility = Visibility.Visible;
                }
                else
                {
                    LauncherUpdateStatusText.Text = "Launcher ist aktuell.";
                    LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;
                    LauncherUpdateNotificationBanner.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch 
        {
            LauncherUpdateStatusText.Text = "Launcher ist aktuell.";
            LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;
        }
    }

    private async void CheckLauncherUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckLauncherUpdateButton.IsEnabled = false;
        try
        {
            LauncherUpdateStatusText.Text = "Suche nach Updates...";
            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RFG-BetaLauncher-Updater");
            client.Timeout = TimeSpan.FromSeconds(5);

            string onlineVersionStr = string.Empty;
            string downloadUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/latest/RFGlauncher.exe";

            try
            {
                string apiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();
                    if (release != null && !string.IsNullOrWhiteSpace(release.TagName))
                    {
                        onlineVersionStr = release.TagName;
                        var asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, "RFGlauncher.exe", StringComparison.OrdinalIgnoreCase));
                        if (asset?.BrowserDownloadUrl != null) downloadUrl = asset.BrowserDownloadUrl;
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(onlineVersionStr))
            {
                string urlWithCacheBuster = $"{LauncherVersionUrl}?t={DateTime.UtcNow.Ticks}";
                var info = await client.GetFromJsonAsync<LauncherVersionInfo>(urlWithCacheBuster);
                onlineVersionStr = info?.Version ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(info?.DownloadUrl)) downloadUrl = info.DownloadUrl;
            }

            if (!string.IsNullOrWhiteSpace(onlineVersionStr))
            {
                Version onlineVersion = ParseVersion(onlineVersionStr);
                Version installedVersion = ParseVersion(CurrentLauncherVersion);

                if (onlineVersion > installedVersion)
                {
                    LauncherUpdateStatusText.Text = $"Update gefunden: v{onlineVersion}";
                    LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#38BDF8")!;
                    StartAutoUpdater(downloadUrl);
                }
                else
                {
                    MessageBox.Show($"Du nutzt bereits die neueste Version (v{installedVersion}).", "Aktuell", MessageBoxButton.OK, MessageBoxImage.Information);
                    LauncherUpdateStatusText.Text = "Launcher ist aktuell.";
                    LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler bei der Update-Prüfung: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            LauncherUpdateStatusText.Text = "Launcher ist aktuell.";
            LauncherUpdateStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;
        }
        finally
        {
            CheckLauncherUpdateButton.IsEnabled = true;
        }
    }

    private void StartAutoUpdater(string? downloadUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(downloadUrl)) return;
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppContext.BaseDirectory, "BetaLauncher.exe");
            UpdateWindow updateWindow = new UpdateWindow(downloadUrl, currentExe);
            updateWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler beim Starten des Updaters: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowPage(UIElement page)
    {
        HomePage.Visibility = Visibility.Collapsed;
        NewsPage.Visibility = Visibility.Collapsed;
        UpdatesPage.Visibility = Visibility.Collapsed;
        AccountPage.Visibility = Visibility.Collapsed;
        ChangePasswordPage.Visibility = Visibility.Collapsed;
        AdminPage.Visibility = Visibility.Collapsed;
        PerformancePage.Visibility = Visibility.Collapsed;
        CreditsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;

        page.Visibility = Visibility.Visible;
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage);
    private void UpdatesButton_Click(object sender, RoutedEventArgs e) => ShowPage(UpdatesPage);
    private void AccountButton_Click(object sender, RoutedEventArgs e) => ShowPage(AccountPage);
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);
    private void AdminButton_Click(object sender, RoutedEventArgs e) { ShowPage(AdminPage); _ = LoadAdminUserListAsync(); }
    private void PerformanceButton_Click(object sender, RoutedEventArgs e) => ShowPage(PerformancePage);
    private void CreditsButton_Click(object sender, RoutedEventArgs e) => ShowPage(CreditsPage);
    private void ExitButton_Click(object sender, RoutedEventArgs e) => Close();
    private void GoToChangePassword_Click(object sender, RoutedEventArgs e) => ShowPage(ChangePasswordPage);

    private void UpdateHomeInformation()
    {
        string localVersion = GetLocalVersion();
        HomeVersionText.Text = string.IsNullOrWhiteSpace(localVersion) ? "Keine Version installiert" : "Version " + localVersion;

        if (IsGameInstalled())
        {
            HomeStatusText.Text = "INSTALLIERT";
            HomeStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;
        }
        else
        {
            HomeStatusText.Text = "NICHT INSTALLIERT";
            HomeStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;
        }

        if (string.IsNullOrEmpty(LoggedInUsername))
        {
            HomeBetaAccessText.Text = "NICHT EINGELOGGT";
            HomeBetaAccessText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;
        }
        else if (HasBetaAccess)
        {
            HomeBetaAccessText.Text = "ZUGRIFF GEWÄHRT";
            HomeBetaAccessText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!;
        }
        else
        {
            HomeBetaAccessText.Text = "ZUGRIFF VERWEIGERT";
            HomeBetaAccessText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E11D48")!;
        }

        StartButton.IsEnabled = IsGameInstalled() && HasBetaAccess;
    }

    private void StartStatusCheck()
    {
        StatusCheckTimer?.Stop();
        StatusCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        StatusCheckTimer.Tick += async (s, e) =>
        {
            if (string.IsNullOrEmpty(LoggedInUsername)) return;

            try
            {
                using HttpClient client = new();
                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/user-status", new { username = LoggedInUsername });
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();

                if (result != null && result.Success)
                {
                    bool statusChanged = HasBetaAccess != result.HasBetaAccess || LoggedInRole != result.Role || result.IsLocked;
                    
                    HasBetaAccess = result.HasBetaAccess;
                    LoggedInRole = result.Role ?? "user";

                    if (result.IsLocked)
                    {
                        MessageBox.Show("Dein Account wurde gesperrt.", "Sicherheit", MessageBoxButton.OK, MessageBoxImage.Error);
                        if (File.Exists(SessionFile)) File.Delete(SessionFile);
                        LoggedInUsername = null;
                        LoggedInPassword = null;
                        ShowPage(AccountPage);
                        UpdateHomeInformation();
                        UpdateAccountUIVisibility();
                        StatusCheckTimer?.Stop();
                        return;
                    }

                    if (statusChanged)
                    {
                        AdminMenuButton.Visibility = LoggedInRole == "admin" ? Visibility.Visible : Visibility.Collapsed;
                        UpdateHomeInformation();
                        UpdateAccountUIVisibility();
                    }
                }
            }
            catch { }
        };
        StatusCheckTimer.Start();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(LoggedInUsername))
            {
                MessageBox.Show("Bitte zuerst anmelden.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowPage(AccountPage);
                return;
            }

            try
            {
                using HttpClient client = new();
                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/user-status", new { username = LoggedInUsername });
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();
                if (result != null && result.Success)
                {
                    HasBetaAccess = result.HasBetaAccess;
                    if (result.IsLocked || !HasBetaAccess)
                    {
                        MessageBox.Show("Kein aktiver Beta-Zugriff oder Account gesperrt.", "Zugriff verweigert", MessageBoxButton.OK, MessageBoxImage.Stop);
                        UpdateHomeInformation();
                        return;
                    }
                }
            }
            catch { }

            if (!HasBetaAccess)
            {
                MessageBox.Show("Du hast keinen Beta-Zugriff.", "Zugriff verweigert", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? gameExe = FindGameExe();
            if (gameExe == null)
            {
                MessageBox.Show("Spiel-Executable nicht gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("Fehler beim Starten: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool IsGameInstalled() => FindGameExe() != null;

    private string? FindGameExe()
    {
        string directPath = Path.Combine(GameDirectory, GameExeName);
        if (File.Exists(directPath)) return directPath;
        if (!Directory.Exists(GameDirectory)) return null;
        try { return Directory.GetFiles(GameDirectory, GameExeName, SearchOption.AllDirectories).FirstOrDefault(); }
        catch { return null; }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e) => await DownloadAndInstallLatestAsync();

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var release = await GetLatestGameReleaseAsync();
            UpdateButton.IsEnabled = true;

            if (release == null) 
            { 
                StatusText.Text = "Kein Release gefunden."; 
                ReleaseNotesText.Text = "Keine Release Notes verfügbar.";
                return; 
            }

            string remoteVersion = release.TagName?.Trim() ?? "unknown";
            string localVersion = GetLocalVersion();

            bool isNewerRelease = false;
            if (File.Exists(UpdateDateFile))
            {
                if (DateTime.TryParse(File.ReadAllText(UpdateDateFile).Trim(), out DateTime lastInstallDate))
                {
                    if (release.PublishedAt > lastInstallDate)
                    {
                        isNewerRelease = true;
                    }
                }
            }
            else
            {
                isNewerRelease = true;
            }

            bool updateAvailable = isNewerRelease || !string.Equals(remoteVersion, localVersion, StringComparison.OrdinalIgnoreCase) || !IsGameInstalled();

            if (updateAvailable)
            {
                StatusText.Text = $"Update verfügbar: {remoteVersion}";
                GameUpdateBannerText.Text = $"Version {remoteVersion} ist verfügbar.";
                GameUpdateNotificationBanner.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Text = "Spiel ist aktuell.";
                GameUpdateNotificationBanner.Visibility = Visibility.Collapsed;
            }

            VersionText.Text = "Installiert: " + (string.IsNullOrWhiteSpace(localVersion) ? "Keine" : localVersion);
            
            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(release.Body) 
                ? "Keine Release Notes für diese Version eingetragen." 
                : release.Body;
        }
        catch (Exception ex) 
        { 
            StatusText.Text = "Fehler bei Update-Prüfung.";
            ReleaseNotesText.Text = "Fehler beim Laden der Release Notes: " + ex.Message;
        }
    }

    private async Task DownloadAndInstallLatestAsync()
    {
        try
        {
            UpdateButton.IsEnabled = false;
            var release = await GetLatestGameReleaseAsync();
            if (release == null) return;

            var asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, "game.zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null) { MessageBox.Show("game.zip fehlt im Release.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            string tempZip = Path.Combine(Path.GetTempPath(), "RFG_game_update.zip");
            if (File.Exists(tempZip)) File.Delete(tempZip);

            StatusText.Text = "Lade herunter...";
            using (HttpClient client = new()) { await DownloadFileWithClientAsync(client, asset.BrowserDownloadUrl, tempZip); }

            StatusText.Text = "Installiere...";
            InstallZip(tempZip);
            File.Delete(tempZip);

            File.WriteAllText(VersionFile, release.TagName?.Trim() ?? "unknown");
            File.WriteAllText(UpdateDateFile, release.PublishedAt.ToString("O"));

            StatusText.Text = "Erfolgreich installiert!";
            GameUpdateNotificationBanner.Visibility = Visibility.Collapsed;
            UpdateHomeInformation();
            await CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Installation fehlgeschlagen.";
            MessageBox.Show("Fehler: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { UpdateButton.IsEnabled = true; }
    }

    private void CloseGameBanner_Click(object sender, RoutedEventArgs e)
    {
        GameUpdateNotificationBanner.Visibility = Visibility.Collapsed;
    }

    private async void BannerGameUpdate_Click(object sender, RoutedEventArgs e)
    {
        GameUpdateNotificationBanner.Visibility = Visibility.Collapsed;
        ShowPage(UpdatesPage);
        await DownloadAndInstallLatestAsync();
    }

    private void CloseLauncherBanner_Click(object sender, RoutedEventArgs e)
    {
        LauncherUpdateNotificationBanner.Visibility = Visibility.Collapsed;
    }

    private async void BannerLauncherUpdate_Click(object sender, RoutedEventArgs e)
    {
        LauncherUpdateNotificationBanner.Visibility = Visibility.Collapsed;
        try
        {
            using HttpClient client = new();
            string downloadUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/latest/RFGlauncher.exe";
            try
            {
                string apiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();
                    var asset = release?.Assets.FirstOrDefault(a => string.Equals(a.Name, "RFGlauncher.exe", StringComparison.OrdinalIgnoreCase));
                    if (asset?.BrowserDownloadUrl != null) downloadUrl = asset.BrowserDownloadUrl;
                }
            }
            catch { }
            StartAutoUpdater(downloadUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler beim Starten des Updates: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<GitHubRelease?> GetLatestGameReleaseAsync()
    {
        string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases?per_page=15";
        using HttpResponseMessage response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();
        var releases = JsonSerializer.Deserialize<GitHubRelease[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        return releases?.Where(r => !r.Draft && r.Assets.Any(a => string.Equals(a.Name, "game.zip", StringComparison.OrdinalIgnoreCase)))
                        .OrderByDescending(r => r.PublishedAt)
                        .FirstOrDefault();
    }

    private async Task DownloadFileWithClientAsync(HttpClient client, string? url, string destination)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
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
            if (totalBytes.HasValue && totalBytes.Value > 0) Progress.Value = Math.Min(100, totalRead * 100.0 / totalBytes.Value);
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
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destinationPath); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, true);
        }
    }

    private string GetLocalVersion() => File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : string.Empty;
    private Version ParseVersion(string? v) => Version.TryParse(v?.Trim().TrimStart('v', 'V'), out Version? res) ? res : new Version(0, 0, 0);

    private async Task TryAutoLoginAsync()
    {
        try
        {
            if (!File.Exists(SessionFile)) return;

            string json = File.ReadAllText(SessionFile);
            var session = JsonSerializer.Deserialize<SavedSession>(json);

            if (session != null && !string.IsNullOrWhiteSpace(session.Username) && !string.IsNullOrWhiteSpace(session.Password))
            {
                using HttpClient client = new();
                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/login", new { username = session.Username, password = session.Password });
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();

                if (result != null && result.Success)
                {
                    LoggedInUsername = result.Username ?? session.Username;
                    LoggedInPassword = session.Password;
                    LoggedInRole = result.Role ?? "user";
                    HasBetaAccess = result.HasBetaAccess;

                    AdminMenuButton.Visibility = LoggedInRole == "admin" ? Visibility.Visible : Visibility.Collapsed;
                    UpdateHomeInformation();
                    UpdateAccountUIVisibility();
                    StartStatusCheck();
                }
                else
                {
                    File.Delete(SessionFile);
                }
            }
        }
        catch { }
    }

    private void SaveSession(string username, string password)
    {
        try
        {
            var session = new SavedSession { Username = username, Password = password };
            string json = JsonSerializer.Serialize(session);
            File.WriteAllText(SessionFile, json);
        }
        catch { }
    }

    private async void LoginAccountButton_Click(object sender, RoutedEventArgs e)
    {
        string username = AccountUsernameTextBox.Text.Trim();
        string password = _rawPassword;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            AccountStatusText.Text = "Bitte alle Felder ausfüllen.";
            return;
        }

        try
        {
            AccountLoginButton.IsEnabled = false;
            AccountStatusText.Text = "Anmeldung läuft...";

            using HttpClient client = new();
            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/login", new { username, password });
            var result = await response.Content.ReadFromJsonAsync<AccountResponse>();

            if (result != null && result.Success)
            {
                LoggedInUsername = result.Username ?? username;
                LoggedInPassword = password;
                LoggedInRole = result.Role ?? "user";
                HasBetaAccess = result.HasBetaAccess;

                SaveSession(username, password);

                AccountStatusText.Text = string.Empty;
                AccountPasswordBox.Clear();
                AccountPasswordVisibleTextBox.Clear();
                _rawPassword = string.Empty;

                AdminMenuButton.Visibility = LoggedInRole == "admin" ? Visibility.Visible : Visibility.Collapsed;
                UpdateHomeInformation();
                UpdateAccountUIVisibility();
                StartStatusCheck();

                ShowPage(result.MustChangePassword ? ChangePasswordPage : HomePage);
            }
            else
            {
                AccountStatusText.Text = result?.Message ?? "Login fehlgeschlagen.";
            }
        }
        catch
        {
            AccountStatusText.Text = "Server nicht erreichbar.";
        }
        finally
        {
            AccountLoginButton.IsEnabled = true;
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

            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/update-display-name", new { username = LoggedInUsername, newDisplayName });
            string responseString = await response.Content.ReadAsStringAsync();

            if (responseString.TrimStart().StartsWith("<"))
            {
                MessageBox.Show("Der Server hat unerwartet HTML statt JSON zurückgegeben.", "Server-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = JsonSerializer.Deserialize<AccountResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result != null && result.Success)
            {
                MessageBox.Show("Anzeigename erfolgreich geändert!", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                CornerUsernameText.Text = newDisplayName;
                ProfileUsernameDisplay.Text = newDisplayName;
                NewDisplayNameTextBox.Clear();
            }
            else
            {
                MessageBox.Show(result?.Message ?? "Fehler beim Ändern des Anzeigenamens.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Server nicht erreichbar: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveNewPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        string newPw = NewPasswordBox.Password;
        string confirmPw = ConfirmPasswordBox.Password;

        if (newPw.Length < 6) { ChangePasswordStatusText.Text = "Mindestens 6 Zeichen."; return; }
        if (newPw != confirmPw) { ChangePasswordStatusText.Text = "Passwörter stimmen nicht überein."; return; }

        try
        {
            using HttpClient client = new();
            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/change-first-password", new { username = LoggedInUsername, currentPassword = LoggedInPassword, newPassword = newPw });
            var result = await response.Content.ReadFromJsonAsync<AccountResponse>();

            if (result != null && result.Success)
            {
                LoggedInPassword = newPw;
                SaveSession(LoggedInUsername ?? string.Empty, newPw);
                MessageBox.Show("Passwort erfolgreich geändert!", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowPage(HomePage);
            }
            else { ChangePasswordStatusText.Text = result?.Message ?? "Fehler."; }
        }
        catch { ChangePasswordStatusText.Text = "Server nicht erreichbar."; }
    }

    private async Task LoadAdminUserListAsync()
    {
        try
        {
            using HttpClient client = new();
            if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
            if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);
            
            var response = await client.GetAsync($"{AccountServerUrl}/api/admin/users");
            
            if (!response.IsSuccessStatusCode)
            {
                AdminActionStatus.Text = $"Server-Fehler: {(int)response.StatusCode} {response.ReasonPhrase}";
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<AdminUserListResponse>();
            if (result != null && result.Success)
            {
                UsersItemsControl.ItemsSource = result.Users;
                AdminActionStatus.Text = $"Benutzer erfolgreich geladen ({result.Users.Count}).";
            }
            else
            {
                AdminActionStatus.Text = "Server meldet Erfolg = false.";
            }
        }
        catch (Exception ex) 
        {  
            AdminActionStatus.Text = "Fehler: " + ex.Message;
        }
    }

    private async void AdminCreateUser_Click(object sender, RoutedEventArgs e)
    {
        string username = AdminNewUsernameBox.Text.Trim();
        string tempPassword = AdminNewTempPassBox.Text.Trim();
        string role = (AdminRoleComboBox.SelectedItem as ComboBoxItem)?.Content.ToString()?.ToLower() ?? "user";

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(tempPassword)) return;

        try
        {
            using HttpClient client = new();
            if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
            if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);
            var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/create-user", new { username, tempPassword, role });
            var result = await response.Content.ReadFromJsonAsync<AccountResponse>();
            AdminActionStatus.Text = result?.Message ?? string.Empty;
            if (result != null && result.Success) { AdminNewUsernameBox.Clear(); AdminNewTempPassBox.Clear(); await LoadAdminUserListAsync(); }
        }
        catch { AdminActionStatus.Test = "Fehler."; }
    }

    private async void AdminToggleBeta_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is UserItem user)
        {
            try
            {
                using HttpClient client = new();
                if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
                if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);
                
                var response = await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/toggle-beta", new { username = user.Username });
                var result = await response.Content.ReadFromJsonAsync<AccountResponse>();
                
                if (result != null && result.Success)
                {
                    AdminActionStatus.Text = $"Beta-Zugang für {user.Username} aktualisiert.";
                    await LoadAdminUserListAsync();
                }
                else
                {
                    AdminActionStatus.Text = result?.Message ?? "Fehler beim Aktualisieren des Beta-Zugangs.";
                }
            }
            catch (Exception ex) 
            {  
                AdminActionStatus.Text = "Fehler: " + ex.Message;
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
                if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
                if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);
                await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/reset-password", new { username = user.Username, newTempPassword = newTempPw });
                MessageBox.Show($"Passwort für {user.Username} zurückgesetzt.\nTemp: {newTempPw}", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadAdminUserListAsync();
            }
            catch { }
        }
    }

    private async void AdminToggleLock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is UserItem user)
        {
            try
            {
                using HttpClient client = new();
                if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
                if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);
                await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/toggle-lock", new { username = user.Username });
                await LoadAdminUserListAsync();
            }
            catch { }
        }
    }

    private async void AdminDeleteUser_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is UserItem user)
        {
            if (ProtectedAdminUsernames.Contains(user.Username, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show($"Der Haupt-Admin '{user.Username}' kann nicht gelöscht werden.", "Gesperrt", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (MessageBox.Show($"Benutzer '{user.Username}' löschen?", "Bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    using HttpClient client = new();
                    if (!string.IsNullOrEmpty(LoggedInUsername)) client.DefaultRequestHeaders.Add("X-Admin-User", LoggedInUsername);
                    if (!string.IsNullOrEmpty(LoggedInPassword)) client.DefaultRequestHeaders.Add("X-Admin-Pass", LoggedInPassword);
                    await client.PostAsJsonAsync($"{AccountServerUrl}/api/admin/delete-user", new { username = user.Username });
                    await LoadAdminUserListAsync();
                }
                catch { }
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
            PerformanceTimer.Tick += (s, e) =>
            {
                if (PerformanceCounter == null) return;
                CpuText.Text = $"CPU: {PerformanceCounter.GetCpuUsage():0}%";
                RamText.Text = $"RAM: {PerformanceCounter.GetRamUsage():0}%";
            };
            PerformanceTimer.Start();
        }
        catch
        {
            CpuText.Text = "CPU: --";
            RamText.Text = "RAM: --";
        }
    }

    private sealed class LauncherVersionInfo
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; set; }
    }

    private sealed class SavedSession
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }

    private sealed class AccountResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("hasBetaAccess")]
        public bool HasBetaAccess { get; set; }

        [JsonPropertyName("mustChangePassword")]
        public bool MustChangePassword { get; set; }

        [JsonPropertyName("isLocked")]
        public bool IsLocked { get; set; }
    }

    private sealed class AdminUserListResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("users")]
        public List<UserItem> Users { get; set; } = new();
    }

    public sealed class UserItem
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("hasBetaAccess")]
        public bool HasBetaAccess { get; set; }

        [JsonPropertyName("isLocked")]
        public bool IsLocked { get; set; }

        [JsonPropertyName("mustChangePassword")]
        public bool MustChangePassword { get; set; }

        public string BetaText => HasBetaAccess ? "Beta: Aktiv" : "Beta: Inaktiv";
        public string BetaColor => HasBetaAccess ? "#10B981" : "#E11D48";

        public string LockText => IsLocked ? "Gesperrt: Ja" : "Gesperrt: Nein";
        public string LockColor => IsLocked ? "#E11D48" : "#10B981";
    }

    public sealed class NewsItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }
    }

    public sealed class PerformanceCounterWrapper
    {
        private readonly PerformanceCounter? cpuCounter;
        private readonly Process currentProcess;

        public PerformanceCounterWrapper()
        {
            currentProcess = Process.GetCurrentProcess();
            try
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                cpuCounter.NextValue();
            }
            catch
            {
                cpuCounter = null;
            }
        }

        public float GetCpuUsage()
        {
            try
            {
                return cpuCounter?.NextValue() ?? 0f;
            }
            catch
            {
                return 0f;
            }
        }

        public float GetRamUsage()
        {
            try
            {
                currentProcess.Refresh();
                long workingSet = currentProcess.WorkingSet64;
                long totalPhysicalMemory = GetTotalMemoryInBytes();
                if (totalPhysicalMemory <= 0) return 0f;
                return (float)((double)workingSet / totalPhysicalMemory * 100.0);
            }
            catch
            {
                return 0f;
            }
        }

        private static long GetTotalMemoryInBytes()
        {
            try
            {
                var gcMemoryInfo = GC.GetGCMemoryInfo();
                return gcMemoryInfo.TotalAvailableMemoryBytes;
            }
            catch
            {
                return 1024L * 1024L * 1024L * 8L;
            }
        }
    }
}
