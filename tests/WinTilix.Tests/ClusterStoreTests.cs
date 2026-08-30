using WinTilix.Session;
using Xunit;

namespace WinTilix.Tests;

public class ClusterStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsHosts()
    {
        var previous = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var tempRoot = Path.Combine(Path.GetTempPath(), "WinTilix-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", tempRoot);
            ClusterStore.SaveHosts(["buffalo-web1", "user@host-2", " buffalo-web1 "]);
            var loaded = ClusterStore.LoadHosts();
            Assert.Equal(["buffalo-web1", "user@host-2"], loaded);
            Assert.True(File.Exists(ClusterStore.StorePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", previous);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
