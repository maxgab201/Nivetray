namespace Nivetray.Windows;

public static class WindowAppearance
{
    public static void SetDarkCaption(nint handle, bool dark)
    {
        if (handle == 0) return;
        var value = dark ? 1 : 0;
        // Unsupported Windows builds keep their native caption; this is appearance only.
        Native.DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
    }
}
