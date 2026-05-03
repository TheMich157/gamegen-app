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
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return s ?? new AppSettings();
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
}
