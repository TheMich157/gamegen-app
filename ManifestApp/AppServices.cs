using ManifestApp.Services;

namespace ManifestApp;

/// <summary>Process-wide services available to WinUI pages.</summary>
internal sealed class AppServices
{
    internal AppServices(HttpClient http)
    {
        Http = http;
        SettingsStore = new();
        PathsResolver = new(SettingsStore);
        SteamStoreSearch = new ManifestApp.Core.SteamStoreSearchClient(Http);
        SteamLibrary = new ManifestApp.Core.SteamLibraryService(SettingsStore);
        SteamStoreDetails = new ManifestApp.Core.SteamStoreAppDetailsClient(Http);
        ArtworkCache = new(Http);
        InstalledRecords = new();
        GameGenApi = new(Http, SettingsStore);
        ZipInstaller = new(InstalledRecords, PathsResolver);
        SteamToolsLocator = new(SettingsStore, PathsResolver);
        DiscordPresence = new DiscordPresenceService(SettingsStore);
        UpdateChecker = new UpdateService(Http);
    }

    internal HttpClient Http { get; }

    internal ManifestApp.Core.SettingsStore SettingsStore { get; }

    internal ManifestApp.Core.SteamPathsResolver PathsResolver { get; }

    internal ManifestApp.Core.SteamStoreSearchClient SteamStoreSearch { get; }

    internal ManifestApp.Core.SteamStoreAppDetailsClient SteamStoreDetails { get; }

    internal ManifestApp.Core.SteamLibraryService SteamLibrary { get; }

    internal ManifestApp.Core.SteamArtworkCache ArtworkCache { get; }

    internal ManifestApp.Core.InstalledManifestStore InstalledRecords { get; }

    internal ManifestApp.Core.GameGenApiClient GameGenApi { get; }

    internal ManifestApp.Core.ZipManifestInstaller ZipInstaller { get; }

    internal ManifestApp.Core.SteamToolsLocator SteamToolsLocator { get; }

    internal DiscordPresenceService DiscordPresence { get; }

    internal UpdateService UpdateChecker { get; }
}
