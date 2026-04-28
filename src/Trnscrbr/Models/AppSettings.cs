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
    public string ProviderMode { get; set; } = "Not configured";
    public string ProviderName { get; set; } = "OpenAI";
    public string CleanupMode { get; set; } = "Clean only";
    public string LanguageMode { get; set; } = "Auto";
    public string MicrophoneName { get; set; } = "Windows default";
    public string ActiveEngine { get; set; } = "None";
    public decimal MonthlyCostWarning { get; set; } = 5.00m;
    public List<string> CustomVocabulary { get; set; } = [];
}
