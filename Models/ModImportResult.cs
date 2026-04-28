namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Result of importing a mod into the library from a folder or archive.
/// </summary>
public sealed class ModImportResult
{
    public string ModName { get; set; } = string.Empty;

    public string DestinationFolderPath { get; set; } = string.Empty;

    public int ImportedFileCount { get; set; }

    public bool CreatedMetadata { get; set; }

    public bool ReplacedExisting { get; set; }
}