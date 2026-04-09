namespace BrowserMux.Core.Models;

public record UpdateInfo(
    bool IsAvailable,
    string CurrentVersion,
    string LatestVersion,
    string? DownloadUrl,
    string? ReleaseNotesUrl);
