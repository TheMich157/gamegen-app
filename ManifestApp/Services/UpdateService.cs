using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManifestApp.Services;

/// <summary>
/// Checks GitHub Releases for a newer version and can download + apply the update.
/// </summary>
internal sealed class UpdateService
{
    private const string GitHubRepo   = "TheMich157/gamegen-app";
    // Asset name that must be attached to every GitHub Release
    private const string ExeAssetName = "ManifestApp.exe";

    private readonly HttpClient _http;

    internal UpdateService(HttpClient http) => _http = http;

    // ── Version helpers ───────────────────────────────────────────────────────

    internal static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);
        }
    }

    internal static string CurrentVersionString => CurrentVersion.ToString(3);

    // ── Check for update ──────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the latest GitHub release. Returns <c>null</c> on any error (fail-silent).
    /// </summary>
    internal async Task<UpdateResult?> CheckAsync(CancellationToken ct = default)
    {
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

            var raw = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(raw, out var latest))
                return null;

            var exeAsset = release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, ExeAssetName, StringComparison.OrdinalIgnoreCase));

            return new UpdateResult(
                CurrentVersion:    CurrentVersion,
                LatestVersion:     latest,
                IsUpdateAvailable: latest > CurrentVersion,
                ReleaseUrl:        release.HtmlUrl ?? $"https://github.com/{GitHubRepo}/releases/latest",
                ExeDownloadUrl:    exeAsset?.BrowserDownloadUrl,
                ReleaseNotes:      release.Body);
        }
        catch
        {
            return null;
        }
    }

    // ── Download update ───────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the release exe to %TEMP% and writes an updater script.
    /// Reports download percentage (0–100) via <paramref name="progress"/>.
    /// Returns the path to the updater .bat, or <c>null</c> on failure.
    /// </summary>
    internal async Task<string?> DownloadUpdateAsync(
        string exeDownloadUrl,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            var tempExe = Path.Combine(Path.GetTempPath(), "ManifestApp_update.exe");

            using var resp = await _http.GetAsync(
                exeDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? -1L;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(tempExe, FileMode.Create, FileAccess.Write, FileShare.None);

            var buf        = new byte[81920];
            long downloaded = 0;
            int  read;

            while ((read = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read), ct);
                downloaded += read;
                if (total > 0)
                    progress?.Report((double)downloaded / total * 100.0);
            }

            var currentExe = Process.GetCurrentProcess().MainModule?.FileName
                             ?? throw new InvalidOperationException("Cannot resolve current exe path.");

            // Build an updater script: wait for app to exit, swap files, relaunch
            var updaterPath = Path.Combine(Path.GetTempPath(), "ManifestApp_updater.bat");
            var script =
                "@echo off\r\n" +
                "ping -n 3 127.0.0.1 > nul\r\n" +
                $"move /y \"{tempExe}\" \"{currentExe}\"\r\n" +
                $"start \"\" \"{currentExe}\"\r\n" +
                "del \"%~f0\"\r\n";

            await File.WriteAllTextAsync(updaterPath, script, ct);
            return updaterPath;
        }
        catch
        {
            return null;
        }
    }

    // ── Apply update ──────────────────────────────────────────────────────────

    /// <summary>
    /// Launches the updater script in a hidden window and exits the app.
    /// Call this after <see cref="DownloadUpdateAsync"/> succeeds.
    /// </summary>
    internal static void ApplyUpdate(string updaterBatPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = "cmd.exe",
            Arguments       = $"/c \"{updaterBatPath}\"",
            UseShellExecute = false,
            CreateNoWindow  = true,
        });

        // Exit the running app so the updater can overwrite the exe
        Environment.Exit(0);
    }
}

// ── Records ───────────────────────────────────────────────────────────────────

internal sealed record UpdateResult(
    Version  CurrentVersion,
    Version  LatestVersion,
    bool     IsUpdateAvailable,
    string   ReleaseUrl,
    string?  ExeDownloadUrl,
    string?  ReleaseNotes);

// ── AOT-friendly JSON models ──────────────────────────────────────────────────

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string?                   TagName { get; set; }
    [JsonPropertyName("html_url")] public string?                   HtmlUrl { get; set; }
    [JsonPropertyName("body")]     public string?                   Body    { get; set; }
    [JsonPropertyName("assets")]   public List<GitHubReleaseAsset>? Assets  { get; set; }
}

internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]                  public string? Name                { get; set; }
    [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl  { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(GitHubReleaseAsset))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext { }
