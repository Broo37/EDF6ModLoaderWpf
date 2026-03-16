namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Static definitions for the three supported Earth Defense Force games.
/// </summary>
public static class SupportedGames
{
    public static readonly GameProfile EDF41 = new()
    {
        GameId = "EDF41",
        DisplayName = "Earth Defense Force 4.1",
        ShortName = "EDF 4.1",
        ExecutableName = "EDF41.exe",
        BannerColor = "#1565C0"
    };

    public static readonly GameProfile EDF5 = new()
    {
        GameId = "EDF5",
        DisplayName = "Earth Defense Force 5",
        ShortName = "EDF 5",
        ExecutableName = "EDF5.exe",
        BannerColor = "#2E7D32"
    };

    public static readonly GameProfile EDF6 = new()
    {
        GameId = "EDF6",
        DisplayName = "Earth Defense Force 6",
        ShortName = "EDF 6",
        ExecutableName = "EDF6.exe",
        BannerColor = "#B71C1C"
    };

    public static readonly GameProfile[] All = [EDF41, EDF5, EDF6];

    public static GameProfile? GetById(string gameId)
        => All.FirstOrDefault(g => g.GameId.Equals(gameId, StringComparison.OrdinalIgnoreCase));
}
