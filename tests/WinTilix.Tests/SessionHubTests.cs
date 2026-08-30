using WinTilix.Session;
using Xunit;

namespace WinTilix.Tests;

public class SessionHubTests
{
    [Fact]
    public void Broadcast_DoesNotCrossHubs()
    {
        var hubA = new SessionHub { SessionSyncEnabled = true };
        var hubB = new SessionHub { SessionSyncEnabled = true };
        var a1 = new FakeTarget("a1");
        var a2 = new FakeTarget("a2");
        var b1 = new FakeTarget("b1");
        hubA.Register(a1);
        hubA.Register(a2);
        hubB.Register(b1);

        hubA.BroadcastInput("a1", "hello");

        Assert.Equal(["hello"], a2.Received);
        Assert.Empty(b1.Received);
    }

    [Fact]
    public void Broadcast_StaysInsideEnabledHubOnly()
    {
        var active = new SessionHub { SessionSyncEnabled = true };
        var background = new SessionHub { SessionSyncEnabled = true };
        var activePeer = new FakeTarget("a2");
        var backgroundPeer = new FakeTarget("b2");
        active.Register(new FakeTarget("a1"));
        active.Register(activePeer);
        background.Register(new FakeTarget("b1"));
        background.Register(backgroundPeer);

        active.BroadcastInput("a1", "typed-in-active-tab");
        background.BroadcastInput("b1", "");

        Assert.Equal(["typed-in-active-tab"], activePeer.Received);
        Assert.Empty(backgroundPeer.Received);
    }

    private sealed class FakeTarget(string id) : ISyncTarget
    {
        public string PaneId { get; } = id;
        public bool ParticipateInSync => true;
        public List<string> Received { get; } = [];
        public void ReceiveSyncedInput(string data) => Received.Add(data);
    }
}
