# 🛡️ EDF Mod Manager

A Windows desktop mod manager for the **Earth Defense Force** series, supporting EDF 4.1, EDF 5, and EDF 6.

Built with WPF (.NET 10), MVVM architecture, and ModernWpfUI for a clean Windows 11-style interface.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-10.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- **Multi-game support** — Switch between EDF 4.1, EDF 5, and EDF 6 from a single app
- **Load order management** — Drag-and-drop reordering with undo/redo support (up to 10 steps)
- **Conflict detection** — Automatically detects file conflicts between mods; higher priority wins
- **One-click apply** — Builds a winner map, cleans old files, copies winners, and saves the registry
- **Mod metadata** — Reads `mod_info.json` for author, description, and version info
- **Settings persistence** — Remembers game paths, theme, font, and preferences across sessions
- **Toast notifications** — Non-intrusive status messages with auto-dismiss
- **Dark/Light theme** — Follows system theme or manual override via ModernWpfUI
- **Error logging** — Detailed error log saved to `%AppData%\EDF6ModManager\error.log`

## Screenshots

<!-- Add screenshots here -->

## Requirements

- Windows 10/11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

## Getting Started

### Build from source

```bash
git clone https://github.com/your-username/EDF6ModManager.git
cd EDF6ModManager
dotnet build
dotnet run
```

### Usage

1. Launch the app and select your game (EDF 4.1 / 5 / 6)
2. Set the game installation folder when prompted
3. Place mod folders inside `[GameRoot]\Mods\`
4. Enable/disable mods and arrange load order (higher number = higher priority)
5. Click **Apply** to deploy mods to the game directory

## Project Structure

```
Models/          → Data classes (ModEntry, ModInfo, AppSettings, etc.)
Services/        → Business logic (ModService, FileService, ConflictService, etc.)
ViewModels/      → MVVM ViewModels (MainViewModel, SettingsViewModel)
Views/           → Secondary windows (SettingsWindow, WelcomeWindow)
Helpers/         → UI utilities (NotificationHelper, ThemeHelper, FontHelper)
MainWindow.xaml  → Primary UI with DataGrid
App.xaml.cs      → DI container setup
```

## Tech Stack

- **Framework:** .NET 10 / WPF
- **MVVM:** [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- **DI:** Microsoft.Extensions.DependencyInjection
- **UI Theme:** [ModernWpfUI](https://github.com/Kinnara/ModernWpf)
- **Serialization:** System.Text.Json

## File Paths

| File | Location |
|------|----------|
| Settings | `%AppData%\EDF6ModManager\settings.json` |
| Error log | `%AppData%\EDF6ModManager\error.log` |
| Active mods registry | `[GameRoot]\Mods\active_mods.json` |
| Mod metadata | `[GameRoot]\Mods\[ModName]\mod_info.json` |

## License

MIT
