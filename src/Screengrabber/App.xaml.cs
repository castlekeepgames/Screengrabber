using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Screengrabber;

public partial class App : Application
{
    private const int CaptureHotkeyId = 1;
    private const int ShareHotkeyId = 2;
    private const int TestHotkeyId = 99;

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSettingsSignal;
    private HotkeyManager? _hotkeys;
    private TrayIcon? _tray;
    private SnipSession? _session;
    private PostCapturePanel? _panel;
    private SharePickerWindow? _picker;
    private SettingsWindow? _settingsWindow;
    private Settings _settings = new();

    /// <summary>True between the end of a snip and the panel appearing (encoding + saving).</summary>
    private bool _saving;
    /// <summary>Capture hotkey pressed while saving: copy the path as soon as the file exists.</summary>
    private bool _copyPathWhenSaved;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Launching again (e.g. from the Start menu) while running opens Settings in the running copy.
        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Screengrabber.ShowSettings");
        _singleInstance = new Mutex(true, @"Local\Screengrabber.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            _showSettingsSignal.Set();
            Shutdown();
            return;
        }
        ThreadPool.RegisterWaitForSingleObject(_showSettingsSignal,
            (_, _) => Dispatcher.BeginInvoke(OpenSettings), null, Timeout.Infinite, executeOnlyOnce: false);

        DispatcherUnhandledException += OnUnhandledException;
        _settings = Settings.Load(Log.Error);

        _tray = new TrayIcon();
        _tray.SetHotkey(_settings.CaptureHotkey);
        _tray.SnipRequested += (_, _) =>
        {
            if (_session == null && !_saving)
                StartSnip(TimeSpan.FromMilliseconds(200)); // let the tray menu fade out first
        };
        _tray.OpenFolderRequested += (_, _) => ScreenshotStore.OpenFolder(_settings.ResolvedSaveFolder);
        _tray.SettingsRequested += (_, _) => OpenSettings();
        _tray.PauseToggled += (_, _) => RegisterCaptureHotkey();
        _tray.QuitRequested += (_, _) => Shutdown();

        _hotkeys = new HotkeyManager();
        RegisterCaptureHotkey();
        ApplyStartWithWindows();

