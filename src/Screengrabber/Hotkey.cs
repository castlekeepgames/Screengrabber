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

    public static readonly Hotkey DefaultCapture = new(ModifierKeys.Control, Key.Oem3);
    public static readonly Hotkey DefaultShare = new(ModifierKeys.Control, Key.Home);

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

    /// <summary>For people: "Ctrl+Shift+`".</summary>
    public string DisplayText => Join(KeyLabels.TryGetValue(Key, out var label) ? label : KeyName(Key));

    /// <summary>For settings.json: "Ctrl+Shift+Oem3".</summary>
    public string StorageText => Join(Key.ToString());

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
        Key key = KeyLabels.FirstOrDefault(kv => kv.Value == keyText).Key;
        if (key == Key.None && !Enum.TryParse(keyText, ignoreCase: true, out key))
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
