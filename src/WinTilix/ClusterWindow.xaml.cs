using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinTilix.Session;
using WinTilix.Terminal;

namespace WinTilix;

public partial class ClusterWindow : Window
{
    private readonly WindowTheme _windowTheme;
    private readonly ObservableCollection<string> _hosts = [];
    private bool _suppressPickerChange;

    public IReadOnlyList<string> Hosts { get; private set; } = [];

    public ClusterWindow()
    {
        _windowTheme = new WindowTheme(this);
        InitializeComponent();
        SelectedHosts.ItemsSource = _hosts;
        LoadPicker();
        LoadSavedHosts();
        Closed += (_, _) => _windowTheme.Dispose();
        Loaded += (_, _) => CustomHostBox.Focus();
    }

    private void LoadPicker()
    {
        _suppressPickerChange = true;
        HostPicker.ItemsSource = SshConfigReader.ReadUserHosts();
        HostPicker.SelectedIndex = -1;
        _suppressPickerChange = false;
    }

    private void LoadSavedHosts()
    {
        foreach (var host in ClusterStore.LoadHosts())
        {
            _hosts.Add(host);
        }
    }

    private void HostPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPickerChange || HostPicker.SelectedItem is not string host)
        {
            return;
        }

        CustomHostBox.Text = host;
    }

    private void CustomHostBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryAddCurrentHost();
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e) => TryAddCurrentHost();

    private void TryAddCurrentHost()
    {
        var host = CustomHostBox.Text.Trim();
        if (host.Length == 0 && HostPicker.SelectedItem is string selected)
        {
            host = selected;
        }

        if (host.Length == 0)
        {
            MessageBox.Show(
                this,
                "Select an SSH alias or type a host first.",
                "WinTilix",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_hosts.Any(existing => string.Equals(existing, host, StringComparison.OrdinalIgnoreCase)))
        {
            CustomHostBox.SelectAll();
            CustomHostBox.Focus();
            return;
        }

        _hosts.Add(host);
        CustomHostBox.Clear();
        _suppressPickerChange = true;
        HostPicker.SelectedIndex = -1;
        _suppressPickerChange = false;
        CustomHostBox.Focus();
    }

    private void RemoveHost_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string host })
        {
            _hosts.Remove(host);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => _hosts.Clear();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_hosts.Count == 0)
        {
            var pending = CustomHostBox.Text.Trim();
            if (pending.Length == 0 && HostPicker.SelectedItem is string selected)
            {
                pending = selected;
            }

            if (pending.Length > 0)
            {
                _hosts.Add(pending);
            }
        }

        if (_hosts.Count == 0)
        {
            MessageBox.Show(this, "Add at least one host.", "WinTilix", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Hosts = _hosts.ToArray();
        ClusterStore.SaveHosts(Hosts);
        DialogResult = true;
    }
}
