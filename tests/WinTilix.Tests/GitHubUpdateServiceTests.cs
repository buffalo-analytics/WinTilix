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
                    """{"tag_name":"v1.2.3","name":"WinTilix 1.2.3","html_url":"https://github.com/buffalo-analytics/WinTilix/releases/tag/v1.2.3"}"""),
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

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
