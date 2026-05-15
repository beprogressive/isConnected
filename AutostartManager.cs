using Microsoft.Win32;
using System.Diagnostics;

namespace IsConnected;

internal static class AutostartManager
{
    private const string AppName = "IsConnected";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(AppName) as string;
            return string.Equals(value, Quote(GetExecutablePath()), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

        if (enabled)
        {
            if (key is null)
            {
                throw new InvalidOperationException("Windows Run registry key is unavailable.");
            }

            key.SetValue(AppName, Quote(GetExecutablePath()), RegistryValueKind.String);
            return;
        }

        key?.DeleteValue(AppName, false);
    }

    private static string GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return processPath;
        }

        return Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
    }

    private static string Quote(string value) => $"\"{value}\"";
}
