using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrowserMux.Core;
using BrowserMux.Core.Models;
using BrowserMux.Core.Services;

namespace BrowserMux.App.Services;

public sealed partial class UpdateChecker
{
    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", $"{AppInfo.AppName}/{AppInfo.AppVersion}" },
            { "Accept", "application/vnd.github+json" },
        },
        Timeout = TimeSpan.FromSeconds(10),
    };

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private static readonly string ApiUrl =
        $"https://api.github.com/repos/{AppInfo.GitHubRepo}/releases/latest";

    /// <summary>
    /// Checks for a newer release on GitHub. Returns null on any failure (network, rate limit, etc.).
    /// Respects a 6-hour cooldown stored in preferences.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(bool force = false, CancellationToken ct = default)
    {
        try
        {
            if (!force && !IsCooldownExpired())
                return null;

            var response = await Http.GetAsync(ApiUrl, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var release = await response.Content.ReadFromJsonAsync(GitHubReleaseContext.Default.GitHubRelease, ct);
            if (release?.TagName is null)
                return null;

            SaveCheckTimestamp();

            var latestStr = release.TagName.TrimStart('v');
            if (!Version.TryParse(latestStr, out var latest) ||
                !Version.TryParse(AppInfo.AppVersion, out var current))
                return null;

            string? downloadUrl = null;
            foreach (var asset in release.Assets ?? [])
            {
                if (asset.Name?.Contains("Setup", StringComparison.OrdinalIgnoreCase) == true
                    && asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.BrowserDownloadUrl;
                    break;
                }
            }

            var releaseUrl = release.HtmlUrl;

            return new UpdateInfo(
                IsAvailable: latest > current,
                CurrentVersion: AppInfo.AppVersion,
                LatestVersion: latestStr,
                DownloadUrl: downloadUrl,
                ReleaseNotesUrl: releaseUrl);
        }
        catch (Exception ex)
        {
            AppLogger.Info($"[UpdateChecker] Check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads the installer to %TEMP% and returns the file path, or null on failure.
    /// </summary>
    public static async Task<string?> DownloadInstallerAsync(
        string downloadUrl,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var destPath = Path.Combine(Path.GetTempPath(), $"BrowserMux-Setup-latest.exe");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var file = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long downloaded = 0;
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloaded += bytesRead;
                if (totalBytes > 0)
                    progress?.Report((double)downloaded / totalBytes);
            }

            return destPath;
        }
        catch (Exception ex)
        {
            AppLogger.Info($"[UpdateChecker] Download failed: {ex.Message}");
            return null;
        }
    }

    private static bool IsCooldownExpired()
    {
        var last = PreferencesService.Instance.Current.Settings.LastUpdateCheck;
        if (last is null) return true;
        return !DateTime.TryParse(last, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
               || DateTime.UtcNow - dt > CheckInterval;
    }

    private static void SaveCheckTimestamp()
    {
        PreferencesService.Instance.Current.Settings.LastUpdateCheck = DateTime.UtcNow.ToString("o");
        PreferencesService.Instance.Save();
    }

    // ── GitHub API response models (source-generated JSON) ─────────────────

    internal sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    internal sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }

    [JsonSerializable(typeof(GitHubRelease))]
    internal sealed partial class GitHubReleaseContext : JsonSerializerContext;
}
