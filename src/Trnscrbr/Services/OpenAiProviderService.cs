using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;
using Trnscrbr.Models;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Services;

public sealed class OpenAiProviderService
{
    public const string DeleteLastInsertionAction = "__TRNSCRBR_ACTION_DELETE_LAST_INSERTION__";
    private static readonly Uri ModelsUri = new("https://api.openai.com/v1/models");
    private static readonly Uri TranscriptionsUri = new("https://api.openai.com/v1/audio/transcriptions");
    private static readonly Uri ResponsesUri = new("https://api.openai.com/v1/responses");
    private const string TranscriptionModel = "gpt-4o-mini-transcribe";
    private const string CleanupModel = "gpt-5.4-mini";
    private readonly HttpClient _httpClient = new();
    private readonly DiagnosticLogService? _diagnosticLog;

    public OpenAiProviderService(DiagnosticLogService? diagnosticLog = null)
    {
        _diagnosticLog = diagnosticLog;
    }

    public async Task<ProviderTestResult> TestApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ProviderTestResult.Fail("API key is empty.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return ProviderTestResult.Success();
            }

            LogProviderFailure("OpenAI API key test failed", response, "models");
            return ProviderTestResult.Fail($"OpenAI test failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ProviderTestResult.Fail($"OpenAI test failed: {ex.Message}");
        }
    }

    public async Task<TranscriptionResult> TranscribeAndCleanAsync(
        string apiKey,
        RecordedAudio audio,
        AppStateViewModel state,
        CancellationToken cancellationToken = default)
    {
        var rawTranscript = await TranscribeAsync(apiKey, audio, state, cancellationToken);
        if (string.IsNullOrWhiteSpace(rawTranscript))
        {
            throw new InvalidOperationException("OpenAI returned an empty transcript.");
        }

        var cleanup = await CleanTranscriptAsync(apiKey, rawTranscript, state, cancellationToken);
        var transcriptionCost = OpenAiPricingCatalog.EstimateTranscriptionCost(audio.Duration);
        var cleanupCost = OpenAiPricingCatalog.EstimateCleanupCost(cleanup.InputTokens, cleanup.OutputTokens);

        return new TranscriptionResult(
            cleanup.CleanedTranscript,
            cleanup.InputTokens,
            cleanup.OutputTokens,
            transcriptionCost + cleanupCost);
    }

    private async Task<string> TranscribeAsync(
        string apiKey,
        RecordedAudio audio,
        AppStateViewModel state,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TranscriptionsUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        await using var stream = File.OpenRead(audio.FilePath);
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", Path.GetFileName(audio.FilePath));
        form.Add(new StringContent(TranscriptionModel), "model");
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("Preserve all intentionally spoken words, including opening words such as okay, so, well, right, and yes. Do not omit them as filler."), "prompt");

        if (!string.Equals(state.Settings.LanguageMode, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            form.Add(new StringContent(state.Settings.LanguageMode), "language");
        }

        request.Content = form;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogProviderFailure("OpenAI transcription failed", response, TranscriptionModel);
            throw new InvalidOperationException($"OpenAI transcription failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("text", out var text)
            ? text.GetString() ?? string.Empty
            : string.Empty;
    }

    private async Task<CleanupResult> CleanTranscriptAsync(
        string apiKey,
        string rawTranscript,
        AppStateViewModel state,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        var instructions = BuildCleanupInstructions(state);
        var payload = JsonSerializer.Serialize(new
        {
            model = CleanupModel,
            instructions,
            input = rawTranscript,
            store = false
        });

        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogProviderFailure("OpenAI cleanup failed", response, CleanupModel);
            throw new InvalidOperationException($"OpenAI cleanup failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var cleaned = ExtractOutputText(json).Trim();
        var (inputTokens, outputTokens) = ExtractUsage(json);

        if (inputTokens == 0 && outputTokens == 0)
        {
            inputTokens = EstimateTokens(instructions) + EstimateTokens(rawTranscript);
            outputTokens = EstimateTokens(cleaned);
        }

        return new CleanupResult(cleaned, inputTokens, outputTokens);
    }

    private static string BuildCleanupInstructions(AppStateViewModel state)
    {
        var rewrite = string.Equals(state.Settings.CleanupMode, "Rewrite", StringComparison.OrdinalIgnoreCase);
        var vocabulary = state.Settings.CustomVocabulary.Count == 0
            ? "None."
            : string.Join(", ", state.Settings.CustomVocabulary);
        var previous = state.LastTranscript is { Length: > 0 }
            ? state.LastTranscript
            : "None.";
        var contextInstruction = state.Settings.ContextualCorrectionEnabled
            ? "Apply contextual correction for obvious speech recognition mistakes unless doing so would change the likely meaning."
            : "Do not perform contextual correction beyond basic cleanup; preserve the recognized wording unless it is clearly filler, stutter, or punctuation/layout handling.";
        var languageInstruction = string.Equals(state.Settings.LanguageMode, "Auto", StringComparison.OrdinalIgnoreCase)
            ? "Language mode: auto detect. Preserve the language used by the speaker."
            : $"Language mode: {state.Settings.LanguageMode}. Preserve that language unless the user clearly switches language.";

        var mode = rewrite
            ? "Rewrite the transcript into cleaner, polished prose while preserving the user's meaning."
            : "Remove filler words, repeated stutters, and pause artifacts while preserving the user's wording as much as possible.";
        var voiceActions = state.Settings.VoiceActionCommandsEnabled
            ? $"""
            Voice action commands are enabled. If the whole utterance is clearly an action command to delete the previous Trnscrbr insertion, such as "delete that", "remove that", or "undo that", return exactly {DeleteLastInsertionAction}. Do not use this action for mixed dictation, quoted text, or discussion about the command.
            """
            : "Voice action commands are disabled. Treat phrases such as delete that, remove that, or undo that as literal dictated text.";

        return $"""
            You clean dictation transcripts for direct insertion into a focused text field.
            {mode}
            {languageInstruction}
            Preserve intentional opening words and discourse markers such as "Okay", "So", "Well", "Right", and "Yes" when they introduce the user's sentence.
            Do not remove "Okay" from the start of a sentence unless it is clearly repeated hesitation such as "okay okay um".
            Remove only true hesitation filler, not meaningful conversational framing.
            {contextInstruction}
            Convert spoken punctuation/layout commands when context indicates they are commands: new line, full stop, question mark, comma.
            {voiceActions}
            Do not add commentary, labels, markdown, or quotes. Return only the final text to insert.
            Do not press Enter or imply submission.

            Custom vocabulary to prefer during correction: {vocabulary}
            Previous temporary transcript for context only: {previous}
            """;
    }

    private static string ExtractOutputText(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("output_text", out var outputText))
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text))
                {
                    builder.Append(text.GetString());
                }
            }
        }

        return builder.ToString();
    }

    private static (int InputTokens, int OutputTokens) ExtractUsage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("usage", out var usage))
        {
            return (0, 0);
        }

        var inputTokens = usage.TryGetProperty("input_tokens", out var input)
            ? input.GetInt32()
            : 0;
        var outputTokens = usage.TryGetProperty("output_tokens", out var output)
            ? output.GetInt32()
            : 0;

        return (inputTokens, outputTokens);
    }

    private static int EstimateTokens(string text)
    {
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4d));
    }

    private void LogProviderFailure(string message, HttpResponseMessage response, string model)
    {
        _diagnosticLog?.Error(message, metadata: new Dictionary<string, string>
        {
            ["provider"] = "OpenAI",
            ["model"] = model,
            ["statusCode"] = ((int)response.StatusCode).ToString(),
            ["reason"] = response.ReasonPhrase ?? string.Empty,
            ["requestId"] = GetRequestId(response)
        });
    }

    private static string GetRequestId(HttpResponseMessage response)
    {
        foreach (var headerName in new[] { "request-id", "x-request-id", "openai-request-id" })
        {
            if (response.Headers.TryGetValues(headerName, out var values))
            {
                return values.FirstOrDefault() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private sealed record CleanupResult(string CleanedTranscript, int InputTokens, int OutputTokens);
}

public sealed record ProviderTestResult(bool IsSuccess, string Message)
{
    public static ProviderTestResult Success() => new(true, "Connection successful.");

    public static ProviderTestResult Fail(string message) => new(false, message);
}
