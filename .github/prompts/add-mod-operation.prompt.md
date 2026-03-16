---
description: "Scaffold a new mod operation end-to-end: ViewModel command, service method, and UI binding following all existing patterns."
agent: "agent"
argument-hint: "Describe the new mod operation (e.g., 'export mod list to CSV', 'duplicate a mod entry')"
---
# Add New Mod Operation

Create a complete vertical slice for a new mod operation in this WPF MVVM app.

## Requirements

Generate code for all three layers:

### 1. ViewModel Command (`ViewModels/MainViewModel.cs`)

Follow this exact pattern:
```csharp
[RelayCommand]
private async Task NewOperationAsync(/* params */)
{
    IsBusy = true;
    try
    {
        _loadOrderService.PushUndoState(Mods);
        CanUndo = _loadOrderService.CanUndo;

        // ... operation logic ...

        var progress = new Progress<string>(msg => StatusText = msg);
        await _modService.ApplyAllModsAsync(Mods.ToList(), _settings.GameRootDirectory, progress);

        RefreshConflictReport();
        UpdateStatusBar();
        ShowToast("✅ Operation completed.");
    }
    catch (UnauthorizedAccessException)
    {
        NotificationHelper.ShowError("Permission Error",
            "Unable to copy/delete files. Try running the app as Administrator.");
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

### 2. Service Method (`Services/`)

- Use `StringComparer.OrdinalIgnoreCase` for file path dictionaries
- Apply retry logic for file I/O operations
- Keep methods `static` if they don't need instance state
- Follow the Apply workflow: BuildWinnerMap → CleanManagedFiles → CopyWinners → SaveRegistry → UpdateStatuses

### 3. UI Binding (`MainWindow.xaml`)

- Add a `Button` with `Command="{Binding NewOperationCommand}"` in the toolbar
- Use ModernWpfUI styling: `BasedOn="{StaticResource {x:Type Button}}"`
- Disable during busy: `IsEnabled="{Binding IsBusy, Converter=...}"` or rely on command CanExecute

## Operation to implement

{{input}}
