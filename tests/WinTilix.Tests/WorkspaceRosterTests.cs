using WinTilix.Session;
using Xunit;

namespace WinTilix.Tests;

public class WorkspaceRosterTests
{
    [Fact]
    public void CloseLastTab_CreatesReplacementAndKeepsOneActive()
    {
        var roster = new WorkspaceRoster<string>();
        roster.Add("shell");
        roster.Activate("shell");

        var active = roster.Close("shell", () => "replacement");

        Assert.Equal(["replacement"], roster);
        Assert.Equal("replacement", active);
        Assert.Equal("replacement", roster.Active);
    }

    [Fact]
    public void CloseActiveTab_SelectsNeighborNotClosedTab()
    {
        var roster = new WorkspaceRoster<string>();
        roster.Add("one");
        roster.Add("two");
        roster.Add("three");
        roster.Activate("two");

        roster.Close("two", () => throw new InvalidOperationException("Should not replace"));

        Assert.Equal(["one", "three"], roster);
        Assert.Equal("three", roster.Active);
    }

    [Fact]
    public void ActivateNextAndPrevious_WrapAround()
    {
        var roster = new WorkspaceRoster<string>();
        roster.Add("a");
        roster.Add("b");
        roster.Add("c");
        roster.Activate("a");

        roster.ActivateNext();
        Assert.Equal("b", roster.Active);
        roster.ActivatePrevious();
        Assert.Equal("a", roster.Active);
        roster.ActivatePrevious();
        Assert.Equal("c", roster.Active);
    }
}
