using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Nivetray.Core;

namespace Nivetray.App;

internal sealed class ThemeManager : IDisposable
{
    private AppTheme preference;
    public ThemeManager(AppTheme initial)
    {
        preference = initial;
        SystemEvents.UserPreferenceChanged += SystemChanged;
        SystemParameters.StaticPropertyChanged += ParametersChanged;
        Apply(initial);
    }
    public void Apply(AppTheme theme)
    {
        preference = theme;
        var dark = theme == AppTheme.Dark;
        if (theme == AppTheme.System)
        {
            try { dark = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { dark = false; }
        }
        var colors = dark
            ? new[] { "#12151F", "#1C202D", "#272C3D", "#F1F2FA", "#B2B9CD", "#3E465D", "#B5A6FF", "#171125", "#36304E", "#30374B", "#FFB4B4" }
            : new[] { "#F4F5FA", "#FFFFFF", "#EEEFF6", "#242738", "#59627A", "#D9DDE8", "#6144B7", "#FFFFFF", "#EDE7FC", "#E5E8F2", "#A22139" };
        var keys = new[] { "Canvas", "Surface", "SurfaceAlt", "Text", "Muted", "Line", "Accent", "AccentText", "AccentSoft", "Hover", "Error" };
        for (var i = 0; i < keys.Length; i++)
        {
            var color = (Color)ColorConverter.ConvertFromString(colors[i]);
            if (SystemParameters.HighContrast)
                color = keys[i] switch
                {
                    "Text" or "Muted" or "Error" => SystemColors.WindowTextColor,
                    "Accent" or "Hover" or "AccentSoft" => SystemColors.HighlightColor,
                    "AccentText" => SystemColors.HighlightTextColor,
                    "Line" => SystemColors.WindowTextColor,
                    _ => SystemColors.WindowColor
                };
            var brush = new SolidColorBrush(color); brush.Freeze();
            Application.Current.Resources[keys[i]] = brush;
        }
        foreach (Window window in Application.Current.Windows)
            Nivetray.Windows.WindowAppearance.SetDarkCaption(new System.Windows.Interop.WindowInteropHelper(window).Handle,
                dark && !SystemParameters.HighContrast);
    }
    private void SystemChanged(object sender, UserPreferenceChangedEventArgs e) => Schedule();
    private void ParametersChanged(object? sender, PropertyChangedEventArgs e) => Schedule();
    private void Schedule() => Application.Current.Dispatcher.BeginInvoke(() => Apply(preference));
    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= SystemChanged;
        SystemParameters.StaticPropertyChanged -= ParametersChanged;
    }
}
