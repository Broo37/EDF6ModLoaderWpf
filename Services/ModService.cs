using System.IO;
using System.ComponentModel;
using EDF6ModLoaderWpf.Models;

namespace EDF6ModLoaderWpf.Services;

/// <summary>
/// High-level service for enabling/disabling mods using the load order system.
/// Coordinates FileService, ConflictService, LoadOrderService, and the active mods registry.
/// </summary>
public sealed class ModService
{
    private readonly BackupService _backupService;
    private readonly FileService _fileService;
    private readonly ConflictService _conflictService;
    private readonly LoadOrderService _loadOrderService;

    public ModService(BackupService backupService, FileService fileService, ConflictService conflictService, LoadOrderService loadOrderService)
    {
        _backupService = backupService;
        _fileService = fileService;
        _conflictService = conflictService;
        _loadOrderService = loadOrderService;
    }

    /// <summary>
    /// Master apply: clears game Mods folder of previously managed files,
    /// then copies only winning files based on load order. Saves registry and updates statuses.
    /// </summary>
    public async Task ApplyAllModsAsync(
        IList<ModEntry> mods,
        string gameRootDir,
        string registryDir,
        IProgress<string>? progress = null,
        string? activePresetName = null)
    {
        // Step 1: Build the winner map
        progress?.Report("Building conflict map...");
        var winnerMap = ConflictService.BuildWinnerMap(mods);

        await _backupService.CreateLastApplyBackupAsync(gameRootDir, registryDir, progress);

        // Step 2: Clear previously installed files
        progress?.Report("Cleaning game Mods folder...");
        await CleanManagedFilesAsync(gameRootDir, registryDir);

        // Step 3: Copy only winning files
        var modsDir = Path.Combine(gameRootDir, "Mods");
        int total = winnerMap.Count;
        int current = 0;

        foreach (var (relPath, winnerMod) in winnerMap)
        {
            current++;
            progress?.Report($"Copying ({current}/{total}): {relPath}");

            var source = Path.Combine(winnerMod.FolderPath, relPath);
            var destination = Path.Combine(modsDir, relPath);

            var destDir = Path.GetDirectoryName(destination);
            if (destDir is not null)
                Directory.CreateDirectory(destDir);

            if (File.Exists(source))
            {
                await CopyWithRetryAsync(source, destination);
            }
        }

        // Step 4: Save updated registry
        progress?.Report("Saving registry...");
        await _loadOrderService.SaveToRegistryAsync(mods, registryDir, activePresetName);

        // Step 5: Update conflict statuses on the UI
        progress?.Report("Updating statuses...");
        _conflictService.UpdateConflictStatuses(mods);
    }

    /// <summary>
    /// Deletes all files that were previously copied by this app (tracked in active_mods.json).
    /// </summary>
    private async Task CleanManagedFilesAsync(string gameRootDir, string registryDir)
    {
        var registry = await _fileService.LoadRegistryAsync(registryDir);
        var modsDir = Path.Combine(gameRootDir, "Mods");

        foreach (var modEntry in registry.ActiveMods)
        {
            foreach (var relPath in modEntry.Files)
            {
                var fullPath = Path.Combine(modsDir, relPath);
                if (File.Exists(fullPath))
                    await DeleteWithRetryAsync(fullPath);
            }
        }
    }

    private const int MaxRetries = 5;
    private const int RetryDelayMs = 200;

    /// <summary>
    /// Copies a file with retry logic to handle transient file locks (antivirus, indexer, etc.).
    /// On the final attempt the IOException filter does not match, so the exception propagates to the caller.
    /// </summary>
    private static async Task CopyWithRetryAsync(string source, string destination)
    {
        for (int i = 0; i < MaxRetries; i++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (i < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (i + 1));
            }
            // Final attempt: IOException propagates to caller — no silent swallow.
        }
    }

    /// <summary>
    /// Deletes a file with retry logic to handle transient file locks.
    /// On the final attempt the IOException filter does not match, so the exception propagates to the caller.
    /// </summary>
    private static async Task DeleteWithRetryAsync(string path)
    {
        for (int i = 0; i < MaxRetries; i++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (i < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (i + 1));
            }
            // Final attempt: IOException propagates to caller — no silent swallow.
        }
    }

    /// <summary>
    /// Updates conflict / active statuses for all mod entries (non-blocking, no dialogs).
    /// </summary>
    public void RefreshStatuses(IList<ModEntry> entries)
    {
        _conflictService.UpdateConflictStatuses(entries);
    }

    /// <summary>
    /// Gets the list of all current conflicts for display in the conflict report panel.
    /// </summary>
    public List<ConflictInfo> GetConflictReport(IList<ModEntry> mods)
    {
        return _conflictService.DetectAllConflicts(mods);
    }
}
