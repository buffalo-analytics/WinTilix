using System.Net;
using WinTilix.Updates;
using Xunit;

namespace WinTilix.Tests;

public sealed class GitHubUpdateServiceTests
{
    [Fact]
    public async Task CheckAsync_ReturnsNewerPublishedRelease()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "tag_name": "v1.2.3",
                      "name": "WinTilix 1.2.3",
                      "html_url": "https://github.com/buffalo-analytics/WinTilix/releases/tag/v1.2.3",
                      "assets": [
                        {
                          "name": "WinTilix-1.2.3-win-x64-setup.exe",
                          "browser_download_url": "https://github.com/buffalo-analytics/WinTilix/releases/download/v1.2.3/WinTilix-1.2.3-win-x64-setup.exe",
                          "size": 123456
                        }
                      ]
                    }
                    """),
            };
        });
        using var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client, "buffalo-analytics", "WinTilix");

        var result = await service.CheckAsync(new Version(1, 0, 0));

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new Version(1, 2, 3), result.LatestVersion);
        Assert.Equal(
            "https://api.github.com/repos/buffalo-analytics/WinTilix/releases/latest",
            capturedRequest?.RequestUri?.AbsoluteUri);
        Assert.Contains("WinTilix/1.0.0", capturedRequest?.Headers.UserAgent.ToString());
        Assert.Equal("WinTilix-1.2.3-win-x64-setup.exe", result.Package?.Name);
        Assert.Equal(123456L, result.Package?.Size);
    }

    [Theory]
    [InlineData("v2.4.1", "2.4.1")]
    [InlineData("1.3.0-beta.1", "1.3.0")]
    [InlineData(" V3.0.0+build.5 ", "3.0.0")]
    public void TryParseVersion_HandlesReleaseTagFormats(string tag, string expected)
    {
        var parsed = GitHubUpdateService.TryParseVersion(tag, out var version);

        Assert.True(parsed);
        Assert.Equal(Version.Parse(expected), version);
    }

    [Fact]
    public void SelectSetupAsset_IgnoresOtherAndInsecureAssets()
    {
        var assets = new[]
        {
            new UpdatePackageAsset(
                "WinTilix-1.2.3-win-x64.zip",
                new Uri("https://example.com/WinTilix-1.2.3-win-x64.zip"),
                10),
            new UpdatePackageAsset(
                "WinTilix-1.2.3-win-x64-setup.exe",
                new Uri("http://example.com/WinTilix-1.2.3-win-x64-setup.exe"),
                20),
            new UpdatePackageAsset(
                "WinTilix-1.2.3-win-x64-setup.exe",
                new Uri("https://example.com/WinTilix-1.2.3-win-x64-setup.exe"),
                30),
        };

        var selected = GitHubUpdateService.SelectSetupAsset(assets);

        Assert.Equal(assets[2], selected);
    }

    [Fact]
    public async Task DownloadAsync_DownloadsSelectedInstallerAndReportsProgress()
    {
        var packageBytes = new byte[] { 1, 2, 3, 4, 5 };
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(packageBytes),
        });
        using var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client, "buffalo-analytics", "WinTilix");
        var package = new UpdatePackageAsset(
            "WinTilix-9.8.7-win-x64-setup.exe",
            new Uri("https://example.com/WinTilix-9.8.7-win-x64-setup.exe"),
            packageBytes.Length);
        var updates = new List<UpdateDownloadProgress>();

        var path = await service.DownloadAsync(
            package,
            new Version(9, 8, 7),
            new InlineProgress<UpdateDownloadProgress>(updates.Add));

        try
        {
            Assert.Equal(packageBytes, await File.ReadAllBytesAsync(path));
            Assert.Equal(packageBytes.Length, updates[^1].BytesReceived);
            Assert.Equal(100d, updates[^1].Percentage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
