# WPF UI Automation Setup

This repository now includes a small Windows UI Automation tool for the WPF app at `Tools/EDF6ModLoaderWpf.Automation`.

It gives you two paths:

- CLI mode for quick local inspection and interaction.
- MCP mode so a client such as Claude Desktop can call the same actions as tools.

## What Was Added

- Stable `AutomationProperties.AutomationId` values on the main WPF surfaces.
- Coverage for the main window, settings window, welcome window, import preview, and apply summary dialogs.
- A FlaUI-based automation console app that can launch or attach to `EDF6ModManager.exe`.
- A minimal MCP server mode over stdio in the same console app.

## Important Automation IDs

Main window:

- `MainWindow`
- `OpenSettingsButton`
- `ApplyModsButton`
- `ApplyAndLaunchButton`
- `LaunchGameButton`
- `ModSearchTextBox`
- `ModsDataGrid`
- `LoadoutButton`
- `SavePresetButton`
- `RecentImportsDrawer`
- `ConflictRadarDrawer`

Settings window:

- `SettingsWindow`
- `GameProfilesTabControl`
- `GameRootPathTextBox`
- `ModsLibraryPathTextBox`
- `BrowseGameRootButton`
- `BrowseModsLibraryButton`
- `SaveSettingsButton`

Welcome window:

- `WelcomeWindow`
- `Edf41RadioButton`
- `Edf5RadioButton`
- `Edf6RadioButton`
- `GetStartedButton`

Import preview window:

- `ImportPreviewWindow`
- `ImportOptionsPanel`
- `ImportAsCopyRadioButton`
- `ReplaceExistingRadioButton`
- `ImportWarningsPanel`
- `ConfirmImportButton`

Apply summary window:

- `ApplySummaryWindow`
- `ApplySummaryOverviewCard`
- `ActiveLoadOrderSection`
- `ConflictWinnersSection`
- `HighRiskModsSection`
- `ConfirmApplyModsButton`

## Build

Build the main app first so the automation tool can find the default executable:

```powershell
dotnet build .\EDF6ModLoaderWpf.csproj
dotnet build .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj
```

If the app is already running, the automation tool will attach to it. If not, it will launch the default debug build from `bin/Debug/net10.0-windows/EDF6ModManager.exe`.

## CLI Usage

Print the automation tree:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- tree --max-depth 3
```

List the visible top-level windows for the app, including modal dialogs:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- windows
```

Set the search box text:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- set-text --automation-id ModSearchTextBox --text armor
```

Click a button by `AutomationId`:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- invoke --automation-id OpenSettingsButton
```

Click a button and wait for a dialog window to appear:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- invoke --automation-id OpenSettingsButton --wait-window-title Settings
```

Capture a PNG screenshot of the current window:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- screenshot --output .\artifacts\ui-captures\main-window.png
```

Capture a specific element by `AutomationId`:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- screenshot --automation-id ModsDataGrid --output .\artifacts\ui-captures\mods-grid.png
```

Open a dialog and capture it in a single deterministic command:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- capture-dialog --open-automation-id OpenSettingsButton --target-window-title Settings --output .\artifacts\ui-captures\settings-window-updated.png
```

