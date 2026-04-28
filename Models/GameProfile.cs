using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Per-game configuration profile. Each supported EDF game has its own profile
/// with isolated paths, settings, and mod library.
/// </summary>
public partial class GameProfile : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("gameId")]
    private string _gameId = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("displayName")]
    private string _displayName = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("shortName")]
    private string _shortName = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("executableName")]
    private string _executableName = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("gameRootPath")]
    private string _gameRootPath = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("modLibraryPath")]
    private string _modLibraryPath = string.Empty;

    [ObservableProperty]
    [property: JsonIgnore]
    private string _appDataFolder = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("isConfigured")]
    private bool _isConfigured;

    [ObservableProperty]
    [property: JsonPropertyName("bannerColor")]
    private string _bannerColor = string.Empty;

    [ObservableProperty]
    [property: JsonIgnore]
    private string _gameIconPath = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("lastOpened")]
    private DateTime? _lastOpened;

    [ObservableProperty]
    [property: JsonIgnore]
    private List<RecentImportEntry> _recentImports = [];

    /// <summary>
    /// True when this profile is the currently active game in the UI.
    /// Runtime-only, not persisted.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isActiveGame;
}
