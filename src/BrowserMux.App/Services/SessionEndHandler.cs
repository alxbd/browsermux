using System.Runtime.InteropServices;
using BrowserMux.Core.Services;

namespace BrowserMux.App.Services;

/// <summary>
/// Answers the Windows end-session protocol (WM_QUERYENDSESSION / WM_ENDSESSION) on the
/// picker window, so that anything asking BrowserMux to quit actually gets it to quit.
///
/// Required because the picker cancels WM_CLOSE to hide into the tray instead of closing.
/// Without this handler nothing in the process ever agrees to shut down, and the Restart
/// Manager — used by the Inno Setup installer (CloseApplications=yes), by Windows Update and
/// by logoff/shutdown — waits 30 seconds then fails with ERROR_FAIL_SHUTDOWN. Setup reports
/// that as "Setup was unable to automatically close all applications".
/// </summary>
internal static class SessionEndHandler
{
    private const uint WM_QUERYENDSESSION = 0x0011;
    private const uint WM_ENDSESSION      = 0x0016;
    private const int  GWLP_WNDPROC       = -4;

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    // Static, and never cleared: the window keeps the native function pointer for its whole
    // lifetime, so the delegate must outlive every GC pass.
    private static WndProcDelegate? _wndProc;
    private static nint _previousWndProc;
    private static Action? _onSessionEnd;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProcW(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>
    /// Subclasses <paramref name="hwnd"/> so end-session requests reach <paramref name="onSessionEnd"/>.
    /// That callback must terminate the process: Windows and the Restart Manager both wait for
    /// the process to disappear, not for the message to return.
    /// </summary>
    public static void Attach(nint hwnd, Action onSessionEnd)
    {
        if (hwnd == 0)
        {
            AppLogger.Error("[SessionEnd] No window handle — shutdown requests will be ignored");
            return;
        }

        _onSessionEnd = onSessionEnd;
        _wndProc = WndProc;

        _previousWndProc = SetWindowLongPtrW(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProc));
        if (_previousWndProc == 0)
        {
            AppLogger.Error(
                $"[SessionEnd] Subclassing failed (err={Marshal.GetLastWin32Error()}) — " +
                "installers and Windows shutdown will not be able to close BrowserMux");
            return;
        }

        AppLogger.Info("[SessionEnd] Listening for shutdown requests");
    }

    private static nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            // "Can you close?" — always yes. Nothing is held unsaved: preferences and rules
            // are written to disk as they change.
            case WM_QUERYENDSESSION:
                AppLogger.Info($"[SessionEnd] WM_QUERYENDSESSION (lParam=0x{lParam:X}) — accepting");
                return 1;

            // "Close now." wParam = 0 means someone else vetoed the session end, so stay alive.
            case WM_ENDSESSION:
                if (wParam != 0)
                {
                    AppLogger.Info("[SessionEnd] WM_ENDSESSION — shutting down");
                    _onSessionEnd?.Invoke();
                }
                return 0;
        }

        return CallWindowProcW(_previousWndProc, hWnd, msg, wParam, lParam);
    }
}
