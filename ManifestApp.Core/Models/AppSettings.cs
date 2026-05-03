namespace ManifestApp.Core.Models;

/// <summary>User-adjustable paths and optional SteamTools override.</summary>
public sealed class AppSettings
{
    /// <summary>Folder containing steam.exe (no trailing slash required).</summary>
    public string? SteamInstallPathOverride { get; set; }

    public string? StPluginFolderOverride { get; set; }

    public string? DepotCacheFolderOverride { get; set; }

    /// <summary>Explicit path to SteamTools.exe.</summary>
    public string? SteamToolsExeOverride { get; set; }

    /// <summary>Set after the first-run setup wizard completes.</summary>
    public bool OnboardingCompleted { get; set; }

    /// <summary>Optional override; default https://gamegen.lol (no trailing slash).</summary>
    public string? GameGenApiBaseUrl { get; set; }

    /// <summary>When false, follows Windows theme. When true, forces dark Fluent theme.</summary>
    public bool PreferDarkUi { get; set; }

    /// <summary>When true, Discord Rich Presence IPC is not used (default false keeps presence on for existing settings files).</summary>
    public bool DiscordRichPresenceDisabled { get; set; }
}
