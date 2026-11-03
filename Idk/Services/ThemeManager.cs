using System.Windows;
using System.Windows.Media;

namespace Idk.Services;

public static class ThemeManager
{
    public static event Action? ThemeChanged;

    private const string ThemeMarker = "IdkAppTheme";

    public static void ApplyFromSettings() => Apply(SettingsStore.AppTheme);

    public static void Apply(AppTheme theme)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var uri = new Uri(
            theme == AppTheme.Light ? "Themes/AppThemeLight.xaml" : "Themes/AppThemeDark.xaml",
            UriKind.Relative);

        var next = new ResourceDictionary { Source = uri };
        next[ThemeMarker] = true;

        var merged = app.Resources.MergedDictionaries;
        var index = -1;
        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i].Contains(ThemeMarker))
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
            merged[index] = next;
        else
            merged.Add(next);

        ApplyShellChrome();
        ThemeChanged?.Invoke();
    }

    private static void ApplyShellChrome()
    {
        if (System.Windows.Application.Current.MainWindow is { } shell)
        {
            shell.Background = Brush("AppBg");
            shell.Foreground = Brush("AppFg");
        }

        global::Idk.App.Overlay?.ApplyTheme();
    }

    public static SolidColorBrush Brush(string key) =>
        System.Windows.Application.Current.FindResource(key) as SolidColorBrush
        ?? new SolidColorBrush(Colors.Magenta);

    public static System.Windows.Media.Color Color(string key) => Brush(key).Color;

    public static SolidColorBrush FrozenBrush(string key)
    {
        var b = Brush(key);
        if (b.IsFrozen) return b;
        var copy = new SolidColorBrush(b.Color);
        copy.Freeze();
        return copy;
    }
}
