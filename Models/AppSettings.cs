using System.Text.Json.Serialization;

namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Global application settings stored in %AppData%\EDFModManager\settings.json.
/// Per-game paths are stored separately in game_config.json files.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// The currently active game profile ID ("EDF41", "EDF5", or "EDF6").
    /// </summary>
    [JsonPropertyName("activeGameId")]
    public string ActiveGameId { get; set; } = "EDF6";

    /// <summary>
    /// Whether the initial setup has been completed (at least one game configured).
    /// </summary>
    [JsonPropertyName("setupCompleted")]
    public bool SetupCompleted { get; set; }

    /// <summary>
    /// The UI font family name. Defaults to Segoe UI (ModernWpf default).
    /// </summary>
    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Segoe UI";

    /// <summary>
    /// Runtime-only list of all game profiles, populated from SupportedGames
    /// constants and per-game config files on disk. Not serialized.
    /// </summary>
    [JsonIgnore]
    public List<GameProfile> GameProfiles { get; set; } = [];
}
