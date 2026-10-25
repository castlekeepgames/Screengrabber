# Screengrabber

A Windows tray app for fast screenshots. See [SCOPE.md](SCOPE.md) for the full plan.

## Use

| Keys | Does |
|---|---|
| Ctrl+\` | Start a snip: drag a box |
| Ctrl+\` again | Capture the whole monitor under the mouse |
| Esc / right-click | Cancel the snip |
| Ctrl+\` after a capture | Copy the file path |
| Ctrl+1 after a capture | Copy the image to the clipboard |
| Ctrl+2 after a capture | Share: pick `1` Slack · `2` Email · `3` Claude Code · `4` Grok Bot · `5` Codex |
| Ctrl+\` once more | Start a new snip |

Screenshots go to `Desktop\Screenshots` as `Screenshot_YYYY-MM-DD_HH-mm-ss.png`. Nothing is copied to the clipboard until you ask. A panel in the upper right shows the shot for 8 seconds (hover to keep it open). Click the thumbnail to open the image, the filename to show it in its folder, or any of the three rows to do that action.

### Sharing (Ctrl+2)

Sharing never touches your clipboard and never presses Send:

Pick a target and it comes to the front, with a banner at the top of the screen. Go to the chat, channel or thread it should go to, point at it, and press Ctrl+2 again. The file is dropped right there.

- **Slack:** also opens Slack's channel switcher (Ctrl+K) for you.
- **Claude Code, Codex, Grok Bot:** open the conversation you want, then point and press Ctrl+2.
- **Email:** a new Mailbird message with the file attached.

Drops are real drags: a small blue chip flashes near the spot while the pointer carries the file in. You can also drag the panel's thumbnail into any app yourself.

**Tray icon:** click to snip; right-click for Open Screenshots folder, Settings…, Pause hotkeys, and Quit. Launching Screengrabber again while it's running (for example from the Start menu) opens Settings.

## Install

Requires the **32-bit (x86)** .NET 10 Desktop Runtime. The app is 32-bit so Email can use Mailbird's 32-bit MAPI DLL.

```powershell
.\install.ps1
```

This builds `dist\Screengrabber.exe`, installs it to `%LOCALAPPDATA%\Programs\Screengrabber`, adds a Start menu shortcut, and starts it. It starts with Windows unless you turn that off in Settings.

```powershell
.\install.ps1 -Uninstall
```

This removes the app, shortcut, and startup entry, and keeps your screenshots and settings.

## Develop

```bash
dotnet run --project src/Screengrabber
```

Only one copy runs at a time, so quit the installed one first (tray → Quit).

- Settings: `%APPDATA%\Screengrabber\settings.json`. Edit share targets in Settings → Edit share targets…; hand edits to `shareTargets` also apply the next time you share.
- Log: `%LOCALAPPDATA%\Screengrabber\screengrabber.log`.
- App icon: regenerate `src/Screengrabber/Screengrabber.ico` with `tools/make-icon.ps1`.
