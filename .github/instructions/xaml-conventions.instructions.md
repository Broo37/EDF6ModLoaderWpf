---
applyTo: "**/*.xaml"
description: "Use when editing XAML files. Covers ModernWpfUI styling, namespace conventions, DataGrid patterns, and data binding rules for this WPF project."
---
# XAML Conventions

## Namespaces

Always include the ModernWpfUI namespace and apply modern window style:

```xml
xmlns:ui="http://schemas.modernwpf.com/2019"
ui:WindowHelper.UseModernWindowStyle="True"
```

## DataGrid Columns

- Checkbox columns: bind `IsChecked="{Binding IsActive, Mode=TwoWay}"`, handle toggle in code-behind event → delegate to ViewModel command
- Editable text columns: use `UpdateSourceTrigger=LostFocus` to commit on blur
- Display-only columns: use `TextBlock` with `OneWay` binding
- Button columns: bind `Command` to ViewModel relay commands, pass the row item via `CommandParameter="{Binding}"`

## Binding Patterns

- **TwoWay:** Checkboxes, editable text fields (`IsActive`, `LoadOrder`, `Group`)
- **OneWay:** Labels, status text, display-only properties
- **UpdateSourceTrigger=PropertyChanged:** Real-time search/filter text inputs
- **MultiBinding with StringFormat:** For composite display text like `"{}{0} ({1} mods)"`

## Styles

- Base custom styles on the framework type: `BasedOn="{StaticResource {x:Type Button}}"`
- Define reusable styles in `<Window.Resources>` with a descriptive `x:Key`
- Use `BooleanToVisibilityConverter` (key: `BoolToVis`) for visibility toggles

## Layout

- Use `Grid` with `RowDefinitions` for major layout sections
- Toolbar/action bars: `StackPanel Orientation="Horizontal"` with consistent `Margin="0,0,8,0"` spacing
- Status bar: bottom `Grid.Row` with `StatusText` binding
- Conflict/info panels: toggle visibility via `Visibility="{Binding IsConflictPanelVisible, Converter={StaticResource BoolToVis}}"`

## Don'ts

- Don't put business logic in code-behind — delegate to ViewModel commands
- Don't use inline event handlers for async operations — use `[RelayCommand]` bindings
- Don't hardcode colors — rely on ModernWpfUI theme resources
