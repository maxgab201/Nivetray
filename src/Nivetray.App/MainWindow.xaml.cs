using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nivetray.Core;
using Nivetray.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Nivetray.App;

public partial class MainWindow : Window
{
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nivetray");
    private readonly PreferencesStore store;
    private readonly Preferences preferences;
    private readonly Win32WindowPlatform platform;
    private readonly WindowManager manager;
    private readonly Forms.NotifyIcon tray;
    private readonly Drawing.Icon trayIcon;
    private readonly Dictionary<string, ImageSource?> icons = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<AppWindow> available = [];
    private bool initialized, exiting, busy;

    public MainWindow()
    {
        InitializeComponent();
        store = new(directory, Log);
        preferences = store.Load();
        platform = new(Log);
        manager = new(platform, Log);
        CloseToTray.IsChecked = preferences.CloseToTray;
        RuleModeBox.ItemsSource = Enum.GetValues<RuleMode>();
        RuleModeBox.SelectedItem = RuleMode.Manage;
        SettingsLocation.Text = "Settings: " + store.FilePath;
        using (var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Nivetray.ico")).Stream)
            trayIcon = new Drawing.Icon(resource);
        tray = new Forms.NotifyIcon { Text = "Nivetray — Keep it running", Icon = trayIcon, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenPanel);
        initialized = true;
        RefreshWindows();
        Activated += (_, _) => { if (initialized && !busy) RefreshWindows(); };
    }

    private void RefreshWindows()
    {
        manager.Prune();
        available = platform.Enumerate().Where(w => WindowFilter.Accept(w, preferences.Rules))
            .OrderByDescending(IsRemembered).ThenBy(w => w.Name).ThenBy(w => w.Title).ToArray();
        UpdateLists();
        BuildTrayMenu();
        RulesList.ItemsSource = preferences.Rules.Select(r => new RuleRow(r)).ToArray();
    }
    private bool IsRemembered(AppWindow w) => preferences.Rules.Any(r => r.Mode != RuleMode.Exclude &&
        string.Equals(r.ExecutablePath, w.ExecutablePath, StringComparison.OrdinalIgnoreCase));
    private bool SearchMatches(AppWindow w) => string.Join(" ", w.Title, w.Name, w.ExecutablePath)
        .Contains(Search.Text.Trim(), StringComparison.OrdinalIgnoreCase);
    private void UpdateLists()
    {
        if (!initialized) return;
        AvailableList.ItemsSource = available.Where(SearchMatches)
            .Select(w => new WindowRow(w, GetIcon(w.ExecutablePath), w.Name + (IsRemembered(w) ? "  ·  Remembered application" : ""))).ToArray();
        HiddenList.ItemsSource = manager.Hidden.Where(e => SearchMatches(e.Window)).ToArray();
        AvailableCount.Text = available.Count == 0 ? "No compatible windows. Open an app, then Refresh." : $"{available.Count} compatible windows";
    }
    private ImageSource? GetIcon(string path)
    {
        if (icons.TryGetValue(path, out var cached)) return cached;
        ImageSource? source = null;
        try
        {
            using var icon = Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is not null)
            {
                source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                source.Freeze();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { Log("Application icon unavailable: " + ex.GetType().Name); }
        if (icons.Count >= 128) icons.Clear();
        icons[path] = source;
        return source;
    }
    private void BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        Add("Open Nivetray / Search windows…", () => { OpenPanel(); Search.Focus(); });
        Add("Search hidden windows…", () => { OpenPanel(); Tabs.SelectedIndex = 1; Search.Focus(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var entry in manager.Hidden)
        {
            var captured = entry;
            var title = entry.Window.Title.Replace("&", "&&");
            Add("Restore: " + (title.Length > 80 ? title[..77] + "…" : title),
                () => RunOperation(() => manager.RestoreAsync(captured)));
        }
        if (manager.Hidden.Count == 0) menu.Items.Add(new Forms.ToolStripMenuItem("No hidden windows") { Enabled = false });
        Add("Restore all", () => RunOperation(RestoreEverything));
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add("Settings", () => { OpenPanel(); Tabs.SelectedIndex = 2; });
        Add("Restore all and exit", () => ExitApplication());
        var previous = tray.ContextMenuStrip;
        tray.ContextMenuStrip = menu;
        previous?.Dispose();
        tray.Text = $"Nivetray — {manager.Hidden.Count} hidden";
        void Add(string label, Action action) => menu.Items.Add(label, null, (_, _) => Dispatcher.BeginInvoke(action));
    }
    private void OpenPanel() { Show(); WindowState = WindowState.Normal; Activate(); RefreshWindows(); }
    private void SearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateLists();
    private void RefreshClicked(object sender, RoutedEventArgs e) => RefreshWindows();
    private void SendToTray(object sender, RoutedEventArgs e) { Hide(); tray.ShowBalloonTip(2000, "Nivetray is running", "Use its tray icon to restore windows or open this panel.", Forms.ToolTipIcon.Info); }
    private void HideSelected(object sender, RoutedEventArgs e)
    {
        if (AvailableList.SelectedItem is WindowRow row) RunOperation(() => manager.HideAsync(row.Window));
        else Status.Text = "Select an available window first.";
    }
    private void RestoreSelected(object sender, RoutedEventArgs e)
    {
        if (HiddenList.SelectedItem is HiddenWindow entry) RunOperation(() => manager.RestoreAsync(entry));
        else Status.Text = "Select a hidden window first.";
    }
    private void RestoreAll(object sender, RoutedEventArgs e) => RunOperation(RestoreEverything);
    private async Task<WindowResult> RestoreEverything() => await manager.RestoreAllAsync()
        ? WindowResult.Ok("All windows restored.") : WindowResult.Fail("Some windows could not be restored. Retry before exiting.");
    private async void RunOperation(Func<Task<WindowResult>> operation)
    {
        if (busy) return;
        busy = true;
        HideButton.IsEnabled = false;
        try { Status.Text = (await operation()).Message; }
        catch (Exception ex) { Log("Window operation failed: " + ex.GetType().Name); Status.Text = "The operation failed. Restore tracked windows before exiting."; }
        finally { busy = false; HideButton.IsEnabled = true; RefreshWindows(); }
    }
    private void RememberSelected(object sender, RoutedEventArgs e)
    {
        if (AvailableList.SelectedItem is not WindowRow row) { Status.Text = "Select an available window first."; return; }
        RulePath.Text = row.Window.ExecutablePath;
        RuleModeBox.SelectedItem = RuleMode.Manage;
        PutRule(row.Window.ExecutablePath, RuleMode.Manage);
    }
    private void SaveRule(object sender, RoutedEventArgs e)
    {
        var path = RulePath.Text.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        { Status.Text = "Enter the full path to an .exe file."; return; }
        PutRule(path, (RuleMode)(RuleModeBox.SelectedItem ?? RuleMode.Manage));
    }
    private void PutRule(string path, RuleMode mode)
    {
        preferences.Rules.RemoveAll(r => string.Equals(r.ExecutablePath, path, StringComparison.OrdinalIgnoreCase));
        preferences.Rules.Add(new(path, mode));
        SavePreferences();
        RefreshWindows();
    }
    private void RemoveRule(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not RuleRow row) return;
        preferences.Rules.Remove(row.Rule); SavePreferences(); RefreshWindows();
    }
    private void ClosePreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        preferences.CloseToTray = CloseToTray.IsChecked == true;
        SavePreferences();
    }
    private void SavePreferences()
    {
        try { store.Save(preferences); Status.Text = "Preferences saved locally."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log("Settings save failed: " + ex.GetType().Name); Status.Text = "Settings could not be saved. Check access to the local settings folder."; }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        if (preferences.CloseToTray) Hide();
        else ExitApplication();
    }
    private void ExitClicked(object sender, RoutedEventArgs e) => ExitApplication();
    private async void ExitApplication()
    {
        if (busy || exiting) return;
        busy = true;
        try
        {
            if (!await manager.RestoreAllAsync())
            {
                OpenPanel();
                Status.Text = "Exit cancelled: some windows could not be restored. Retry Restore all.";
                return;
            }
            exiting = true;
            tray.Visible = false;
            tray.ContextMenuStrip?.Dispose();
            tray.Dispose();
            trayIcon.Dispose();
            platform.Dispose();
            Application.Current.Shutdown();
        }
        catch (Exception ex) { Log("Exit recovery failed: " + ex.GetType().Name); Show(); Status.Text = "Exit cancelled. Retry restoring your windows."; }
        finally { busy = false; }
    }
    private void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "diagnostics.log");
            if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024) File.Move(file, file + ".previous", true);
            File.AppendAllText(file, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Logging must never break window recovery. */ }
    }
    private sealed record WindowRow(AppWindow Window, ImageSource? Icon, string Subtitle);
    private sealed record RuleRow(ApplicationRule Rule) { public string Display => $"{Rule.Mode}  ·  {Rule.ExecutablePath}"; }
}
