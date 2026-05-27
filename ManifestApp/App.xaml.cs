using Microsoft.UI.Xaml;

namespace ManifestApp;

public partial class App : Application
{
    internal AppServices Svcs { get; }

    /// <summary>Primary shell window — required for pickers and dialogs.</summary>
    internal Window MainShell { get; private set; } = null!;

    public App()
    {
        // Single-file WASDK bootstrap: PRI/runtime layout resolves under the extraction/root dir.
        Environment.SetEnvironmentVariable(
            "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY",
            AppContext.BaseDirectory);

        // TEMP: diagnostic logging for XAML/runtime crashes
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogCrash("TaskScheduler", e.Exception);

        InitializeComponent();

        UnhandledException += (_, e) => LogCrash("App", e.Exception);

        Svcs = new AppServices(CreateHttpClient());
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gamegen-crash.log");
            System.IO.File.AppendAllText(path,
                $"[{DateTime.Now:O}] [{source}] {ex?.GetType().FullName}: {ex?.Message}{Environment.NewLine}" +
                $"{ex?.StackTrace}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* best-effort */ }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10),
        };

        // Literal "GameGen App <version>" — the space means UserAgent.ParseAdd would treat it
        // as multiple product tokens, so bypass validation to keep the exact wording.
        var version = Services.UpdateService.CurrentVersionString;
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            $"GameGen App {version}");
        return client;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        MainShell = window;
        // The splash overlay inside MainWindow drives the update check and calls NavigateShell()
        // once the loading sequence finishes — see MainWindow.RunStartupSequenceAsync.
        window.Activate();
    }
}
