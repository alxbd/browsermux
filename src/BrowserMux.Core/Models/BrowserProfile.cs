namespace BrowserMux.Core.Models;

public record BrowserProfile
{
    public required string Name { get; init; }
    /// <summary>Folder name in User Data (e.g. "Default", "Profile 1")</summary>
    public required string ProfileDirectory { get; init; }
    public string? AvatarUrl { get; init; }

    /// <summary>
    /// Profile highlight color as ARGB int from Chromium's Local State.
    /// Convert: Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v)
    /// Null if not available (non-Chromium browser).
    /// </summary>
    public int? ProfileColor { get; init; }

    /// <summary>
    /// Stable profile ID. Format: "brave.exe:::Profile 1"
    /// Assigned by BrowserDetector during construction.
    /// </summary>
    public string Id { get; init; } = string.Empty;
}
