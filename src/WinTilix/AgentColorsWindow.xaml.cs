using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinTilix.Agents;

namespace WinTilix;

public partial class AgentColorsWindow : Window
{
    private readonly WindowTheme _windowTheme;
    private readonly Dictionary<AgentRoleKind, (TextBox Box, Border Swatch)> _rows = new();

    public AgentColorsWindow()
    {
        _windowTheme = new WindowTheme(this);
        InitializeComponent();
        Closed += (_, _) => _windowTheme.Dispose();

        foreach (var kind in Enum.GetValues<AgentRoleKind>())
        {
            AddRow(kind);
        }
    }

    private static string LabelFor(AgentRoleKind kind) => kind switch
    {
        AgentRoleKind.Unknown => "No role set",
        AgentRoleKind.Qa => "QA",
        AgentRoleKind.Ui => "UI",
        _ => kind.ToString(),
    };

    private void AddRow(AgentRoleKind kind)
    {
        var swatch = new Border
        {
            Width = 28,
            Height = 22,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 10, 0),
        };
        var box = new TextBox
        {
            Style = (Style)FindResource("DarkTextBox"),
            Width = 100,
            Text = AgentRoleCatalog.ColorHex(kind),
        };
        box.TextChanged += (_, _) => UpdateSwatch(box, swatch);
        UpdateSwatch(box, swatch);

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(box, Dock.Right);
        row.Children.Add(box);
        row.Children.Add(swatch);
        row.Children.Add(new TextBlock { Text = LabelFor(kind), VerticalAlignment = VerticalAlignment.Center });
        Rows.Children.Add(row);
        _rows[kind] = (box, swatch);
    }

    private static void UpdateSwatch(TextBox box, Border swatch)
    {
        var valid = AgentRoleCatalog.TryNormalizeHex(box.Text, out var hex);
        swatch.Background = valid
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
            : Brushes.Transparent;
        swatch.BorderBrush = valid ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));
        swatch.BorderThickness = new Thickness(valid ? 0 : 1);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (kind, row) in _rows)
        {
            row.Box.Text = AgentRoleCatalog.DefaultColorHex(kind);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var invalid = _rows.Where(pair => !AgentRoleCatalog.TryNormalizeHex(pair.Value.Box.Text, out _)).ToList();
        if (invalid.Count > 0)
        {
            MessageBox.Show(
                this,
                "These colours are not valid hex values (use six digits, e.g. #E3B341): " +
                string.Join(", ", invalid.Select(pair => LabelFor(pair.Key))),
                "Agent colours",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AgentRoleCatalog.SaveColors(_rows.ToDictionary(pair => pair.Key, pair => pair.Value.Box.Text));
        DialogResult = true;
    }
}
