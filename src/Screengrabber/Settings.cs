using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Screengrabber;

internal enum ShareMethod
{
    /// <summary>Bring an app to the front and hand it the screenshot (see <see cref="ShareDelivery"/>).</summary>
    App,
    /// <summary>Open a new email with the file attached (Simple MAPI), falling back to a blank email.</summary>
    Email,
}

/// <summary>How an App target receives the screenshot. None of these touch the clipboard.</summary>
internal enum ShareDelivery
{
    /// <summary>Drop the file onto the app's window, as if it had been dragged there.</summary>
    DropOnWindow,
    /// <summary>Wait while the user picks a spot in the app (e.g. a Slack channel or thread), then drop it where they point.</summary>
    DropAtPointer,
    /// <summary>Type the file's path into the app's focused text box.</summary>
    TypePath,
    /// <summary>Only bring the app to the front.</summary>
    OpenOnly,
}

/// <summary>Pre-version-3 delivery setting, read only to migrate old settings files.</summary>
internal enum ClipboardContent
{
    None,
    Image,
    Path,
}

/// <summary>One button in the share picker. The number key is its position in the list (1-based).</summary>
internal sealed class ShareTarget
{
    public string Label { get; set; } = "";
    public ShareMethod Method { get; set; } = ShareMethod.App;
    public ShareDelivery Delivery { get; set; } = ShareDelivery.DropOnWindow;

    /// <summary>Exe path, URI, or <c>shell:AppsFolder\&lt;AppID&gt;</c>. Environment variables are expanded.</summary>
    public string? Launch { get; set; }

    /// <summary>Process name (no .exe) whose window is brought to the front if it's already running.</summary>
    public string? FocusProcess { get; set; }

    /// <summary>A key combination to press once the app is in front, e.g. "Ctrl+K" to open Slack's channel switcher.</summary>
    public string? KeysAfterOpening { get; set; }

    [JsonPropertyName("clipboard")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClipboardContent? LegacyClipboard { get; set; }
}

internal sealed class Settings
{
    /// <summary>Steps through snip → whole screen → open the folder → new snip.</summary>
    public Hotkey CaptureHotkey { get; set; } = Hotkey.DefaultCapture;

    /// <summary>Copies the file path. Only registered while the post-capture panel is showing.</summary>
    public Hotkey CopyPathHotkey { get; set; } = Hotkey.DefaultCopyPath;

    /// <summary>Copies the image to the clipboard. Only registered while the post-capture panel is showing.</summary>
    public Hotkey CopyImageHotkey { get; set; } = Hotkey.DefaultCopyImage;

    /// <summary>Opens the share picker. Only registered while the post-capture panel is showing.</summary>
    public Hotkey ShareHotkey { get; set; } = Hotkey.DefaultShare;

    /// <summary>Where screenshots are saved. Null means &lt;Desktop&gt;\Screenshots.</summary>
    public string? SaveFolder { get; set; }

    public int PanelTimeoutSeconds { get; set; } = 8;

    public bool StartWithWindows { get; set; } = true;

    /// <summary>Look for a newer release on GitHub shortly after startup and once a day.</summary>
    public bool CheckForUpdates { get; set; } = true;

    public List<ShareTarget> ShareTargets { get; set; } = DefaultShareTargets();

    /// <summary>True when there was no settings file yet: this is the first run, so show the welcome window.</summary>
    [JsonIgnore]
    public bool IsFirstRun { get; private set; }

    /// <summary>
    /// &lt;Desktop&gt;\Screenshots. DesktopDirectory resolves through the Known Folder API,
    /// so OneDrive-redirected desktops land in the right place.
    /// </summary>
    public static string DefaultSaveFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Screenshots");

    [JsonIgnore]
    public string ResolvedSaveFolder =>
        string.IsNullOrWhiteSpace(SaveFolder) ? DefaultSaveFolder : Environment.ExpandEnvironmentVariables(SaveFolder);

    [JsonIgnore]
    public TimeSpan PanelTimeout => TimeSpan.FromSeconds(Math.Clamp(PanelTimeoutSeconds, 2, 120));

    public Settings Clone() =>
        JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Screengrabber", "settings.json");

