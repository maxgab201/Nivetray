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

namespace Nivetray.Tests;

internal static partial class Program
{
    private static void RunConsoleFixture(string tag, string stopName, string info)
    {
        using var stop = EventWaitHandle.OpenExisting(stopName);
        NativeFixture.FreeConsole(); // Detach only this test child before allocating its isolated host.
        if (!NativeFixture.AllocConsole()) { File.WriteAllText(info, "unavailable"); return; }
        NativeFixture.SetConsoleTitle(tag);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
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
        // The child inherits ONLY this fixture's newly allocated console and exits on our event.
        var command = $"[System.Threading.EventWaitHandle]::OpenExisting('{stopName}').WaitOne(60000) | Out-Null";
        using var shell = Process.Start(new ProcessStartInfo("powershell.exe")
        { UseShellExecute = false, ArgumentList = { "-NoLogo", "-NoProfile", "-Command", command } });
        File.WriteAllText(info, $"{NativeFixture.GetConsoleWindow().ToInt64()}|{((IPEndPoint)listener.LocalEndpoint).Port}|{shell?.Id ?? 0}");
        stop.WaitOne(60000);
        shell?.WaitForExit(5000);
    }

    private static async Task RunConsoleTest()
    {
        var tag = "Nivetray-Console-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), tag);
        Directory.CreateDirectory(directory);
        var info = Path.Combine(directory, "fixture.txt");
        var stopName = @"Local\" + tag;
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, stopName);
        using var fixture = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--console-fixture", tag, stopName, info } })
            ?? throw new InvalidOperationException("Console fixture did not start.");
        var reported = new HashSet<string>();
        using var platform = new Win32WindowPlatform(message => { if (reported.Add(message)) Console.WriteLine(message); });
        var manager = new WindowManager(platform, Console.WriteLine);
        try
        {
            for (var i = 0; i < 60 && !File.Exists(info); i++) await Task.Delay(100);
            var details = File.ReadAllText(info);
            if (details == "unavailable") { Console.WriteLine("SKIP: this desktop cannot allocate a dedicated console."); return; }
            var fields = details.Split('|');
            var handle = (nint)long.Parse(fields[0]);
            AppWindow? window = null;
            for (var i = 0; i < 30; i++)
            {
                window = platform.Enumerate().FirstOrDefault(w => w.Identity.Handle == handle && w.Identity.ClassName == "ConsoleWindowClass");
                if (window is not null) break;
                await Task.Delay(100);
            }
            if (window is null)
            {
                Console.WriteLine("SKIP: allocated console exposes no visible classic ConsoleWindowClass (possible pseudoconsole delegation).");
                return;
            }
            Check(WindowFilter.Accept(window, []), "dedicated console host is detected without a cmd/powershell process-name allowlist");
            Check((await manager.HideAsync(window)).Success && !platform.IsVisible(window.Identity), "dedicated console hides successfully");
            using var child = Process.GetProcessById(int.Parse(fields[2]));
            Check(!fixture.HasExited && !child.HasExited, "Windows PowerShell child stays alive in the hidden dedicated console");
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, int.Parse(fields[1]));
            var data = new byte[5];
            var count = await client.GetStream().ReadAsync(data).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Check(count > 0 && data[0] == (byte)'a', "console fixture TCP server responds while its host is hidden");
            Check(await manager.RestoreAllAsync() && platform.IsVisible(window.Identity), "dedicated console restores successfully");
        }
        finally
        {
            await manager.RestoreAllAsync();
            stop.Set();
            await fixture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (File.Exists(info)) File.Delete(info);
            Directory.Delete(directory);
        }
    }
}
