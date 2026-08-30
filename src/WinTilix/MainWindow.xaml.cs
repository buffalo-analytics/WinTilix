using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinTilix.Controls;
using WinTilix.Layout;
using WinTilix.Session;
using WinTilix.Terminal;

namespace WinTilix;

public partial class MainWindow : Window
{
    private readonly SessionHub _hub = new();
    private readonly TilingHost _tiling = new();
    private TerminalPane? _focused;

    public ICommand SplitRightCommand { get; }
    public ICommand SplitDownCommand { get; }
    public ICommand ClosePaneCommand { get; }
    public ICommand ToggleSyncCommand { get; }
    public ICommand NewShellCommand { get; }

    public MainWindow()
    {
        SplitRightCommand = new RelayCommand(() => SplitFocused(Orientation.Horizontal));
        SplitDownCommand = new RelayCommand(() => SplitFocused(Orientation.Vertical));
        ClosePaneCommand = new RelayCommand(CloseFocused);
        ToggleSyncCommand = new RelayCommand(ToggleSync);
        NewShellCommand = new RelayCommand(() => SplitFocused(Orientation.Horizontal));
        DataContext = this;
        InitializeComponent();
        RootHost.Content = _tiling;
        var first = CreatePane(ShellResolver.DefaultShellCommand(), "Shell");
        _tiling.SetRoot(first);
        _hub.SyncModeChanged += () => Dispatcher.BeginInvoke(() => SyncToggle.IsChecked = _hub.SessionSyncEnabled);
        Closed += (_, _) => _tiling.DisposeAll();
    }

    private TerminalPane CreatePane(string commandLine, string title)
    {
        var pane = new TerminalPane(_hub, commandLine, title);
        pane.SplitRequested += (source, orientation) => Split(source, orientation);
        pane.CloseRequested += ClosePane;
        pane.Activated += p => _focused = p;
        _focused ??= pane;
        return pane;
    }

    private void SplitRight_Click(object sender, RoutedEventArgs e) => SplitFocused(Orientation.Horizontal);

    private void SplitDown_Click(object sender, RoutedEventArgs e) => SplitFocused(Orientation.Vertical);

    private void NewShell_Click(object sender, RoutedEventArgs e) => SplitFocused(Orientation.Horizontal);

    private void ClosePane_Click(object sender, RoutedEventArgs e) => CloseFocused();

    private void SyncToggle_Click(object sender, RoutedEventArgs e)
    {
        _hub.SessionSyncEnabled = SyncToggle.IsChecked == true;
        StatusText.Text = _hub.SessionSyncEnabled
            ? "Synchronized input is on. Keyboard icon on a pane includes/excludes it."
            : "Synchronized input is off.";
    }

    private void ToggleSync()
    {
        _hub.SessionSyncEnabled = !_hub.SessionSyncEnabled;
    }

    private void Cluster_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ClusterWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var panes = dialog.Hosts
            .Select(host => CreatePane(ShellResolver.SshCommand(host), host))
            .ToList();
        _tiling.ReplaceAll(panes);
        _focused = panes[0];
        _hub.SessionSyncEnabled = true;
        StatusText.Text = $"Cluster of {panes.Count} SSH panes. Type in one pane to drive them all.";
    }

    private void SplitFocused(Orientation orientation)
    {
        var pane = _focused ?? _tiling.Panes.FirstOrDefault();
        if (pane != null)
        {
            Split(pane, orientation);
        }
    }

    private void Split(TerminalPane source, Orientation orientation)
    {
        var newbie = CreatePane(ShellResolver.DefaultShellCommand(), "Shell");
        _tiling.Split(source, orientation, newbie);
        _focused = newbie;
    }

    private void CloseFocused()
    {
        var pane = _focused ?? _tiling.Panes.LastOrDefault();
        if (pane != null)
        {
            ClosePane(pane);
        }
    }

    private void ClosePane(TerminalPane pane)
    {
        if (_tiling.Close(pane) && _focused == pane)
        {
            _focused = _tiling.Panes.FirstOrDefault();
        }
    }
}

internal sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
