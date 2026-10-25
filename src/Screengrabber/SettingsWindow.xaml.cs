using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Screengrabber;

public partial class SettingsWindow : Window
{
    private readonly Settings _original;
    private readonly Func<Hotkey, bool> _isHotkeyAvailable;
    private Hotkey _capture;
    private Hotkey _copyImage;
    private Hotkey _share;
    private List<ShareTarget> _targets;

    /// <summary>Raised with the new settings when the user saves. The caller persists and applies them.</summary>
    internal event EventHandler<Settings>? Saved;

    /// <param name="isHotkeyAvailable">Checks that no other app owns a combination.</param>
    internal SettingsWindow(Settings current, Func<Hotkey, bool> isHotkeyAvailable)
    {
        _original = current;
        _isHotkeyAvailable = isHotkeyAvailable;
        InitializeComponent();

        _capture = current.CaptureHotkey;
        _copyImage = current.CopyImageHotkey;
        _share = current.ShareHotkey;
        CaptureHotkeyBox.Text = _capture.DisplayText;
        CopyImageHotkeyBox.Text = _copyImage.DisplayText;
        ShareHotkeyBox.Text = _share.DisplayText;
        HookUpRecorder(CaptureHotkeyBox, hotkey => _capture = hotkey, () => _capture);
        HookUpRecorder(CopyImageHotkeyBox, hotkey => _copyImage = hotkey, () => _copyImage);
        HookUpRecorder(ShareHotkeyBox, hotkey => _share = hotkey, () => _share);

        FolderBox.Text = current.ResolvedSaveFolder;
        TimeoutBox.Text = current.PanelTimeoutSeconds.ToString();
        StartWithWindowsCheck.IsChecked = current.StartWithWindows;
        CheckForUpdatesCheck.IsChecked = current.CheckForUpdates;

        // Fresh from disk, in case settings.json was edited by hand since the app started.
        _targets = Settings.Load(Log.Error, writeBack: false).ShareTargets;
        UpdateTargetsSummary();

        BrowseButton.Click += (_, _) => BrowseForFolder();
        DefaultFolderButton.Click += (_, _) => FolderBox.Text = Settings.DefaultSaveFolder;
        EditTargetsButton.Click += (_, _) => EditTargets();
        SaveButton.Click += (_, _) => Save();
        // IsCancel only closes modal dialogs; this window is modeless.
        CancelButton.Click += (_, _) => Close();
    }

    /// <summary>The box shows the hotkey; while focused, the next key combination pressed replaces it.</summary>
    private void HookUpRecorder(TextBox box, Action<Hotkey> set, Func<Hotkey> get)
    {
        box.GotKeyboardFocus += (_, _) => box.Text = "Press a key combination…";
        box.LostKeyboardFocus += (_, _) => box.Text = get().DisplayText;
        box.PreviewKeyDown += (_, e) => Record(box, e, set);
        // PrintScreen never arrives as KeyDown.
        box.PreviewKeyUp += (_, e) =>
        {
            if (e.Key == Key.Snapshot)
                Record(box, e, set);
        };
    }

    private void Record(TextBox box, KeyEventArgs e, Action<Hotkey> set)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Let Tab move between fields and Esc cancel the window.
        if (key is Key.Tab or Key.Escape || key is Key.ImeProcessed or Key.DeadCharProcessed)
            return;
        e.Handled = true;

        if (Hotkey.IsModifierKey(key))
        {
            box.Text = Hotkey.ModifiersText(Keyboard.Modifiers) + "+…";
            return;
        }

        var hotkey = new Hotkey(Keyboard.Modifiers, key);
        if (!hotkey.IsUsable)
        {
            ShowError($"{hotkey.DisplayText} would get in the way of typing. Add Ctrl, Alt, Shift or Win (F-keys and PrtScn work alone).");
            return;
        }

        set(hotkey);
        box.Text = hotkey.DisplayText;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void BrowseForFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Save screenshots to",
            InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : Settings.DefaultSaveFolder,
        };
        if (dialog.ShowDialog(this) == true)
            FolderBox.Text = dialog.FolderName;
    }

    private void EditTargets()
    {
        var editor = new ShareTargetsWindow(_targets) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            _targets = editor.Result;
            UpdateTargetsSummary();
        }
    }

    private void UpdateTargetsSummary() =>
        TargetsSummary.Text = _targets.Count == 0
            ? "No share targets."
            : string.Join(" · ", _targets.Select((t, i) => $"{i + 1} {t.Label}"));

    private void Save()
    {
        var hotkeys = new (string Name, Hotkey Key)[] { ("capture", _capture), ("copy image", _copyImage), ("share", _share) };
        if (hotkeys.Select(h => h.Key).Distinct().Count() < hotkeys.Length)
        {
            ShowError("Capture, Copy image and Share each need a different hotkey.");
            return;
        }
        // Our own hotkeys are switched off while this window is open, so only other apps can make these fail.
        foreach (var (name, key) in hotkeys)
        {
            if (!_isHotkeyAvailable(key))
            {
                ShowError($"{key.DisplayText} is already used by another app. Pick a different {name} hotkey.");
                return;
            }
        }

        if (!int.TryParse(TimeoutBox.Text.Trim(), out int timeout) || timeout < 2 || timeout > 120)
        {
            ShowError("The panel timeout must be a whole number from 2 to 120 seconds.");
            return;
        }

        string folder = FolderBox.Text.Trim();
        try
        {
            Directory.CreateDirectory(Environment.ExpandEnvironmentVariables(folder));
        }
        catch (Exception ex)
        {
            ShowError($"Can't use that folder: {ex.Message}");
            return;
        }

        Settings updated = _original.Clone();
        updated.CaptureHotkey = _capture;
        updated.CopyImageHotkey = _copyImage;
        updated.ShareHotkey = _share;
        updated.SaveFolder = string.Equals(Path.GetFullPath(folder), Path.GetFullPath(Settings.DefaultSaveFolder), StringComparison.OrdinalIgnoreCase)
            ? null
            : folder;
        updated.PanelTimeoutSeconds = timeout;
        updated.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        updated.CheckForUpdates = CheckForUpdatesCheck.IsChecked == true;
        updated.ShareTargets = _targets;

        Saved?.Invoke(this, updated);
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
