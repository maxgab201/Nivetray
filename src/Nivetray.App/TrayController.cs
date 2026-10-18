using System.ComponentModel;
using System.Windows;
using Nivetray.Core;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Nivetray.App;

internal sealed class TrayController : IDisposable
{
    private readonly WindowManager manager;
    private readonly Action open, search, settings, restoreAll, exit;
    private readonly Action<HiddenWindow> restore;
    private readonly Action<string> log;
    private readonly Forms.NotifyIcon icon;
    private readonly Drawing.Icon artwork;
    private readonly Forms.ContextMenuStrip menu = new();
    public TrayController(WindowManager manager, Action open, Action search, Action settings, Action<HiddenWindow> restore,
        Action restoreAll, Action exit, Action<string> log)
    {
        this.manager = manager; this.open = open; this.search = search; this.settings = settings;
        this.restore = restore; this.restoreAll = restoreAll; this.exit = exit; this.log = log;
        using (var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Nivetray.ico")).Stream)
            artwork = new Drawing.Icon(resource);
        icon = new Forms.NotifyIcon { Text = "Nivetray", Icon = artwork, ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += DoubleClick;
        menu.Opening += Opening;
        Refresh();
    }
    public void Refresh()
    {
        icon.Text = $"Nivetray — {manager.Hidden.Count} hidden";
        // An async discovery/restore may finish while the user is reading the menu.
        // Keep that menu stable; Opening always rebuilds it from current entries.
        if (menu.Visible) return;
        foreach (var item in menu.Items.Cast<Forms.ToolStripItem>().ToArray()) item.Dispose();
        menu.Items.Clear();
        Add("Open Nivetray", open);
        Add($"View all hidden windows ({manager.Hidden.Count}) / Search…", search);
        menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var entry in TrayPolicy.Recent(manager.Hidden))
        {
            var label = entry.Window.Name + " — " + entry.Window.Title;
            if (label.Length > 76) label = label[..73] + "…";
            if (entry.State == RecoveryState.NeedsAttention) label = "Needs attention: " + label;
            Add("Restore " + label.Replace("&", "&&"), () => restore(entry));
        }
        if (manager.Hidden.Count == 0) menu.Items.Add(new Forms.ToolStripMenuItem("No hidden windows") { Enabled = false });
        Add("Restore all", restoreAll, manager.Hidden.Count > 0);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add("Settings", settings); Add("Restore all and exit", exit);
    }
    private void Add(string label, Action action, bool enabled = true)
    {
        var item = new Forms.ToolStripMenuItem(label) { Enabled = enabled };
        item.Click += (_, _) => Application.Current.Dispatcher.BeginInvoke(action);
        menu.Items.Add(item);
    }
    private void DoubleClick(object? sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(open);
    private void Opening(object? sender, CancelEventArgs e)
    {
        try { manager.Prune(); Refresh(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { log("Tray refresh failed: " + ex.GetType().Name); }
    }
    public void ShowTip() => icon.ShowBalloonTip(1800, "Nivetray is in the tray", "Open its tray icon to restore windows or return to the panel.", Forms.ToolTipIcon.Info);
    public void Dispose()
    { menu.Opening -= Opening; icon.DoubleClick -= DoubleClick; icon.Visible = false; icon.Dispose(); menu.Dispose(); artwork.Dispose(); }
}
