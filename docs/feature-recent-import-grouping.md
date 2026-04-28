# Recent Import Grouping

## Overview

The recent-import panel now separates pinned imports from the standard recent history so important mods stay visible without mixing into the rolling list.

## Behavior

- The latest import card still highlights the newest import across the active game profile.
- Pinned entries render in a dedicated `Pinned` section with a compact item count in the header.
- Unpinned entries render in a separate `Recent` section with its own count in the header.
- The existing pin, select, and open-folder actions work the same in both sections.

## Implementation Notes

- `MainViewModel` now rebuilds pinned and unpinned collections together inside `SyncRecentImportsForActiveGame()`.
- The main window uses a shared card template so both sections stay visually consistent.
- Existing persistence and normalization rules remain unchanged.

## Files Updated

- `ViewModels/MainViewModel.cs`
- `MainWindow.xaml`