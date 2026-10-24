using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Screengrabber;

/// <summary>Finding, launching, and focusing other apps' windows.</summary>
internal static class AppWindows
{
    /// <summary>The main window of the first running process with this name, or zero.</summary>
    public static IntPtr FindMainWindow(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                    return process.MainWindowHandle;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Visible top-level windows of the named process with exactly this title.</summary>
    public static HashSet<IntPtr> FindWindowsByTitle(string processName, string title)
    {
        var ids = Process.GetProcessesByName(processName).Select(p => { using (p) return (uint)p.Id; }).ToHashSet();
        var found = new HashSet<IntPtr>();
        var text = new StringBuilder(256);
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (!ids.Contains(pid) || !Native.IsWindowVisible(hwnd))
                return true;
            text.Clear();
            Native.GetWindowText(hwnd, text, text.Capacity);
            if (text.ToString() == title)
                found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static void BringToFront(IntPtr hwnd)
    {
        if (Native.IsIconic(hwnd))
            Native.ShowWindow(hwnd, Native.SW_RESTORE);
        if (Native.SetForegroundWindow(hwnd) && Native.GetForegroundWindow() == hwnd)
            return;

        // Windows refuses focus changes from a process that isn't in the foreground. Sharing the current
        // foreground window's input state lifts that restriction for this one call.
        uint foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        uint ourThread = Native.GetCurrentThreadId();
        bool attached = foregroundThread != ourThread && Native.AttachThreadInput(ourThread, foregroundThread, true);
        try
        {
            Native.BringWindowToTop(hwnd);
            Native.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                Native.AttachThreadInput(ourThread, foregroundThread, false);
        }
    }

    /// <summary>Opens an exe, URI, or <c>shell:AppsFolder\&lt;AppID&gt;</c>.</summary>
    public static void Launch(string target)
    {
        // Let whatever we start take the foreground; we have the right to grant it because the user just interacted with us.
        Native.AllowSetForegroundWindow(Native.ASFW_ANY);

        string expanded = Environment.ExpandEnvironmentVariables(target);
        var start = expanded.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("explorer.exe", $"\"{expanded}\"")
            : new ProcessStartInfo(expanded) { UseShellExecute = true };
        Process.Start(start)?.Dispose();
    }

    /// <summary>Presses Ctrl+V, but only if the expected process ends up in front within the timeout.</summary>
    public static async Task PasteInto(string processName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (ForegroundProcessName() is string name && name.Equals(processName, StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(150); // let the app finish focusing its input box
                Native.keybd_event(Native.VK_CONTROL, 0, 0, UIntPtr.Zero);
                Native.keybd_event(Native.VK_V, 0, 0, UIntPtr.Zero);
                Native.keybd_event(Native.VK_V, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Native.keybd_event(Native.VK_CONTROL, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                return;
            }
            await Task.Delay(100);
        }
    }

    private static string? ForegroundProcessName()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
