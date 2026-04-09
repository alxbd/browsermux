using System.Runtime.InteropServices;

namespace BrowserMux.App.Services;

public static class CursorHelper
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    public static (int X, int Y) GetCursorPosition()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    /// <summary>DPI scale factor for a window (e.g. 1.5 for 150%).</summary>
    public static double GetDpiScale(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }
}
