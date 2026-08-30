using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WinTilix.Updates;

public sealed class GitHubUpdateService
{
    private static readonly Regex SetupAssetNamePattern = new(
        @"^WinTilix-.+-win-x64-setup\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;
    private readonly Uri _latestReleaseUri;

    public GitHubUpdateService(HttpClient httpClient, string owner, string repository)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        _httpClient = httpClient;
        _latestReleaseUri = new Uri(
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/releases/latest");
    }

    public async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        using var request = new HttpRequestMessage(HttpMethod.Get, _latestReleaseUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WinTilix", FormatVersion(currentVersion)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (release is null ||
            !TryParseVersion(release.TagName, out var latestVersion) ||
            !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var releaseUri) ||
            releaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("GitHub returned release information WinTilix could not understand.");
        }

        var package = SelectSetupAsset(
            release.Assets?
                .Where(asset =>
                    Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var downloadUri) &&
                    downloadUri.Scheme == Uri.UriSchemeHttps)
                .Select(asset => new UpdatePackageAsset(
                    asset.Name,
                    new Uri(asset.DownloadUrl),
                    asset.Size))
            ?? []);

        return new UpdateCheckResult(
            currentVersion,
            latestVersion,
            release.Name ?? release.TagName,
            releaseUri,
            latestVersion > currentVersion,
            package);
    }

    public async Task<string> DownloadAsync(
        UpdatePackageAsset package,
        Version version,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(version);

        if (!SetupAssetNamePattern.IsMatch(package.Name) ||
            package.DownloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The update package is not a valid WinTilix installer.", nameof(package));
        }

        var downloadDirectory = Path.Combine(
            Path.GetTempPath(),
            "WinTilix",
            "Updates",
            FormatVersion(version));
        Directory.CreateDirectory(downloadDirectory);

        var destinationPath = Path.Combine(downloadDirectory, package.Name);
        var temporaryPath = destinationPath + ".download";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, package.DownloadUri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WinTilix", FormatVersion(version)));
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? (package.Size > 0 ? package.Size : null);
            progress?.Report(new UpdateDownloadProgress(0, totalBytes));

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                long bytesReceived = 0;
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    bytesReceived += bytesRead;
                    progress?.Report(new UpdateDownloadProgress(bytesReceived, totalBytes));
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, destinationPath, true);
            return destinationPath;
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    public static UpdatePackageAsset? SelectSetupAsset(IEnumerable<UpdatePackageAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        return assets.FirstOrDefault(asset =>
            SetupAssetNamePattern.IsMatch(asset.Name) &&
            asset.DownloadUri.Scheme == Uri.UriSchemeHttps);
    }

    public static bool TryParseVersion(string? tagName, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return false;
        }

        var value = tagName.Trim().TrimStart('v', 'V');
        var suffixIndex = value.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            value = value[..suffixIndex];
        }

        return Version.TryParse(value, out version!);
    }

    public static string FormatVersion(Version version) =>
        version.Build >= 0 ? version.ToString(3) : version.ToString();

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
        [property: JsonPropertyName("size")] long Size);
}

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    string ReleaseName,
    Uri ReleaseUri,
    bool IsUpdateAvailable,
    UpdatePackageAsset? Package);

public sealed record UpdatePackageAsset(string Name, Uri DownloadUri, long Size);

public sealed record UpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Percentage =>
        TotalBytes > 0 ? Math.Min(100, BytesReceived * 100d / TotalBytes.Value) : null;
}
