using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinTilix.Controls;
using WinTilix.Diagnostics;
using WinTilix.Session;
using WinTilix.Terminal;
using WinTilix.Updates;

namespace WinTilix;

public partial class MainWindow : Window
{
    private static readonly Version CurrentVersion =
        typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private static readonly TimeSpan UpdatePollInterval = TimeSpan.FromHours(3);

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly GitHubUpdateService _updateService;
    private readonly WindowTheme _windowTheme;
    private readonly WorkspaceManager _workspaces;
    private readonly UpdateCheckCoordinator _updateChecker;
    private readonly DispatcherTimer _updatePollTimer;
    private SessionHub? _syncBoundHub;

    public ICommand SplitRightCommand { get; }
    public ICommand SplitDownCommand { get; }
    public ICommand ClosePaneCommand { get; }
    public ICommand ToggleSyncCommand { get; }
    public ICommand NewShellCommand { get; }
    public ICommand NextTabCommand { get; }
    public ICommand PreviousTabCommand { get; }

    public MainWindow()
    {
        _windowTheme = new WindowTheme(this);
        _updateService = new GitHubUpdateService(_httpClient, "buffalo-analytics", "WinTilix");
        _updateChecker = new UpdateCheckCoordinator(
            token => _updateService.CheckAsync(CurrentVersion, token));
        _updatePollTimer = new DispatcherTimer { Interval = UpdatePollInterval };
        _updatePollTimer.Tick += async (_, _) => await PollForUpdatesAsync();
        _workspaces = new WorkspaceManager(CreatePane);
        _workspaces.ActiveChanged += OnActiveWorkspaceChanged;

        SplitRightCommand = new RelayCommand(() => _workspaces.SplitActive(Orientation.Horizontal));
        SplitDownCommand = new RelayCommand(() => _workspaces.SplitActive(Orientation.Vertical));
        ClosePaneCommand = new RelayCommand(CloseFocused);
        ToggleSyncCommand = new RelayCommand(ToggleSync);
        NewShellCommand = new RelayCommand(() => _workspaces.SplitActive(Orientation.Horizontal));
        NextTabCommand = new RelayCommand(() => _workspaces.ActivateNext());
        PreviousTabCommand = new RelayCommand(() => _workspaces.ActivatePrevious());
        DataContext = this;
        InitializeComponent();

        TabStrip.ItemsSource = _workspaces.Tabs;
        _workspaces.EnsureShellWorkspace();
        RefreshActiveWorkspaceUi();
        DebugLog.Info("Main window ready");

        Loaded += async (_, _) =>
        {
            _updatePollTimer.Start();
            await PollForUpdatesAsync();
        };
        Activated += async (_, _) => await PollForUpdatesAsync();
        Closed += (_, _) =>
        {
            _updatePollTimer.Stop();
            _httpClient.Dispose();
            _windowTheme.Dispose();
            _workspaces.Dispose();
        };
    }

    private TerminalPane CreatePane(SessionHub hub, string commandLine, string title)
    {
        var pane = new TerminalPane(hub, commandLine, title);
        pane.SplitRequested += (_, orientation) =>
        {
            if (ReferenceEquals(_workspaces.Active?.Hub, hub))
            {
                _workspaces.SplitActive(orientation);
            }
        };
        pane.CloseRequested += ClosePane;
        return pane;
    }

    private void OnActiveWorkspaceChanged() => RefreshActiveWorkspaceUi();

    private void RefreshActiveWorkspaceUi()
    {
        var tab = _workspaces.Active;
        var nextHost = tab?.Tiling;
        if (!ReferenceEquals(RootHost.Content, nextHost))
        {
            Keyboard.ClearFocus();
            RootHost.Content = nextHost;
        }

        if (_syncBoundHub is not null)
        {
            _syncBoundHub.SyncModeChanged -= OnActiveHubSyncChanged;
        }

        _syncBoundHub = tab?.Hub;
        if (_syncBoundHub is not null)
        {
            _syncBoundHub.SyncModeChanged += OnActiveHubSyncChanged;
        }

        SyncToggle.IsChecked = tab?.Hub.SessionSyncEnabled == true;
        Dispatcher.BeginInvoke(HighlightActiveTab);
        if (tab is not null)
        {
            StatusText.Text = tab.ClusterId is null
                ? $"Workspace: {tab.Title}. Keyboard input is active only in this tab."
                : $"Cluster workspace: {tab.Title}. Sync and typing stay inside this tab.";
        }
    }

    private void OnActiveHubSyncChanged() =>
        SyncToggle.IsChecked = _workspaces.Active?.Hub.SessionSyncEnabled == true;

