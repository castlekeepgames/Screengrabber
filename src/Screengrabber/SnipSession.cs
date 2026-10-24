using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Forms = System.Windows.Forms;

namespace Screengrabber;

/// <summary>
/// One snip from hotkey press to result: freezes every monitor, shows an overlay on each,
/// and finishes with either a region, the whole monitor under the cursor, or nothing (cancel).
/// </summary>
internal sealed class SnipSession
{
    private readonly List<MonitorShot> _shots;
    private readonly List<SnipOverlayWindow> _windows = new();
    private readonly Hotkey _captureHotkey;
    private bool _finished;

    /// <summary>Raised once. The bitmap is null when cancelled; otherwise the receiver owns it.</summary>
    public event EventHandler<Bitmap?>? Finished;

    private SnipSession(List<MonitorShot> shots, Hotkey captureHotkey)
    {
        _shots = shots;
        _captureHotkey = captureHotkey;
    }

    /// <param name="captureHotkey">Shown in the overlay hint ("… for whole screen").</param>
    public static SnipSession Start(Hotkey captureHotkey)
    {
        var session = new SnipSession(ScreenCapture.CaptureAllMonitors(), captureHotkey);
        session.ShowOverlays();
        return session;
    }

    private void ShowOverlays()
    {
        SnipOverlayWindow? underCursor = null;
        foreach (var shot in _shots)
        {
            var window = new SnipOverlayWindow(shot, _captureHotkey);
            window.RegionSelected += (_, region) => Finish(shot.Crop(region));
            window.Cancelled += (_, _) => Cancel();
            window.ShowOnMonitor();
            _windows.Add(window);

            if (shot.Bounds.Contains(Forms.Cursor.Position))
                underCursor = window;
        }

        // Take keyboard focus so Esc works. The hotkey press grants us foreground rights.
        (underCursor ?? _windows[0]).Activate();
    }

    /// <summary>
    /// Creates and shows one overlay off-screen, then closes it. The very first overlay pays for WPF
    /// rendering setup and JIT (~260 ms); doing that at startup keeps the first real snip under 200 ms.
    /// </summary>
    public static void WarmUp(Hotkey captureHotkey)
    {
        var offScreen = new Rectangle(-32000, -32000, 64, 64);
        using var shot = new MonitorShot(offScreen, new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format24bppRgb));
        var window = new SnipOverlayWindow(shot, captureHotkey);
        window.ShowOnMonitor();
        window.UpdateLayout();
        window.Close();
    }

    /// <summary>Saves the frozen image of the monitor the cursor is on.</summary>
    public void CaptureWholeScreen()
    {
        Point cursor = Forms.Cursor.Position;
        MonitorShot shot = _shots.FirstOrDefault(s => s.Bounds.Contains(cursor)) ?? _shots[0];
        Finish(shot.CopyAll());
    }

    public void Cancel() => Finish(null);

    private void Finish(Bitmap? result)
    {
        if (_finished)
        {
            result?.Dispose();
            return;
        }
        _finished = true;

        foreach (var window in _windows)
            window.Close();
        foreach (var shot in _shots)
            shot.Dispose();

        Finished?.Invoke(this, result);
    }
}
