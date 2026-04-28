using CommunityToolkit.Mvvm.ComponentModel;
using EDF6ModLoaderWpf.Models;

namespace EDF6ModLoaderWpf.ViewModels;

/// <summary>
/// ViewModel for the import preview dialog.
/// </summary>
public partial class ImportPreviewViewModel : ObservableObject
{
    public ImportPreviewViewModel(ModImportPreview preview)
    {
        Preview = preview;
    }

    public ModImportPreview Preview { get; }

    [ObservableProperty]
    private ModImportMode _selectedImportMode = ModImportMode.ImportAsCopy;

    public bool HasWarnings => Preview.Warnings.Count > 0;

    public bool HasDuplicateCandidates => Preview.DuplicateCandidates.Count > 0;

    public bool CanReplaceExisting => Preview.CanReplaceExisting;

    public bool ImportAsCopySelected
    {
        get => SelectedImportMode == ModImportMode.ImportAsCopy;
        set
        {
            if (value)
                SelectedImportMode = ModImportMode.ImportAsCopy;
        }
    }

    public bool ReplaceExistingSelected
    {
        get => SelectedImportMode == ModImportMode.ReplaceExisting;
        set
        {
            if (value && CanReplaceExisting)
                SelectedImportMode = ModImportMode.ReplaceExisting;
        }
    }

    partial void OnSelectedImportModeChanged(ModImportMode value)
    {
        OnPropertyChanged(nameof(ImportAsCopySelected));
        OnPropertyChanged(nameof(ReplaceExistingSelected));
    }
}