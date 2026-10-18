using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace Nivetray.App;

internal sealed class ExecutableIcons(Action<string> log)
{
    private readonly Dictionary<string, ImageSource?> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();
    public ImageSource? Cached(string path) { lock (sync) return cache.GetValueOrDefault(path); }
    public ImageSource? Get(string path)
    {
        lock (sync)
        {
            if (cache.TryGetValue(path, out var cached)) return cached;
            ImageSource? source = null;
            try
            {
                using var icon = Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                {
                    source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(28, 28));
                    source.Freeze();
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or System.ComponentModel.Win32Exception or
                UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
            { log("Application icon unavailable: " + ex.GetType().Name); }
            if (cache.Count >= 64) cache.Clear();
            cache[path] = source;
            return source;
        }
    }
}
