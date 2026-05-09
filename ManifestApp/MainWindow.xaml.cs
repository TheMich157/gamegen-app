using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Navigation;
using ManifestApp.Pages;
using ManifestApp.Services;
using Windows.UI;

namespace ManifestApp;

public sealed partial class MainWindow : Window
{
    private TrayIconService? _trayIcon;
    private bool             _splashStarted;
    private DispatcherTimer? _bgUpdateTimer;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    public MainWindow()
    {
        InitializeComponent();

        // Minimize to tray on the X button; only close for real from the tray "Exit" menu
        AppWindow.Closing += OnAppWindowClosing;

        // Create system tray icon
        var uiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _trayIcon = new TrayIconService(
            uiQueue,
            onOpen:         ShowFromTray,
            onCheckUpdates: OpenSettingsForUpdates,
            onExit: () =>
            {
                Close();
            });

        NavFrame.Navigated += NavFrame_OnNavigated;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        RootChrome.ActualThemeChanged += (_, _) => SyncCaptionButtonColors();
        RootChrome.Loaded += (_, _) => SyncCaptionButtonColors();
        Activated += MainWindow_Activated;

        // Start background update checker
        _bgUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _bgUpdateTimer.Tick += BgUpdateTimer_Tick;
        _bgUpdateTimer.Start();
    }

    private async void BgUpdateTimer_Tick(object? sender, object e)
    {
        var svcs = ((App)Application.Current).Svcs;
        var result = await svcs.UpdateChecker.CheckAsync();
        if (result?.IsUpdateAvailable == true && !GlobalUpdateNotification.IsOpen)
        {
            svcs.StartupUpdateResult = result;
            GlobalUpdateNotification.Message = $"Version {result.LatestVersion} is available.";
            GlobalUpdateNotification.IsOpen = true;
            try { MessageBeep(0x30); /* MB_ICONEXCLAMATION */ } catch { }
        }
    }

    private async void GlobalUpdateNotification_InstallClick(object sender, RoutedEventArgs e)
    {
        GlobalUpdateNotification.IsOpen = false;
        var svcs = ((App)Application.Current).Svcs;
        var result = svcs.StartupUpdateResult;
        if (result?.ExeDownloadUrl != null)
        {
            var batPath = await svcs.UpdateChecker.DownloadUpdateAsync(result.ExeDownloadUrl);
            if (batPath != null)
                UpdateService.ApplyUpdate(batPath);
        }
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated)
            SyncCaptionButtonColors();

