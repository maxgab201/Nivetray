using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nivetray.Core;
using Nivetray.Windows;

namespace Nivetray.App;

public partial class MainWindow : Window
{
    private readonly string directory;
    private readonly DiagnosticsLog diagnostics;
    private readonly PreferencesStore store;
    private readonly Preferences preferences;
    private readonly Win32WindowPlatform platform;
    private readonly WindowManager manager;
    private readonly ExecutableIcons icons;
    private readonly ThemeManager themes;
    private readonly TrayController tray;
    private IReadOnlyList<WindowRow> available = [];
    private Task? discovery;
    private bool initialized, exiting, busy, exitRequested;

    public MainWindow(string? dataDirectory = null)
    {
        directory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nivetray");
        diagnostics = new(directory);
        store = new(directory, diagnostics.Write);
        preferences = store.Load();
        themes = new(preferences.Theme);
        icons = new(diagnostics.Write);
        platform = new(diagnostics.Write);
        manager = new(platform, diagnostics.Write);
        InitializeComponent();
        SourceInitialized += (_, _) => themes.Apply(preferences.Theme);
        CloseToTray.IsChecked = preferences.CloseToTray;
        Notifications.IsChecked = preferences.TrayNotifications;
        Auxiliary.IsChecked = preferences.ShowAuxiliaryWindows;
        ThemeBox.ItemsSource = Enum.GetValues<AppTheme>();
        ThemeBox.SelectedItem = preferences.Theme;
        RuleModeBox.ItemsSource = Enum.GetValues<RuleMode>().Select(m => new RuleChoice(m)).ToArray();
        RuleModeBox.SelectedIndex = 0;
        SettingsLocation.Text = "Local data: " + directory;
        tray = new(manager, OpenPanel, () => { OpenPanel(); Tabs.SelectedIndex = 1; HiddenSearch.Focus(); },
            () => { OpenPanel(); Tabs.SelectedIndex = 3; }, entry => RunOperation(() => manager.RestoreAsync(entry)),
            () => RunOperation(RestoreEverything), ExitApplication, diagnostics.Write);
        initialized = true;
        Loaded += async (_, _) => await RefreshWindows();
        Activated += async (_, _) => { if (initialized && !busy && !exiting) await RefreshWindows(); };
    }

