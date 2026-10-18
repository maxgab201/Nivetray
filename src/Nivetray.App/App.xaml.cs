using System.Threading;
using System.Windows;

namespace Nivetray.App;

public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? openRequest;
    private RegisteredWaitHandle? openWait;
    protected override void OnStartup(StartupEventArgs e)
    {
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        base.OnStartup(e);
        // Static utility UI: measured lower private memory than persistent GPU-backed surfaces.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var user = identity.User?.Value ?? Environment.UserName;
        instance = new Mutex(true, @"Local\Nivetray." + user, out var first);
        if (!first)
        {
            try { using var request = EventWaitHandle.OpenExisting(@"Local\Nivetray.Open." + user); request.Set(); }
            catch (WaitHandleCannotBeOpenedException)
            { MessageBox.Show("Nivetray is starting. Open it from the system tray in a moment.", "Nivetray"); }
            Shutdown();
            return;
        }
        string? directory = null;
        if (e.Args.Length == 2 && e.Args[0] == "--data-directory")
            directory = Path.GetFullPath(e.Args[1]);
        var window = new MainWindow(directory);
        MainWindow = window;
        openRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Nivetray.Open." + user);
        openWait = ThreadPool.RegisterWaitForSingleObject(openRequest, (_, _) => Dispatcher.BeginInvoke(() =>
        { window.Show(); window.WindowState = WindowState.Normal; window.Activate(); }), null, Timeout.Infinite, false);
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        openWait?.Unregister(null);
        openRequest?.Dispose();
        base.OnExit(e);
    }
}
