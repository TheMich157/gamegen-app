using ManifestApp.Services;
using ManifestApp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace ManifestApp.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _suppressDarkThemeToggle;
    private bool _suppressDiscordPresenceToggle;
    private string? _latestReleaseUrl;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SyncDarkThemeToggleFromStore();
        SyncDiscordPresenceToggleFromStore();
    }

    private void SyncDarkThemeToggleFromStore()
    {
        _suppressDarkThemeToggle = true;
        DarkThemeToggle.IsOn = TypedApp.Svcs.SettingsStore.Load().PreferDarkUi;
        _suppressDarkThemeToggle = false;
    }

    private void SyncDiscordPresenceToggleFromStore()
    {
        _suppressDiscordPresenceToggle = true;
        DiscordPresenceToggle.IsOn = TypedApp.Svcs.SettingsStore.Load().DiscordRichPresenceDisabled;
        _suppressDiscordPresenceToggle = false;
    }

    private App TypedApp => (App)Application.Current;

    private void SettingsPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var s = TypedApp.Svcs.SettingsStore.Load();

        SteamPathBox.Text = s.SteamInstallPathOverride ?? "";
        PluginPathBox.Text = s.StPluginFolderOverride ?? "";
        DepotPathBox.Text = s.DepotCacheFolderOverride ?? "";
        SteamToolsPathBox.Text = s.SteamToolsExeOverride ?? "";
        GameGenApiBaseUrlBox.Text = s.GameGenApiBaseUrl ?? "";

        ApiKeyStatus.Text = GameGenApiKeyStore.TryRetrieve(out _)
            ? "API key is saved securely on this device."
            : "No API key saved yet. Paste below and tap save.";

        VersionText.Text = UpdateService.CurrentVersionString;
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateInfoBar.IsOpen = false;
        DownloadUpdateButton.Visibility = Visibility.Collapsed;
        _latestReleaseUrl = null;

        try
        {
            var result = await TypedApp.Svcs.UpdateChecker.CheckAsync();

            if (result is null)
            {
                UpdateInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
                UpdateInfoBar.Title = "Could not check for updates";
                UpdateInfoBar.Message = "Make sure you're connected to the internet, or the GitHub repo hasn't been configured yet.";
            }
            else if (result.IsUpdateAvailable)
            {
                UpdateInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
                UpdateInfoBar.Title = $"Update available — v{result.LatestVersion.ToString(3)}";
                UpdateInfoBar.Message = $"You're on v{result.CurrentVersion.ToString(3)}. A newer release is ready to download.";
                _latestReleaseUrl = result.ReleaseUrl;
                DownloadUpdateButton.NavigateUri = new Uri(result.ReleaseUrl);
                DownloadUpdateButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
                UpdateInfoBar.Title = "You're up to date";
                UpdateInfoBar.Message = $"v{result.CurrentVersion.ToString(3)} is the latest release.";
            }

            UpdateInfoBar.IsOpen = true;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void DarkThemeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressDarkThemeToggle)
            return;

        var cur = TypedApp.Svcs.SettingsStore.Load();
        cur.PreferDarkUi = DarkThemeToggle.IsOn;
        TypedApp.Svcs.SettingsStore.Save(cur);

        if (TypedApp.MainShell is MainWindow mw)
            mw.ApplyThemeFromSettings();
    }

    private void DiscordPresenceToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressDiscordPresenceToggle)
            return;

        var cur = TypedApp.Svcs.SettingsStore.Load();
        cur.DiscordRichPresenceDisabled = DiscordPresenceToggle.IsOn;
        TypedApp.Svcs.SettingsStore.Save(cur);
        TypedApp.Svcs.DiscordPresence.Connect();
    }

    private void SaveGameGenSettings_Click(object sender, RoutedEventArgs e)
    {
        var cur = TypedApp.Svcs.SettingsStore.Load();
        cur.GameGenApiBaseUrl = NormalizeOrNull(GameGenApiBaseUrlBox.Text);
        TypedApp.Svcs.SettingsStore.Save(cur);

        var keyProvided = false;
        if (!string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            GameGenApiKeyStore.Replace(ApiKeyBox.Password);
            keyProvided = true;
        }

        ApiKeyBox.Password = string.Empty;

        var vaultOk = GameGenApiKeyStore.TryRetrieve(out _);
        ApiKeyStatus.Text = vaultOk switch
        {
            true when keyProvided => "API key updated securely. Endpoint preference saved.",
            true => "Endpoint saved; API key unchanged in Credential Locker.",
            false when keyProvided => "Key storage failed unexpectedly — retry.",
            _ => "No API key is stored yet. Paste your key above to authorize GameGen HTTPS calls.",
        };
    }

    private async void ResetPathsFromSteam_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var paths = TypedApp.Svcs.PathsResolver;

        var steam = paths.ResolveSteamInstall();
        if (string.IsNullOrEmpty(steam))
        {
            SaveSettingsStatus.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            SaveSettingsStatus.Text =
                "Could not detect Steam. Set Steam install folder above, tap Save Steam settings, then try again.";
            return;
        }

        SteamPathBox.Text = steam;
        PluginPathBox.Text = paths.DefaultStPluginFromSteamRoot(steam) ?? "";
        DepotPathBox.Text = paths.DefaultDepotCacheFromSteamRoot(steam) ?? "";
    }

    private void SaveSettings_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var cur = TypedApp.Svcs.SettingsStore.Load();
        cur.SteamInstallPathOverride = NormalizeOrNull(SteamPathBox.Text);
        cur.StPluginFolderOverride = NormalizeOrNull(PluginPathBox.Text);
        cur.DepotCacheFolderOverride = NormalizeOrNull(DepotPathBox.Text);
        cur.SteamToolsExeOverride = NormalizeOrNull(SteamToolsPathBox.Text);
        TypedApp.Svcs.SettingsStore.Save(cur);

        SaveSettingsStatus.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        SaveSettingsStatus.Text = "Steam paths saved.";
    }

    private async void BrowseSteam_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var path = await PickFolderAsync(PickerLocationId.ComputerFolder);
        if (!string.IsNullOrEmpty(path))
            SteamPathBox.Text = path;
    }

    private async void BrowsePlugin_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var path = await PickFolderAsync(PickerLocationId.ComputerFolder);
        if (!string.IsNullOrEmpty(path))
            PluginPathBox.Text = path;
    }

    private async void BrowseDepot_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var path = await PickFolderAsync(PickerLocationId.ComputerFolder);
        if (!string.IsNullOrEmpty(path))
            DepotPathBox.Text = path;
    }

    private async void BrowseSteamTools_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };

        picker.FileTypeFilter.Add(".exe");

        BindPickerInterop(picker);

        var file = await picker.PickSingleFileAsync();
        if (!string.IsNullOrEmpty(file?.Path))
            SteamToolsPathBox.Text = file.Path;
    }

    private async Task<string?> PickFolderAsync(PickerLocationId start)
    {
        var picker = new FolderPicker { SuggestedStartLocation = start };
        picker.FileTypeFilter.Add("*");
        BindPickerInterop(picker);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private void BindPickerInterop(object picker)
    {
        var hwnd = WindowInterop.GetWindowHandle(TypedApp.MainShell);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    private static string? NormalizeOrNull(string s)
    {
        var t = s.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }
}
