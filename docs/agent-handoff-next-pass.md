# Agent Handoff: WPF Automation + UI Review

Date: 2026-04-30

## Goal Reached In This Pass

This pass answered the original request to make the WPF app inspectable in a Playwright-like way and usable through MCP. The repo now has a working Windows UI Automation tool, a minimal MCP server, stable automation IDs across key windows, and a first UI cleanup pass on the main window.

This document is the continuation note for the next agent.

## What Was Completed

### 1. WPF automation and MCP setup

- Confirmed there was no existing MCP or WPF automation stack in the repo.
- Chose FlaUI with UIA3 as the practical WPF equivalent for inspection and interaction.
- Added a separate automation tool project at `Tools/EDF6ModLoaderWpf.Automation`.
- Added CLI commands for:
  - `tree` / `list`
  - `invoke`
  - `set-text`
  - `screenshot` / `capture`
  - `windows`
- Added MCP mode with stdio framing and these tools:
  - `inspect_ui`
  - `list_windows`
  - `invoke_element`
  - `set_text`
  - `capture_screenshot`

### 2. Stable automation coverage in the WPF app

- Added `AutomationProperties.AutomationId` coverage to the main app surfaces.
- Main window coverage includes major controls such as:
  - `MainWindow`
  - `OpenSettingsButton`
  - `ApplyModsButton`
  - `ApplyAndLaunchButton`
  - `LaunchGameButton`
  - `RefreshButton`
  - `ImportMenuButton`
  - `ManageMenuButton`
  - `ViewMenuButton`
  - `LoadoutButton`
  - `ModSearchTextBox`
  - `ShowActiveOnlyCheckBox`
  - `ShowConflictsOnlyCheckBox`
  - `ShowRiskyOnlyCheckBox`
  - `ClearFiltersButton`
  - `ModsDataGrid`
  - `RecentImportsDrawer`
  - `ConflictRadarDrawer`
  - `DropOverlay`
  - `EmptyLibraryState`
- Added automation IDs to these dialog windows too:
  - `Views/SettingsWindow.xaml`
  - `Views/WelcomeWindow.xaml`
  - `Views/ImportPreviewWindow.xaml`
  - `Views/ApplySummaryWindow.xaml`

### 3. Main window UI cleanup pass

The main window got a practical first layout pass after live screenshot review. Current notable changes:

- Added a dedicated `ModGridHeaderStyle` for clearer DataGrid headers.
- Increased sidebar width to `276`.
- Sidebar subtitle now shows `ActiveGame.ShortName`.
- Improved some labels and headers:
  - `Warnings`
  - `Move`
  - `Active only`
- Added the empty-state block with `AutomationProperties.AutomationId="EmptyLibraryState"`.

### 4. Build and repo wiring fixes

- Added the automation project to `EDF6ModLoaderWpf.slnx`.
- Updated the main `EDF6ModLoaderWpf.csproj` so nested `Tools/**` sources are not compiled into the WPF app.
- Fixed automation-side issues encountered during implementation:
  - invalid MCP switch syntax
  - blank-input handling in the MCP reader
  - screenshot math overload ambiguity
  - vulnerable transitive `System.Drawing.Common` version

### 5. Documentation and artifacts

- Added the automation setup doc at `docs/wpf-ui-automation.md`.
- Captured live UI artifacts under `artifacts/ui-captures/`.

## Files Most Relevant To Continue

### WPF UI

- `MainWindow.xaml`
- `Views/SettingsWindow.xaml`
- `Views/WelcomeWindow.xaml`
- `Views/ImportPreviewWindow.xaml`
- `Views/ApplySummaryWindow.xaml`

### Automation tool

- `Tools/EDF6ModLoaderWpf.Automation/Program.cs`
- `Tools/EDF6ModLoaderWpf.Automation/WpfAutomationClient.cs`
- `Tools/EDF6ModLoaderWpf.Automation/McpServer.cs`
- `Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj`

### Project wiring and docs

- `EDF6ModLoaderWpf.csproj`
- `EDF6ModLoaderWpf.slnx`
- `docs/wpf-ui-automation.md`
- `docs/agent-handoff-next-pass.md`

