using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Screengrabber;

/// <summary>
/// Opens a new message in the default mail client with a file attached, via Simple MAPI.
/// Works with any client that registers a MAPI DLL (Mailbird, Outlook classic, Thunderbird)
/// whose bitness matches this process.
/// </summary>
internal static class MapiMail
{
    private const int MAPI_LOGON_UI = 0x00000001;
    private const int MAPI_DIALOG = 0x00000008;
    public const int SUCCESS_SUCCESS = 0;
    public const int MAPI_USER_ABORT = 1;

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

    /// <returns>The MAPI result code: <see cref="SUCCESS_SUCCESS"/>, <see cref="MAPI_USER_ABORT"/>, or an error.</returns>
    public static Task<int> ComposeWithAttachment(string filePath, string subject)
    {
        // MAPI wants an STA thread, and some clients block until the compose window closes.
        var result = new TaskCompletionSource<int>();
        var thread = new Thread(() =>
        {
            try
            {
                result.SetResult(Send(filePath, subject));
            }
            catch (Exception ex)
            {
                result.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "MAPI compose",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    private static int Send(string filePath, string subject)
    {
        var file = new MapiFileDescW { PathName = filePath, FileName = Path.GetFileName(filePath) };
        IntPtr files = Marshal.AllocHGlobal(Marshal.SizeOf<MapiFileDescW>());
        try
        {
            Marshal.StructureToPtr(file, files, false);
            var message = new MapiMessageW { Subject = subject, FileCount = 1, Files = files };
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            return MAPISendMailW(IntPtr.Zero, IntPtr.Zero, message, MAPI_LOGON_UI | MAPI_DIALOG, 0);
        }
        finally
        {
            Marshal.DestroyStructure<MapiFileDescW>(files);
            Marshal.FreeHGlobal(files);
        }
    }
}
