namespace Trnscrbr.Models;

public sealed class AppSettings
{
    public bool OnboardingCompleted { get; set; }
    public bool LaunchOnStartup { get; set; } = true;
    public bool FloatingButtonEnabled { get; set; }
    public bool AddTrailingSpace { get; set; } = true;
    public bool ContextualCorrectionEnabled { get; set; } = true;
    public bool CursorContextEnabled { get; set; }
    public bool VoiceActionCommandsEnabled { get; set; }
    public bool DiagnosticsEnabled { get; set; }
    public bool ForceCpuOnly { get; set; }
    public int CaptureStartupBufferMilliseconds { get; set; }
    public string ProviderMode { get; set; } = "Not configured";
    public string ProviderName { get; set; } = "OpenAI";
    public string CleanupMode { get; set; } = "Clean only";
    public string RewriteStyle { get; set; } = "Plain English";
    public string LanguageMode { get; set; } = "Auto";
    public string EnglishDialect { get; set; } = "Auto";
    public string PasteMethod { get; set; } = "Ctrl+V";
    public string ToggleRecordingHotkey { get; set; } = "Ctrl+Alt+R";
    public string PushToTalkHotkey { get; set; } = "Ctrl+Alt+Space";
    public string MicrophoneName { get; set; } = "Windows default";
    public string ActiveEngine { get; set; } = "None";
    public string LocalWhisperExecutablePath { get; set; } = string.Empty;
    public string LocalWhisperModelPath { get; set; } = string.Empty;
    public string LocalLlmEndpoint { get; set; } = "http://localhost:11434/api/chat";
    public string LocalLlmModel { get; set; } = string.Empty;
    public string LocalWhisperCliVersion { get; set; } = string.Empty;
    public string LocalWhisperModelPresetId { get; set; } = string.Empty;
    public string LocalSetupSource { get; set; } = string.Empty;
    public DateTimeOffset? LocalSetupCompletedAt { get; set; }
    public string LastNotifiedUpdateVersion { get; set; } = string.Empty;
    public decimal MonthlyCostWarning { get; set; } = 5.00m;
    public List<string> CustomVocabulary { get; set; } = [];
}
