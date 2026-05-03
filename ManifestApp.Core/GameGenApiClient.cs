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

        var keySeg = Uri.EscapeDataString(apiKey.Trim());
        var generateUrl = $"{GetApiRoot().TrimEnd('/')}/api/{keySeg}/generate/{appId}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, generateUrl);
            req.Headers.Accept.ParseAdd("application/json");
            using var jsonResponse =
                await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

            var raw = await ReadBodyAsync(jsonResponse.Content, cancellationToken).ConfigureAwait(false);

            if (LooksLikeZip(raw))
            {
                if (jsonResponse.IsSuccessStatusCode)
                    return GameGenZipResult.Successful(raw);

                return GameGenZipResult.Fail($"GameGen replied with ZIP along with HTTP {(int)jsonResponse.StatusCode}.");
            }

            if (!TryExtractJsonEnvelope(raw, out var explicitFail, out var err, out var dl, out var mApp))
            {
                if (!jsonResponse.IsSuccessStatusCode)
                {
                    return GameGenZipResult.Fail(
                        $"GameGen HTTP {(int)jsonResponse.StatusCode} — body was not recognizable JSON.");
                }

                return await TryZipQueryFallbackAsync(generateUrl, cancellationToken, progress).ConfigureAwait(false);
            }

            if (explicitFail)
            {
                return GameGenZipResult.Fail(string.IsNullOrWhiteSpace(err)
                    ? "GameGen returned success=false for this Steam App ID."
                    : err!);
            }

            if (!string.IsNullOrEmpty(mApp) &&
                uint.TryParse(mApp, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var rspId) &&
                rspId != appId)
            {
                return GameGenZipResult.Fail(
                    $"GameGen returned manifest metadata for Steam app {rspId}, but this install targets {appId}.");
            }

            if (!string.IsNullOrWhiteSpace(dl))
            {
                try
                {
                    var resolved = AbsoluteUrl(generateUrl, dl!);
                    var zipBytes = await DownloadBytesAsync(resolved, cancellationToken, progress).ConfigureAwait(false);
                    return LooksLikeZip(zipBytes)
                        ? GameGenZipResult.Successful(zipBytes)
                        : GameGenZipResult.Fail("GameGen download URL did not return a ZIP archive.");
                }
                catch (Exception ex)
                {
                    return GameGenZipResult.Fail($"Downloading manifest ZIP failed: {ex.Message}");
                }
            }

            return await TryZipQueryFallbackAsync(generateUrl, cancellationToken, progress).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return GameGenZipResult.Fail($"Network error contacting GameGen: {ex.Message}");
        }
    }

    private async Task<GameGenZipResult> TryZipQueryFallbackAsync(string generateUrl,
        CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var zipUrl = $"{generateUrl}?format=zip";
        try
        {
            using var rsp =
                await http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            using (rsp)
            {
                // Stream with progress for the fallback ZIP download
                var raw = await StreamBodyAsync(rsp.Content, cancellationToken, progress).ConfigureAwait(false);

                if (rsp.IsSuccessStatusCode && LooksLikeZip(raw))
                    return GameGenZipResult.Successful(raw);

                if (TryExtractJsonEnvelope(raw, out _, out var err, out _, out _))
                {
                    var msg = string.IsNullOrWhiteSpace(err) ? $"{(int)rsp.StatusCode} {rsp.ReasonPhrase}".Trim() : err;
                    return GameGenZipResult.Fail($"{msg} (ZIP ?format=zip fallback).");
                }

                var hint = rsp.IsSuccessStatusCode ? "unexpected body" : DescribeHttp(rsp);
                return GameGenZipResult.Fail($"{hint} (ZIP fallback).");
            }
        }
        catch (Exception ex)
        {
            return GameGenZipResult.Fail($"ZIP fallback request failed: {ex.Message}");
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
        data.Count >= 2 && data[0] == 'P' && data[1] == 'K';
}

public sealed class GameGenZipResult(bool ok, byte[]? zipBytes, string? errorMessage)
{
    public bool Ok { get; } = ok;
    public byte[]? ZipBytes { get; } = zipBytes;
    public string? ErrorMessage { get; } = errorMessage;

    public static GameGenZipResult Successful(byte[] bytes) => new(true, bytes, null);

    public static GameGenZipResult Fail(string msg) => new(false, null, msg);
}
