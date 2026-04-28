using Microsoft.Win32;
using Trnscrbr.Models;

namespace Trnscrbr.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Trnscrbr";

    public static void Apply(AppSettings settings)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (!settings.LaunchOnStartup)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        key.SetValue(ValueName, $"\"{processPath}\"");
    }
}
