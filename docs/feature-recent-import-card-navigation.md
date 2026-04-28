# Recent Import Card Navigation

## Overview

The recent-import card list now supports keyboard navigation so users can move across imported mod cards without tabbing through every action button in sequence.

## Behavior

- Focus the `Jump` button on the latest-import summary card or the `Select` button on any recent-import card.
- Focus the `Select` button on any recent-import card.
- Use `Left` or `Up` to move focus to the previous card's `Select` action.
- Use `Right` or `Down` to move focus to the next card's `Select` action.
- Use `Home` to jump to the first card and `End` to jump to the last card.

## Implementation Notes

- The behavior is handled in `MainWindow.xaml.cs` because it is strictly keyboard focus management.
- The shared recent-import card template and latest-import summary action mark their primary actions with a tag so the window can gather visible actions in display order.
- Existing recent-import commands and persistence remain unchanged.

## Files Updated

- `MainWindow.xaml`
- `MainWindow.xaml.cs`