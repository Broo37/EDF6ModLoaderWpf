namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Snapshot of a single mod's state in a load order configuration.
/// Used by the undo stack to restore previous states.
/// </summary>
public sealed class ModOrderSnapshot
{
    public string ModName { get; set; } = string.Empty;
    public int LoadOrder { get; set; }
    public bool IsActive { get; set; }
    public string Group { get; set; } = string.Empty;
}
