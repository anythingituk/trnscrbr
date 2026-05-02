using System.Reflection;

namespace Trnscrbr;

public static class AppInfo
{
    public const string ReleasesUrl = "https://github.com/anythingituk/trnscrbr/releases";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public static string DisplayVersion { get; } = Version.Split('+')[0];
}
