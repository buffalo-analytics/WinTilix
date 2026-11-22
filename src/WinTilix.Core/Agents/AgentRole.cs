using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinTilix.Agents;

public enum AgentRoleKind
{
    Unknown,
    Coordinator,
    Reviewer,
    Backend,
    Ui,
    Builder,
    Qa,
}

/// <summary>
/// Maps an agent name (e.g. "davidp-12") to a development role, and a role to a colour.
/// Roles live in %LOCALAPPDATA%\WinTilix\agent-roles.json as {"davidp-12": "COORDINATOR", ...}
/// because the team roster is rewritten by hand after every restart.
/// </summary>
public static class AgentRoleCatalog
{
    public static string RolesPath =>
        Path.Combine(AppDataRoot, "WinTilix", "agent-roles.json");

    private static string AppDataRoot =>
        Environment.GetEnvironmentVariable("LOCALAPPDATA")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static IReadOnlyDictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(RolesPath))
            {
                return new Dictionary<string, string>();
            }

            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(RolesPath));
            return new Dictionary<string, string>(raw ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Records the role of a launched agent so its badge is coloured straight away.</summary>
    public static void SetRoles(IReadOnlyDictionary<string, string> additions)
    {
        var roles = new Dictionary<string, string>(Load(), StringComparer.OrdinalIgnoreCase);
        foreach (var (name, role) in additions)
        {
            roles[name] = role;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(RolesPath)!);
        File.WriteAllText(
            RolesPath,
            JsonSerializer.Serialize(roles, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static AgentRoleKind Classify(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return AgentRoleKind.Unknown;
        }

        if (Has(role, "coordinator"))
        {
            return AgentRoleKind.Coordinator;
        }

        if (Has(role, "qa"))
        {
            return AgentRoleKind.Qa;
        }

        if (Has(role, "gate") || Has(role, "reviewer"))
        {
            return AgentRoleKind.Reviewer;
        }

        if (Has(role, "backend"))
        {
            return AgentRoleKind.Backend;
        }

        if (Has(role, "ui"))
        {
            return AgentRoleKind.Ui;
        }

        return Has(role, "builder") ? AgentRoleKind.Builder : AgentRoleKind.Unknown;
    }

    public static string ColorsPath =>
        Path.Combine(AppDataRoot, "WinTilix", "agent-colors.json");

    /// <summary>The user's colour for the role, falling back to the built-in default.</summary>
    public static string ColorHex(AgentRoleKind kind)
    {
        var overrides = LoadColorOverrides();
        return overrides.TryGetValue(kind, out var hex) ? hex : DefaultColorHex(kind);
    }

    public static IReadOnlyDictionary<AgentRoleKind, string> LoadColorOverrides()
    {
        var result = new Dictionary<AgentRoleKind, string>();
        try
        {
            if (File.Exists(ColorsPath))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ColorsPath));
                foreach (var (key, value) in raw ?? [])
                {
                    if (Enum.TryParse<AgentRoleKind>(key, ignoreCase: true, out var kind) &&
                        TryNormalizeHex(value, out var hex))
                    {
                        result[kind] = hex;
                    }
                }
            }
        }
        catch
        {
            // A hand-edited or half-written file falls back to the defaults.
        }

        return result;
    }

    /// <summary>Stores only colours that differ from the defaults; an empty set removes the file.</summary>
    public static void SaveColors(IReadOnlyDictionary<AgentRoleKind, string> colors)
    {
        var changed = colors
            .Where(pair => TryNormalizeHex(pair.Value, out var hex) &&
                           !string.Equals(hex, DefaultColorHex(pair.Key), StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key.ToString().ToLowerInvariant(), pair => NormalizeHex(pair.Value));

        if (changed.Count == 0)
        {
            if (File.Exists(ColorsPath))
            {
                File.Delete(ColorsPath);
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ColorsPath)!);
        File.WriteAllText(
            ColorsPath,
            JsonSerializer.Serialize(changed, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool TryNormalizeHex(string? value, out string hex)
    {
        hex = "";
        if (string.IsNullOrWhiteSpace(value) ||
            !Regex.IsMatch(value.Trim(), "^#?[0-9a-fA-F]{6}$"))
        {
            return false;
        }

        hex = NormalizeHex(value);
        return true;
    }

    private static string NormalizeHex(string value) => "#" + value.Trim().TrimStart('#').ToUpperInvariant();

    public static string DefaultColorHex(AgentRoleKind kind) => kind switch
    {
        AgentRoleKind.Coordinator => "#E3B341",
        AgentRoleKind.Reviewer => "#B392F0",
        AgentRoleKind.Backend => "#58A6FF",
        AgentRoleKind.Ui => "#F778BA",
        AgentRoleKind.Builder => "#7EE787",
        AgentRoleKind.Qa => "#FFA657",
        _ => "#8B949E",
    };

    private static bool Has(string role, string word) =>
        Regex.IsMatch(role, $@"\b{word}\b", RegexOptions.IgnoreCase);
}
