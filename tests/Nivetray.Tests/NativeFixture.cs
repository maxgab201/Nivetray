using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Nivetray.Tests;

internal static class NativeFixture
{
    internal delegate int PropertyCallback(nint hwnd, nint name, nint data, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int EnumPropsEx(nint hwnd, PropertyCallback callback, nint state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint RemoveProp(nint hwnd, string name);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool AllocConsole();
    [DllImport("kernel32.dll")] internal static extern bool FreeConsole();
    [DllImport("kernel32.dll")] internal static extern nint GetConsoleWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern bool SetConsoleTitle(string title);
    internal static bool DropNivetrayProperty(nint hwnd, bool generation)
    {
        string? found = null;
        EnumPropsEx(hwnd, (_, name, _, _) =>
        {
            if ((nuint)name <= ushort.MaxValue) return 1;
            var text = Marshal.PtrToStringUni(name);
            if (text?.StartsWith("Nivetray.", StringComparison.Ordinal) == true && text.Contains("Generation.") == generation)
                found = text;
            return 1;
        }, 0);
        return found is not null && RemoveProp(hwnd, found) != 0;
    }
}
