using WinTilix.Agents;
using Xunit;

namespace WinTilix.Tests;

public class AgentLocatorTests
{
    [Theory]
    [InlineData("COORDINATOR + lims.earth deploy owner [98]", AgentRoleKind.Coordinator)]
    [InlineData("GATE REVIEWER, read-only [ed]", AgentRoleKind.Reviewer)]
    [InlineData("SECOND GATE REVIEWER, read-only", AgentRoleKind.Reviewer)]
    [InlineData("BACKEND [64]: Telegram, ingest", AgentRoleKind.Backend)]
    [InlineData("UI + DEPLOY SCRIPT [c3]", AgentRoleKind.Ui)]
    [InlineData("HMI BUILDER [0a]", AgentRoleKind.Builder)]
    [InlineData("BUILDER, standby [new]", AgentRoleKind.Builder)]
    [InlineData("UX QA, review-only [60]", AgentRoleKind.Qa)]
    [InlineData("build things", AgentRoleKind.Unknown)]
    [InlineData(null, AgentRoleKind.Unknown)]
    public void Classify_MapsRoleTextToKind(string? role, AgentRoleKind expected) =>
        Assert.Equal(expected, AgentRoleCatalog.Classify(role));

    [Fact]
    public void ColorHex_GivesEveryKindADistinctColour()
    {
        var colours = Enum.GetValues<AgentRoleKind>().Select(AgentRoleCatalog.ColorHex).ToList();
        Assert.Equal(colours.Count, colours.Distinct().Count());
    }

    [Fact]
    public void FindSessionPid_PrefersTheNearestDescendant()
    {
        var children = new Dictionary<int, List<int>>
        {
            [1] = [2],
            [2] = [3, 4],
            [3] = [5],
        };

        var found = AgentLocator.FindSessionPid(1, pid => pid is 5 or 4, children);

        Assert.Equal(4, found);
    }

    [Fact]
    public void FindSessionPid_ReturnsNullWhenNoSessionInTree()
    {
        var children = new Dictionary<int, List<int>> { [1] = [2] };

        Assert.Null(AgentLocator.FindSessionPid(1, _ => false, children));
    }

    [Fact]
    public void ReadSessions_ReadsNameAndStatusAndSkipsBadFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "WinTilix-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "10.json"), """{"pid":10,"name":"davidp-12","status":"busy"}""");
            File.WriteAllText(Path.Combine(dir, "11.json"), "{ not json");
            File.WriteAllText(Path.Combine(dir, "12.json"), """{"pid":12}""");

            var sessions = AgentLocator.ReadSessions(dir);

            Assert.Equal(("davidp-12", "busy"), sessions[10]);
            Assert.Single(sessions);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
