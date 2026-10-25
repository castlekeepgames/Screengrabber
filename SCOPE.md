# Screengrabber — Scope

## 1. Purpose

Take a screenshot and get it where it needs to go with as few keystrokes as possible. One hotkey, **Ctrl+\`** (the backtick key just left of 1), drives the whole flow: snip, full screen, save, copy path. After a capture, **Ctrl+1** copies the image and **Ctrl+2** sends the shot to a destination.

**Target user:** one person (me), on Windows 11, who takes a lot of screenshots and pastes them into Slack, email, and AI coding/chat tools.

---

## 2. Core flow

The app runs in the background with a tray icon. Pressing Ctrl+\` again moves you to the next step:

| Press | When | What happens |
|---|---|---|
| **1st** Ctrl+\` | Idle | The screen dims and a crosshair appears. **Snip mode starts right away**, so you just drag a box. Upper-right hint: **"Ctrl+\` for whole screen"** |
| Drag a box | In snip mode | Captures the region → **saved to `Desktop\Screenshots`** → shows the post-capture panel (below) |
| **2nd** Ctrl+\` | In snip mode (before dragging) | Captures the **whole monitor the mouse is on** → saved → shows the post-capture panel |
| **3rd** Ctrl+\` | Post-capture panel visible | Copies the **full file path** to the clipboard. The panel shows "Path copied ✓" and **stays open**, so Ctrl+1 and Ctrl+2 still work |
| **4th** Ctrl+\` | Panel visible, path already copied | Closes the panel and starts a **new snip** (so you don't have to wait for the panel to close) |
| **Ctrl+1** | Post-capture panel visible | Copies the **image** to the clipboard ("Image copied ✓") |
| **Ctrl+2** | Post-capture panel visible | Opens the **Share picker** |
| Esc | Any state | Cancels or closes the overlay or panel (a shot that was already saved stays saved) |

### Post-capture panel (upper right, small, always on top)

```
┌──────────────────────────────────┐
│ ✓ Saved  Screenshot_2026-10-24…  │
│ [thumbnail]                      │
│ Ctrl+`    : copy screenshot path │
│ Ctrl+1    : copy image           │
│ Ctrl+2    : share                │
└──────────────────────────────────┘
```

