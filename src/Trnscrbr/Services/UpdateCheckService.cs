using System.Net.Http;
using System.Text.Json;

namespace Trnscrbr.Services;

public sealed class UpdateCheckService
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/anythingituk/trnscrbr/releases/latest");
    private readonly HttpClient _httpClient = new();

    public UpdateCheckService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"Trnscrbr/{AppInfo.Version}");
    }

    public async Task<UpdateCheckResult> CheckLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseUri, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return UpdateCheckResult.NoRelease("No published releases found yet.");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed($"Update check failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString() ?? string.Empty
                : string.Empty;
            var url = root.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;

            if (!TryParseVersion(tag, out var latest) || !TryParseVersion(AppInfo.DisplayVersion, out var current))
            {
                return UpdateCheckResult.Failed($"Latest release: {tag}. Current version: {AppInfo.DisplayVersion}.");
            }

            return latest > current
                ? UpdateCheckResult.UpdateAvailable(tag, url)
                : UpdateCheckResult.UpToDate(tag, url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return UpdateCheckResult.Failed($"Update check failed: {ex.Message}");
        }
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var clean = value.Trim().TrimStart('v', 'V');
        return Version.TryParse(clean, out version!);
    }
}

public sealed record UpdateCheckResult(bool IsSuccess, bool IsUpdateAvailable, string LatestVersion, string Message, string ReleaseUrl)
{
    public static UpdateCheckResult UpToDate(string tag, string releaseUrl) =>
        new(true, false, tag, $"You are up to date. Latest release: {tag}.", releaseUrl);

    public static UpdateCheckResult UpdateAvailable(string tag, string releaseUrl) =>
        new(true, true, tag, $"Update available: {tag}. Open releases to download the installer.", releaseUrl);

    public static UpdateCheckResult NoRelease(string message) =>
        new(true, false, string.Empty, message, string.Empty);

    public static UpdateCheckResult Failed(string message) =>
        new(false, false, string.Empty, message, string.Empty);
}
