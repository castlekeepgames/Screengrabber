using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Screengrabber;

/// <summary>
/// Synthesized keyboard and mouse input. Text is typed as Unicode, so no clipboard is involved.
/// Mouse coordinates are physical screen pixels.
/// </summary>
internal static class SyntheticInput
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // The union is sized by its largest member (MOUSEINPUT), which SendInput's cbSize check depends on.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>Moves the pointer as real mouse input (so apps see genuine mouse moves).</summary>
    public static void MoveMouse(int x, int y)
    {
        // Absolute coordinates are normalised to 0..65535 across the whole virtual desktop.
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN), top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN), height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        Send(new[] { Mouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            (int)Math.Round((x - left) * 65535.0 / (width - 1)), (int)Math.Round((y - top) * 65535.0 / (height - 1))) });
    }

    public static void LeftButtonDown() => Send(new[] { Mouse(MOUSEEVENTF_LEFTDOWN, 0, 0) });

    public static void LeftButtonUp() => Send(new[] { Mouse(MOUSEEVENTF_LEFTUP, 0, 0) });

    private static INPUT Mouse(uint flags, int dx, int dy) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } },
    };

    /// <summary>Types the text into whatever has keyboard focus.</summary>
    public static void TypeText(string text)
    {
        var inputs = new INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            inputs[2 * i] = Unicode(text[i], keyUp: false);
            inputs[2 * i + 1] = Unicode(text[i], keyUp: true);
        }
        Send(inputs);
    }

    /// <summary>Presses and releases a key combination, e.g. Ctrl+K.</summary>
    public static void Press(Hotkey hotkey)
    {
        var modifiers = new[]
        {
            (ModifierKeys.Control, Key.LeftCtrl), (ModifierKeys.Alt, Key.LeftAlt),
            (ModifierKeys.Shift, Key.LeftShift), (ModifierKeys.Windows, Key.LWin),
        };
        var down = new System.Collections.Generic.List<INPUT>();
        var up = new System.Collections.Generic.List<INPUT>();
        foreach (var (flag, key) in modifiers)
        {
            if (!hotkey.Modifiers.HasFlag(flag))
                continue;
            down.Add(VirtualKey(key, keyUp: false));
            up.Insert(0, VirtualKey(key, keyUp: true));
        }
        down.Add(VirtualKey(hotkey.Key, keyUp: false));
        up.Insert(0, VirtualKey(hotkey.Key, keyUp: true));
        down.AddRange(up);
        Send(down.ToArray());
    }

    private static INPUT Unicode(char c, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0) } },
    };

    private static INPUT VirtualKey(Key key, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)KeyInterop.VirtualKeyFromKey(key), dwFlags = keyUp ? KEYEVENTF_KEYUP : 0 } },
    };

    private static void Send(INPUT[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            throw new InvalidOperationException($"Couldn't send keyboard input (error {Marshal.GetLastWin32Error()}).");
    }
}
