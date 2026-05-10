using System.Text.Json;

namespace ManifestApp.Core;

/// <summary>
/// Canonical GameGen route: <c>GET https://gamegen.lol/api/{key}/generate/{appId}</c>.
/// Documentation sometimes shows placeholders percent-encoded (<c>%7Bkey%7D</c>, <c>%7BappId%7D</c>) —
/// substitute your real dashboard key (URL-encoded as a single path segment) and numeric Steam App ID.
/// Response: JSON with <c>manifest.downloadUrl</c> (preferred) then <c>?format=zip</c> fallback.
/// </summary>
public sealed class GameGenApiClient(HttpClient http, SettingsStore settingsStore)
{
    public async Task<GameGenZipResult> DownloadGenerateZipAsync(string apiKey, uint appId,
        CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return GameGenZipResult.Fail("GameGen API key is empty. Add it in Settings.");

        var keySeg      = Uri.EscapeDataString(apiKey.Trim());
        var generateUrl = $"{GetApiRoot().TrimEnd('/')}/api/{keySeg}/generate/{appId}?format=zip";

        try
        {
            // Single API call — request ZIP directly so the server charges exactly one credit.
            using var req = new HttpRequestMessage(HttpMethod.Get, generateUrl);
            req.Headers.Accept.ParseAdd("application/zip, application/octet-stream, application/json");
            using var rsp =
                await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

            var raw = await StreamBodyAsync(rsp.Content, cancellationToken, progress).ConfigureAwait(false);

            if (LooksLikeZip(raw))
                return GameGenZipResult.Successful(raw);

            // Server returned JSON — parse for error or a CDN downloadUrl.
            if (TryExtractJsonEnvelope(raw, out var explicitFail, out var err, out var dl, out var mApp))
            {
                if (explicitFail)
                    return GameGenZipResult.Fail(string.IsNullOrWhiteSpace(err)
                        ? "GameGen returned success=false for this Steam App ID."
                        : err!);

                if (!string.IsNullOrEmpty(mApp) &&
                    uint.TryParse(mApp, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var rspId) &&
                    rspId != appId)
                    return GameGenZipResult.Fail(
                        $"GameGen returned manifest for app {rspId}, but install targets {appId}.");

                // downloadUrl is a CDN link — downloading it does not consume an additional credit.
                if (!string.IsNullOrWhiteSpace(dl))
                {
                    try
                    {
                        var resolved = AbsoluteUrl(generateUrl, dl!);
                        var zipBytes = await DownloadBytesAsync(resolved, cancellationToken, progress)
                            .ConfigureAwait(false);
                        return LooksLikeZip(zipBytes)
                            ? GameGenZipResult.Successful(zipBytes)
                            : GameGenZipResult.Fail("Download URL did not return a ZIP archive.");
                    }
                    catch (Exception ex)
                    {
                        return GameGenZipResult.Fail($"Downloading manifest ZIP failed: {ex.Message}");
                    }
                }

                return GameGenZipResult.Fail(string.IsNullOrWhiteSpace(err)
                    ? $"GameGen HTTP {(int)rsp.StatusCode} — no manifest returned."
                    : err!);
            }

            return GameGenZipResult.Fail(rsp.IsSuccessStatusCode
                ? "GameGen response was not a ZIP archive."
                : $"GameGen HTTP {(int)rsp.StatusCode}.");
        }
        catch (Exception ex)
        {
            return GameGenZipResult.Fail($"Network error contacting GameGen: {ex.Message}");
        }
    }