## Validation Already Done

### Build validation

- Main app build succeeded.
- Automation project build succeeded.
- The recurring build blocker was the app EXE being locked when `EDF6ModManager.exe` was still running.

### Automation validation

- CLI tree inspection worked.
- CLI invoke worked.
- CLI text entry worked.
- MCP `initialize` and `tools/list` worked.
- Screenshot capture worked.
- Window enumeration worked, including modal-aware listing in at least one successful run.

### Screenshot artifacts already captured

- `artifacts/ui-captures/main-window.png`
- `artifacts/ui-captures/settings-window.png`
- `artifacts/ui-captures/main-window-updated.png`

## Status Update From Follow-Up Pass

The Settings dialog capture issue has been addressed.

- Added a deterministic `capture-dialog` CLI command that invokes a known AutomationId, waits for a target window title fragment, and captures that dialog in one run.
- Added `--wait-window-title` support to `invoke`.
- Added MCP support for `capture_dialog`, plus `openAutomationId` and `targetWindowTitleContains` arguments on `capture_screenshot`.
- Hardened cleanup for automation-launched app instances so modal windows do not linger after capture.
- Captured a fresh Settings artifact at `artifacts/ui-captures/settings-window-updated.png`.
- Applied a small Settings/secondary-window polish pass using theme brushes and safer wrapping.

## Known Limitation Still Open

The Settings dialog repeatability issue from the prior pass is no longer open. Remaining UI-review work is mostly about getting realistic app state/data for Import Preview and Apply Summary captures.

## Recommended Next Pass

### Priority 1: capture realistic secondary-window states

- Use the new `capture-dialog` path for dialogs opened from the main window.
- Review and tidy these windows using fresh screenshots where realistic state is available:
  - `Views/ImportPreviewWindow.xaml`
  - `Views/ApplySummaryWindow.xaml`
- Focus on alignment, spacing consistency, row density, and any clipped or weakly grouped controls.

### Priority 2: one more main window polish pass

- Re-check DataGrid readability after the header pass.
- Look at row density, action-button visual weight, and whether the filter area still feels crowded on narrower widths.
- Confirm the empty state, recent imports drawer, and conflict radar drawer all remain visually aligned after further tweaks.

### Priority 3: optional automation hardening

- Add a small smoke script or documented checklist to quickly verify:
  - app launches or attaches
  - tree listing works
  - search box text can be set
  - settings dialog can be opened
  - a screenshot can be captured

## Commands That Worked

Build main app:

```powershell
dotnet build .\EDF6ModLoaderWpf.csproj
```

Build automation tool:

```powershell
dotnet build .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj
```

Inspect UI tree:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- tree --max-depth 3
```

List visible windows:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- windows --timeout-ms 8000
```

Invoke the Settings button:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- invoke --automation-id OpenSettingsButton
```

Capture the main window:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- screenshot --output .\artifacts\ui-captures\main-window-updated.png
```

Open and capture the Settings dialog in one command:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- capture-dialog --open-automation-id OpenSettingsButton --target-window-title Settings --output .\artifacts\ui-captures\settings-window-updated.png
```

Run in MCP mode:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- --mcp
```

If builds start failing due to file locks, kill the running app first:

```powershell
taskkill /IM EDF6ModManager.exe /F
```

## Suggested Resume Order For The Next Agent

1. Read `docs/wpf-ui-automation.md` and this file.
2. Build the main app and the automation tool.
3. Launch or attach using the automation CLI and confirm `windows` plus `tree` still work.
4. Use `capture-dialog` for Settings or any other dialog that can be opened from a known AutomationId.
5. Capture fresh screenshots for Import Preview and Apply Summary once suitable app state/test data is available.
6. Apply the next UI cleanup pass based on those screenshots.

## Short Summary

The MCP and WPF automation foundation is already in place and validated. The Settings modal capture path is now deterministic. The best return for the next pass is to continue UI review on Import Preview, Apply Summary, and any remaining main-window density issues.
