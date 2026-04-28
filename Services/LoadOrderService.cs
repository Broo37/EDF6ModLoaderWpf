using EDF6ModLoaderWpf.Models;

namespace EDF6ModLoaderWpf.Services;

/// <summary>
/// Manages load order numbers, persistence via the registry, and the undo stack.
/// </summary>
public sealed class LoadOrderService
{
    private const int MaxUndoHistory = 10;

    private readonly FileService _fileService;
    private readonly Stack<List<ModOrderSnapshot>> _undoStack = new();

    public LoadOrderService(FileService fileService)
    {
        _fileService = fileService;
    }

    /// <summary>Whether there is a previous state to undo to.</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Pushes the current state of all mods onto the undo stack.
    /// Call this BEFORE making any load order change.
    /// </summary>
    public void PushUndoState(IEnumerable<ModEntry> mods)
    {
        var snapshot = mods.Select(m => new ModOrderSnapshot
        {
            ModName = m.ModName,
            LoadOrder = m.LoadOrder,
            IsActive = m.IsActive,
            Group = m.Group
        }).ToList();

        _undoStack.Push(snapshot);

        // Trim to max history — keep the most recent MaxUndoHistory entries
        if (_undoStack.Count > MaxUndoHistory)
        {
            var keep = _undoStack.Take(MaxUndoHistory).Reverse().ToList();
            _undoStack.Clear();
            foreach (var item in keep)
                _undoStack.Push(item);
        }
    }

    /// <summary>
    /// Pops the last state from the undo stack and restores it onto the given mod list.
    /// Returns true if undo was performed.
    /// </summary>
    public bool TryUndo(IList<ModEntry> mods)
    {
        if (_undoStack.Count == 0)
            return false;

        var snapshot = _undoStack.Pop();
        var lookup = snapshot.ToDictionary(s => s.ModName, StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            if (lookup.TryGetValue(mod.ModName, out var state))
            {
                mod.IsActive = state.IsActive;
                mod.LoadOrder = state.LoadOrder;
                mod.Group = state.Group;
            }
            else
            {
                // Mod was not in the snapshot (new mod added after snapshot) — deactivate
                mod.IsActive = false;
                mod.LoadOrder = 0;
                mod.Group = string.Empty;
            }
        }

        return true;
    }

    /// <summary>
    /// Assigns sequential load order numbers (1..N) to all active mods,
    /// preserving their relative order. Inactive mods get LoadOrder = 0.
    /// </summary>
    public static void ResequenceLoadOrders(IList<ModEntry> mods)
    {
        var activeMods = mods
            .Where(m => m.IsActive)
            .OrderBy(m => m.LoadOrder == 0 ? int.MaxValue : m.LoadOrder)
            .ToList();

        for (int i = 0; i < activeMods.Count; i++)
            activeMods[i].LoadOrder = i + 1;

        foreach (var m in mods.Where(m => !m.IsActive))
            m.LoadOrder = 0;
    }

    /// <summary>
    /// Assigns the next available load order to a newly activated mod.
    /// </summary>
    public static void AssignNextLoadOrder(ModEntry mod, IEnumerable<ModEntry> allMods)
    {
        int maxOrder = allMods.Where(m => m.IsActive).Select(m => m.LoadOrder).DefaultIfEmpty(0).Max();
        mod.LoadOrder = maxOrder + 1;
    }

    /// <summary>
    /// Swaps the load order of two mods.
    /// </summary>
    public static void SwapLoadOrder(ModEntry modA, ModEntry modB)
    {
        (modA.LoadOrder, modB.LoadOrder) = (modB.LoadOrder, modA.LoadOrder);
    }

    /// <summary>
    /// Applies load orders from the registry onto the mod entries.
    /// New mods not in the registry are assigned the next available load order but remain inactive.
    /// Mods in the registry whose folders no longer exist are removed.
    /// </summary>
    public async Task ApplyRegistryToModsAsync(IList<ModEntry> mods, string registryDir)
    {
        var registry = await _fileService.LoadRegistryAsync(registryDir);

        var registryLookup = registry.ActiveMods
            .ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

        int maxOrder = registry.ActiveMods.Count > 0
            ? registry.ActiveMods.Max(e => e.LoadOrder)
            : 0;

        foreach (var mod in mods)
        {
            if (registryLookup.TryGetValue(mod.ModName, out var regEntry))
            {
                mod.IsActive = true;
                mod.LoadOrder = regEntry.LoadOrder;
                mod.Group = regEntry.Group;
            }
            else
            {
                mod.IsActive = false;
                // Assign next available order so if user activates, it goes to the end
                maxOrder++;
                mod.LoadOrder = maxOrder;

                // Restore group from the all-mods group map (covers inactive mods)
                if (registry.ModGroups.TryGetValue(mod.ModName, out var group))
                    mod.Group = group;
            }
        }

        // Re-sequence to ensure no gaps among active mods
        ResequenceLoadOrders(mods);
    }

