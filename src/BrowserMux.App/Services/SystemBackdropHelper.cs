using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BrowserMux.App.Services;

/// <summary>
/// Applies a Mica backdrop on Windows 11+ and falls back to a solid theme-aware
/// background on Windows 10 (where Mica isn't supported and a transparent root
/// would otherwise leave the window completely see-through).
/// </summary>
internal static class SystemBackdropHelper
{
    /// <summary>
    /// Try to attach a MicaBackdrop to the window. If Mica isn't supported (Win10),
    /// assigns a solid background brush to the window's root element instead.
    /// </summary>
    /// <param name="window">The window to apply the backdrop to.</param>
    /// <param name="micaKind">Mica kind (Base or BaseAlt). Default Base.</param>
    public static void Apply(Window window, MicaKind micaKind = MicaKind.Base)
    {
        if (MicaController.IsSupported())
        {
            window.SystemBackdrop = new MicaBackdrop { Kind = micaKind };
            return;
        }

        // Win10 fallback: paint the window root with a solid theme brush. We always
        // overwrite — XAML often sets the root to Transparent so Mica can show through,
        // and that "transparent for Mica" choice is exactly what we need to undo here.
        if (window.Content is FrameworkElement root &&
            Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] is Brush brush)
        {
            if (root is Microsoft.UI.Xaml.Controls.Panel panel)
                panel.Background = brush;
            else if (root is Microsoft.UI.Xaml.Controls.Control ctrl)
                ctrl.Background = brush;
        }
    }
}
