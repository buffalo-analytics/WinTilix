using System.Reflection;
using System.Text.Json;

namespace WinTilix.Agents;

/// <summary>
/// Ready-made groups that ship inside the app: a software team, a mechanical and mechatronic
/// engineering team and a scientific veterinary review team, each in a full and a "(core)" size.
/// They are generated from the wintilix-groups skill specs (see tools/wintilix-groups-skill) and
/// embedded as JSON, so they are available on every install without touching the saved-groups file.
/// A template keeps a stable id, so saving an edited copy replaces the saved copy of the same group.
/// </summary>
public static class AgentGroupTemplates
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static IReadOnlyList<AgentGroupDefinition>? _all;

    public static IReadOnlyList<AgentGroupDefinition> All() => _all ??= Load();

    /// <summary>A copy that can be edited freely without changing the shared template.</summary>
    public static AgentGroupDefinition Copy(AgentGroupDefinition template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        FallbackModel = template.FallbackModel,
        UseWorktrees = template.UseWorktrees,
        Agents = template.Agents
            .Select(agent => new AgentLaunchSpec
            {
                Name = agent.Name,
                Role = agent.Role,
                Model = agent.Model,
                Folder = agent.Folder,
                Prompt = agent.Prompt,
                FirstMessage = agent.FirstMessage,
            })
            .ToList(),
    };

    private static IReadOnlyList<AgentGroupDefinition> Load()
    {
        var assembly = typeof(AgentGroupTemplates).Assembly;
        var groups = new List<AgentGroupDefinition>();
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(name => name.Contains(".Agents.Templates.", StringComparison.Ordinal) &&
                                    name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream is not null &&
                    JsonSerializer.Deserialize<AgentGroupDefinition>(stream, JsonOptions) is { } group &&
                    group.Agents.Count > 0)
                {
                    groups.Add(group);
                }
            }
            catch (JsonException)
            {
                // A damaged template is skipped so the rest still load.
            }
        }

        return groups.OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
