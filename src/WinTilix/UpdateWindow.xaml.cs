using System.Diagnostics;
using System.Windows;
using WinTilix.Updates;

namespace WinTilix;

public partial class UpdateWindow : Window
{
    private readonly WindowTheme _windowTheme;
    private readonly GitHubUpdateService _updateService;
    private readonly UpdateCheckResult? _result;
    private readonly Uri? _releaseUri;
    private CancellationTokenSource? _downloadCancellation;

    public UpdateWindow(
        Version currentVersion,
        GitHubUpdateService updateService,
        UpdateCheckResult? result,
        string? errorMessage = null)
    {
        _windowTheme = new WindowTheme(this);
        _updateService = updateService;
        _result = result;
        InitializeComponent();

        CurrentVersionText.Text = GitHubUpdateService.FormatVersion(currentVersion);
        LatestVersionText.Text = result is null
            ? "Unavailable"
            : GitHubUpdateService.FormatVersion(result.LatestVersion);

        if (result?.IsUpdateAvailable == true)
        {
            _releaseUri = result.ReleaseUri;
            if (result.Package is not null)
            {
                MessageText.Text = $"{result.ReleaseName} is available. WinTilix can download the installer and start the update.";
            }
            else
            {
                MessageText.Text = $"{result.ReleaseName} is available, but it does not include a compatible Windows installer. View the release to download it manually.";
                InstallButton.Visibility = Visibility.Collapsed;
            }
        }
        else if (result is not null)
        {
            MessageText.Text = "You have the latest published version.";
            ReleaseButton.Visibility = Visibility.Collapsed;
            InstallButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            MessageText.Text = errorMessage ?? "WinTilix could not check GitHub for updates.";
            ReleaseButton.Visibility = Visibility.Collapsed;
            InstallButton.Visibility = Visibility.Collapsed;
        }

        Closed += (_, _) =>
        {
            _downloadCancellation?.Cancel();
            _windowTheme.Dispose();
        };
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_result?.Package is null)
        {
            return;
        }

        InstallButton.IsEnabled = false;
        ReleaseButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.IsIndeterminate = _result.Package.Size <= 0;
        MessageText.Text = $"Downloading {_result.Package.Name}…";
        _downloadCancellation = new CancellationTokenSource();

        var progress = new Progress<UpdateDownloadProgress>(download =>
        {
            if (download.Percentage is double percentage)
            {
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = percentage;
                MessageText.Text = $"Downloading update… {percentage:0}%";
            }
            else
            {
                MessageText.Text = $"Downloading update… {FormatBytes(download.BytesReceived)} received";
            }
        });

        try
        {
            var installerPath = await _updateService.DownloadAsync(
                _result.Package,
                _result.LatestVersion,
                progress,
                _downloadCancellation.Token);

            MessageText.Text = "Starting the installer. WinTilix will close so the update can be applied.";
            Process.Start(new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
            });
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) when (_downloadCancellation?.IsCancellationRequested == true)
        {
            // Closing the window cancels an in-progress download.
        }
        catch (Exception)
        {
            MessageText.Text = "The installer could not be downloaded or started. Use View release to download it manually.";
            DownloadProgress.Visibility = Visibility.Collapsed;
            InstallButton.IsEnabled = true;
            ReleaseButton.IsEnabled = true;
        }
        finally
        {
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
        }
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

    private static string FormatBytes(long byteCount) =>
        byteCount >= 1024 * 1024
            ? $"{byteCount / (1024d * 1024d):0.0} MB"
            : $"{byteCount / 1024d:0.0} KB";
}
