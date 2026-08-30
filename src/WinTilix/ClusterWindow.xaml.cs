using System.Windows;

namespace WinTilix;

public partial class ClusterWindow : Window
{
    public IReadOnlyList<string> Hosts { get; private set; } = [];

    public ClusterWindow()
    {
        InitializeComponent();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Hosts = HostsBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'))
            .ToArray();

        if (Hosts.Count == 0)
        {
            MessageBox.Show(this, "Enter at least one host.", "WinTilix", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
