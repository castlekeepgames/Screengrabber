using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Screengrabber;

/// <summary>Carries out a share recipe for a saved screenshot. Nothing here touches the clipboard.</summary>
internal static class ShareService
{
    private const string EmailSubject = "Screenshot";
    private const string BlankEmail = "mailto:?subject=" + EmailSubject;

    /// <summary>How long a launched (or tray-restored) app gets to show its window.</summary>
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Must be called on the UI thread while Screengrabber has the foreground (it hands focus to the target).
    /// <paramref name="beginPointerDrop"/> takes over for point-and-drop once the app is in front
    /// (its window may be zero if it couldn't be identified).
    /// </summary>
    public static async Task Share(ShareTarget target, string imagePath, Action<ShareTarget, string, IntPtr> beginPointerDrop)
    {
        if (target.Method == ShareMethod.Email)
        {
            await ShareByEmail(target, imagePath, beginPointerDrop);
            return;
        }

        IntPtr window = await FocusOrLaunch(target);
        if (window == IntPtr.Zero && target.FocusProcess != null)
            throw new InvalidOperationException($"{target.Label} didn't open a window.");
        await Task.Delay(300); // let it come forward and repaint

        if (window != IntPtr.Zero && !string.IsNullOrWhiteSpace(target.KeysAfterOpening))
        {
            SyntheticInput.Press(Hotkey.Parse(target.KeysAfterOpening));
            await Task.Delay(200);
        }

        switch (target.Delivery)
        {
            case ShareDelivery.DropOnWindow when window != IntPtr.Zero:
                // Right of centre and a bit low: the conversation area, not a sidebar.
                var point = AppWindows.PointIn(window, 0.6, 0.6);
                if (!await FileDropper.DropAt(imagePath, point.X, point.Y))
                    throw new InvalidOperationException($"{target.Label} didn't accept the file. You can drag the thumbnail into it instead.");
                AppWindows.BringToFront(window);
                break;

            case ShareDelivery.DropOnWindow:   // couldn't tell which window is the app: let the user point instead
            case ShareDelivery.DropAtPointer:
                beginPointerDrop(target, imagePath, window);
                break;

            case ShareDelivery.TypePath:
                // Only type if the app really is in front; typing into the wrong window is worse than doing nothing.
                if (window == IntPtr.Zero || Native.GetForegroundWindow() != window)
                    throw new InvalidOperationException($"{target.Label} didn't come to the front, so the path wasn't typed.");
                SyntheticInput.TypeText(imagePath);
                break;

            case ShareDelivery.OpenOnly:
                break;
        }
    }

    /// <summary>
    /// Opens a new email with the file attached through the default mail program. If it has no MAPI support
    /// (new Outlook, webmail), opens a blank email and lets the user point at it to drop the file in.
    /// </summary>
    private static async Task ShareByEmail(ShareTarget target, string imagePath, Action<ShareTarget, string, IntPtr> beginPointerDrop)
    {
        // Older compose windows may share the subject; remember them so we focus the new one.
        var existing = AppWindows.FindComposeWindows(EmailSubject);

        int result = await MapiMail.ComposeWithAttachment(imagePath, EmailSubject);
        if (result == MapiMail.SUCCESS_SUCCESS)
        {
            // Some clients (Mailbird) open the compose window behind everything; pull it forward.
            await FocusNewComposeWindow(existing, TimeSpan.FromSeconds(3));
            return;
        }
        if (result == MapiMail.MAPI_USER_ABORT)
            return;

        Log.Info($"Email share: MAPI returned {result}; opening a blank email for point-and-drop");
        IntPtr previous = Native.GetForegroundWindow();
        AppWindows.Launch(string.IsNullOrWhiteSpace(target.Launch) ? BlankEmail : target.Launch);
        IntPtr compose = await AppWindows.WaitForNewForegroundWindow(previous, LaunchTimeout);
        beginPointerDrop(target, imagePath, compose);
    }

    private static async Task FocusNewComposeWindow(HashSet<IntPtr> existing, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (IntPtr hwnd in AppWindows.FindComposeWindows(EmailSubject))
            {
                if (!existing.Contains(hwnd))
                {
                    Log.Info($"Email share: focusing new compose window {hwnd} ({AppWindows.ProcessNameOf(hwnd)})");
                    AppWindows.BringToFront(hwnd);
                    // Mailbird hands focus away from its compose window a few seconds after opening it.
                    _ = KeepInFront(hwnd, TimeSpan.FromSeconds(6));
                    return;
                }
            }
            await Task.Delay(100);
        }
        Log.Info($"Email share: no new '{EmailSubject}' window appeared within {timeout.TotalSeconds}s");
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

    /// <returns>The app's main window, or zero if it couldn't be identified.</returns>
    private static async Task<IntPtr> FocusOrLaunch(ShareTarget target)
    {
        if (target.FocusProcess != null)
        {
            IntPtr hwnd = AppWindows.FindMainWindow(target.FocusProcess);
            if (hwnd != IntPtr.Zero)
            {
                AppWindows.BringToFront(hwnd);
                return hwnd;
            }
        }

        // Not running, or hidden in the tray: launching again shows the existing instance for single-instance apps.
        IntPtr previous = Native.GetForegroundWindow();
        if (target.Launch != null)
            AppWindows.Launch(target.Launch);

        IntPtr launched = target.FocusProcess != null
            ? await AppWindows.WaitForMainWindow(target.FocusProcess, LaunchTimeout)
            // Process unknown (e.g. added from the installed-apps list): follow whatever window comes forward.
            : await AppWindows.WaitForNewForegroundWindow(previous, LaunchTimeout);
        if (launched != IntPtr.Zero)
            AppWindows.BringToFront(launched);
        return launched;
    }
}
