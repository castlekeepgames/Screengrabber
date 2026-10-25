using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace Screengrabber;

/// <summary>
/// A large white crosshair with a dark outline, readable on both the dimmed overlay and bright content.
/// Built as a real Windows cursor (not drawn by WPF), so it tracks the mouse with no lag.
/// The centre is left open so the pixel being aimed at stays visible.
/// </summary>
internal static class CrosshairCursor
{
    /// <summary>Cursor size at 100% scaling. Odd, so there's an exact centre pixel for the hotspot.</summary>
    private const int BaseSize = 41;

    private static readonly Dictionary<int, Cursor> Cache = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool DestroyCursor(IntPtr hCursor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>The crosshair sized for a monitor at this scale (1.0 = 100%). Falls back to the system cross.</summary>
    public static Cursor For(double scale)
    {
        int size = (int)Math.Round(BaseSize * scale) | 1;
        if (Cache.TryGetValue(size, out Cursor? cursor))
            return cursor;

        try
        {
            cursor = Create(size, scale);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            cursor = Cursors.Cross;
        }
        Cache[size] = cursor;
        return cursor;
    }

    private static Cursor Create(int size, double scale)
    {
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.None;
            float c = size / 2;                                  // centre pixel
            float gap = (float)Math.Round(4 * scale);           // open space around the centre
            float white = Math.Max(2, (float)Math.Round(2 * scale));
            float outline = white + 2 * Math.Max(1, (float)Math.Round(scale));
            float end = size - 1;

            // Four arms from the edges toward the centre: dark outline first, white on top.
            void Arms(Pen pen, float trim)
            {
                g.DrawLine(pen, c, 0 + trim, c, c - gap - trim);
                g.DrawLine(pen, c, c + gap + trim, c, end - trim);
                g.DrawLine(pen, 0 + trim, c, c - gap - trim, c);
                g.DrawLine(pen, c + gap + trim, c, end - trim, c);
            }

            using var dark = new Pen(Color.FromArgb(230, 0, 0, 0), outline);
            using var light = new Pen(Color.White, white);
            Arms(dark, 0);
            Arms(light, (outline - white) / 2);
        }

        // GetHicon makes an icon; turn it into a cursor with the hotspot on the centre pixel.
        IntPtr icon = bitmap.GetHicon();
        try
        {
            if (!GetIconInfo(icon, out ICONINFO info))
                throw new InvalidOperationException("GetIconInfo failed.");
            try
            {
                info.fIcon = false;
                info.xHotspot = size / 2;
                info.yHotspot = size / 2;
                IntPtr handle = CreateIconIndirect(ref info);
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException("CreateIconIndirect failed.");
                return CursorInteropHelper.Create(new CursorHandle(handle));
            }
            finally
            {
                DeleteObject(info.hbmMask);
                DeleteObject(info.hbmColor);
            }
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    private sealed class CursorHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public CursorHandle(IntPtr handle) : base(ownsHandle: true) => SetHandle(handle);

        protected override bool ReleaseHandle() => DestroyCursor(handle);
    }
}
