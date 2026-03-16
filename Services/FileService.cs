using System.IO;
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

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
            var infoPath = Path.Combine(dir, "mod_info.json");
            if (File.Exists(infoPath))
            {
                try
                {
                    var json = File.ReadAllText(infoPath);
                    var info = JsonSerializer.Deserialize<ModInfo>(json, JsonOptions);
                    if (info is not null)
                    {
                        entry.Info = info;
                        entry.Description = string.IsNullOrWhiteSpace(info.Description)
                            ? "No info"
                            : info.Description;
                    }
                }
                catch
                {
                    // Ignore malformed mod_info.json
                }
            }

            entries.Add(entry);
        }

        return entries;
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
            .Where(f => !f.EndsWith("mod_info.json", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(modFolderPath, f))
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

            // Ensure the target directory exists
            var destDir = Path.GetDirectoryName(destination);
            if (destDir is not null)
                Directory.CreateDirectory(destDir);

            // Copy with overwrite to handle conflicts gracefully
            File.Copy(source, destination, overwrite: true);
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

        await Task.Run(() =>
        {
            foreach (var relPath in relativeFiles)
            {
                // Skip files that belong to another active mod
                if (filesOwnedByOtherMods.Contains(relPath))
                    continue;

                var fullPath = Path.Combine(modsDir, relPath);
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
        });
    }

    /// <summary>
    /// Loads the active mods registry from the given directory's active_mods.json.
    /// For multi-game support, this is typically %AppData%\EDFModManager\{gameId}\.
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
            catch
            {
                return new ActiveModsRegistry();
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
    /// Writes or updates mod_info.json inside a mod folder.
    /// </summary>
    public async Task SaveModInfoAsync(string modFolderPath, ModInfo info)
    {
        var path = Path.Combine(modFolderPath, "mod_info.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, info, JsonOptions);
    }
}
