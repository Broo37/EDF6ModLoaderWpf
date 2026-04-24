# Bug Fix Report — EDF6 Mod Manager

**Date:** 2026-04-24  
**Reviewed by:** Engineering code review (superpowers:code-reviewer)  
**Fixed by:** Claude Code  
**Build status after fixes:** ✅ 0 warnings, 0 errors

---

## Summary

A full engineering review was performed on the initial commit of the EDF6 Mod Manager WPF application. The review identified 4 critical issues, 3 important issues, and several minor items. This document records every finding, the root cause analysis, whether it was a real bug or a false positive, and the fix applied.

---

## Critical Issues

### C1 — `async void` event handler swallows exceptions

**File:** `MainWindow.xaml.cs` — `GroupTextBox_LostFocus`  
**Status:** ✅ Fixed  

**Root cause:**  
WPF event handlers must be `void`-returning, so `async void` is unavoidable at the handler boundary. The problem was that `SetGroupAsync` was `await`-ed inside an `async void` method with no `try/catch`. Any exception thrown by `SetGroupAsync` (e.g., I/O failure saving the registry) would propagate onto the WPF Dispatcher's message loop as an unhandled exception, either crashing the app or silently disappearing — the user would see no feedback.

**Fix:**  
Wrapped the entire async body in `try/catch`. Exceptions are logged via `SettingsService.LogErrorAsync` and surfaced to the user via `NotificationHelper.ShowError`.

```csharp
// Before
private async void GroupTextBox_LostFocus(object sender, RoutedEventArgs e)
{
    // ...
    await _viewModel.SetGroupAsync(mod, newGroup);
    ApplyGrouping();
}

// After
private async void GroupTextBox_LostFocus(object sender, RoutedEventArgs e)
{
    // ...
    try
    {
        await _viewModel.SetGroupAsync(mod, newGroup);
        ApplyGrouping();
    }
    catch (Exception ex)
    {
        await SettingsService.LogErrorAsync(ex);
        NotificationHelper.ShowError("Group Error", ex.Message);
    }
}
```

**Rule:** Every `async void` method (event handlers only) must contain a top-level `try/catch`. Never rely on the WPF dispatcher to handle async exceptions.

---

### C2 — `App.OnStartup` had no top-level error handler

**File:** `App.xaml.cs` — `OnStartup`  
**Status:** ✅ Fixed  

**Root cause:**  
`OnStartup` is an `async void` override (required by the WPF framework). Any exception thrown during startup — DI container build failure, settings corruption, missing font, etc. — would propagate as an unhandled exception with no recovery path. The application would crash with a raw .NET exception dialog, and the error would not be logged.

**Fix:**  
Wrapped the entire startup sequence in `try/catch`. On failure, the error is logged and a friendly `MessageBox` is shown before `Shutdown(1)` is called.

```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    try
    {
        // ... full startup ...
    }
    catch (Exception ex)
    {
        await SettingsService.LogErrorAsync(ex);
        MessageBox.Show($"The application failed to start:\n\n{ex.Message}", ...);
        Shutdown(1);
    }
}
```

**Note:** `SettingsService.LogErrorAsync` is a `static` method that writes directly to `%AppData%\EDFModManager\error.log`. It does not depend on the DI container, so it is safe to call even if the container failed to build.

---

### C3 — Retry loop silent failure (REVIEWER FALSE POSITIVE)

**Files:** `ModService.cs` — `CopyWithRetryAsync`, `DeleteWithRetryAsync`; `FileService.cs` — `CopyModFilesAsync`, `DeleteModFilesAsync`  
**Status:** ⚠️ Not a bug — clarifying comments added  

**Reviewer claim:** The `catch (IOException) when (i < MaxRetries - 1)` pattern silently swallows the exception on the final retry attempt.

**Root cause analysis:**  
This claim is **incorrect**. In C#, when an exception filter (`when (condition)`) evaluates to `false`, the catch block does **not** execute and the exception is **not** caught. It propagates naturally up the call stack — identical to there being no catch block at all.

```
i = 0,1,2,3 → when (i < 4) = true  → catch, delay, retry
i = 4       → when (4 < 4) = false → exception NOT caught → propagates to caller ✅
```

This is the **correct and idiomatic C# retry pattern**. The exception is never swallowed.

**What was done:**  
Added explicit comments to each retry loop documenting that the last-attempt propagation is intentional, to prevent future confusion:

```csharp
catch (IOException) when (i < MaxRetries - 1)
{
    await Task.Delay(RetryDelayMs * (i + 1));
}
// Final attempt: IOException propagates to caller — no silent swallow.
```

---

### C4 — Undo passes list copy (REVIEWER FALSE POSITIVE)

**File:** `ViewModels/MainViewModel.cs` — `Undo`  
**Status:** ⚠️ Not a bug — clarity improvement applied  

**Reviewer claim:** `_loadOrderService.TryUndo(Mods.ToList())` passes a copy, so changes made in `TryUndo` don't reflect in the UI.

**Root cause analysis:**  
This claim is **incorrect**. `Mods.ToList()` creates a new `List<ModEntry>` container, but it holds **references to the same `ModEntry` objects** that live in the `ObservableCollection<Mods>`. `TryUndo` only sets properties on those objects (`mod.IsActive`, `mod.LoadOrder`, `mod.Group`). Because `ModEntry` extends `ObservableObject`, every property setter fires `PropertyChanged`, which WPF's DataGrid binding picks up directly. The UI **does** update.

