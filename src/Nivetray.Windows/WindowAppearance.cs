namespace Nivetray.Windows;

public static class WindowAppearance
{
    public static void ConfigureUiThreadDpi()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)) return;
        if (Native.SetThreadDpiAwarenessContext(-4) == 0) Native.SetThreadDpiAwarenessContext(-3);
    }
    public static void SetDarkCaption(nint handle, bool dark)
    {
        if (handle == 0) return;
        var value = dark ? 1 : 0;
        // Unsupported Windows builds keep their native caption; this is appearance only.
        Native.DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
    }
}