        if (Array.IndexOf(e.Args, "--settings") >= 0)
            OpenSettings();
        else if (Array.IndexOf(e.Args, StartupRegistration.StartupArg) < 0)
            _tray.ShowInfo("Screengrabber is running", $"Press {_settings.CaptureHotkey.DisplayText} to take a screenshot.");

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            try
            {
                SnipSession.WarmUp(_settings.CaptureHotkey);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            MemoryTrim.Schedule();
        });
    }

    /// <summary>
    /// (Re)registers the capture hotkey, unless hotkeys are paused or the settings window is open
    /// (it needs the keys to record new hotkeys).
    /// </summary>
    private void RegisterCaptureHotkey()
    {
        if (_hotkeys == null)
            return;
        _hotkeys.Unregister(CaptureHotkeyId);
        if (_tray?.Paused == true || _settingsWindow != null)
            return;

        Hotkey hotkey = _settings.CaptureHotkey;
        if (!_hotkeys.Register(CaptureHotkeyId, hotkey.Win32Modifiers, hotkey.VirtualKey, OnCaptureHotkey))
        {
            _tray?.ShowError("Hotkey unavailable",
                $"{hotkey.DisplayText} is already in use by another app. Pick another in Settings, or click the tray icon to snip.");
        }
    }

    /// <summary>True if no other app owns this combination right now.</summary>
    private bool IsHotkeyAvailable(Hotkey hotkey)
    {
        if (_hotkeys == null || !_hotkeys.Register(TestHotkeyId, hotkey.Win32Modifiers, hotkey.VirtualKey, () => { }))
            return false;
        _hotkeys.Unregister(TestHotkeyId);
        return true;
    }

    private void ApplyStartWithWindows()
    {
        try
        {
            StartupRegistration.Apply(_settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _picker?.Close();
        _panel?.Close();

        var window = new SettingsWindow(_settings, IsHotkeyAvailable);
        window.Saved += (_, saved) =>
        {
            _settings = saved;
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                _tray?.ShowError("Couldn't save settings", ex.Message);
            }
            _tray?.SetHotkey(_settings.CaptureHotkey);
            ApplyStartWithWindows();
        };
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            RegisterCaptureHotkey();
            MemoryTrim.Schedule();
        };

        // Hotkeys are off while the window is open, so pressing the current ones can record them.
        _settingsWindow = window;
        RegisterCaptureHotkey();
        window.Show();
        window.Activate();
    }

    /// <summary>
    /// The capture hotkey steps through the flow:
    /// snip → (again) whole screen → (again) copy path → (again) start a new snip.
    /// </summary>
    private void OnCaptureHotkey()
    {
        if (_session != null)
        {
            _session.CaptureWholeScreen();
        }
        else if (_saving)
        {
            _copyPathWhenSaved = true;
        }
        else if (_panel is { PathCopied: false })
        {
            CopyPath(_panel);
        }
        else
        {
            StartSnip(TimeSpan.Zero);
        }
    }

    private async void StartSnip(TimeSpan delay)
    {
        _picker?.Close();
        if (_panel != null)
        {
            _panel.Close();
            // Give the compositor a moment to remove the panel so it isn't in the frozen screenshot.
            delay = TimeSpan.FromMilliseconds(Math.Max(delay.TotalMilliseconds, 60));
        }
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay);
        if (_session != null)
            return;

        _session = SnipSession.Start(_settings.CaptureHotkey);
        _session.Finished += OnSessionFinished;
    }

    private async void OnSessionFinished(object? sender, Bitmap? shot)
    {
        _session = null;
        if (shot == null)
        {
            MemoryTrim.Schedule();
            return;
        }

        // The panel goes on the monitor where the snip happened, which is where the cursor still is.
        Rectangle workArea = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        string folder = _settings.ResolvedSaveFolder;
        _saving = true;
        _copyPathWhenSaved = false;
        try
        {
            int width = shot.Width;
            byte[] png;
            string path;
            using (shot)
            {
                png = await Task.Run(() => ScreenshotStore.EncodePng(shot));
                path = await Task.Run(() => ScreenshotStore.Save(png, folder));

                if (_settings.CopyImageOnCapture && !_copyPathWhenSaved)
                    TryClipboard(() => ClipboardHelper.SetImage(shot, png));
            }

            ShowPanel(path, png, width, workArea);
            if (_copyPathWhenSaved && _panel != null)
                CopyPath(_panel);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            _tray?.ShowError("Couldn't save screenshot", ex.Message);
        }
        finally
        {
            _saving = false;
            _copyPathWhenSaved = false;
        }
    }

    private void ShowPanel(string path, byte[] png, int imageWidth, Rectangle workArea)
    {
        _panel?.Close();
        var panel = new PostCapturePanel(path, png, imageWidth, workArea,
            _settings.PanelTimeout, _settings.CaptureHotkey, _settings.ShareHotkey);
        panel.ShareRequested += (_, _) => OpenSharePicker();
        panel.Closed += (_, _) =>
        {
            if (_panel != panel)
                return;
            _panel = null;
            _hotkeys?.Unregister(ShareHotkeyId);
            MemoryTrim.Schedule();
        };
        _panel = panel;
        panel.ShowInCorner();

        // The share hotkey is only ours while the panel is up, so e.g. Ctrl+Home keeps working everywhere else.
        // If another app owns it, the panel's share row is still clickable.
        if (_tray?.Paused != true)
        {
            Hotkey share = _settings.ShareHotkey;
            _hotkeys?.Register(ShareHotkeyId, share.Win32Modifiers, share.VirtualKey, OpenSharePicker);
        }
    }

    private void OpenSharePicker()
    {
        if (_panel == null || _picker != null)
            return;

        // Re-read share targets so hand edits to settings.json apply without a restart.
        var targets = Settings.Load(Log.Error, writeBack: false).ShareTargets;
        var picker = new SharePickerWindow(_panel.ImagePath, targets, _panel.WorkArea);
        picker.ShareFailed += (_, ex) =>
        {
            Log.Error(ex);
            _tray?.ShowError("Couldn't share screenshot", ex.Message);
        };
        picker.Closed += (_, _) =>
        {
            if (_picker == picker)
                _picker = null;
            MemoryTrim.Schedule();
        };
        _picker = picker;

        // The picker takes over from the panel.
        _panel.Close();
        picker.ShowCentered();
    }

    private void CopyPath(PostCapturePanel panel)
    {
        if (TryClipboard(() => ClipboardHelper.SetText(panel.ImagePath)))
            panel.MarkPathCopied();
    }

    private bool TryClipboard(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            // Clipboard locked by another app even after retries. The file is still saved.
            Log.Error(ex);
            _tray?.ShowError("Clipboard busy", "Couldn't copy to the clipboard. The screenshot is still saved.");
            return false;
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A tray app shouldn't die silently: log it, reset any half-finished snip, keep running.
        Log.Error(e.Exception);
        e.Handled = true;
        try { _session?.Cancel(); } catch { }
        _session = null;
        _saving = false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _session?.Cancel();
        _picker?.Close();
        _panel?.Close();
        _settingsWindow?.Close();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        _showSettingsSignal?.Dispose();
        base.OnExit(e);
    }
}
