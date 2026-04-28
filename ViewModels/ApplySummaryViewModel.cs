using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDF6ModLoaderWpf.Models;

namespace EDF6ModLoaderWpf.ViewModels;

/// <summary>
/// ViewModel for the pre-apply summary dialog.
/// </summary>
public sealed partial class ApplySummaryViewModel : ObservableObject
{
    public ApplySummaryViewModel(ApplySummaryPreview preview)
    {
        Preview = preview;
        ActiveModsSection = new ApplySummaryListSection(preview.ActiveMods);
        AddedFilesSection = new ApplySummaryListSection(preview.AddedFiles);
        RemovedFilesSection = new ApplySummaryListSection(preview.RemovedFiles);
        ReplacedFilesSection = new ApplySummaryListSection(preview.ReplacedFiles);
        ConflictSection = new ApplySummaryListSection(preview.ConflictSummaries);
        HighRiskModsSection = new ApplySummaryListSection(preview.HighRiskMods);
    }

    public ApplySummaryPreview Preview { get; }

    public ApplySummaryListSection ActiveModsSection { get; }

    public ApplySummaryListSection AddedFilesSection { get; }

    public ApplySummaryListSection RemovedFilesSection { get; }

    public ApplySummaryListSection ReplacedFilesSection { get; }

    public ApplySummaryListSection ConflictSection { get; }

    public ApplySummaryListSection HighRiskModsSection { get; }

    public string LoadoutSummary => string.IsNullOrWhiteSpace(Preview.ActivePresetName)
        ? "No named loadout is currently selected."
        : Preview.IsActivePresetDirty
            ? $"Loadout: {Preview.ActivePresetName} (modified since it was last saved)"
            : $"Loadout: {Preview.ActivePresetName}";
}

public sealed partial class ApplySummaryListSection : ObservableObject
{
    private const int PreviewItemLimit = 8;

    public ApplySummaryListSection(IReadOnlyList<string> items)
    {
        Items = items;
    }

    public IReadOnlyList<string> Items { get; }

    public bool HasItems => Items.Count > 0;

    public bool HasOverflow => Items.Count > PreviewItemLimit;

    public IReadOnlyList<string> VisibleItems => IsExpanded || !HasOverflow
        ? Items
        : Items.Take(PreviewItemLimit).ToList();

    public string ToggleLabel => IsExpanded ? "Show Less" : $"Show All ({Items.Count})";

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(VisibleItems));
        OnPropertyChanged(nameof(ToggleLabel));
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }
}