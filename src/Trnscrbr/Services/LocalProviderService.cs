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

    private async Task<string> TranscribeAsync(
        RecordedAudio audio,
        AppStateViewModel state,
        CancellationToken cancellationToken)
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

            return File.Exists(outputTextPath)
                ? File.ReadAllText(outputTextPath).Trim()
                : stdout.Trim();
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
