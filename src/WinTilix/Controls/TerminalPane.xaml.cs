using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using WinTilix.Session;
using WinTilix.Terminal;

namespace WinTilix.Controls;

public partial class TerminalPane : UserControl, ISyncTarget
{
    private readonly SessionHub _hub;
    private ConPtySession? _session;
    private bool _participateInSync = true;
    private bool _webReady;
    private bool _webviewHooked;

    public string PaneId { get; } = Guid.NewGuid().ToString("N");
    public string CommandLine { get; }
    public string Title { get; }

    public bool ParticipateInSync
    {
        get => _participateInSync;
        set
        {
            _participateInSync = value;
            UpdateSyncChrome();
        }
    }

    public event Action<TerminalPane, Orientation>? SplitRequested;
    public event Action<TerminalPane>? CloseRequested;
    public event Action<TerminalPane>? Activated;

    public TerminalPane(SessionHub hub, string commandLine, string title)
    {
        InitializeComponent();
        _hub = hub;
        CommandLine = commandLine;
        Title = title;
        TitleText.Text = title;
        Loaded += OnLoaded;
        GotFocus += (_, _) => Activated?.Invoke(this);
        PreviewMouseDown += (_, _) => Activated?.Invoke(this);
        _hub.Register(this);
        _hub.SyncModeChanged += UpdateSyncChrome;
        UpdateSyncChrome();
    }

    public void ReceiveSyncedInput(string data) => _session?.Write(data);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_webviewHooked && WebView.CoreWebView2 is not null)
        {
            return;
        }

        try
        {
            await WebView.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Microsoft Edge WebView2 Runtime is required.\n\n" + ex.Message,
                "WinTilix",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (_webviewHooked)
        {
            return;
        }

        _webviewHooked = true;
        var webRoot = Path.Combine(AppContext.BaseDirectory, "Web");
        var core = WebView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 failed to initialize.");
        core.SetVirtualHostNameToFolderMapping(
            "wintilix.assets",
            webRoot,
            CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.WebMessageReceived += OnWebMessage;
        core.Navigate("https://wintilix.assets/terminal.html");
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        try
        {
            var raw = e.TryGetWebMessageAsString();
            msg = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? e.WebMessageAsJson : raw).RootElement;
        }
        catch
        {
            return;
        }

        var type = msg.GetProperty("type").GetString();
        switch (type)
        {
            case "ready":
                _webReady = true;
                var cols = msg.TryGetProperty("cols", out var c) ? c.GetInt32() : 80;
                var rows = msg.TryGetProperty("rows", out var r) ? r.GetInt32() : 24;
                StartSession(cols, rows);
                break;
            case "input":
                var data = msg.GetProperty("data").GetString() ?? "";
                _session?.Write(data);
                _hub.BroadcastInput(PaneId, data);
                Activated?.Invoke(this);
                break;
            case "resize":
                _session?.Resize(msg.GetProperty("cols").GetInt32(), msg.GetProperty("rows").GetInt32());
                break;
            case "command":
                HandleCommand(msg.GetProperty("name").GetString());
                break;
        }
    }

    private void HandleCommand(string? name)
    {
        switch (name)
        {
            case "split-right":
                SplitRequested?.Invoke(this, Orientation.Horizontal);
                break;
            case "split-down":
                SplitRequested?.Invoke(this, Orientation.Vertical);
                break;
            case "close":
                CloseRequested?.Invoke(this);
                break;
            case "toggle-sync":
                _hub.SessionSyncEnabled = !_hub.SessionSyncEnabled;
                break;
        }
    }

    private void StartSession(int cols, int rows)
    {
        if (_session != null)
        {
            return;
        }

        try
        {
            _session = ConPtySession.Start(CommandLine, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cols, rows, OnOutput);
            _session.Exited += () => Dispatcher.BeginInvoke(() => TitleText.Text = Title + " (exited)");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Failed to start terminal", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOutput(byte[] bytes)
    {
        if (!_webReady)
        {
            return;
        }

        var payload = Convert.ToBase64String(bytes);
        Dispatcher.BeginInvoke(() =>
        {
            if (WebView.CoreWebView2 is null)
            {
                return;
            }

            var json = JsonSerializer.Serialize(new { type = "output", data = payload });
            WebView.CoreWebView2.PostWebMessageAsJson(json);
        });
    }

    private void UpdateSyncChrome()
    {
        var sessionOn = _hub.SessionSyncEnabled;
        SyncButton.Visibility = sessionOn ? Visibility.Visible : Visibility.Collapsed;
        SyncButton.Opacity = ParticipateInSync ? 1 : 0.35;
        TitleBar.Background = new SolidColorBrush(
            sessionOn && ParticipateInSync
                ? Color.FromRgb(0x1f, 0x3a, 0x38)
                : Color.FromRgb(0x25, 0x25, 0x25));
    }

    private void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        ParticipateInSync = !ParticipateInSync;
    }

    private void SplitRight_Click(object sender, RoutedEventArgs e) => SplitRequested?.Invoke(this, Orientation.Horizontal);

    private void SplitDown_Click(object sender, RoutedEventArgs e) => SplitRequested?.Invoke(this, Orientation.Vertical);

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this);

    public void DisposeSession()
    {
        _hub.Unregister(this);
        _hub.SyncModeChanged -= UpdateSyncChrome;
        _session?.Dispose();
        _session = null;
    }
}
