using System.IO;
using System.IO.Compression;
using System.Text.Json;
using EDF6ModLoaderWpf.Models;

namespace EDF6ModLoaderWpf.Services;

/// <summary>
/// Low-level file system operations: folder creation, scanning, copying, deleting.
/// </summary>
public sealed class FileService
{
    /// <summary>
    /// Standard subfolders expected inside the game's Mods directory.
    /// </summary>
    public static readonly string[] StandardSubfolders =
    [
        "DEFAULTPACKAGE", "MainScript", "Mission", "Object", "Patches", "Plugins", "Weapon"
    ];

    private static readonly HashSet<string> StandardSubfolderSet =
        new(StandardSubfolders, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> HighRiskSubfolderSet =
        new(["DEFAULTPACKAGE", "MainScript", "Plugins"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> IgnoredImportFileNames =
        new([".DS_Store", "desktop.ini", "Thumbs.db"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ReservedRootFileNames =
        new(["active_mods.json"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ReservedRootFolderNames =
        new(["backups"], StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const int MaxRetries = 5;
    private const int RetryDelayMs = 200;

    /// <summary>
    /// Ensures [GameRoot]\Mods\ and all standard subfolders exist.
    /// Returns true when any folder was newly created.
    /// </summary>
    public bool EnsureModsFolderStructure(string gameRootDir)
    {
        var modsDir = Path.Combine(gameRootDir, "Mods");
        bool created = false;

        if (!Directory.Exists(modsDir))
        {
            Directory.CreateDirectory(modsDir);
            created = true;
        }

        foreach (var sub in StandardSubfolders)
        {
            var subPath = Path.Combine(modsDir, sub);
            if (!Directory.Exists(subPath))
            {
                Directory.CreateDirectory(subPath);
                created = true;
            }
        }

        return created;
    }

    /// <summary>
    /// Enumerates all mod folders in the mods library directory.
    /// Each direct child folder is treated as one mod.
    /// </summary>
    public List<ModEntry> ScanModsLibrary(string modsLibraryDir)
    {
        var entries = new List<ModEntry>();
        if (!Directory.Exists(modsLibraryDir))
            return entries;

        foreach (var dir in Directory.GetDirectories(modsLibraryDir))
        {
            var dirInfo = new DirectoryInfo(dir);
            var entry = new ModEntry
            {
                ModName = dirInfo.Name,
                FolderPath = dir,
                DateAdded = dirInfo.LastWriteTime
            };

            // Determine which standard subfolders this mod uses
            var usedSubs = StandardSubfolders
                .Where(s => Directory.Exists(Path.Combine(dir, s)))
                .ToList();
            entry.Subfolders = usedSubs.Count > 0 ? string.Join(", ", usedSubs) : "(root files)";

            // Try to read mod_info.json
            var info = TryReadModInfo(dir);
            if (info is not null)
            {
                entry.Info = info;
                entry.Description = string.IsNullOrWhiteSpace(info.Description)
                    ? "No info"
                    : info.Description;
            }

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Annotates scanned mods with compatibility and layout warnings for the active game.
    /// </summary>
    public void AnnotateModWarnings(IList<ModEntry> entries, string activeGameId)
    {
        foreach (var entry in entries)
        {
            AnnotateModWarning(entry, activeGameId);
        }
    }

    /// <summary>
    /// Imports a mod from a .zip archive into the mods library.
    /// Normalizes single wrapper folders and creates fallback metadata when missing.
    /// </summary>
    public async Task<ModImportPreview> PreviewModArchiveAsync(string archivePath, string modsLibraryDir, string activeGameId)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive file was not found.", archivePath);

        var extension = Path.GetExtension(archivePath);
        if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .zip archives are supported right now.");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"EDF6ModImportPreview_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempRoot);
            ZipFile.ExtractToDirectory(archivePath, tempRoot);

            var importRoot = ResolveImportRoot(tempRoot);
            var preparation = BuildImportPreparation(importRoot, modsLibraryDir, activeGameId,
                Path.GetFileNameWithoutExtension(archivePath));

            return CreateImportPreview(preparation, Path.GetFileName(archivePath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    public async Task<ModImportResult> ImportModArchiveAsync(
        string archivePath,
        string modsLibraryDir,
        string activeGameId,
        ModImportMode importMode = ModImportMode.ImportAsCopy)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive file was not found.", archivePath);

        var extension = Path.GetExtension(archivePath);
        if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .zip archives are supported right now.");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"EDF6ModImport_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempRoot);
            ZipFile.ExtractToDirectory(archivePath, tempRoot);

            var importRoot = ResolveImportRoot(tempRoot);
            return await ImportModFromDirectoryAsync(importRoot, modsLibraryDir, activeGameId,
                Path.GetFileNameWithoutExtension(archivePath), importMode);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// Imports a mod from an existing folder into the mods library.
    /// </summary>
    public Task<ModImportPreview> PreviewModFolderAsync(string sourceFolderPath, string modsLibraryDir, string activeGameId)
    {
        if (!Directory.Exists(sourceFolderPath))
            throw new DirectoryNotFoundException("Source mod folder was not found.");

        var normalizedSource = Path.GetFullPath(sourceFolderPath);
        var normalizedLibrary = Path.GetFullPath(modsLibraryDir);
        if (IsSameOrChildPath(normalizedSource, normalizedLibrary))
            throw new InvalidOperationException("The selected folder is already inside the mods library.");

        var importRoot = ResolveImportRoot(sourceFolderPath);
        var preparation = BuildImportPreparation(importRoot, modsLibraryDir, activeGameId,
            Path.GetFileName(importRoot));

        return Task.FromResult(CreateImportPreview(preparation, Path.GetFileName(sourceFolderPath)));
    }

    public async Task<ModImportResult> ImportModFolderAsync(
        string sourceFolderPath,
        string modsLibraryDir,
        string activeGameId,
        ModImportMode importMode = ModImportMode.ImportAsCopy)
    {
        if (!Directory.Exists(sourceFolderPath))
            throw new DirectoryNotFoundException("Source mod folder was not found.");

        var normalizedSource = Path.GetFullPath(sourceFolderPath);
        var normalizedLibrary = Path.GetFullPath(modsLibraryDir);
        if (IsSameOrChildPath(normalizedSource, normalizedLibrary))
            throw new InvalidOperationException("The selected folder is already inside the mods library.");

        var importRoot = ResolveImportRoot(sourceFolderPath);
        return await ImportModFromDirectoryAsync(importRoot, modsLibraryDir, activeGameId,
            Path.GetFileName(importRoot), importMode);
    }

    /// <summary>
    /// Collects all relative file paths inside a mod folder.
    /// Paths are relative to the mod folder root (e.g. "Weapon\file.bin").
    /// </summary>
    public static List<string> GetModRelativeFiles(string modFolderPath)
    {
        if (!Directory.Exists(modFolderPath))
            return [];

        return Directory.GetFiles(modFolderPath, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(modFolderPath, f))
            .Where(IsModPayloadRelativeFile)
            .ToList();
    }

    /// <summary>
    /// Copies all files from a mod folder into [GameRoot]\Mods\, preserving subfolder structure.
    /// Returns the list of relative paths that were copied.
    /// </summary>
    public async Task<List<string>> CopyModFilesAsync(string modFolderPath, string gameRootDir)
    {
        var modsDir = Path.Combine(gameRootDir, "Mods");
        var relativeFiles = GetModRelativeFiles(modFolderPath);

        foreach (var relPath in relativeFiles)
        {
            var source = Path.Combine(modFolderPath, relPath);
            var destination = Path.Combine(modsDir, relPath);

            var destDir = Path.GetDirectoryName(destination);
            if (destDir is not null)
                Directory.CreateDirectory(destDir);

            for (int i = 0; i < MaxRetries; i++)
            {
                try
                {
                    await Task.Run(() => File.Copy(source, destination, overwrite: true));
                    break;
                }
                catch (IOException) when (i < MaxRetries - 1)
                {
                    await Task.Delay(RetryDelayMs * (i + 1));
                }
            }
        }

        return relativeFiles;
    }

    /// <summary>
    /// Deletes the specified relative files from [GameRoot]\Mods\.
    /// Only deletes files that are NOT owned by another active mod.
    /// </summary>
    public async Task DeleteModFilesAsync(
        IEnumerable<string> relativeFiles,
        string gameRootDir,
        HashSet<string> filesOwnedByOtherMods)
    {
        var modsDir = Path.Combine(gameRootDir, "Mods");

        foreach (var relPath in relativeFiles)
        {
            if (filesOwnedByOtherMods.Contains(relPath))
                continue;

            var fullPath = Path.Combine(modsDir, relPath);
            if (!File.Exists(fullPath))
                continue;

            for (int i = 0; i < MaxRetries; i++)
            {
                try
                {
                    File.Delete(fullPath);
                    break;
                }
                catch (IOException) when (i < MaxRetries - 1)
                {
                    await Task.Delay(RetryDelayMs * (i + 1));
                }
            }
        }
    }

    /// <summary>
    /// Loads the active mods registry from the given directory's active_mods.json.
    /// For multi-game support, this is the active game's [GameRoot]\Mods folder.
    /// </summary>
    public async Task<ActiveModsRegistry> LoadRegistryAsync(string registryDir)
    {
        var path = Path.Combine(registryDir, "active_mods.json");
        if (!File.Exists(path))
            return new ActiveModsRegistry();

        for (int i = 0; i < 5; i++)
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var registry = JsonSerializer.Deserialize<ActiveModsRegistry>(json, JsonOptions);
                return registry ?? new ActiveModsRegistry();
            }
            catch (IOException) when (i < 4)
            {
                await Task.Delay(200 * (i + 1));
            }
            catch (UnauthorizedAccessException) when (i < 4)
            {
                await Task.Delay(200 * (i + 1));
            }
            catch (JsonException ex)
            {
                await SettingsService.LogErrorAsync(ex);
                throw new InvalidOperationException(
                    $"The active mods registry is malformed and was not overwritten: {path}",
                    ex);
            }
            catch (NotSupportedException ex)
            {
                await SettingsService.LogErrorAsync(ex);
                throw new InvalidOperationException(
                    $"The active mods registry has unsupported JSON content and was not overwritten: {path}",
                    ex);
            }
        }

        return new ActiveModsRegistry();
    }

    /// <summary>
    /// Saves the active mods registry to the given directory's active_mods.json.
    /// </summary>
    public async Task SaveRegistryAsync(string registryDir, ActiveModsRegistry registry)
    {
        Directory.CreateDirectory(registryDir);

        var path = Path.Combine(registryDir, "active_mods.json");
        var json = JsonSerializer.Serialize(registry, JsonOptions);

        for (int i = 0; i < 5; i++)
        {
            try
            {
                await File.WriteAllTextAsync(path, json);
                return;
            }
            catch (IOException) when (i < 4)
            {
                await Task.Delay(200 * (i + 1));
            }
        }
    }

    /// <summary>
    /// Copies registry state from a legacy directory into the canonical registry directory when needed.
    /// Existing canonical registry files are never overwritten.
    /// </summary>
    public async Task MigrateRegistryAsync(string legacyRegistryDir, string registryDir)
    {
        if (string.IsNullOrWhiteSpace(legacyRegistryDir) || string.IsNullOrWhiteSpace(registryDir))
            return;

        var legacyFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyRegistryDir));
        var registryFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(registryDir));
        if (string.Equals(legacyFullPath, registryFullPath, StringComparison.OrdinalIgnoreCase))
            return;

        var legacyRegistryPath = Path.Combine(legacyFullPath, "active_mods.json");
        var registryPath = Path.Combine(registryFullPath, "active_mods.json");
        if (!File.Exists(registryPath) && File.Exists(legacyRegistryPath))
        {
            await CopyFileWithRetryAsync(legacyRegistryPath, registryPath);
        }

        var legacyBackupsPath = Path.Combine(legacyFullPath, "backups");
        var backupsPath = Path.Combine(registryFullPath, "backups");
        if (!Directory.Exists(backupsPath) && Directory.Exists(legacyBackupsPath))
        {
            await CopyDirectoryAsync(legacyBackupsPath, backupsPath);
        }
    }

    /// <summary>
    /// Writes or updates mod_info.json inside a mod folder.
    /// </summary>
    public async Task SaveModInfoAsync(string modFolderPath, ModInfo info)
    {
        var path = Path.Combine(modFolderPath, "mod_info.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, info, JsonOptions);
    }

    private async Task<ModImportResult> ImportModFromDirectoryAsync(
        string importRoot,
        string modsLibraryDir,
        string activeGameId,
        string fallbackName,
        ModImportMode importMode)
    {
        Directory.CreateDirectory(modsLibraryDir);

        var preparation = BuildImportPreparation(importRoot, modsLibraryDir, activeGameId, fallbackName);
        var destinationFolderPath = importMode == ModImportMode.ReplaceExisting
            ? preparation.ExactMatchFolderPath
            : preparation.DestinationFolderPath;

        if (string.IsNullOrWhiteSpace(destinationFolderPath))
            throw new InvalidOperationException("The selected import mode is not available for this source.");

        if (importMode == ModImportMode.ReplaceExisting)
            await DeleteDirectoryWithRetryAsync(destinationFolderPath);

        await CopyDirectoryAsync(importRoot, destinationFolderPath);

        bool createdMetadata = false;
        var destinationInfoPath = Path.Combine(destinationFolderPath, "mod_info.json");
        if (!File.Exists(destinationInfoPath))
        {
            createdMetadata = true;
            await SaveModInfoAsync(destinationFolderPath, new ModInfo
            {
                Name = Path.GetFileName(destinationFolderPath),
                Description = "Imported via EDF Mod Manager.",
                GameVersion = activeGameId,
                Files = GetModRelativeFiles(destinationFolderPath),
                DateAdded = DateTime.Now.ToString("yyyy-MM-dd")
            });
        }

        return new ModImportResult
        {
            ModName = Path.GetFileName(destinationFolderPath),
            DestinationFolderPath = destinationFolderPath,
            ImportedFileCount = GetModRelativeFiles(destinationFolderPath).Count,
            CreatedMetadata = createdMetadata,
            ReplacedExisting = importMode == ModImportMode.ReplaceExisting
        };
    }

    private static ModImportPreview CreateImportPreview(ImportPreparation preparation, string sourceDisplayName)
    {
        return new ModImportPreview
        {
            SourceDisplayName = sourceDisplayName,
            ProposedModName = Path.GetFileName(preparation.DestinationFolderPath),
            DestinationFolderPath = preparation.DestinationFolderPath,
            ImportedFileCount = preparation.RelativeFiles.Count,
            HasMetadata = preparation.Info is not null,
            WillCreateMetadata = preparation.Info is null,
            DetectedGameVersion = preparation.Info?.GameVersion ?? string.Empty,
            HasNameCollision = preparation.HasNameCollision,
            CanReplaceExisting = !string.IsNullOrWhiteSpace(preparation.ExactMatchFolderPath),
            ReplaceTargetFolderName = preparation.ExactMatchFolderName,
            DuplicateCandidates = [.. preparation.DuplicateCandidates],
            Warnings = [.. preparation.Warnings]
        };
    }

    private static async Task CopyDirectoryAsync(string sourceDirectoryPath, string destinationDirectoryPath)
    {
        Directory.CreateDirectory(destinationDirectoryPath);

        foreach (var directory in Directory.GetDirectories(sourceDirectoryPath))
        {
            var directoryName = Path.GetFileName(directory);
            if (string.Equals(directoryName, "__MACOSX", StringComparison.OrdinalIgnoreCase))
                continue;

            var destinationSubdirectory = Path.Combine(destinationDirectoryPath, directoryName);
            await CopyDirectoryAsync(directory, destinationSubdirectory);
        }

        foreach (var filePath in Directory.GetFiles(sourceDirectoryPath))
        {
            if (IgnoredImportFileNames.Contains(Path.GetFileName(filePath)))
                continue;

            var destinationFilePath = Path.Combine(destinationDirectoryPath, Path.GetFileName(filePath));
            await CopyFileWithRetryAsync(filePath, destinationFilePath);
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

    private static async Task DeleteDirectoryWithRetryAsync(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return;

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

    private static string ResolveImportRoot(string sourceRootPath)
    {
        var currentPath = sourceRootPath;

        while (true)
        {
            var childDirectories = Directory.GetDirectories(currentPath)
                .Where(d => !string.Equals(Path.GetFileName(d), "__MACOSX", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var visibleFiles = Directory.GetFiles(currentPath)
                .Where(f => !IgnoredImportFileNames.Contains(Path.GetFileName(f)))
                .ToList();

            bool hasRecognizedContent = visibleFiles.Count > 0 ||
                                        File.Exists(Path.Combine(currentPath, "mod_info.json")) ||
                                        childDirectories.Any(d => StandardSubfolderSet.Contains(Path.GetFileName(d)));

            if (hasRecognizedContent || childDirectories.Count != 1)
                return currentPath;

            currentPath = childDirectories[0];
        }
    }

    private static ImportPreparation BuildImportPreparation(
        string importRoot,
        string modsLibraryDir,
        string activeGameId,
        string fallbackName)
    {
        var relativeFiles = GetModRelativeFiles(importRoot);
        if (relativeFiles.Count == 0)
            throw new InvalidOperationException("The selected source does not contain any importable mod files.");

        var info = TryReadModInfo(importRoot);
        var preferredName = !string.IsNullOrWhiteSpace(info?.Name)
            ? info.Name
            : fallbackName;

        var sanitizedName = SanitizeFolderName(preferredName);
        var destinationFolderPath = GetUniqueDestinationFolderPath(modsLibraryDir, preferredName);
        var exactMatchFolderPath = FindExactMatchFolderPath(modsLibraryDir, sanitizedName);
        var duplicateCandidates = FindDuplicateCandidates(modsLibraryDir, sanitizedName, info);
        var warnings = BuildImportWarnings(relativeFiles, info, activeGameId, duplicateCandidates,
            hasNameCollision: !string.Equals(Path.GetFileName(destinationFolderPath), sanitizedName, StringComparison.OrdinalIgnoreCase));

        return new ImportPreparation
        {
            RelativeFiles = relativeFiles,
            Info = info,
            DestinationFolderPath = destinationFolderPath,
            ExactMatchFolderPath = exactMatchFolderPath,
            ExactMatchFolderName = sanitizedName,
            DuplicateCandidates = duplicateCandidates,
            Warnings = warnings,
            HasNameCollision = !string.Equals(Path.GetFileName(destinationFolderPath), sanitizedName, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string? FindExactMatchFolderPath(string modsLibraryDir, string sanitizedName)
    {
        if (!Directory.Exists(modsLibraryDir))
            return null;

        return Directory.GetDirectories(modsLibraryDir)
            .FirstOrDefault(directory =>
                string.Equals(Path.GetFileName(directory), sanitizedName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetUniqueDestinationFolderPath(string modsLibraryDir, string preferredName)
    {
        var sanitizedName = SanitizeFolderName(preferredName);
        var destinationPath = Path.Combine(modsLibraryDir, sanitizedName);
        int suffix = 2;

        while (Directory.Exists(destinationPath))
        {
            destinationPath = Path.Combine(modsLibraryDir, $"{sanitizedName} ({suffix})");
            suffix++;
        }

        return destinationPath;
    }

    private static string SanitizeFolderName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(ch => invalidCharacters.Contains(ch) ? '_' : ch)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "Imported Mod" : sanitized;
    }

    private static bool IsSameOrChildPath(string path, string parentPath)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(path);
        var normalizedParent = Path.TrimEndingDirectorySeparator(parentPath);

        return normalizedPath.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static ModInfo? TryReadModInfo(string modFolderPath)
    {
        var infoPath = Path.Combine(modFolderPath, "mod_info.json");
        if (!File.Exists(infoPath))
            return null;

        try
        {
            var json = File.ReadAllText(infoPath);
            return JsonSerializer.Deserialize<ModInfo>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsModPayloadRelativeFile(string relativePath)
    {
        var normalizedPath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var segments = normalizedPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return false;

        if (string.Equals(segments[^1], "mod_info.json", StringComparison.OrdinalIgnoreCase))
            return false;

        if (segments.Length == 1 && ReservedRootFileNames.Contains(segments[0]))
            return false;

        return !ReservedRootFolderNames.Contains(segments[0]);
    }

    private static List<string> FindDuplicateCandidates(string modsLibraryDir, string sanitizedName, ModInfo? importInfo)
    {
        var duplicates = new List<string>();
        if (!Directory.Exists(modsLibraryDir))
            return duplicates;

        foreach (var existingDirectory in Directory.GetDirectories(modsLibraryDir))
        {
            var directoryName = Path.GetFileName(existingDirectory);
            bool sameFolderName = string.Equals(directoryName, sanitizedName, StringComparison.OrdinalIgnoreCase);
            bool sameMetadataName = false;

            if (!sameFolderName && !string.IsNullOrWhiteSpace(importInfo?.Name))
            {
                var existingInfo = TryReadModInfo(existingDirectory);
                sameMetadataName = !string.IsNullOrWhiteSpace(existingInfo?.Name) &&
                                   string.Equals(existingInfo.Name, importInfo.Name, StringComparison.OrdinalIgnoreCase);
            }

            if (sameFolderName || sameMetadataName)
                duplicates.Add(directoryName);
        }

        return duplicates;
    }

    private static List<string> BuildImportWarnings(
        List<string> relativeFiles,
        ModInfo? info,
        string activeGameId,
        List<string> duplicateCandidates,
        bool hasNameCollision)
    {
        var warnings = new List<string>();

        if (info is null)
        {
            warnings.Add("mod_info.json is missing and will be created during import.");
        }
        else if (string.IsNullOrWhiteSpace(info.GameVersion))
        {
            warnings.Add("mod_info.json does not declare gameVersion.");
        }
        else if (!string.Equals(info.GameVersion, activeGameId, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"mod_info.json targets {info.GameVersion}, not {activeGameId}.");
        }

        var topLevelFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasRootFiles = false;
        bool hasUnknownFolders = false;

        foreach (var relPath in relativeFiles)
        {
            var normalizedPath = relPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var segments = normalizedPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length <= 1)
            {
                hasRootFiles = true;
                continue;
            }

            var topLevelFolder = segments[0];
            topLevelFolders.Add(topLevelFolder);

            if (!StandardSubfolderSet.Contains(topLevelFolder))
                hasUnknownFolders = true;
        }

        if (hasRootFiles)
            warnings.Add("Contains files at the mod root.");

        if (hasUnknownFolders)
            warnings.Add("Contains files outside the standard EDF mod folders.");

        if (topLevelFolders.Overlaps(HighRiskSubfolderSet))
            warnings.Add("Touches high-risk EDF folders: DEFAULTPACKAGE, MainScript, or Plugins.");

        if (duplicateCandidates.Count > 0)
            warnings.Add($"Looks similar to existing library entries: {string.Join(", ", duplicateCandidates)}.");

        if (hasNameCollision)
            warnings.Add("Destination folder name already exists and will be renamed during import.");

        return warnings;
    }

    private sealed class ImportPreparation
    {
        public List<string> RelativeFiles { get; init; } = [];

        public ModInfo? Info { get; init; }

        public string DestinationFolderPath { get; init; } = string.Empty;

        public string? ExactMatchFolderPath { get; init; }

        public string ExactMatchFolderName { get; init; } = string.Empty;

        public List<string> DuplicateCandidates { get; init; } = [];

        public List<string> Warnings { get; init; } = [];

        public bool HasNameCollision { get; init; }
    }

    private static void AnnotateModWarning(ModEntry entry, string activeGameId)
    {
        var warnings = new List<string>();
        string compatibilityStatus = ModEntry.CompatibilityCompatible;
        string riskLevel = ModEntry.RiskNone;
        bool isHighRisk = false;

        if (entry.Info is null)
        {
            compatibilityStatus = ModEntry.CompatibilityUnknown;
            warnings.Add("Missing mod_info.json metadata.");
        }
        else if (string.IsNullOrWhiteSpace(entry.Info.GameVersion))
        {
            compatibilityStatus = ModEntry.CompatibilityUnknown;
            warnings.Add("mod_info.json does not declare gameVersion.");
        }
        else if (!string.Equals(entry.Info.GameVersion, activeGameId, StringComparison.OrdinalIgnoreCase))
        {
            compatibilityStatus = ModEntry.CompatibilityMismatch;
            warnings.Add($"Marked for {entry.Info.GameVersion}, not {activeGameId}.");
        }

        var relativeFiles = GetModRelativeFiles(entry.FolderPath);
        var topLevelFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasRootFiles = false;
        bool hasUnknownFolders = false;

        foreach (var relPath in relativeFiles)
        {
            var normalizedPath = relPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var segments = normalizedPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length <= 1)
            {
                hasRootFiles = true;
                continue;
            }

            var topLevelFolder = segments[0];
            topLevelFolders.Add(topLevelFolder);

            if (!StandardSubfolderSet.Contains(topLevelFolder))
                hasUnknownFolders = true;
        }

        if (hasRootFiles)
        {
            warnings.Add("Contains files at the mod root.");
            riskLevel = PromoteRisk(riskLevel, ModEntry.RiskMedium);
        }

        if (hasUnknownFolders)
        {
            warnings.Add("Contains files outside the standard EDF mod folders.");
            riskLevel = PromoteRisk(riskLevel, ModEntry.RiskMedium);
        }

        if (topLevelFolders.Overlaps(HighRiskSubfolderSet))
        {
            warnings.Add("Touches high-risk EDF folders: DEFAULTPACKAGE, MainScript, or Plugins.");
            riskLevel = ModEntry.RiskHigh;
            isHighRisk = true;
        }

        entry.CompatibilityStatus = compatibilityStatus;
        entry.RiskLevel = riskLevel;
        entry.IsHighRisk = isHighRisk;
        entry.HasWarnings = warnings.Count > 0;
        entry.WarningSummary = warnings.Count == 0
            ? string.Empty
            : compatibilityStatus == ModEntry.CompatibilityMismatch
                ? "Game mismatch"
                : riskLevel == ModEntry.RiskHigh
                    ? "High risk"
                    : "Check mod";
        entry.WarningDetails = warnings.Count == 0
            ? "No warnings detected."
            : string.Join(Environment.NewLine, warnings);
    }

    private static string PromoteRisk(string currentRisk, string nextRisk)
    {
        if (string.Equals(currentRisk, ModEntry.RiskHigh, StringComparison.Ordinal))
            return currentRisk;

        if (string.Equals(nextRisk, ModEntry.RiskHigh, StringComparison.Ordinal))
            return nextRisk;

        if (string.Equals(currentRisk, ModEntry.RiskMedium, StringComparison.Ordinal))
            return currentRisk;

        return nextRisk;
    }
}
