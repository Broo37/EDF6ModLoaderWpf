namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Tracks a recently imported mod for quick re-selection and pinning in the main UI.
/// Persisted per game via game_config.json.
/// </summary>
public sealed class RecentImportEntry
{
    public string ModName { get; init; } = string.Empty;

    public string SourceLabel { get; init; } = string.Empty;

    public string FolderPath { get; init; } = string.Empty;

    public int ImportedFileCount { get; init; }

    public bool ReplacedExisting { get; init; }

    public bool IsPinned { get; set; }

    public DateTime ImportedAt { get; init; }

    public string ImportedAtLabel => ImportedAt.ToString("HH:mm");
}