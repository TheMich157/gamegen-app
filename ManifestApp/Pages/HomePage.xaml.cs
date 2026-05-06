using System.Collections.ObjectModel;
using System.Diagnostics;
using ManifestApp.Core;
using ManifestApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

        if (sel.TrackedDeployedPaths is { Count: > 0 } paths)
        {
            sb.AppendLine();
            sb.AppendLine("Deployed files");
            const int maxLines = 25;
            var n = Math.Min(paths.Count, maxLines);
            for (var i = 0; i < n; i++)
                sb.AppendLine(" • " + paths[i]);

            if (paths.Count > maxLines)
                sb.AppendLine($"… and {paths.Count - maxLines} more.");
        }

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

        if (!await EnsureSteamToolsOrContinueAnywayAsync())
            return;

        if (!GameGenApiKeyStore.TryRetrieve(out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            DetailPanel.Visibility = Visibility.Visible;
            DetailStatus.Text =
                "Paste and save your GameGen API key (Settings → GameGen API). It is sent in the HTTPS path.";
            await ShowInfoAsync(
                "API key missing",
                "Open Settings → GameGen API (Save endpoint & API key).\nYour key becomes the \"{yourKey}\" segment in:\n\nhttps://gamegen.lol/api/{yourKey}/generate/{steamAppId}");
            return;
        }

        if (IsStoreSearchSource())
        {
            var installed = TryEvaluateLocalSteamInstallPresence(row.AppId, out var couldVerify);
            if (!couldVerify)
            {
                DetailPanel.Visibility = Visibility.Visible;
                DetailStatus.Text =
                    "Steam paths aren’t set or detected — we can’t check whether this App ID is installed locally. Configure Settings.";
            }
            else if (!installed)
            {
                DetailStatus.Text =
                    $"App ID {row.AppId} is not listed in your local Steam manifests — install/update it in Steam first.";
                var proceedLocal = await ChooseContinueAnywayAsync(
                    "Game not installed locally",
                    $"\"{row.DisplayName}\" (App ID {row.AppId}) is not in your enumerated Steam libraries (no appmanifest match).\n\nDownload or repair the game through Steam unless you knowingly continue.",
                    "Stop",
                    "Continue anyway");

                if (!proceedLocal)
                    return;
            }
        }

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

            var zipResult = await TypedApp.Svcs.GameGenApi
                .DownloadGenerateZipAsync(apiKey.Trim(), row.AppId, CancellationToken.None, downloadProgress);

            HideInstallProgress();

            if (!zipResult.Ok || zipResult.ZipBytes is null)
            {
                var err = zipResult.ErrorMessage ??
                          "GameGen response could not be turned into manifest artifacts.";
                DetailStatus.Text = err;
                await ShowInfoAsync(
                    "GameGen couldn’t fetch the ZIP",
                    err + "\n\nConfirm your key, Steam App ID, and that GameGen has data for this title.");
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

            // Offer to run SteamTools if found
            if (TypedApp.Svcs.SteamToolsLocator.TryFindSteamTools(out var toolsPath))
            {
                var runTools = await AskRunSteamToolsAsync().ConfigureAwait(true);
                if (runTools)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = toolsPath,
                            UseShellExecute = true,
                        });
                    }
                    catch (Exception ex)
                    {
                        DetailStatus.Text += $"\nCould not launch SteamTools: {ex.Message}";
                    }
                }
            }

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

    // ── SteamTools dialog ─────────────────────────────────────────────────────

    private async Task<bool> AskRunSteamToolsAsync()
    {
        var dlg = new ContentDialog
        {
            Title              = "Run SteamTools?",
            Content            = "SteamTools was found. Run it now to activate the installed manifest?",
            PrimaryButtonText  = "Run SteamTools",
            CloseButtonText    = "Skip",
            DefaultButton      = ContentDialogButton.Primary,
            XamlRoot           = XamlRoot!,
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
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