Target a custom app build explicitly:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- tree --exe D:\custom\EDF6ModManager.exe
```

## MCP Usage

Run the tool in MCP mode:

```powershell
dotnet run --project .\Tools\EDF6ModLoaderWpf.Automation\EDF6ModLoaderWpf.Automation.csproj -- --mcp
```

The MCP server exposes these tools:

- `inspect_ui`
- `list_windows`
- `invoke_element`
- `set_text`
- `capture_screenshot`
- `capture_dialog`

Example Claude Desktop config:

```json
{
  "mcpServers": {
    "edf6-wpf": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "D:/PROJECTFILE/PROJECT_BELAJAR/EDF6ModLoaderWpf/Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj",
        "--",
        "--mcp"
      ]
    }
  }
}
```

After the client connects, it can inspect the UI tree, set text into the search box, invoke buttons, and save screenshots using the automation IDs above.

If you need to capture a modal dialog such as Settings, use `capture_dialog` with the button AutomationId and the dialog title fragment. You can also call `capture_screenshot` with `openAutomationId` and `targetWindowTitleContains` for the same one-shot open-and-capture flow. If a dialog is already open, call `list_windows` first to see the exact window title and pass that title fragment into `capture_screenshot` with `windowTitleContains`.

## Notes

- WPF desktop automation is less forgiving than Playwright on the web. Stable automation IDs matter.
- If a build shows file-lock warnings, close the running app before rebuilding the main executable.
- The current MCP server is intentionally small and stateless. Each tool call launches or attaches, performs the action, and returns text output.

---

# UI Issue Investigation Guide (for Future Agents)

This section describes the correct workflow for investigating and fixing UI problems in this app using the automation tooling. Read this **before** touching any XAML or ViewModel code.

## Core Principle: Tree First, Screenshot Second

**The accessibility tree is always real-time.** `tree` reads live UI state via the Windows UI Automation API — no render pipeline, no buffering. Use it to confirm what the app actually contains before drawing any conclusions.

**Screenshots are for visual verification only.** Use them to confirm layout, spacing, and visual appearance after you already know what's in the tree. Never use a screenshot alone to diagnose a missing or wrong element.

## Step 1 — Build the Automation Tool

```bash
dotnet build EDF6ModLoaderWpf.csproj
dotnet build Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj
```

## Step 2 — Start the App

```bash
cmd.exe /c start "" "bin/Debug/net10.0-windows/EDF6ModManager.exe"
```

Wait ~3 seconds for async initialization to complete before inspecting.

> **Why wait?** `MainWindow.InitializeAsync` calls `await _viewModel.InitializeAsync()` which loads settings, game profiles, and mod lists asynchronously. Elements may exist in the tree but show empty/default values until initialization finishes.

## Step 3 — Dump the Accessibility Tree

```bash
dotnet run --project Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj -- tree --max-depth 4
```

This gives you:
- Which elements exist and their `AutomationId`
- `ControlType` (Button, Edit, DataItem, etc.)
- `IsEnabled`, `IsOffscreen`
- Text content / names

### Finding Missing AutomationIds

Elements without an `AutomationId` will show as `AutomationId=""` in the tree. If you need to interact with or test an element, add an `AutomationProperties.AutomationId` to it in XAML:

```xml
<Button Content="⬆️"
        AutomationProperties.AutomationId="MoveUpButton"
        AutomationProperties.Name="Move Up" ... />
