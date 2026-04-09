using System.Runtime.InteropServices;
using BrowserMux.Core.Services;

namespace BrowserMux.App.Services;

/// <summary>
/// Registers a system-wide hotkey via Win32 RegisterHotKey.
/// Runs a dedicated message-pump thread so WM_HOTKEY is always received,
/// regardless of the WinUI 3 dispatcher.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_QUIT = 0x0012;
    private const int HOTKEY_ID = 0xB001;

    private Thread? _thread;
    private nint _hwnd;
    private volatile bool _running;
    private Action? _onHotkeyPressed;
    private uint _threadId;

    // Win32 imports
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint dwExStyle, [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("kernel32.dll")]
    private static extern nint GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private delegate nint WNDPROC(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASS
    {
        public uint style;
        public WNDPROC lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    private const nint HWND_MESSAGE = -3;

    // Modifier flags for RegisterHotKey
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    // prevent GC of the delegate
    private WNDPROC? _wndProc;

    public void Register(string? hotkeyString, Action onPressed)
    {
        _onHotkeyPressed = onPressed;

        // Tear down previous thread + hotkey
        Stop();

        if (string.IsNullOrWhiteSpace(hotkeyString))
            return;

        if (!TryParseHotkey(hotkeyString, out uint modifiers, out uint vk))
        {
            AppLogger.Warn($"[GlobalHotkeyService] Invalid hotkey: {hotkeyString}");
            return;
        }

        var ready = new ManualResetEventSlim(false);
        bool regOk = false;

        _running = true;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            CreateMessageWindow();

            if (_hwnd != 0)
            {
                regOk = RegisterHotKey(_hwnd, HOTKEY_ID, modifiers | MOD_NOREPEAT, vk);
                if (!regOk)
                {
                    var err = Marshal.GetLastWin32Error();
                    AppLogger.Warn($"[GlobalHotkeyService] RegisterHotKey failed (err={err}). Key may be in use.");
                }
            }

            ready.Set();

            // Message pump
            while (_running)
            {
                var ret = GetMessageW(out MSG msg, 0, 0, 0);
                if (ret <= 0) break; // WM_QUIT or error
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }

            // Cleanup on this thread
            if (_hwnd != 0)
            {
                UnregisterHotKey(_hwnd, HOTKEY_ID);
                DestroyWindow(_hwnd);
                _hwnd = 0;
            }
        })
        {
            IsBackground = true,
            Name = "BrowserMux_HotkeyThread",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        ready.Wait(TimeSpan.FromSeconds(2));

        if (regOk)
            AppLogger.Info($"[GlobalHotkeyService] Registered hotkey: {hotkeyString}");
    }

    private void Stop()
    {
        if (_thread is null) return;

        _running = false;

        // Post WM_QUIT to unblock GetMessage
        if (_threadId != 0)
            PostThreadMessageW(_threadId, WM_QUIT, 0, 0);

        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _threadId = 0;
    }

    private void CreateMessageWindow()
    {
        var hInstance = GetModuleHandleW(null);
        var className = $"BrowserMux_HotkeyMsg_{Environment.TickCount64}";

        _wndProc = WndProc;
        var wc = new WNDCLASS
        {
            lpfnWndProc = _wndProc,
            hInstance = hInstance,
            lpszClassName = className,
        };

        RegisterClassW(ref wc);

        _hwnd = CreateWindowExW(
            0, className, "", 0,
            0, 0, 0, 0,
            HWND_MESSAGE, 0, hInstance, 0);

        if (_hwnd == 0)
            AppLogger.Warn($"[GlobalHotkeyService] CreateWindowEx failed (err={Marshal.GetLastWin32Error()})");
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_HOTKEY && wParam == HOTKEY_ID)
        {
            _onHotkeyPressed?.Invoke();
            return 0;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Parses a hotkey string like "Ctrl+Alt+B" into Win32 modifiers and virtual key.
    /// </summary>
    public static bool TryParseHotkey(string hotkey, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= MOD_CONTROL; break;
                case "alt":               modifiers |= MOD_ALT; break;
                case "shift":             modifiers |= MOD_SHIFT; break;
                case "win" or "windows":  modifiers |= MOD_WIN; break;
                default: return false;
            }
        }

        if (modifiers == 0) return false; // Must have at least one modifier

        var keyStr = parts[^1].ToUpperInvariant();

        // Single letter A-Z
        if (keyStr.Length == 1 && keyStr[0] >= 'A' && keyStr[0] <= 'Z')
        {
            vk = (uint)keyStr[0]; // VK_A = 0x41 = 'A'
            return true;
        }

        // Single digit 0-9
        if (keyStr.Length == 1 && keyStr[0] >= '0' && keyStr[0] <= '9')
        {
            vk = (uint)keyStr[0]; // VK_0 = 0x30 = '0'
            return true;
        }

        // Function keys F1-F24
        if (keyStr.StartsWith('F') && int.TryParse(keyStr[1..], out int fNum) && fNum >= 1 && fNum <= 24)
        {
            vk = (uint)(0x70 + fNum - 1); // VK_F1 = 0x70
            return true;
        }

        // Special keys
        vk = keyStr switch
        {
            "SPACE"     => 0x20,
            "TAB"       => 0x09,
            "ENTER"     => 0x0D,
            "BACKSPACE" => 0x08,
            "DELETE"    => 0x2E,
            "INSERT"    => 0x2D,
            "HOME"      => 0x24,
            "END"       => 0x23,
            "PAGEUP"    => 0x21,
            "PAGEDOWN"  => 0x22,
            "UP"        => 0x26,
            "DOWN"      => 0x28,
            "LEFT"      => 0x25,
            "RIGHT"     => 0x27,
            "ESCAPE" or "ESC" => 0x1B,
            _           => 0,
        };

        return vk != 0;
    }

    /// <summary>
    /// Formats modifiers + vk back to a display string like "Ctrl + Alt + B".
    /// </summary>
    public static string FormatHotkey(uint modifiers, uint vk)
    {
        var parts = new List<string>();
        if ((modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & MOD_WIN) != 0) parts.Add("Win");

        // Letter
        if (vk >= 'A' && vk <= 'Z')
            parts.Add(((char)vk).ToString());
        else if (vk >= '0' && vk <= '9')
            parts.Add(((char)vk).ToString());
        else if (vk >= 0x70 && vk <= 0x87)
            parts.Add($"F{vk - 0x70 + 1}");
        else
        {
            var name = vk switch
            {
                0x20 => "Space", 0x09 => "Tab", 0x0D => "Enter", 0x08 => "Backspace",
                0x2E => "Delete", 0x2D => "Insert", 0x24 => "Home", 0x23 => "End",
                0x21 => "PageUp", 0x22 => "PageDown",
                0x26 => "Up", 0x28 => "Down", 0x25 => "Left", 0x27 => "Right",
                0x1B => "Escape",
                _ => $"0x{vk:X2}",
            };
            parts.Add(name);
        }

        return string.Join(" + ", parts);
    }
}
