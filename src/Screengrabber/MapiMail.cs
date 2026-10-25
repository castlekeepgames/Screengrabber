using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Screengrabber;

/// <summary>
/// Opens a new message in the default mail client with a file attached, via Simple MAPI.
/// Works with any client that registers a MAPI DLL (Outlook classic, Thunderbird, Mailbird…), 64- or 32-bit.
/// Clients without one (new Outlook, webmail) report failure, and the caller falls back.
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

    private const string HelperName = "Screengrabber.MapiHelper.exe";

    /// <summary>
    /// Some mail programs keep MAPISendMail waiting until their compose window closes. Past this, we assume the
    /// window is open (success) rather than waiting on the user.
    /// </summary>
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Tries this (64-bit) process first; if the mail program's MAPI DLL is 32-bit only, it fails to load here,
    /// so retry through the 32-bit helper.
    /// </summary>
    /// <returns>The MAPI result code: <see cref="SUCCESS_SUCCESS"/>, <see cref="MAPI_USER_ABORT"/>, or an error.</returns>
    public static async Task<int> ComposeWithAttachment(string filePath, string subject)
    {
        int result;
        try
        {
            result = await OrAssumeOpen(ComposeInProcess(filePath, subject));
        }
        catch (DllNotFoundException)
        {
            result = MAPI_E_FAILURE;
        }
        Log.Info($"Email: in-process MAPI result {result}");
        if (result is SUCCESS_SUCCESS or MAPI_USER_ABORT)
            return result;

        string helper = Path.Combine(AppContext.BaseDirectory, HelperName);
        if (!File.Exists(helper))
            return result;
        result = await OrAssumeOpen(RunHelper(helper, filePath, subject));
        Log.Info($"Email: 32-bit helper result {result}");
        return result;
    }

    private const int MAPI_E_FAILURE = 2;

    private static async Task<int> OrAssumeOpen(Task<int> compose) =>
        await Task.WhenAny(compose, Task.Delay(OpenTimeout)) == compose ? await compose : SUCCESS_SUCCESS;

    private static async Task<int> RunHelper(string helper, string filePath, string subject)
    {
        Native.AllowSetForegroundWindow(Native.ASFW_ANY);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false };
        start.ArgumentList.Add(filePath);
        start.ArgumentList.Add(subject);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Couldn't start the MAPI helper.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static Task<int> ComposeInProcess(string filePath, string subject)
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
