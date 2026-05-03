namespace ManifestApp.Core;

public static class AppPaths
{
    public const string PublisherFolder = "GameGenApp";

    public static string LocalRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            PublisherFolder);

    public static string SettingsPath => Path.Combine(LocalRoot, "settings.json");

    public static string InstalledRecordsPath => Path.Combine(LocalRoot, "installed_manifests.json");

    public static string ImageCacheDir => Path.Combine(LocalRoot, "image_cache");

    public static void EnsureLayout()
    {
        Directory.CreateDirectory(LocalRoot);
        Directory.CreateDirectory(ImageCacheDir);
    }
}
