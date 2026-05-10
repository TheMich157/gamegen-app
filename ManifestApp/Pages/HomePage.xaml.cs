using System.Collections.ObjectModel;
using System.Diagnostics;
using ManifestApp.Core;
using ManifestApp.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Dispatching;

namespace ManifestApp.Pages;

public sealed partial class HomePage : Page
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    private bool _suppressSourceComboChanged;

    private App TypedApp => (App)Application.Current;

    public HomePage()
    {
        InitializeComponent();
        Loaded += (_, _) => StatusPulse.Begin();
    }

    /// <summary>
    /// Offline / API-failure fallback list — used only when Steam's featured endpoint is unreachable
    /// so the landing grid is never blank.
    /// </summary>
    private static readonly (uint AppId, string Name)[] PopularSteamGames =
    {
        (730,     "Counter-Strike 2"),
        (570,     "Dota 2"),
        (271590,  "Grand Theft Auto V"),
        (1086940, "Baldur's Gate 3"),
        (1245620, "Elden Ring"),
        (1091500, "Cyberpunk 2077"),
        (252490,  "Rust"),
        (578080,  "PUBG: BATTLEGROUNDS"),
        (1888930, "The Last of Us Part I"),
        (2358720, "Black Myth: Wukong"),
        (292030,  "The Witcher 3: Wild Hunt"),
        (1174180, "Red Dead Redemption 2"),
    };

    /// <summary>Sync entry point — fires the async fetch without blocking navigation.</summary>
    private void LoadTrendingGames() => _ = LoadTrendingGamesAsync();

    /// <summary>
    /// Populates <see cref="GamesGrid"/> with Steam's actual current top-sellers.
    /// Falls back to <see cref="LoadPopularGames"/> on any error so the page is never empty.
    /// </summary>
    private async Task LoadTrendingGamesAsync()
    {
        DetailTitle.Text  = "Loading trending games…";
        DetailStatus.Text = "Fetching top sellers from Steam.";
        GamesGrid.ItemsSource = new ObservableCollection<GameRowVm>();

        try
        {
            using var resp = await TypedApp.Svcs.Http
                .GetAsync("https://store.steampowered.com/api/featuredcategories/?cc=us&l=en")
                .ConfigureAwait(true);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(true);
            using var doc = await System.Text.Json.JsonDocument
                .ParseAsync(stream).ConfigureAwait(true);

            var view = new ObservableCollection<GameRowVm>();
            if (doc.RootElement.TryGetProperty("top_sellers", out var top) &&
                top.TryGetProperty("items", out var items) &&
                items.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idEl)) continue;
                    if (!idEl.TryGetUInt32(out var appId) || appId == 0) continue;

                    var name = item.TryGetProperty("name", out var n)
                        ? (n.GetString() ?? $"Steam App {appId}")
                        : $"Steam App {appId}";

                    var vm = new GameRowVm(appId, name);
                    HydrateTrackedState(vm);
                    _ = FallbackCapsule184Async(vm).ConfigureAwait(false);
                    view.Add(vm);
                    if (view.Count >= 18) break;
                }
            }

            if (view.Count == 0)
            {
                LoadPopularGames();
                return;
            }

            GamesGrid.ItemsSource  = view;
            GamesGrid.SelectedItem = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            DetailTitle.Text  = "Trending on Steam";
            DetailStatus.Text = $"Top sellers right now — {view.Count:N0} titles. Use Search above to find any game.";
        }
        catch
        {
            LoadPopularGames();
        }
    }

    /// <summary>Static fallback population using the curated list above.</summary>
    private void LoadPopularGames()
    {
        var view = new ObservableCollection<GameRowVm>();
        foreach (var (appId, name) in PopularSteamGames)
        {
            var vm = new GameRowVm(appId, name);
            HydrateTrackedState(vm);
            _ = FallbackCapsule184Async(vm).ConfigureAwait(false);
            view.Add(vm);
        }

        GamesGrid.ItemsSource  = view;
        GamesGrid.SelectedItem = null;
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailTitle.Text  = "Popular on Steam";
        DetailStatus.Text = "Pick a title below or use Search to find any game on Steam.";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        UpdateSteamBanner();
        try
        {
            RunHomeNavigatedToCore(e);
        }
        finally
        {
            SyncDiscordPresenceFromHome();
        }
    }

    private void RunHomeNavigatedToCore(NavigationEventArgs e)
    {
        if (e.Parameter is HomeNavigationArgs { Mode: HomeLandingMode.ManifestLibrary })
        {
            _suppressSourceComboChanged = true;
            SourceCombo.SelectedIndex = 2;
            _suppressSourceComboChanged = false;
            ApplyTrackedLibrarySourceUiOnly();
            LoadTrackedLibrary();
            RefreshHomeIntroText();
            return;
        }

        if (e.Parameter is HomeNavigationArgs { Mode: HomeLandingMode.StoreSearch })
        {
            _suppressSourceComboChanged = true;
            SourceCombo.SelectedIndex = 0;
            _suppressSourceComboChanged = false;
            ApplyStoreSourceUiOnly();
            LoadPopularGames();
            RefreshHomeIntroText();
            return;
        }

        if (SourceCombo.SelectedIndex == 1)
        {
            ApplyInstalledSourceUiOnly();
            LoadInstalledGames();
            RefreshHomeIntroText();
            return;
        }

        if (SourceCombo.SelectedIndex == 2)
        {
            ApplyTrackedLibrarySourceUiOnly();
            LoadTrackedLibrary();
            RefreshHomeIntroText();
            return;
        }

        ApplyStoreSourceUiOnly();
        GamesGrid.ItemsSource = new ObservableCollection<GameRowVm>();
        GamesGrid.SelectedItem = null;
        DetailPanel.Visibility = Visibility.Collapsed;

        DetailTitle.Text = SelectGameTitleFallback();
        DetailStatus.Text = "Search the store, then choose a game below.";
        RefreshHomeIntroText();
    }

    private void SyncDiscordPresenceFromHome()
    {
        try
        {
            TypedApp.Svcs.DiscordPresence.NotifyHomeSource(SourceCombo.SelectedIndex);
        }
        catch
        {
            /* optional RPC only */
        }
    }

    private void UpdateSteamBanner()
    {
        var steamKnown = TypedApp.Svcs.PathsResolver.ResolveSteamInstall() != null;
        SteamPathBanner.IsOpen = !steamKnown;
        SteamPathBanner.Visibility = steamKnown ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string SelectGameTitleFallback() => "Select a result";

    private async void SearchStore_Click(object sender, RoutedEventArgs e)
    {
        if (SourceCombo.SelectedIndex != 0)
        {
            _suppressSourceComboChanged = true;
            SourceCombo.SelectedIndex = 0;
            _suppressSourceComboChanged = false;
            ApplyStoreSourceUiOnly();
        }

        // If the user already typed something, run that search. Otherwise drop the
        // caret into the search bar so they can start typing immediately.
        var existing = SearchBox.Text?.Trim() ?? string.Empty;
        if (existing.Length == 0)
        {
            SearchBox.Focus(FocusState.Programmatic);
            return;
        }

        await RunSteamStoreSearchAsync(existing);
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _ = args;
        await RunSteamStoreSearchAsync(sender.Text);
    }

    private async Task RunSteamStoreSearchAsync(string? rawQuery)
    {
        var query = rawQuery?.Trim() ?? "";
        if (SourceCombo.SelectedIndex != 0)
        {
            _suppressSourceComboChanged = true;
            SourceCombo.SelectedIndex = 0;
            _suppressSourceComboChanged = false;
            ApplyStoreSourceUiOnly();
        }

        DetailPanel.Visibility = Visibility.Visible;
        GamesGrid.SelectedItem = null;
        UpdateSteamBanner();

        if (query.Length == 0)
        {
            DetailTitle.Text = "Nothing to search";
            DetailStatus.Text = "Enter a Steam Store-style game name.";
            GamesGrid.ItemsSource = new ObservableCollection<GameRowVm>();
            SyncDiscordPresenceFromHome();
            return;
        }

        // ── Direct App ID entry: numeric query skips store search ───────────
        if (uint.TryParse(query, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var directId) && directId > 0)
        {
            var directVm = new GameRowVm(directId, $"Steam App {directId}");
            HydrateTrackedState(directVm);
            _ = FallbackCapsule184Async(directVm).ConfigureAwait(false);

            GamesGrid.ItemsSource = new ObservableCollection<GameRowVm> { directVm };
            GamesGrid.SelectedItem = directVm;

            DetailTitle.Text = $"App ID {directId}";
            DetailStatus.Text = "Direct App ID — install or remove manifests using the buttons below.";
            SyncDiscordPresenceFromHome();
            return;
        }

        DetailTitle.Text = $"Searching Steam Store… \"{query}\"";
        TypedApp.Svcs.DiscordPresence.NotifySearchingStore(TruncateForDiscordPresence(query));

        try
        {
            var hits = await TypedApp.Svcs.SteamStoreSearch.SearchAppsAsync(query).ConfigureAwait(true);

            if (hits.Count == 0)
            {
                DetailStatus.Text =
                    $"No results for \"{query}\". Try another name, DLC, or paste a numeric App ID.";
                GamesGrid.ItemsSource = new ObservableCollection<GameRowVm>();
                return;
            }

            var view = new ObservableCollection<GameRowVm>();
            foreach (var hit in hits)
            {
                var vm = new GameRowVm(hit.AppId, hit.Name);
                HydrateTrackedState(vm);
                ApplyCapsule(vm, hit);
                view.Add(vm);
            }

            GamesGrid.ItemsSource = view;
            _ = TypedApp.Svcs.AdminReporter.ReportSearchAsync(query);

            DetailTitle.Text = $"{hits.Count:N0} games found";
            DetailStatus.Text = "Select a game to see its App ID and install or remove manifests.";
        }
        catch (Exception ex)
        {
            DetailTitle.Text = "Store search failed";
            DetailStatus.Text =
                $"{ex.Message}{Environment.NewLine}Check your network/VPN rules — the query targets store.steampowered.com.";
        }
        finally
        {
            SyncDiscordPresenceFromHome();
        }
    }

    private static string TruncateForDiscordPresence(string s, int max = 96)
    {
        if (s.Length <= max)
            return s;
        return string.Concat(s.AsSpan(0, max - 1), "…");
    }

    private void HydrateTrackedState(GameRowVm rowVm)
    {
        var rec = TypedApp.Svcs.InstalledRecords.FindByAppId(rowVm.AppId);
        rowVm.IsConfigured = rec != null;
        if (rec != null)
            rowVm.AttachTrackingMetadata(rec);
        else
            rowVm.ClearTrackingMetadata();
    }

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSourceComboChanged || SourceCombo.SelectedItem is null)
            return;

        try
        {
            if (SourceCombo.SelectedIndex == 1)
            {
                ApplyInstalledSourceUiOnly();
                LoadInstalledGames();
                RefreshHomeIntroText();
                return;
            }

            if (SourceCombo.SelectedIndex == 2)
            {
                ApplyTrackedLibrarySourceUiOnly();
                LoadTrackedLibrary();
                RefreshHomeIntroText();
                return;
            }

            ApplyStoreSourceUiOnly();
            GamesGrid.ItemsSource = new ObservableCollection<GameRowVm>();
            GamesGrid.SelectedItem = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            DetailTitle.Text = SelectGameTitleFallback();
            DetailStatus.Text = "Search the store, then choose a game below.";
            RefreshHomeIntroText();
        }
        finally
        {
            SyncDiscordPresenceFromHome();
        }
    }

    private void RefreshHomeIntroText()
    {
        HomeIntroText.Text = SourceCombo.SelectedIndex == 2
            ? "Titles you installed through GameGen appear here (newest first). Open one to see files or remove."
            : "Use Search for the Steam catalog, or Installed games for appmanifest App IDs on this PC.";
    }

    private void ApplyStoreSourceUiOnly()
    {
        SearchBox.Visibility = Visibility.Visible;
        RefreshInstalledButton.Visibility = Visibility.Collapsed;
    }

    private void ApplyInstalledSourceUiOnly()
    {
        SearchBox.Visibility = Visibility.Collapsed;
        RefreshInstalledButton.Visibility = Visibility.Visible;
    }

    private void ApplyTrackedLibrarySourceUiOnly()
    {
        SearchBox.Visibility = Visibility.Collapsed;
        RefreshInstalledButton.Visibility = Visibility.Visible;
    }

    private void RefreshInstalled_Click(object sender, RoutedEventArgs e)
    {
        if (SourceCombo.SelectedIndex == 2)
            LoadTrackedLibrary();
        else
            LoadInstalledGames();
    }

    private void LoadInstalledGames()
    {
        DetailPanel.Visibility = Visibility.Visible;
        UpdateSteamBanner();

        var steamKnown = TypedApp.Svcs.PathsResolver.ResolveSteamInstall() != null;
        if (!steamKnown)
        {
            GamesGrid.ItemsSource = new ObservableCollection<GameRowVm>();
            DetailTitle.Text = "Steam not detected";
            DetailStatus.Text = "Add your Steam folder under Settings to list installed games.";
            return;
        }

        var games = TypedApp.Svcs.SteamLibrary.ListGames();
        if (games.Count == 0)
        {
            GamesGrid.ItemsSource = new ObservableCollection<GameRowVm>();
            DetailTitle.Text = "No installed games";
            DetailStatus.Text = "Install a game in Steam, then tap Refresh list.";
            return;
        }

        var view = new ObservableCollection<GameRowVm>();
        foreach (var g in games)
        {
            var vm = new GameRowVm(g.AppId, g.DisplayName);
            HydrateTrackedState(vm);
            _ = FallbackCapsule184Async(vm).ConfigureAwait(false);
            view.Add(vm);
        }

        GamesGrid.ItemsSource = view;
        GamesGrid.SelectedItem = null;
        DetailTitle.Text = $"{games.Count:N0} installed games";
        DetailStatus.Text = "Select a game to see its App ID and manage manifests.";
    }

    private void LoadTrackedLibrary()
    {
        DetailPanel.Visibility = Visibility.Visible;
        UpdateSteamBanner();

        var sorted = TypedApp.Svcs.InstalledRecords
            .Load()
            .Records.OrderByDescending(r => r.InstalledUtc)
            .ToList();

        if (sorted.Count == 0)
        {
            GamesGrid.ItemsSource = new ObservableCollection<GameRowVm>();
            GamesGrid.SelectedItem = null;
            DetailTitle.Text = "Library is empty";
            DetailStatus.Text = "Install manifests from Search or Installed games first — they will show up here.";
            return;
        }

        var nameByAppId = new Dictionary<uint, string>();
        foreach (var g in TypedApp.Svcs.SteamLibrary.ListGames())
            nameByAppId[g.AppId] = g.DisplayName;

        var view = new ObservableCollection<GameRowVm>();
        foreach (var r in sorted)
        {
            var display = nameByAppId.TryGetValue(r.AppId, out var n) && !string.IsNullOrWhiteSpace(n)
                ? n
                : $"Steam App ID {r.AppId}";

            var vm = new GameRowVm(r.AppId, display);
            vm.IsConfigured = true;
            vm.AttachTrackingMetadata(r);
            _ = FallbackCapsule184Async(vm).ConfigureAwait(false);
            view.Add(vm);
        }

        GamesGrid.ItemsSource = view;
        GamesGrid.SelectedItem = null;
        DetailTitle.Text = $"{sorted.Count:N0} tracked install(s)";
        DetailStatus.Text = "Select a game to see deployed files or remove its manifests.";
    }

    private void ApplyCapsule(GameRowVm rowVm, ManifestApp.Core.SteamStoreAppHit hit)
    {
        try
        {
            if (!string.IsNullOrEmpty(hit.TinyImageHttpsUrl))
            {
                rowVm.AttachRemoteHttpsThumbnail(hit.TinyImageHttpsUrl);
                return;
            }
        }
        catch
        {
            /* fall through to CDN */
        }

        _ = FallbackCapsule184Async(rowVm).ConfigureAwait(false);
    }

    private async Task FallbackCapsule184Async(GameRowVm rowVm)
    {
        var path = await TypedApp.Svcs.ArtworkCache
            .GetCapsule184PathAsync(rowVm.AppId, CancellationToken.None)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        var ok = _dispatcher.TryEnqueue(() => rowVm.AttachLocalThumbnail(path));
        _ = ok;
    }

    private GameRowVm? SelectedRow =>
        GamesGrid.SelectedItem as GameRowVm;

    private void GamesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshDetailPanel();

    private void RefreshDetailPanel()
    {
        var sel = SelectedRow;
        DetailPanel.Visibility = sel != null ? Visibility.Visible : Visibility.Collapsed;

        if (sel is null)
        {
            DetailTitle.Text = SelectGameTitleFallback();
            InstallButton.IsEnabled      = false;
            RemoveButton.IsEnabled       = false;
            LaunchSteamButton.Visibility = Visibility.Collapsed;
            SteamActionsPanel.Visibility = Visibility.Collapsed;
            HideInstallProgress();
            return;
        }

        DetailTitle.Text = sel.DisplayName;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"App ID {sel.AppId:N0}");

        if (!string.IsNullOrEmpty(sel.TrackingSubtitleLine))
            sb.AppendLine(sel.TrackingSubtitleLine);
        else if (sel.IsConfigured)
            sb.AppendLine("Tracked — path list unavailable for this record.");
        else
            sb.AppendLine("Not tracked — Install adds plugin/depot files from GameGen.");



        if (!string.IsNullOrEmpty(sel.ZipFingerprintPrefix))
        {
            sb.AppendLine();
            sb.AppendLine($"Package SHA-256 prefix: {sel.ZipFingerprintPrefix}");
        }

        DetailStatus.Text = sb.ToString().TrimEnd();

        InstallButton.IsEnabled    = !sel.IsConfigured;
        RemoveButton.IsEnabled     = sel.IsConfigured;
        LaunchSteamButton.Visibility = Visibility.Visible;
        SteamActionsPanel.Visibility = Visibility.Visible;
    }

    private async Task<bool> EnsureSteamToolsOrContinueAnywayAsync()
    {
        if (TypedApp.Svcs.SteamToolsLocator.TryFindSteamTools(out _))
            return true;

        var dlg = new ContentDialog
        {
            Title = "SteamTools not found",
            Content =
                "GameGen App couldn’t locate SteamTools.exe. Without it, plugin / depot flows often won't work." +
                Environment.NewLine + Environment.NewLine +
                "Continue anyway?",
            PrimaryButtonText = "Cancel",
            SecondaryButtonText = "Continue anyway",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot!,
        };

        var choice = await dlg.ShowAsync();
        return choice == ContentDialogResult.Secondary;
    }

    private async Task ShowInfoAsync(string title, string message, string close = "OK")
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = close,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot!,
        };
        await dlg.ShowAsync();
    }

    /// <returns>true if caller should proceed (Secondary), false when user aborts.</returns>
    private async Task<bool> ChooseContinueAnywayAsync(
        string title,
        string message,
        string stopText,
        string continueAnywayText)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = stopText,
            SecondaryButtonText = continueAnywayText,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot!,
        };

        var r = await dlg.ShowAsync();
        return r == ContentDialogResult.Secondary;
    }

    private async Task<bool> AskRestartSteamNowAsync(string dialogExplanation)
    {
        var dlg = new ContentDialog
        {
            Title = "Restart Steam?",
            Content = dialogExplanation,
            PrimaryButtonText = "Restart Steam now",
            CloseButtonText = "I'll restart later",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot!,
        };

        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task OfferSteamGracefulRestartAfterManifestMutationAsync(
        string restartDialogExplanation,
        string deferReminderSuffix)
    {
        if (!await AskRestartSteamNowAsync(restartDialogExplanation).ConfigureAwait(true))
        {
            DetailStatus.Text += deferReminderSuffix;
            return;
        }

        var steamRoot = TypedApp.Svcs.PathsResolver.ResolveSteamInstall();
        if (string.IsNullOrEmpty(steamRoot))
        {
            var manual =
                "The Steam folder is unknown — restart Steam yourself from Desktop or Start.";
            DetailStatus.Text = manual;
            await ShowInfoAsync("Restart Steam manually", manual).ConfigureAwait(true);
        }
        else
        {
            var outcome = await SteamClientRestart
                .TryGracefulRestartAsync(steamRoot, TimeSpan.FromSeconds(45), CancellationToken.None)
                .ConfigureAwait(true);

            DetailStatus.Text = outcome.Message;
            await ShowInfoAsync(
                outcome.Succeeded ? "Steam restarted" : "Steam restart incomplete",
                outcome.Message).ConfigureAwait(true);
        }
    }

    private bool IsStoreSearchSource() => SourceCombo.SelectedIndex == 0;

    /// <remarks><paramref name="couldVerify"/> is false when Steam root is unknown (skip local checks).</remarks>
    private bool TryEvaluateLocalSteamInstallPresence(uint appId, out bool couldVerify)
    {
        couldVerify = TypedApp.Svcs.PathsResolver.ResolveSteamInstall() is { Length: > 0 };

        return couldVerify &&
               TypedApp.Svcs.SteamLibrary.ListGames().Any(g => g.AppId == appId);
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedRow;
        if (row is null || row.IsConfigured)
            return;

        // Walk user through setup if API key or Steam paths are missing
        if (!await RunSetupGuideAsync())
            return;

        if (!await EnsureSteamToolsOrContinueAnywayAsync())
            return;

        InstallButton.IsEnabled = false;
        SetInstallProgressIndeterminate("Fetching from GameGen...");

        try
        {
            TypedApp.Svcs.DiscordPresence.NotifyInstalling(TruncateForDiscordPresence(row.DisplayName, 72));

            var listing = await TypedApp.Svcs.SteamStoreDetails
                .GetListingAsync(row.AppId, CancellationToken.None).ConfigureAwait(true);

            if (listing.Parsed && listing.ComingSoon)
            {
                var storefrontName = listing.NameOnStore ?? row.DisplayName;
                var when = string.IsNullOrWhiteSpace(listing.ReleaseDateCaption)
                    ? "Steam marks this title as Coming soon."
                    : listing.ReleaseDateCaption!.Trim();

                DetailStatus.Text = $"Coming soon on Steam storefront: \"{when}\".";
                var proceedSoon = await ChooseContinueAnywayAsync(
                    "Game not released yet",
                    $"\"{storefrontName}\" is not publicly released ({when}). GameGen frequently has nothing to generate until launch.\n\nYou can retry after release or dismiss with Stop.",
                    "Stop install",
                    "Try GameGen anyway");

                if (!proceedSoon)
                    return;
            }

            // Progress: spinner until ZIP starts streaming, then switch to progress bar
            var downloadProgress = new Progress<double>(pct =>
            {
                if (InstallProgressRing.Visibility == Visibility.Visible)
                {
                    InstallProgressRing.IsActive = false;
                    InstallProgressRing.Visibility = Visibility.Collapsed;
                    InstallProgressBar.Visibility = Visibility.Visible;
                }
                InstallProgressBar.Value = pct;
                InstallProgressText.Text = $"Downloading ZIP... {pct:0}%";
            });

            if (!GameGenApiKeyStore.TryRetrieve(out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
            {
                // RunSetupGuideAsync should have ensured a key exists, but guard defensively.
                DetailStatus.Text = "API key missing — open Settings to add your GameGen key.";
                return;
            }
            var zipResult = await TypedApp.Svcs.GameGenApi
                .DownloadGenerateZipAsync(apiKey.Trim(), row.AppId, CancellationToken.None, downloadProgress);

            HideInstallProgress();

            if (!zipResult.Ok || zipResult.ZipBytes is null)
            {
                var err = zipResult.ErrorMessage ??
                          "GameGen response could not be turned into manifest artifacts.";
                DetailStatus.Text = err;

                _ = TypedApp.Svcs.AdminReporter.ReportInstallAsync(row.AppId, row.DisplayName, false);

                // Check quota exhaustion first — show a specific daily-limit warning
                // instead of the generic "request this game" dialog.
                var (isQuotaExhausted, cachedStats) = await CheckQuotaAsync(err);
                if (isQuotaExhausted)
                    await ShowOutOfGenerationsDialogAsync(cachedStats);
                else
                    await OfferGameRequestAsync(row, err);

                return;
            }

            try
            {
                await TypedApp.Svcs.ZipInstaller
                    .InstallForAppAsync(row.AppId, zipResult.ZipBytes, CancellationToken.None);
            }
            catch (InvalidOperationException ex)
            {
                DetailStatus.Text = ex.Message;
                await ShowInfoAsync("Install blocked", ex.Message + "\n\nCheck Steam paths in Settings or ZIP contents (.lua/.manifest).");
                return;
            }

            row.IsConfigured = true;
            HydrateTrackedState(row);
            DetailStatus.Text = $"Install finished for App ID {row.AppId}.";
            _ = TypedApp.Svcs.AdminReporter.ReportInstallAsync(row.AppId, row.DisplayName, true);

            await OfferSteamGracefulRestartAfterManifestMutationAsync(
                "Manifest files were installed. Restart the Steam client now so it reloads stplug-in and depot cache changes.",
                " Restart Steam yourself when convenient so depot and plugin layouts reload.").ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            DetailStatus.Text = "Install cancelled.";
        }
        catch (Exception ex)
        {
            DetailStatus.Text = $"Install failed: {ex.Message}";
            await ShowInfoAsync(
                "Install failed",
                ex.Message + "\n\nCheck Steam paths, antivirus blocking HTTP, and GameGen service status.",
                "Close");
        }
        finally
        {
            HideInstallProgress();
            RefreshDetailPanel();
            SyncDiscordPresenceFromHome();
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedRow;
        if (row is null || !row.IsConfigured)
            return;

        try
        {
            TypedApp.Svcs.DiscordPresence.NotifyRemoving(TruncateForDiscordPresence(row.DisplayName, 72));

            TypedApp.Svcs.ZipInstaller.RemoveForApp(row.AppId);
            _ = TypedApp.Svcs.AdminReporter.ReportRemoveAsync(row.AppId, row.DisplayName);

            if (SourceCombo.SelectedIndex == 2)
            {
                LoadTrackedLibrary();
            }
            else
            {
                HydrateTrackedState(row);
                RefreshDetailPanel();
            }

            const string removalNote = "Removed tracked files (best effort).";
            DetailStatus.Text = $"{removalNote}{Environment.NewLine}{Environment.NewLine}{DetailStatus.Text.Trim()}";

            await OfferSteamGracefulRestartAfterManifestMutationAsync(
                "Tracked manifest files were removed. Restart Steam now so it reloads cleared stplug-in and depotcache changes.",
                " Restart Steam yourself when convenient so layouts match what is on disk.").ConfigureAwait(true);
        }
        finally
        {
            SyncDiscordPresenceFromHome();
        }
    }

    private void OpenPluginFolder_Click(object sender, RoutedEventArgs e)
    {
        TryOpenExplorer(TypedApp.Svcs.PathsResolver.ResolveStPluginFolder(), "Plugin path unresolved — configure Settings.");
    }

    private void OpenDepotFolder_Click(object sender, RoutedEventArgs e)
    {
        TryOpenExplorer(TypedApp.Svcs.PathsResolver.ResolveDepotCacheFolder(),
            "Depot cache path unresolved — configure Settings.");
    }

    private void TryOpenExplorer(string? path, string fallbackMessage)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            DetailStatus.Text = fallbackMessage;
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + Path.GetFullPath(path) + "\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DetailStatus.Text = $"Couldn't launch Explorer: {ex.Message}";
        }
    }

    // ── Launch in Steam ───────────────────────────────────────────────────────

    private void LaunchSteam_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedRow;
        if (sel is null) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = $"steam://run/{sel.AppId}",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DetailStatus.Text = $"Couldn't launch Steam: {ex.Message}";
        }
    }

    /// <summary>Asks Steam to install the selected app (steam://install/&lt;appid&gt;).</summary>
    private void SteamInstall_Click(object sender, RoutedEventArgs e)
        => OpenSteamProtocol("install", "Steam install");

    /// <summary>Asks Steam to uninstall the selected app (steam://uninstall/&lt;appid&gt;).</summary>
    private void SteamUninstall_Click(object sender, RoutedEventArgs e)
        => OpenSteamProtocol("uninstall", "Steam uninstall");

    private void OpenSteamProtocol(string verb, string label)
    {
        var sel = SelectedRow;
        if (sel is null) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = $"steam://{verb}/{sel.AppId}",
                UseShellExecute = true,
            });
            DetailStatus.Text = $"Asked Steam to {verb} App ID {sel.AppId}. Confirm in the Steam window.";
        }
        catch (Exception ex)
        {
            DetailStatus.Text = $"Couldn't trigger {label}: {ex.Message}";
        }
    }


    // ── Daily generation limit detection & dialog ────────────────────────────

    /// <summary>
    /// Checks whether a generation failure is due to quota exhaustion.
    /// Returns the quota verdict alongside any live <see cref="GameGenStatsResult"/> fetched,
    /// so the caller can reuse it without a second API round-trip.
    /// </summary>
    private async Task<(bool IsExhausted, GameGenStatsResult? Stats)> CheckQuotaAsync(string errorMessage)
    {
        // Fast path: recognisable keywords in the error text
        var msg = errorMessage.ToLowerInvariant();
        bool keywordHit =
            msg.Contains("limit")    || msg.Contains("credit")  ||
            msg.Contains("quota")    || msg.Contains("daily")   ||
            msg.Contains("exceeded") || msg.Contains("ran out") ||
            msg.Contains("no more");   // removed "rate" — too broad

        // Fetch live stats either way (needed for the dialog's plan/total display).
        GameGenStatsResult? stats = null;
        if (GameGenApiKeyStore.TryRetrieve(out var apiKey) &&
            !string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                stats = await TypedApp.Svcs.GameGenApi
                    .GetStatsAsync(apiKey.Trim(), CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch { /* best-effort */ }
        }

        // Stats win over keyword heuristic when available
        if (stats?.Ok == true && stats.CreditsRemaining.HasValue)
            return (stats.CreditsRemaining.Value <= 0, stats);

        return (keywordHit, stats);
    }

    /// <summary>
    /// Formats the quota reset timestamp into a human-readable label.
    /// Accepts an ISO-8601 string from the API; falls back to a generic message.
    /// </summary>
    private static string ResetAtLabel(string? resetAt)
    {
        if (!string.IsNullOrWhiteSpace(resetAt) &&
            DateTimeOffset.TryParse(resetAt,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var dt))
        {
            var local = dt.ToLocalTime();
            var diff  = local - DateTimeOffset.Now;
            if (diff.TotalMinutes < 1)
                return "Quota resets in less than a minute";
            if (diff.TotalHours < 1)
                return $"Quota resets in {(int)diff.TotalMinutes} min";
            return $"Quota resets at {local:h:mm tt}";
        }
        return "Quota resets every 24 hours";
    }

    /// <summary>
    /// Shows a targeted "daily limit reached" dialog that includes the user's
    /// plan name and total daily allowance. Accepts pre-fetched stats so no
    /// extra API call is needed.
    /// </summary>
    private async Task ShowOutOfGenerationsDialogAsync(GameGenStatsResult? stats)
    {
        string? plan  = stats?.Ok == true ? stats.Plan : null;
        int?    total = stats?.Ok == true ? stats.CreditsTotal : null;

        var planLabel = string.IsNullOrWhiteSpace(plan)
            ? "Your plan"
            : $"{char.ToUpperInvariant(plan[0])}{plan[1..]} plan";

        var limitLine = total.HasValue
            ? $"{planLabel} includes {total} generation{(total == 1 ? "" : "s")} per day."
            : $"{planLabel} has a daily generation limit.";

        var dlg = new ContentDialog
        {
            Title         = "Daily generation limit reached",
            XamlRoot      = XamlRoot!,
            CloseButtonText   = "OK",
            DefaultButton = ContentDialogButton.Close,
            Content       = new StackPanel
            {
                Spacing  = 12,
                MaxWidth = 420,
                Children =
                {
                    new TextBlock
                    {
                        Text         = $"You've used all your available GameGen generations for today. {limitLine}",
                        FontSize     = 13,
                        TextWrapping = TextWrapping.WrapWholeWords,
                    },
                    new InfoBar
                    {
                        IsOpen     = true,
                        IsClosable = false,
                        Severity   = InfoBarSeverity.Warning,
                        Title      = ResetAtLabel(stats?.ResetAt),
                        Message    = "Upgrade your plan at gamegen.lol for a higher daily limit.",
                    },
                },
            },
        };

        // Refresh the pane-footer credit count after showing the dialog
        await dlg.ShowAsync();
        if (TypedApp.MainShell is MainWindow mw)
            await mw.RefreshUserStatsAsync().ConfigureAwait(true);
    }

    // ── Game-not-found request flow ───────────────────────────────────────────

    /// <summary>
    /// Called when GameGen fails to produce a ZIP for a game.
    /// Shows a dialog explaining the error and offering to jump to the Requests tab
    /// so the user can formally request the title be added to the registry.
    /// </summary>
    private async Task OfferGameRequestAsync(GameRowVm row, string errorMessage)
    {
        var dlg = new ContentDialog
        {
            Title   = "GameGen couldn't generate this title",
            Content = new StackPanel
            {
                Spacing  = 12,
                MaxWidth = 420,
                Children =
                {
                    new TextBlock
                    {
                        Text         = errorMessage,
                        FontSize     = 13,
                        TextWrapping = TextWrapping.WrapWholeWords,
                        Foreground   = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    },
                    new InfoBar
                    {
                        IsOpen      = true,
                        IsClosable  = false,
                        Severity    = InfoBarSeverity.Informational,
                        Title       = "Game not in registry?",
                        Message     = "If this title isn't in the GameGen registry yet, you can formally " +
                                      "request it via the Requests tab. Your request goes straight to " +
                                      "our fulfillment pipeline.",
                    },
                },
            },
            PrimaryButtonText   = "Go to Requests tab",
            CloseButtonText     = "Close",
            DefaultButton       = ContentDialogButton.Primary,
            XamlRoot            = XamlRoot!,
        };

        var choice = await dlg.ShowAsync();
        if (choice == ContentDialogResult.Primary &&
            TypedApp.MainShell is MainWindow mw)
        {
            mw.NavigateToRequests(row.AppId, row.DisplayName);
        }
    }

    // ── Setup guide wizard ────────────────────────────────────────────────────

    /// <summary>
    /// Shows a step-by-step setup guide if the API key or Steam path is not yet configured.
    /// Returns true when the user is ready to proceed with the install, false when they cancel.
    /// </summary>
    private async Task<bool> RunSetupGuideAsync()
    {
        var hasApiKey = GameGenApiKeyStore.TryRetrieve(out _);
        var hasSteam  = TypedApp.Svcs.PathsResolver.ResolveSteamInstall() is { Length: > 0 };

        if (hasApiKey && hasSteam) return true;   // all good — skip the guide

        // Build ordered step list (always starts with Welcome, ends with Ready)
        var steps = new List<string> { "welcome" };
        if (!hasApiKey) steps.Add("apikey");
        if (!hasSteam)  steps.Add("steam");
        steps.Add("ready");

        int  cur  = 0;
        bool done = false;

        // Shared control references filled lazily inside the builders below
        PasswordBox? apiKeyBox  = null;
        TextBlock?   apiKeyHint = null;
        TextBlock?   steamHint  = null;

        // ── Step content builders ─────────────────────────────────────────────

        UIElement BuildWelcome()
        {
            var sp = new StackPanel { Spacing = 12, MaxWidth = 400 };

            sp.Children.Add(new FontIcon
            {
                Glyph = "",
                FontSize = 36,
                HorizontalAlignment = HorizontalAlignment.Left,
            });

            sp.Children.Add(new TextBlock
            {
                Text = "Let's get GameGen set up. Here's what we'll configure:",
                FontSize = 14,
                TextWrapping = TextWrapping.WrapWholeWords,
            });

            var bullets = new StackPanel { Spacing = 6, Margin = new Thickness(8, 0, 0, 0) };
            if (!hasApiKey)
                bullets.Children.Add(new TextBlock
                {
                    Text = "① Your GameGen API key — authenticates the manifest generation call",
                    FontSize = 13,
                    TextWrapping = TextWrapping.WrapWholeWords,
                });
            if (!hasSteam)
                bullets.Children.Add(new TextBlock
                {
                    Text = "② Your Steam install path — so files deploy to the right folders",
                    FontSize = 13,
                    TextWrapping = TextWrapping.WrapWholeWords,
                });
            sp.Children.Add(bullets);

            sp.Children.Add(new TextBlock
            {
                Text = "Click Next to walk through each step.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.WrapWholeWords,
            });
            return sp;
        }

        UIElement BuildApiKey()
        {
            var sp = new StackPanel { Spacing = 10, MaxWidth = 400 };

            sp.Children.Add(new TextBlock
            {
                Text = "Your GameGen API key is the {YOUR_KEY} segment in the generate URL:",
                FontSize = 13,
                TextWrapping = TextWrapping.WrapWholeWords,
            });

            sp.Children.Add(new TextBlock
            {
                Text = "https://gamegen.lol/api/{YOUR_KEY}/generate/{appId}",
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                TextWrapping = TextWrapping.Wrap,
            });

            apiKeyBox = new PasswordBox
            {
                PlaceholderText = "Paste your API key here…",
                Margin = new Thickness(0, 4, 0, 0),
            };
            sp.Children.Add(apiKeyBox);

            apiKeyHint = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.OrangeRed),
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.WrapWholeWords,
            };
            sp.Children.Add(apiKeyHint);

            sp.Children.Add(new TextBlock
            {
                Text = "You can find or regenerate your key at gamegen.lol.",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            return sp;
        }

        UIElement BuildSteam()
        {
            var sp = new StackPanel { Spacing = 10, MaxWidth = 400 };

            sp.Children.Add(new TextBlock
            {
                Text = "GameGen deploys manifest files into Steam’s stplug-in and depotcache folders. We need your Steam install path to find them.",
                FontSize = 13,
                TextWrapping = TextWrapping.WrapWholeWords,
            });

            steamHint = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.WrapWholeWords,
            };
            var detected = TypedApp.Svcs.PathsResolver.ResolveSteamInstall();
            if (detected is { Length: > 0 })
            {
                steamHint.Text       = $"✓  Steam detected at: {detected}";
                steamHint.Foreground = new SolidColorBrush(Colors.LightGreen);
            }
            else
            {
                steamHint.Text       = "Steam path not detected yet. Click Auto-detect, or open Settings to set it manually.";
                steamHint.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            }

            var autoBtn = new Button
            {
                Content = "Auto-detect Steam path",
                Padding = new Thickness(14, 8, 14, 8),
            };
            autoBtn.Click += (_, _) =>
            {
                var found = TypedApp.Svcs.PathsResolver.ResolveSteamInstall();
                if (found is { Length: > 0 })
                {
                    var s = TypedApp.Svcs.SettingsStore.Load();
                    s.SteamInstallPathOverride = found;
                    TypedApp.Svcs.SettingsStore.Save(s);
                    steamHint!.Text      = $"✓  Saved: {found}";
                    steamHint.Foreground = new SolidColorBrush(Colors.LightGreen);
                }
                else
                {
                    steamHint!.Text      = "⚠  Couldn't auto-detect. Open Settings → Steam paths to set it manually.";
                    steamHint.Foreground = new SolidColorBrush(Colors.OrangeRed);
                }
            };
            sp.Children.Add(autoBtn);
            sp.Children.Add(steamHint);

            var settingsLink = new HyperlinkButton
            {
                Content = "Open Settings to configure paths manually →",
                Padding = new Thickness(0, 2, 0, 0),
            };
            settingsLink.Click += (_, _) =>
            {
                if (TypedApp.MainShell is MainWindow mw)
                    mw.NavigateToSettings();
            };
            sp.Children.Add(settingsLink);
            return sp;
        }

        UIElement BuildReady()
        {
            var sp = new StackPanel { Spacing = 10, MaxWidth = 400 };

            sp.Children.Add(new FontIcon
            {
                Glyph      = "",
                FontSize   = 36,
                Foreground = new SolidColorBrush(Colors.LightGreen),
                HorizontalAlignment = HorizontalAlignment.Left,
            });

            sp.Children.Add(new TextBlock
            {
                Text = "You’re all set! Click Generate to fetch the manifest from GameGen and deploy it to your Steam folders.",
                FontSize = 14,
                TextWrapping = TextWrapping.WrapWholeWords,
            });

            var summary = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            if (GameGenApiKeyStore.TryRetrieve(out _))
                summary.Children.Add(new TextBlock
                {
                    Text       = "✓  GameGen API key configured",
                    FontSize   = 13,
                    Foreground = new SolidColorBrush(Colors.LightGreen),
                });
            var steam = TypedApp.Svcs.PathsResolver.ResolveSteamInstall();
            if (steam is { Length: > 0 })
                summary.Children.Add(new TextBlock
                {
                    Text         = $"✓  Steam: {steam}",
                    FontSize     = 13,
                    Foreground   = new SolidColorBrush(Colors.LightGreen),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            sp.Children.Add(summary);
            return sp;
        }

        // ── Step dispatch helpers ─────────────────────────────────────────────

        UIElement BuildContent(int idx) => steps[idx] switch
        {
            "welcome" => BuildWelcome(),
            "apikey"  => BuildApiKey(),
            "steam"   => BuildSteam(),
            "ready"   => BuildReady(),
            _         => new StackPanel(),
        };

        string StepTitle(int idx) => steps[idx] switch
        {
            "welcome" => $"Get started  ·  Step 1 of {steps.Count}",
            "apikey"  => $"GameGen API Key  ·  Step {idx + 1} of {steps.Count}",
            "steam"   => $"Steam Path  ·  Step {idx + 1} of {steps.Count}",
            "ready"   => "All set!",
            _         => "Setup",
        };

        string PrimaryLabel(int idx) => steps[idx] == "ready" ? "Generate!" : "Next  →";

        bool ValidateCurrent()
        {
            if (steps[cur] != "apikey") return true;

            var key = apiKeyBox?.Password?.Trim() ?? "";
            if (!string.IsNullOrEmpty(key))
            {
                GameGenApiKeyStore.Replace(key);
                if (apiKeyBox != null) apiKeyBox.Password = "";
                return true;
            }
            if (apiKeyHint != null)
            {
                apiKeyHint.Text       = "Please paste your API key before continuing.";
                apiKeyHint.Visibility = Visibility.Visible;
            }
            return false;
        }

        // ── Dialog shell ──────────────────────────────────────────────────────

        var dlg = new ContentDialog
        {
            XamlRoot            = XamlRoot!,
            DefaultButton       = ContentDialogButton.Primary,
            PrimaryButtonText   = PrimaryLabel(cur),
            SecondaryButtonText = "",
            CloseButtonText     = "Cancel",
            Title               = StepTitle(cur),
            Content             = BuildContent(cur),
        };

        void RefreshDialog()
        {
            dlg.Title               = StepTitle(cur);
            dlg.Content             = BuildContent(cur);
            dlg.PrimaryButtonText   = PrimaryLabel(cur);
            dlg.SecondaryButtonText = cur > 0 ? "←  Back" : "";
        }

        dlg.PrimaryButtonClick += (_, e) =>
        {
            if (!ValidateCurrent()) { e.Cancel = true; return; }
            cur++;
            if (cur >= steps.Count)
            {
                done = true;   // let the dialog close naturally
            }
            else
            {
                e.Cancel = true;   // stay open; navigate to next step
                RefreshDialog();
            }
        };

        dlg.SecondaryButtonClick += (_, e) =>
        {
            if (cur <= 0) return;
            e.Cancel = true;
            cur--;
            RefreshDialog();
        };

        await dlg.ShowAsync();
        return done;
    }

    // ── Install progress helpers ──────────────────────────────────────────────

    private void SetInstallProgressIndeterminate(string text)
    {
        InstallProgressPanel.Visibility = Visibility.Visible;
        InstallProgressRing.IsActive    = true;
        InstallProgressRing.Visibility  = Visibility.Visible;
        InstallProgressBar.Visibility   = Visibility.Collapsed;
        InstallProgressText.Text        = text;
    }

    private void HideInstallProgress()
    {
        InstallProgressPanel.Visibility = Visibility.Collapsed;
        InstallProgressRing.IsActive    = false;
        InstallProgressRing.Visibility  = Visibility.Collapsed;
        InstallProgressBar.Visibility   = Visibility.Collapsed;
        InstallProgressBar.Value        = 0;
        InstallProgressText.Text        = string.Empty;
    }

    // ── Keyboard accelerators ─────────────────────────────────────────────────

    private void FocusSearch_Accelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (SearchBox.Visibility == Visibility.Visible)
        {
            SearchBox.Focus(FocusState.Keyboard);
            args.Handled = true;
        }
    }

    private void Refresh_Accelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (RefreshInstalledButton.Visibility == Visibility.Visible)
        {
            if (SourceCombo.SelectedIndex == 2)
                LoadTrackedLibrary();
            else
                LoadInstalledGames();
            args.Handled = true;
        }
    }
}
