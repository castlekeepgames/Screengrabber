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
| Ctrl+2 after a capture | Share: pick one of your apps by number (`1`, `2`, …) |
| Ctrl+\` once more | Start a new snip |

Screenshots go to `Desktop\Screenshots` as `Screenshot_YYYY-MM-DD_HH-mm-ss.png`. Nothing is copied to the clipboard until you ask. A panel in the upper right shows the shot for 8 seconds (hover to keep it open). Click the thumbnail to open the image, the filename to show it in its folder, or any of the three rows to do that action.

### Sharing (Ctrl+2)

Sharing never touches your clipboard and never presses Send:

Pick a target and it comes to the front, with a banner at the top of the screen. Go to the chat, channel or thread it should go to, point at it, and press Ctrl+2 again. The file is dropped right there.

- **Slack and Discord:** also open their channel switcher (Ctrl+K) for you.
- **Email:** a new message in your default mail app with the file attached (Outlook classic, Thunderbird, Mailbird…, 64- or 32-bit). Mail apps that can't do that (new Outlook, webmail) get a blank email to point at instead.

On first run Screengrabber offers the apps it finds on your PC (Slack, Discord, Teams, WhatsApp, Telegram, Signal, Claude, ChatGPT, Codex, Grok, Copilot). Add any other app later in Settings → Edit share targets → Add app….

Drops are real drags: a small blue chip flashes near the spot while the pointer carries the file in. You can also drag the panel's thumbnail into any app yourself.

**Tray icon:** click to snip; right-click for Open Screenshots folder, Settings…, Pause hotkeys, and Quit. Launching Screengrabber again while it's running (for example from the Start menu) opens Settings.

## Install

Download `ScreengrabberSetup-<version>.exe` from [Releases](https://github.com/castlekeepgames/Screengrabber/releases/latest) and run it. It needs no admin rights and nothing else installed (it starts at sign-in through a per-user scheduled task named Screengrabber, which you can turn off in Settings) (Windows 10 1809+ or 11, 64-bit; the .NET runtime is included). A welcome window then shows the keys and lets you pick the apps to share to.

The installer isn't code-signed yet, so Windows may say "Windows protected your PC": click **More info → Run anyway**.

Screengrabber checks for new versions shortly after it starts and once a day (turn this off in Settings), and offers them in a notification and at the top of the tray menu. Installing an update takes a few seconds and keeps your settings. Tray → Check for updates checks right away.

Uninstall from Windows Settings → Apps. Your screenshots and settings are kept.

## Build the installer

Requires the .NET 10 SDK and Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

```powershell
.uild-installer.ps1
```

This publishes a self-contained 64-bit build (plus the 32-bit email helper, `Screengrabber.MapiHelper.exe`) to `distpp` and compiles `installer\Screengrabber.iss` into `dist\ScreengrabberSetup-<version>.exe`. The version comes from `src/Screengrabber/Screengrabber.csproj`.

## Release a new version

1. Bump `<Version>` in `src/Screengrabber/Screengrabber.csproj` (e.g. `1.3.0`) and commit.
2. Tag and push:

   ```bash
   git tag v1.3.0
   ```

   ```bash
   git push origin main v1.3.0
   ```

GitHub Actions ([release.yml](.github/workflows/release.yml)) builds the installer and publishes it as a release. The tag must match the project version or the build stops. Installed copies pick the update up within a day. Running the workflow by hand (Actions → Release → Run workflow) builds the installer as a downloadable artifact without publishing.

## Develop

```bash
dotnet run --project src/Screengrabber
```

Only one copy runs at a time, so quit the installed one first (tray → Quit, or `Screengrabber.exe --quit`).

- Settings: `%APPDATA%\Screengrabber\settings.json`. Delete it to see the first-run welcome again.
- Log: `%LOCALAPPDATA%\Screengrabber\screengrabber.log`. `Screengrabber.exe --list-apps` logs which share targets it would suggest on this PC.
- Test updating from a Debug build: set `SG_PRETEND_VERSION=1.0.0`; it then checks GitHub 5 seconds after starting.
- App icon: regenerate `src/Screengrabber/Screengrabber.ico` with `tools/make-icon.ps1`.
