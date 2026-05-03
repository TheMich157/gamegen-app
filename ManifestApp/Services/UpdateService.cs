using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManifestApp.Services;

/// <summary>
/// Checks the GitHub Releases API and compares the latest release tag
/// against the running assembly version.
/// </summary>
internal sealed class UpdateService
{
    // ── TODO: set your GitHub repository before shipping ──────────────────────
    // Format: "owner/repo"  e.g. "octocat/my-app"
    private const string GitHubRepo = "TheMich157/gamegen-app";
    // ──────────────────────────────────────────────────────────────────────────

    private readonly HttpClient _http;

    internal UpdateService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>Current running version read from the assembly (set by &lt;Version&gt; in the .csproj).</summary>
    internal static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);
        }
    }

    /// <summary>Current version as a display string, e.g. "1.0.0".</summary>
    internal static string CurrentVersionString => CurrentVersion.ToString(3);

    /// <summary>
    /// Fetches the latest GitHub release and returns update info.
    /// Returns <c>null</c> when the repo placeholder hasn't been set,
    /// or on any network / parse error (fail-silent).
    /// </summary>
    internal async Task<UpdateResult?> CheckAsync(CancellationToken ct = default)
    {
        if (GitHubRepo == "OWNER/REPO")
            return null; // placeholder not yet configured

        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{GitHubRepo}/releases/latest");

            req.Headers.Add("Accept", "application/vnd.github+json");
            req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            var release = JsonSerializer.Deserialize(json, UpdateJsonContext.Default.GitHubRelease);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                return null;

            // Strip leading 'v' — common convention: "v1.2.3" → "1.2.3"
            var raw = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(raw, out var latest))
                return null;

            var current = CurrentVersion;
            return new UpdateResult(
                CurrentVersion: current,
                LatestVersion: latest,
                IsUpdateAvailable: latest > current,
                ReleaseUrl: release.HtmlUrl ?? $"https://github.com/{GitHubRepo}/releases/latest",
                ReleaseNotes: release.Body);
        }
        catch
        {
            return null; // network down, rate-limited, etc. — fail silently
        }
    }
}

internal sealed record UpdateResult(
    Version CurrentVersion,
    Version LatestVersion,
    bool IsUpdateAvailable,
    string ReleaseUrl,
    string? ReleaseNotes);

// ── Minimal AOT-friendly JSON deserialization ──────────────────────────────────

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("body")]     public string? Body    { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext { }
