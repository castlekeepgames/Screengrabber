using System;
using System.Collections.Generic;
using System.Windows.Interop;

namespace Screengrabber;

/// <summary>
/// Registers system-wide hotkeys against a hidden message-only window and dispatches
/// WM_HOTKEY to handlers on the UI thread.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("ScreengrabberHotkeys")
        {
            ParentWindow = Native.HWND_MESSAGE,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <returns>False if another application already owns this key combination.</returns>
    public bool Register(int id, uint modifiers, uint virtualKey, Action handler)
    {
        if (!Native.RegisterHotKey(_source.Handle, id, modifiers | Native.MOD_NOREPEAT, virtualKey))
            return false;

        _handlers[id] = handler;
        return true;
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id))
            Native.UnregisterHotKey(_source.Handle, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handled = true;
            handler();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (int id in _handlers.Keys)
            Native.UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
        _source.Dispose();
    }
}
