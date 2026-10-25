using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Screengrabber;

/// <summary>A key plus modifiers. Stored as e.g. "Ctrl+Oem3"; shown as "Ctrl+`".</summary>
[JsonConverter(typeof(HotkeyJsonConverter))]
internal readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    /// <summary>Ctrl + the key left of 1, whatever character it types on this keyboard layout.</summary>
    public static readonly Hotkey DefaultCapture = new(ModifierKeys.Control, KeyLeftOfOne());
    public static readonly Hotkey DefaultCopyImage = new(ModifierKeys.Control, Key.D1);
    public static readonly Hotkey DefaultShare = new(ModifierKeys.Control, Key.D2);

    /// <summary>Keys that are fine as a hotkey with no modifier, because nothing types them.</summary>
    private static readonly HashSet<Key> StandaloneKeys = new()
    {
        Key.PrintScreen, Key.Pause, Key.Scroll,
        Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12,
        Key.F13, Key.F14, Key.F15, Key.F16, Key.F17, Key.F18, Key.F19, Key.F20, Key.F21, Key.F22, Key.F23, Key.F24,
    };

    private static readonly Dictionary<Key, string> KeyLabels = new()
    {
        [Key.Oem3] = "`", [Key.OemMinus] = "-", [Key.OemPlus] = "=", [Key.OemOpenBrackets] = "[",
        [Key.Oem6] = "]", [Key.Oem5] = "\\", [Key.Oem1] = ";", [Key.Oem7] = "'", [Key.OemComma] = ",",
        [Key.OemPeriod] = ".", [Key.Oem2] = "/", [Key.PageUp] = "PgUp", [Key.PageDown] = "PgDn",
        [Key.PrintScreen] = "PrtScn", [Key.Scroll] = "ScrollLock", [Key.Back] = "Backspace", [Key.Return] = "Enter",
    };

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public uint Win32Modifiers =>
        (Modifiers.HasFlag(ModifierKeys.Alt) ? MOD_ALT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Control) ? Native.MOD_CONTROL : 0) |
        (Modifiers.HasFlag(ModifierKeys.Shift) ? MOD_SHIFT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Windows) ? MOD_WIN : 0);

    /// <summary>Has a modifier, or is a key that nothing types (F-keys, PrintScreen…).</summary>
    public bool IsUsable => Key != Key.None && (Modifiers != ModifierKeys.None || StandaloneKeys.Contains(Key));

    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>For people: "Ctrl+Shift+`". Punctuation keys show what they type on the current keyboard layout.</summary>
    public string DisplayText => Join(LayoutCharacter(Key) ?? (KeyLabels.TryGetValue(Key, out var label) ? label : KeyName(Key)));

    private const uint MAPVK_VK_TO_CHAR = 2;
    private const uint MAPVK_VSC_TO_VK_EX = 3;
    /// <summary>Scan code of the physical key left of 1 on standard keyboards (` on US, ^ on German, ² on French).</summary>
    private const uint ScanCodeLeftOfOne = 0x29;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    private static Key KeyLeftOfOne()
    {
        uint vk = MapVirtualKey(ScanCodeLeftOfOne, MAPVK_VSC_TO_VK_EX);
        return vk == 0 ? Key.Oem3 : KeyInterop.KeyFromVirtualKey((int)vk);
    }

    /// <summary>The character an OEM punctuation key types on the current layout, or null.</summary>
    private static string? LayoutCharacter(Key key)
    {
        if (!key.ToString().StartsWith("Oem", StringComparison.Ordinal))
            return null;
        // The high bit marks a dead key (e.g. ^ on German layouts); the character is still in the low word.
        char c = (char)(MapVirtualKey((uint)KeyInterop.VirtualKeyFromKey(key), MAPVK_VK_TO_CHAR) & 0xFFFF);
        return c > ' ' ? c.ToString() : null;
    }

    /// <summary>For settings.json: "Ctrl+Shift+Oem3", with digits as "Ctrl+1".</summary>
    public string StorageText => Join(KeyName(Key));

    public override string ToString() => DisplayText;

    private string Join(string key)
    {
        string modifiers = ModifiersText(Modifiers);
        return modifiers.Length == 0 ? key : $"{modifiers}+{key}";
    }

    /// <summary>"Ctrl+Shift", or empty for none.</summary>
    public static string ModifiersText(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return string.Join("+", parts);
    }

    private static string KeyName(Key key) => key is >= Key.D0 and <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key.ToString();

    public static Hotkey Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            throw new FormatException($"Empty hotkey: '{text}'");

        var modifiers = ModifierKeys.None;
        foreach (var part in parts.Take(parts.Length - 1))
        {
            modifiers |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "win" or "windows" => ModifierKeys.Windows,
                _ => throw new FormatException($"Unknown modifier '{part}' in hotkey '{text}'"),
            };
        }

        string keyText = parts[^1];
        // Digits first: Enum.TryParse would read "1" as the numeric value of Key.Cancel.
        Key key = keyText.Length == 1 && char.IsAsciiDigit(keyText[0])
            ? Key.D0 + (keyText[0] - '0')
            : KeyLabels.FirstOrDefault(kv => kv.Value == keyText).Key;
        if (key == Key.None && (!Enum.TryParse(keyText, ignoreCase: true, out key) || int.TryParse(keyText, out _)))
            throw new FormatException($"Unknown key '{keyText}' in hotkey '{text}'");
        return new Hotkey(modifiers, key);
    }
}

internal sealed class HotkeyJsonConverter : JsonConverter<Hotkey>
{
    public override Hotkey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Hotkey.Parse(reader.GetString() ?? "");

    public override void Write(Utf8JsonWriter writer, Hotkey value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.StorageText);
}
