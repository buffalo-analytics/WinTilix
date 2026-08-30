using WinTilix.Session;
using Xunit;

namespace WinTilix.Tests;

public class ClusterStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsNamedClusters()
    {
        using var scope = new TempAppData();
        var saved = ClusterStore.Save(new SshClusterDefinition
        {
            Name = "Buffalo Web Cluster",
            Hosts = ["buffalo-web1", "buffalo-web2", " buffalo-web1 "],
        });

        var loaded = ClusterStore.LoadAll();
        Assert.Single(loaded);
        Assert.Equal(saved.Id, loaded[0].Id);
        Assert.Equal("Buffalo Web Cluster", loaded[0].Name);
        Assert.Equal(["buffalo-web1", "buffalo-web2"], loaded[0].Hosts);
        Assert.True(File.Exists(ClusterStore.StorePath));
    }

    [Fact]
    public void Save_RejectsDuplicateNamesAndEmptyHosts()
    {
        using var scope = new TempAppData();
        ClusterStore.Save(new SshClusterDefinition
        {
            Name = "Prod",
            Hosts = ["a"],
        });

        Assert.Throws<ArgumentException>(() =>
            ClusterStore.Save(new SshClusterDefinition
            {
                Name = "prod",
                Hosts = ["b"],
            }));
        Assert.Throws<ArgumentException>(() =>
            ClusterStore.Save(new SshClusterDefinition
            {
                Name = "Empty",
                Hosts = [],
            }));
    }

    [Fact]
    public void MigratesLegacyFlatHostList()
    {
        using var scope = new TempAppData();
        var directory = Path.GetDirectoryName(ClusterStore.LegacyStorePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            ClusterStore.LegacyStorePath,
            """
            { "Hosts": ["legacy-1", "legacy-2"] }
            """);

        var loaded = ClusterStore.LoadAll();
        Assert.Single(loaded);
        Assert.Equal("Imported", loaded[0].Name);
        Assert.Equal(["legacy-1", "legacy-2"], loaded[0].Hosts);
        Assert.True(File.Exists(ClusterStore.StorePath));
        Assert.True(File.Exists(ClusterStore.LegacyStorePath));
    }

    [Fact]
    public void LoadAll_ReturnsEmptyWhenStoreIsCorrupt()
    {
        using var scope = new TempAppData();
        var directory = Path.GetDirectoryName(ClusterStore.StorePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(ClusterStore.StorePath, "{ not-json");

        Assert.Empty(ClusterStore.LoadAll());
    }

    [Fact]
    public void Save_RejectsBlankName()
    {
        using var scope = new TempAppData();
        Assert.Throws<ArgumentException>(() =>
            ClusterStore.Save(new SshClusterDefinition
            {
                Name = "  ",
                Hosts = ["h1"],
            }));
    }

    [Fact]
    public void Delete_RemovesCluster()
    {
        using var scope = new TempAppData();
        var saved = ClusterStore.Save(new SshClusterDefinition
        {
            Name = "Temp",
            Hosts = ["h1"],
        });
        Assert.True(ClusterStore.Delete(saved.Id));
        Assert.Empty(ClusterStore.LoadAll());
    }

    private sealed class TempAppData : IDisposable
    {
        private readonly string? _previous;
        private readonly string _tempRoot;

        public TempAppData()
        {
            _previous = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            _tempRoot = Path.Combine(Path.GetTempPath(), "WinTilix-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable("LOCALAPPDATA", _tempRoot);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", _previous);
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
    }
}
