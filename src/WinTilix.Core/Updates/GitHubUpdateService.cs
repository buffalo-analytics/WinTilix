using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinTilix.Updates;

public sealed class GitHubUpdateService
{
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

        return new UpdateCheckResult(
            currentVersion,
            latestVersion,
            release.Name ?? release.TagName,
            releaseUri,
            latestVersion > currentVersion);
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
        [property: JsonPropertyName("html_url")] string HtmlUrl);
}

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    string ReleaseName,
    Uri ReleaseUri,
    bool IsUpdateAvailable);
