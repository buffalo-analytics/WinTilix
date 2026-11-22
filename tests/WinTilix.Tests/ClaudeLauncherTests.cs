using System.Text;
using WinTilix.Agents;
using Xunit;

namespace WinTilix.Tests;

[Collection("LocalAppData")]
public class ClaudeLauncherTests : IDisposable
{
    private readonly string? _previous = Environment.GetEnvironmentVariable("LOCALAPPDATA");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WinTilix-tests-" + Guid.NewGuid().ToString("N"));

    public ClaudeLauncherTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _previous);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void BuildScript_PassesNameModelFallbackPromptAndFirstMessage()
    {
        var spec = new AgentLaunchSpec
        {
            Name = "coordinator",
            Model = "opus",
            Prompt = "You are the swarm coordinator.",
            FirstMessage = "Start.",
        };

        var script = ClaudeLauncher.BuildScript(spec, @"C:\work", "sonnet");

        Assert.Equal(
            "Set-Location -LiteralPath 'C:\\work'\r\n".Replace("\r\n", Environment.NewLine) +
            "& claude -n 'coordinator' --model 'opus' --fallback-model 'sonnet' " +
            "--append-system-prompt 'You are the swarm coordinator.' 'Start.'",
            script);
    }

    [Fact]
    public void BuildScript_OmitsEmptyOptions()
    {
        var script = ClaudeLauncher.BuildScript(new AgentLaunchSpec { Name = "qa", Model = "" }, @"C:\w", null);

        Assert.EndsWith("& claude -n 'qa'", script);
    }

    [Theory]
    [InlineData("it's", "'it''s'")]
    [InlineData("\u2018smart\u2019", "'\u2018\u2018smart\u2019\u2019'")]
    public void Quote_DoublesEveryKindOfSingleQuote(string value, string expected) =>
        Assert.Equal(expected, ClaudeLauncher.Quote(value));

    [Fact]
    public void BuildCommandLine_EncodesTheScriptAndHasNoSpacesInIt()
    {
        var script = "Write-Host \"it's fine\"";

        var line = ClaudeLauncher.BuildCommandLine(script, @"C:\Program Files\PowerShell\7\pwsh.exe");

        Assert.StartsWith("\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -NoLogo -NoExit -EncodedCommand ", line);
        var encoded = line[(line.LastIndexOf(' ') + 1)..];
        Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
    }

    [Fact]
    public void WithUniqueNames_SuffixesDuplicatesAndFillsBlanks()
    {
        var result = ClaudeLauncher.WithUniqueNames(
        [
            new AgentLaunchSpec { Name = "builder", Role = "Builder" },
            new AgentLaunchSpec { Name = "Builder", Role = "Builder" },
            new AgentLaunchSpec { Name = " ", Role = "Qa" },
            new AgentLaunchSpec { Name = "my agent!", Role = "Ui" },
        ]);

        Assert.Equal(["builder", "Builder-2", "qa", "my-agent"], result.Select(spec => spec.Name));
    }

    [Fact]
    public void GroupStore_RoundTripsAndDeletes()
    {
        var group = AgentGroupStore.Save(new AgentGroupDefinition
        {
            Name = "Swarm",
            FallbackModel = "sonnet",
            UseWorktrees = true,
            Agents = AgentGroupPresets.DefaultAgents(),
        });

        var loaded = Assert.Single(AgentGroupStore.LoadAll());
        Assert.Equal("Swarm", loaded.Name);
        Assert.True(loaded.UseWorktrees);
        Assert.Equal("opus", loaded.Agents[0].Model);

        AgentGroupStore.Delete(group.Id);
        Assert.Empty(AgentGroupStore.LoadAll());
    }

    [Fact]
    public void GroupStore_RejectsEmptyNameAndNoAgents()
    {
        Assert.Throws<ArgumentException>(() => AgentGroupStore.Save(
            new AgentGroupDefinition { Name = " ", Agents = AgentGroupPresets.DefaultAgents() }));
        Assert.Throws<ArgumentException>(() => AgentGroupStore.Save(new AgentGroupDefinition { Name = "x" }));
    }

    [Fact]
    public void SetRoles_MergesIntoExistingRolesFile()
    {
        AgentRoleCatalog.SetRoles(new Dictionary<string, string> { ["coordinator"] = "Coordinator" });
        AgentRoleCatalog.SetRoles(new Dictionary<string, string> { ["qa"] = "Qa" });

        var roles = AgentRoleCatalog.Load();
        Assert.Equal("Coordinator", roles["coordinator"]);
        Assert.Equal("Qa", roles["qa"]);
    }

    [Fact]
    public void PresetRoles_ClassifyToTheirOwnKind()
    {
        foreach (var spec in AgentGroupPresets.DefaultAgents())
        {
            Assert.Equal(spec.Role, AgentRoleCatalog.Classify(spec.Role).ToString());
        }
    }
}