    private void HighlightActiveTab()
    {
        TabStrip.UpdateLayout();
        for (var i = 0; i < TabStrip.Items.Count; i++)
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter presenter)
            {
                continue;
            }

            presenter.ApplyTemplate();
            var border = FindVisualChild<Border>(presenter);
            if (border is null)
            {
                continue;
            }

            var isActive = ReferenceEquals(TabStrip.Items[i], _workspaces.Active);
            border.Background = new SolidColorBrush(
                isActive ? Color.FromRgb(0x1f, 0x3a, 0x38) : Color.FromRgb(0x25, 0x25, 0x25));
            border.BorderBrush = new SolidColorBrush(
                isActive ? Color.FromRgb(0x9a, 0xdb, 0xd6) : Color.FromRgb(0x3a, 0x3a, 0x3a));
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                return typed;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void SplitRight_Click(object sender, RoutedEventArgs e) =>
        _workspaces.SplitActive(Orientation.Horizontal);

    private void SplitDown_Click(object sender, RoutedEventArgs e) =>
        _workspaces.SplitActive(Orientation.Vertical);

    private void NewShell_Click(object sender, RoutedEventArgs e) =>
        _workspaces.SplitActive(Orientation.Horizontal);

    private void ClosePane_Click(object sender, RoutedEventArgs e) => CloseFocused();

    private void NewTab_Click(object sender, RoutedEventArgs e) =>
        _workspaces.AddShellWorkspace();

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorkspaceTab tab })
        {
            _workspaces.Activate(tab);
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorkspaceTab tab })
        {
            e.Handled = true;
            _workspaces.CloseTab(tab);
            RefreshActiveWorkspaceUi();
        }
    }

    private void SyncToggle_Click(object sender, RoutedEventArgs e)
    {
        var tab = _workspaces.Active;
        if (tab is null)
        {
            return;
        }

        tab.Hub.SessionSyncEnabled = SyncToggle.IsChecked == true;
        StatusText.Text = tab.Hub.SessionSyncEnabled
            ? $"Synchronized input is on for workspace '{tab.Title}'."
            : $"Synchronized input is off for workspace '{tab.Title}'.";
    }

    private void ToggleSync()
    {
        var tab = _workspaces.Active;
        if (tab is null)
        {
            return;
        }

        tab.Hub.SessionSyncEnabled = !tab.Hub.SessionSyncEnabled;
        SyncToggle.IsChecked = tab.Hub.SessionSyncEnabled;
    }

    private void Cluster_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ClusterWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedCluster is null)
        {
            return;
        }

        var tab = _workspaces.OpenCluster(dialog.SelectedCluster);
        StatusText.Text = $"Opened cluster '{tab.Title}' in a new workspace tab.";
        RefreshActiveWorkspaceUi();
    }

    private void DebugReport_Click(object sender, RoutedEventArgs e)
    {
        var notes = _workspaces.Active is { } tab
            ? $"Active workspace '{tab.Title}', tabs={_workspaces.Tabs.Count}, panes={tab.Tiling.Panes.Count()}"
            : $"tabs={_workspaces.Tabs.Count}";
        new DebugReportWindow(CurrentVersion, notes: notes) { Owner = this }.ShowDialog();
    }

    private void EditSshConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SshConfigWindow { Owner = this };
        dialog.ShowDialog();

        if (dialog.Saved)
        {
            StatusText.Text = "SSH config saved. Open a pane or cluster selector to see the updated hosts.";
        }
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        // The window runs its own fresh check as it opens.
        new UpdateWindow(CurrentVersion, _updateService, _updateChecker) { Owner = this }.ShowDialog();

        if (_updateChecker.LastResult is { } result)
        {
            ShowUpdateAvailability(result);
        }
    }

    private async Task PollForUpdatesAsync()
    {
        try
        {
            ShowUpdateAvailability(await _updateChecker.GetAsync());
        }
        catch (Exception ex)
        {
            DebugLog.Warn("Background update check failed: " + ex.Message);
        }
    }

    private void ShowUpdateAvailability(UpdateCheckResult result)
    {
        if (!result.IsUpdateAvailable)
        {
            UpdateButton.Content = "Check for updates";
            return;
        }

        var latest = GitHubUpdateService.FormatVersion(result.LatestVersion);
        UpdateButton.Content = $"Update to {latest}";
        StatusText.Text = $"WinTilix {latest} is available. Use the update button to install it.";
    }

    private void CloseFocused()
    {
        var pane = _workspaces.Active?.FocusedPane ?? _workspaces.Active?.Tiling.Panes.LastOrDefault();
        if (pane is not null)
        {
            ClosePane(pane);
        }
    }

    private void ClosePane(TerminalPane pane)
    {
        _workspaces.ClosePane(pane);
        RefreshActiveWorkspaceUi();
    }
}

internal sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
