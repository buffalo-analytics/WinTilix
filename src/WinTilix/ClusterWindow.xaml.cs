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
    private readonly ObservableCollection<SshClusterDefinition> _clusters = [];
    private readonly ObservableCollection<string> _hosts = [];
    private SshClusterDefinition? _selected;
    private bool _suppressPickerChange;
    private bool _loading;

    public SshClusterDefinition? SelectedCluster { get; private set; }

    public ClusterWindow()
    {
        _windowTheme = new WindowTheme(this);
        InitializeComponent();
        ClusterList.ItemsSource = _clusters;
        SelectedHosts.ItemsSource = _hosts;
        LoadPicker();
        ReloadClusters();
        Closed += (_, _) => _windowTheme.Dispose();
        Loaded += (_, _) => ClusterNameBox.Focus();
    }

    private void LoadPicker()
    {
        _suppressPickerChange = true;
        HostPicker.ItemsSource = SshConfigReader.ReadUserHosts();
        HostPicker.SelectedIndex = -1;
        _suppressPickerChange = false;
    }

    private void ReloadClusters(string? selectId = null)
    {
        _loading = true;
        _clusters.Clear();
        foreach (var cluster in ClusterStore.LoadAll())
        {
            _clusters.Add(Clone(cluster));
        }

        _loading = false;
        if (_clusters.Count == 0)
        {
            NewCluster_Click(this, new RoutedEventArgs());
            return;
        }

        ClusterList.SelectedItem = _clusters.FirstOrDefault(cluster =>
            selectId is not null &&
            string.Equals(cluster.Id, selectId, StringComparison.OrdinalIgnoreCase))
            ?? _clusters[0];
    }

    private void ClusterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _selected = ClusterList.SelectedItem as SshClusterDefinition;
        if (_selected is null)
        {
            ClusterNameBox.Text = "";
            _hosts.Clear();
            return;
        }

        ClusterNameBox.Text = _selected.Name;
        _hosts.Clear();
        foreach (var host in _selected.Hosts)
        {
            _hosts.Add(host);
        }
    }

    private void NewCluster_Click(object sender, RoutedEventArgs e)
    {
        var cluster = new SshClusterDefinition
        {
            Name = NextAvailableName(),
            Hosts = [],
        };
        _clusters.Add(cluster);
        ClusterList.SelectedItem = cluster;
        ClusterNameBox.Focus();
        ClusterNameBox.SelectAll();
    }

    private void DeleteCluster_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        if (MessageBox.Show(this, $"Delete cluster '{_selected.Name}'?",
                "SSH clusters", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (_clusters.Any(cluster =>
                string.Equals(cluster.Id, _selected.Id, StringComparison.OrdinalIgnoreCase) &&
                ClusterStore.Get(_selected.Id) is not null))
        {
            ClusterStore.Delete(_selected.Id);
        }

        var id = _selected.Id;
        _clusters.Remove(_selected);
        if (_clusters.Count == 0)
        {
            NewCluster_Click(sender, e);
        }
        else
        {
            ClusterList.SelectedItem = _clusters[0];
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
            MessageBox.Show(this, "Select an SSH alias or type a host first.",
                "WinTilix", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saved = PersistSelected();
            ReloadClusters(saved.Id);
            StatusLike($"Saved cluster '{saved.Name}'.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "SSH clusters", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saved = PersistSelected();
            SelectedCluster = saved;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "SSH clusters", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private SshClusterDefinition PersistSelected()
    {
        if (_selected is null)
        {
            throw new InvalidOperationException("Select or create a cluster first.");
        }

        _selected.Name = ClusterNameBox.Text;
        _selected.Hosts = _hosts.ToList();
        return ClusterStore.Save(_selected);
    }

    private string NextAvailableName()
    {
        var index = 1;
        while (true)
        {
            var name = index == 1 ? "New cluster" : $"New cluster {index}";
            if (!_clusters.Any(cluster =>
                    string.Equals(cluster.Name, name, StringComparison.OrdinalIgnoreCase)) &&
                ClusterStore.LoadAll().All(cluster =>
                    !string.Equals(cluster.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }

            index++;
        }
    }

    private static SshClusterDefinition Clone(SshClusterDefinition source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            Hosts = source.Hosts.ToList(),
        };

    private void StatusLike(string message) => Title = $"SSH clusters — {message}";
}
