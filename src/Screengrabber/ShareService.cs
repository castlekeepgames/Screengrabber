using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Screengrabber;

/// <summary>Carries out a share recipe for a saved screenshot. Nothing here touches the clipboard.</summary>
internal static class ShareService
{
    private const string EmailSubject = "Screenshot";

    /// <summary>How long a launched (or tray-restored) app gets to show its window.</summary>
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Must be called on the UI thread while Screengrabber has the foreground (it hands focus to the target).
    /// <paramref name="beginPointerDrop"/> takes over for <see cref="ShareDelivery.DropAtPointer"/> targets once the app is in front.
    /// </summary>
    public static async Task Share(ShareTarget target, string imagePath, Action<ShareTarget, string, IntPtr> beginPointerDrop)
    {
        if (target.Method == ShareMethod.Email)
        {
            await ShareByEmail(target, imagePath);
            return;
        }

        IntPtr window = await FocusOrLaunch(target);
        if (window == IntPtr.Zero)
        {
            if (target.FocusProcess != null)
                throw new InvalidOperationException($"{target.Label} didn't open a window.");
            return; // launched something we can't track (e.g. a URL); nothing more to do
        }
        await Task.Delay(300); // let it come forward and repaint

        if (!string.IsNullOrWhiteSpace(target.KeysAfterOpening))
        {
            SyntheticInput.Press(Hotkey.Parse(target.KeysAfterOpening));
            await Task.Delay(200);
        }

        switch (target.Delivery)
        {
            case ShareDelivery.DropOnWindow:
                // Right of centre and a bit low: the conversation area, not a sidebar.
                var point = AppWindows.PointIn(window, 0.6, 0.6);
                if (!await FileDropper.DropAt(imagePath, point.X, point.Y))
                    throw new InvalidOperationException($"{target.Label} didn't accept the file. You can drag the thumbnail into it instead.");
                AppWindows.BringToFront(window);
                break;

            case ShareDelivery.DropAtPointer:
                beginPointerDrop(target, imagePath, window);
                break;

            case ShareDelivery.TypePath:
                // Only type if the app really is in front; typing into the wrong window is worse than doing nothing.
                if (target.FocusProcess != null && !AppWindows.IsForeground(target.FocusProcess))
                    throw new InvalidOperationException($"{target.Label} didn't come to the front, so the path wasn't typed.");
                SyntheticInput.TypeText(imagePath);
                break;

            case ShareDelivery.OpenOnly:
                break;
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

        if (result == MapiMail.SUCCESS_SUCCESS)
        {
            // Some clients (Mailbird) open the compose window behind everything; pull it forward.
            if (target.FocusProcess != null)
                await FocusNewWindow(target.FocusProcess, EmailSubject, existing, TimeSpan.FromSeconds(3));
            return;
        }
        if (result == MapiMail.MAPI_USER_ABORT)
            return;

        // MAPI unavailable or failed: open a blank message and show the file, ready to drag in.
        Log.Info($"Email share: MAPISendMail returned {result}");
        if (target.Launch != null)
            AppWindows.Launch(target.Launch);
        ScreenshotStore.OpenFolder(System.IO.Path.GetDirectoryName(imagePath)!, imagePath);
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

    /// <returns>The app's main window, or zero if none appeared.</returns>
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
        if (target.Launch != null)
            AppWindows.Launch(target.Launch);
        if (target.FocusProcess == null)
            return IntPtr.Zero;

        IntPtr launched = await AppWindows.WaitForMainWindow(target.FocusProcess, LaunchTimeout);
        if (launched != IntPtr.Zero)
            AppWindows.BringToFront(launched);
        return launched;
    }
}
