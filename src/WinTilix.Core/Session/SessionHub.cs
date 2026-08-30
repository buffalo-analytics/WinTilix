namespace WinTilix.Session;

/// <summary>
/// Session-scoped input sync, matching Tilix:
/// session.d onTerminalSyncInput fans a SyncInputEvent to every other pane
/// whose synchronizeInput flag is on.
/// </summary>
public sealed class SessionHub
{
    private readonly object _gate = new();
    private readonly List<ISyncTarget> _targets = [];
    private bool _sessionSyncEnabled;

    public bool SessionSyncEnabled
    {
        get => _sessionSyncEnabled;
        set
        {
            if (_sessionSyncEnabled == value)
            {
                return;
            }

            _sessionSyncEnabled = value;
            SyncModeChanged?.Invoke();
        }
    }

    public event Action? SyncModeChanged;

    public void Register(ISyncTarget target)
    {
        lock (_gate)
        {
            _targets.Add(target);
        }
    }

    public void Unregister(ISyncTarget target)
    {
        lock (_gate)
        {
            _targets.Remove(target);
        }
    }

    public IReadOnlyList<ISyncTarget> Snapshot()
    {
        lock (_gate)
        {
            return _targets.ToArray();
        }
    }

    public void BroadcastInput(string senderId, string data)
    {
        if (!_sessionSyncEnabled || string.IsNullOrEmpty(data))
        {
            return;
        }

        foreach (var target in Snapshot())
        {
            if (target.PaneId == senderId || !target.ParticipateInSync)
            {
                continue;
            }

            target.ReceiveSyncedInput(data);
        }
    }
}

public interface ISyncTarget
{
    string PaneId { get; }
    bool ParticipateInSync { get; }
    void ReceiveSyncedInput(string data);
}
