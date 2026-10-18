using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Nivetray.Core;

namespace Nivetray.Windows;

public sealed class Win32WindowPlatform(Action<string> log) : IWindowPlatform, IDisposable
{
    // Per-run property prevents an old HWND from authorizing operations on a new window,
    // including reuse by the SAME process/thread/class. No foreign code or message hook.
    private readonly string property = "Nivetray." + Guid.NewGuid().ToString("N");
    private readonly string generationProperty = "Nivetray.Generation." + Guid.NewGuid().ToString("N");
    private readonly Dictionary<nint, WindowIdentity> observed = [];
    private nint nextGeneration;
    private static readonly HashSet<string> SystemClasses = new(StringComparer.OrdinalIgnoreCase)
    { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "DV2ControlHost" };
    private static readonly HashSet<string> SystemExecutables = new(StringComparer.OrdinalIgnoreCase)
    { "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "LockApp", "LogonUI", "dwm", "sihost" };

    public IReadOnlyList<AppWindow> Enumerate()
    {
        var result = new List<AppWindow>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            var title = Text(hwnd, false);
            if (string.IsNullOrWhiteSpace(title)) return true;
            try
            {
                var id = ReadIdentity(hwnd);
                if (id is null) return true;
                using var process = Process.GetProcessById(id.ProcessId);
                var path = process.MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) return true;
                Native.DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int));
                if (cloaked != 0 || SystemClasses.Contains(id.ClassName) ||
                    SystemExecutables.Contains(Path.GetFileNameWithoutExtension(path)) || id.ProcessId == Environment.ProcessId)
                    return true;
                // Assign a lifetime stamp while listing, not only when Hide is clicked.
                // Destruction removes window properties even if the HWND is later recycled.
                if (id.InstanceToken == 0)
                {
                    if (observed.TryGetValue(hwnd, out var prior) && prior.ProcessId == id.ProcessId &&
                        prior.ProcessStartedUtcTicks == id.ProcessStartedUtcTicks && prior.ThreadId == id.ThreadId && prior.ClassName == id.ClassName)
                    {
                        log("Window lifetime stamp missing; preserving unresolved identity.");
                        return true;
                    }
                    if (!Native.SetProp(hwnd, generationProperty, checked(++nextGeneration)))
                    { log("Skipped window: access denied while assigning identity."); return true; }
                    id = ReadIdentity(hwnd);
                    if (id is null || id.InstanceToken == 0) return true;
                }
                observed[hwnd] = id;
                result.Add(new(id, title, path, true, Native.GetWindow(hwnd, 4) != 0,
                    (Native.GetWindowLong(hwnd, -20) & 0x80) != 0, cloaked != 0,
                    SystemClasses.Contains(id.ClassName) || SystemExecutables.Contains(Path.GetFileNameWithoutExtension(path)),
                    id.ProcessId == Environment.ProcessId));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException)
            { log("Skipped inaccessible window: " + ex.GetType().Name); }
            return true;
        }, 0);
        foreach (var id in observed.Values.ToArray())
            if (Inspect(id) is IdentityState.Destroyed or IdentityState.Replaced) observed.Remove(id.Handle);
        return result;
    }

    public bool Matches(WindowIdentity identity) => ReadIdentity(identity.Handle) == identity;
    public IdentityState Inspect(WindowIdentity identity)
    {
        var hwnd = identity.Handle;
        if (!Native.IsWindow(hwnd)) return IdentityState.Destroyed;
        var thread = Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (thread == 0 || pid == 0) return Native.IsWindow(hwnd) ? IdentityState.Unavailable : IdentityState.Destroyed;
        if (pid != identity.ProcessId || thread != identity.ThreadId) return IdentityState.Replaced;
        var actual = ReadIdentity(hwnd);
        if (actual is null) return Native.IsWindow(hwnd) ? IdentityState.Unavailable : IdentityState.Destroyed;
        if (actual.ProcessStartedUtcTicks != identity.ProcessStartedUtcTicks || actual.ClassName != identity.ClassName)
            return IdentityState.Replaced;
        if (actual.InstanceToken == 0) return IdentityState.Unavailable;
        return actual.InstanceToken == identity.InstanceToken ? IdentityState.Original : IdentityState.Replaced;
    }
    public bool TryMark(WindowIdentity identity, nint token)
    {
        if (!Matches(identity) || !Native.SetProp(identity.Handle, property, token)) return false;
        if (HasMark(identity, token)) return true;
        RemoveMark(identity, token);
        return false;
    }
    public bool HasMark(WindowIdentity identity, nint token) =>
        Matches(identity) && Native.GetProp(identity.Handle, property) == token;
    public void RemoveMark(WindowIdentity identity, nint token)
    {
        if (HasMark(identity, token)) Native.RemoveProp(identity.Handle, property);
    }
    public int GetShowCommand(WindowIdentity identity)
    {
        var placement = new Native.Placement { Length = Marshal.SizeOf<Native.Placement>() };
        return Matches(identity) && Native.GetWindowPlacement(identity.Handle, ref placement)
            ? placement.ShowCommand : 1;
    }
    public bool IsVisible(WindowIdentity identity) => Matches(identity) && Native.IsWindowVisible(identity.Handle);

    public async Task<bool> SetVisibleAsync(WindowIdentity identity, nint token, bool visible, int showCommand)
    {
        if (!HasMark(identity, token)) return false;
        var command = !visible ? 0 : showCommand switch { 3 => 3, 2 or 6 or 7 => 7, _ => 4 };
        if (!Native.ShowWindowAsync(identity.Handle, command)) return false;
        var confirmations = 0;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(40);
            if (!HasMark(identity, token)) return false;
            confirmations = Native.IsWindowVisible(identity.Handle) == visible ? confirmations + 1 : 0;
            if (confirmations >= 3)
            {
                // Keep recovery tracked when the owning UI thread does not respond.
                var responsive = await Task.Run(() => Native.SendMessageTimeout(identity.Handle, 0, 0, 0, 3, 200, out _) != 0);
                return responsive && HasMark(identity, token) && Native.IsWindowVisible(identity.Handle) == visible;
            }
        }
        return false;
    }

    private WindowIdentity? ReadIdentity(nint hwnd)
    {
        if (!Native.IsWindow(hwnd)) return null;
        var thread = Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (thread == 0 || pid == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return new(hwnd, (int)pid, process.StartTime.ToUniversalTime().Ticks, thread, Text(hwnd, true),
                Native.GetProp(hwnd, generationProperty).ToInt64());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException)
        { return null; }
    }
    public void Dispose()
    {
        foreach (var id in observed.Values)
            if (Matches(id)) Native.RemoveProp(id.Handle, generationProperty);
        observed.Clear();
    }
    private static string Text(nint hwnd, bool className)
    {
        var value = new StringBuilder(className ? 256 : 2048);
        if (className) Native.GetClassName(hwnd, value, value.Capacity);
        else Native.GetWindowText(hwnd, value, value.Capacity);
        return value.ToString();
    }
}
