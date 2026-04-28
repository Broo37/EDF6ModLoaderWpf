using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDF6ModLoaderWpf.Helpers;
using EDF6ModLoaderWpf.Models;
using EDF6ModLoaderWpf.Services;
using Microsoft.Win32;

namespace EDF6ModLoaderWpf.ViewModels;

/// <summary>
/// ViewModel for the Settings window — tabbed per-game configuration.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly Action? _onSaved;
    private AppSettings _settings = null!;

    /// <summary>All game profiles shown as tabs.</summary>
    public ObservableCollection<GameProfile> GameProfiles { get; } = [];

    /// <summary>Currently selected game tab.</summary>
    [ObservableProperty]
    private GameProfile? _selectedProfile;

    /// <summary>Validation error text shown in the UI.</summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>Validation status text (e.g. "✅ EDF6.exe found").</summary>
    [ObservableProperty]
    private string _validationMessage = string.Empty;

    /// <summary>All available system font families, sorted alphabetically.</summary>
    public ObservableCollection<string> AvailableFonts { get; } = [];

    /// <summary>Currently selected UI font family name.</summary>
    [ObservableProperty]
    private string _selectedFontFamily = "Segoe UI";

    public SettingsViewModel(SettingsService settingsService, Action? onSaved = null)
    {
        _settingsService = settingsService;
        _onSaved = onSaved;
    }

    /// <summary>
    /// Loads all game profiles into the tabs.
    /// </summary>
    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        _settings = await _settingsService.LoadAsync();
        GameProfiles.Clear();
        foreach (var profile in _settings.GameProfiles)
            GameProfiles.Add(profile);

        // Pre-select the active game's tab
        SelectedProfile = GameProfiles.FirstOrDefault(p => p.GameId == _settings.ActiveGameId)
            ?? GameProfiles.FirstOrDefault();

        // Load available system fonts
        AvailableFonts.Clear();
        foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
            AvailableFonts.Add(font.Source);

        // Pre-select the saved font
        SelectedFontFamily = AvailableFonts.Contains(_settings.FontFamily)
            ? _settings.FontFamily
            : "Segoe UI";

        ValidateCurrentProfile();
    }

    partial void OnSelectedProfileChanged(GameProfile? value)
    {
        ErrorMessage = string.Empty;
        ValidateCurrentProfile();
    }

    /// <summary>
    /// Selects a specific game tab by ID (used by WelcomeWindow).
    /// </summary>
    public void SelectGame(string gameId)
    {
        SelectedProfile = GameProfiles.FirstOrDefault(p => p.GameId == gameId);
    }

    /// <summary>
    /// Opens a folder browser for the selected game's root directory.
    /// </summary>
    [RelayCommand]
    private void BrowseGameRoot()
    {
        if (SelectedProfile is null) return;

        var dialog = new OpenFolderDialog
        {
            Title = $"Select {SelectedProfile.DisplayName} Game Directory"
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedProfile.GameRootPath = dialog.FolderName;
            ValidateCurrentProfile();
        }
    }

    /// <summary>
    /// Opens a folder browser for the selected game's mods library.
    /// </summary>
    [RelayCommand]
    private void BrowseModsLibrary()
    {
        if (SelectedProfile is null) return;

        var dialog = new OpenFolderDialog
        {
            Title = $"Select {SelectedProfile.DisplayName} Mods Library Directory"
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedProfile.ModLibraryPath = dialog.FolderName;
            ValidateCurrentProfile();
        }
    }

    [RelayCommand]
    private void AutoDetectSelectedGame()
    {
        if (SelectedProfile is null)
            return;

        ErrorMessage = string.Empty;
        var changed = _settingsService.TryAutoDetectGameProfile(SelectedProfile);
        var gameValid = SettingsService.ValidateGameDirectory(SelectedProfile.GameRootPath, SelectedProfile.ExecutableName);

        ValidateCurrentProfile();

        if (gameValid)
        {
            var libraryStatus = Directory.Exists(SelectedProfile.ModLibraryPath)
                ? "Mods library folder ready."
                : "Mods library path suggested and will be created on save.";
            ValidationMessage = $"✅ Auto-detected {SelectedProfile.DisplayName}. {libraryStatus}";
            return;
        }

        if (!changed)
        {
            ErrorMessage = $"Could not auto-detect {SelectedProfile.DisplayName} in common Steam library locations.";
        }
    }

    /// <summary>
    /// Validates and saves the currently selected game configuration.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedProfile is null) return;
        ErrorMessage = string.Empty;

        // Validate game directory
        if (!SettingsService.ValidateGameDirectory(SelectedProfile.GameRootPath, SelectedProfile.ExecutableName))
        {
            ErrorMessage = $"Invalid game directory — {SelectedProfile.ExecutableName} was not found in the specified folder.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedProfile.ModLibraryPath))
        {
            ErrorMessage = "Choose a mods library directory.";
            return;
        }

        try
        {
            Directory.CreateDirectory(SelectedProfile.ModLibraryPath);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not create the mods library directory: {ex.Message}";
            return;
        }

        // Validate mods library directory
        if (!SettingsService.ValidateModsLibraryDirectory(SelectedProfile.ModLibraryPath))
        {
            ErrorMessage = "The mods library directory does not exist.";
            return;
        }

        try
        {
            SelectedProfile.IsConfigured = true;
            SelectedProfile.LastOpened = DateTime.Now;
            await _settingsService.SaveGameConfigAsync(SelectedProfile);

            // Update global settings
            _settings.SetupCompleted = true;
            _settings.FontFamily = SelectedFontFamily;
            await _settingsService.SaveAsync(_settings);

            // Apply the chosen font to all open windows
            FontHelper.ApplyFont(SelectedFontFamily);

            _onSaved?.Invoke();
        }
        catch (Exception ex)
        {
            NotificationHelper.ShowError("Save Error", $"Failed to save settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears / resets the selected game's configuration.
    /// </summary>
    [RelayCommand]
    private async Task ClearGameConfigAsync()
    {
        if (SelectedProfile is null) return;

        SelectedProfile.GameRootPath = string.Empty;
        SelectedProfile.ModLibraryPath = string.Empty;
        SelectedProfile.IsConfigured = false;

        try
        {
            await _settingsService.SaveGameConfigAsync(SelectedProfile);
            ErrorMessage = string.Empty;
            ValidationMessage = string.Empty;
        }
        catch (Exception ex)
        {
            NotificationHelper.ShowError("Error", $"Failed to clear config: {ex.Message}");
        }
    }

    private void ValidateCurrentProfile()
    {
        if (SelectedProfile is null) return;
        ErrorMessage = string.Empty;

        bool gameValid = SettingsService.ValidateGameDirectory(
            SelectedProfile.GameRootPath, SelectedProfile.ExecutableName);

        if (!string.IsNullOrWhiteSpace(SelectedProfile.GameRootPath))
        {
            var libraryStatus = GetModsLibraryStatusMessage(SelectedProfile.ModLibraryPath);
            ValidationMessage = gameValid
                ? $"✅ {SelectedProfile.ExecutableName} found — valid game directory. {libraryStatus}"
                : $"⚠️ {SelectedProfile.ExecutableName} not found in this folder";
        }
        else
        {
            ValidationMessage = string.Empty;
        }
    }

    private static string GetModsLibraryStatusMessage(string modLibraryPath)
    {
        if (string.IsNullOrWhiteSpace(modLibraryPath))
            return "Choose a mods library folder.";

        return Directory.Exists(modLibraryPath)
            ? "Mods library folder ready."
            : "Mods library will be created on save.";
    }
}
