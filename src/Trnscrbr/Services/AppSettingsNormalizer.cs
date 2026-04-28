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

    public static AppSettings Normalize(AppSettings? settings)
    {
        settings ??= new AppSettings();
        var defaults = new AppSettings();

        settings.ProviderMode = Pick(settings.ProviderMode, ProviderModes, defaults.ProviderMode);
        settings.ProviderName = string.IsNullOrWhiteSpace(settings.ProviderName) ? defaults.ProviderName : settings.ProviderName;
        settings.CleanupMode = Pick(settings.CleanupMode, CleanupModes, defaults.CleanupMode);
        settings.LanguageMode = string.IsNullOrWhiteSpace(settings.LanguageMode) ? defaults.LanguageMode : settings.LanguageMode;
        settings.PasteMethod = Pick(settings.PasteMethod, PasteMethods, defaults.PasteMethod);
        settings.MicrophoneName = string.IsNullOrWhiteSpace(settings.MicrophoneName) ? defaults.MicrophoneName : settings.MicrophoneName;
        settings.ActiveEngine = string.IsNullOrWhiteSpace(settings.ActiveEngine) ? defaults.ActiveEngine : settings.ActiveEngine;
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
