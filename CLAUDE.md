# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
dotnet build          # Build the project
dotnet run            # Run the application (output: EDF6ModManager.exe)
```

No test project currently. Target: `net10.0-windows`.

## Architecture

WPF desktop app (.NET 10, C#) using **MVVM + Dependency Injection**.

```
Models/          → Data classes (ModEntry, ModInfo, AppSettings, GameProfile, etc.)
Services/        → Business logic singletons (ModService, FileService, ConflictService, LoadOrderService, SettingsService, GameSwitchService)
ViewModels/      → CommunityToolkit.Mvvm ObservableObjects (MainViewModel, SettingsViewModel)
Views/           → Secondary windows (SettingsWindow, WelcomeWindow)
Helpers/         → UI utilities (NotificationHelper, ThemeHelper, FontHelper)
MainWindow.xaml  → Primary UI with mod DataGrid
App.xaml.cs      → DI container setup + startup
```

**DI registration:** Services are **Singletons**, ViewModels are **Transient**. Resolve via `App.GetService<T>()` — never `new` a service directly.

**Game switching:** `GameSwitchService.SwitchAsync()` saves the new active game and broadcasts a `GameSwitchedMessage` via `WeakReferenceMessenger.Default` — subscribe in ViewModels that need to react.

## Key Domain Concepts

- **Supported games:** EDF 4.1, EDF 5, EDF 6 — defined as static profiles in `SupportedGames`. Game-specific paths live in per-game `game_config.json` files; `AppSettings` only stores the active game ID.
- **Load order priority:** Higher number = higher priority = wins file conflicts.
- **Winner map:** `ConflictService.BuildWinnerMap()` iterates active mods in ascending `LoadOrder`; last writer wins.
- **Apply workflow** — must follow this exact sequence, never skip or reorder:
  ```
  BuildWinnerMap → CleanManagedFiles → CopyWinners → SaveRegistry → UpdateStatuses
  ```
- **Undo stack:** `LoadOrderService` maintains a max-10 LIFO stack of `ModOrderSnapshot`.
- **Standard mod subfolders** in `[GameRoot]\Mods\`: `DEFAULTPACKAGE`, `MainScript`, `Mission`, `Object`, `Patches`, `Plugins`, `Weapon`. Reference via `FileService.StandardSubfolders` — don't hardcode elsewhere.
- **Mod metadata:** Read from `mod_info.json` inside each mod folder; graceful fallback when missing. Exclude `mod_info.json` when enumerating mod files.

## File Paths

- Settings: `%AppData%\EDF6ModManager\settings.json`
- Error log: `%AppData%\EDF6ModManager\error.log`
- Active mods registry: `[GameRoot]\Mods\active_mods.json`

## Code Style

**Observable properties:** Use `[ObservableProperty]` on `private _camelCase` fields (CommunityToolkit.Mvvm source generators).

**Commands:** Use `[RelayCommand]` on `private async Task MethodNameAsync()` — generates `MethodNameCommand`. All async ViewModel commands must follow this pattern:
```csharp
[RelayCommand]
private async Task OperationAsync()
{
    IsBusy = true;
    try
    {
        // ... logic ...
    }
    catch (UnauthorizedAccessException)
    {
        NotificationHelper.ShowError("Permission Error", "Try running as Administrator.");
    }
    catch (Exception ex)
    {
        await SettingsService.LogErrorAsync(ex);
        NotificationHelper.ShowError("Error", ex.Message);
    }
    finally
    {
        IsBusy = false;
    }
}
```

**Toast notifications:** `NotificationHelper.ShowToast()` on the `NotificationPanel` — auto-dismiss after 4 s.

**File operations:** Use retry logic (5 attempts, 200ms exponential backoff) — `CopyWithRetryAsync` / `DeleteWithRetryAsync` in `ModService`.

**JSON persistence:** `System.Text.Json` with `JsonSerializerOptions { WriteIndented = true }`.

## Service Layer Conventions

- Always use case-insensitive comparison for file path dictionaries: `new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase)`.
- Use `static` for pure utility methods that don't need instance state (e.g., `GetModRelativeFiles`, `BuildWinnerMap`).
- Always `Directory.CreateDirectory(destDir)` before writing files.

## XAML Conventions

Always include the ModernWpfUI namespace and apply modern window style:
```xml
xmlns:ui="http://schemas.modernwpf.com/2019"
ui:WindowHelper.UseModernWindowStyle="True"
```

- Checkbox columns: bind `IsChecked="{Binding IsActive, Mode=TwoWay}"`, handle toggle in code-behind → delegate to ViewModel command.
- Editable text columns: use `UpdateSourceTrigger=LostFocus`.
- Button columns: bind `Command` to ViewModel relay commands, pass row item via `CommandParameter="{Binding}"`.
- Search/filter inputs: use `UpdateSourceTrigger=PropertyChanged`.
- Toolbar buttons: `StackPanel Orientation="Horizontal"` with `Margin="0,0,8,0"` spacing.
- Visibility toggles: `BooleanToVisibilityConverter` (key: `BoolToVis`).
- Don't hardcode colors — rely on ModernWpfUI theme resources.
- Don't put business logic in code-behind — delegate to ViewModel commands.
