using System.Diagnostics;
using System.Runtime.InteropServices;
using Trnscrbr.Models;

namespace Trnscrbr.Services;

public sealed class EnvironmentDiagnosticsService
{
    private static readonly string[] InterestingProcessNames =
    [
        "TextInputHost",
        "VoiceAccess",
        "SpeechRuntime",
        "PowerToys",
        "PowerToys.QuickAccess",
        "PowerToys.KeyboardManagerEngine"
    ];

    private readonly DiagnosticLogService _diagnosticLog;

    public EnvironmentDiagnosticsService(DiagnosticLogService diagnosticLog)
    {
        _diagnosticLog = diagnosticLog;
    }

    public void LogStartupSnapshot(AppSettings settings, bool hasOpenAiApiKey)
    {
        _diagnosticLog.Info("Application startup", new Dictionary<string, string>
        {
            ["appVersion"] = AppInfo.Version,
            ["os"] = RuntimeInformation.OSDescription,
            ["framework"] = RuntimeInformation.FrameworkDescription,
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["providerMode"] = settings.ProviderMode,
            ["providerName"] = settings.ProviderName,
            ["activeEngine"] = settings.ActiveEngine,
            ["apiKeyPresent"] = hasOpenAiApiKey ? "yes" : "no",
            ["microphone"] = settings.MicrophoneName,
            ["hotkeys"] = $"toggle {settings.ToggleRecordingHotkey}, push-to-talk {settings.PushToTalkHotkey}, Esc",
            ["localModelPreset"] = string.IsNullOrWhiteSpace(settings.LocalWhisperModelPresetId) ? "not set" : settings.LocalWhisperModelPresetId
        });

        var detectedProcesses = GetDetectedProcesses();
        if (detectedProcesses.Length > 0)
        {
            _diagnosticLog.Info("Potential input or hotkey companion processes detected", new Dictionary<string, string>
            {
                ["processes"] = string.Join(", ", detectedProcesses)
            });
        }
    }

    private static string[] GetDetectedProcesses()
    {
        try
        {
            var processNames = Process.GetProcesses()
                .Select(process => process.ProcessName)
                .Where(name => InterestingProcessNames.Any(interesting =>
                    name.Contains(interesting, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return processNames;
        }
        catch
        {
            return [];
        }
    }
}
