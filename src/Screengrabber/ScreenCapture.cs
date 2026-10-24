using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Screengrabber;

/// <summary>A frozen image of one monitor. Bounds are in physical (virtual-screen) pixels.</summary>
internal sealed class MonitorShot : IDisposable
{
    public MonitorShot(Rectangle bounds, Bitmap image)
    {
        Bounds = bounds;
        Image = image;
    }

    public Rectangle Bounds { get; }
    public Bitmap Image { get; }

    public Bitmap Crop(Rectangle region) => Image.Clone(region, Image.PixelFormat);

    public Bitmap CopyAll() => Crop(new Rectangle(0, 0, Image.Width, Image.Height));

    public void Dispose() => Image.Dispose();
}

internal static class ScreenCapture
{
    public static List<MonitorShot> CaptureAllMonitors()
    {
        var shots = new List<MonitorShot>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            Rectangle bounds = screen.Bounds;
            // 24bpp: screen pixels carry no meaningful alpha, and 32bppArgb can produce transparent PNGs.
            var bmp = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            shots.Add(new MonitorShot(bounds, bmp));
        }
        return shots;
    }

    /// <summary>Fast GDI+ → WPF conversion via a direct pixel copy (no HBITMAP round-trip).</summary>
    public static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        try
        {
            var source = BitmapSource.Create(
                bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgr24, null,
                data.Scan0, data.Stride * bmp.Height, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
