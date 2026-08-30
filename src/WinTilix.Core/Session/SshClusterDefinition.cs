namespace WinTilix.Session;

public sealed class SshClusterDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<string> Hosts { get; set; } = [];
}

public sealed class SshClusterStoreDocument
{
    public int Version { get; set; } = 1;
    public List<SshClusterDefinition> Clusters { get; set; } = [];
}
