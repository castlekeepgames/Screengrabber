using System;
using System.IO;

namespace Screengrabber;

/// <summary>Appends to %LOCALAPPDATA%\Screengrabber\screengrabber.log. Never throws.</summary>
internal static class Log
{
    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Screengrabber", "screengrabber.log");

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(Exception ex) => Write("ERROR", ex.ToString());

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"[{DateTime.Now:O}] {level} {message}{Environment.NewLine}");
        }
        catch
        {
            // Nothing sensible left to do.
        }
    }
}
