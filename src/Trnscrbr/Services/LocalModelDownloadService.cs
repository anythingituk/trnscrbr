using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Trnscrbr.Models;

namespace Trnscrbr.Services;

public sealed class LocalModelDownloadService
{
    private readonly HttpClient _httpClient = new();

    public static IReadOnlyList<LocalModelPreset> Presets { get; } =
    [
        new(
            "small",
            "Small - base.en",
            "ggml-base.en.bin",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin",
            "137c40403d78fd54d454da0f9bd998f78703390c",
            "142 MiB",
            "4 GB RAM",
            "Fastest practical English preset for older or low-power PCs."),
        new(
            "medium",
            "Medium - small.en",
            "ggml-small.en.bin",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin",
            "db8a495a91d927739e50b3fc1cc4c6b8f6c2d022",
            "466 MiB",
            "8 GB RAM",
            "Better English accuracy while staying manageable for most laptops."),
        new(
            "large",
            "Large - large-v3-turbo-q5_0",
            "ggml-large-v3-turbo-q5_0.bin",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin",
            "e050f7970618a659205450ad97eb95a18d69c9ee",
            "547 MiB",
            "8-16 GB RAM",
            "Highest quality preset that remains realistic for a free local MVP.")
    ];

    public string ModelsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Trnscrbr",
        "Models");

    public async Task<LocalModelInstallResult?> TryUseExistingAsync(
        LocalModelPreset preset,
        CancellationToken cancellationToken = default)
    {
        var targetPath = Path.Combine(ModelsDirectory, preset.FileName);
        return File.Exists(targetPath) && await VerifySha1Async(targetPath, preset.Sha1, cancellationToken)
            ? new LocalModelInstallResult(targetPath, preset.Id)
            : null;
    }

    public async Task<bool> VerifyPresetAsync(
        string path,
        LocalModelPreset preset,
        CancellationToken cancellationToken = default)
    {
        return File.Exists(path) && await VerifySha1Async(path, preset.Sha1, cancellationToken);
    }

    public LocalModelPreset? FindPreset(string path, string presetId)
    {
        if (!string.IsNullOrWhiteSpace(presetId))
        {
            var byId = Presets.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, presetId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fileName = Path.GetFileName(path);
        return Presets.FirstOrDefault(candidate =>
            string.Equals(candidate.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<LocalModelInstallResult> DownloadAsync(
        LocalModelPreset preset,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ModelsDirectory);

        var targetPath = Path.Combine(ModelsDirectory, preset.FileName);
        if (File.Exists(targetPath) && await VerifySha1Async(targetPath, preset.Sha1, cancellationToken))
        {
            progress?.Report(1);
            return new LocalModelInstallResult(targetPath, preset.Id);
        }

        var partialPath = targetPath + ".download";
        var existingBytes = File.Exists(partialPath)
            ? new FileInfo(partialPath).Length
            : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, preset.DownloadUrl);
        if (existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (existingBytes > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            existingBytes = 0;
            File.Delete(partialPath);
        }

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength is { } length
            ? length + existingBytes
            : 0;

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(partialPath, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[1024 * 128];
            var downloadedBytes = existingBytes;

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

        if (!await VerifySha1Async(partialPath, preset.Sha1, cancellationToken))
        {
            File.Delete(partialPath);
            throw new InvalidOperationException("Downloaded model failed checksum verification.");
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(partialPath, targetPath);
        progress?.Report(1);
        return new LocalModelInstallResult(targetPath, preset.Id);
    }

    private static async Task<bool> VerifySha1Async(
        string path,
        string expectedSha1,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record LocalModelInstallResult(string ModelPath, string PresetId);
