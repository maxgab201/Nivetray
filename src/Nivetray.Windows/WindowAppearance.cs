namespace Nivetray.Windows;

public static class WindowAppearance
{
    public static void ConfigureProcessDpi()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)) return;
        if (!Native.SetProcessDpiAwarenessContext(-4)) Native.SetProcessDpiAwarenessContext(-3);
    }
    public static void SetDarkCaption(nint handle, bool dark)
    {
        if (handle == 0) return;
        var value = dark ? 1 : 0;
        // Unsupported Windows builds keep their native caption; this is appearance only.
        Native.DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
    }
}
