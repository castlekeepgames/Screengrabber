using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Screengrabber;

/// <summary>Carries out a share recipe for a saved screenshot.</summary>
internal static class ShareService
{
    private const string EmailSubject = "Screenshot";

    /// <summary>Must be called on the UI thread while Screengrabber has the foreground (it hands focus to the target).</summary>
    public static async Task Share(ShareTarget target, string imagePath)
    {
        if (target.Method == ShareMethod.Email)
        {
            await ShareByEmail(target, imagePath);
            return;
        }

        SetClipboard(target.Clipboard, imagePath);
        bool launched = FocusOrLaunch(target);

        if (target.AutoPaste && target.FocusProcess != null)
        {
            // A cold start takes a while to get a window up.
            await AppWindows.PasteInto(target.FocusProcess, TimeSpan.FromSeconds(launched ? 10 : 2));
        }
    }

    private static async Task ShareByEmail(ShareTarget target, string imagePath)
    {
        // Older compose windows may share the subject; remember them so we focus the new one.
        var existing = target.FocusProcess != null
            ? AppWindows.FindWindowsByTitle(target.FocusProcess, EmailSubject)
            : new HashSet<IntPtr>();

        int result;
        try
        {
            result = await MapiMail.ComposeWithAttachment(imagePath, EmailSubject);
        }
        catch (DllNotFoundException)
        {
            result = -1; // no MAPI on this system
        }
        if (result != MapiMail.SUCCESS_SUCCESS)
            Log.Info($"Email share: MAPISendMail returned {result}");

        if (result == MapiMail.SUCCESS_SUCCESS)
        {
            // Some clients (Mailbird) open the compose window behind everything; pull it forward.
            if (target.FocusProcess != null)
                await FocusNewWindow(target.FocusProcess, EmailSubject, existing, TimeSpan.FromSeconds(3));
            return;
        }
        if (result == MapiMail.MAPI_USER_ABORT)
            return;

        // MAPI unavailable or failed: open a blank message and let the user paste the image.
        ClipboardHelper.SetImageFromFile(imagePath);
        if (target.Launch != null)
            AppWindows.Launch(target.Launch);
    }

    private static async Task FocusNewWindow(string processName, string title, HashSet<IntPtr> existing, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (IntPtr hwnd in AppWindows.FindWindowsByTitle(processName, title))
            {
                if (!existing.Contains(hwnd))
                {
                    AppWindows.BringToFront(hwnd);
                    // Mailbird hands focus away from its compose window a few seconds after opening it.
                    _ = KeepInFront(hwnd, TimeSpan.FromSeconds(6));
                    return;
                }
            }
            await Task.Delay(100);
        }
        Log.Info($"Email share: no new '{title}' window from {processName} within {timeout.TotalSeconds}s");
    }

    /// <summary>
    /// Pulls the window back to the front if something other than the user takes focus away within the period.
    /// Any keyboard or mouse input from the user ends the watch, so a deliberate switch is never undone.
    /// </summary>
    private static async Task KeepInFront(IntPtr hwnd, TimeSpan period)
    {
        uint inputAtStart = Native.LastInputTick();
        var deadline = DateTime.UtcNow + period;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            if (Native.LastInputTick() != inputAtStart || !Native.IsWindowVisible(hwnd))
                return;
            if (Native.GetForegroundWindow() != hwnd)
                AppWindows.BringToFront(hwnd);
        }
    }

    private static void SetClipboard(ClipboardContent content, string imagePath)
    {
        switch (content)
        {
            case ClipboardContent.Image:
                ClipboardHelper.SetImageFromFile(imagePath);
                break;
            case ClipboardContent.Path:
                ClipboardHelper.SetText(imagePath);
                break;
        }
    }

    /// <returns>True if the app had to be launched (no window was found).</returns>
    private static bool FocusOrLaunch(ShareTarget target)
    {
        if (target.FocusProcess != null)
        {
            IntPtr hwnd = AppWindows.FindMainWindow(target.FocusProcess);
            if (hwnd != IntPtr.Zero)
            {
                AppWindows.BringToFront(hwnd);
                return false;
            }
        }

        // Not running, or hidden in the tray: launching again shows the existing instance for single-instance apps.
        if (target.Launch != null)
            AppWindows.Launch(target.Launch);
        return true;
    }
}
