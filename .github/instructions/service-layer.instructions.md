---
applyTo: "Services/**"
description: "Use when editing service classes. Covers file path comparison, retry logic, winner map contract, and singleton service conventions."
---
# Service Layer Conventions

## File Path Handling

Always use case-insensitive comparison for file paths — Windows filesystem is case-insensitive:

```csharp
new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
new HashSet<string>(StringComparer.OrdinalIgnoreCase);
```

Exclude `mod_info.json` when enumerating mod files:

```csharp
.Where(f => !f.EndsWith("mod_info.json", StringComparison.OrdinalIgnoreCase))
```

## BuildWinnerMap Contract

The winner map determines which mod's file gets deployed. It must:
1. Filter to `IsActive` mods only
2. Iterate in ascending `LoadOrder` order (lowest first)
3. Use last-writer-wins — higher LoadOrder overwrites earlier entries
4. Return `Dictionary<string, ModEntry>` keyed on relative file path

## Apply Workflow

Any operation that changes mod state must follow this sequence:
```
BuildWinnerMap → CleanManagedFiles → CopyWinners → SaveRegistry → UpdateStatuses
```
Never skip steps or reorder them.

## File Operation Retry

Use retry logic for copy/delete operations to handle transient file locks:
- 5 attempts maximum
- 200ms exponential backoff: `delay = 200 * (attempt + 1)`
- Catch `IOException` and `UnauthorizedAccessException` per attempt
- Rethrow on final attempt failure

## Service Design

- Services are registered as **Singletons** — do not store per-request state in fields
- Use `static` for pure utility methods that don't need instance state (e.g., `GetModRelativeFiles`, `BuildWinnerMap`)
- Accept dependencies via constructor injection
- JSON persistence: use `System.Text.Json` with `WriteIndented = true`
- Always `Directory.CreateDirectory(destDir)` before writing files

## Standard Subfolders

The valid mod subfolders in `[GameRoot]\Mods\` are:
`DEFAULTPACKAGE`, `MainScript`, `Mission`, `Object`, `Patches`, `Plugins`, `Weapon`

Reference via `FileService.StandardSubfolders` — don't hardcode elsewhere.
