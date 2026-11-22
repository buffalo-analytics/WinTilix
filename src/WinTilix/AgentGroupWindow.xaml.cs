using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WinTilix.Agents;

namespace WinTilix;

public partial class AgentGroupWindow : Window
{
    private static readonly string[] Models = ["opus", "sonnet", "fable"];

    private readonly WindowTheme _windowTheme;
    private readonly List<AgentRow> _rows = [];
    private string _groupId = Guid.NewGuid().ToString("N");
    private bool _loading;

    public AgentGroupDefinition? SelectedGroup { get; private set; }

    public AgentGroupWindow()
    {
        _windowTheme = new WindowTheme(this);
        InitializeComponent();
        Closed += (_, _) => _windowTheme.Dispose();
        RefreshSaved();
        Load(new AgentGroupDefinition { Name = "Dev group", Agents = AgentGroupPresets.DefaultAgents() });
    }

    private void RefreshSaved()
    {
        _loading = true;
        SavedGroups.ItemsSource = AgentGroupStore.LoadAll();
        _loading = false;
    }

    private void Load(AgentGroupDefinition group)
    {
        _groupId = group.Id;
        NameBox.Text = group.Name;
        FallbackBox.Text = group.FallbackModel;
        WorktreeBox.IsChecked = group.UseWorktrees;
        _rows.Clear();
        Rows.Children.Clear();
        foreach (var spec in group.Agents)
        {
            AddRow(spec);
        }
    }

    private AgentGroupDefinition Collect() => new()
    {
        Id = _groupId,
        Name = NameBox.Text,
        FallbackModel = FallbackBox.Text.Trim(),
        UseWorktrees = WorktreeBox.IsChecked == true,
        Agents = _rows.Select(row => row.ToSpec()).ToList(),
    };

    private void SavedGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && SavedGroups.SelectedItem is AgentGroupDefinition group)
        {
            Load(group);
        }
    }

    private void AddAgent_Click(object sender, RoutedEventArgs e) =>
        AddRow(new AgentLaunchSpec { Name = "builder", Role = nameof(AgentRoleKind.Builder) });

    private void AddRow(AgentLaunchSpec spec)
    {
        var row = new AgentRow(this, spec, remove => { _rows.Remove(remove); Rows.Children.Remove(remove.Card); });
        _rows.Add(row);
        Rows.Children.Add(row.Card);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => TrySave();

    private bool TrySave()
    {
        try
        {
            AgentGroupStore.Save(Collect());
            RefreshSaved();
            return true;
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Claude Dev Group", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        AgentGroupStore.Delete(_groupId);
        RefreshSaved();
        Load(new AgentGroupDefinition { Name = "Dev group", Agents = AgentGroupPresets.DefaultAgents() });
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var group = Collect();
        if (group.Name.Trim().Length == 0 || group.Agents.Count == 0 || !TrySave())
        {
            return;
        }

        SelectedGroup = group;
        DialogResult = true;
    }

    /// <summary>One agent's card: name, role, model, folder, system prompt and first message.</summary>
    private sealed class AgentRow
    {
        private readonly TextBox _name;
        private readonly ComboBox _role;
        private readonly ComboBox _model;
        private readonly TextBox _folder;
        private readonly TextBox _prompt;
        private readonly TextBox _first;

        public Border Card { get; }

        public AgentRow(AgentGroupWindow owner, AgentLaunchSpec spec, Action<AgentRow> remove)
        {
            Style Dark(string key) => (Style)owner.FindResource(key);

            _name = new TextBox { Style = Dark("DarkTextBox"), Text = spec.Name, ToolTip = "Session name (claude -n)" };
            _role = new ComboBox
            {
                Style = Dark("DarkComboBox"),
                Height = 26,
                ItemsSource = Enum.GetValues<AgentRoleKind>().Where(k => k != AgentRoleKind.Unknown).Select(k => k.ToString()).ToList(),
                SelectedItem = spec.Role,
                ToolTip = "Role: sets the badge colour",
            };
            _model = new ComboBox
            {
                Style = Dark("DarkComboBox"),
                Height = 26,
                IsEditable = true,
                ItemsSource = Models,
                Text = spec.Model,
                ToolTip = "claude --model: an alias (opus, sonnet, fable) or a full model name",
            };
            _folder = new TextBox { Style = Dark("DarkTextBox"), Text = spec.Folder, ToolTip = "Working folder (empty = your user folder)" };
            _prompt = new TextBox { Style = Dark("DarkTextBox"), Text = spec.Prompt, ToolTip = "Role prompt, appended to Claude's system prompt" };
            _first = new TextBox { Style = Dark("DarkTextBox"), Text = spec.FirstMessage, ToolTip = "Optional first message; empty leaves the agent waiting" };

            var browse = new Button { Content = "…", Width = 30, Margin = new Thickness(6, 0, 0, 0) };
            browse.Click += (_, _) =>
            {
                var dialog = new OpenFolderDialog { Title = "Working folder for this agent" };
                if (dialog.ShowDialog(owner) == true)
                {
                    _folder.Text = dialog.FolderName;
                }
            };
            var close = new Button { Content = "✕", Width = 30, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Remove this agent" };
            close.Click += (_, _) => remove(this);

            var top = new Grid();
            foreach (var width in new[] { 1.4, 1.2, 1.0 })
            {
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
            }

            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Place(top, _name, 0, new Thickness(0, 0, 8, 0));
            Place(top, _role, 1, new Thickness(0, 0, 8, 0));
            Place(top, _model, 2, new Thickness(0));
            Place(top, close, 3, new Thickness(0));

            var folderRow = new DockPanel();
            DockPanel.SetDock(browse, Dock.Right);
            folderRow.Children.Add(browse);
            folderRow.Children.Add(_folder);

            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(Labeled("Folder", folderRow));
            stack.Children.Add(Labeled("Role prompt", _prompt));
            stack.Children.Add(Labeled("First message", _first));

            Card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = stack,
            };
        }

        public AgentLaunchSpec ToSpec() => new()
        {
            Name = _name.Text,
            Role = _role.SelectedItem as string ?? nameof(AgentRoleKind.Builder),
            Model = _model.Text.Trim(),
            Folder = _folder.Text.Trim(),
            Prompt = _prompt.Text,
            FirstMessage = _first.Text,
        };

        private static DockPanel Labeled(string label, UIElement control)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var text = new TextBlock
            {
                Text = label,
                Width = 90,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a)),
            };
            DockPanel.SetDock(text, Dock.Left);
            row.Children.Add(text);
            row.Children.Add(control);
            return row;
        }

        private static void Place(Grid grid, UIElement element, int column, Thickness margin)
        {
            Grid.SetColumn(element, column);
            if (element is FrameworkElement fe)
            {
                fe.Margin = margin;
            }

            grid.Children.Add(element);
        }
    }
}
