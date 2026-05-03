using System.Text.Json;

namespace ManifestApp.Core;

/// <summary>Steam Store <c>/api/appdetails</c> helper for release / coming-soon flags.</summary>
public sealed class SteamStoreAppDetailsClient(HttpClient http)
{
    public async Task<SteamAppListingInfo> GetListingAsync(uint appId,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic,languages&cc=US&l=en";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://store.steampowered.com/");

            using var resp =
                await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return SteamAppListingInfo.Unavailable($"{(int)resp.StatusCode} {resp.ReasonPhrase}");

            var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var idKey = appId.ToString();
            if (!doc.RootElement.TryGetProperty(idKey, out var appNode))
                return SteamAppListingInfo.Unavailable("Store listing not found.");

            if (!appNode.TryGetProperty("success", out var succ) || succ.ValueKind != JsonValueKind.True ||
                !appNode.TryGetProperty("data", out var data))
                return SteamAppListingInfo.Unavailable("Steam has no public store page data for this App ID.");

            var name = data.TryGetProperty("name", out var n) ? n.GetString() : null;
            bool comingSoon = false;
            string? dateLine = null;
            if (data.TryGetProperty("release_date", out var rd))
            {
                if (rd.TryGetProperty("coming_soon", out var cs) &&
                    cs.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    comingSoon = cs.GetBoolean();

                if (rd.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String)
                    dateLine = d.GetString();
            }

            return new SteamAppListingInfo(
                Parsed: true,
                NameOnStore: name,
                ComingSoon: comingSoon,
                ReleaseDateCaption: dateLine,
                Diagnostics: null);
        }
        catch (Exception ex)
        {
            return SteamAppListingInfo.Unavailable(ex.Message);
        }
    }
}

public sealed record SteamAppListingInfo(
    bool Parsed,
    string? NameOnStore,
    bool ComingSoon,
    string? ReleaseDateCaption,
    string? Diagnostics)
{
    internal static SteamAppListingInfo Unavailable(string? why) =>
        new(false, null, false, null, why);
}
