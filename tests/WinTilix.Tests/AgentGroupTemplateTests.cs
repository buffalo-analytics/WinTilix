using System.Text.RegularExpressions;
using WinTilix.Agents;
using Xunit;

namespace WinTilix.Tests;

public class AgentGroupTemplateTests
{
    [Fact]
    public void Ships_three_teams_in_a_full_and_a_core_size()
    {
        var names = AgentGroupTemplates.All().Select(group => group.Name).ToList();

        Assert.Equal(6, names.Count);
        Assert.Contains("Software Dev Team", names);
        Assert.Contains("Mechanical and Mechatronic Engineering", names);
        Assert.Contains("Scientific Veterinary Review", names);
        Assert.Contains("Software Dev Team (core)", names);
        Assert.Contains("Mechanical and Mechatronic Engineering (core)", names);
        Assert.Contains("Scientific Veterinary Review (core)", names);
        Assert.Equal(names.Count, AgentGroupTemplates.All().Select(group => group.Id).Distinct().Count());
    }

    [Fact]
    public void Every_template_has_the_backbone_roster()
    {
        foreach (var group in AgentGroupTemplates.All())
        {
            var kinds = group.Agents.Select(agent => agent.Role).ToList();
            Assert.True(kinds.Count(kind => kind == nameof(AgentRoleKind.Coordinator)) == 1, group.Name + ": one coordinator");
            Assert.Contains(nameof(AgentRoleKind.Qa), kinds);
            Assert.Contains(nameof(AgentRoleKind.Reviewer), kinds);
            Assert.Contains(kinds, kind => kind is nameof(AgentRoleKind.Builder) or nameof(AgentRoleKind.Backend) or nameof(AgentRoleKind.Ui));
        }
    }

    [Fact]
    public void Roles_names_and_prompts_fit_the_group_window_and_the_command_line()
    {
        foreach (var group in AgentGroupTemplates.All())
        {
            var names = group.Agents.Select(agent => agent.Name).ToList();
            Assert.Equal(names.Count, names.Select(name => name.ToLowerInvariant()).Distinct().Count());
            foreach (var agent in group.Agents)
            {
                Assert.True(Enum.TryParse<AgentRoleKind>(agent.Role, out var kind) && kind != AgentRoleKind.Unknown, $"{group.Name}/{agent.Name}: role {agent.Role}");
                Assert.Matches(new Regex(@"^[\w.-]+$"), agent.Name);
                Assert.False(string.IsNullOrWhiteSpace(agent.Prompt));

                var script = ClaudeLauncher.BuildScript(agent, @"C:\Users\someone", group.FallbackModel);
                var commandLine = ClaudeLauncher.BuildCommandLine(script, @"C:\Program Files\PowerShell\7\pwsh.exe");
                Assert.True(commandLine.Length < 30000, $"{group.Name}/{agent.Name}: command line is {commandLine.Length} characters");
            }
        }
    }

    [Fact]
    public void Core_templates_leave_out_only_optional_seats()
    {
        var all = AgentGroupTemplates.All();
        foreach (var core in all.Where(group => group.Name.EndsWith(" (core)", StringComparison.Ordinal)))
        {
            var full = all.Single(group => group.Name == core.Name[..^" (core)".Length]);
            Assert.True(core.Agents.Count < full.Agents.Count, core.Name);
            Assert.All(core.Agents, agent => Assert.Contains(full.Agents, other => other.Name == agent.Name));
        }
    }

    [Fact]
    public void Copy_is_independent_of_the_template()
    {
        var template = AgentGroupTemplates.All()[0];
        var copy = AgentGroupTemplates.Copy(template);

        copy.Agents[0].Prompt = "changed";
        copy.Agents.RemoveAt(1);

        Assert.NotEqual("changed", template.Agents[0].Prompt);
        Assert.True(template.Agents.Count > copy.Agents.Count);
        Assert.Equal(template.Id, copy.Id);
    }
}
