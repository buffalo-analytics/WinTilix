using WinTilix.Session;
using Xunit;

namespace WinTilix.Tests;

public class SessionHubTests
{
    [Fact]
    public void Broadcasts_to_other_participating_panes_only()
    {
        var hub = new SessionHub { SessionSyncEnabled = true };
        var a = new FakePane("a");
        var b = new FakePane("b");
        var c = new FakePane("c") { ParticipateInSync = false };
        hub.Register(a);
        hub.Register(b);
        hub.Register(c);

        hub.BroadcastInput("a", "hostname\r");

        Assert.Empty(a.Received);
        Assert.Equal(["hostname\r"], b.Received);
        Assert.Empty(c.Received);
    }

    [Fact]
    public void Does_not_broadcast_when_session_sync_is_off()
    {
        var hub = new SessionHub();
        var a = new FakePane("a");
        var b = new FakePane("b");
        hub.Register(a);
        hub.Register(b);

        hub.BroadcastInput("a", "ls\r");

        Assert.Empty(b.Received);
    }

    private sealed class FakePane : ISyncTarget
    {
        public FakePane(string id) => PaneId = id;
        public string PaneId { get; }
        public bool ParticipateInSync { get; set; } = true;
        public List<string> Received { get; } = [];
        public void ReceiveSyncedInput(string data) => Received.Add(data);
    }
}