    /// <summary>
    /// Saves the current mod states to the registry file.
    /// </summary>
    public async Task SaveToRegistryAsync(IList<ModEntry> mods, string registryDir)
    {
        var registry = await _fileService.LoadRegistryAsync(registryDir);
        registry.LastUpdated = DateTime.Now;
        registry.ActiveMods = BuildActiveEntries(mods);
        registry.ModGroups = BuildGroupMap(mods);

        await _fileService.SaveRegistryAsync(registryDir, registry);
    }

    public async Task<List<string>> GetPresetNamesAsync(string registryDir)
    {
        var registry = await _fileService.LoadRegistryAsync(registryDir);
        return registry.Presets
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Name)
            .ToList();
    }

    public async Task<string> GetActivePresetNameAsync(string registryDir)
    {
        var registry = await _fileService.LoadRegistryAsync(registryDir);
        return registry.ActivePresetName;
    }

    public async Task<string> SavePresetAsync(IList<ModEntry> mods, string registryDir, string presetName)
    {
        var normalizedName = NormalizePresetName(presetName);
        var registry = await _fileService.LoadRegistryAsync(registryDir);

        var activeEntries = BuildActiveEntries(mods);
        var modGroups = BuildGroupMap(mods);
        var preset = registry.Presets.FirstOrDefault(p =>
            string.Equals(p.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (preset is null)
        {
            preset = new ModPreset { Name = normalizedName };
            registry.Presets.Add(preset);
        }

        preset.LastUpdated = DateTime.Now;
        preset.ActiveMods = CloneActiveEntries(activeEntries);
        preset.ModGroups = CloneGroupMap(modGroups);

        registry.ActivePresetName = preset.Name;
        registry.LastUpdated = DateTime.Now;
        registry.ActiveMods = CloneActiveEntries(activeEntries);
        registry.ModGroups = CloneGroupMap(modGroups);

        await _fileService.SaveRegistryAsync(registryDir, registry);
        return preset.Name;
    }

    public async Task<bool> LoadPresetAsync(IList<ModEntry> mods, string registryDir, string presetName)
    {
        var normalizedName = NormalizePresetName(presetName);
        var registry = await _fileService.LoadRegistryAsync(registryDir);
        var preset = registry.Presets.FirstOrDefault(p =>
            string.Equals(p.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (preset is null)
            return false;

        var presetLookup = preset.ActiveMods.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            if (presetLookup.TryGetValue(mod.ModName, out var presetEntry))
            {
                mod.IsActive = true;
                mod.LoadOrder = presetEntry.LoadOrder;
                mod.Group = presetEntry.Group;
            }
            else
            {
                mod.IsActive = false;
                mod.LoadOrder = 0;
                mod.Group = preset.ModGroups.TryGetValue(mod.ModName, out var group)
                    ? group
                    : string.Empty;
            }
        }

        ResequenceLoadOrders(mods);

        registry.ActivePresetName = preset.Name;
        registry.LastUpdated = DateTime.Now;
        registry.ActiveMods = CloneActiveEntries(preset.ActiveMods);
        registry.ModGroups = CloneGroupMap(preset.ModGroups);

        await _fileService.SaveRegistryAsync(registryDir, registry);
        return true;
    }

    public async Task<bool> DeletePresetAsync(string registryDir, string presetName)
    {
        var normalizedName = NormalizePresetName(presetName);
        var registry = await _fileService.LoadRegistryAsync(registryDir);
        var removed = registry.Presets.RemoveAll(p =>
            string.Equals(p.Name, normalizedName, StringComparison.OrdinalIgnoreCase)) > 0;

        if (!removed)
            return false;

        if (string.Equals(registry.ActivePresetName, normalizedName, StringComparison.OrdinalIgnoreCase))
            registry.ActivePresetName = string.Empty;

        registry.LastUpdated = DateTime.Now;
        await _fileService.SaveRegistryAsync(registryDir, registry);
        return true;
    }

    public async Task<string> RenamePresetAsync(string registryDir, string existingName, string newName)
    {
        var normalizedExistingName = NormalizePresetName(existingName);
        var normalizedNewName = NormalizePresetName(newName);
        var registry = await _fileService.LoadRegistryAsync(registryDir);
        var preset = registry.Presets.FirstOrDefault(p =>
            string.Equals(p.Name, normalizedExistingName, StringComparison.OrdinalIgnoreCase));

        if (preset is null)
            throw new InvalidOperationException("Preset not found.");

        bool nameTaken = registry.Presets.Any(p =>
            !ReferenceEquals(p, preset) &&
            string.Equals(p.Name, normalizedNewName, StringComparison.OrdinalIgnoreCase));

        if (nameTaken)
            throw new InvalidOperationException("A preset with that name already exists.");

        preset.Name = normalizedNewName;
        preset.LastUpdated = DateTime.Now;

        if (string.Equals(registry.ActivePresetName, normalizedExistingName, StringComparison.OrdinalIgnoreCase))
            registry.ActivePresetName = normalizedNewName;

        registry.LastUpdated = DateTime.Now;
        await _fileService.SaveRegistryAsync(registryDir, registry);
        return normalizedNewName;
    }

    public async Task<ModPreset?> GetPresetAsync(string registryDir, string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            return null;

        var normalizedName = NormalizePresetName(presetName);
        var registry = await _fileService.LoadRegistryAsync(registryDir);
        var preset = registry.Presets.FirstOrDefault(p =>
            string.Equals(p.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        return preset is null ? null : ClonePreset(preset);
    }

    public static bool DoesPresetMatchState(IList<ModEntry> mods, ModPreset? preset)
    {
        if (preset is null)
            return false;

        var activeMods = mods
            .Where(m => m.IsActive)
            .OrderBy(m => m.LoadOrder)
            .ToList();

        if (activeMods.Count != preset.ActiveMods.Count)
            return false;

        for (int i = 0; i < activeMods.Count; i++)
        {
            var current = activeMods[i];
            var saved = preset.ActiveMods[i];
            if (!string.Equals(current.ModName, saved.Name, StringComparison.OrdinalIgnoreCase) ||
                current.LoadOrder != saved.LoadOrder ||
                !string.Equals(current.Group, saved.Group, StringComparison.Ordinal))
            {
                return false;
            }
        }

        var currentGroups = BuildGroupMap(mods);
        if (currentGroups.Count != preset.ModGroups.Count)
            return false;

        foreach (var (name, group) in currentGroups)
        {
            if (!preset.ModGroups.TryGetValue(name, out var savedGroup) ||
                !string.Equals(group, savedGroup, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static List<ActiveModEntry> BuildActiveEntries(IList<ModEntry> mods)
    {
        return mods
            .Where(m => m.IsActive)
            .OrderBy(m => m.LoadOrder)
            .Select(m => new ActiveModEntry
            {
                Name = m.ModName,
                LoadOrder = m.LoadOrder,
                Group = m.Group,
                Files = FileService.GetModRelativeFiles(m.FolderPath)
            })
            .ToList();
    }

    private static Dictionary<string, string> BuildGroupMap(IList<ModEntry> mods)
    {
        return mods
            .Where(m => !string.IsNullOrEmpty(m.Group))
            .ToDictionary(m => m.ModName, m => m.Group, StringComparer.OrdinalIgnoreCase);
    }

    private static List<ActiveModEntry> CloneActiveEntries(IEnumerable<ActiveModEntry> entries)
    {
        return entries.Select(e => new ActiveModEntry
        {
            Name = e.Name,
            LoadOrder = e.LoadOrder,
            Group = e.Group,
            Files = [.. e.Files]
        }).ToList();
    }

    private static Dictionary<string, string> CloneGroupMap(Dictionary<string, string> groups)
    {
        return new Dictionary<string, string>(groups, StringComparer.OrdinalIgnoreCase);
    }

    private static ModPreset ClonePreset(ModPreset preset)
    {
        return new ModPreset
        {
            Name = preset.Name,
            LastUpdated = preset.LastUpdated,
            ActiveMods = CloneActiveEntries(preset.ActiveMods),
            ModGroups = CloneGroupMap(preset.ModGroups)
        };
    }

    private static string NormalizePresetName(string presetName)
    {
        var normalizedName = presetName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Preset name cannot be empty.", nameof(presetName));

        return normalizedName;
    }
}
