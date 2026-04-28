# Pre-Apply Summary

## Overview

Applying mods now shows a summary dialog before any files are deployed to the game's `Mods` folder.

The dialog is meant to make the apply step safer by previewing the current file impact, conflict winners, and high-risk mods before the manager cleans managed files and copies the new winners.

## Behavior

- `Apply Mods` and `Apply + Launch` both show the summary before deployment starts.
- The summary compares the current winning file map against the last saved active registry.
- The dialog reports:
  - active mod count
  - winning file count
  - files that will be added
  - files that will be removed
  - files whose winning mod changed
  - conflicted files and current winners
  - high-risk mods that touch EDF-sensitive folders
- Longer lists start with a short preview and can be expanded with `Show All` / `Show Less` inside each section.
- Cancelling the dialog aborts the apply action.

## Implementation Notes

- `MainViewModel.ApplyModsInternalAsync()` now calls a pre-apply confirmation step before setting `IsBusy` and starting deployment.
- The preview is built from:
  - current active mods in memory
  - `ConflictService.BuildWinnerMap()`
  - the saved `active_mods.json` registry loaded through `FileService`
  - current conflict report data from `ModService`
  - existing `IsHighRisk` flags on `ModEntry`
- The dialog uses the same window/delegate pattern already used by the import preview flow.
- The dialog keeps full result lists in the preview model and lets the summary view model handle expandable list sections.
- The deploy service itself was left unchanged.

## Files Updated

- `Models/ApplySummaryPreview.cs`
- `ViewModels/ApplySummaryViewModel.cs`
- `Views/ApplySummaryWindow.xaml`
- `Views/ApplySummaryWindow.xaml.cs`
- `ViewModels/MainViewModel.cs`
- `MainWindow.xaml.cs`