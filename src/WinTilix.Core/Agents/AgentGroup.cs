using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinTilix.Agents;

public sealed class AgentLaunchSpec
{
    /// <summary>Session name passed as <c>claude -n</c>; also the key for the role badge.</summary>
    public string Name { get; set; } = "";

    /// <summary>One of the <see cref="AgentRoleKind"/> names; picks the badge colour.</summary>
    public string Role { get; set; } = nameof(AgentRoleKind.Builder);

    public string Model { get; set; } = "sonnet";
    public string Folder { get; set; } = "";

    /// <summary>Appended to Claude's system prompt, so the role holds for the whole session.</summary>
    public string Prompt { get; set; } = "";

    /// <summary>Optional opening message; empty leaves the agent waiting for input.</summary>
    public string FirstMessage { get; set; } = "";
}

public sealed class AgentGroupDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string FallbackModel { get; set; } = "";
    public bool UseWorktrees { get; set; }
    public List<AgentLaunchSpec> Agents { get; set; } = [];
}

public static class AgentGroupPresets
{
    public static List<AgentLaunchSpec> DefaultAgents() =>
    [
        new()
        {
            Name = "coordinator", Role = nameof(AgentRoleKind.Coordinator), Model = "opus",
            Prompt = "You are the swarm coordinator. Own the task board, assign work to the other agents and keep everyone unblocked.",
        },
        new()
        {
            Name = "backend", Role = nameof(AgentRoleKind.Backend), Model = "sonnet",
            Prompt = "You are the backend builder. Work only on tasks the coordinator assigns you.",
        },
        new()
        {
            Name = "ui", Role = nameof(AgentRoleKind.Ui), Model = "sonnet",
            Prompt = "You are the UI builder. Work only on tasks the coordinator assigns you.",
        },
        new()
        {
            Name = "reviewer", Role = nameof(AgentRoleKind.Reviewer), Model = "opus",
            Prompt = "You are the reviewer. You are read-only: review branches the coordinator names and report findings; never edit, commit or push.",
        },
        new()
        {
            Name = "qa", Role = nameof(AgentRoleKind.Qa), Model = "sonnet",
            Prompt = "You are the QA tester. Check the branches the coordinator names and report what you find; do not edit code.",
        },
    ];
}

public static class AgentGroupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string StorePath =>
        Path.Combine(
            Environment.GetEnvironmentVariable("LOCALAPPDATA")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinTilix",
            "agent-groups.json");

    public static IReadOnlyList<AgentGroupDefinition> LoadAll()
    {
        try
        {
            return File.Exists(StorePath)
                ? JsonSerializer.Deserialize<List<AgentGroupDefinition>>(File.ReadAllText(StorePath), JsonOptions) ?? []
                : [];
        }
        catch
        {
            return [];
        }
    }

    public static AgentGroupDefinition Save(AgentGroupDefinition group)
    {
        group.Name = group.Name.Trim();
        if (group.Name.Length == 0)
        {
            throw new ArgumentException("Group name is required.", nameof(group));
        }

        if (group.Agents.Count == 0)
        {
            throw new ArgumentException("Add at least one agent.", nameof(group));
        }

        var groups = LoadAll().Where(existing => existing.Id != group.Id).ToList();
        groups.Add(group);
        Write(groups.OrderBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase).ToList());
        return group;
    }

    public static void Delete(string id) =>
        Write(LoadAll().Where(existing => existing.Id != id).ToList());

    private static void Write(List<AgentGroupDefinition> groups)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(groups, JsonOptions));
    }
}

public static class ClaudeLauncher
{
    /// <summary>Names must be unique per group so each badge maps to one agent: builder, builder-2, ...</summary>
    public static List<AgentLaunchSpec> WithUniqueNames(IEnumerable<AgentLaunchSpec> specs)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AgentLaunchSpec>();
        foreach (var spec in specs)
        {
            var baseName = Regex.Replace(spec.Name.Trim(), @"[^\w.-]+", "-").Trim('-');
            if (baseName.Length == 0)
            {
                baseName = spec.Role.ToLowerInvariant();
            }

            var name = baseName;
            for (var n = 2; !used.Add(name); n++)
            {
                name = $"{baseName}-{n}";
            }

            result.Add(new AgentLaunchSpec
            {
                Name = name,
                Role = spec.Role,
                Model = spec.Model,
                Folder = spec.Folder,
                Prompt = spec.Prompt,
                FirstMessage = spec.FirstMessage,
            });
        }

        return result;
    }

    /// <summary>The PowerShell script that enters the folder and starts Claude Code.</summary>
    public static string BuildScript(AgentLaunchSpec spec, string folder, string? fallbackModel)
    {
        var script = new StringBuilder();
        script.AppendLine($"Set-Location -LiteralPath {Quote(folder)}");
        script.Append($"& claude -n {Quote(spec.Name)}");
        if (!string.IsNullOrWhiteSpace(spec.Model))
        {
            script.Append($" --model {Quote(spec.Model.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(fallbackModel))
        {
            script.Append($" --fallback-model {Quote(fallbackModel.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(spec.Prompt))
        {
            script.Append($" --append-system-prompt {Quote(spec.Prompt.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(spec.FirstMessage))
        {
            script.Append($" {Quote(spec.FirstMessage.Trim())}");
        }

        return script.ToString();
    }

    /// <summary>
    /// A command line for a pane. The script travels as -EncodedCommand so prompts can hold any
    /// quotes; -NoExit keeps the tile (and its shell) open after Claude exits.
    /// </summary>
    public static string BuildCommandLine(string script, string shellExecutable)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return $"{WinTilix.Terminal.ShellResolver.Quote(shellExecutable)} -NoLogo -NoExit -EncodedCommand {encoded}";
    }

    public static string PowerShellExecutable()
    {
        var pwsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        return File.Exists(pwsh)
            ? pwsh
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
    }

    // PowerShell treats straight and typographic single quotes alike, so double all of them.
    public static string Quote(string value) =>
        "'" + Regex.Replace(value, "['‘’‚‛]", "$0$0") + "'";

    /// <summary>Adds a git worktree beside the repository so agents never share a checkout.</summary>
    public static (string? Path, string? Error) TryCreateWorktree(string folder, string name)
    {
        var top = RunGit(folder, "rev-parse --show-toplevel");
        if (top.ExitCode != 0)
        {
            return (null, $"{folder} is not a git repository");
        }

        var root = top.Output.Trim().Replace('/', Path.DirectorySeparatorChar);
        var path = root + "-" + name;
        var branch = "agent/" + name;
        var add = Directory.Exists(path)
            ? (ExitCode: 0, Output: "")
            : RunGit(root, $"worktree add -b \"{branch}\" \"{path}\"");
        return add.ExitCode == 0 ? (path, null) : (null, add.Output.Trim());
    }

    private static (int ExitCode, string Output) RunGit(string workingDirectory, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            return process.WaitForExit(30000) ? (process.ExitCode, output) : (-1, "git timed out");
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
