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

        // Trim to max history
        if (_undoStack.Count > MaxUndoHistory)
        {
            var temp = _undoStack.ToArray().Take(MaxUndoHistory).ToList();
            _undoStack.Clear();
            foreach (var item in temp.AsEnumerable().Reverse())
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
        var registry = new ActiveModsRegistry
        {
            LastUpdated = DateTime.Now,
            ActiveMods = mods
                .Where(m => m.IsActive)
                .OrderBy(m => m.LoadOrder)
                .Select(m => new ActiveModEntry
                {
                    Name = m.ModName,
                    LoadOrder = m.LoadOrder,
                    Group = m.Group,
                    Files = FileService.GetModRelativeFiles(m.FolderPath)
                })
                .ToList(),
            ModGroups = mods
                .Where(m => !string.IsNullOrEmpty(m.Group))
                .ToDictionary(m => m.ModName, m => m.Group, StringComparer.OrdinalIgnoreCase)
        };

        await _fileService.SaveRegistryAsync(registryDir, registry);
    }
}
