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
