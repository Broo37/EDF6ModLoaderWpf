using System.Text.Json.Serialization;

namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Optional metadata file (mod_info.json) stored inside each mod folder.
/// </summary>
public sealed class ModInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("gameVersion")]
    public string GameVersion { get; set; } = "EDF6";

    /// <summary>
    /// Relative file paths owned by this mod (e.g. "Weapon/weapon_data.bin").
    /// Used to safely remove only files belonging to this mod.
    /// </summary>
    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = [];

    [JsonPropertyName("dateAdded")]
    public string DateAdded { get; set; } = string.Empty;
}
