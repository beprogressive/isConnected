using System.Reflection;

namespace IsConnected;

internal static class ApplicationInfo
{
    public const string RepositoryUrl = "https://github.com/beprogressive/isConnected";

    public static string Version
    {
        get
        {
            var assembly = typeof(ApplicationInfo).Assembly;
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion;
            }

            return assembly.GetName().Version?.ToString() ?? "Unknown";
        }
    }
}
