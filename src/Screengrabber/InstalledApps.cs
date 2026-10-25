using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Screengrabber;

/// <summary>An app from the Start menu. <see cref="AppId"/> launches it via <c>shell:AppsFolder\&lt;AppId&gt;</c>.</summary>
internal sealed record InstalledApp(string Name, string AppId)
{
    public string LaunchTarget => $@"shell:AppsFolder\{AppId}";
}

/// <summary>
/// Lists installed apps (the same list as Start → All apps) and turns them into share targets, so nobody has
/// to type exe paths or app IDs.
/// </summary>
internal static class InstalledApps
{
    /// <summary>Apps worth suggesting as share targets, with extras the Start menu can't tell us.</summary>
    private sealed record Known(Regex Name, string? Label = null, string? Process = null, string? KeysAfterOpening = null);

    private static readonly Known[] Catalog =
    {
        // Ctrl+K opens the channel/conversation switcher in Slack and Discord.
        new(new Regex("^Slack$"), Process: "slack", KeysAfterOpening: "Ctrl+K"),
        new(new Regex("^Discord$"), Process: "Discord", KeysAfterOpening: "Ctrl+K"),
        new(new Regex("^Microsoft Teams"), Label: "Teams", Process: "ms-teams"),
        new(new Regex("^WhatsApp")),
        new(new Regex("^Telegram")),
        new(new Regex("^Signal$")),
        new(new Regex("^Claude$")),
        new(new Regex("^ChatGPT$")),
        new(new Regex("^Codex")),
        new(new Regex("^Grok")),
        new(new Regex("^(Microsoft 365 )?Copilot$")),
    };

    /// <summary>Things in the Start menu that aren't apps you'd share to.</summary>
    private static readonly Regex NotAnApp = new(@"uninstall|readme|release notes|help|documentation|website|license|support",
        RegexOptions.IgnoreCase);

    /// <summary>All Start menu apps, sorted by name. Takes about a second; runs on its own STA thread (Shell COM).</summary>
    public static Task<List<InstalledApp>> ListAsync()
    {
        var result = new TaskCompletionSource<List<InstalledApp>>();
        var thread = new Thread(() =>
        {
            try
            {
                result.SetResult(List());
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                result.SetResult(new List<InstalledApp>());
            }
        })
        {
            IsBackground = true,
            Name = "List installed apps",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    private static List<InstalledApp> List()
    {
        Type shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");

        var apps = new List<InstalledApp>();
        foreach (dynamic item in folder.Items())
        {
            string name = item.Name;
            string id = item.Path;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || NotAnApp.IsMatch(name) ||
                id.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;
            apps.Add(new InstalledApp(name, id));
        }
        return apps.DistinctBy(a => a.AppId).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// The installed apps that are in the catalog of good share targets, one per name. When a name appears twice
    /// (e.g. two "ChatGPT" entries), prefer the one whose process can be identified.
    /// </summary>
    public static List<InstalledApp> Suggested(IEnumerable<InstalledApp> apps) =>
        apps.Where(a => Catalog.Any(k => k.Name.IsMatch(a.Name)))
            .GroupBy(a => a.Name)
            .Select(g => g.OrderByDescending(a => ProcessNameOf(a) != null).First())
            .ToList();

    /// <summary>A point-and-drop share target for the app.</summary>
    public static ShareTarget ToShareTarget(InstalledApp app)
    {
        Known? known = Catalog.FirstOrDefault(k => k.Name.IsMatch(app.Name));
        return new ShareTarget
        {
            Label = known?.Label ?? app.Name,
            Method = ShareMethod.App,
            Delivery = ShareDelivery.DropAtPointer,
            Launch = app.LaunchTarget,
            FocusProcess = known?.Process ?? ProcessNameOf(app),
            KeysAfterOpening = known?.KeysAfterOpening,
        };
    }

    /// <summary>
    /// The process an app runs as, when it can be worked out: the exe for desktop apps, or the manifest's
    /// executable for Store/MSIX apps. Null otherwise; sharing then follows whichever window comes forward.
    /// </summary>
    private static string? ProcessNameOf(InstalledApp app)
    {
        try
        {
            if (app.AppId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return Path.GetFileNameWithoutExtension(app.AppId.Split('\\', '/').Last());

            int bang = app.AppId.IndexOf('!');
            return bang > 0 ? PackagedExecutable(app.AppId[..bang], app.AppId[(bang + 1)..]) : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return null;
        }
    }

    /// <summary>Reads Application[@Id].Executable from the package's AppxManifest.xml.</summary>
    private static string? PackagedExecutable(string familyName, string applicationId)
    {
        // Family "Name_PublisherId"; installed packages are listed by full name "Name_Version_Arch_Resource_PublisherId".
        int split = familyName.LastIndexOf('_');
        if (split <= 0)
            return null;
        string name = familyName[..split], publisher = familyName[(split + 1)..];

        using RegistryKey? packages = Registry.CurrentUser.OpenSubKey(
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        if (packages == null)
            return null;
        foreach (string fullName in packages.GetSubKeyNames())
        {
            if (!fullName.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) ||
                !fullName.EndsWith("_" + publisher, StringComparison.OrdinalIgnoreCase))
                continue;
            using RegistryKey? package = packages.OpenSubKey(fullName);
            if (package?.GetValue("PackageRootFolder") is not string root)
                continue;
            string manifest = Path.Combine(root, "AppxManifest.xml");
            if (!File.Exists(manifest))
                continue;

            string? executable = XDocument.Load(manifest).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Application" && (string?)e.Attribute("Id") == applicationId)
                ?.Attribute("Executable")?.Value;
            return executable == null ? null : Path.GetFileNameWithoutExtension(executable);
        }
        return null;
    }
}
