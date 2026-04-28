# Apply Backup Snapshot

## Overview

Applying mods now creates an automatic `last apply` backup snapshot before the manager cleans the currently managed files in the game's `Mods` folder.

That snapshot can be restored later from the main `Manage` menu.

## Behavior

- Every apply operation saves the currently deployed managed-file state before cleanup starts.
- The backup stores:
  - the active deployed mod state
  - the active preset name
  - group assignments
  - the managed files that were actually present in the game's `Mods` folder
- The `Manage` menu now exposes `Restore Last Apply`.
- Restoring the backup:
  - removes the currently managed deployed files
  - copies the backed-up files back into the game's `Mods` folder
  - restores the saved active registry state
  - refreshes the in-memory mod list from the restored registry

## Implementation Notes

- Backup creation is owned by `ModService.ApplyAllModsAsync()` so every apply path automatically gets the same safety behavior.
- Backups are stored under the active game's app-data registry folder in a dedicated `backups/last-apply` folder.
- The snapshot intentionally preserves only deploy-state data; saved preset definitions remain in the current registry.
- Restore uses retry-based copy and delete logic and keeps path handling case-insensitive.

## Files Updated

- `Models/ApplyBackupSnapshot.cs`
- `Services/BackupService.cs`
- `Services/ModService.cs`
- `ViewModels/MainViewModel.cs`
- `MainWindow.xaml`
- `Views/ApplySummaryWindow.xaml`
- `App.xaml.cs`