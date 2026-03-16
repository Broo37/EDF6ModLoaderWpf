# EDF6 Mod Manager — Project Guidelines

## Architecture

WPF desktop app (.NET 10, C#) using **MVVM + Dependency Injection**.

```
Models/          → Data classes (ModEntry, ModInfo, AppSettings, etc.)
Services/        → Business logic singletons (ModService, FileService, ConflictService, LoadOrderService, SettingsService)
ViewModels/      → CommunityToolkit.Mvvm ObservableObjects (MainViewModel, SettingsViewModel)
Views/           → Secondary windows (SettingsWindow)
Helpers/         → UI utilities (NotificationHelper)
MainWindow.xaml  → Primary UI with DataGrid
App.xaml.cs      → DI container setup (Microsoft.Extensions.DependencyInjection)
```

**DI registration:** Services are **Singletons**, ViewModels are **Transient**. Resolve via `App.GetService<T>()`.

## Key Domain Concepts

- **Load order priority:** Higher number = higher priority = wins file conflicts
- **Winner map:** `ConflictService.BuildWinnerMap()` iterates mods ordered by LoadOrder; last writer wins
- **Apply workflow:** BuildWinnerMap → CleanManagedFiles → CopyWinners → SaveRegistry → UpdateStatuses
- **Undo stack:** `LoadOrderService` maintains a max-10 LIFO stack of `ModOrderSnapshot`
- **Standard subfolders** in game's `Mods\`: DEFAULTPACKAGE, MainScript, Mission, Object, Patches, Plugins, Weapon

## Code Style

- **Observable properties:** Use `[ObservableProperty]` on `private _camelCase` fields (CommunityToolkit.Mvvm source generators)
- **Commands:** Use `[RelayCommand]` on `private async Task MethodNameAsync()` — generates `MethodNameCommand`
- **Nullable enabled:** All reference types have explicit nullability annotations
- **UI framework:** ModernWpfUI for Windows 11 styling; use `ui:` namespace for themed controls

## Conventions

- All async ViewModel commands wrap in try/catch with `IsBusy` guard and `finally { IsBusy = false; }`
- Errors: catch `UnauthorizedAccessException` separately (suggest admin), log all exceptions via `SettingsService.LogErrorAsync()`
- Toast notifications via `NotificationHelper.ShowToast()` on the `NotificationPanel` — auto-dismiss after 4s
- File operations use retry logic: 5 attempts, 200ms exponential backoff (`CopyWithRetryAsync`, `DeleteWithRetryAsync`)
- JSON persistence: `System.Text.Json` with `JsonSerializerOptions { WriteIndented = true }`
- Mod metadata from `mod_info.json` inside each mod folder; graceful fallback when missing

## Build and Test

```bash
dotnet build          # Build the project
dotnet run            # Run the application (output: EDF6ModManager.exe)
```

No test project currently. Target: `net10.0-windows`.

## File Paths

- Settings: `%AppData%\EDF6ModManager\settings.json`
- Error log: `%AppData%\EDF6ModManager\error.log`
- Active mods registry: `[GameRoot]\Mods\active_mods.json`
