using System.Diagnostics;
using System.IO;
using System.Windows;
using WinTilix.Diagnostics;

namespace WinTilix;

public partial class DebugReportWindow : Window
{
    private readonly WindowTheme _windowTheme;
    private readonly string _reportPath;
    private readonly Uri _issueUri;

    public DebugReportWindow(Version version, Exception? exception = null, string? notes = null)
    {
        _windowTheme = new WindowTheme(this);
        InitializeComponent();
        _reportPath = DebugLog.WriteReport(version, exception, notes);
        _issueUri = DebugLog.CreateGitHubIssueUri(_reportPath);
        PathBox.Text = _reportPath;
        PreviewBox.Text = File.ReadAllText(_reportPath);
        Closed += (_, _) => _windowTheme.Dispose();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_reportPath);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_reportPath}\"")
        {
            UseShellExecute = true,
        });
    }

    private void GitHub_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(_issueUri.AbsoluteUri)
        {
            UseShellExecute = true,
        });
    }
}
