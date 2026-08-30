using System.Collections.ObjectModel;
using System.Windows.Controls;
using WinTilix.Controls;
using WinTilix.Terminal;
using Orientation = System.Windows.Controls.Orientation;

namespace WinTilix.Session;

public sealed class WorkspaceManager : IDisposable
{
    private readonly Func<SessionHub, string, string, TerminalPane> _createPane;
    private readonly WorkspaceRoster<WorkspaceTab> _tabs = [];

    public WorkspaceManager(Func<SessionHub, string, string, TerminalPane> createPane)
    {
        _createPane = createPane;
        Tabs = new ReadOnlyObservableCollection<WorkspaceTab>(_tabs);
    }

    public ReadOnlyObservableCollection<WorkspaceTab> Tabs { get; }
    public WorkspaceTab? Active => _tabs.Active;
    public event Action? ActiveChanged;

    public WorkspaceTab EnsureShellWorkspace()
    {
        if (_tabs.Count == 0)
        {
            return AddShellWorkspace();
        }

        if (Active is null)
        {
            Activate(_tabs[0]);
        }

        return Active!;
    }

    public WorkspaceTab AddShellWorkspace()
    {
        var tab = CreateShellTab();
        _tabs.Add(tab);
        Activate(tab);
        return tab;
    }

    public WorkspaceTab OpenCluster(SshClusterDefinition cluster)
    {
        if (cluster.Hosts.Count == 0)
        {
            throw new ArgumentException("Add at least one host.", nameof(cluster));
        }

        var tab = new WorkspaceTab(cluster.Name, cluster.Id);
        var panes = cluster.Hosts
            .Select(host =>
            {
                var pane = _createPane(tab.Hub, ShellResolver.SshCommand(host), host);
                WirePane(tab, pane);
                return pane;
            })
            .ToList();
        tab.Tiling.ReplaceAll(panes);
        tab.FocusedPane = panes[0];
        tab.Hub.SessionSyncEnabled = true;
        _tabs.Add(tab);
        Activate(tab);
        return tab;
    }

    public void Activate(WorkspaceTab tab)
    {
        if (!_tabs.Contains(tab))
        {
            return;
        }

        if (!ReferenceEquals(_tabs.Active, tab))
        {
            _tabs.Active?.Deactivate();
            _tabs.Activate(tab);
            ActiveChanged?.Invoke();
        }

        tab.Activate();
    }

    public void ActivateNext()
    {
        if (Active is null || _tabs.Count == 0)
        {
            return;
        }

        var index = _tabs.IndexOf(Active);
        Activate(_tabs[(index + 1) % _tabs.Count]);
    }

    public void ActivatePrevious()
    {
        if (Active is null || _tabs.Count == 0)
        {
            return;
        }

        var index = _tabs.IndexOf(Active);
        Activate(_tabs[(index - 1 + _tabs.Count) % _tabs.Count]);
    }

    public void CloseTab(WorkspaceTab tab)
    {
        if (!_tabs.Contains(tab))
        {
            return;
        }

        var wasActive = ReferenceEquals(_tabs.Active, tab);
        _tabs.Close(tab, CreateShellTab);
        ActiveChanged?.Invoke();
        tab.Dispose();
        if (wasActive)
        {
            _tabs.Active?.Activate();
        }
    }

    public bool ClosePane(TerminalPane pane)
    {
        var tab = _tabs.FirstOrDefault(candidate => candidate.Tiling.Panes.Contains(pane));
        if (tab is null)
        {
            return false;
        }

        var remaining = tab.Tiling.Panes.Count();
        if (remaining <= 1)
        {
            CloseTab(tab);
            return true;
        }

        if (!tab.Tiling.Close(pane))
        {
            return false;
        }

        if (tab.FocusedPane == pane)
        {
            tab.FocusedPane = tab.Tiling.Panes.FirstOrDefault();
        }

        return true;
    }

    public void SplitActive(Orientation orientation)
    {
        var tab = Active;
        if (tab is null)
        {
            return;
        }

        var source = tab.FocusedPane ?? tab.Tiling.Panes.FirstOrDefault();
        if (source is null)
        {
            return;
        }

        var newbie = _createPane(tab.Hub, ShellResolver.DefaultShellCommand(), "Shell");
        WirePane(tab, newbie);
        tab.Tiling.Split(source, orientation, newbie);
        tab.FocusedPane = newbie;
        newbie.SetAcceptsInput(true);
        newbie.FocusTerminal();
    }

    public void Dispose()
    {
        foreach (var tab in _tabs.ToArray())
        {
            tab.Dispose();
        }

        _tabs.Clear();
    }

    private WorkspaceTab CreateShellTab()
    {
        var tab = new WorkspaceTab("Shell");
        var pane = _createPane(tab.Hub, ShellResolver.DefaultShellCommand(), "Shell");
        WirePane(tab, pane);
        tab.Tiling.SetRoot(pane);
        tab.FocusedPane = pane;
        return tab;
    }

    private void WirePane(WorkspaceTab tab, TerminalPane pane)
    {
        pane.Activated += activated =>
        {
            if (ReferenceEquals(Active, tab))
            {
                tab.FocusedPane = activated;
            }
        };
    }
}
