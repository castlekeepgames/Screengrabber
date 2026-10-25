using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Screengrabber.MapiHelper;

/// <summary>
/// Usage: Screengrabber.MapiHelper.exe "file path" "subject"
/// Opens a new message in the default mail program with the file attached. The exit code is the MAPI result
/// (0 = compose window opened, 1 = user cancelled, anything else = failed).
/// </summary>
internal static class Program
{
    private const int MAPI_LOGON_UI = 0x00000001;
    private const int MAPI_DIALOG = 0x00000008;
    private const int MAPI_E_FAILURE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class MapiFileDescW
    {
        public int Reserved;
        public int Flags;
        public int Position = -1;
        public string? PathName;
        public string? FileName;
        public IntPtr FileType;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class MapiMessageW
    {
        public int Reserved;
        public string? Subject;
        public string? NoteText;
        public string? MessageType;
        public string? DateReceived;
        public string? ConversationId;
        public int Flags;
        public IntPtr Originator;
        public int RecipCount;
        public IntPtr Recips;
        public int FileCount;
        public IntPtr Files;
    }

    [DllImport("mapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int MAPISendMailW(IntPtr session, IntPtr uiParam, MapiMessageW message, int flags, int reserved);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[0]))
            return MAPI_E_FAILURE;

        var file = new MapiFileDescW { PathName = args[0], FileName = Path.GetFileName(args[0]) };
        IntPtr files = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MapiFileDescW)));
        try
        {
            Marshal.StructureToPtr(file, files, false);
            var message = new MapiMessageW { Subject = args[1], FileCount = 1, Files = files };
            return MAPISendMailW(IntPtr.Zero, IntPtr.Zero, message, MAPI_LOGON_UI | MAPI_DIALOG, 0);
        }
        catch (DllNotFoundException)
        {
            return MAPI_E_FAILURE;
        }
        finally
        {
            Marshal.DestroyStructure(files, typeof(MapiFileDescW));
            Marshal.FreeHGlobal(files);
        }
    }
}
