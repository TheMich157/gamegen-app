using System.Text.Json;
using ManifestApp.Core.Models;

namespace ManifestApp.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AppSettings Load()
    {
        AppPaths.EnsureLayout();
        if (!File.Exists(AppPaths.SettingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsPath);
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            // Re-apply defaults for fields that may be null in settings files saved before
            // the default was introduced (System.Text.Json restores explicit nulls from disk).
            s.AdminEndpointUrl ??= "https://gamegen.lol";
            s.GameDetailsVideoStartupBehavior = NormalizeVideoStartupBehavior(s.GameDetailsVideoStartupBehavior);
            return s;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureLayout();
        File.WriteAllText(
            AppPaths.SettingsPath,
            JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static string NormalizeVideoStartupBehavior(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "paused" => "paused",
            "sound" => "sound",
            _ => "muted",
        };
}
