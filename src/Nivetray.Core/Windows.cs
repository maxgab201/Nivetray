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

public enum IdentityState { Original, Destroyed, Replaced, Unavailable }
public enum RecoveryState { Hiding, Hidden, NeedsAttention, Restoring }
public sealed record HiddenWindow(AppWindow Window, nint Token, int OriginalShowCommand)
{
    public RecoveryState State { get; internal set; } = RecoveryState.Hiding;
    public string? Error { get; internal set; }
}
public sealed record WindowResult(bool Success, string Message)
{
    public static WindowResult Ok(string message) => new(true, message);
    public static WindowResult Fail(string message) => new(false, message);
}

public interface IWindowPlatform
{
    IReadOnlyList<AppWindow> Enumerate();
    bool Matches(WindowIdentity identity);
    IdentityState Inspect(WindowIdentity identity);
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
    // All mutations run on the caller's UI context. A gate also queues restore/exit
    // behind an in-flight hide instead of losing recovery requests.
    private readonly SemaphoreSlim operations = new(1, 1);
    private bool inFlight;
    private nint nextToken;
    public IReadOnlyList<HiddenWindow> Hidden => hidden.Values.ToArray();

    public async Task<WindowResult> HideAsync(AppWindow window)
    {
        var id = window.Identity;
        await operations.WaitAsync();
        try
        {
            Prune();
            inFlight = true;
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
            {
                entry.State = RecoveryState.Hidden;
                return WindowResult.Ok("Window hidden. Its process keeps running.");
            }
            // An asynchronous request may complete late. Keep the recovery entry until restored.
            return Attention(entry, "Hiding was not confirmed. Keep Nivetray running and choose Restore to recover this window.");
        }
        finally { inFlight = false; operations.Release(); }
    }

    public async Task<WindowResult> RestoreAsync(HiddenWindow entry)
    {
        await operations.WaitAsync();
        try
        {
            inFlight = true;
            return await RestoreCoreAsync(entry);
        }
        finally { inFlight = false; operations.Release(); }
    }

    private async Task<WindowResult> RestoreCoreAsync(HiddenWindow entry)
    {
        var id = entry.Window.Identity;
        if (!hidden.TryGetValue(id.Handle, out var current) || current.Token != entry.Token || current.Window.Identity != id)
                return WindowResult.Fail("This entry is no longer managed.");
        try
        {
            var identity = platform.Inspect(id);
            if (identity is IdentityState.Destroyed or IdentityState.Replaced)
            {
                hidden.Remove(id.Handle);
                return WindowResult.Fail("The original window has closed. No other window was changed.");
            }
            if (identity != IdentityState.Original)
                return Attention(current, "Window identity cannot be verified. Keep Nivetray running and retry. Use the application's own Show command if available; this entry stays protected.");
            // Authorization is not identity. Reacquire it ONLY after independently
            // confirming the discovery lifetime stamp, PID, start time, thread and class.
            if (!platform.HasMark(id, current.Token) && !platform.TryMark(id, current.Token))
                return Attention(current, "Access to the original window was denied. Keep Nivetray running, close any permission prompt in that app, then retry Restore.");
            current.State = RecoveryState.Restoring;
            if (!await platform.SetVisibleAsync(id, entry.Token, true, entry.OriginalShowCommand))
                return Attention(current, "Restore was not confirmed. The app may be busy or unresponsive. Keep Nivetray running and retry Restore.");
            platform.RemoveMark(id, entry.Token);
            hidden.Remove(id.Handle);
            return WindowResult.Ok("Window restored.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log("Recovery platform error: " + ex.GetType().Name);
            return Attention(current, "Windows could not complete recovery. This window remains tracked. Retry Restore or keep Nivetray in the tray.");
        }
    }

    public async Task<bool> RestoreAllAsync()
    {
        await operations.WaitAsync();
        try
        {
            inFlight = true;
            foreach (var entry in Hidden) await RestoreCoreAsync(entry);
            return hidden.Count == 0;
        }
        finally { inFlight = false; operations.Release(); }
    }

    public void Prune()
    {
        if (inFlight) return;
        foreach (var entry in Hidden)
        {
            var identity = platform.Inspect(entry.Window.Identity);
            if (identity is IdentityState.Destroyed or IdentityState.Replaced)
                hidden.Remove(entry.Window.Identity.Handle);
            else if (identity == IdentityState.Unavailable || !platform.HasMark(entry.Window.Identity, entry.Token))
            {
                entry.State = RecoveryState.NeedsAttention;
                entry.Error = "Recovery needs attention. Retry Restore; do not force-stop Nivetray.";
            }
        }
    }

    private WindowResult Attention(HiddenWindow entry, string message)
    { entry.State = RecoveryState.NeedsAttention; entry.Error = message; return Failure(message); }

    private WindowResult Failure(string message) { log(message); return WindowResult.Fail(message); }
}
