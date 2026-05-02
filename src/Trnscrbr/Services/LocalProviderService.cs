using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Trnscrbr.Models;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Services;

public sealed class LocalProviderService
{
    private readonly HttpClient _httpClient = new();
    private readonly CursorContextService _cursorContext = new();
    private readonly DiagnosticLogService? _diagnosticLog;

    public LocalProviderService(DiagnosticLogService? diagnosticLog = null)
    {
        _diagnosticLog = diagnosticLog;
    }

    public bool IsConfigured(AppStateViewModel state)
    {
        return File.Exists(state.Settings.LocalWhisperExecutablePath)
            && File.Exists(state.Settings.LocalWhisperModelPath);
    }

    public async Task<ProviderTestResult> TestLocalConfigurationAsync(
        AppStateViewModel state,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(state.Settings.LocalWhisperExecutablePath))
        {
            return ProviderTestResult.Fail("Choose a whisper.cpp executable before using local mode.");
        }

        if (!File.Exists(state.Settings.LocalWhisperModelPath))
        {
            return ProviderTestResult.Fail("Choose a Whisper model file before using local mode.");
        }

        if (string.IsNullOrWhiteSpace(state.Settings.LocalLlmModel))
        {
            return ProviderTestResult.Success();
        }

        try
        {
            var models = await ListLocalLlmModelsAsync(state.Settings.LocalLlmEndpoint, cancellationToken);
            return models.Any(model => string.Equals(model, state.Settings.LocalLlmModel, StringComparison.OrdinalIgnoreCase))
                ? ProviderTestResult.Success()
                : ProviderTestResult.Fail("Whisper is configured, but the selected Ollama cleanup model was not found.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            _diagnosticLog?.Error("Local LLM test failed", ex);
            return ProviderTestResult.Fail($"Whisper is configured, but Ollama cleanup could not be reached: {LocalSetupErrorFormatter.GetUserMessage(ex)}");
        }
    }

    public async Task<ProviderTestResult> RunWhisperSmokeTestAsync(
        AppStateViewModel state,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(state))
        {
            return ProviderTestResult.Fail("Choose a whisper.cpp executable and Whisper model before running the smoke test.");
        }

        var audioPath = Path.Combine(
            Path.GetTempPath(),
            $"trnscrbr-smoke-{Guid.NewGuid():N}.wav");

        try
        {
            WriteSmokeTestWav(audioPath);
            var transcript = await TranscribeAsync(
                new RecordedAudio(
                    audioPath,
                    TimeSpan.FromSeconds(1),
                    16000,
                    1,
                    new FileInfo(audioPath).Length,
                    "Smoke test"),
                state,
                cancellationToken,
                allowEmptyTranscript: true);

            return string.IsNullOrWhiteSpace(transcript)
                ? ProviderTestResult.Success("Whisper runtime test passed. The generated test audio produced no transcript, which is expected.")
                : ProviderTestResult.Success("Whisper runtime test passed. Generated test audio produced a short transcript, which can happen and is not a quality check.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _diagnosticLog?.Error("Local Whisper smoke test failed", ex);
            return ProviderTestResult.Fail(LocalSetupErrorFormatter.Format("Whisper runtime test failed", ex));
        }
        finally
        {
            TryDelete(audioPath);
        }
    }

    public async Task<IReadOnlyList<string>> ListLocalLlmModelsAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var tagsUri = BuildOllamaTagsUri(endpoint);
        using var response = await _httpClient.GetAsync(tagsUri, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _diagnosticLog?.Error("Ollama model discovery failed", metadata: new Dictionary<string, string>
            {
                ["statusCode"] = ((int)response.StatusCode).ToString(),
                ["reason"] = response.ReasonPhrase ?? string.Empty,
                ["response"] = TrimForLog(json)
            });
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("models", out var models)
            || models.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return models
            .EnumerateArray()
            .Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<TranscriptionResult> TranscribeAndCleanAsync(
        RecordedAudio audio,
        AppStateViewModel state,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(state))
        {
            throw new InvalidOperationException("Local mode requires a whisper.cpp executable path and model path in Settings.");
        }

        var rawTranscript = await TranscribeAsync(audio, state, cancellationToken);
        if (string.IsNullOrWhiteSpace(rawTranscript))
        {
            throw new InvalidOperationException("Local transcription returned an empty transcript.");
        }

        var cleanedTranscript = await TryCleanWithLocalLlmAsync(rawTranscript, state, cancellationToken)
            ?? BasicClean(rawTranscript);

        return new TranscriptionResult(
            cleanedTranscript,
            EstimateTokens(rawTranscript),
            EstimateTokens(cleanedTranscript),
            0);
    }

