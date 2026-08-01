using System.Runtime.InteropServices;
using BrowserMux.Core.Services;

namespace BrowserMux.App.Services;

/// <summary>
/// Forces a window to become the foreground window.
///
/// <see cref="Microsoft.UI.Xaml.Window.Activate"/> is not enough: Windows only lets a process
/// steal the foreground when it already owns it, was started by the process that owns it, or
/// just received user input or a hotkey. BrowserMux usually fails all three — the picker is
/// shown from a named-pipe message sent by the Handler, in the background. The request is then
/// denied, the window still appears (it is always-on-top) but keyboard input keeps going to
/// whatever the user was using, so the arrow keys do nothing until the window is clicked.
/// </summary>
internal static class ForegroundHelper
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, nint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>
    /// Brings <paramref name="hwnd"/> to the foreground and gives it keyboard focus.
    ///
    /// When the plain call is refused, we briefly attach our input queue to the current
    /// foreground thread's. Two threads sharing an input queue share a foreground state, so
    /// the restriction no longer applies. Detaching again immediately keeps the side effects
    /// to the duration of the call.
    /// </summary>
    public static void ForceForeground(nint hwnd)
    {
        if (hwnd == 0) return;

        var foreground = GetForegroundWindow();
        if (foreground == hwnd) return;

        if (SetForegroundWindow(hwnd)) return;

        var foregroundThread = GetWindowThreadProcessId(foreground, 0);
        var ourThread = GetCurrentThreadId();

        if (foregroundThread == 0 || foregroundThread == ourThread)
        {
            AppLogger.Warn("[Foreground] Could not focus the picker and no thread to attach to");
            return;
        }

        var attached = AttachThreadInput(ourThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            if (!SetForegroundWindow(hwnd))
                AppLogger.Warn($"[Foreground] SetForegroundWindow refused (err={Marshal.GetLastWin32Error()})");
        }
        finally
        {
            if (attached) AttachThreadInput(ourThread, foregroundThread, false);
        }
    }
}
