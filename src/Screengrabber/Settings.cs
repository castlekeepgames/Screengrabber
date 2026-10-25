using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Screengrabber;

internal enum ShareMethod
{
    /// <summary>Put something on the clipboard and bring an app to the front.</summary>
    App,
    /// <summary>Open a new email with the file attached (Simple MAPI), falling back to mailto + clipboard.</summary>
    Email,
}

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
    public ClipboardContent Clipboard { get; set; } = ClipboardContent.Image;

    /// <summary>Exe path, URI, or <c>shell:AppsFolder\&lt;AppID&gt;</c>. Environment variables are expanded.</summary>
    public string? Launch { get; set; }

    /// <summary>Process name (no .exe) whose window is brought to the front if it's already running.</summary>
    public string? FocusProcess { get; set; }

    /// <summary>Press Ctrl+V in the app after focusing it. Off by default: pasting into the wrong window is worse than one keypress.</summary>
    public bool AutoPaste { get; set; }
}

internal sealed class Settings
{
    /// <summary>Steps through snip → whole screen → copy path.</summary>
    public Hotkey CaptureHotkey { get; set; } = Hotkey.DefaultCapture;

    /// <summary>Copies the image to the clipboard. Only registered while the post-capture panel is showing.</summary>
    public Hotkey CopyImageHotkey { get; set; } = Hotkey.DefaultCopyImage;

    /// <summary>Opens the share picker. Only registered while the post-capture panel is showing.</summary>
    public Hotkey ShareHotkey { get; set; } = Hotkey.DefaultShare;

    /// <summary>Where screenshots are saved. Null means &lt;Desktop&gt;\Screenshots.</summary>
    public string? SaveFolder { get; set; }

    public int PanelTimeoutSeconds { get; set; } = 8;

    public bool StartWithWindows { get; set; } = true;

    public List<ShareTarget> ShareTargets { get; set; } = DefaultShareTargets();

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
    /// </summary>
    public int Version { get; set; }

    private const int CurrentVersion = 2;

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
                : new Settings { Version = CurrentVersion };
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
        Version = CurrentVersion;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private static List<ShareTarget> DefaultShareTargets() => new()
    {
        new()
        {
            Label = "Slack",
            Clipboard = ClipboardContent.Image,
            Launch = @"shell:AppsFolder\com.squirrel.slack.slack",
            FocusProcess = "slack",
        },
        new()
        {
            Label = "Email",
            Method = ShareMethod.Email,
            Clipboard = ClipboardContent.None,
            Launch = "mailto:?subject=Screenshot",
            FocusProcess = "Mailbird",
        },
        new()
        {
            Label = "Claude Code",
            Clipboard = ClipboardContent.Path,
            Launch = @"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude",
            FocusProcess = "claude",
        },
        new()
        {
            Label = "Grok Bot",
            Clipboard = ClipboardContent.Image,
            Launch = @"%LOCALAPPDATA%\Programs\Grok Bot\Grok Bot.exe",
            FocusProcess = "Grok Bot",
        },
        new()
        {
            // The Codex desktop app ships as ChatGPT.exe inside the OpenAI.Codex package.
            Label = "Codex",
            Clipboard = ClipboardContent.Path,
            Launch = @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App",
            FocusProcess = "ChatGPT",
        },
    };
}
