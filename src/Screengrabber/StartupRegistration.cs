using System;
using Microsoft.Win32;

namespace Screengrabber;

/// <summary>Start with Windows via the per-user Run key (no admin rights needed).</summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Screengrabber";

    /// <summary>Passed when Windows starts the app, so it starts quietly (no "running" notification).</summary>
    public const string StartupArg = "--startup";

    /// <summary>
    /// Adds or removes the Run entry. When enabled it always points at the running exe,
    /// so moving or reinstalling the app keeps startup working.
    /// </summary>
    public static void Apply(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled && Environment.ProcessPath is string exe)
            key.SetValue(ValueName, $"\"{exe}\" {StartupArg}");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
