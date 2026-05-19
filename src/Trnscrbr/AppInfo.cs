using System.Reflection;

namespace Trnscrbr;

public static class AppInfo
{
    public const string ReleasesUrl = "https://github.com/anythingituk/trnscrbr/releases";
    public const string LatestReleaseUrl = "https://github.com/anythingituk/trnscrbr/releases/latest";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public static string DisplayVersion { get; } = Version.Split('+')[0];
}
