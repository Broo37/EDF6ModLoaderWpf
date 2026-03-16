using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Represents a single mod displayed in the main list.
/// Uses CommunityToolkit.Mvvm source generators for observable properties.
/// </summary>
public partial class ModEntry : ObservableObject
{
    /// <summary>
    /// Whether this mod is currently enabled (files copied into the game's Mods folder).
    /// </summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// Load order number (1 = lowest priority, N = highest). Only meaningful when active.
    /// </summary>
    [ObservableProperty]
    private int _loadOrder;

    /// <summary>
    /// Optional group name for organizing mods (e.g. "Weapons", "Characters").
    /// Empty string means ungrouped.
    /// </summary>
    [ObservableProperty]
    private string _group = string.Empty;

    /// <summary>
    /// Display name of the mod (defaults to folder name).
    /// </summary>
    [ObservableProperty]
    private string _modName = string.Empty;

    /// <summary>
    /// Comma-separated list of subfolders this mod uses (e.g. "Weapon, Object").
    /// </summary>
    [ObservableProperty]
    private string _subfolders = string.Empty;

    /// <summary>
    /// Last modified date of the mod folder.
    /// </summary>
    [ObservableProperty]
    private DateTime _dateAdded;

    /// <summary>
    /// Description from mod_info.json, or "No info" when not available.
    /// </summary>
    [ObservableProperty]
    private string _description = "No info";

    /// <summary>
    /// Status text: "Active", "Inactive", "✅ Wins", or "⚠️ Overridden".
    /// </summary>
    [ObservableProperty]
    private string _status = "Inactive";

    /// <summary>
    /// True when this mod has a file conflict with another active mod.
    /// </summary>
    [ObservableProperty]
    private bool _hasConflict;

    /// <summary>
    /// List of mod names this mod conflicts with.
    /// </summary>
    public ObservableCollection<string> ConflictsWith { get; set; } = [];

    /// <summary>
    /// Display string for the load order column ("-" when inactive).
    /// </summary>
    public string LoadOrderDisplay => IsActive ? LoadOrder.ToString() : "-";

    /// <summary>
    /// Full path to the mod folder inside the mods library.
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Parsed mod_info.json metadata (null when the file doesn't exist).
    /// </summary>
    public ModInfo? Info { get; set; }

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(LoadOrderDisplay));
    }

    partial void OnLoadOrderChanged(int value)
    {
        OnPropertyChanged(nameof(LoadOrderDisplay));
    }
}