        if (!_splashStarted && e.WindowActivationState != WindowActivationState.Deactivated)
        {
            _splashStarted = true;
            _ = RunStartupSequenceAsync();
        }
    }

    /// <summary>
    /// Drives the splash overlay through its intro, the background update check, and the outro.
    /// Calls <see cref="NavigateShell"/> behind the splash so the real UI is ready when the splash fades.
    /// </summary>
    private async Task RunStartupSequenceAsync()
    {
        var svcs = ((App)Application.Current).Svcs;

        try
        {
            await Splash.PlayIntroAsync();

            // Step 0 — Check for updates
            Splash.SetStep(SplashOverlay.SplashStep.CheckUpdates, SplashOverlay.StepState.Active);
            var updateTask = svcs.UpdateChecker.CheckAsync();
            // Min display time so a fast network doesn't make the step feel like a flicker.
            await Task.WhenAll(updateTask, Task.Delay(700));
            var result = await updateTask;
            if (result?.IsUpdateAvailable == true)
            {
                svcs.StartupUpdateResult = result;

                if (!string.IsNullOrEmpty(result.ExeDownloadUrl))
                {
                    Splash.SetStep(SplashOverlay.SplashStep.CheckUpdates, SplashOverlay.StepState.Active, $"Downloading update v{result.LatestVersion}...");
                    var batPath = await svcs.UpdateChecker.DownloadUpdateAsync(result.ExeDownloadUrl);
                    if (batPath != null)
                    {
                        Splash.SetStep(SplashOverlay.SplashStep.CheckUpdates, SplashOverlay.StepState.Done, "Installing update...");
                        await Task.Delay(500);
                        UpdateService.ApplyUpdate(batPath);
                        return; // App will restart
                    }
                }
            }

            Splash.SetStep(SplashOverlay.SplashStep.CheckUpdates, SplashOverlay.StepState.Done,
                result?.IsUpdateAvailable == true
                    ? $"Update available  ·  v{result.LatestVersion}"
                    : "Up to date");
            await Task.Delay(220);

            // Step 1 — Preparing workspace (set up the real shell behind the splash)
            Splash.SetStep(SplashOverlay.SplashStep.Workspace, SplashOverlay.StepState.Active);
            await Task.Delay(280);
            NavigateShell();
            Splash.SetStep(SplashOverlay.SplashStep.Workspace, SplashOverlay.StepState.Done);
            await Task.Delay(180);

            // Step 2 — Ready
            Splash.SetStep(SplashOverlay.SplashStep.Ready, SplashOverlay.StepState.Active);
            await Task.Delay(220);
            Splash.SetStep(SplashOverlay.SplashStep.Ready, SplashOverlay.StepState.Done);
            await Task.Delay(260);

            await Splash.PlayOutroAsync();
        }
        catch
        {
            // Never let a splash hiccup block the app — best-effort fallback to the main shell.
            try { NavigateShell(); } catch { /* ignore */ }
        }
        finally
        {
            SplashHost.Visibility = Visibility.Collapsed;
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        // Real exit: clean up tray and Discord
        _trayIcon?.Dispose();
        _trayIcon = null;
        try { ((App)Application.Current).Svcs.DiscordPresence.Dispose(); } catch { /* ignore */ }
    }

    private void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    private void OpenSettingsForUpdates()
    {
        ShowFromTray();
        if (NavFrame.CurrentSourcePageType != typeof(SettingsPage))
            NavFrame.Navigate(typeof(SettingsPage));
    }

    internal void NavigateToSettings()
    {
        if (NavFrame.CurrentSourcePageType != typeof(SettingsPage))
            NavFrame.Navigate(typeof(SettingsPage));
    }

    private void NavFrame_OnNavigated(object sender, NavigationEventArgs e)
    {
        if (e.Content is SettingsPage)
            ((App)Application.Current).Svcs.DiscordPresence.NotifySettingsPage();
    }

    /// <summary>Sets root <see cref="FrameworkElement.RequestedTheme"/> from JSON <c>PreferDarkUi</c>.</summary>
    internal void ApplyThemeFromSettings()
    {
        var preferDark =
            ((App)Application.Current).Svcs.SettingsStore.Load().PreferDarkUi;
        RootChrome.RequestedTheme = preferDark ? ElementTheme.Dark : ElementTheme.Default;
        SyncCaptionButtonColors();
    }

    /// <summary>
    /// System minimize / maximize / close glyphs: <see cref="AppWindowTitleBar"/> <c>Button*</c> colors only
    /// (does not set title text / <c>ForegroundColor</c>).
    /// </summary>
    private void SyncCaptionButtonColors()
    {
        var bar = AppWindow.TitleBar;
        if (RootChrome.ActualTheme != ElementTheme.Dark)
        {
            bar.ButtonBackgroundColor = null;
            bar.ButtonForegroundColor = null;
            bar.ButtonHoverBackgroundColor = null;
            bar.ButtonHoverForegroundColor = null;
            bar.ButtonPressedBackgroundColor = null;
            bar.ButtonPressedForegroundColor = null;
            bar.ButtonInactiveBackgroundColor = null;
            bar.ButtonInactiveForegroundColor = null;
            return;
        }

        var white = Color.FromArgb(255, 255, 255, 255);
        var transparent = Color.FromArgb(0, 255, 255, 255);
        var inactiveGlyph = Color.FromArgb(255, 184, 184, 184);
        bar.ButtonBackgroundColor = transparent;
        bar.ButtonForegroundColor = white;
        bar.ButtonInactiveBackgroundColor = transparent;
        bar.ButtonInactiveForegroundColor = inactiveGlyph;
        bar.ButtonHoverForegroundColor = white;
        bar.ButtonPressedForegroundColor = white;
        bar.ButtonHoverBackgroundColor = Color.FromArgb(255, 44, 44, 44);
        bar.ButtonPressedBackgroundColor = Color.FromArgb(255, 62, 62, 62);
    }

    internal void NavigateShell()
    {
        ApplyThemeFromSettings();

        var svc = ((App)Application.Current).Svcs;

        if (FirstLaunchGate.ShouldShowWelcomeWizard(svc))
        {
            NavView.Visibility = Visibility.Collapsed;
            OnboardingHost.Visibility = Visibility.Visible;
            AppTitleBar.IsPaneToggleButtonVisible = false;
            OnboardingFrame.Navigate(typeof(OnboardingPage), new OnboardingNavigationArgs(FinishWelcomeFlow));
            return;
        }

        OnboardingHost.Visibility = Visibility.Collapsed;
        NavFrame.Navigate(typeof(HomePage), new HomeNavigationArgs(HomeLandingMode.StoreSearch));
        SvcsDiscordReload();

        // Kick off background stats load so the pane footer populates without blocking startup
        _ = RefreshUserStatsAsync();
    }

    private void FinishWelcomeFlow()
    {
        OnboardingHost.Visibility = Visibility.Collapsed;
        NavView.Visibility = Visibility.Visible;
        AppTitleBar.IsPaneToggleButtonVisible = true;
        NavFrame.Navigate(typeof(HomePage), new HomeNavigationArgs(HomeLandingMode.StoreSearch));
        SvcsDiscordReload();
    }

    private void SvcsDiscordReload()
    {
        try
        {
            ((App)Application.Current).Svcs.DiscordPresence.Connect();
        }
        catch
        {
            /* optional */
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack)
            NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            if (NavFrame.CurrentSourcePageType != typeof(SettingsPage))
                NavFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            return;

        if (tag == "manifest-library")
        {
            NavFrame.Navigate(typeof(HomePage), new HomeNavigationArgs(HomeLandingMode.ManifestLibrary));
            return;
        }

        if (tag == "game-requests")
        {
            NavFrame.Navigate(typeof(GameRequestsPage));
            return;
        }

        if (tag == "home")
            NavFrame.Navigate(typeof(HomePage), new HomeNavigationArgs(HomeLandingMode.StoreSearch));
    }

    // ── Game Requests navigation ──────────────────────────────────────────────

    /// <summary>
    /// Navigates to the Game Requests page, optionally pre-selecting a game
    /// (called from the Home page install-failure flow).
    /// </summary>
    internal void NavigateToRequests(uint? appId = null, string? gameName = null)
    {
        // Highlight the Requests nav item
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is string tag && tag == "game-requests")
            {
                NavView.SelectedItem = item;
                break;
            }
        }
        NavFrame.Navigate(typeof(GameRequestsPage),
            new GameRequestsNavigationArgs(appId, gameName));
    }

    // ── User stats (pane footer) ──────────────────────────────────────────────

    /// <summary>
    /// Fetches /api/{key}/stats and populates the pane-footer user card.
    /// Safe to call at any time; silently no-ops when no key is configured.
    /// </summary>
    internal async Task RefreshUserStatsAsync()
    {
        var svcs = ((App)Application.Current).Svcs;

        if (!ManifestApp.Services.GameGenApiKeyStore.TryRetrieve(out var key) ||
            string.IsNullOrWhiteSpace(key))
        {
            UserStatsCard.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var stats = await svcs.GameGenApi
                .GetStatsAsync(key.Trim(), CancellationToken.None)
                .ConfigureAwait(true);

            if (!stats.Ok)
            {
                UserStatsCard.Visibility = Visibility.Collapsed;
                return;
            }

            // Prefer display name → raw Discord ID → generic fallback
            string nameText;
            if (!string.IsNullOrWhiteSpace(stats.DisplayName))
                nameText = stats.DisplayName;
            else if (!string.IsNullOrWhiteSpace(stats.DiscordId))
                nameText = $"Discord ID {stats.DiscordId}";
            else
                nameText = "Discord account";

            PaneUserName.Text    = nameText;
            PaneDiscordTag.Text  = !string.IsNullOrWhiteSpace(stats.DisplayName) ? "via Discord" : "";

            PanePlanText.Text = string.IsNullOrWhiteSpace(stats.Plan)
                ? ""
                : $"Plan: {stats.Plan}";

            if (stats.CreditsRemaining.HasValue)
            {
                PaneCreditsText.Text = stats.CreditsTotal.HasValue
                    ? $"Credits: {stats.CreditsRemaining}/{stats.CreditsTotal} today"
                    : $"Credits left: {stats.CreditsRemaining}";
            }
            else
            {
                PaneCreditsText.Text = "";
            }

            UserStatsCard.Visibility = Visibility.Visible;
        }
        catch
        {
            UserStatsCard.Visibility = Visibility.Collapsed;
        }
    }
}
