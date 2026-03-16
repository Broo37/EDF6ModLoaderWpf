using System.Windows;
using System.Windows.Media;

namespace EDF6ModLoaderWpf.Helpers;

/// <summary>
/// Applies a global font family to all open windows and ModernWpf-themed controls.
/// </summary>
public static class FontHelper
{
    private static FontFamily? _currentFont;

    /// <summary>
    /// Stores the font, overrides the ModernWpf theme font resource, and applies to all open windows.
    /// </summary>
    public static void ApplyFont(string? fontFamilyName)
    {
        if (string.IsNullOrWhiteSpace(fontFamilyName))
            return;

        _currentFont = new FontFamily(fontFamilyName);

        // Override the ModernWpf theme font so buttons, comboboxes, etc. also use it
        Application.Current.Resources["ContentControlThemeFontFamily"] = _currentFont;

        foreach (Window window in Application.Current.Windows)
            window.FontFamily = _currentFont;
    }

    /// <summary>
    /// Applies the stored font to a specific window (e.g. on creation or Loaded).
    /// </summary>
    public static void ApplyCurrentFont(Window window)
    {
        if (_currentFont is not null)
            window.FontFamily = _currentFont;
    }
}
