using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Nivetray.Windows;

internal static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
    internal delegate bool EnumProc(nint hwnd, nint state);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint state);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetProp(nint hwnd, string name, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetProp(nint hwnd, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint RemoveProp(nint hwnd, string name);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SendMessageTimeout(nint hwnd, uint message, nint wparam, nint lparam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(nint hwnd, ref Placement placement);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Placement
    {
        public int Length, Flags, ShowCommand;
        public Point MinPosition, MaxPosition;
        public Rect NormalPosition;
    }
}
