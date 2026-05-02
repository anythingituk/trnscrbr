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

    public async Task<string> DownloadLatestX64Async(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(cancellationToken);
        var asset = release.Assets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, "whisper-bin-x64.zip", StringComparison.OrdinalIgnoreCase));

        if (asset is null)
        {
            throw new InvalidOperationException("The latest whisper.cpp release does not include whisper-bin-x64.zip.");
        }

        Directory.CreateDirectory(ToolsDirectory);
        var releaseDirectory = Path.Combine(ToolsDirectory, release.TagName);
        var existingCliPath = FindWhisperCli(releaseDirectory);
        if (existingCliPath is not null)
        {
            progress?.Report(1);
            return existingCliPath;
        }

        var zipPath = Path.Combine(ToolsDirectory, asset.Name);
        await DownloadAssetAsync(asset.DownloadUrl, zipPath, progress, cancellationToken);

        if (!string.IsNullOrWhiteSpace(asset.Sha256)
            && !await VerifySha256Async(zipPath, asset.Sha256, cancellationToken))
        {
            File.Delete(zipPath);
            throw new InvalidOperationException("Downloaded whisper.cpp CLI failed checksum verification.");
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
            throw new InvalidOperationException("The whisper.cpp CLI archive did not contain whisper-cli.exe.");
        }

        progress?.Report(1);
        return discoveredCliPath;
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

    private sealed record WhisperRelease(string TagName, IReadOnlyList<WhisperReleaseAsset> Assets);

    private sealed record WhisperReleaseAsset(string Name, string DownloadUrl, string Sha256);
}
