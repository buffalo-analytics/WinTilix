using System.Text.Json;

namespace WinTilix.Session;

public static class ClusterStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string StorePath =>
        Path.Combine(AppDataRoot, "WinTilix", "ssh-clusters.json");

    public static string LegacyStorePath =>
        Path.Combine(AppDataRoot, "WinTilix", "ssh-cluster.json");

    private static string AppDataRoot =>
        Environment.GetEnvironmentVariable("LOCALAPPDATA")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static IReadOnlyList<SshClusterDefinition> LoadAll()
    {
        EnsureMigrated();
        try
        {
            if (!File.Exists(StorePath))
            {
                return [];
            }

            var doc = JsonSerializer.Deserialize<SshClusterStoreDocument>(
                File.ReadAllText(StorePath), JsonOptions);
            return Normalize(doc?.Clusters ?? []);
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static SshClusterDefinition? Get(string id) =>
        LoadAll().FirstOrDefault(cluster =>
            string.Equals(cluster.Id, id, StringComparison.OrdinalIgnoreCase));

    public static SshClusterDefinition Save(SshClusterDefinition cluster)
    {
        var name = cluster.Name.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("Cluster name is required.", nameof(cluster));
        }

        var hosts = CleanHosts(cluster.Hosts);
        if (hosts.Count == 0)
        {
            throw new ArgumentException("Add at least one host.", nameof(cluster));
        }

        var all = LoadAll().ToList();
        if (all.Any(existing =>
                !string.Equals(existing.Id, cluster.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"A cluster named '{name}' already exists.", nameof(cluster));
        }

        var saved = new SshClusterDefinition
        {
            Id = string.IsNullOrWhiteSpace(cluster.Id) ? Guid.NewGuid().ToString("N") : cluster.Id,
            Name = name,
            Hosts = hosts,
        };

        var index = all.FindIndex(existing =>
            string.Equals(existing.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            all[index] = saved;
        }
        else
        {
            all.Add(saved);
        }

        WriteAll(all);
        return saved;
    }

    public static bool Delete(string id)
    {
        var all = LoadAll().ToList();
        var removed = all.RemoveAll(cluster =>
            string.Equals(cluster.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return false;
        }

        WriteAll(all);
        return true;
    }

    /// <summary>Legacy helper used by older tests/callers; maps to the first imported cluster hosts.</summary>
    public static IReadOnlyList<string> LoadHosts() =>
        LoadAll().FirstOrDefault()?.Hosts ?? [];

    public static void SaveHosts(IEnumerable<string> hosts)
    {
        var cleaned = CleanHosts(hosts);
        if (cleaned.Count == 0)
        {
            return;
        }

        var all = LoadAll().ToList();
        var existing = all.FirstOrDefault(cluster =>
            string.Equals(cluster.Name, "Imported", StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            all.Insert(0, new SshClusterDefinition
            {
                Name = "Imported",
                Hosts = cleaned,
            });
        }
        else
        {
            existing.Hosts = cleaned;
        }

        WriteAll(all);
    }

    private static void EnsureMigrated()
    {
        if (File.Exists(StorePath) || !File.Exists(LegacyStorePath))
        {
            return;
        }

        try
        {
            var legacy = JsonSerializer.Deserialize<LegacyClusterDocument>(
                File.ReadAllText(LegacyStorePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var hosts = CleanHosts(legacy?.Hosts ?? []);
            if (hosts.Count == 0)
            {
                return;
            }

            WriteAll(
            [
                new SshClusterDefinition
                {
                    Name = "Imported",
                    Hosts = hosts,
                },
            ]);
        }
        catch (Exception)
        {
            // Leave the legacy file untouched if migration fails.
        }
    }

    private static void WriteAll(IEnumerable<SshClusterDefinition> clusters)
    {
        var directory = Path.GetDirectoryName(StorePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var doc = new SshClusterStoreDocument
        {
            Version = 1,
            Clusters = Normalize(clusters).ToList(),
        };
        File.WriteAllText(StorePath, JsonSerializer.Serialize(doc, JsonOptions));
    }

    private static List<SshClusterDefinition> Normalize(IEnumerable<SshClusterDefinition> clusters) =>
        clusters
            .Where(cluster => !string.IsNullOrWhiteSpace(cluster.Name))
            .Select(cluster => new SshClusterDefinition
            {
                Id = string.IsNullOrWhiteSpace(cluster.Id) ? Guid.NewGuid().ToString("N") : cluster.Id,
                Name = cluster.Name.Trim(),
                Hosts = CleanHosts(cluster.Hosts),
            })
            .Where(cluster => cluster.Hosts.Count > 0)
            .OrderBy(cluster => cluster.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> CleanHosts(IEnumerable<string> hosts) =>
        hosts
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private sealed class LegacyClusterDocument
    {
        public string[] Hosts { get; set; } = [];
    }
}
