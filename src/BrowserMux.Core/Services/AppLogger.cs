namespace BrowserMux.Core.Services;

/// <summary>Minimal file logger, watchable with `tail -f` or PowerShell.</summary>
public static class AppLogger
{
    public static readonly string LogPath = AppInfo.LogPath;

    private static readonly object _lock = new();

    static AppLogger()
    {
        var dir = Path.GetDirectoryName(LogPath)!;
        Directory.CreateDirectory(dir);
        // Simple rotation: keep only the last 500 lines
        TrimLog();
    }

    public static void Info(string message)  => Write("INFO ", message);
    public static void Warn(string message)  => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message} → {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
        lock (_lock)
        {
            try { File.AppendAllText(LogPath, line + Environment.NewLine); }
            catch { /* Don't crash if log is inaccessible */ }
        }
        System.Diagnostics.Debug.WriteLine(line);
    }

    private static void TrimLog()
    {
        if (!File.Exists(LogPath)) return;
        try
        {
            var lines = File.ReadAllLines(LogPath);
            if (lines.Length > 500)
                File.WriteAllLines(LogPath, lines[^400..]);
        }
        catch { }
    }
}
