using System.Text.Json.Serialization;

namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Persisted as [GameRoot]\Mods\active_mods.json.
/// Tracks which mods are currently enabled, their load order, and the files they own.
/// </summary>
public sealed class ActiveModsRegistry
{
    /// <summary>
    /// Ordered list of active mods with their load order and owned files.
    /// </summary>
    [JsonPropertyName("activeMods")]
    public List<ActiveModEntry> ActiveMods { get; set; } = [];

    /// <summary>
    /// Timestamp of the last modification.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    /// <summary>
    /// Group assignments for all mods (including inactive). Keyed by mod name.
    /// </summary>
    [JsonPropertyName("modGroups")]
    public Dictionary<string, string> ModGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A single entry in the active mods registry.
/// </summary>
public sealed class ActiveModEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("loadOrder")]
    public int LoadOrder { get; set; }

    /// <summary>
    /// Optional group name for organizing mods.
    /// </summary>
    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Relative file paths installed by this mod (e.g. "Weapon\weapon_data.bin").
    /// </summary>
    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = [];
}