    private Task RefreshWindows()
    {
        if (exiting) return Task.CompletedTask;
        if (discovery is { IsCompleted: false }) return discovery;
        return discovery = LoadWindowsAsync();
    }
    private async Task LoadWindowsAsync()
    {
        Progress.Visibility = Visibility.Visible;
        try
        {
            manager.Prune();
            var rules = preferences.Rules.ToArray();
            var auxiliary = preferences.ShowAuxiliaryWindows;
            available = await Task.Run(() => platform.Enumerate()
                .Where(w => WindowFilter.Accept(w, rules, auxiliary))
                .OrderByDescending(w => rules.Any(r => r.Mode == RuleMode.Manage && r.ExecutablePath.Equals(w.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                .ThenBy(w => w.Name).ThenBy(w => w.Title)
                .Select(w => new WindowRow(w, icons.Get(w.ExecutablePath), null,
                    rules.Any(r => r.Mode == RuleMode.Manage && r.ExecutablePath.Equals(w.ExecutablePath, StringComparison.OrdinalIgnoreCase)))).ToArray());
            UpdateLists();
            tray.Refresh();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { diagnostics.Write("Window discovery failed: " + ex.GetType().Name); Status.Text = "Windows could not be refreshed. Try Refresh again; hidden windows stay tracked."; }
        finally { if (!busy) Progress.Visibility = Visibility.Collapsed; }
    }
    private void UpdateLists()
    {
        if (!initialized) return;
        var rows = available.Where(r => WindowSearch.Matches(r.Window, Search.Text)).ToArray();
        AvailableList.ItemsSource = rows;
        var hidden = manager.Hidden.Where(e => WindowSearch.Matches(e.Window, HiddenSearch.Text))
            .Select(e => new WindowRow(e.Window, icons.Cached(e.Window.ExecutablePath), e, false)).ToArray();
        HiddenList.ItemsSource = hidden;
        HiddenBadge.Text = $"{manager.Hidden.Count} hidden";
        AvailableCount.Text = $"{rows.Length} of {available.Count} windows";
        AvailableEmpty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AvailableEmptyText.Text = Search.Text.Length > 0 ? "No matching windows. Try a different title or app name." : "Open an app, then choose Refresh. Only compatible windows appear here.";
        HiddenEmpty.Visibility = hidden.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        HiddenEmptyText.Text = HiddenSearch.Text.Length > 0 ? "No matching hidden windows. Try another search." : "Windows you hide appear here. You can find them in the tray, too.";
        var rules = preferences.Rules.Select(r => new RuleRow(r)).Where(r =>
            (r.Name + " " + r.Rule.ExecutablePath).Contains(RulesSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        RulesList.ItemsSource = rules;
        RulesEmpty.Visibility = rules.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OpenPanel() { Show(); WindowState = WindowState.Normal; Activate(); _ = RefreshWindows(); }
    private void SearchChanged(object sender, TextChangedEventArgs e) => UpdateLists();
    private async void RefreshClicked(object sender, RoutedEventArgs e) => await RefreshWindows();
    private void SendToTray(object sender, RoutedEventArgs e) { Hide(); if (preferences.TrayNotifications) tray.ShowTip(); }
    private void HideSelected(object sender, RoutedEventArgs e)
    {
        if (AvailableList.SelectedItem is WindowRow row) RunOperation(() => manager.HideAsync(row.Window));
        else Status.Text = "Select a window to hide, or use its Hide button.";
    }
    private void RowAction(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not WindowRow row) return;
        if (row.Entry is null) RunOperation(() => manager.HideAsync(row.Window));
        else RunOperation(() => manager.RestoreAsync(row.Entry));
    }
    private void RestoreSelected(object sender, RoutedEventArgs e)
    {
        if (HiddenList.SelectedItem is WindowRow { Entry: not null } row) RunOperation(() => manager.RestoreAsync(row.Entry));
        else Status.Text = "Select a hidden window to restore, or use its Restore button.";
    }
    private void RestoreAll(object sender, RoutedEventArgs e) => RunOperation(RestoreEverything);
    private async Task<WindowResult> RestoreEverything() => await manager.RestoreAllAsync()
        ? WindowResult.Ok("All windows restored.") : WindowResult.Fail("Some windows need attention. Open Hidden windows for recovery steps. Nivetray will keep them tracked.");
    private async void RunOperation(Func<Task<WindowResult>> operation)
    {
        if (busy || exiting) return;
        busy = true;
        HideButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Status.Text = "Updating window visibility…";
        try { Status.Text = (await operation()).Message; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { diagnostics.Write("Window operation failed: " + ex.GetType().Name); Status.Text = "The operation failed. Hidden windows remain tracked; retry Restore."; }
        finally
        {
            busy = false; HideButton.IsEnabled = true; UpdateLists(); tray.Refresh();
            await RefreshWindows();
            if (exitRequested) { exitRequested = false; ExitApplication(); }
        }
    }
    private void RememberSelected(object sender, RoutedEventArgs e)
    {
        if (AvailableList.SelectedItem is not WindowRow row) { Status.Text = "Select a window to favorite its app."; return; }
        PutRule(row.Window.ExecutablePath, RuleMode.Manage);
    }
    private void SaveRule(object sender, RoutedEventArgs e)
    {
        var path = RulePath.Text.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        { Status.Text = "Choose an executable with Browse, or enter its full .exe path."; return; }
        PutRule(path, (RuleModeBox.SelectedItem as RuleChoice)?.Mode ?? RuleMode.Manage);
    }
    private void PutRule(string path, RuleMode mode)
    {
        preferences.Rules.RemoveAll(r => r.ExecutablePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        preferences.Rules.Add(new(path, mode)); SavePreferences(); _ = RefreshWindows();
    }
    private void BrowseExecutable(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) RulePath.Text = dialog.FileName;
    }
    private void RemoveRule(object sender, RoutedEventArgs e)
    { if (RulesList.SelectedItem is RuleRow row) { preferences.Rules.Remove(row.Rule); SavePreferences(); _ = RefreshWindows(); } }
    private void ClosePreferenceChanged(object sender, RoutedEventArgs e)
    { if (initialized) { preferences.CloseToTray = CloseToTray.IsChecked == true; SavePreferences(); } }
    private void OtherPreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        preferences.TrayNotifications = Notifications.IsChecked == true;
        preferences.ShowAuxiliaryWindows = Auxiliary.IsChecked == true;
        SavePreferences(); _ = RefreshWindows();
    }
    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || ThemeBox.SelectedItem is not AppTheme theme) return;
        preferences.Theme = theme; themes.Apply(theme); SavePreferences(); e.Handled = true;
    }
    private void NavigationChanged(object sender, SelectionChangedEventArgs e)
    { if (initialized && e.Source == Tabs) UpdateLists(); }
    private void SavePreferences()
    {
        try { store.Save(preferences); Status.Text = "Preferences saved on this computer."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { diagnostics.Write("Settings save failed: " + ex.GetType().Name); Status.Text = "Preferences could not be saved. Check access to the local data folder."; }
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        { (Tabs.SelectedIndex switch { 1 => HiddenSearch, 2 => RulesSearch, _ => Search }).Focus(); e.Handled = true; }
        else if (e.Key == Key.F5) { _ = RefreshWindows(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
    }
    private void OpenDataFolder(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(directory); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { directory }, UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { Status.Text = "The data folder could not be opened."; }
    }
    private void CopyRecoveryDetails(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText("Nivetray 0.2.0 recovery\n" + string.Join("\n", manager.Hidden.Select(h =>
                $"PID {h.Window.Identity.ProcessId}; HWND 0x{h.Window.Identity.Handle:X}; {h.State}: {h.Error ?? "Tracked"}")));
            Status.Text = "Recovery details copied. They contain IDs and error categories, without titles or paths.";
        }
        catch (System.Runtime.InteropServices.ExternalException) { Status.Text = "The clipboard is busy. Try again."; }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    { if (!exiting) { e.Cancel = true; if (preferences.CloseToTray) Hide(); else ExitApplication(); } }
    private void ExitClicked(object sender, RoutedEventArgs e) => ExitApplication();
    private async void ExitApplication()
    {
        if (exiting) return;
        if (busy) { exitRequested = true; Status.Text = "Finishing the current operation, then restoring windows before exit…"; return; }
        busy = true; Progress.Visibility = Visibility.Visible;
        try
        {
            if (!await manager.RestoreAllAsync())
            { OpenPanel(); Tabs.SelectedIndex = 1; UpdateLists(); Status.Text = "Exit paused: some windows need recovery. Retry Restore, or keep Nivetray running in the tray."; return; }
            if (discovery is not null) await discovery;
            exiting = true; tray.Dispose(); themes.Dispose(); platform.Dispose(); Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { diagnostics.Write("Exit recovery failed: " + ex.GetType().Name); Show(); Status.Text = "Exit cancelled. Your windows stay tracked. Retry Restore."; }
        finally { busy = false; Progress.Visibility = Visibility.Collapsed; }
    }
    private sealed record RuleChoice(RuleMode Mode) { public string Label => WindowSearch.RuleLabel(Mode); }
    private sealed record RuleRow(ApplicationRule Rule)
    { public string Name => Path.GetFileNameWithoutExtension(Rule.ExecutablePath); public string Label => WindowSearch.RuleLabel(Rule.Mode); }
    private sealed record WindowRow(AppWindow Window, ImageSource? Icon, HiddenWindow? Entry, bool Favorite)
    {
        public string AppName => Window.Name switch { "WindowsTerminal" => "Windows Terminal", "powershell" => "Windows PowerShell", "pwsh" => "PowerShell", "cmd" => "Command Prompt", "conhost" or "OpenConsole" => "Console host", _ => Window.Name };
        public string Subtitle => Entry is null ? Favorite ? "Favorite · Ready to hide" : "Ready to hide" : Entry.State switch { RecoveryState.Hidden => "Hidden · App still running", RecoveryState.Hiding => "Hiding…", RecoveryState.Restoring => "Restoring…", _ => "Recovery needs attention" };
        public string? Error => Entry?.Error;
        public string Action => Entry is null ? "Hide" : "Restore";
        public string ActionName => Action + " " + Window.Title;
    }
}
