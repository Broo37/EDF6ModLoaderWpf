using System.Text.Json.Serialization;

namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Snapshot of the last deployed managed-file state before a new apply operation starts.
/// </summary>
public sealed class ApplyBackupSnapshot
{
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("activePresetName")]
    public string ActivePresetName { get; set; } = string.Empty;

    [JsonPropertyName("activeMods")]
    public List<ActiveModEntry> ActiveMods { get; set; } = [];

    [JsonPropertyName("modGroups")]
    public Dictionary<string, string> ModGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("backedUpFiles")]
    public List<string> BackedUpFiles { get; set; } = [];
}