namespace WinTilix.Terminal;

public static class SshConfigReader
{
    public static string UserConfigPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".ssh", "config");
        }
    }

    public static IReadOnlyList<string> ReadUserHosts()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return ReadHosts(UserConfigPath, home);
    }

    public static IReadOnlyList<string> ReadHosts(string configPath, string homeDirectory)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ReadFile(Path.GetFullPath(configPath), Path.GetFullPath(homeDirectory), aliases, visited);
        return aliases.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> ReadUserConfigFiles()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return ReadConfigFiles(UserConfigPath, home);
    }

    public static IReadOnlyList<string> ReadConfigFiles(string configPath, string homeDirectory)
    {
        var root = Path.GetFullPath(configPath);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        ReadFile(root, Path.GetFullPath(homeDirectory), aliases, visited, files);

        if (!files.Contains(root, StringComparer.OrdinalIgnoreCase))
        {
            files.Insert(0, root);
        }

        return files;
    }

    private static void ReadFile(
        string path,
        string homeDirectory,
        HashSet<string> aliases,
        HashSet<string> visited,
        List<string>? files = null)
    {
        if (!visited.Add(path) || !File.Exists(path))
        {
            return;
        }

        files?.Add(path);

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var rawLine in lines)
        {
            var tokens = SshConfigTokenizer.Tokenize(rawLine);
            if (tokens.Count < 2)
            {
                continue;
            }

            if (tokens[0].Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var alias in tokens.Skip(1).Where(SshConfigTokenizer.IsSelectableAlias))
                {
                    aliases.Add(alias);
                }
            }
            else if (tokens[0].Equals("Include", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var include in tokens.Skip(1))
                {
                    foreach (var includedPath in ExpandInclude(include, homeDirectory))
                    {
                        ReadFile(includedPath, homeDirectory, aliases, visited, files);
                    }
                }
            }
        }
    }

    private static IEnumerable<string> ExpandInclude(string include, string homeDirectory)
    {
        var expanded = include
            .Replace("%d", homeDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace('/', Path.DirectorySeparatorChar);

        if (expanded == "~")
        {
            expanded = homeDirectory;
        }
        else if (expanded.StartsWith($"~{Path.DirectorySeparatorChar}"))
        {
            expanded = Path.Combine(homeDirectory, expanded[2..]);
        }

        if (!Path.IsPathRooted(expanded))
        {
            expanded = Path.Combine(homeDirectory, ".ssh", expanded);
        }

        var directory = Path.GetDirectoryName(expanded);
        var pattern = Path.GetFileName(expanded);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(pattern))
        {
            yield break;
        }

        if (pattern.IndexOfAny(['*', '?']) < 0)
        {
            yield return Path.GetFullPath(expanded);
            yield break;
        }

        if (!Directory.Exists(directory))
        {
            yield break;
        }

        IEnumerable<string> matches;
        try
        {
            matches = Directory.EnumerateFiles(directory, pattern)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var match in matches)
        {
            yield return Path.GetFullPath(match);
        }
    }
}
