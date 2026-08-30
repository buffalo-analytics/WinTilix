using WinTilix.Controls;
using WinTilix.Layout;
using WinTilix.Terminal;

namespace WinTilix.Session;

public sealed class WorkspaceTab : IDisposable
{
    public WorkspaceTab(string title, string? clusterId = null)
    {
        Title = title;
        ClusterId = clusterId;
        Hub = new SessionHub();
        Tiling = new TilingHost();
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; }
    public string? ClusterId { get; set; }
    public SessionHub Hub { get; }
    public TilingHost Tiling { get; }
    public TerminalPane? FocusedPane { get; set; }

    public void Deactivate() => SetInputEnabled(false);

    public void Activate()
    {
        SetInputEnabled(true);
        foreach (var pane in Tiling.Panes)
        {
            pane.RequestTerminalFit();
        }

        FocusedPane ??= Tiling.Panes.FirstOrDefault();
        FocusedPane?.FocusTerminal();
    }

    public void SetInputEnabled(bool enabled)
    {
        Tiling.IsHitTestVisible = enabled;
        Tiling.Focusable = enabled;
        foreach (var pane in Tiling.Panes)
        {
            pane.SetAcceptsInput(enabled);
        }
    }

    public void Dispose() => Tiling.DisposeAll();
}
