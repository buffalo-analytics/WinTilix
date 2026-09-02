using WinTilix.Updates;
using Xunit;

namespace WinTilix.Tests;

public class UpdateCheckCoordinatorTests
{
    [Fact]
    public async Task ReusesRecentResultWithoutCallingGitHubAgain()
    {
        var calls = 0;
        var coordinator = new UpdateCheckCoordinator(
            _ => Task.FromResult(Result(calls++, new Version(0, 1, 6))),
            TimeSpan.FromMinutes(15),
            () => DateTimeOffset.UnixEpoch);

        await coordinator.GetAsync();
        await coordinator.GetAsync();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ChecksAgainOnceTheCachedResultGoesStale()
    {
        var calls = 0;
        var now = DateTimeOffset.UnixEpoch;
        var coordinator = new UpdateCheckCoordinator(
            _ => Task.FromResult(Result(calls++, new Version(0, 1, 7))),
            TimeSpan.FromMinutes(15),
            () => now);

        await coordinator.GetAsync();
        now = now.AddMinutes(16);
        await coordinator.GetAsync();

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ForceRefreshIgnoresTheCache()
    {
        var calls = 0;
        var coordinator = new UpdateCheckCoordinator(
            _ => Task.FromResult(Result(calls++, new Version(0, 1, 7))),
            TimeSpan.FromHours(12),
            () => DateTimeOffset.UnixEpoch);

        await coordinator.GetAsync();
        var second = await coordinator.GetAsync(forceRefresh: true);

        Assert.Equal(2, calls);
        Assert.True(second.IsUpdateAvailable);
    }

    [Fact]
    public async Task SeesANewReleasePublishedWhileTheAppIsRunning()
    {
        var latest = new Version(0, 1, 6);
        var now = DateTimeOffset.UnixEpoch;
        var coordinator = new UpdateCheckCoordinator(
            _ => Task.FromResult(Result(0, latest)),
            TimeSpan.FromMinutes(15),
            () => now);

        var atStartup = await coordinator.GetAsync();
        Assert.False(atStartup.IsUpdateAvailable);

        latest = new Version(0, 1, 7);
        now = now.AddHours(3);
        var afterPolling = await coordinator.GetAsync();

        Assert.True(afterPolling.IsUpdateAvailable);
        Assert.Equal(new Version(0, 1, 7), coordinator.LastResult?.LatestVersion);
    }

    [Fact]
    public async Task FailedCheckIsNotCached()
    {
        var calls = 0;
        var coordinator = new UpdateCheckCoordinator(
            _ =>
            {
                calls++;
                return calls == 1
                    ? Task.FromException<UpdateCheckResult>(new HttpRequestException("offline"))
                    : Task.FromResult(Result(calls, new Version(0, 1, 7)));
            },
            TimeSpan.FromHours(12),
            () => DateTimeOffset.UnixEpoch);

        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.GetAsync());
        Assert.Null(coordinator.LastResult);

        var recovered = await coordinator.GetAsync();
        Assert.Equal(2, calls);
        Assert.True(recovered.IsUpdateAvailable);
    }

    private static UpdateCheckResult Result(int _, Version latest)
    {
        var current = new Version(0, 1, 6);
        return new UpdateCheckResult(
            current,
            latest,
            $"WinTilix {latest}",
            new Uri("https://github.com/buffalo-analytics/WinTilix/releases/latest"),
            latest > current,
            null);
    }
}
