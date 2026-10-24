using System;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Screengrabber;

internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _image;
    private readonly Forms.ToolStripMenuItem _snipItem;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private string _hotkeyText = "";

    public event EventHandler? SnipRequested;
    public event EventHandler? OpenFolderRequested;
    public event EventHandler? SettingsRequested;
    /// <summary>Raised with the new paused state.</summary>
    public event EventHandler<bool>? PauseToggled;
    public event EventHandler? QuitRequested;

    public TrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        _snipItem = new Forms.ToolStripMenuItem("Take snip", null, (_, _) => SnipRequested?.Invoke(this, EventArgs.Empty));
        _pauseItem = new Forms.ToolStripMenuItem("Pause hotkeys", null, (_, _) =>
        {
            _pauseItem!.Checked = !_pauseItem.Checked;
            UpdateTooltip();
            PauseToggled?.Invoke(this, _pauseItem.Checked);
        });
        menu.Items.Add(_snipItem);
        menu.Items.Add("Open Screenshots folder", null, (_, _) => OpenFolderRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));

        _image = LoadIcon();
        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = "Screengrabber",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                SnipRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public bool Paused => _pauseItem.Checked;

    /// <summary>Shows the capture hotkey in the menu and tooltip.</summary>
    public void SetHotkey(Hotkey capture)
    {
        _hotkeyText = capture.DisplayText;
        _snipItem.ShortcutKeyDisplayString = _hotkeyText;
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        // NotifyIcon.Text is limited to 63 characters.
        string text = Paused ? "Screengrabber (hotkeys paused)" : $"Screengrabber ({_hotkeyText} to snip)";
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    public void ShowInfo(string title, string text) =>
        _icon.ShowBalloonTip(3000, title, text, Forms.ToolTipIcon.Info);

    public void ShowError(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Error);

    /// <summary>The app icon (tools/make-icon.ps1) at the tray's size for the current DPI.</summary>
    private static Drawing.Icon LoadIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("Screengrabber.Screengrabber.ico")
            ?? throw new InvalidOperationException("Embedded icon missing.");
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
