using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Navigation;
using ManifestApp.Pages;
using Windows.UI;

namespace ManifestApp;

public sealed partial class MainWindow : Window
{
    private TrayIconService? _trayIcon;
    private bool             _reallyClosing;

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
                _reallyClosing = true;
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
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated)
            SyncCaptionButtonColors();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (_reallyClosing)
        {
            // Real exit: clean up tray and Discord
            _trayIcon?.Dispose();
            _trayIcon = null;
            try { ((App)Application.Current).Svcs.DiscordPresence.Dispose(); } catch { /* ignore */ }
            return;
        }

        // Hide to tray instead of closing
        e.Cancel = true;
        sender.Hide();
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

        if (tag == "home")
            NavFrame.Navigate(typeof(HomePage), new HomeNavigationArgs(HomeLandingMode.StoreSearch));
    }
}
