using System.Diagnostics;
using System.Windows;
using WinTilix.Updates;

namespace WinTilix;

public partial class UpdateWindow : Window
{
    private readonly WindowTheme _windowTheme;
    private readonly Uri? _releaseUri;

    public UpdateWindow(Version currentVersion, UpdateCheckResult? result, string? errorMessage = null)
    {
        _windowTheme = new WindowTheme(this);
        InitializeComponent();

        CurrentVersionText.Text = GitHubUpdateService.FormatVersion(currentVersion);
        LatestVersionText.Text = result is null
            ? "Unavailable"
            : GitHubUpdateService.FormatVersion(result.LatestVersion);

        if (result?.IsUpdateAvailable == true)
        {
            MessageText.Text = $"{result.ReleaseName} is available. Open the GitHub release to download and run the installer.";
            _releaseUri = result.ReleaseUri;
        }
        else if (result is not null)
        {
            MessageText.Text = "You have the latest published version.";
            ReleaseButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            MessageText.Text = errorMessage ?? "WinTilix could not check GitHub for updates.";
            ReleaseButton.Visibility = Visibility.Collapsed;
        }

        Closed += (_, _) => _windowTheme.Dispose();
    }

    private void ReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_releaseUri is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(_releaseUri.AbsoluteUri)
        {
            UseShellExecute = true,
        });
    }
}
