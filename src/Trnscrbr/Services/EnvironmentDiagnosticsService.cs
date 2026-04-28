using System.Diagnostics;
using System.Runtime.InteropServices;

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

    public void LogStartupSnapshot()
    {
        _diagnosticLog.Info("Application startup", new Dictionary<string, string>
        {
            ["appVersion"] = AppInfo.Version,
            ["os"] = RuntimeInformation.OSDescription,
            ["framework"] = RuntimeInformation.FrameworkDescription,
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["hotkeys"] = "Ctrl+Win+Space, Esc"
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