    public async Task<string> TranscribeOnlyAsync(
        RecordedAudio audio,
        AppStateViewModel state,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(state))
        {
            throw new InvalidOperationException("Local mode requires a whisper.cpp executable path and model path in Settings.");
        }

        return await TranscribeAsync(audio, state, cancellationToken);
    }

    private async Task<string> TranscribeAsync(
        RecordedAudio audio,
        AppStateViewModel state,
        CancellationToken cancellationToken,
        bool allowEmptyTranscript = false)
    {
        var outputBase = Path.Combine(
            Path.GetTempPath(),
            $"trnscrbr-local-{Guid.NewGuid():N}");
        var outputTextPath = outputBase + ".txt";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = state.Settings.LocalWhisperExecutablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add(state.Settings.LocalWhisperModelPath);
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(audio.FilePath);
            startInfo.ArgumentList.Add("-otxt");
            startInfo.ArgumentList.Add("-of");
            startInfo.ArgumentList.Add(outputBase);
            startInfo.ArgumentList.Add("-nt");
            startInfo.ArgumentList.Add("-np");

            if (!string.Equals(state.Settings.LanguageMode, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add("-l");
                startInfo.ArgumentList.Add(state.Settings.LanguageMode);
            }

            if (state.Settings.ForceCpuOnly)
            {
                startInfo.ArgumentList.Add("-ng");
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start local Whisper process.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                _diagnosticLog?.Error("Local Whisper failed", metadata: new Dictionary<string, string>
                {
                    ["exitCode"] = process.ExitCode.ToString(),
                    ["stderr"] = TrimForLog(stderr)
                });
                throw new InvalidOperationException($"Local Whisper failed with exit code {process.ExitCode}.");
            }

            var transcript = File.Exists(outputTextPath)
                ? File.ReadAllText(outputTextPath).Trim()
                : stdout.Trim();

            if (!allowEmptyTranscript && string.IsNullOrWhiteSpace(transcript))
            {
                throw new InvalidOperationException("Local transcription returned an empty transcript.");
            }

            return transcript;
        }
        finally
        {
            TryDelete(outputTextPath);
        }
    }

    private async Task<string?> TryCleanWithLocalLlmAsync(
        string rawTranscript,
        AppStateViewModel state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state.Settings.LocalLlmModel)
            || string.IsNullOrWhiteSpace(state.Settings.LocalLlmEndpoint))
        {
            return null;
        }

        try
        {
            var cursorContext = state.Settings.CursorContextEnabled
                ? _cursorContext.TryReadFocusedContext()
                : string.Empty;
            var instructions = OpenAiProviderService.BuildCleanupInstructions(state, cursorContext);
            var payload = JsonSerializer.Serialize(new
            {
                model = state.Settings.LocalLlmModel,
                stream = false,
                messages = new[]
                {
                    new { role = "system", content = instructions },
                    new { role = "user", content = rawTranscript }
                },
                options = new
                {
                    temperature = 0
                }
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, state.Settings.LocalLlmEndpoint);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _diagnosticLog?.Error("Local LLM cleanup failed", metadata: new Dictionary<string, string>
                {
                    ["statusCode"] = ((int)response.StatusCode).ToString(),
                    ["reason"] = response.ReasonPhrase ?? string.Empty,
                    ["response"] = TrimForLog(json)
                });
                return null;
            }

            var cleaned = ExtractOllamaMessage(json).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _diagnosticLog?.Error("Local LLM cleanup unavailable", ex);
            return null;
        }
    }

    private static string ExtractOllamaMessage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            return content.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("response", out var response))
        {
            return response.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static Uri BuildOllamaTagsUri(string endpoint)
    {
        var fallback = new Uri("http://localhost:11434/api/tags");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return fallback;
        }

        var builder = new UriBuilder(uri)
        {
            Path = "/api/tags",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static string BasicClean(string transcript)
    {
        return transcript.Trim();
    }

    private static int EstimateTokens(string text)
    {
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4d));
    }

    private static string TrimForLog(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 600 ? trimmed : trimmed[..600];
    }

    private static void WriteSmokeTestWav(string path)
    {
        const int sampleRate = 16000;
        const short bitsPerSample = 16;
        const short channels = 1;
        const int durationSeconds = 1;
        var dataLength = sampleRate * channels * bitsPerSample / 8 * durationSeconds;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup of temporary local transcription output.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cancellation of local transcription process.
        }
    }
}
