# Recent Import Pinning

## Overview

Recent imports can now be pinned per game so important mods stay at the top of the recent-import list.

The feature is designed to work with the existing recent-import history:

- Pinned entries appear first in the recent-import card list.
- The `Latest` shortcut still shows the most recently imported mod, even if it is not pinned.
- Pin state is persisted per game in that game's `game_config.json`.

## User Experience

- Each recent-import card now exposes a `Pin` or `Unpin` action.
- The latest-import shortcut also exposes a `Pin` or `Unpin` action.
- Pinned entries show a `📌 Pinned` label.

## Persistence Rules

- Pin state is stored on `RecentImportEntry.IsPinned`.
- The list is normalized before display and save:
  - pinned entries first
  - newest imports first within each group
- Re-importing the same mod preserves its previous pin state.

## Files Updated

- `Models/RecentImportEntry.cs`
- `ViewModels/MainViewModel.cs`
- `MainWindow.xaml`

## Notes

- Pinning only changes the recent-import presentation layer. It does not affect load order, activation state, or deployed files.
- Recent imports remain scoped per game profile.