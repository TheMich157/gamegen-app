using DiscordRPC;
using ManifestApp.Core;

namespace ManifestApp.Services;

/// <summary>Discord Rich Presence (local IPC). Requires Discord desktop; IDs and copy live in constants below.</summary>
public sealed class DiscordPresenceService : IDisposable
{
    private const string RpcApplicationId = "1500464887275589695";

    private const string PresenceDetails = "GameGen App";

    private const string IdleState = "Idle";
    private const string SettingsState = "Settings";
    private const string SearchingPrefix = "Searching store:";
    private const string InstallingPrefix = "Installing —";
    private const string RemovingPrefix = "Removing —";
    private const string BrowsingPrefix = "Viewing —";

    /// <summary>Rich Presence artwork key registered under the Discord application (“gamegen”).</summary>
    private const string PresenceLargeImageKey = "gamegen";

    private const string PresenceLargeImageHoverText = "GameGen";

    private readonly SettingsStore _settingsStore;

    private DiscordRpcClient? _client;
    private DateTime? _sessionUtcStart;

    public DiscordPresenceService(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    private bool PresenceDisabled => _settingsStore.Load().DiscordRichPresenceDisabled;

    /// <summary>Creates the IPC client if enabled and <see cref="RpcApplicationId"/> is set.</summary>
    public void Connect()
    {
        if (PresenceDisabled)
        {
            DisposeClient();
            return;
        }

        DisposeClient();
        if (string.IsNullOrWhiteSpace(RpcApplicationId))
            return;

        try
        {
            _client = new DiscordRpcClient(RpcApplicationId.Trim());
            _client.Initialize();
            _sessionUtcStart = DateTime.UtcNow;
            SetPresence(PresenceDetails, IdleState);
        }
        catch
        {
            DisposeClient();
        }
    }

    public void NotifySettingsPage()
    {
        SetPresence(PresenceDetails, SettingsState);
    }

    public void NotifyHomeSource(int sourceComboIndex)
    {
        var state = sourceComboIndex switch
        {
            1 => "Installed Steam games",
            2 => "Manifest library",
            _ => "Steam Store search",
        };
        SetPresence(PresenceDetails, state);
    }

    public void NotifySearchingStore(string queryTruncated)
    {
        SetPresence(PresenceDetails, $"{SearchingPrefix} “{queryTruncated}”");
    }

    public void NotifyInstalling(string displayNameTruncated)
    {
        SetPresence(PresenceDetails, $"{InstallingPrefix} {displayNameTruncated}");
    }

    public void NotifyRemoving(string displayNameTruncated)
    {
        SetPresence(PresenceDetails, $"{RemovingPrefix} {displayNameTruncated}");
    }

    public void NotifyBrowsingGame(string displayNameTruncated)
    {
        SetPresence(PresenceDetails, $"{BrowsingPrefix} {displayNameTruncated}");
    }

    private void SetPresence(string details, string? state = null)
    {
        if (PresenceDisabled || _client is null)
            return;

        try
        {
            _client.SetPresence(new RichPresence
            {
                Details = Truncate(details, 120),
                State = string.IsNullOrEmpty(state) ? null : Truncate(state, 120),
                Timestamps = SessionTimestamps(),
                Assets = new Assets
                {
                    LargeImageKey = PresenceLargeImageKey,
                    LargeImageText = PresenceLargeImageHoverText,
                },
            });
        }
        catch
        {
            /* RPC best effort */
        }
    }

    private Timestamps SessionTimestamps()
    {
        if (_sessionUtcStart is { } start)
            return new Timestamps { Start = start };
        return Timestamps.Now;
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max)
            return s;
        return s[..(max - 1)] + "…";
    }

    private void DisposeClient()
    {
        _sessionUtcStart = null;

        try
        {
            _client?.ClearPresence();
        }
        catch
        {
            /* ignore */
        }

        try
        {
            _client?.Dispose();
        }
        catch
        {
            /* ignore */
        }

        _client = null;
    }

    public void Dispose() => DisposeClient();
}
