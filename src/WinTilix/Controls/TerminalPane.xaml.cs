using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using WinTilix.Session;
using WinTilix.Terminal;

namespace WinTilix.Controls;

public partial class TerminalPane : UserControl, ISyncTarget
{
    private readonly SessionHub _hub;
    private bool _isSsh;
    private ConPtySession? _session;
    private bool _participateInSync = true;
    private bool _webReady;
    private bool _webviewHooked;
    private bool _selectorReady;
    private int _cols = 80;
    private int _rows = 24;
    private int _sessionGeneration;
    private SshHealth _sshHealth = SshHealth.Connecting;
    private string _sshOutputTail = "";

    public string PaneId { get; } = Guid.NewGuid().ToString("N");
    public string CommandLine { get; private set; }
    public string Title { get; private set; }

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
        _isSsh = ShellResolver.IsSshCommand(commandLine);
        CommandLine = commandLine;
        Title = title;
        RefreshSessionChoices();
        Loaded += OnLoaded;
        SizeChanged += (_, _) => RequestTerminalFit();
        GotFocus += (_, _) => Activated?.Invoke(this);
        PreviewMouseDown += (_, _) => Activated?.Invoke(this);
        _hub.Register(this);
        _hub.SyncModeChanged += UpdateSyncChrome;
        if (_isSsh)
        {
            ConnectionHealthBadge.Visibility = Visibility.Visible;
            SetSshHealth(SshHealth.Connecting);
        }
        UpdateSyncChrome();
    }

    public void ReceiveSyncedInput(string data) => _session?.Write(data);

    /// <summary>
    /// Ask xterm to remeasure after the WPF tile size changes (close pane, splitter, window resize).
    /// WebView2 does not reliably raise window.resize for host-control-only size changes.
    /// </summary>
    public void RequestTerminalFit()
    {
        if (!_webReady || WebView.CoreWebView2 is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                if (!_webReady || WebView.CoreWebView2 is null)
                {
                    return;
                }

                WebView.CoreWebView2.PostWebMessageAsJson(
                    JsonSerializer.Serialize(new { type = "fit" }));
            },
            DispatcherPriority.Loaded);
    }

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
                _cols = msg.TryGetProperty("cols", out var c) ? c.GetInt32() : 80;
                _rows = msg.TryGetProperty("rows", out var r) ? r.GetInt32() : 24;
                StartSession(_cols, _rows);
                break;
            case "input":
                var data = msg.GetProperty("data").GetString() ?? "";
                _session?.Write(data);
                _hub.BroadcastInput(PaneId, data);
                Activated?.Invoke(this);
                break;
            case "resize":
                _cols = msg.GetProperty("cols").GetInt32();
                _rows = msg.GetProperty("rows").GetInt32();
                _session?.Resize(_cols, _rows);
                break;
            case "command":
                HandleCommand(msg.GetProperty("name").GetString());
                break;
            case "copy":
                var copyText = msg.GetProperty("data").GetString() ?? "";
                if (copyText.Length > 0)
                {
                    Dispatcher.BeginInvoke(() => TrySetClipboardText(copyText));
                }
                break;
            case "paste-request":
                Dispatcher.BeginInvoke(PasteFromClipboard);
                break;
        }
    }

    private void PasteFromClipboard()
    {
        try
        {
            if (WebView.CoreWebView2 is null || !Clipboard.ContainsText())
            {
                return;
            }

            var text = Clipboard.GetText();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            // Normalize Windows newlines so shells receive the same paste as on Linux terminals.
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            WebView.CoreWebView2.PostWebMessageAsJson(
                JsonSerializer.Serialize(new { type = "paste", data = text }));
            Activated?.Invoke(this);
        }
        catch
        {
            // Windows clipboard access can briefly fail when another process owns it.
        }
    }

    private static void TrySetClipboardText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // The next selection change or Ctrl+Shift+C provides a clean retry.
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
            SetSshHealth(SshHealth.Connecting);
            var generation = ++_sessionGeneration;
            var session = ConPtySession.Start(
                CommandLine,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                cols,
                rows,
                bytes => OnOutput(generation, bytes));
            _session = session;
            session.Exited += () => Dispatcher.BeginInvoke(() =>
            {
                if (!ReferenceEquals(_session, session))
                {
                    return;
                }

                if (_isSsh)
                {
                    SwitchSession(ShellResolver.DefaultShellCommand(), "Shell");
                    return;
                }

                SessionSelector.ToolTip = $"{Title} (exited)";
            });
        }
        catch (Exception ex)
        {
            var returnToShell = _isSsh;
            SetSshHealth(SshHealth.Failed);
            MessageBox.Show(ex.Message, "Failed to start terminal", MessageBoxButton.OK, MessageBoxImage.Error);
            if (returnToShell)
            {
                SwitchSession(ShellResolver.DefaultShellCommand(), "Shell");
            }
        }
    }

    private void OnOutput(int generation, byte[] bytes)
    {
        if (generation != _sessionGeneration)
        {
            return;
        }

        if (_isSsh)
        {
            UpdateSshHealthFromOutput(Encoding.UTF8.GetString(bytes));
        }

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

    private void SessionSelector_DropDownOpened(object sender, EventArgs e) =>
        RefreshSessionChoices();

    // Arrow keys on a closed selector would silently tear down the session and reconnect
    // elsewhere, so navigation keys only apply once the list has been opened deliberately.
    private void SessionSelector_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (SessionSelector.IsDropDownOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
            case Key.Left:
            case Key.Right:
            case Key.PageUp:
            case Key.PageDown:
            case Key.Home:
            case Key.End:
                e.Handled = true;
                break;
        }
    }

    private void SessionSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selectorReady || SessionSelector.SelectedItem is not SessionChoice choice)
        {
            return;
        }

        var wantsShell = choice.Host is null;
        var alreadyOnHost = _isSsh && string.Equals(Title, choice.Host, StringComparison.OrdinalIgnoreCase);

        if (wantsShell && _isSsh)
        {
            SwitchSession(ShellResolver.DefaultShellCommand(), "Shell");
        }
        else if (!wantsShell && !alreadyOnHost)
        {
            SwitchSession(ShellResolver.SshCommand(choice.Host!), choice.Host!);
        }

        AutomationProperties.SetName(SessionSelector, choice.Label);
        FocusTerminal();
    }

    private void FocusTerminal()
    {
        // Close the dropdown first so Up/Down cannot change hosts, then move
        // keyboard focus into the terminal so typing goes to the shell.
        SessionSelector.IsDropDownOpen = false;
        Dispatcher.BeginInvoke(
            () =>
            {
                WebView.Focus();
                Keyboard.Focus(WebView);
                WebView.CoreWebView2?.PostWebMessageAsJson(
                    JsonSerializer.Serialize(new { type = "focus" }));
            },
            DispatcherPriority.Background);
    }

    private void RefreshSessionChoices()
    {
        var choices = new List<SessionChoice> { new("Shell", null) };
        choices.AddRange(SshConfigReader.ReadUserHosts()
            .Select(host => new SessionChoice($"SSH · {host}", host)));

        if (_isSsh && choices.All(choice =>
                !string.Equals(choice.Host, Title, StringComparison.OrdinalIgnoreCase)))
        {
            choices.Add(new SessionChoice($"SSH · {Title}", Title));
        }

        choices = choices
            .Take(1)
            .Concat(choices.Skip(1).OrderBy(choice => choice.Host, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var selected = _isSsh
            ? choices.First(choice => string.Equals(choice.Host, Title, StringComparison.OrdinalIgnoreCase))
            : choices[0];

        _selectorReady = false;
        SessionSelector.ItemsSource = choices;
        SessionSelector.SelectedItem = selected;
        AutomationProperties.SetName(SessionSelector, selected.Label);
        _selectorReady = true;
    }

    private void SwitchSession(string commandLine, string title)
    {
        var previous = _session;
        _session = null;
        _sessionGeneration++;
        previous?.Dispose();

        CommandLine = commandLine;
        Title = title;
        _isSsh = ShellResolver.IsSshCommand(commandLine);
        _sshOutputTail = "";
        _sshHealth = SshHealth.Connecting;

        ConnectionHealthBadge.Visibility = _isSsh
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_isSsh)
        {
            SetSshHealth(SshHealth.Connecting);
        }

        SessionSelector.ToolTip = "Choose the local shell or an alias from ~/.ssh/config";
        RefreshSessionChoices();

        if (WebView.CoreWebView2 is not null)
        {
            WebView.CoreWebView2.PostWebMessageAsJson(
                JsonSerializer.Serialize(new { type = "clear" }));
        }

        StartSession(_cols, _rows);
        Activated?.Invoke(this);
    }

    private void UpdateSshHealthFromOutput(string output)
    {
        var visible = Regex.Replace(
            output,
            @"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\a]*(?:\a|\x1B\\))",
            "");
        if (string.IsNullOrWhiteSpace(visible))
        {
            return;
        }

        _sshOutputTail = (_sshOutputTail + visible)[^Math.Min(512, _sshOutputTail.Length + visible.Length)..];

        if (ContainsAny(
                _sshOutputTail,
                "connection refused",
                "connection timed out",
                "connection reset",
                "connection closed",
                "could not resolve hostname",
                "no route to host",
                "kex_exchange_identification"))
        {
            SetSshHealth(SshHealth.Failed);
            return;
        }

        if (_sshHealth != SshHealth.Connected &&
            ContainsAny(_sshOutputTail, "host key verification failed", "permission denied"))
        {
            SetSshHealth(SshHealth.Failed);
            return;
        }

        if (ContainsAny(_sshOutputTail, "password:", "passphrase", "authenticity of host"))
        {
            SetSshHealth(SshHealth.Authenticating);
            return;
        }

        SetSshHealth(SshHealth.Connected);
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private void SetSshHealth(SshHealth health)
    {
        if (!_isSsh)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetSshHealth(health));
            return;
        }

        var (text, foreground, background, tooltip) = health switch
        {
            SshHealth.Connecting => ("● Connecting", "#FFD166", "#4A3D1D", "Starting SSH and waiting for remote output"),
            SshHealth.Authenticating => ("● Authenticating", "#FFD166", "#4A3D1D", "SSH is waiting for authentication"),
            SshHealth.Connected => ("● Connected", "#7EE787", "#193C25", "SSH process is active and remote output was received"),
            SshHealth.Disconnected => ("● Disconnected", "#FF8A8A", "#4A2020", "The SSH process exited"),
            _ => ("● Connection error", "#FF8A8A", "#4A2020", "SSH reported a connection or authentication error")
        };

        ConnectionHealthText.Text = text;
        _sshHealth = health;
        ConnectionHealthText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(foreground));
        ConnectionHealthBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
        ConnectionHealthBadge.ToolTip = tooltip;
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

    private enum SshHealth
    {
        Connecting,
        Authenticating,
        Connected,
        Disconnected,
        Failed
    }

    private sealed record SessionChoice(string Label, string? Host);
}
