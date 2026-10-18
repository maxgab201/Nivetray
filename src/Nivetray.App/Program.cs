using System.Runtime.CompilerServices;
using Nivetray.Windows;

namespace Nivetray.App;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        // WPF caches process DPI awareness during initialization. Setting only the
        // UI thread's context later creates a PM-aware HWND with incorrect layout.
        WindowAppearance.ConfigureProcessDpi();
        return RunWpf();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunWpf()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
