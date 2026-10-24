# Screengrabber

A Windows tray app for fast screenshots. See [SCOPE.md](SCOPE.md) for the full plan.

## Use

| Keys | Does |
|---|---|
| Ctrl+\` | Start a snip: drag a box |
| Ctrl+\` again | Capture the whole monitor under the mouse |
| Esc / right-click | Cancel the snip |
| Ctrl+\` after a capture | Copy the file path |
| Ctrl+\` once more | Start a new snip |
| Ctrl+Home after a capture | Share: pick `1` Slack · `2` Email · `3` Claude Code · `4` Grok Bot · `5` Codex |

Screenshots go to `Desktop\Screenshots` as `Screenshot_YYYY-MM-DD_HH-mm-ss.png`, and the image is also copied to the clipboard. A panel in the upper right shows the shot for 8 seconds (hover to keep it open). Click the thumbnail to open the image, the filename to show it in its folder, or "share" to pick a destination.

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

- Settings and share targets: `%APPDATA%\Screengrabber\settings.json`. Hand edits to `shareTargets` apply the next time you share; other settings apply on restart or through the Settings window.
- Log: `%LOCALAPPDATA%\Screengrabber\screengrabber.log`.
- App icon: regenerate `src/Screengrabber/Screengrabber.ico` with `tools/make-icon.ps1`.
