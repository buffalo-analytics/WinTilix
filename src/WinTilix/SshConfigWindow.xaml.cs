using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinTilix.Terminal;

namespace WinTilix;

public partial class SshConfigWindow : Window
{
    private readonly WindowTheme _windowTheme;
    private SshConfigDocument? _document;
    private SshHostSection? _selected;
    private bool _loading;
    private bool _dirty;
    private bool _allowClose;

    public bool Saved { get; private set; }

    public SshConfigWindow()
    {
        _windowTheme = new WindowTheme(this);
        InitializeComponent();
        ConfigFilePicker.ItemsSource = SshConfigReader.ReadUserConfigFiles();
        ConfigFilePicker.SelectedItem = SshConfigReader.UserConfigPath;
        Closing += Window_Closing;
        Closed += (_, _) => _windowTheme.Dispose();
    }

    private void ConfigFilePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ConfigFilePicker.SelectedItem is not string path)
        {
            return;
        }

        if (_dirty && !ConfirmDiscardOrSave())
        {
            _loading = true;
            ConfigFilePicker.SelectedItem = _document?.FilePath;
            _loading = false;
            return;
        }

        LoadDocument(path);
    }

    private void LoadDocument(string path)
    {
        try
        {
            _loading = true;
            var content = SshConfigStore.Read(path);
            _document = SshConfigParser.Parse(path, content);
            RefreshHostList();
            _dirty = false;
            StatusText.Text = File.Exists(path) ? path : $"{path} (new file)";
            ClearEditor();
            if (HostList.Items.Count > 0)
            {
                HostList.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to read SSH config",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshHostList()
    {
        if (_document is null)
        {
            return;
        }

        HostList.ItemsSource = null;
        HostList.ItemsSource = _document.HostSections
            .Where(section => section.IsEditable)
            .ToList();
    }

    private void HostList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (_selected is not null && EditorPanel.IsEnabled)
        {
            if (!TryApplyEditorToSelected())
            {
                _loading = true;
                HostList.SelectedItem = _selected;
                _loading = false;
                return;
            }
        }

        _selected = HostList.SelectedItem as SshHostSection;
        if (_selected is null)
        {
            ClearEditor();
            return;
        }

        LoadEditor(_selected);
    }

    private void LoadEditor(SshHostSection section)
    {
        _loading = true;
        EditorPanel.IsEnabled = true;
        var edit = SshConfigParser.ReadHost(section);
        AliasBox.Text = edit.Alias;
        HostNameBox.Text = edit.HostName ?? "";
        UserBox.Text = edit.User ?? "";
        PortBox.Text = edit.Port?.ToString() ?? "";
        IdentityBox.Text = string.Join(Environment.NewLine, edit.IdentityFiles);
        ProxyJumpBox.Text = string.Join(Environment.NewLine, edit.ProxyJumps);
        AliveIntervalBox.Text = edit.ServerAliveInterval?.ToString() ?? "";
        AliveCountBox.Text = edit.ServerAliveCountMax?.ToString() ?? "";
        AdvancedBox.Text = edit.AdvancedText;
        _loading = false;
    }

    private void ClearEditor()
    {
        _loading = true;
        EditorPanel.IsEnabled = false;
        AliasBox.Text = "";
        HostNameBox.Text = "";
        UserBox.Text = "";
        PortBox.Text = "";
        IdentityBox.Text = "";
        ProxyJumpBox.Text = "";
        AliveIntervalBox.Text = "";
        AliveCountBox.Text = "";
        AdvancedBox.Text = "";
        _selected = null;
        _loading = false;
    }

    private SshHostEdit BuildEditFromUi()
    {
        int? ParseOptionalInt(string text)
        {
            text = text.Trim();
            if (text.Length == 0)
            {
                return null;
            }

            return int.TryParse(text, out var value) ? value : -1;
        }

        return new SshHostEdit
        {
            Alias = AliasBox.Text,
            HostName = HostNameBox.Text,
            User = UserBox.Text,
            Port = ParseOptionalInt(PortBox.Text),
            IdentityFiles = IdentityBox.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            ProxyJumps = ProxyJumpBox.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            ServerAliveInterval = ParseOptionalInt(AliveIntervalBox.Text),
            ServerAliveCountMax = ParseOptionalInt(AliveCountBox.Text),
            AdvancedText = AdvancedBox.Text,
        };
    }

    private bool TryApplyEditorToSelected()
    {
        if (_document is null || _selected is null || !EditorPanel.IsEnabled)
        {
            return true;
        }

        var edit = BuildEditFromUi();
        if (edit.Port is -1 || edit.ServerAliveInterval is -1 || edit.ServerAliveCountMax is -1)
        {
            MessageBox.Show(this, "Port and keepalive fields must be whole numbers.",
                "SSH config editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var validation = SshConfigParser.Validate(edit, _document, _selected);
        if (!validation.IsValid)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, validation.Errors),
                "SSH config editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        SshConfigParser.ApplyHost(_document, _selected, edit);
        _dirty = true;
        var current = _selected;
        RefreshHostList();
        HostList.SelectedItem = _document.HostSections.FirstOrDefault(section =>
            section.Aliases.SequenceEqual(current.Aliases, StringComparer.OrdinalIgnoreCase)) ?? current;
        return true;
    }

    private void AddHost_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        if (_selected is not null && !TryApplyEditorToSelected())
        {
            return;
        }

        var baseAlias = "new-host";
        var alias = baseAlias;
        var index = 1;
        while (_document.HostSections.Any(section =>
                   section.Aliases.Any(a => string.Equals(a, alias, StringComparison.OrdinalIgnoreCase))))
        {
            alias = $"{baseAlias}-{index++}";
        }

        var edit = new SshHostEdit { Alias = alias };
        SshConfigParser.ApplyHost(_document, null, edit);
        _dirty = true;
        RefreshHostList();
        HostList.SelectedItem = _document.HostSections.Last(section =>
            section.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase));
        AliasBox.Focus();
        AliasBox.SelectAll();
    }

    private void DeleteHost_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || _selected is null)
        {
            return;
        }

        if (MessageBox.Show(this, $"Delete Host '{_selected.DisplayName}'?",
                "SSH config editor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        SshConfigParser.RemoveHost(_document, _selected);
        _dirty = true;
        ClearEditor();
        RefreshHostList();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveCurrent();

    private bool SaveCurrent()
    {
        if (_document is null)
        {
            return false;
        }

        if (_selected is not null && !TryApplyEditorToSelected())
        {
            return false;
        }

        try
        {
            SshConfigStore.Save(_document.FilePath, _document.Serialize());
            Saved = true;
            _dirty = false;
            StatusText.Text = $"Saved {_document.FilePath}";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to save SSH config",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        if (!_dirty || MessageBox.Show(this, "Discard unsaved changes and reload this file?",
                "SSH config editor", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            LoadDocument(_document.FilePath);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_dirty || ConfirmDiscard())
        {
            _allowClose = true;
            Close();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && _dirty && !ConfirmDiscard())
        {
            e.Cancel = true;
        }
    }

    private bool ConfirmDiscardOrSave()
    {
        var result = MessageBox.Show(this,
            "Save changes before opening another SSH config file?",
            "SSH config editor", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => SaveCurrent(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private bool ConfirmDiscard() =>
        MessageBox.Show(this, "Discard unsaved SSH config changes?",
            "SSH config editor", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            SaveCurrent();
        }
    }
}
