using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Screengrabber;

/// <summary>A newer release on GitHub and its installer.</summary>
internal sealed record UpdateInfo(Version Version, string DownloadUrl, string FileName, long Size, string? Sha256);

/// <summary>
/// Checks GitHub Releases for a newer version, downloads its installer, and runs it. The installer closes this
/// app, upgrades it in place (settings and screenshots are kept) and starts it again.
/// </summary>
internal static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/castlekeepgames/Screengrabber/releases/latest";
    public const string ReleasesPage = "https://github.com/castlekeepgames/Screengrabber/releases";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>This build's version (major.minor.patch).</summary>
    public static Version CurrentVersion { get; } = Normalize(PretendVersion() ?? typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0));

    /// <returns>The newer release, or null if this is the latest (or nothing has been released yet).</returns>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using HttpResponseMessage response = await Http.GetAsync(LatestReleaseApi);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null; // no releases yet
        response.EnsureSuccessStatusCode();

        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        JsonElement release = json.RootElement;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? latest) || Normalize(latest) <= CurrentVersion)
            return null;

        foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith("ScreengrabberSetup-", StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;

            // GitHub reports a "sha256:<hex>" digest for release assets; verify against it when present.
            string? digest = asset.TryGetProperty("digest", out JsonElement d) ? d.GetString() : null;
            string? sha256 = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
            return new UpdateInfo(Normalize(latest), asset.GetProperty("browser_download_url").GetString()!, name,
                asset.GetProperty("size").GetInt64(), sha256);
        }
        Log.Info($"Update check: release {tag} has no installer attached");
        return null;
    }

    /// <summary>Downloads the installer to a temp folder and checks its size and SHA-256.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update)
    {
        string folder = Path.Combine(Path.GetTempPath(), "Screengrabber-update");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, update.FileName);

        using (HttpResponseMessage response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(path);
            await response.Content.CopyToAsync(file);
        }

        var info = new FileInfo(path);
        if (info.Length != update.Size)
            throw new InvalidDataException($"The download is {info.Length:N0} bytes; expected {update.Size:N0}.");
        if (update.Sha256 != null)
        {
            await using var file = File.OpenRead(path);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
            if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download doesn't match the release's checksum.");
        }
        return path;
    }

    /// <summary>Runs the installer with a progress window but no questions. It closes this app before installing.</summary>
    public static void RunInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true })?.Dispose();

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Debug builds can pretend to be an older version (SG_PRETEND_VERSION=1.0.0) to test updating.</summary>
    private static Version? PretendVersion()
    {
#if DEBUG
        return Version.TryParse(Environment.GetEnvironmentVariable("SG_PRETEND_VERSION"), out Version? v) ? v : null;
#else
        return null;
#endif
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub's API requires a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Screengrabber/{typeof(UpdateChecker).Assembly.GetName().Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
