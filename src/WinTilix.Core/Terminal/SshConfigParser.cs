namespace WinTilix.Terminal;

public static class SshConfigParser
{
    private static readonly HashSet<string> KnownDirectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "HostName", "User", "Port", "IdentityFile", "ProxyJump",
        "ServerAliveInterval", "ServerAliveCountMax",
    };

    public static SshConfigDocument Parse(string path, string content)
    {
        var document = new SshConfigDocument { FilePath = Path.GetFullPath(path) };
        SshConfigSection? current = null;

        foreach (var raw in SplitLines(content))
        {
            var line = Classify(raw);
            if (line.Kind == SshLineKind.HostHeader)
            {
                current = new SshHostSection
                {
                    Header = line,
                    Aliases = line.Values.ToList(),
                };
                document.Sections.Add(current);
                continue;
            }

            if (line.Kind == SshLineKind.MatchHeader)
            {
                current = new SshMatchSection { Header = line };
                document.Sections.Add(current);
                continue;
            }

            if (current is null)
            {
                document.Preamble.Add(line);
            }
            else
            {
                current.Body.Add(line);
            }
        }

        return document;
    }

    public static SshHostEdit ReadHost(SshHostSection section)
    {
        var edit = new SshHostEdit
        {
            Alias = string.Join(' ', section.Aliases),
        };

        var advanced = new List<string>();
        foreach (var line in section.Body)
        {
            if (line.Kind != SshLineKind.Directive || line.Keyword is null)
            {
                if (line.Kind is SshLineKind.Comment or SshLineKind.Blank)
                {
                    continue;
                }

                if (line.Kind == SshLineKind.Directive)
                {
                    advanced.Add(line.RawText.TrimStart());
                }

                continue;
            }

            if (!KnownDirectives.Contains(line.Keyword))
            {
                advanced.Add(line.RawText.TrimStart());
                continue;
            }

            switch (line.Keyword.ToLowerInvariant())
            {
                case "hostname":
                    edit.HostName = line.Values.FirstOrDefault();
                    break;
                case "user":
                    edit.User = line.Values.FirstOrDefault();
                    break;
                case "port" when int.TryParse(line.Values.FirstOrDefault(), out var port):
                    edit.Port = port;
                    break;
                case "identityfile":
                    edit.IdentityFiles.AddRange(line.Values);
                    break;
                case "proxyjump":
                    edit.ProxyJumps.AddRange(
                        line.Values.SelectMany(value =>
                            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                    break;
                case "serveraliveinterval" when int.TryParse(line.Values.FirstOrDefault(), out var interval):
                    edit.ServerAliveInterval = interval;
                    break;
                case "serveralivecountmax" when int.TryParse(line.Values.FirstOrDefault(), out var count):
                    edit.ServerAliveCountMax = count;
                    break;
            }
        }

        edit.AdvancedText = string.Join(Environment.NewLine, advanced);
        return edit;
    }

    public static SshConfigValidationResult Validate(SshHostEdit edit, SshConfigDocument document, SshHostSection? existing)
    {
        var result = new SshConfigValidationResult();
        var aliases = ParseAliases(edit.Alias);
        if (aliases.Count == 0)
        {
            result.Errors.Add("Host alias is required.");
        }
        else if (aliases.Any(alias => !SshConfigTokenizer.IsSelectableAlias(alias)))
        {
            result.Errors.Add("Host aliases cannot contain wildcards (* ?) or start with !.");
        }
        else
        {
            foreach (var alias in aliases)
            {
                var conflict = document.HostSections.Any(section =>
                    !ReferenceEquals(section, existing) &&
                    section.Aliases.Any(candidate =>
                        string.Equals(candidate, alias, StringComparison.OrdinalIgnoreCase)));
                if (conflict)
                {
                    result.Errors.Add($"Another Host already uses the alias '{alias}'.");
                    break;
                }
            }
        }

        if (edit.Port is < 1 or > 65535)
        {
            result.Errors.Add("Port must be between 1 and 65535.");
        }

        if (edit.ServerAliveInterval is < 0)
        {
            result.Errors.Add("ServerAliveInterval cannot be negative.");
        }

        if (edit.ServerAliveCountMax is < 0)
        {
            result.Errors.Add("ServerAliveCountMax cannot be negative.");
        }

        return result;
    }

    public static void ApplyHost(SshConfigDocument document, SshHostSection? existing, SshHostEdit edit)
    {
        var aliases = ParseAliases(edit.Alias);
        var section = existing ?? CreateHostSection(aliases);
        if (existing is null)
        {
            document.Sections.Add(section);
        }

        section.Aliases = aliases.ToList();
        section.Header = MakeHeader("Host", aliases);

        var indent = DetectIndent(section);
        var commentsAndBlanks = section.Body
            .Where(line => line.Kind is SshLineKind.Comment or SshLineKind.Blank)
            .ToList();

        var body = new List<SshConfigLine>();
        AddDirective(body, indent, "HostName", NullIfEmpty(edit.HostName));
        AddDirective(body, indent, "User", NullIfEmpty(edit.User));
        if (edit.Port is int port)
        {
            AddDirective(body, indent, "Port", port.ToString());
        }

        foreach (var identity in edit.IdentityFiles.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            AddDirective(body, indent, "IdentityFile", identity.Trim());
        }

        foreach (var jump in edit.ProxyJumps.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            AddDirective(body, indent, "ProxyJump", jump.Trim());
        }

        if (edit.ServerAliveInterval is int interval)
        {
            AddDirective(body, indent, "ServerAliveInterval", interval.ToString());
        }

        if (edit.ServerAliveCountMax is int count)
        {
            AddDirective(body, indent, "ServerAliveCountMax", count.ToString());
        }

        foreach (var advancedLine in SplitLines(edit.AdvancedText))
        {
            if (string.IsNullOrWhiteSpace(advancedLine))
            {
                continue;
            }

            var trimmed = advancedLine.TrimStart();
            body.Add(Classify(indent + trimmed));
        }

        var leading = commentsAndBlanks
            .TakeWhile(line => line.Kind is SshLineKind.Comment or SshLineKind.Blank)
            .ToList();
        var trailing = commentsAndBlanks.Skip(leading.Count).ToList();

        section.Body.Clear();
        section.Body.AddRange(leading);
        section.Body.AddRange(body);
        section.Body.AddRange(trailing);
    }

    public static void RemoveHost(SshConfigDocument document, SshHostSection section) =>
        document.Sections.Remove(section);

    public static IReadOnlyList<string> ParseAliases(string value) =>
        value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static SshHostSection CreateHostSection(IReadOnlyList<string> aliases) =>
        new()
        {
            Header = MakeHeader("Host", aliases),
            Aliases = aliases.ToList(),
        };

    private static SshConfigLine MakeHeader(string keyword, IReadOnlyList<string> values) =>
        new()
        {
            RawText = $"{keyword} {string.Join(' ', values.Select(SshConfigTokenizer.QuoteIfNeeded))}",
            Kind = keyword.Equals("Host", StringComparison.OrdinalIgnoreCase)
                ? SshLineKind.HostHeader
                : SshLineKind.MatchHeader,
            Keyword = keyword,
            Values = values.ToArray(),
        };

    private static void AddDirective(List<SshConfigLine> body, string indent, string keyword, string? value)
    {
        if (value is null)
        {
            return;
        }

        body.Add(new SshConfigLine
        {
            RawText = $"{indent}{keyword} {SshConfigTokenizer.QuoteIfNeeded(value)}",
            Kind = SshLineKind.Directive,
            Keyword = keyword,
            Values = [value],
        });
    }

    private static string DetectIndent(SshHostSection section)
    {
        var sample = section.Body.FirstOrDefault(line =>
            line.Kind == SshLineKind.Directive && line.RawText.Length > 0 && char.IsWhiteSpace(line.RawText[0]));
        if (sample is null)
        {
            return "    ";
        }

        var count = sample.RawText.TakeWhile(char.IsWhiteSpace).Count();
        return sample.RawText[..count];
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IEnumerable<string> SplitLines(string content)
    {
        if (content.Length == 0)
        {
            yield break;
        }

        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static SshConfigLine Classify(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new SshConfigLine { RawText = raw, Kind = SshLineKind.Blank };
        }

        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith('#'))
        {
            return new SshConfigLine { RawText = raw, Kind = SshLineKind.Comment };
        }

        var tokens = SshConfigTokenizer.Tokenize(raw);
        if (tokens.Count == 0)
        {
            return new SshConfigLine { RawText = raw, Kind = SshLineKind.Blank };
        }

        var keyword = tokens[0];
        var values = tokens.Skip(1).ToArray();
        if (keyword.Equals("Host", StringComparison.OrdinalIgnoreCase))
        {
            return new SshConfigLine
            {
                RawText = raw,
                Kind = SshLineKind.HostHeader,
                Keyword = keyword,
                Values = values,
            };
        }

        if (keyword.Equals("Match", StringComparison.OrdinalIgnoreCase))
        {
            return new SshConfigLine
            {
                RawText = raw,
                Kind = SshLineKind.MatchHeader,
                Keyword = keyword,
                Values = values,
            };
        }

        return new SshConfigLine
        {
            RawText = raw,
            Kind = SshLineKind.Directive,
            Keyword = keyword,
            Values = values,
        };
    }
}
