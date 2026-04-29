using Microsoft.Win32;
using ModernWpf;
using System.Linq;
using System.Windows;

namespace EDF6ModLoaderWpf.Helpers;

public static class ThemeHelper
{
    private const string ThemeRegKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ThemeRegValue = "AppsUseLightTheme";

    private static readonly Uri DarkUri  = new("Themes/DarkTheme.xaml",  UriKind.Relative);
    private static readonly Uri LightUri = new("Themes/LightTheme.xaml", UriKind.Relative);

    public static void ApplyTheme(bool isDark)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var existing = dicts.FirstOrDefault(d => d.Source == DarkUri || d.Source == LightUri);
        if (existing != null) dicts.Remove(existing);

        dicts.Add(new ResourceDictionary { Source = isDark ? DarkUri : LightUri });
        ThemeManager.Current.ApplicationTheme = isDark ? ApplicationTheme.Dark : ApplicationTheme.Light;
    }

    public static bool GetSystemTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ThemeRegKey);
        var value = key?.GetValue(ThemeRegValue);
        return value is int i && i == 0; // 0 = dark, 1 = light
    }

    public static void LoadSavedOrSystemTheme(string? themeOverride)
    {
        bool isDark = themeOverride switch
        {
            "Dark"  => true,
            "Light" => false,
            _       => GetSystemTheme()
        };
        ApplyTheme(isDark);
    }
}
