using ManifestApp.Core;
using Xunit;

namespace ManifestApp.Core.Tests;

public sealed class TrustedDownloadUrlTests
{
    [Theory]
    [InlineData("https://github.com/TheMich157/gamegen-app/releases/download/v1/ManifestApp.exe")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/123/456")]
    public void IsAllowedGitHubReleaseAsset_accepts_github_hosts(string url)
    {
        Assert.True(TrustedDownloadUrl.IsAllowedGitHubReleaseAsset(new Uri(url)));
    }

    [Theory]
    [InlineData("https://evil.example/ManifestApp.exe")]
    [InlineData("http://github.com/release.exe")]
    public void IsAllowedGitHubReleaseAsset_rejects_untrusted_urls(string url)
    {
        Assert.False(TrustedDownloadUrl.IsAllowedGitHubReleaseAsset(new Uri(url)));
    }

    [Fact]
    public void IsAllowedGameGenCdn_accepts_gamegen_hosts()
    {
        Assert.True(TrustedDownloadUrl.IsAllowedGameGenCdn(
            new Uri("https://cdn.gamegen.lol/manifest.zip"),
            "https://gamegen.lol"));
    }
}