    /// <summary>
    /// File format version. Missing in files written before versioning (reads as 0).
    /// 2: share hotkey moved from Ctrl+Home to Ctrl+3; image no longer copied automatically.
    /// 3: share targets deliver by drop/typing instead of the clipboard.
    /// 4: share hotkey Ctrl+3 → Ctrl+2; Claude Code, Codex and Grok Bot use point-and-drop like Slack.
    /// 5: after a capture the capture key opens the folder; copy path Ctrl+1, copy image Ctrl+2, share Ctrl+3.
    /// </summary>
    public int Version { get; set; }

    private const int CurrentVersion = 5;

    public static List<ShareTarget> CloneTargets(IEnumerable<ShareTarget> targets) =>
        JsonSerializer.Deserialize<List<ShareTarget>>(JsonSerializer.Serialize(targets, JsonOptions), JsonOptions)!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Keep "Ctrl+1" readable rather than escaping the plus sign; this file is only read locally.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Loads settings. With <paramref name="writeBack"/>, saves them again so new options show up in the file.
    /// A broken file is left alone and defaults are used.
    /// </summary>
    public static Settings Load(Action<Exception> log, bool writeBack = true)
    {
        Settings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings()
                : new Settings { Version = CurrentVersion, IsFirstRun = true };
        }
        catch (Exception ex)
        {
            log(ex);
            return new Settings { Version = CurrentVersion };
        }
        settings.Migrate();

        if (!writeBack)
            return settings;
        try
        {
            settings.Save();
        }
        catch (Exception ex)
        {
            log(ex);
        }
        return settings;
    }

    private void Migrate()
    {
        // Only move the share hotkey if it was still the old default; a deliberate choice is kept.
        if (Version < 2 && ShareHotkey == new Hotkey(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.Home))
            ShareHotkey = Hotkey.DefaultShare;

        if (Version < 3)
        {
            foreach (var target in ShareTargets)
            {
                if (string.Equals(target.FocusProcess, "slack", StringComparison.OrdinalIgnoreCase))
                {
                    target.Delivery = ShareDelivery.DropAtPointer;
                    target.KeysAfterOpening ??= "Ctrl+K";
                }
                else
                {
                    target.Delivery = target.LegacyClipboard switch
                    {
                        ClipboardContent.Path => ShareDelivery.TypePath,
                        ClipboardContent.None => ShareDelivery.OpenOnly,
                        _ => ShareDelivery.DropOnWindow,
                    };
                }
                target.LegacyClipboard = null;
            }
        }

        if (Version < 4)
        {
            if (ShareHotkey == new Hotkey(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.D3))
                ShareHotkey = Hotkey.DefaultShare;
            foreach (var target in ShareTargets)
            {
                if (target.Method == ShareMethod.App && target.FocusProcess is "claude" or "ChatGPT" or "Grok Bot")
                    target.Delivery = ShareDelivery.DropAtPointer;
            }
        }

        if (Version < 5)
        {
            // Move the old defaults (image Ctrl+1, share Ctrl+2) up one to make room for copy path on Ctrl+1.
            var ctrl = System.Windows.Input.ModifierKeys.Control;
            if (CopyImageHotkey == new Hotkey(ctrl, System.Windows.Input.Key.D1) && ShareHotkey == new Hotkey(ctrl, System.Windows.Input.Key.D2))
            {
                CopyImageHotkey = Hotkey.DefaultCopyImage;
                ShareHotkey = Hotkey.DefaultShare;
            }
            // Customised keys stay; give copy path the first free Ctrl+digit.
            var taken = new HashSet<Hotkey> { CaptureHotkey, CopyImageHotkey, ShareHotkey };
            for (var key = System.Windows.Input.Key.D1; taken.Contains(CopyPathHotkey) && key <= System.Windows.Input.Key.D9; key++)
                CopyPathHotkey = new Hotkey(ctrl, key);
        }
        Version = CurrentVersion;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>
    /// Just Email, which works on any PC. On first run the welcome window adds the user's own apps
    /// (see <see cref="InstalledApps"/>).
    /// </summary>
    private static List<ShareTarget> DefaultShareTargets() => new() { EmailTarget() };

    /// <summary>Email through the default mail program (attached via MAPI, or a blank email to drop into).</summary>
    public static ShareTarget EmailTarget() => new()
    {
        Label = "Email",
        Method = ShareMethod.Email,
        Launch = "mailto:?subject=Screenshot",
    };
}
