using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BrowserMux.App.Services;

public static class IconExtractor
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    // Cache: key = "exePath|size", value = PNG bytes
    private static readonly ConcurrentDictionary<string, byte[]> _pngCache = new();

    public static async Task<BitmapImage?> GetBitmapFromExeAsync(string exePath, int size = 32)
    {
        var cacheKey = $"{exePath}|{size}";

        // Try cache first
        if (!_pngCache.TryGetValue(cacheKey, out var pngBytes))
        {
            pngBytes = ExtractIconToPng(exePath, size);
            if (pngBytes is null) return null;
            _pngCache[cacheKey] = pngBytes;
        }

        // BitmapImage must be created on the UI thread each time (not shareable)
        var ms = new MemoryStream(pngBytes);
        var stream = ms.AsRandomAccessStream();
        var bitmapImage = new BitmapImage();
        await bitmapImage.SetSourceAsync(stream);
        return bitmapImage;
    }

    private static byte[]? ExtractIconToPng(string exePath, int size)
    {
        var hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
        if (hIcon == IntPtr.Zero) return null;

        try
        {
            using var icon = System.Drawing.Icon.FromHandle(hIcon);
            using var bitmap = icon.ToBitmap();
            using var resized = new System.Drawing.Bitmap(bitmap, size, size);

            using var ms = new MemoryStream();
            resized.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    public static void ClearCache() => _pngCache.Clear();
}
