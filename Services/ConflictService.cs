using System.IO;
using EDF6ModLoaderWpf.Models;

namespace EDF6ModLoaderWpf.Services;

/// <summary>
/// Detects and resolves file conflicts between mods using load order priority.
/// Higher load order number = higher priority = wins the conflict.
/// </summary>
public sealed class ConflictService
{
    /// <summary>
    /// Scans all active mods and returns every pairwise file conflict.
    /// </summary>
    public List<ConflictInfo> DetectAllConflicts(IList<ModEntry> activeMods)
    {
        var conflicts = new List<ConflictInfo>();

        // Build a map: relative file path → list of mods that contain it
        var fileToMods = new Dictionary<string, List<ModEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in activeMods.Where(m => m.IsActive))
        {
            var relFiles = FileService.GetModRelativeFiles(mod.FolderPath);
            foreach (var relPath in relFiles)
            {
                if (!fileToMods.TryGetValue(relPath, out var owners))
                {
                    owners = [];
                    fileToMods[relPath] = owners;
                }
                owners.Add(mod);
            }
        }

        // For each file owned by more than one mod, determine winner and losers
        foreach (var (relPath, owners) in fileToMods)
        {
            if (owners.Count < 2)
                continue;

            var winner = owners.OrderByDescending(m => m.LoadOrder).First();

            foreach (var loser in owners.Where(m => m != winner))
            {
                var fileName = Path.GetFileName(relPath);
                var subFolder = Path.GetDirectoryName(relPath) ?? string.Empty;

                conflicts.Add(new ConflictInfo
                {
                    FileName = fileName,
                    SubFolder = subFolder,
                    RelativePath = relPath,
                    WinnerModName = winner.ModName,
                    LoserModName = loser.ModName,
                    WinnerLoadOrder = winner.LoadOrder,
                    LoserLoadOrder = loser.LoadOrder
                });
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Builds the final "winning file map" for all active mods.
    /// Key = relative file path (e.g. "Weapon\weapon_data.bin"), Value = mod that wins.
    /// For each file, the mod with the highest load order among all owners wins.
    /// </summary>
    public static Dictionary<string, ModEntry> BuildWinnerMap(IList<ModEntry> allMods)
    {
        var winnerMap = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in allMods.Where(m => m.IsActive).OrderBy(m => m.LoadOrder))
        {
            var relFiles = FileService.GetModRelativeFiles(mod.FolderPath);
            foreach (var relPath in relFiles)
            {
                // Later (higher load order) mods overwrite earlier ones
                winnerMap[relPath] = mod;
            }
        }

        return winnerMap;
    }

    /// <summary>
    /// Updates conflict/active status properties on every mod entry using load-order resolution.
    /// No blocking dialogs — just visual indicators.
    /// </summary>
    public void UpdateConflictStatuses(IList<ModEntry> entries)
    {
        var activeMods = entries.Where(m => m.IsActive).ToList();
        var conflicts = DetectAllConflicts(entries);

        // Collect which mods win and which lose at least one conflict
        var winners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var losers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conflictPartners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in conflicts)
        {
            winners.Add(c.WinnerModName);
            losers.Add(c.LoserModName);

            // Track who conflicts with whom
            if (!conflictPartners.TryGetValue(c.WinnerModName, out var wp))
            {
                wp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                conflictPartners[c.WinnerModName] = wp;
            }
            wp.Add(c.LoserModName);

            if (!conflictPartners.TryGetValue(c.LoserModName, out var lp))
            {
                lp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                conflictPartners[c.LoserModName] = lp;
            }
            lp.Add(c.WinnerModName);
        }

        foreach (var entry in entries)
        {
            entry.ConflictsWith.Clear();

            if (!entry.IsActive)
            {
                entry.HasConflict = false;
                entry.Status = "Inactive";
                continue;
            }

            bool isWinner = winners.Contains(entry.ModName);
            bool isLoser = losers.Contains(entry.ModName);

            if (conflictPartners.TryGetValue(entry.ModName, out var partners))
            {
                foreach (var p in partners)
                    entry.ConflictsWith.Add(p);
            }

            entry.HasConflict = isWinner || isLoser;

            if (isLoser && !isWinner)
            {
                // This mod loses ALL its conflicts — fully overridden
                entry.Status = "⚠️ Overridden";
            }
            else if (isWinner && isLoser)
            {
                // Wins some, loses some
                entry.Status = "⚠️ Partial";
            }
            else if (isWinner)
            {
                entry.Status = "✅ Wins";
            }
            else
            {
                entry.Status = "Active";
            }
        }
    }
}
