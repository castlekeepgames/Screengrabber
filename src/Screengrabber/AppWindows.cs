using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// Visible top-level windows of other apps titled like a new email with this subject: exactly the subject
    /// (Mailbird) or the subject followed by more (Outlook's "Screenshot - Message (HTML)").
    /// </summary>
    public static HashSet<IntPtr> FindComposeWindows(string subject)
    {
        uint ourProcess = (uint)Environment.ProcessId;
        var found = new HashSet<IntPtr>();
        var text = new StringBuilder(256);
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ourProcess || !Native.IsWindowVisible(hwnd))
                return true;
            text.Clear();
            Native.GetWindowText(hwnd, text, text.Capacity);
            string title = text.ToString();
            if (title == subject || title.StartsWith(subject + " ", StringComparison.Ordinal))
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

    /// <summary>
    /// Waits for the named process to show a main window (e.g. after launching it, or while a tray app restores itself).
    /// </summary>
    public static async Task<IntPtr> WaitForMainWindow(string processName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            IntPtr hwnd = FindMainWindow(processName);
            if (hwnd != IntPtr.Zero || DateTime.UtcNow >= deadline)
                return hwnd;
            await Task.Delay(150);
        }
    }

    /// <summary>
    /// Waits for another app's window to come to the front (e.g. after launching something whose process we
    /// don't know). Returns zero if nothing new appeared in time.
    /// </summary>
    public static async Task<IntPtr> WaitForNewForegroundWindow(IntPtr previous, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(150);
            IntPtr hwnd = Native.GetForegroundWindow();
            if (hwnd != IntPtr.Zero && hwnd != previous && !IsOurs(hwnd) && Native.IsWindowVisible(hwnd))
                return hwnd;
        }
        return IntPtr.Zero;
    }

    public static bool IsOurs(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>The top-level window under a screen point (physical pixels).</summary>
    public static IntPtr RootWindowAt(System.Drawing.Point point)
    {
        IntPtr hwnd = WindowFromPoint(new Native.POINT { X = point.X, Y = point.Y });
        return hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GA_ROOT);
    }

    /// <summary>A point inside the window, as fractions of its width and height.</summary>
    public static System.Drawing.Point PointIn(IntPtr hwnd, double fx, double fy)
    {
        GetWindowRect(hwnd, out RECT r);
        return new System.Drawing.Point(r.Left + (int)((r.Right - r.Left) * fx), r.Top + (int)((r.Bottom - r.Top) * fy));
    }

    public static string? ProcessNameOf(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
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

    private const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Native.POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
