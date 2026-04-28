namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Preview of the changes that will happen when mods are applied to the game.
/// </summary>
public sealed class ApplySummaryPreview
{
    public string GameDisplayName { get; set; } = string.Empty;

    public string ActivePresetName { get; set; } = string.Empty;

    public bool IsActivePresetDirty { get; set; }

    public int ActiveModCount { get; set; }

    public int WinningFileCount { get; set; }

    public int AddedFileCount { get; set; }

    public int RemovedFileCount { get; set; }

    public int ReplacedFileCount { get; set; }

    public int ConflictFileCount { get; set; }

    public int HighRiskModCount { get; set; }

    public List<string> ActiveMods { get; set; } = [];

    public List<string> AddedFiles { get; set; } = [];

    public List<string> RemovedFiles { get; set; } = [];

    public List<string> ReplacedFiles { get; set; } = [];

    public List<string> ConflictSummaries { get; set; } = [];

    public List<string> HighRiskMods { get; set; } = [];
}