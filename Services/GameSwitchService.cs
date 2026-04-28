using CommunityToolkit.Mvvm.Messaging;
using EDF6ModLoaderWpf.Models;

namespace EDF6ModLoaderWpf.Services;

/// <summary>
/// Message broadcast when the active game changes.
/// </summary>
public record GameSwitchedMessage(GameProfile NewGame, object? Sender = null);

/// <summary>
/// Coordinates switching between game profiles: persists the active game,
/// updates the profile, and notifies all subscribers via MVVM Messenger.
/// </summary>
public sealed class GameSwitchService
{
    private readonly SettingsService _settingsService;

    public GameSwitchService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Switches the active game to <paramref name="gameId"/>, saves the choice,
    /// and broadcasts a <see cref="GameSwitchedMessage"/>.
    /// </summary>
    public async Task<GameProfile?> SwitchAsync(string gameId, AppSettings settings, object? sender = null)
    {
        settings.ActiveGameId = gameId;
        await _settingsService.SaveAsync(settings);

        var profile = settings.GameProfiles
            .FirstOrDefault(p => p.GameId.Equals(gameId, StringComparison.OrdinalIgnoreCase));

        if (profile is not null)
        {
            profile.LastOpened = DateTime.Now;
            await _settingsService.SaveGameConfigAsync(profile);

            WeakReferenceMessenger.Default.Send(new GameSwitchedMessage(profile, sender));
        }

        return profile;
    }
}
