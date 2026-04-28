namespace EDF6ModLoaderWpf.Models;

/// <summary>
/// Controls how an import should behave when a duplicate target exists.
/// </summary>
public enum ModImportMode
{
    ImportAsCopy,
    ReplaceExisting
}