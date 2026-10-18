using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nivetray.Core;
using Drawing = System.Drawing;

namespace Nivetray.App;

internal sealed class ExecutableIcons(Action<string> log)
{
    private readonly BoundedResourceCache<ImageSource> cache = new(64, path => Read(path, log));
    public ImageSource? Cached(string path) => cache.Cached(path);
    public ImageSource? Get(string path, CancellationToken cancellation)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        return cache.GetAsync(path, TimeSpan.FromMilliseconds(250), cancellation).GetAwaiter().GetResult();
    }
    private static ImageSource? Read(string path, Action<string> log)
    {
        try
        {
            using var icon = Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(28, 28));
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.ComponentModel.Win32Exception or
            UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        { log("Application icon unavailable: " + ex.GetType().Name); return null; }
    }
}
