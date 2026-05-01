using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Globalization;
using Trnscrbr.Models;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Services;

public sealed class OpenAiProviderService
{
    public const string DeleteLastInsertionAction = "__TRNSCRBR_ACTION_DELETE_LAST_INSERTION__";
    public const string DiscardCurrentTranscriptAction = "__TRNSCRBR_ACTION_DISCARD_CURRENT_TRANSCRIPT__";
    private static readonly Uri ModelsUri = new("https://api.openai.com/v1/models");
    private static readonly Uri TranscriptionsUri = new("https://api.openai.com/v1/audio/transcriptions");
    private static readonly Uri ResponsesUri = new("https://api.openai.com/v1/responses");
    private const string TranscriptionModel = "gpt-4o-mini-transcribe";
    private const string CleanupModel = "gpt-5.4-mini";
    private readonly HttpClient _httpClient = new();
    private readonly CursorContextService _cursorContext = new();
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

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            LogProviderFailure("OpenAI API key test failed", response, "models", body);
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
        form.Add(new StringContent("Transcribe the user's speech accurately. Do not invent missing words when the audio is too quiet, clipped, or unclear."), "prompt");

        if (!string.Equals(state.Settings.LanguageMode, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            form.Add(new StringContent(state.Settings.LanguageMode), "language");
        }

        request.Content = form;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogProviderFailure("OpenAI transcription failed", response, TranscriptionModel, json);
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

        var cursorContext = state.Settings.CursorContextEnabled
            ? _cursorContext.TryReadFocusedContext()
            : string.Empty;
        var instructions = BuildCleanupInstructions(state, cursorContext);
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
            LogProviderFailure("OpenAI cleanup failed", response, CleanupModel, json);
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

    private static string BuildCleanupInstructions(AppStateViewModel state, string cursorContext)
    {
        var rewrite = string.Equals(state.Settings.CleanupMode, "Rewrite", StringComparison.OrdinalIgnoreCase);
        var vocabulary = state.Settings.CustomVocabulary.Count == 0
            ? "None."
            : string.Join(", ", state.Settings.CustomVocabulary);
        var previous = state.LastTranscript is { Length: > 0 }
            ? state.LastTranscript
            : "None.";
        var activeCursorContext = string.IsNullOrWhiteSpace(cursorContext)
            ? "None."
            : cursorContext;
        var contextInstruction = state.Settings.ContextualCorrectionEnabled
            ? "Apply contextual correction for obvious speech recognition mistakes unless doing so would change the likely meaning."
            : "Do not perform contextual correction beyond basic cleanup; preserve the recognized wording unless it is clearly filler, stutter, or punctuation/layout handling.";
        var languageInstruction = string.Equals(state.Settings.LanguageMode, "Auto", StringComparison.OrdinalIgnoreCase)
            ? "Language mode: auto detect. Preserve the language used by the speaker."
            : $"Language mode: {state.Settings.LanguageMode}. Preserve that language unless the user clearly switches language.";
        var englishDialectInstruction = GetEnglishDialectInstruction(state.Settings.EnglishDialect);

        var mode = rewrite
            ? $"Rewrite the transcript into cleaner text while preserving the user's meaning. {GetRewriteStyleInstruction(state.Settings.RewriteStyle)}"
            : "Remove filler words, repeated stutters, and pause artifacts while preserving the user's wording as much as possible.";
        var voiceActions = state.Settings.VoiceActionCommandsEnabled
            ? $"""
            Voice action commands are enabled, but only when the whole utterance is clearly a command.
            If the whole utterance asks to delete the previous Trnscrbr insertion, such as "delete that", "remove that", "undo that", or "scratch that" when it refers to already inserted text, return exactly {DeleteLastInsertionAction}.
            If the whole utterance asks to discard the current dictation/transcript, such as "cancel that", "cancel this", "discard that", "scratch that" when it refers to the current dictation, or "never mind", return exactly {DiscardCurrentTranscriptAction}.
            Treat "stop recording" as a control command only if it is the whole utterance; in this cleanup stage return exactly {DiscardCurrentTranscriptAction} because recording has already stopped and there is no text to insert.
            Do not use action tokens for mixed dictation, quoted text, or discussion about the command words.
            """
            : "Voice action commands are disabled. Treat phrases such as delete that, cancel that, scratch that, stop recording, remove that, or undo that as literal dictated text.";

        return $"""
            You clean dictation transcripts for direct insertion into a focused text field.
            {mode}
            {languageInstruction}
            {englishDialectInstruction}
            Preserve intentional opening words and discourse markers such as "Okay", "So", "Well", "Right", and "Yes" when they introduce the user's sentence.
            Do not remove "Okay" from the start of a sentence unless it is clearly repeated hesitation such as "okay okay um".
            If the transcript contains only hesitation or discourse markers, such as "okay", "so", "well", "right", "um", "uh", "er", "like", or short combinations of those words, return exactly {DiscardCurrentTranscriptAction}.
            Remove only true hesitation filler, not meaningful conversational framing.
            {contextInstruction}
            Convert spoken punctuation/layout commands when context indicates they are commands: new line, full stop, question mark, comma.
            {voiceActions}
            Do not add commentary, labels, markdown, or quotes. Return only the final text to insert.
            Do not press Enter or imply submission.

            Custom vocabulary to prefer during correction: {vocabulary}
            Previous temporary transcript for context only: {previous}
            Text near the active cursor for correction context only: {activeCursorContext}
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

    private static string GetRewriteStyleInstruction(string rewriteStyle)
    {
        return rewriteStyle switch
        {
            "Professional" => "Use a professional workplace tone suitable for emails, support messages, and business communication.",
            "Friendly" => "Use a warm, approachable tone while keeping the message clear and natural.",
            "Concise" => "Make the result brief and direct. Remove unnecessary wording while preserving the core meaning.",
            "Native-level English" => "Make the English sound natural, fluent, and idiomatic, as a careful native speaker would write it.",
            _ => "Use plain, clear English. Avoid overly formal wording and avoid adding new ideas."
        };
    }

    private static string GetEnglishDialectInstruction(string englishDialect)
    {
        var dialect = string.Equals(englishDialect, "Auto", StringComparison.OrdinalIgnoreCase)
            ? DetectEnglishDialectFromCulture()
            : englishDialect;

        return dialect switch
        {
            "British English" => "For English output, use British English spelling and wording, such as colour, organise, centre, and favour. Do not translate non-English speech into English.",
            "American English" => "For English output, use American English spelling and wording, such as color, organize, center, and favor. Do not translate non-English speech into English.",
            "Canadian English" => "For English output, use Canadian English spelling and wording where appropriate. Do not translate non-English speech into English.",
            "Australian English" => "For English output, use Australian English spelling and wording where appropriate. Do not translate non-English speech into English.",
            _ => "For English output, preserve the speaker's likely English spelling convention. Do not translate non-English speech into English."
        };
    }

    private static string DetectEnglishDialectFromCulture()
    {
        var region = RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant();
        return region switch
        {
            "GB" or "IE" => "British English",
            "US" => "American English",
            "CA" => "Canadian English",
            "AU" or "NZ" => "Australian English",
            _ => "British English"
        };
    }

    private void LogProviderFailure(string message, HttpResponseMessage response, string model, string? responseBody = null)
    {
        var metadata = new Dictionary<string, string>
        {
            ["provider"] = "OpenAI",
            ["model"] = model,
            ["statusCode"] = ((int)response.StatusCode).ToString(),
            ["reason"] = response.ReasonPhrase ?? string.Empty,
            ["requestId"] = GetRequestId(response)
        };

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            metadata["response"] = responseBody.Length <= 600
                ? responseBody
                : responseBody[..600];
        }

        _diagnosticLog?.Error(message, metadata: metadata);
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
