using Microsoft.Win32;

namespace ClaudeWidget.Core;

public static class Autostart
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeWidget";

    public static bool IsEnabled(string keyPath = RunKeyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ValueName) is string;
    }

    public static void Enable(string exePath, string keyPath = RunKeyPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(ValueName, $"\"{exePath}\"");
    }

    public static void Disable(string keyPath = RunKeyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
