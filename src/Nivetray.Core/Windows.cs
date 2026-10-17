namespace Nivetray.Core;

public sealed record WindowIdentity(nint Handle, int ProcessId, long ProcessStartedUtcTicks,
    uint ThreadId, string ClassName, long InstanceToken = 0);

public sealed record AppWindow(WindowIdentity Identity, string Title, string ExecutablePath,
    bool IsVisible, bool IsOwned, bool IsToolWindow, bool IsCloaked, bool IsSystem, bool IsSelf)
{
    public string Name => Path.GetFileNameWithoutExtension(ExecutablePath);
}

public enum RuleMode { Manage, Include, Exclude }
public sealed record ApplicationRule(string ExecutablePath, RuleMode Mode);

public sealed class Preferences
{
    public bool CloseToTray { get; set; } = true;
    public List<ApplicationRule> Rules { get; set; } = [];
}

public static class WindowFilter
{
    public static bool Accept(AppWindow window, IReadOnlyList<ApplicationRule> rules)
    {
        // Safety exclusions cannot be overridden. Include only relaxes auxiliary-window filtering.
        if (!window.IsVisible || window.IsCloaked || window.IsSystem || window.IsSelf ||
            string.IsNullOrWhiteSpace(window.Title) || string.IsNullOrWhiteSpace(window.ExecutablePath))
            return false;
        var rule = rules.LastOrDefault(r => string.Equals(r.ExecutablePath, window.ExecutablePath,
            StringComparison.OrdinalIgnoreCase));
        if (rule?.Mode == RuleMode.Exclude) return false;
        return rule?.Mode == RuleMode.Include || (!window.IsOwned && !window.IsToolWindow);
    }
}

public sealed record HiddenWindow(AppWindow Window, nint Token, int OriginalShowCommand);
public sealed record WindowResult(bool Success, string Message)
{
    public static WindowResult Ok(string message) => new(true, message);
    public static WindowResult Fail(string message) => new(false, message);
}

public interface IWindowPlatform
{
    IReadOnlyList<AppWindow> Enumerate();
    bool Matches(WindowIdentity identity);
    bool TryMark(WindowIdentity identity, nint token);
    bool HasMark(WindowIdentity identity, nint token);
    void RemoveMark(WindowIdentity identity, nint token);
    int GetShowCommand(WindowIdentity identity);
    bool IsVisible(WindowIdentity identity);
    Task<bool> SetVisibleAsync(WindowIdentity identity, nint token, bool visible, int showCommand);
}

public sealed class WindowManager(IWindowPlatform platform, Action<string> log)
{
    private readonly Dictionary<nint, HiddenWindow> hidden = [];
    private readonly HashSet<nint> pending = [];
    private nint nextToken;
    public IReadOnlyList<HiddenWindow> Hidden => hidden.Values.ToArray();

    public async Task<WindowResult> HideAsync(AppWindow window)
    {
        var id = window.Identity;
        if (!pending.Add(id.Handle)) return WindowResult.Fail("This window already has an operation pending.");
        try
        {
            Prune();
            if (hidden.ContainsKey(id.Handle)) return WindowResult.Fail("This window is already hidden.");
            if (!platform.Matches(id) || !platform.IsVisible(id))
                return Failure("The selected window has closed or changed. Refresh the list.");
            var token = checked(++nextToken);
            if (!platform.TryMark(id, token))
                return Failure("Windows denied access to this window. Elevated or protected apps may be incompatible.");
            var entry = new HiddenWindow(window, token, platform.GetShowCommand(id));
            // Track before requesting hide so shutdown can recover a partially completed operation.
            hidden.Add(id.Handle, entry);
            if (await platform.SetVisibleAsync(id, token, false, entry.OriginalShowCommand))
                return WindowResult.Ok("Window hidden. Its process keeps running.");
            // An asynchronous request may complete late. Keep the recovery entry until restored.
            return Failure("The window did not confirm hiding. It remains in the recovery list; try Restore.");
        }
        finally { pending.Remove(id.Handle); }
    }

    public async Task<WindowResult> RestoreAsync(HiddenWindow entry)
    {
        var id = entry.Window.Identity;
        if (!pending.Add(id.Handle)) return WindowResult.Fail("This window already has an operation pending.");
        try
        {
            if (!hidden.TryGetValue(id.Handle, out var current) || current != entry)
                return WindowResult.Fail("This entry is no longer managed.");
            if (!platform.HasMark(id, entry.Token))
            {
                hidden.Remove(id.Handle);
                return Failure("The original window no longer exists. No other window was changed.");
            }
            if (!await platform.SetVisibleAsync(id, entry.Token, true, entry.OriginalShowCommand))
                return Failure("Windows did not confirm restoring this window. Retry before exiting.");
            platform.RemoveMark(id, entry.Token);
            hidden.Remove(id.Handle);
            return WindowResult.Ok("Window restored.");
        }
        finally { pending.Remove(id.Handle); }
    }

    public async Task<bool> RestoreAllAsync()
    {
        foreach (var entry in Hidden) await RestoreAsync(entry);
        Prune();
        return hidden.Count == 0 && pending.Count == 0;
    }

    public void Prune()
    {
        foreach (var entry in Hidden)
            if (!pending.Contains(entry.Window.Identity.Handle) &&
                !platform.HasMark(entry.Window.Identity, entry.Token))
                hidden.Remove(entry.Window.Identity.Handle);
    }

    private WindowResult Failure(string message) { log(message); return WindowResult.Fail(message); }
}
