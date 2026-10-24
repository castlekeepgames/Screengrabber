using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Screengrabber;

/// <summary>
/// A capture briefly holds several full-screen bitmaps. Once things go idle, compact the heap and hand
/// the freed pages back to Windows, so a resident tray app doesn't sit on hundreds of MB.
/// </summary>
internal static class MemoryTrim
{
    private static readonly DispatcherTimer Timer = CreateTimer();

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumSize, IntPtr maximumSize);

    /// <summary>Trims a couple of seconds from now. Calling again restarts the countdown.</summary>
    public static void Schedule()
    {
        Timer.Stop();
        Timer.Start();
    }

    private static DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Trim();
        };
        return timer;
    }

    private static void Trim()
    {
        // Screen bitmaps live on the large object heap, which isn't compacted unless asked.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Release physical RAM. Measured: the next overlay still opens in ~110 ms, since the pages come back
        // from the standby list rather than disk.
        using var process = Process.GetCurrentProcess();
        SetProcessWorkingSetSize(process.Handle, -1, -1);
    }
}
