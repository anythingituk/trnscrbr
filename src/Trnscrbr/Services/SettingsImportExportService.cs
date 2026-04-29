using System.IO;
using System.Text.Json;
using Trnscrbr.Models;

namespace Trnscrbr.Services;

public sealed class SettingsImportExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public void Export(string path, AppSettings settings)
    {
        var export = new SettingsExport
        {
            Settings = Clone(settings)
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        File.WriteAllText(path, json);
    }

    public AppSettings Import(string path)
    {
        var json = File.ReadAllText(path);
        var export = JsonSerializer.Deserialize<SettingsExport>(json)
            ?? throw new InvalidOperationException("The selected file is not a valid Trnscrbr settings export.");

        var settings = AppSettingsNormalizer.Normalize(export.Settings);

        // Provider secrets are never exported. Keep the provider name, but require local key setup.
        if (settings.ProviderMode == "Bring your own API key")
        {
            settings.ActiveEngine = "OpenAI";
        }

        return AppSettingsNormalizer.Normalize(settings);
    }

    private static AppSettings Clone(AppSettings settings)
    {
        AppSettingsNormalizer.Normalize(settings);

        return new AppSettings
        {
            OnboardingCompleted = settings.OnboardingCompleted,
            LaunchOnStartup = settings.LaunchOnStartup,
            FloatingButtonEnabled = settings.FloatingButtonEnabled,
            AddTrailingSpace = settings.AddTrailingSpace,
            ContextualCorrectionEnabled = settings.ContextualCorrectionEnabled,
            CursorContextEnabled = settings.CursorContextEnabled,
            VoiceActionCommandsEnabled = settings.VoiceActionCommandsEnabled,
            DiagnosticsEnabled = settings.DiagnosticsEnabled,
            ForceCpuOnly = settings.ForceCpuOnly,
            CaptureStartupBufferMilliseconds = settings.CaptureStartupBufferMilliseconds,
            ProviderMode = settings.ProviderMode,
            ProviderName = settings.ProviderName,
            CleanupMode = settings.CleanupMode,
            RewriteStyle = settings.RewriteStyle,
            LanguageMode = settings.LanguageMode,
            EnglishDialect = settings.EnglishDialect,
            PasteMethod = settings.PasteMethod,
            MicrophoneName = settings.MicrophoneName,
            ActiveEngine = settings.ActiveEngine,
            MonthlyCostWarning = settings.MonthlyCostWarning,
            CustomVocabulary = settings.CustomVocabulary.ToList()
        };
    }
}