    /// <returns>false if payload is not JSON object.</returns>
    private static bool TryExtractJsonEnvelope(
        ReadOnlySpan<byte> raw,
        out bool explicitFail,
        out string? error,
        out string? downloadUrl,
        out string? manifestAppId)
    {
        explicitFail = false;
        error = null;
        downloadUrl = null;
        manifestAppId = null;

        try
        {
            var txt = System.Text.Encoding.UTF8.GetString(raw);
            using var doc = JsonDocument.Parse(txt);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("error", out var errEl))
                error = errEl.GetString();

            if (root.TryGetProperty("success", out var succ))
            {
                if (succ.ValueKind == JsonValueKind.False)
                    explicitFail = true;
                else if (succ.ValueKind == JsonValueKind.String &&
                         bool.TryParse(succ.GetString(), out var sb) &&
                         !sb)
                    explicitFail = true;
            }

            if (root.TryGetProperty("manifest", out var mf))
            {
                if (mf.TryGetProperty("downloadUrl", out var mdu))
                    downloadUrl = mdu.GetString();
                if (mf.TryGetProperty("appId", out var maid))
                {
                    manifestAppId = maid.ValueKind switch
                    {
                        JsonValueKind.Number => maid.GetUInt32().ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        JsonValueKind.String => maid.GetString(),
                        _ => null,
                    };
                }
            }

            if (string.IsNullOrEmpty(downloadUrl) && root.TryGetProperty("downloadUrl", out var topDu))
                downloadUrl = topDu.GetString();

            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GetApiRoot()
    {
        var b = settingsStore.Load().GameGenApiBaseUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(b))
            return "https://gamegen.lol";
        if (!(b.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
              b.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            return "https://gamegen.lol";
        return b;
    }

    private async Task<byte[]> DownloadBytesAsync(Uri url, CancellationToken cancellationToken,
        IProgress<double>? progress = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response =
            await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await StreamBodyAsync(response.Content, cancellationToken, progress).ConfigureAwait(false);
    }

    /// <summary>Reads an HTTP body as a byte array, reporting download progress (0–100) when <paramref name="progress"/> is provided.</summary>
    private static async Task<byte[]> StreamBodyAsync(HttpContent content, CancellationToken ct,
        IProgress<double>? progress = null)
    {
        if (progress is null)
        {
            await using var ms0 = new MemoryStream();
            await content.CopyToAsync(ms0, ct).ConfigureAwait(false);
            return ms0.ToArray();
        }

        var total = content.Headers.ContentLength ?? -1L;
        await using var src = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var ms  = new MemoryStream(total > 0 ? (int)Math.Min(total, 32 * 1024 * 1024) : 65536);

        var buf        = new byte[81920];
        long downloaded = 0;
        int  read;
        while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            ms.Write(buf, 0, read);
            downloaded += read;
            if (total > 0)
                progress.Report(downloaded * 100.0 / total);
        }

        return ms.ToArray();
    }

    private static Uri AbsoluteUrl(string generateUrl, string relativeOrAbsolute)
    {
        var t = relativeOrAbsolute.Trim();
        if (t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return new Uri(t, UriKind.Absolute);
        return new Uri(new Uri(generateUrl, UriKind.Absolute), t.TrimStart('/'));
    }

    private static async Task<byte[]> ReadBodyAsync(HttpContent content, CancellationToken ct)
    {
        await using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }

    private static string DescribeHttp(HttpResponseMessage rsp) =>
        string.IsNullOrWhiteSpace(rsp.ReasonPhrase)
            ? $"{(int)rsp.StatusCode}"
            : $"{(int)rsp.StatusCode} {rsp.ReasonPhrase}";

    private static bool LooksLikeZip(IReadOnlyList<byte> data) =>
        data.Count >= 4 &&
        data[0] == 0x50 && data[1] == 0x4B &&   // 'P','K'
        data[2] == 0x03 && data[3] == 0x04;      // local-file header signature

    // ── Game Request ─────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/{key}/request/{appId} — formally requests a game be added to the GameGen registry.
    /// </summary>
    public async Task<GameGenRequestResult> RequestGameAsync(
        string apiKey, uint appId, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return GameGenRequestResult.Fail("API key is missing.");

        var keySeg = Uri.EscapeDataString(apiKey.Trim());
        var url    = $"{GetApiRoot().TrimEnd('/')}/api/{keySeg}/request/{appId}";

        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                reason = string.IsNullOrWhiteSpace(reason) ? (string?)null : reason.Trim(),
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            };
            using var rsp = await http.SendAsync(req, ct).ConfigureAwait(false);
            var raw = await rsp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root      = doc.RootElement;
            var status    = root.TryGetProperty("status",   out var st) ? st.GetString() : null;
            var gameName  = root.TryGetProperty("gameName", out var gn) ? gn.GetString() : null;
            var appIdStr  = root.TryGetProperty("appId",    out var ai) ? ai.GetString() : appId.ToString();
            var error     = root.TryGetProperty("error",    out var er) ? er.GetString() : null;

            return status == "sent"
                ? GameGenRequestResult.Success(appIdStr, gameName)
                : GameGenRequestResult.Fail(error ?? $"Request not sent (status: {status ?? "unknown"}).");
        }
        catch (Exception ex)
        {
            return GameGenRequestResult.Fail($"Network error: {ex.Message}");
        }
    }

