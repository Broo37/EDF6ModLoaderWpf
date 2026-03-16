using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using EDF6ModLoaderWpf.Models;

namespace EDF6ModLoaderWpf.Services;

/// <summary>
/// Loads and saves global application settings and per-game configurations.
/// Global settings: %AppData%\EDFModManager\settings.json
/// Per-game config: %AppData%\EDFModManager\{gameId}\game_config.json
/// </summary>
public sealed class SettingsService
{
    private static readonly string AppDataRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EDFModManager");

    private static readonly string SettingsFilePath =
        Path.Combine(AppDataRoot, "settings.json");

    private static readonly string ErrorLogPath =
        Path.Combine(AppDataRoot, "error.log");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private AppSettings? _cachedSettings;

    /// <summary>
    /// Loads global settings from disk and populates game profiles
    /// from SupportedGames constants + per-game config files.
    /// </summary>
    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            AppSettings settings;

            if (File.Exists(SettingsFilePath))
            {
                await using var stream = File.OpenRead(SettingsFilePath);
                settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions)
                           ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }

            // Build game profiles from static definitions
            settings.GameProfiles = SupportedGames.All.Select(g => new GameProfile
            {
                GameId = g.GameId,
                DisplayName = g.DisplayName,
                ShortName = g.ShortName,
                ExecutableName = g.ExecutableName,
                BannerColor = g.BannerColor,
                AppDataFolder = GetGameAppDataFolder(g.GameId)
            }).ToList();

            // Overlay per-game config from disk
            foreach (var profile in settings.GameProfiles)
            {
                var config = await LoadGameConfigInternalAsync(profile.GameId);
                if (config is not null)
                {
                    profile.GameRootPath = config.GameRootPath;
                    profile.ModLibraryPath = config.ModLibraryPath;
                    profile.IsConfigured = config.IsConfigured;
                    profile.LastOpened = config.LastOpened;
                }
            }

            _cachedSettings = settings;
            return settings;
        }
        catch (Exception ex)
        {
            await LogErrorAsync(ex);
            var fallback = new AppSettings();
            _cachedSettings = fallback;
            return fallback;
        }
    }

    /// <summary>
    /// Persists global settings (activeGameId, setupCompleted) to disk.
    /// </summary>
    public async Task SaveAsync(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppDataRoot);
            await using var stream = File.Create(SettingsFilePath);
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
            _cachedSettings = settings;
        }
        catch (Exception ex)
        {
            await LogErrorAsync(ex);
            throw;
        }
    }

    /// <summary>
    /// Saves a game profile's paths and config to its per-game game_config.json file.
    /// </summary>
    public async Task SaveGameConfigAsync(GameProfile profile)
    {
        var gameFolder = GetGameAppDataFolder(profile.GameId);
        Directory.CreateDirectory(gameFolder);

        var configPath = Path.Combine(gameFolder, "game_config.json");
        var config = new GameConfigJson
        {
            GameId = profile.GameId,
            GameRootPath = profile.GameRootPath,
            ModLibraryPath = profile.ModLibraryPath,
            IsConfigured = profile.IsConfigured,
            LastOpened = profile.LastOpened ?? DateTime.Now
        };

        await using var stream = File.Create(configPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions);
    }

    /// <summary>
    /// Gets a game profile from the cached settings by ID.
    /// </summary>
    public GameProfile? GetGameProfile(string gameId)
    {
        return _cachedSettings?.GameProfiles
            .FirstOrDefault(p => p.GameId.Equals(gameId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the currently active game profile from cached settings.
    /// </summary>
    public GameProfile? GetActiveGameProfile()
    {
        if (_cachedSettings is null) return null;
        return GetGameProfile(_cachedSettings.ActiveGameId);
    }

    /// <summary>
    /// Returns the AppData folder path for a given game.
    /// </summary>
    public static string GetGameAppDataFolder(string gameId)
        => Path.Combine(AppDataRoot, gameId);

    /// <summary>
    /// Validates that the given directory contains the specified game executable.
    /// </summary>
    public static bool ValidateGameDirectory(string path, string executableName)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(executableName))
            return false;

        return File.Exists(Path.Combine(path, executableName));
    }

    /// <summary>
    /// Validates that the given mods library directory exists.
    /// </summary>
    public static bool ValidateModsLibraryDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return Directory.Exists(path);
    }

    /// <summary>
    /// Appends an error message to the log file.
    /// </summary>
    public static async Task LogErrorAsync(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppDataRoot);
            var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
            await File.AppendAllTextAsync(ErrorLogPath, logLine);
        }
        catch
        {
            // Swallow logging failures to avoid infinite loops.
        }
    }

    // ── Private helpers ──────────────────────────────────────────────

    private static async Task<GameConfigJson?> LoadGameConfigInternalAsync(string gameId)
    {
        var configPath = Path.Combine(GetGameAppDataFolder(gameId), "game_config.json");
        if (!File.Exists(configPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            return JsonSerializer.Deserialize<GameConfigJson>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// JSON shape for the per-game game_config.json file.
    /// </summary>
    private sealed class GameConfigJson
    {
        [JsonPropertyName("gameId")]
        public string GameId { get; set; } = string.Empty;

        [JsonPropertyName("gameRootPath")]
        public string GameRootPath { get; set; } = string.Empty;

        [JsonPropertyName("modLibPath")]
        public string ModLibraryPath { get; set; } = string.Empty;

        [JsonPropertyName("isConfigured")]
        public bool IsConfigured { get; set; }

        [JsonPropertyName("lastOpened")]
        public DateTime? LastOpened { get; set; }
    }
}