The confusion arises because `ObservableCollection<T>` raises its own `CollectionChanged` event for add/remove/move operations, but that is not what `TryUndo` does — it only mutates existing items.

**What was done:**  
Changed from `Mods.ToList()` to `Mods` (passing the actual `ObservableCollection` directly). This is functionally identical but eliminates the unnecessary list allocation and removes the ambiguity:

```csharp
// Before (works, but misleading)
_loadOrderService.TryUndo(Mods.ToList());

// After (same effect, clearer intent)
_loadOrderService.TryUndo(Mods);
```

---

## Important Issues

### I1 — Memory leak: `PropertyChanged` subscription never unsubscribed

**File:** `MainWindow.xaml.cs`  
**Status:** ✅ Fixed  

**Root cause:**  
The ViewModel's `PropertyChanged` event was subscribed using an anonymous lambda in `InitializeAsync`. Because anonymous lambdas cannot be unsubscribed by reference, the subscription was never cleaned up. When `MainWindow` closes, the handler keeps the `MainViewModel` object alive in memory (the ViewModel holds a reference to the window's `ApplyGrouping` method via the closure, creating a reference cycle).

In a single-window application this is a minor leak. However, if the window is ever recreated (e.g., after a restart flow), the old subscription would still fire.

**Fix:**  
Extracted the lambda to a named method `OnViewModelPropertyChanged` and added an `OnClosed` override to unsubscribe:

```csharp
// InitializeAsync
_viewModel.PropertyChanged += OnViewModelPropertyChanged;

// Named handler (can be unsubscribed)
private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
{
    if (args.PropertyName == nameof(MainViewModel.IsGroupViewActive))
        ApplyGrouping();
}

// Cleanup
protected override void OnClosed(EventArgs e)
{
    if (_viewModel is not null)
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    base.OnClosed(e);
}
```

**Rule:** Never subscribe to an event on an object with a longer lifetime using an anonymous lambda unless you explicitly manage the subscription lifetime. Always prefer named handlers when you need to unsubscribe.

---

### I2 — `Process.Start("explorer.exe", ...)` had no error handling

**File:** `ViewModels/MainViewModel.cs` — `OpenGameFolder`, `OpenModsLibrary`  
**Status:** ✅ Fixed  

**Root cause:**  
`Process.Start` can throw if the executable is not found, the path is invalid, or OS security policies block the call. With no `try/catch`, any failure would propagate as an unhandled exception from a `[RelayCommand]` method — crashing the operation silently or showing a raw exception dialog.

**Fix:**  
Wrapped each call in `try/catch`. Errors are logged and surfaced as a toast notification:

```csharp
try
{
    Process.Start("explorer.exe", ActiveGame.GameRootPath);
}
catch (Exception ex)
{
    _ = SettingsService.LogErrorAsync(ex);
    ShowToast("Could not open game folder.", isError: true);
}
```

**Note:** `LogErrorAsync` is fire-and-forgotten here (`_ = ...`) because this is a non-critical logging call inside a synchronous command. This is the one acceptable use of fire-and-forget in the codebase.

---

### I3 — No test project

**Status:** 📋 Documented (not yet implemented)  

The codebase has zero automated tests. The following units have deterministic, pure logic that is straightforward to test:

| Class | Methods to test |
|-------|----------------|
| `ConflictService` | `BuildWinnerMap` (ordering, last-writer-wins, inactive mods excluded) |
| `ConflictService` | `DetectAllConflicts` (pairwise detection, correct winner/loser assignment) |
| `LoadOrderService` | `TryUndo` / `PushUndoState` (stack limit, restore accuracy) |
| `LoadOrderService` | `ResequenceLoadOrders` (gaps filled, inactive = 0) |
| `LoadOrderService` | `AssignNextLoadOrder` (next = max + 1) |
| `ModService` | `ApplyAllModsAsync` (workflow sequence: BuildWinnerMap → Clean → Copy → Save → Update) |

**Recommended setup:**
```xml
<!-- Add to solution -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="NSubstitute" Version="5.*" /> <!-- for mocking FileService -->
  </ItemGroup>
</Project>
```

---

## Minor Issues Fixed

### M1 — Dead code: `ConflictService.ResolveConflict`

**File:** `Services/ConflictService.cs`  
**Status:** ✅ Removed  

`ResolveConflict(ModEntry modA, ModEntry modB)` was a public static method that existed but was never called anywhere. `BuildWinnerMap` performs conflict resolution inline using `OrderBy(m => m.LoadOrder)`. The method was removed to avoid confusion about which resolution path is canonical.

---

## Patterns to Remember

### The `async void` Rule
WPF event handlers are the **only** place `async void` is acceptable. Every such method **must** have a top-level `try/catch` to prevent exceptions from escaping onto the dispatcher.

### The Retry Pattern
The `catch (IOException) when (i < MaxRetries - 1)` idiom is correct C#. When the filter is `false`, the exception is **not** caught — it propagates naturally. This is preferable to an explicit rethrow because it preserves the original stack trace.

### `ObservableCollection` vs. `List` — Reference Semantics
Passing `collection.ToList()` to a method that only modifies **properties** of existing elements is functionally equivalent to passing the collection directly. Both contain references to the same objects. The UI updates correctly in both cases via `INotifyPropertyChanged`. Prefer passing the actual collection to avoid unnecessary allocations and to signal intent clearly.

### Event Handler Lifetime
Always use named methods (not lambdas) when subscribing to events on objects with a longer lifetime. This allows proper unsubscription in `OnClosed` / `Dispose` and prevents memory leaks and phantom callbacks.
