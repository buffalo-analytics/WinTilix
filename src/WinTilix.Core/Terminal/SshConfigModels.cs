namespace WinTilix.Terminal;

public sealed class SshConfigDocument
{
    public required string FilePath { get; init; }
    public List<SshConfigLine> Preamble { get; } = [];
    public List<SshConfigSection> Sections { get; } = [];

    public IEnumerable<SshHostSection> HostSections =>
        Sections.OfType<SshHostSection>();

    public string Serialize()
    {
        var lines = new List<string>();
        lines.AddRange(Preamble.Select(line => line.RawText));
        foreach (var section in Sections)
        {
            lines.Add(section.Header.RawText);
            lines.AddRange(section.Body.Select(line => line.RawText));
        }

        return string.Join(Environment.NewLine, lines) +
               (lines.Count > 0 ? Environment.NewLine : string.Empty);
    }
}

public abstract class SshConfigSection
{
    public required SshConfigLine Header { get; set; }
    public List<SshConfigLine> Body { get; } = [];
}

public sealed class SshHostSection : SshConfigSection
{
    public List<string> Aliases { get; set; } = [];
    public bool IsEditable =>
        Aliases.Count > 0 && Aliases.All(SshConfigTokenizer.IsSelectableAlias);

    public string DisplayName =>
        Aliases.Count == 0 ? "(unnamed Host)" : string.Join(' ', Aliases);
}

public sealed class SshMatchSection : SshConfigSection;

public sealed class SshConfigLine
{
    public required string RawText { get; set; }
    public SshLineKind Kind { get; init; }
    public string? Keyword { get; init; }
    public IReadOnlyList<string> Values { get; init; } = [];
}

public enum SshLineKind
{
    Blank,
    Comment,
    Directive,
    HostHeader,
    MatchHeader,
}

public sealed class SshHostEdit
{
    public string Alias { get; set; } = "";
    public string? HostName { get; set; }
    public string? User { get; set; }
    public int? Port { get; set; }
    public List<string> IdentityFiles { get; set; } = [];
    public List<string> ProxyJumps { get; set; } = [];
    public int? ServerAliveInterval { get; set; }
    public int? ServerAliveCountMax { get; set; }
    public string AdvancedText { get; set; } = "";
}

public sealed class SshConfigValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = [];
}
