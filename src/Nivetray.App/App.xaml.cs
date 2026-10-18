using System.Threading;
using System.Windows;

namespace Nivetray.App;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        instance = new Mutex(true, @"Local\Nivetray." + user, out var first);
        if (!first)
        {
            MessageBox.Show("Nivetray is already running. Open it from the system tray.", "Nivetray");
            Shutdown();
            return;
        }
        string? directory = null;
        if (e.Args.Length == 2 && e.Args[0] == "--data-directory")
            directory = Path.GetFullPath(e.Args[1]);
        var window = new MainWindow(directory);
        MainWindow = window;
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
