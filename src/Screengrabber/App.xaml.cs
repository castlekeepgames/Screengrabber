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
    private const int CopyImageHotkeyId = 3;
    private const int DropHotkeyId = 4;
    private const int TestHotkeyId = 99;

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSettingsSignal;
    private EventWaitHandle? _quitSignal;
    private HotkeyManager? _hotkeys;
    private TrayIcon? _tray;
    private SnipSession? _session;
    private PostCapturePanel? _panel;
    private SharePickerWindow? _picker;
    private SettingsWindow? _settingsWindow;
    private DropGuideWindow? _dropGuide;
    private Settings _settings = new();

    /// <summary>True between the end of a snip and the panel appearing (encoding + saving).</summary>
    private bool _saving;
    /// <summary>Capture hotkey pressed while saving: copy the path as soon as the file exists.</summary>
    private bool _copyPathWhenSaved;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Diagnostics: log what app detection finds, then exit.
        if (Array.IndexOf(e.Args, "--list-apps") >= 0)
        {
            ListAppsAndExit();
            return;
        }

        // Launching again (e.g. from the Start menu) while running opens Settings in the running copy;
        // "--quit" (used by the installer and uninstaller) closes it instead.
        bool quit = Array.IndexOf(e.Args, "--quit") >= 0;
        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Screengrabber.ShowSettings");
        _quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Screengrabber.Quit");
        _singleInstance = new Mutex(true, @"Local\Screengrabber.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance || quit)
        {
            if (!isFirstInstance)
                (quit ? _quitSignal : _showSettingsSignal).Set();
            Shutdown();
            return;
        }
        ThreadPool.RegisterWaitForSingleObject(_showSettingsSignal,
            (_, _) => Dispatcher.BeginInvoke(OpenSettings), null, Timeout.Infinite, executeOnlyOnce: false);
        ThreadPool.RegisterWaitForSingleObject(_quitSignal,
            (_, _) => Dispatcher.BeginInvoke(Shutdown), null, Timeout.Infinite, executeOnlyOnce: true);

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

        if (_settings.IsFirstRun)
            ShowWelcome();
        else if (Array.IndexOf(e.Args, "--settings") >= 0)
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

    private void ShowWelcome()
    {
        var welcome = new WelcomeWindow(_settings);
        welcome.Finished += (_, choice) =>
        {
            _settings.ShareTargets = choice.Targets;
            _settings.StartWithWindows = choice.StartWithWindows;
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            ApplyStartWithWindows();
            _tray?.ShowInfo("Screengrabber is ready", $"Press {_settings.CaptureHotkey.DisplayText} to take a screenshot.");
            MemoryTrim.Schedule();
        };
        welcome.Show();
        welcome.Activate();
    }

    private async void ListAppsAndExit()
    {
        var apps = await InstalledApps.ListAsync();
        Log.Info($"--list-apps: {apps.Count} apps");
        foreach (var app in InstalledApps.Suggested(apps))
        {
            ShareTarget t = InstalledApps.ToShareTarget(app);
            Log.Info($"  suggested: {t.Label} | launch={t.Launch} | process={t.FocusProcess ?? "(unknown)"} | keys={t.KeysAfterOpening}");
        }
        Shutdown();
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
        _dropGuide?.Close();

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
        _dropGuide?.Close();
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
                png = await Task.Run(() => ScreenshotStore.EncodePng(shot));
            path = await Task.Run(() => ScreenshotStore.Save(png, folder));

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
        var panel = new PostCapturePanel(path, png, imageWidth, workArea, _settings.PanelTimeout,
            _settings.CaptureHotkey, _settings.CopyImageHotkey, _settings.ShareHotkey);
        panel.CopyPathRequested += (_, _) => CopyPath(panel);
        panel.CopyImageRequested += (_, _) => CopyImage(panel);
        panel.ShareRequested += (_, _) => OpenSharePicker();
        panel.Closed += (_, _) =>
        {
            if (_panel != panel)
                return;
            _panel = null;
            _hotkeys?.Unregister(CopyImageHotkeyId);
            _hotkeys?.Unregister(ShareHotkeyId);
            MemoryTrim.Schedule();
        };
        _panel = panel;
        panel.ShowInCorner();

        // These hotkeys are only ours while the panel is up, so e.g. Ctrl+1 keeps working everywhere else.
        // If another app owns one, the panel's rows are still clickable.
        if (_tray?.Paused != true && _hotkeys != null)
        {
            Hotkey copyImage = _settings.CopyImageHotkey;
            Hotkey share = _settings.ShareHotkey;
            _hotkeys.Register(CopyImageHotkeyId, copyImage.Win32Modifiers, copyImage.VirtualKey, () =>
            {
                if (_panel != null)
                    CopyImage(_panel);
            });
            _hotkeys.Register(ShareHotkeyId, share.Win32Modifiers, share.VirtualKey, OpenSharePicker);
        }
    }

    private void OpenSharePicker()
    {
        if (_panel == null || _picker != null)
            return;

        // Re-read share targets so hand edits to settings.json apply without a restart.
        var targets = Settings.Load(Log.Error, writeBack: false).ShareTargets;
        var picker = new SharePickerWindow(_panel.ImagePath, targets, _panel.WorkArea, BeginPointerDrop);
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

    /// <summary>
    /// Point-and-drop: the app is in front; wait while the user picks the chat, channel, thread or email,
    /// then drop the file wherever they point when they press the share hotkey again.
    /// </summary>
    /// <param name="targetWindow">The app's window, or zero if it couldn't be identified.</param>
    private void BeginPointerDrop(ShareTarget target, string imagePath, IntPtr targetWindow)
    {
        _dropGuide?.Close();
        Hotkey key = _settings.ShareHotkey;
        var screen = targetWindow != IntPtr.Zero ? Forms.Screen.FromHandle(targetWindow) : Forms.Screen.FromPoint(Forms.Cursor.Position);
        string instruction = target.Method == ShareMethod.Email
            ? "Find the new email that just opened (your mail app couldn't attach the file itself)."
            : $"In {target.Label}, open the chat, channel or thread it should go to.";
        var guide = new DropGuideWindow($"Share to {target.Label}", instruction, key, screen.WorkingArea);
        guide.Closed += (_, _) =>
        {
            if (_dropGuide != guide)
                return;
            _dropGuide = null;
            _hotkeys?.Unregister(DropHotkeyId);
            MemoryTrim.Schedule();
        };
        _dropGuide = guide;
        guide.ShowAtTop();

        // Run the drop after the hotkey message is handled: a drag's modal loop shouldn't start inside a window procedure.
        if (_hotkeys?.Register(DropHotkeyId, key.Win32Modifiers, key.VirtualKey,
                () => Dispatcher.BeginInvoke(() => DropAtPointer(target, imagePath, guide))) != true)
            guide.ShowProblem($"{key.DisplayText} is in use by another app. Drag the panel's thumbnail in instead.");
    }

    private async void DropAtPointer(ShareTarget target, string imagePath, DropGuideWindow guide)
    {
        System.Drawing.Point point = Forms.Cursor.Position;
        IntPtr window = AppWindows.RootWindowAt(point);
        bool wrongApp = target.FocusProcess != null &&
            !string.Equals(AppWindows.ProcessNameOf(window), target.FocusProcess, StringComparison.OrdinalIgnoreCase);
        if (window == IntPtr.Zero || AppWindows.IsOurs(window) || wrongApp)
        {
            guide.ShowProblem($"The pointer isn't over {target.Label}. Point at where it should go, then press {_settings.ShareHotkey.DisplayText}.");
            return;
        }

        // Ignore repeat presses while this drop is running.
        _hotkeys?.Unregister(DropHotkeyId);
        guide.Hide();
        bool dropped;
        try
        {
            dropped = await FileDropper.DropAt(imagePath, point.X, point.Y);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            dropped = false;
        }
        guide.Close();

        if (dropped)
            AppWindows.BringToFront(window);
        else
            _tray?.ShowError("Couldn't drop the screenshot", $"{target.Label} didn't accept it there. Drag the panel's thumbnail in instead.");
    }

    private void CopyPath(PostCapturePanel panel)
    {
        if (TryClipboard(() => ClipboardHelper.SetText(panel.ImagePath)))
            panel.MarkPathCopied();
    }

    private void CopyImage(PostCapturePanel panel)
    {
        if (TryClipboard(() => ClipboardHelper.SetImageFromFile(panel.ImagePath)))
            panel.MarkImageCopied();
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
        _dropGuide?.Close();
        _panel?.Close();
        _settingsWindow?.Close();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        _showSettingsSignal?.Dispose();
        _quitSignal?.Dispose();
        base.OnExit(e);
    }
}