- Doesn't steal focus from the app you're working in.
- Closes on its own after **~8 seconds** of no interaction (configurable). Hovering keeps it open.
- Clicking the thumbnail opens the image. Clicking the filename opens the folder with the file selected.
- After it closes, the next Ctrl+\` starts a **new snip** (step 1 again).

### Share picker (opens on Ctrl+2)

A small popup with five large buttons, each with its own number key for keyboard-only use:

`1 Slack` · `2 Email` · `3 Claude Code` · `4 Grok Bot` · `5 Codex` · `Esc cancel`

---

## 3. Functional requirements

### 3.1 Hotkeys
- **Ctrl+\`** is registered globally while the app runs. It is registered by the physical key (virtual key `VK_OEM_3`), so it stays the same key on keyboard layouts where that key isn't a backtick.
- **Ctrl+1** and **Ctrl+2** are registered **only while the post-capture panel is visible**, so they keep working normally in other apps (e.g. switching browser tabs).
- Both hotkeys can be changed in settings.
- If another app already owns the hotkey, show a tray notification explaining that.
- Known conflict: VS Code uses Ctrl+\` to show and hide its terminal. While Screengrabber is running, that shortcut won't reach VS Code.

### 3.2 Capture
- **Region snip:** a rectangle drag across a frozen, dimmed copy of the screen, like Snipping Tool. Shows a live W×H readout while you drag.
- **Whole screen:** captures the monitor the mouse cursor is on.
- Must work across multiple monitors with different DPI scaling (per-monitor DPI aware) with no blur and no offset.
- The overlay should appear in under 200 ms after the hotkey.
- Nothing goes on the clipboard automatically. Ctrl+\` copies the path, Ctrl+1 the image.

### 3.3 Saving
- Folder: `<Desktop>\Screenshots`. The Desktop location comes from Windows (the Known Folder API), so it still works when OneDrive redirects the Desktop. The folder is created if it's missing.
- Filename: `Screenshot_YYYY-MM-DD_HH-mm-ss.png`. A suffix `_2` etc. is added if two shots land in the same second.
- Format: PNG, lossless.

### 3.4 Share targets
Each target is a configurable **action recipe**, so it can be tuned or new targets added without code changes:

| Target | v1 behavior | Later (v2) |
|---|---|---|
| **Slack** | Image to clipboard → open or focus the Slack desktop app → you pick the channel and press Ctrl+V | Upload straight to a default channel or DM through the Slack API |
| **Email (Mailbird)** | Opens a new Mailbird message with the PNG **already attached**, using Simple MAPI (confirmed working). If MAPI fails: open a new message with a `mailto:` link and put the image on the clipboard to paste | Pick a default recipient list |
| **Claude Code** | Copies the **file path** → brings the Claude desktop app to the front | Optional auto-paste |
| **Grok Bot** | Image to clipboard → opens or focuses the Grok desktop app | — |
| **Codex** | Copies the **file path** → brings the Codex desktop app to the front | Optional auto-paste |

Recipe fields: `label`, `hotkeyNumber`, `clipboard: image | path | none`, `launch: exe path or URL`, `focusWindowMatch: window title/process name`, `autoPaste: true/false` (default false, because pasting into the wrong window is worse than one extra Ctrl+V).

### 3.5 Tray app and settings
- Tray icon menu: Take snip · Open Screenshots folder · Settings · Pause hotkeys · Quit.
- Option to **start with Windows** (on by default).
- Settings are stored as a JSON file in `%APPDATA%\Screengrabber\settings.json`: hotkeys, save folder, panel timeout, start with Windows, share recipes.

---

## 4. Out of scope (v1)

- Annotation or markup (arrows, highlights, blur)
- Freeform, window, or delayed snips
- Capturing all monitors at once
- Screen recording or GIFs
- OCR or text extraction
- Cloud upload or link sharing
- Direct Slack API upload
- Screenshot history browser (the Screenshots folder serves this purpose)
- Mac or Linux

---

## 5. Recommended tech stack

**C# / .NET 10 (LTS) + WPF**, 64-bit, shipped as a self-contained build (runtime included) in an Inno Setup installer. A tiny 32-bit .NET Framework helper handles mail programs whose MAPI DLL is 32-bit only.
- Native global hotkeys (`RegisterHotKey`), fast startup, proper per-monitor DPI support, easy access to the clipboard, MAPI, and window focusing.
- Rejected alternatives: **Electron**, which starts too slowly and uses too much memory for a snip tool. **AutoHotkey**, which is great for prototypes but makes the overlay and share UI clunky. **Python**, where packaging and DPI handling are painful.

---

## 6. Acceptance criteria

1. Ctrl+\` from any app → crosshair overlay ready within 200 ms; dragging a box saves a correct PNG to `Desktop\Screenshots`.
2. Ctrl+\` twice → a full-screen PNG of the monitor under the cursor is saved.
3. Ctrl+\` after a capture → `Ctrl+V` in Notepad pastes the exact full path, and the panel is still open.
4. Ctrl+2 after a capture → share picker opens:
   - Slack: Slack is in front, Ctrl+V pastes the image.
   - Email: a new Mailbird message opens with the PNG attached, or with the image ready to paste if MAPI isn't supported.
   - Claude Code / Codex: the desktop app is in front, Ctrl+V pastes the path.
   - Grok Bot: the Grok app is in front, Ctrl+V pastes the image.
5. Ctrl+1 and Ctrl+2 do nothing special when no panel is showing (normal app behavior is untouched).
6. Works correctly on a 2-monitor setup with mixed 100% / 150% scaling.
7. Survives a reboot (starts with Windows) and runs idle at under 50 MB RAM.

---

## 7. Milestones

1. **M1 – Capture core:** tray app, Ctrl+\`, region and full-screen capture, save to folder. ✅ Done 2026-10-24. Known limitations: a region can't span two monitors, and memory is ~290 MB after several captures (a target for M4).
2. **M2 – Post-capture panel:** thumbnail, copy path, auto-close, image on clipboard. ✅ Done 2026-10-24. The Ctrl+Home hint is shown, but the key does nothing until M3.
3. **M3 – Share:** picker UI and the 5 recipes (including the Mailbird MAPI check). ✅ Done 2026-10-24. Recipes are in `%APPDATA%\Screengrabber\settings.json`. The panel's "share" row can also be clicked.
4. **M4 – Polish:** settings UI, start with Windows, hotkey conflict handling, installer or single exe. ✅ Done 2026-10-24.
   - Settings window (tray → Settings…, or launch the app again while it's running): both hotkeys, save folder, panel timeout, copy-image-on-capture, start with Windows, and a button to edit the share targets.
   - Hotkey conflicts: Save refuses a hotkey another app owns. If the capture hotkey can't be registered at startup, a tray notification says so.
   - Tray: Take snip · Open Screenshots folder · Settings… · Pause hotkeys · Quit.
   - `install.ps1` builds the exe, installs it to `%LOCALAPPDATA%\Programs\Screengrabber`, adds a Start menu shortcut, and starts it. `-Uninstall` removes it all. (Replaced by the installer in M5.)
   - Packaged as a **framework-dependent** single exe instead of self-contained. Self-contained would be ~150 MB, and compressing it raises RAM use because everything is unpacked into memory.
   - Memory: idle RAM (working set) is **1–6 MB** after captures, because the app returns memory to Windows once it goes idle. Committed memory is ~175 MB, mostly paged out, and flat over repeated captures. Overlay opens in 100–160 ms, including the first snip after launch (warmed up at startup).

5. **M5 – Shareable:** installer, first-run setup, email for any mail app. ✅ Done 2026-10-24.
   - `build-installer.ps1` creates `dist\ScreengrabberSetup-<version>.exe` (~51 MB) with Inno Setup: self-contained 64-bit build, per-user install with no admin prompt, Start menu entry, Apps & Features uninstall (keeps settings and screenshots). Closes a running copy first (`--quit`).
   - First run: a welcome window shows the keys and offers the share apps found on the PC (read from the Start menu's app list, with a small catalog for extras like Ctrl+K). "Add app…" in the editor uses the same list.
   - Targets no longer need a known process name: the app is launched by its Start menu ID, and Screengrabber follows whichever window comes forward.
   - Email: the app is 64-bit again. MAPI is tried in-process, then through a 32-bit .NET Framework helper (built into Windows) for 32-bit-only mail DLLs. Without MAPI, a blank email opens for point-and-drop.
   - The default capture hotkey is the physical key left of 1 on any keyboard layout, labelled with the character it types there.
   - Not done yet: code signing (SmartScreen warning).
6. **M6 – Releases and updates:** ✅ Done 2026-10-24. Public repo [castlekeepgames/Screengrabber](https://github.com/castlekeepgames/Screengrabber). Pushing a `v<version>` tag builds the installer on GitHub Actions and publishes a release. The app checks the latest release about a minute after startup and daily, offers it in a notification and the tray menu, downloads it (size and SHA-256 checked), and runs the installer silently. The installer closes the app, upgrades it and restarts it.
---

## 8. Decisions log

| Question | Decision |
|---|---|
| Main hotkey | **Ctrl+\`** instead of Ctrl+Insert, so Ctrl+Insert keeps working as Copy |
| Grok Bot | Grok **desktop app**, image on the clipboard |
| Email client | **Mailbird**. Try to attach the file automatically; if that fails, paste instead |
| Slack | The paste flow is enough for v1 |
| Claude Code / Codex | **Desktop apps**, with the **file path** on the clipboard |
| Whole screen | **Monitor under the cursor** |
| After copying the path | Panel **stays open** so you can still share |
| Post-capture keys (2026-10-24) | **Ctrl+\`** copy path, **Ctrl+1** copy image, **Ctrl+2** share (was Ctrl+Home). The image is no longer copied automatically, and that setting is gone. Existing settings files are migrated |
| Editing share targets | In-app editor (Settings → Edit share targets…) instead of opening settings.json in Notepad |
| Sharing without the clipboard (2026-10-24) | Sharing must not overwrite what the user has copied. Each target **delivers** by: dropping the file on the app, dropping it where the user points, typing its path, or just opening the app. Drops are real drags driven by synthesized mouse input (a drag started purely in code was unreliable). No auto-send |
| Slack channel + thread, no API | Slack on Windows isn't a Windows Share target, and `slack://` links can't attach files. So Screengrabber opens Slack's own Ctrl+K switcher and the user picks the channel (and thread). Then they point and press Ctrl+2, and the file is dropped there. Works the same for Slack in a browser (Window = `chrome`) |
| Claude / Codex / Grok session | Their desktop apps expose no way to pick a conversation from outside. They use the same point-and-drop flow as Slack: the user opens the conversation in the app, points, and presses Ctrl+2 (2026-10-24, replacing typed path / drop-on-window) |
| Distribution (2026-10-24) | Inno Setup installer with a self-contained runtime (no prerequisites), per-user. Unsigned for now |
| Email on any PC (2026-10-24) | 64-bit app, with a 32-bit MAPI helper for 32-bit-only mail DLLs, and point-and-drop when there's no MAPI. Replaces "the whole app is 32-bit for Mailbird" |
| Share key (2026-10-24) | **Ctrl+2** instead of Ctrl+3, for opening the picker and for the drop step. Existing settings are migrated |

## 9. Confirmed during the build

- Mailbird accepts Simple MAPI attachments, but only from a 32-bit process.
- App identities on this machine: Slack `slack` (often hidden in the tray, so it's launched to show it), Mailbird `Mailbird`, Claude `claude`, Grok Bot `Grok Bot`, and Codex, whose desktop app runs as `ChatGPT.exe` from the `OpenAI.Codex` package.
- Mailbird can lose focus from its compose window shortly after opening it. For 6 seconds afterward, Screengrabber pulls the compose window back to the front, but only if you haven't touched the keyboard or mouse.
