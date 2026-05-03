using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Trnscrbr.Services;

public sealed class LocalWhisperToolDownloadService
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/ggml-org/whisper.cpp/releases/latest");
    private readonly HttpClient _httpClient = new();

    public LocalWhisperToolDownloadService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"Trnscrbr/{AppInfo.Version}");
    }

    public string ToolsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Trnscrbr",
        "Tools",
        "whisper.cpp");

    public async Task<LocalWhisperToolInstallResult?> TryUseExistingLatestX64Async(
        CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(cancellationToken);
        var releaseDirectory = Path.Combine(ToolsDirectory, release.TagName);
        var existingCliPath = FindWhisperCli(releaseDirectory);
        return existingCliPath is null
            ? null
            : new LocalWhisperToolInstallResult(existingCliPath, release.TagName);
    }

    public async Task<LocalWhisperToolUpdateResult> CheckLatestX64Async(
        string installedVersion,
        CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(cancellationToken);
        var hasX64Asset = release.Assets.Any(candidate =>
            string.Equals(candidate.Name, "whisper-bin-x64.zip", StringComparison.OrdinalIgnoreCase));

        if (!hasX64Asset)
        {
            return new LocalWhisperToolUpdateResult(
                false,
                installedVersion,
                release.TagName,
                "The latest local engine release does not include a Windows x64 download.");
        }

        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            return new LocalWhisperToolUpdateResult(
                true,
                installedVersion,
                release.TagName,
                $"Local engine {release.TagName} is available. Click Install Local Engine to install it.");
        }

        if (!TryParseVersion(installedVersion, out var installed)
            || !TryParseVersion(release.TagName, out var latest))
        {
            var isDifferent = !string.Equals(installedVersion, release.TagName, StringComparison.OrdinalIgnoreCase);
            return new LocalWhisperToolUpdateResult(
                isDifferent,
                installedVersion,
                release.TagName,
                isDifferent
                    ? $"Installed: {installedVersion}. Latest: {release.TagName}. Click Install Local Engine to update."
                    : $"Local engine is current: {release.TagName}.");
        }

        return latest > installed
            ? new LocalWhisperToolUpdateResult(
                true,
                installedVersion,
                release.TagName,
                $"Local engine update available: {release.TagName}. Click Install Local Engine to update.")
            : new LocalWhisperToolUpdateResult(
                false,
                installedVersion,
                release.TagName,
                $"Local engine is current: {installedVersion}.");
    }

    public async Task<LocalWhisperToolInstallResult> DownloadLatestX64Async(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(cancellationToken);
        var asset = release.Assets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, "whisper-bin-x64.zip", StringComparison.OrdinalIgnoreCase));

        if (asset is null)
        {
            throw new InvalidOperationException("The latest local engine release does not include a Windows x64 download.");
        }

        Directory.CreateDirectory(ToolsDirectory);
        var releaseDirectory = Path.Combine(ToolsDirectory, release.TagName);
        var existingCliPath = FindWhisperCli(releaseDirectory);
        if (existingCliPath is not null)
        {
            progress?.Report(1);
            return new LocalWhisperToolInstallResult(existingCliPath, release.TagName);
        }

        var zipPath = Path.Combine(ToolsDirectory, asset.Name);
        await DownloadAssetAsync(asset.DownloadUrl, zipPath, progress, cancellationToken);

        if (!string.IsNullOrWhiteSpace(asset.Sha256)
            && !await VerifySha256Async(zipPath, asset.Sha256, cancellationToken))
        {
            File.Delete(zipPath);
            throw new InvalidOperationException("Downloaded local engine failed checksum verification.");
        }

        if (Directory.Exists(releaseDirectory))
        {
            Directory.Delete(releaseDirectory, recursive: true);
        }

        Directory.CreateDirectory(releaseDirectory);
        ZipFile.ExtractToDirectory(zipPath, releaseDirectory);

        var discoveredCliPath = FindWhisperCli(releaseDirectory);

        if (discoveredCliPath is null)
        {
            throw new InvalidOperationException("The local engine archive did not contain the expected executable.");
        }

        progress?.Report(1);
        return new LocalWhisperToolInstallResult(discoveredCliPath, release.TagName);
    }

    private async Task<WhisperRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tagName = root.TryGetProperty("tag_name", out var tag)
            ? tag.GetString() ?? "latest"
            : "latest";

        var assets = new List<WhisperReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement)
            && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;
                var downloadUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString() ?? string.Empty
                    : string.Empty;
                var digest = asset.TryGetProperty("digest", out var digestElement)
                    ? digestElement.GetString() ?? string.Empty
                    : string.Empty;
                var sha256 = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? digest["sha256:".Length..]
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(downloadUrl))
                {
                    assets.Add(new WhisperReleaseAsset(name, downloadUrl, sha256));
                }
            }
        }

        return new WhisperRelease(tagName, assets);
    }

    private async Task DownloadAssetAsync(
        string downloadUrl,
        string targetPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            downloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[1024 * 128];
        long downloadedBytes = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloadedBytes += read;

            if (totalBytes > 0)
            {
                progress?.Report(Math.Clamp(downloadedBytes / (double)totalBytes, 0, 1));
            }
        }
    }

    private static async Task<bool> VerifySha256Async(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindWhisperCli(string releaseDirectory)
    {
        return Directory.Exists(releaseDirectory)
            ? Directory.EnumerateFiles(releaseDirectory, "whisper-cli.exe", SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var clean = value.Trim().TrimStart('v', 'V');
        return Version.TryParse(clean, out version!);
    }

    private sealed record WhisperRelease(string TagName, IReadOnlyList<WhisperReleaseAsset> Assets);

    private sealed record WhisperReleaseAsset(string Name, string DownloadUrl, string Sha256);
}

public sealed record LocalWhisperToolInstallResult(string ExecutablePath, string Version);

public sealed record LocalWhisperToolUpdateResult(
    bool IsUpdateAvailable,
    string InstalledVersion,
    string LatestVersion,
    string Message);
