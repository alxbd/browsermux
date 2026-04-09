using BrowserMux.App.Controls;
using BrowserMux.App.Services;
using BrowserMux.App.ViewModels;
using BrowserMux.Core;
using BrowserMux.Core.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using VirtualKey = global::Windows.System.VirtualKey;

namespace BrowserMux.App.Windows;

public sealed partial class PickerWindow : Window
{
    public static PickerViewModel ViewModel { get; } = new();

    private bool _applyingSettings;
    private bool _micaSet;

    public PickerWindow()
    {
        InitializeComponent();
        Title = AppInfo.AppName;
        ConfigureWindow();

        // Wire VM events
        ViewModel.RequestHide += HideWindow;
        ViewModel.RequestActivate += OnViewModelActivate;
        ViewModel.RequestShowSettings += OpenSettings;

        // React to VM hint text / visibility changes for elements that can't use x:Bind easily
        ViewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(PickerViewModel.HintText):
                    ShiftHint.Text = ViewModel.HintText;
                    break;
                case nameof(PickerViewModel.HintOpacity):
                    ShiftHint.Opacity = ViewModel.HintOpacity;
                    break;
                case nameof(PickerViewModel.UrlBarVisibility):
                    UrlBar.Visibility = ViewModel.UrlBarVisibility;
                    break;
                case nameof(PickerViewModel.CurrentUrl):
                    UrlText.Text = ViewModel.CurrentUrl;
                    break;
                case nameof(PickerViewModel.SetDefaultLinkVisibility):
                    SetDefaultLink.Visibility = ViewModel.SetDefaultLinkVisibility;
                    break;
                case nameof(PickerViewModel.ShiftHintVisibility):
                    ShiftHint.Visibility = ViewModel.ShiftHintVisibility;
                    break;
                case nameof(PickerViewModel.IsAltHeld):
                    RefreshAllCardIcons();
                    break;
            }
        };

        // Sync browsers collection to GridView
        ViewModel.Browsers.CollectionChanged += (_, _) =>
        {
            BrowsersPanel.ItemsSource = null;
            BrowsersPanel.ItemsSource = ViewModel.Browsers;
        };

        PreferencesService.Instance.SettingsChanged += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_applyingSettings) return;
                _applyingSettings = true;
                try
                {
                    ApplyGridSettings();
                    ApplyAlwaysOnTop();
                    ApplyThemeAndBackground();
                    PositionWindow();
                }
                finally { _applyingSettings = false; }
            });
        };
    }

    // ── Public entry points (called by App.xaml.cs) ─────────────────────────

    public void ShowForUrl(string url)
    {
        if (!ViewModel.ShowForUrl(url))
            return; // auto-launched, don't show
    }

    public void ShowAsLauncher()
    {
        ViewModel.ShowAsLauncher();
    }

    // ── VM event handlers ───────────────────────────────────────────────────

    private void OnViewModelActivate()
    {
        ApplyGridSettings();
        ApplyAlwaysOnTop();
        ApplyThemeAndBackground();
        PositionWindow();

        RootGrid.Opacity = 1;
        Activate();

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (BrowsersPanel.Items.Count == 0) return;
            if (BrowsersPanel.SelectedIndex < 0) BrowsersPanel.SelectedIndex = 0;
            BrowsersPanel.Focus(FocusState.Programmatic);
        });
    }

    private SettingsWindow? _settingsWindow;
    private string? _pendingUrl;
    private bool _pendingLauncher;

    private void OpenSettings()
    {
        // If the picker is currently visible, capture its state and hide it
        // while Settings is open. We re-show it when Settings closes.
        if (AppWindow.IsVisible)
        {
            if (ViewModel.IsLauncherMode)
                _pendingLauncher = true;
            else if (!string.IsNullOrEmpty(ViewModel.CurrentUrl))
                _pendingUrl = ViewModel.CurrentUrl;

            AppWindow.Hide();
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                var url = _pendingUrl;
                var launcher = _pendingLauncher;
                _pendingUrl = null;
                _pendingLauncher = false;
                if (launcher)
                    DispatcherQueue.TryEnqueue(ShowAsLauncher);
                else if (url is not null)
                    DispatcherQueue.TryEnqueue(() => ShowForUrl(url));
            };
            _settingsWindow.RecreateRequested += () =>
            {
                // Defer reopen until after the current Close completes.
                DispatcherQueue.TryEnqueue(() =>
                {
                    _settingsWindow = null;
                    OpenSettings();
                });
            };
        }
        _settingsWindow.Activate();
    }

    // ── Window chrome (stays in code-behind) ────────────────────────────────

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        AppWindow.SetIcon(App.AppIconPath);

        SystemBackdropHelper.Apply(this);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }

        Activated += OnFirstActivated;
        Activated += OnActivationChanged;
        AppWindow.Closing += (_, args) => { args.Cancel = true; HideWindow(); };

        Content.KeyDown += OnKeyDown;
        Content.KeyUp   += OnKeyUp;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_micaSet) return;
        _micaSet = true;
        ApplyAlwaysOnTop();
        ApplyThemeAndBackground();
    }

    private void OnActivationChanged(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated
            && PreferencesService.Instance.Current.Settings.CloseOnFocusLoss)
            HideWindow();
    }

    private void ApplyAlwaysOnTop()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = PreferencesService.Instance.Current.Settings.AlwaysOnTop;
    }

    private void ApplyThemeAndBackground()
    {
        var settings = PreferencesService.Instance.Current.Settings;
        var isDark = settings.Theme == Core.Models.AppTheme.Dark
            || (settings.Theme == Core.Models.AppTheme.System && IsSystemDarkTheme());

        if (Content is FrameworkElement root)
            root.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;

        // Background is transparent — Mica is provided by Window.SystemBackdrop.

        var titleBar = AppWindow.TitleBar;
        var fg = isDark ? global::Windows.UI.Color.FromArgb(255, 255, 255, 255)
                        : global::Windows.UI.Color.FromArgb(255, 0, 0, 0);
        var bg = global::Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ForegroundColor = fg;
        titleBar.ButtonForegroundColor = fg;
        titleBar.ButtonBackgroundColor = bg;
        titleBar.ButtonInactiveBackgroundColor = bg;
        titleBar.ButtonHoverBackgroundColor = isDark
            ? global::Windows.UI.Color.FromArgb(30, 255, 255, 255)
            : global::Windows.UI.Color.FromArgb(30, 0, 0, 0);
        titleBar.ButtonHoverForegroundColor = fg;
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int i && i == 0;
        }
        catch { return false; }
    }

    // ── Grid layout ─────────────────────────────────────────────────────────

    // ── Vertical list (Command Palette style) ───────────────────────────────

    private const int RowHeight = 58;        // 56 row + 2 margin
    private const int MaxVisibleRows = 8;
    private const int ListWidth = 440;

    private void ApplyGridSettings()
    {
        BrowsersPanel.MaxHeight = MaxVisibleRows * RowHeight + 12;
    }

    private int VisibleRowCount() => Math.Min(Math.Max(ViewModel.Browsers.Count, 1), MaxVisibleRows);

    // ── Positioning ─────────────────────────────────────────────────────────

    private (double w, double h) ComputeWindowSize()
    {
        double urlBarHeight = ViewModel.IsLauncherMode ? 0 : 44;
        double titleH = 40;
        double footerH = 48;
        double listH = VisibleRowCount() * RowHeight + 24;
        double h = titleH + urlBarHeight + listH + footerH;
        return (ListWidth, h);
    }

    private void PositionWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = CursorHelper.GetDpiScale(hwnd);

        var (wDip, hDip) = ComputeWindowSize();
        int w = (int)(wDip * scale);
        int h = (int)(hDip * scale);

        var cursorPos = CursorHelper.GetCursorPosition();
        var displayArea = DisplayArea.GetFromPoint(
            new PointInt32(cursorPos.X, cursorPos.Y), DisplayAreaFallback.Primary);

        var workArea = displayArea.WorkArea;
        int x = workArea.X + (workArea.Width - w) / 2;
        int y = workArea.Y + (workArea.Height - h) / 2;

        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    // ── Show/Hide ───────────────────────────────────────────────────────────

    private void HideWindow()
    {
        AppWindow.Hide();
    }

    // ── Keyboard routing → VM ───────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu)
        {
            ViewModel.SetAltHeld(true);
            RefreshAllCardIcons();
            e.Handled = true;
            return;
        }

        if (e.Key is VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.Shift)
        {
            if (!ViewModel.IsAltHeld) ViewModel.SetAlwaysOpenMode(true);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                ViewModel.CancelCommand.Execute(null);
                e.Handled = true;
                break;

            case VirtualKey.C:
                ViewModel.CopyUrlCommand.Execute(null);
                if (!ViewModel.IsLauncherMode) ShowCopyFeedback();
                e.Handled = true;
                break;

            case >= VirtualKey.Number1 and <= VirtualKey.Number9:
                var idx = e.Key - VirtualKey.Number1;
                if (idx < ViewModel.Browsers.Count)
                    ViewModel.LaunchCard(ViewModel.Browsers[idx], incognito: false);
                e.Handled = true;
                break;

            case VirtualKey.Tab:
                if (ViewModel.Browsers.Count == 0) break;
                var shift = Microsoft.UI.Input.InputKeyboardSource
                    .GetKeyStateForCurrentThread(VirtualKey.Shift)
                    .HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
                var si = ViewModel.SelectedIndex;
                ViewModel.SelectedIndex = shift
                    ? (si <= 0 ? ViewModel.Browsers.Count - 1 : si - 1)
                    : ((si + 1) % ViewModel.Browsers.Count);
                BrowsersPanel.SelectedIndex = ViewModel.SelectedIndex;
                BrowsersPanel.ScrollIntoView(BrowsersPanel.SelectedItem);
                e.Handled = true;
                break;

            case VirtualKey.Up:
                if (ViewModel.Browsers.Count == 0) break;
                BrowsersPanel.SelectedIndex = BrowsersPanel.SelectedIndex <= 0
                    ? ViewModel.Browsers.Count - 1
                    : BrowsersPanel.SelectedIndex - 1;
                ViewModel.SelectedIndex = BrowsersPanel.SelectedIndex;
                BrowsersPanel.ScrollIntoView(BrowsersPanel.SelectedItem);
                e.Handled = true;
                break;

            case VirtualKey.Down:
                if (ViewModel.Browsers.Count == 0) break;
                BrowsersPanel.SelectedIndex = (BrowsersPanel.SelectedIndex + 1) % ViewModel.Browsers.Count;
                ViewModel.SelectedIndex = BrowsersPanel.SelectedIndex;
                BrowsersPanel.ScrollIntoView(BrowsersPanel.SelectedItem);
                e.Handled = true;
                break;

            case VirtualKey.Enter:
                var sel = BrowsersPanel.SelectedIndex >= 0 ? BrowsersPanel.SelectedIndex : ViewModel.SelectedIndex;
                if (sel >= 0 && sel < ViewModel.Browsers.Count)
                {
                    var altDown = Microsoft.UI.Input.InputKeyboardSource
                        .GetKeyStateForCurrentThread(VirtualKey.Menu)
                        .HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
                    ViewModel.LaunchCard(ViewModel.Browsers[sel], incognito: altDown);
                }
                e.Handled = true;
                break;
        }
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu)
        {
            ViewModel.SetAltHeld(false);
            var shiftState = Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Shift);
            if ((shiftState & global::Windows.UI.Core.CoreVirtualKeyStates.Down) != 0)
                ViewModel.SetAlwaysOpenMode(true);
            RefreshAllCardIcons();
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.Shift)
        {
            ViewModel.SetAlwaysOpenMode(false);
            e.Handled = true;
        }
    }

    // ── UI helpers ──────────────────────────────────────────────────────────

    private void RefreshAllCardIcons()
    {
        foreach (var container in BrowsersPanel.Items
            .Select((_, i) => BrowsersPanel.ContainerFromIndex(i))
            .OfType<GridViewItem>())
        {
            FindChild<BrowserCard>(container)?.RefreshPinIcon();
        }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindChild<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private async void ShowCopyFeedback()
    {
        CopyIcon.Glyph = "\uE73E";
        CopyLabel.Text = "Copied!";
        await Task.Delay(1200);
        CopyIcon.Glyph = "\uE8C8";
        CopyLabel.Text = "Copy URL";
    }

    // ── XAML event handlers (delegate to VM) ────────────────────────────────

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CopyUrlCommand.Execute(null);
        if (!ViewModel.IsLauncherMode) ShowCopyFeedback();
    }

    private void SetDefaultBrowser_Click(object sender, RoutedEventArgs e)
        => ViewModel.SetDefaultBrowserCommand.Execute(null);

    private void RulesButton_Click(object sender, RoutedEventArgs e)
        => ViewModel.OpenSettingsCommand.Execute(null);

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => ViewModel.CancelCommand.Execute(null);
}
