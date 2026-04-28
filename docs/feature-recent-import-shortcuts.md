# Recent Import Shortcuts

## Overview

The recent-import workflow now supports keyboard shortcuts so the newest import can be revisited or managed without using the mouse.

## Shortcuts

- `Ctrl+Shift+J` selects the latest imported mod in the main grid.
- `Ctrl+Alt+P` toggles the pin state of the latest imported mod.
- `Ctrl+Alt+R` clears the recent-import list for the active game profile.

## Notes

- These shortcuts are scoped to the main window.
- `Ctrl+Alt+R` still asks for confirmation before clearing recent imports.
- The latest-import card and recent-import buttons expose matching tooltip hints for discoverability.

## Files Updated

- `ViewModels/MainViewModel.cs`
- `MainWindow.xaml`