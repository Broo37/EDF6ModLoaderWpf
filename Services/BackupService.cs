using System.IO;
using System.Text.Json;
using EDF6ModLoaderWpf.Models;

namespace EDF6ModLoaderWpf.Services;

/// <summary>
/// Persists the last deployed managed-file state before apply so the user can restore it later.
/// </summary>
public sealed class BackupService
{
    private const string BackupFolderName = "backups";
    private const string LastApplyFolderName = "last-apply";
    private const string BackupFilesFolderName = "files";
    private const string BackupManifestFileName = "last_apply_backup.json";
    private const int MaxRetries = 5;
    private const int RetryDelayMs = 200;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly FileService _fileService;

    public BackupService(FileService fileService)
    {
        _fileService = fileService;
    }

    public bool HasLastApplyBackup(string registryDir)
    {
        if (string.IsNullOrWhiteSpace(registryDir))
            return false;

        return File.Exists(GetBackupManifestPath(registryDir));
    }

    public async Task CreateLastApplyBackupAsync(string gameRootDir, string registryDir, IProgress<string>? progress = null)
    {
        var registry = await _fileService.LoadRegistryAsync(registryDir);
        var backupRoot = GetBackupRoot(registryDir);
        var backupFilesRoot = GetBackupFilesRoot(registryDir);

        if (Directory.Exists(backupRoot))
            await DeleteDirectoryWithRetryAsync(backupRoot);

        Directory.CreateDirectory(backupFilesRoot);

        progress?.Report("Creating backup snapshot...");

        var modsDir = Path.Combine(gameRootDir, "Mods");
        var backedUpFiles = new List<string>();

        foreach (var relPath in registry.ActiveMods
                     .SelectMany(mod => mod.Files)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var sourcePath = Path.Combine(modsDir, relPath);
            if (!File.Exists(sourcePath))
                continue;

            var destinationPath = Path.Combine(backupFilesRoot, relPath);
            await CopyFileWithRetryAsync(sourcePath, destinationPath);
            backedUpFiles.Add(relPath);
        }

        var snapshot = new ApplyBackupSnapshot
        {
            CreatedAt = DateTime.Now,
            ActivePresetName = registry.ActivePresetName,
            ActiveMods = CloneActiveEntries(registry.ActiveMods),
            ModGroups = new Dictionary<string, string>(registry.ModGroups, StringComparer.OrdinalIgnoreCase),
            BackedUpFiles = backedUpFiles
        };

        await SaveLastApplyBackupAsync(registryDir, snapshot);
    }

    public async Task<ApplyBackupSnapshot> RestoreLastApplyBackupAsync(
        string gameRootDir,
        string registryDir,
        IProgress<string>? progress = null)
    {
        var snapshot = await LoadLastApplyBackupAsync(registryDir)
            ?? throw new InvalidOperationException("No previous apply backup is available for this game.");

        var currentRegistry = await _fileService.LoadRegistryAsync(registryDir);

        progress?.Report("Cleaning current managed files...");
        await DeleteManagedFilesAsync(gameRootDir, currentRegistry);

        progress?.Report("Restoring backup snapshot...");
        await CopyBackupFilesIntoGameAsync(gameRootDir, registryDir);

        currentRegistry.LastUpdated = DateTime.Now;
        currentRegistry.ActivePresetName = snapshot.ActivePresetName;
        currentRegistry.ActiveMods = CloneActiveEntries(snapshot.ActiveMods);
        currentRegistry.ModGroups = new Dictionary<string, string>(snapshot.ModGroups, StringComparer.OrdinalIgnoreCase);

        await _fileService.SaveRegistryAsync(registryDir, currentRegistry);

        return snapshot;
    }

    public async Task<ApplyBackupSnapshot?> LoadLastApplyBackupAsync(string registryDir)
    {
        var manifestPath = GetBackupManifestPath(registryDir);
        if (!File.Exists(manifestPath))
            return null;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath);
                return JsonSerializer.Deserialize<ApplyBackupSnapshot>(json, JsonOptions);
            }
            catch (IOException) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (attempt + 1));
            }
        }

        throw new InvalidOperationException("The last apply backup could not be loaded.");
    }

    private async Task SaveLastApplyBackupAsync(string registryDir, ApplyBackupSnapshot snapshot)
    {
        var backupRoot = GetBackupRoot(registryDir);
        Directory.CreateDirectory(backupRoot);

        var manifestPath = GetBackupManifestPath(registryDir);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                await File.WriteAllTextAsync(manifestPath, json);
                return;
            }
            catch (IOException) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (attempt + 1));
            }
        }
    }

    private async Task DeleteManagedFilesAsync(string gameRootDir, ActiveModsRegistry registry)
    {
        var modsDir = Path.Combine(gameRootDir, "Mods");
        var managedFiles = registry.ActiveMods
            .SelectMany(mod => mod.Files)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var relPath in managedFiles)
        {
            var fullPath = Path.Combine(modsDir, relPath);
            if (!File.Exists(fullPath))
                continue;

            await DeleteFileWithRetryAsync(fullPath);
        }
    }

    private async Task CopyBackupFilesIntoGameAsync(string gameRootDir, string registryDir)
    {
        var backupFilesRoot = GetBackupFilesRoot(registryDir);
        if (!Directory.Exists(backupFilesRoot))
            return;

        var modsDir = Path.Combine(gameRootDir, "Mods");
        foreach (var filePath in Directory.GetFiles(backupFilesRoot, "*", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(backupFilesRoot, filePath);
            var destinationPath = Path.Combine(modsDir, relPath);
            await CopyFileWithRetryAsync(filePath, destinationPath);
        }
    }

    private static async Task CopyFileWithRetryAsync(string sourcePath, string destinationPath)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (destinationDirectory is not null)
            Directory.CreateDirectory(destinationDirectory);

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (attempt + 1));
            }
        }
    }

    private static async Task DeleteFileWithRetryAsync(string filePath)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                File.Delete(filePath);
                return;
            }
            catch (IOException) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (attempt + 1));
            }
        }
    }

    private static async Task DeleteDirectoryWithRetryAsync(string directoryPath)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                Directory.Delete(directoryPath, recursive: true);
                return;
            }
            catch (IOException) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (attempt + 1));
            }
        }
    }

    private static string GetBackupRoot(string registryDir)
        => Path.Combine(registryDir, BackupFolderName, LastApplyFolderName);

    private static string GetBackupFilesRoot(string registryDir)
        => Path.Combine(GetBackupRoot(registryDir), BackupFilesFolderName);

    private static string GetBackupManifestPath(string registryDir)
        => Path.Combine(GetBackupRoot(registryDir), BackupManifestFileName);

    private static List<ActiveModEntry> CloneActiveEntries(IEnumerable<ActiveModEntry> entries)
    {
        return entries.Select(entry => new ActiveModEntry
        {
            Name = entry.Name,
            LoadOrder = entry.LoadOrder,
            Group = entry.Group,
            Files = [.. entry.Files]
        }).ToList();
    }
}