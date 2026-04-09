using Microsoft.UI.Xaml;

namespace BrowserMux.App.Services;

public static class WindowExtensions
{
    /// <summary>Walks up the visual tree to find the parent Window.</summary>
    public static Window? FindParentWindow(this FrameworkElement element)
    {
        var content = element;
        while (content.Parent is FrameworkElement parent)
            content = parent;

        // In WinUI 3, the root is the Window's content
        foreach (var window in _activeWindows)
        {
            if (window.Content == content)
                return window;
        }
        return null;
    }

    private static readonly List<Window> _activeWindows = [];

    public static void Register(Window window) => _activeWindows.Add(window);
    public static void Unregister(Window window) => _activeWindows.Remove(window);
}
