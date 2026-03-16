# Feature: Font Selection in Settings

## Overview

Added a font family dropdown to the Settings window so users can pick a custom UI font. The selected font persists in `settings.json` and applies globally at startup and on change.

## Files Modified

| File | Change |
|------|--------|
| `Models/AppSettings.cs` | Added `FontFamily` string property (default: `"Segoe UI"`) with `[JsonPropertyName("fontFamily")]` |
| `Helpers/FontHelper.cs` | **New file** — static helper that stores and applies a `FontFamily` to all windows |
| `ViewModels/SettingsViewModel.cs` | Added `AvailableFonts` collection, `SelectedFontFamily` property, and save/load logic |
| `Views/SettingsWindow.xaml` | Added "Appearance" section with font `ComboBox` (items render in their own font for preview) |
| `Views/SettingsWindow.xaml.cs` | Added `FontHelper.ApplyCurrentFont(this)` on `Loaded` |
| `App.xaml.cs` | Loads settings and applies the saved font to `MainWindow` before showing it |

## How It Works

1. **On startup** (`App.xaml.cs`):
   - `FontHelper.ApplyFont(settings.FontFamily)` is called — this **stores** the font in a static field and applies it to any open windows.
   - `FontHelper.ApplyCurrentFont(mainWindow)` is called right after creating the main window, before `Show()`.

2. **In Settings** (`SettingsViewModel.cs`):
   - `LoadSettingsAsync` populates `AvailableFonts` from `Fonts.SystemFontFamilies` (sorted A–Z) and pre-selects the saved font.
   - `SaveAsync` persists `SelectedFontFamily` → `_settings.FontFamily` and calls `FontHelper.ApplyFont()` to update all open windows immediately.

3. **New windows** (e.g. `SettingsWindow.xaml.cs`):
   - Call `FontHelper.ApplyCurrentFont(this)` on `Loaded` so they pick up the stored font.

## Bug Fix: Font Not Persisting Across Restarts

### Problem

After selecting a font in Settings and restarting the app, the font reverted to Segoe UI.

### Root Cause

`FontHelper.ApplyFont()` iterates `Application.Current.Windows` — but it was called **before** any window was created. The loop applied the font to zero windows.

### Solution

1. `FontHelper` now stores the current font in a static `_currentFont` field.
2. Added `ApplyCurrentFont(Window)` method for applying the stored font to individual windows at creation time.
3. `App.xaml.cs` calls `ApplyFont()` early (to store the value), then `ApplyCurrentFont(mainWindow)` after creating the window.
4. `SettingsWindow.xaml.cs` calls `ApplyCurrentFont(this)` on `Loaded`.

### Key Lesson

> When applying global WPF resources via `Application.Current.Windows`, timing matters. If no windows exist yet, the loop does nothing. Always provide a way to apply settings to windows at creation time, not just retroactively.

## FontHelper.cs — Final Implementation

```csharp
public static class FontHelper
{
    private static FontFamily? _currentFont;

    // Stores the font and applies it to all currently open windows
    public static void ApplyFont(string? fontFamilyName)
    {
        if (string.IsNullOrWhiteSpace(fontFamilyName))
            return;

        _currentFont = new FontFamily(fontFamilyName);

        foreach (Window window in Application.Current.Windows)
            window.FontFamily = _currentFont;
    }

    // Applies the stored font to a specific window (e.g. on creation or Loaded)
    public static void ApplyCurrentFont(Window window)
    {
        if (_currentFont is not null)
            window.FontFamily = _currentFont;
    }
}
```

## Settings UI (SettingsWindow.xaml)

The font selector is an "Appearance" section placed below the game configuration tabs:

```xml
<!-- Appearance: Font selector -->
<StackPanel Grid.Row="4">
    <TextBlock Text="Appearance" FontWeight="SemiBold" FontSize="14" Margin="0,0,0,6"/>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="8"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0" Text="Font Family" VerticalAlignment="Center"/>
        <ComboBox Grid.Column="2"
                  ItemsSource="{Binding AvailableFonts}"
                  SelectedItem="{Binding SelectedFontFamily}"
                  MaxDropDownHeight="300">
            <ComboBox.ItemTemplate>
                <DataTemplate>
                    <!-- Each font name renders in its own typeface for preview -->
                    <TextBlock Text="{Binding}" FontFamily="{Binding}" FontSize="13"/>
                </DataTemplate>
            </ComboBox.ItemTemplate>
        </ComboBox>
    </Grid>
</StackPanel>
```

## Persistence

The font is stored in the global `settings.json` at `%AppData%\EDFModManager\settings.json`:

```json
{
  "activeGameId": "EDF6",
  "setupCompleted": true,
  "fontFamily": "Consolas"
}
```
