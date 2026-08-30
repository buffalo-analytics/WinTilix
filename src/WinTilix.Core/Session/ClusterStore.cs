using System.Text.Json;

namespace WinTilix.Session;

public static class ClusterStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string StorePath =>
        Path.Combine(AppDataRoot, "WinTilix", "ssh-cluster.json");

    private static string AppDataRoot =>
        Environment.GetEnvironmentVariable("LOCALAPPDATA")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static IReadOnlyList<string> LoadHosts()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return [];
            }

            var doc = JsonSerializer.Deserialize<ClusterDocument>(File.ReadAllText(StorePath), JsonOptions);
            return doc?.Hosts?
                .Where(host => !string.IsNullOrWhiteSpace(host))
                .Select(host => host.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static void SaveHosts(IEnumerable<string> hosts)
    {
        var cleaned = hosts
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var directory = Path.GetDirectoryName(StorePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            StorePath,
            JsonSerializer.Serialize(new ClusterDocument { Hosts = cleaned }, JsonOptions));
    }

    private sealed class ClusterDocument
    {
        public string[] Hosts { get; set; } = [];
    }
}