```

For DataGrid row template buttons, add both `AutomationId` and `Name` so screen readers and test tools can distinguish them.

## Step 4 — Capture a Screenshot

```bash
dotnet run --project Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj -- screenshot --output artifacts/ui-captures/main-window.png
```

Capture a specific element only:

```bash
dotnet run --project Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj -- screenshot --automation-id ConflictRadarDrawer --output artifacts/ui-captures/conflict-radar.png
```

### Why Screenshots are Reliable Now (PrintWindow)

The screenshot command uses `PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT)` — not `Graphics.CopyFromScreen`.

**Do NOT replace this with `CopyFromScreen` or any GDI screen capture.** GDI captures the back-buffer and misses DWM-composited WPF frames, producing stale/empty screenshots even after a long delay. `PrintWindow` with `PW_RENDERFULLCONTENT` forces Windows to render the actual DWM frame into a bitmap, works off-screen and behind other windows, and always reflects the current app state.

### High-DPI Image Sizes

On a machine with 200% DPI scaling, `PrintWindow` captures at physical pixel resolution. A 1200×760 logical window produces a ~2550×1554 PNG. This is correct — it's the actual pixel content. To resize for viewing:

```powershell
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile("artifacts\ui-captures\main-window.png")
$w = [int]($img.Width * 0.6); $h = [int]($img.Height * 0.6)
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.DrawImage($img, 0, 0, $w, $h)
$bmp.Save("artifacts\ui-captures\main-window-small.png")
$g.Dispose(); $bmp.Dispose(); $img.Dispose()
```

## Step 5 — Diagnose the Issue

| Symptom | Use tree? | Use screenshot? | Look for |
|---|---|---|---|
| Element not appearing | Yes | No | Missing from tree = code/binding issue |
| Element visible but wrong text | Yes | No | Read `Name`/`Value` from tree |
| Layout / spacing wrong | No | Yes | Visual comparison |
| Button not clickable | Yes | No | `IsEnabled=false` in tree |
| Dialog not opening | Yes | Yes | `list windows` then `capture-dialog` |
| Stats showing 0 / empty DataGrid | Yes | No | Likely pre-init; wait longer or check ViewModel |

## Step 6 — Fix Patterns

### Empty/Corrupt Data Showing in UI

If ViewModel-bound data shows garbage values (e.g., entries with empty names), fix at the normalization layer — **not** in the View. Example:

```csharp
// In the ViewModel sync/load method:
ActiveGame.RecentImports.RemoveAll(entry => string.IsNullOrWhiteSpace(entry.ModName));
```

Never add null-guards or "if empty skip rendering" hacks to XAML. The ViewModel is the gatekeeper.

### Visibility Issues

Use `BooleanToVisibilityConverter` (key `BoolToVis`) for show/hide toggling. Never set `Visibility` to a hardcoded value in XAML unless it's unconditionally hidden. Trace visibility back to the ViewModel boolean property.

### Missing AutomationIds

If the tree shows a button/control you need to interact with but it has no `AutomationId`:
1. Find it in `MainWindow.xaml` (or the relevant View file).
2. Add `AutomationProperties.AutomationId="DescriptiveId"`.
3. For buttons inside `DataGridTemplateColumn`, also add `AutomationProperties.Name` so they're distinguishable across rows.
4. Rebuild and re-dump the tree to confirm.

### Colors / Themes

Never hardcode colors — use ModernWpfUI theme resource keys. If a color looks wrong in a screenshot, check whether it's a hardcoded `#RRGGBB` value in XAML and replace it with the appropriate `{DynamicResource ...}` key.

## Known Gotchas

| Gotcha | Details |
|---|---|
| `CopyFromScreen` produces stale frames | Do not use. The automation tool uses `PrintWindow` — keep it that way. |
| Screenshot captured before async init | Always wait 3+ seconds after launching before capturing. `tree` is immune to this. |
| High-DPI makes screenshots large | 200% DPI → 2× physical pixels. Normal and expected. Resize for viewing if needed. |
| Modal dialogs not in main tree | `tree` only walks the main window. Use `windows` command to list all open windows, then `capture-dialog` for modals. |
| DataGrid row buttons share AutomationId | Row buttons in a `DataGridTemplateColumn` share the same static `AutomationId` across all rows. Use `Name` or row index for disambiguation in tests. |
| Empty `RecentImportEntry` records | Old settings files may contain entries with blank `ModName`. Strip them in the ViewModel with `RemoveAll(e => string.IsNullOrWhiteSpace(e.ModName))` on load. |

## Quick Reference: Automation Tool Commands

```bash
# Real-time element tree (most useful diagnostic tool)
dotnet run --project Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj -- tree --max-depth 4

# List all visible windows (including modals)
dotnet run --project Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj -- windows

# Full window screenshot (reliable, uses PrintWindow)
dotnet run --project Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj -- screenshot --output artifacts/ui-captures/main.png

# Specific element screenshot
dotnet run --project Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj -- screenshot --automation-id ModsDataGrid --output artifacts/ui-captures/grid.png

# Click a button
dotnet run --project Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj -- invoke --automation-id ApplyModsButton

# Click a button and wait for a dialog
dotnet run --project Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj -- invoke --automation-id OpenSettingsButton --wait-window-title Settings

# Open dialog + capture in one shot
dotnet run --project Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj -- capture-dialog --open-automation-id OpenSettingsButton --target-window-title Settings --output artifacts/ui-captures/settings.png

# Type text into a field
dotnet run --project Tools/EDF6ModLoaderWpf.Automation/EDF6ModLoaderWpf.Automation.csproj -- set-text --automation-id ModSearchTextBox --text "armor"
```
