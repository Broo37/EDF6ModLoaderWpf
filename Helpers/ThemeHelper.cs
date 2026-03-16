using System.Windows.Media;
using ModernWpf;

namespace EDF6ModLoaderWpf.Helpers;

/// <summary>
/// Applies dynamic accent colors per game using ModernWpfUI's ThemeManager.
/// </summary>
public static class ThemeHelper
{
    public static void ApplyAccentColor(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor))
            return;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            ThemeManager.Current.AccentColor = color;
        }
        catch
        {
            // Ignore invalid color strings
        }
    }
}
