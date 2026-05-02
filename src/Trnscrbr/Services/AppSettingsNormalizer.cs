using Trnscrbr.Models;

namespace Trnscrbr.Services;

public static class AppSettingsNormalizer
{
    private static readonly string[] ProviderModes =
    [
        "Not configured",
        "Bring your own API key",
        "Local mode",
        "Cloud managed by app (planned)"
    ];

    private static readonly string[] CleanupModes =
    [
        "Clean only",
        "Rewrite"
    ];

    private static readonly string[] PasteMethods =
    [
        "Ctrl+V",
        "Shift+Insert"
    ];

    private static readonly string[] ToggleRecordingHotkeys =
    [
        "Ctrl+Alt+R",
        "Ctrl+Shift+R",
        "Ctrl+Alt+D",
        "F9"
    ];

    private static readonly string[] PushToTalkHotkeys =
    [
        "Ctrl+Alt+Space",
        "Ctrl+Shift+Space",
        "F10"
    ];

    private static readonly string[] RewriteStyles =
    [
        "Plain English",
        "Professional",
        "Friendly",
        "Concise",
        "Native-level English"
    ];

    private static readonly string[] EnglishDialects =
    [
        "Auto",
        "British English",
        "American English",
        "Canadian English",
        "Australian English"
    ];

    public static AppSettings Normalize(AppSettings? settings)
    {
        settings ??= new AppSettings();
        var defaults = new AppSettings();

        settings.ProviderMode = Pick(settings.ProviderMode, ProviderModes, defaults.ProviderMode);
        settings.ProviderName = string.IsNullOrWhiteSpace(settings.ProviderName) ? defaults.ProviderName : settings.ProviderName;
        settings.CleanupMode = Pick(settings.CleanupMode, CleanupModes, defaults.CleanupMode);
        settings.RewriteStyle = Pick(settings.RewriteStyle, RewriteStyles, defaults.RewriteStyle);
        settings.LanguageMode = string.IsNullOrWhiteSpace(settings.LanguageMode) ? defaults.LanguageMode : settings.LanguageMode;
        settings.EnglishDialect = Pick(settings.EnglishDialect, EnglishDialects, defaults.EnglishDialect);
        settings.PasteMethod = Pick(settings.PasteMethod, PasteMethods, defaults.PasteMethod);
        settings.ToggleRecordingHotkey = Pick(settings.ToggleRecordingHotkey, ToggleRecordingHotkeys, defaults.ToggleRecordingHotkey);
        settings.PushToTalkHotkey = Pick(settings.PushToTalkHotkey, PushToTalkHotkeys, defaults.PushToTalkHotkey);
        if (string.Equals(settings.ToggleRecordingHotkey, settings.PushToTalkHotkey, StringComparison.OrdinalIgnoreCase))
        {
            settings.PushToTalkHotkey = settings.ToggleRecordingHotkey == defaults.PushToTalkHotkey
                ? "F10"
                : defaults.PushToTalkHotkey;
        }

        settings.MicrophoneName = string.IsNullOrWhiteSpace(settings.MicrophoneName) ? defaults.MicrophoneName : settings.MicrophoneName;
        settings.ActiveEngine = string.IsNullOrWhiteSpace(settings.ActiveEngine) ? defaults.ActiveEngine : settings.ActiveEngine;
        settings.LocalWhisperExecutablePath = settings.LocalWhisperExecutablePath?.Trim() ?? string.Empty;
        settings.LocalWhisperModelPath = settings.LocalWhisperModelPath?.Trim() ?? string.Empty;
        settings.LocalLlmEndpoint = string.IsNullOrWhiteSpace(settings.LocalLlmEndpoint)
            ? defaults.LocalLlmEndpoint
            : settings.LocalLlmEndpoint.Trim();
        settings.LocalLlmModel = settings.LocalLlmModel?.Trim() ?? string.Empty;
        settings.LocalWhisperCliVersion = settings.LocalWhisperCliVersion?.Trim() ?? string.Empty;
        settings.LocalWhisperModelPresetId = settings.LocalWhisperModelPresetId?.Trim() ?? string.Empty;
        settings.LocalSetupSource = settings.LocalSetupSource?.Trim() ?? string.Empty;
        settings.LastNotifiedUpdateVersion = settings.LastNotifiedUpdateVersion?.Trim() ?? string.Empty;
        settings.MonthlyCostWarning = settings.MonthlyCostWarning < 0 ? defaults.MonthlyCostWarning : settings.MonthlyCostWarning;
        settings.CaptureStartupBufferMilliseconds = settings.CaptureStartupBufferMilliseconds switch
        {
            0 or 250 or 500 or 750 => settings.CaptureStartupBufferMilliseconds,
            _ => defaults.CaptureStartupBufferMilliseconds
        };
        settings.CustomVocabulary ??= [];

        return settings;
    }

    private static string Pick(string? value, IReadOnlyCollection<string> allowedValues, string fallback)
    {
        return allowedValues.Any(allowed => string.Equals(allowed, value, StringComparison.OrdinalIgnoreCase))
            ? allowedValues.First(allowed => string.Equals(allowed, value, StringComparison.OrdinalIgnoreCase))
            : fallback;
    }
}
