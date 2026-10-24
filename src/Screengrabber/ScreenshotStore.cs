using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Screengrabber;

internal static class ScreenshotStore
{
    /// <summary>Encodes once so the same bytes can go to disk, the clipboard, and the thumbnail.</summary>
    public static byte[] EncodePng(Bitmap image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <returns>The full path of the saved PNG.</returns>
    public static string Save(byte[] png, string folder)
    {
        Directory.CreateDirectory(folder);

        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string path = Path.Combine(folder, $"Screenshot_{stamp}.png");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"Screenshot_{stamp}_{n}.png");

        File.WriteAllBytes(path, png);
        return path;
    }

    /// <summary>Opens the Screenshots folder in Explorer, optionally with a file selected.</summary>
    public static void OpenFolder(string folder, string? selectFile = null)
    {
        Directory.CreateDirectory(folder);
        string args = selectFile != null && File.Exists(selectFile)
            ? $"/select,\"{selectFile}\""
            : $"\"{folder}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }

    /// <summary>Opens the image in the default viewer.</summary>
    public static void OpenImage(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
