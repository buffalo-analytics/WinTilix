using System.Diagnostics;
using System.Windows;
using WinTilix.Diagnostics;
using WinTilix.Updates;

namespace WinTilix;

public partial class UpdateWindow : Window
{
    private readonly WindowTheme _windowTheme;
    private readonly Version _currentVersion;
    private readonly GitHubUpdateService _updateService;
    private readonly UpdateCheckCoordinator _updateChecker;
    private UpdateCheckResult? _result;
    private Uri? _releaseUri;
    private CancellationTokenSource? _downloadCancellation;

    public UpdateWindow(
        Version currentVersion,
        GitHubUpdateService updateService,
        UpdateCheckCoordinator updateChecker)
    {
        _windowTheme = new WindowTheme(this);
        _currentVersion = currentVersion;
        _updateService = updateService;
        _updateChecker = updateChecker;
        InitializeComponent();

        CurrentVersionText.Text = GitHubUpdateService.FormatVersion(currentVersion);
        ApplyResult(updateChecker.LastResult);

        // Opening the window always asks GitHub again rather than trusting an earlier answer.
        Loaded += async (_, _) => await RunCheckAsync();
        Closed += (_, _) =>
        {
            _downloadCancellation?.Cancel();
            _windowTheme.Dispose();
        };
    }

    private async void CheckNowButton_Click(object sender, RoutedEventArgs e) => await RunCheckAsync();

    private async Task RunCheckAsync()
    {
        CheckNowButton.IsEnabled = false;
        InstallButton.Visibility = Visibility.Collapsed;
        ReleaseButton.Visibility = Visibility.Collapsed;
        MessageText.Text = "Checking GitHub for the latest release…";
        CheckedAtText.Text = "";

        try
        {
            ApplyResult(await _updateChecker.GetAsync(forceRefresh: true));
        }
        catch (Exception ex)
        {
            DebugLog.Warn("Update check failed: " + ex.Message);
            ApplyResult(
                null,
                "WinTilix could not reach GitHub. Check your connection, then use Check now to try again.");
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    private void ApplyResult(UpdateCheckResult? result, string? errorMessage = null)
    {
        _result = result;
        _releaseUri = result?.ReleaseUri;

        LatestVersionText.Text = result is null
            ? "Unavailable"
            : GitHubUpdateService.FormatVersion(result.LatestVersion);
        CheckedAtText.Text = _updateChecker.LastCheckedAt is { } checkedAt
            ? $"Last checked {checkedAt.ToLocalTime():HH:mm:ss}"
            : "";

        if (result is null)
        {
            MessageText.Text = errorMessage ?? "WinTilix could not check GitHub for updates.";
            ReleaseButton.Visibility = Visibility.Collapsed;
            InstallButton.Visibility = Visibility.Collapsed;
            return;
        }

        if (!result.IsUpdateAvailable)
        {
            MessageText.Text =
                $"You are on the latest published version ({GitHubUpdateService.FormatVersion(_currentVersion)}).";
            ReleaseButton.Visibility = Visibility.Collapsed;
            InstallButton.Visibility = Visibility.Collapsed;
            return;
        }

        ReleaseButton.Visibility = Visibility.Visible;
        if (result.Package is not null)
        {
            MessageText.Text = $"{result.ReleaseName} is available. WinTilix can download the installer and start the update.";
            InstallButton.Visibility = Visibility.Visible;
        }
        else
        {
            MessageText.Text = $"{result.ReleaseName} is available, but it does not include a compatible Windows installer. View the release to download it manually.";
            InstallButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_result?.Package is null)
        {
            return;
        }

        InstallButton.IsEnabled = false;
        ReleaseButton.IsEnabled = false;
        CheckNowButton.IsEnabled = false;
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
        catch (Exception ex)
        {
            DebugLog.Error("Update download failed", ex);
            MessageText.Text = "The installer could not be downloaded or started. Use View release to download it manually.";
            DownloadProgress.Visibility = Visibility.Collapsed;
            InstallButton.IsEnabled = true;
            ReleaseButton.IsEnabled = true;
            CheckNowButton.IsEnabled = true;
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
