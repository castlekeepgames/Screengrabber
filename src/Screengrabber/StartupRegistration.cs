using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace Screengrabber;

/// <summary>
/// Start with Windows via a per-user scheduled task that runs at sign-in (no admin rights needed).
/// </summary>
/// <remarks>
/// Earlier versions used the HKCU Run key. Windows walks that key by position while launching each entry, and apps
/// that rewrite their own entry as they start shift the newest entries into slots it has already passed, so those
/// are silently skipped (seen on a real sign-in). It's also slow: entries start one by one, ~30 s apart under load.
/// A logon task avoids both. The Run key remains the fallback if the task can't be created.
/// </remarks>
internal static class StartupRegistration
{
    private const string TaskName = "Screengrabber";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Screengrabber";

    /// <summary>Passed when Windows starts the app, so it starts quietly (no "running" notification).</summary>
    public const string StartupArg = "--startup";

    /// <summary>
    /// Adds or removes the sign-in task. When enabled it always points at the running exe, so moving or
    /// reinstalling the app keeps startup working. Runs schtasks.exe, so call it off the UI thread.
    /// </summary>
    public static void Apply(bool enabled)
    {
        SetRunEntry(null);
        if (!enabled || Environment.ProcessPath is not string exe)
        {
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            return;
        }
        if (!CreateLogonTask(exe))
        {
            Log.Info("Startup: couldn't create the sign-in task; using the Run key instead");
            SetRunEntry($"\"{exe}\" {StartupArg}");
        }
    }

    private static bool CreateLogonTask(string exe)
    {
        string user = $@"{Environment.UserDomainName}\{Environment.UserName}";
        // Delay: give the taskbar a moment so the tray icon appears straight away.
        // ExecutionTimeLimit PT0S: otherwise Task Scheduler stops the app after 3 days.
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts Screengrabber when you sign in. Turn off in Screengrabber Settings.</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT10S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(exe)}</Command><Arguments>{StartupArg}</Arguments></Exec>
              </Actions>
            </Task>
            """;

        string file = Path.Combine(Path.GetTempPath(), $"screengrabber-task-{Environment.ProcessId}.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <returns>True if schtasks succeeded.</returns>
    private static bool RunSchtasks(string arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start);
        if (process == null)
            return false;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit(10_000);
        return process.HasExited && process.ExitCode == 0;
    }

    /// <summary>Writes the Run entry, or removes it when <paramref name="command"/> is null.</summary>
    private static void SetRunEntry(string? command)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (command != null)
            key.SetValue(ValueName, command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
