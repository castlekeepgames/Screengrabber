using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Interop;

namespace Screengrabber;

/// <summary>
/// Writes the clipboard through Win32 directly. Windows owns each block after SetClipboardData and frees it
/// when the clipboard next changes. (WinForms' Bitmap path left a screen-sized GDI bitmap behind per copy.)
/// </summary>
internal static class ClipboardHelper
{
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_DIB = 8;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const int BitmapFileHeaderSize = 14;

    // Another app can briefly hold the clipboard open; retry instead of failing.
    private const int RetryTimes = 10;
    private const int RetryDelayMs = 50;

    private static readonly uint PngFormat = RegisterClipboardFormat("PNG");
    private static HwndSource? _owner;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr hMem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    /// <summary>
    /// Puts the image on the clipboard as a DIB (classic apps; Windows derives Bitmap from it) and as PNG
    /// (which Slack, browsers, and Electron apps prefer).
    /// </summary>
    public static void SetImage(Bitmap image, byte[] png)
    {
        byte[] dib = ToDib(image);
        Write((CF_DIB, dib), (PngFormat, png));
    }

    public static void SetImageFromFile(string pngPath)
    {
        byte[] png = File.ReadAllBytes(pngPath);
        using var bitmap = new Bitmap(new MemoryStream(png));
        SetImage(bitmap, png);
    }

    public static void SetText(string text) =>
        Write((CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0")));

    /// <summary>A .bmp file minus its 14-byte file header is exactly the CF_DIB layout.</summary>
    private static byte[] ToDib(Bitmap image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Bmp);
        return stream.GetBuffer().AsSpan(BitmapFileHeaderSize, (int)stream.Length - BitmapFileHeaderSize).ToArray();
    }

    private static void Write(params (uint Format, byte[] Data)[] items)
    {
        // EmptyClipboard needs an owner window, or SetClipboardData may fail.
        _owner ??= new HwndSource(new HwndSourceParameters("ScreengrabberClipboard") { ParentWindow = Native.HWND_MESSAGE, WindowStyle = 0 });

        for (int attempt = 0; !OpenClipboard(_owner.Handle); attempt++)
        {
            if (attempt >= RetryTimes)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The clipboard is in use by another app.");
            Thread.Sleep(RetryDelayMs);
        }

        try
        {
            EmptyClipboard();
            foreach (var (format, data) in items)
                SetData(format, data);
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static void SetData(uint format, byte[] data)
    {
        IntPtr handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
        if (handle == IntPtr.Zero)
            throw new OutOfMemoryException("Couldn't allocate clipboard memory.");

        IntPtr target = GlobalLock(handle);
        Marshal.Copy(data, 0, target, data.Length);
        GlobalUnlock(handle);

        if (SetClipboardData(format, handle) == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            GlobalFree(handle); // still ours if the clipboard refused it
            throw new Win32Exception(error, "Couldn't write to the clipboard.");
        }
    }
}
