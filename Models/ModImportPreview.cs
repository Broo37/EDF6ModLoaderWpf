namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Preview of a mod import before files are copied into the library.
/// </summary>
public sealed class ModImportPreview
{
    public string SourceDisplayName { get; set; } = string.Empty;

    public string ProposedModName { get; set; } = string.Empty;

    public string DestinationFolderPath { get; set; } = string.Empty;

    public int ImportedFileCount { get; set; }

    public bool HasMetadata { get; set; }

    public bool WillCreateMetadata { get; set; }

    public string DetectedGameVersion { get; set; } = string.Empty;

    public bool HasNameCollision { get; set; }

    public bool CanReplaceExisting { get; set; }

    public string ReplaceTargetFolderName { get; set; } = string.Empty;

    public List<string> DuplicateCandidates { get; set; } = [];

    public List<string> Warnings { get; set; } = [];
}