namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Describes a single file conflict between two mods, resolved by load order.
/// </summary>
public sealed class ConflictInfo
{
    /// <summary>File name (e.g. "weapon_data.bin").</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Subfolder inside the Mods directory (e.g. "Weapon").</summary>
    public string SubFolder { get; set; } = string.Empty;

    /// <summary>Relative path: SubFolder/FileName.</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Name of the mod that wins (highest load order).</summary>
    public string WinnerModName { get; set; } = string.Empty;

    /// <summary>Name of the mod that gets overridden.</summary>
    public string LoserModName { get; set; } = string.Empty;

    /// <summary>Load order of the winning mod.</summary>
    public int WinnerLoadOrder { get; set; }

    /// <summary>Load order of the losing mod.</summary>
    public int LoserLoadOrder { get; set; }
}
