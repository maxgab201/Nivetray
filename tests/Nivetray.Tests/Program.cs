using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Nivetray.Core;
using Nivetray.Windows;
using Forms = System.Windows.Forms;

namespace Nivetray.Tests;

internal static class Program
{
    private static int passed;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--fixture") { RunFixture(args[1], int.Parse(args[2])); return 0; }
        try
        {
            RunTests().GetAwaiter().GetResult();
            if (args.Contains("--native")) RunNativeTest().GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {passed} assertions. Native integration: {args.Contains("--native")}.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static AppWindow Window(nint hwnd = 101, int pid = 42, long start = 1234, long generation = 11) =>
        new(new(hwnd, pid, start, 7, "FixtureWindow", generation), "Fixture", @"C:\Apps\fixture.exe", true, false, false, false, false, false);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        passed++; Console.WriteLine("PASS: " + message);
    }
    private static async Task RunTests()
    {
        var w = Window();
        Check(WindowFilter.Accept(w, []), "ordinary visible app accepted");
        Check(!WindowFilter.Accept(w with { IsVisible = false }, []), "invisible window excluded");
        Check(!WindowFilter.Accept(w with { IsCloaked = true }, []), "cloaked window excluded");
        Check(!WindowFilter.Accept(w with { IsSystem = true }, [new(w.ExecutablePath, RuleMode.Include)]), "system exclusion cannot be bypassed");
        Check(!WindowFilter.Accept(w with { IsSelf = true }, []), "own windows excluded");
        Check(!WindowFilter.Accept(w with { Title = " " }, []), "untitled window excluded");
        Check(!WindowFilter.Accept(w with { IsOwned = true }, []), "owned helper excluded");
        Check(!WindowFilter.Accept(w with { IsToolWindow = true }, []), "tool window excluded");
        Check(WindowFilter.Accept(w with { IsToolWindow = true }, [new(w.ExecutablePath.ToUpperInvariant(), RuleMode.Include)]), "explicit include exception, case insensitive");
        Check(!WindowFilter.Accept(w, [new(w.ExecutablePath, RuleMode.Exclude)]), "executable exclusion");

        var fake = new FakePlatform(w, Window(102));
        var manager = new WindowManager(fake, _ => { });
        Check((await manager.HideAsync(w)).Success, "hide succeeds");
        Check(!fake.IsVisible(w.Identity) && fake.IsVisible(Window(102).Identity), "hiding one window leaves its sibling visible");
        Check(!(await manager.HideAsync(w)).Success && manager.Hidden.Count == 1, "duplicate hide rejected");
        Check((await manager.RestoreAsync(manager.Hidden.Single())).Success, "restore succeeds");
        Check(fake.IsVisible(w.Identity) && manager.Hidden.Count == 0, "restored entry removed");
        Check(fake.Commands.Last() == 3, "original maximized state passed to restore");
        await manager.HideAsync(w);
        var previousEntry = manager.Hidden.Single();
        await manager.RestoreAsync(previousEntry);
        await manager.HideAsync(w);
        var oldCalls = fake.Commands.Count;
        Check(!(await manager.RestoreAsync(previousEntry)).Success && fake.Commands.Count == oldCalls,
            "stale recovery entry cannot restore a later hide of the same window");
        await manager.RestoreAllAsync();

        foreach (var replacement in new[] { Window(pid: 43), Window(start: 9999), Window(generation: 12) })
        {
            fake = new(w);
            manager = new(fake, _ => { });
            await manager.HideAsync(w);
            var old = manager.Hidden.Single();
            fake.Replace(replacement);
            var calls = fake.Commands.Count;
            Check(!(await manager.RestoreAsync(old)).Success && fake.Commands.Count == calls,
                "recycled HWND rejected: " + replacement.Identity);
        }
        fake = new(w);
        manager = new(fake, _ => { });
        fake.Replace(Window(generation: 22));
        Check(!(await manager.HideAsync(w)).Success && fake.Commands.Count == 0, "stale list selection rejected before hide");

        fake = new(w) { DenyMark = true };
        manager = new(fake, _ => { });
        Check(!(await manager.HideAsync(w)).Success && manager.Hidden.Count == 0 && fake.Commands.Count == 0, "access denial leaves window untouched");
        fake = new(w) { FailOperation = true };
        manager = new(fake, _ => { });
        Check(!(await manager.HideAsync(w)).Success && manager.Hidden.Count == 1, "unconfirmed hide stays recoverable");
        Check(!await manager.RestoreAllAsync() && manager.Hidden.Count == 1, "failed restoration retained for retry");
        fake.FailOperation = false;
        Check(await manager.RestoreAllAsync() && fake.IsVisible(w.Identity), "retry restores pending window");

        fake = new(w, Window(102));
        manager = new(fake, _ => { });
        await manager.HideAsync(w); await manager.HideAsync(Window(102));
        Check(await manager.RestoreAllAsync() && manager.Hidden.Count == 0, "shutdown restores all windows");
        await RecoveryRegressions(w);

        var directory = Path.Combine(Path.GetTempPath(), "Nivetray.Tests." + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PreferencesStore(directory, _ => { });
            var prefs = new Preferences { CloseToTray = false, Rules = [new(w.ExecutablePath, RuleMode.Include)] };
            store.Save(prefs);
            var loaded = store.Load();
            Check(!loaded.CloseToTray && loaded.Rules.Single() == prefs.Rules.Single(), "JSON roundtrip");
            File.WriteAllText(store.FilePath, "{broken");
            Check(store.Load().CloseToTray && File.ReadAllText(store.FilePath) == "{broken", "malformed JSON falls back and is preserved");
            File.WriteAllText(store.FilePath, "{\"Rules\":null}");
            Check(store.Load().Rules.Count == 0, "null rules handled safely");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task RecoveryRegressions(AppWindow w)
    {
        var fake = new FakePlatform(w);
        var manager = new WindowManager(fake, _ => { });
        await manager.HideAsync(w);
        fake.DropMark(w.Identity.Handle);
        manager.Prune();
        Check(manager.Hidden.Count == 1 && manager.Hidden[0].State == RecoveryState.NeedsAttention,
            "lost authorization never prunes a live hidden window");
        Check(await manager.RestoreAllAsync() && fake.IsVisible(w.Identity),
            "verified original lifetime can reacquire authorization and recover");

        fake = new(w); manager = new(fake, _ => { });
        await manager.HideAsync(w); fake.DropMark(w.Identity.Handle); fake.DenyMark = true;
        Check(!await manager.RestoreAllAsync() && manager.Hidden.Count == 1,
            "denied reauthorization retains recovery and blocks unsafe exit");
        Check(manager.Hidden[0].Error is not null, "recovery failure explains a practical retry action");
        fake.DenyMark = false;
        Check(await manager.RestoreAllAsync(), "permission recovery retries successfully");

        fake = new(w); manager = new(fake, _ => { });
        await manager.HideAsync(w); fake.IdentityOverride = IdentityState.Unavailable;
        var requests = fake.Commands.Count;
        manager.Prune();
        Check(!await manager.RestoreAllAsync() && manager.Hidden.Count == 1 && fake.Commands.Count == requests,
            "unknown identity neither changes a window nor drops recovery");
        fake.IdentityOverride = IdentityState.Original;
        Check(await manager.RestoreAllAsync(), "temporarily inaccessible identity can be retried");

        foreach (var state in new[] { IdentityState.Destroyed, IdentityState.Replaced })
        {
            fake = new(w); manager = new(fake, _ => { });
            await manager.HideAsync(w); fake.IdentityOverride = state; requests = fake.Commands.Count;
            manager.Prune();
            Check(await manager.RestoreAllAsync() && fake.Commands.Count == requests && manager.Hidden.Count == 0,
                "confirmed " + state + " removes only the obsolete record");
        }

        fake = new(w) { OperationGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        manager = new(fake, _ => { });
        var hide = manager.HideAsync(w);
        var restore = manager.RestoreAllAsync();
        manager.Prune();
        Check(!hide.IsCompleted && !restore.IsCompleted && manager.Hidden.Count == 1,
            "exit recovery queues behind an asynchronous hide and prune cannot discard it");
        fake.OperationGate.SetResult();
        Check((await hide).Success && await restore && manager.Hidden.Count == 0 && fake.IsVisible(w.Identity),
            "queued restore executes after pending hide");

        fake = new(w); manager = new(fake, _ => { });
        await manager.HideAsync(w); fake.ThrowOnRestore = true;
        Check(!await manager.RestoreAllAsync() && manager.Hidden.Count == 1,
            "native exception retains the recovery entry");
        fake.ThrowOnRestore = false;
        Check(await manager.RestoreAllAsync(), "native failure can be retried");
        for (var cycle = 0; cycle < 10; cycle++)
        {
            await manager.HideAsync(w);
            Check(await manager.RestoreAllAsync() && manager.Hidden.Count == 0,
                "repeat hide/restore cycle " + cycle);
        }
    }

    private static async Task RunNativeTest()
    {
        // Only interact with windows owned by our explicitly launched fixture process.
        var tag = "Nivetray-Test-" + Guid.NewGuid().ToString("N");
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--fixture {tag} {port}")
            { UseShellExecute = false, CreateNoWindow = true }) ?? throw new InvalidOperationException("Cannot start fixture.");
        using var platform = new Win32WindowPlatform(Console.WriteLine);
        var manager = new WindowManager(platform, Console.WriteLine);
        try
        {
            AppWindow[] windows = [];
            for (var i = 0; i < 60; i++)
            {
                windows = platform.Enumerate().Where(w => w.Identity.ProcessId == process.Id && w.Title.StartsWith(tag)).ToArray();
                if (windows.Length == 2) break;
                await Task.Delay(100);
            }
            Check(windows.Length == 2, "native EnumWindows detects two fixture windows");
            Check((await manager.HideAsync(windows[0])).Success, "native hide confirmed");
            Check(!platform.IsVisible(windows[0].Identity) && platform.IsVisible(windows[1].Identity), "native sibling remains visible");
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                var data = new byte[5];
                var count = await client.GetStream().ReadAsync(data).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                Check(count > 0 && data[0] == (byte)'a', "TCP server continues responding while window is hidden");
            }
            Check(!process.HasExited, "fixture process remains alive while hidden");
            Check(await manager.RestoreAllAsync() && platform.IsVisible(windows[0].Identity), "native restore-all confirms visibility");
            // Native window destruction invalidates its lifetime stamp.
            var old = windows[0];
            process.CloseMainWindow();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Check(!platform.Matches(old.Identity), "closed native window identity invalidated");
        }
        finally
        {
            await manager.RestoreAllAsync();
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                // The fixture also has a 30 second self-exit timer; no process termination API is used.
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35));
            }
        }
    }
    private static void RunFixture(string tag, int port)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    await client.GetStream().WriteAsync("alive"u8.ToArray());
                }
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
        });
        using var first = new Forms.Form { Text = tag + " A", Width = 320, Height = 180, ShowInTaskbar = true };
        using var second = new Forms.Form { Text = tag + " B", Width = 320, Height = 180, ShowInTaskbar = true };
        using var timer = new Forms.Timer { Interval = 30000 };
        timer.Tick += (_, _) => Forms.Application.Exit();
        timer.Start();
        first.Shown += (_, _) => second.Show();
        second.FormClosed += (_, _) => first.Close();
        Forms.Application.Run(first);
    }

    private sealed class FakePlatform(params AppWindow[] windows) : IWindowPlatform
    {
        private readonly Dictionary<nint, AppWindow> current = windows.ToDictionary(w => w.Identity.Handle);
        private readonly Dictionary<nint, nint> marks = [];
        public List<int> Commands { get; } = [];
        public bool DenyMark { get; set; }
        public IdentityState? IdentityOverride { get; set; }
        public TaskCompletionSource? OperationGate { get; set; }
        public bool ThrowOnRestore { get; set; }
        public bool FailOperation { get; set; }
        public IReadOnlyList<AppWindow> Enumerate() => current.Values.ToArray();
        public void Replace(AppWindow w) { current[w.Identity.Handle] = w; marks.Remove(w.Identity.Handle); }
        public bool Matches(WindowIdentity id) => current.TryGetValue(id.Handle, out var w) && w.Identity == id;
        public IdentityState Inspect(WindowIdentity id) => IdentityOverride ??
            (!current.ContainsKey(id.Handle) ? IdentityState.Destroyed : Matches(id) ? IdentityState.Original : IdentityState.Replaced);
        public void DropMark(nint hwnd) => marks.Remove(hwnd);
        public bool TryMark(WindowIdentity id, nint token) { if (DenyMark || !Matches(id)) return false; marks[id.Handle] = token; return true; }
        public bool HasMark(WindowIdentity id, nint token) => Matches(id) && marks.GetValueOrDefault(id.Handle) == token;
        public void RemoveMark(WindowIdentity id, nint token) { if (HasMark(id, token)) marks.Remove(id.Handle); }
        public int GetShowCommand(WindowIdentity id) => 3;
        public bool IsVisible(WindowIdentity id) => Matches(id) && current[id.Handle].IsVisible;
        public async Task<bool> SetVisibleAsync(WindowIdentity id, nint token, bool visible, int showCommand)
        {
            if (OperationGate is not null) await OperationGate.Task;
            if (visible && ThrowOnRestore) throw new InvalidOperationException("Simulated native failure");
            if (!HasMark(id, token)) return false;
            Commands.Add(visible ? showCommand : 0);
            if (FailOperation) return false;
            current[id.Handle] = current[id.Handle] with { IsVisible = visible };
            return true;
        }
    }
}
