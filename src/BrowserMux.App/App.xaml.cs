using System.IO.Pipes;
using BrowserMux.App.Services;
using BrowserMux.App.Windows;
using BrowserMux.Core;
using BrowserMux.Core.Services;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;

namespace BrowserMux.App;

public partial class App : Application
{
    private PickerWindow? _pickerWindow;
    private CancellationTokenSource? _pipeCts;
    private Mutex? _singleInstanceMutex;
    private TrayIconWithContextMenu? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private GlobalHotkeyService? _hotkeyService;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            var log = AppInfo.CrashPath;
            File.WriteAllText(log, $"[{DateTime.Now}]\n{e.Message}\n{e.Exception}");
            e.Handled = false;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstanceMutex = new Mutex(true, AppInfo.MutexName, out bool isOwned);
        if (!isOwned)
        {
            var cmdArgs = Environment.GetCommandLineArgs();
            var url = cmdArgs.Length > 1 ? cmdArgs[1] : null;
            if (url is not null)
                _ = TrySendUrlToPipe(url);
            Exit();
            return;
        }

        var launchArgs = Environment.GetCommandLineArgs();
        var launchUrl = launchArgs.Length > 1 ? launchArgs[1] : null;

        _pickerWindow = new PickerWindow();

        if (launchUrl is not null)
            _pickerWindow.ShowForUrl(launchUrl);

        SetupTrayIcon();
        SetupLauncherHotkey();

        PreferencesService.Instance.SettingsChanged += () =>
        {
            _pickerWindow?.DispatcherQueue.TryEnqueue(SetupLauncherHotkey);
        };

        _pipeCts = new CancellationTokenSource();
        _ = StartPipeServerAsync(_pipeCts.Token);
        _ = CheckForUpdatesOnStartupAsync();
    }

    private static async Task CheckForUpdatesOnStartupAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        await Services.UpdateChecker.CheckAsync();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TrayIconWithContextMenu
        {
            ToolTip = AppInfo.AppName,
            Icon = CreateTrayIcon(),
        };

        _trayIcon.ContextMenu = new PopupMenu
        {
            Items =
            {
                new PopupMenuItem("Open launcher", (_, _) =>
                {
                    _pickerWindow?.DispatcherQueue.TryEnqueue(() => _pickerWindow.ShowAsLauncher());
                }),
                new PopupMenuItem("Open URL from clipboard", (_, _) =>
                {
                    _pickerWindow?.DispatcherQueue.TryEnqueue(OpenUrlFromClipboard);
                }),
                new PopupMenuItem("Settings", (_, _) =>
                {
                    _pickerWindow?.DispatcherQueue.TryEnqueue(OpenSettings);
                }),
                new PopupMenuSeparator(),
                new PopupMenuItem("Exit", (_, _) =>
                {
                    _trayIcon?.Dispose();
                    _pickerWindow?.DispatcherQueue.TryEnqueue(Exit);
                }),
            }
        };

        _trayIcon.MessageWindow.MouseEventReceived += (_, e) =>
        {
            if (e.MouseEvent == H.NotifyIcon.Core.MouseEvent.IconLeftMouseUp)
            {
                _pickerWindow?.DispatcherQueue.TryEnqueue(() => _pickerWindow.ShowAsLauncher());
            }
        };

        _trayIcon.Create();
    }

    internal static string AppIconPath =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

    private static System.Drawing.Icon? _trayIconResource;

    private static nint CreateTrayIcon()
    {
        _trayIconResource = new System.Drawing.Icon(AppIconPath, 32, 32);
        return _trayIconResource.Handle;
    }

    private async void OpenUrlFromClipboard()
    {
        if (_pickerWindow is null) return;

        var dataPackage = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        if (!dataPackage.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            return;

        var raw = (await dataPackage.GetTextAsync())?.Trim();
        if (string.IsNullOrEmpty(raw)) return;

        if (TryNormalizeUrl(raw, out var url))
            _pickerWindow.ShowForUrl(url);
    }

    private static bool TryNormalizeUrl(string input, out string url)
    {
        url = "";
        // Strip surrounding whitespace and common wrappers (<>, quotes)
        var s = input.Trim().Trim('<', '>', '"', '\'');
        if (s.Length == 0 || s.Length > 2048) return false;
        if (s.Any(char.IsWhiteSpace)) return false;

        // Add scheme if missing (e.g., "github.com/foo")
        if (!s.Contains("://", StringComparison.Ordinal))
            s = "https://" + s;

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(uri.Host) || !uri.Host.Contains('.')) return false;

        url = uri.AbsoluteUri;
        return true;
    }

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Activate();
    }

    private void SetupLauncherHotkey()
    {
        _hotkeyService ??= new GlobalHotkeyService();
        var hotkey = PreferencesService.Instance.Current.Settings.LauncherHotkey;
        _hotkeyService.Register(hotkey, () =>
        {
            _pickerWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                _pickerWindow.ShowAsLauncher();
            });
        });
    }

    private static async Task TrySendUrlToPipe(string url)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", AppInfo.PipeName, PipeDirection.Out);
            using var cts = new CancellationTokenSource(millisecondsDelay: 300);
            await client.ConnectAsync(cts.Token);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync(url);
        }
        catch { }
    }

    private async Task StartPipeServerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    AppInfo.PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server);
                var url = await reader.ReadLineAsync(ct);

                if (!string.IsNullOrWhiteSpace(url))
                {
                    _pickerWindow?.DispatcherQueue.TryEnqueue(() =>
                    {
                        _pickerWindow.ShowForUrl(url);
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }
}