    // ── Stats ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/{key}/stats — plan status and remaining daily credits.
    /// </summary>
    public async Task<GameGenStatsResult> GetStatsAsync(string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new GameGenStatsResult { Ok = false, ErrorMessage = "No API key." };

        var keySeg = Uri.EscapeDataString(apiKey.Trim());
        var url    = $"{GetApiRoot().TrimEnd('/')}/api/{keySeg}/stats";

        try
        {
            using var rsp = await http.GetAsync(url, ct).ConfigureAwait(false);
            var raw = await rsp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!rsp.IsSuccessStatusCode)
                return new GameGenStatsResult { Ok = false, ErrorMessage = $"HTTP {(int)rsp.StatusCode}" };

            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;

            string? plan = null, displayName = null, discordId = null, userRole = null;
            bool isStaff = false;
            int? creditsRemaining = null, creditsTotal = null;

            if (root.TryGetProperty("plan", out var p)) plan = p.GetString();

            // The API returns Discord identity nested under "user": { discordId, username, discriminator }
            var userEl = root.TryGetProperty("user", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.Object
                ? u
                : root;   // fall back to root-level search for older API shapes

            if (userEl.TryGetProperty("discordId", out var did) &&
                did.ValueKind == System.Text.Json.JsonValueKind.String)
                discordId = did.GetString();

            if (userEl.TryGetProperty("username", out var un) &&
                un.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var name = un.GetString() ?? "";
                // Append discriminator only when it's a legacy 4-digit tag (not "0" or absent)
                if (userEl.TryGetProperty("discriminator", out var disc) &&
                    disc.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var tag = disc.GetString();
                    if (!string.IsNullOrWhiteSpace(tag) && tag != "0")
                        name = $"{name}#{tag}";
                }
                if (!string.IsNullOrWhiteSpace(name))
                    displayName = name;
            }

            if (userEl.TryGetProperty("role", out var roleEl) &&
                roleEl.ValueKind == System.Text.Json.JsonValueKind.String)
                userRole = roleEl.GetString();

            if (userEl.TryGetProperty("isStaff", out var staffEl))
                isStaff = staffEl.ValueKind == System.Text.Json.JsonValueKind.True ||
                          (staffEl.ValueKind == System.Text.Json.JsonValueKind.String &&
                           bool.TryParse(staffEl.GetString(), out var sb) && sb);

            // Credits/usage are nested under "usage": { today, limit, remaining, resetAt }
            int?     usageToday  = null;
            string?  resetAt     = null;

            if (root.TryGetProperty("usage", out var usage) &&
                usage.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (usage.TryGetProperty("remaining", out var rem) && rem.TryGetInt32(out var r))
                    creditsRemaining = r;
                if (usage.TryGetProperty("limit", out var lim) && lim.TryGetInt32(out var l))
                    creditsTotal = l;
                if (usage.TryGetProperty("today", out var tod) && tod.TryGetInt32(out var t))
                    usageToday = t;
                if (usage.TryGetProperty("resetAt", out var rat) &&
                    rat.ValueKind == System.Text.Json.JsonValueKind.String)
                    resetAt = rat.GetString();
            }
            else
            {
                // Fallback: root-level search for older API shapes
                foreach (var field in new[] { "creditsRemaining", "remaining", "credits", "dailyCreditsRemaining", "requestsRemaining" })
                {
                    if (root.TryGetProperty(field, out var v) && v.TryGetInt32(out var i))
                    { creditsRemaining = i; break; }
                }
                foreach (var field in new[] { "creditsTotal", "dailyLimit", "totalCredits", "limit", "dailyCredits" })
                {
                    if (root.TryGetProperty(field, out var v) && v.TryGetInt32(out var i))
                    { creditsTotal = i; break; }
                }
            }

            return new GameGenStatsResult
            {
                Ok               = true,
                Plan             = plan,
                CreditsRemaining = creditsRemaining,
                CreditsTotal     = creditsTotal,
                UsageToday       = usageToday,
                ResetAt          = resetAt,
                DisplayName      = displayName,
                DiscordId        = discordId,
                Role             = userRole,
                IsStaff          = isStaff,
            };
        }
        catch (Exception ex)
        {
            return new GameGenStatsResult { Ok = false, ErrorMessage = ex.Message };
        }
    }
}

public sealed class GameGenZipResult(bool ok, byte[]? zipBytes, string? errorMessage)
{
    public bool Ok { get; } = ok;
    public byte[]? ZipBytes { get; } = zipBytes;
    public string? ErrorMessage { get; } = errorMessage;

    public static GameGenZipResult Successful(byte[] bytes) => new(true, bytes, null);
    public static GameGenZipResult Fail(string msg) => new(false, null, msg);
}

public sealed class GameGenRequestResult
{
    public bool    Sent         { get; init; }
    public string? GameName     { get; init; }
    public string? AppId        { get; init; }
    public string? ErrorMessage { get; init; }

    public static GameGenRequestResult Success(string? appId, string? gameName) =>
        new() { Sent = true, AppId = appId, GameName = gameName };
    public static GameGenRequestResult Fail(string msg) =>
        new() { Sent = false, ErrorMessage = msg };
}

public sealed class GameGenStatsResult
{
    public bool    Ok               { get; init; }
    public string? Plan             { get; init; }
    public int?    CreditsRemaining { get; init; }
    public int?    CreditsTotal     { get; init; }
    /// <summary>Generations already used today (usage.today).</summary>
    public int?    UsageToday       { get; init; }
    /// <summary>ISO-8601 timestamp when the daily quota resets (usage.resetAt).</summary>
    public string? ResetAt          { get; init; }
    public string? DisplayName      { get; init; }
    /// <summary>Raw Discord snowflake ID — used as last-resort display when no human-readable name is available.</summary>
    public string? DiscordId        { get; init; }
    /// <summary>Role string from the API: "USER", "ADMIN", "OWNER", etc.</summary>
    public string? Role             { get; init; }
    /// <summary>True when role is anything other than USER (staff/admin/owner).</summary>
    public bool    IsStaff          { get; init; }
    public string? ErrorMessage     { get; init; }
}
