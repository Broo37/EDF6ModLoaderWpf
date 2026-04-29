# UI Redesign — Sci-Fi Command Center

**Date:** 2026-04-29
**Scope:** Full visual redesign of `MainWindow.xaml` and supporting theme infrastructure. No changes to business logic or services. ViewModels receive minor property additions for sidebar state only.

---

## Goals

- Dramatic visual upgrade: Sci-Fi Command Center aesthetic with deep navy/purple + cyan glow palette
- Light/dark mode: follows Windows system theme by default, user can override via sidebar toggle (persisted in `AppSettings`)
- Unified color palette (no per-game accent color switching)
- Layout restructure: collapsible left sidebar + main content area
- Emojis used purposefully as meaning-bearing indicators, not decoration

---

## Color Palette

### Dark Mode

| Token | Hex | Usage |
|---|---|---|
| `AppBg` | `#0B0F1A` | Window background |
| `Surface` | `#111827` | Cards, panels |
| `SurfaceBorder` | `#1E2D4A` | Card borders, dividers |
| `SidebarBg` | `#0D1323` | Left sidebar background |
| `Accent` | `#6C63FF` | Primary buttons, active highlights, glow |
| `AccentCyan` | `#00D4FF` | Group text, links, secondary info |
| `Success` | `#22C55E` | Active mod indicator |
| `Warning` | `#F59E0B` | Conflicts, overridden status |
| `Danger` | `#EF4444` | High risk, errors |
| `TextPrimary` | `#E2E8F0` | Main text |
| `TextMuted` | `#64748B` | Secondary/meta labels |
| `StatusBar` | `#080C15` | Status bar background |

### Light Mode

| Token | Hex | Usage |
|---|---|---|
| `AppBg` | `#F0F4FF` | Window background |
| `Surface` | `#FFFFFF` | Cards, panels |
| `SurfaceBorder` | `#C7D2FE` | Card borders |
| `SidebarBg` | `#EEF2FF` | Left sidebar background |
| `Accent` | `#5B50E8` | Primary buttons, highlights |
| `AccentCyan` | `#0EA5E9` | Group text, links |
| `Success` | `#16A34A` | Active mod |
| `Warning` | `#D97706` | Conflicts |
| `Danger` | `#DC2626` | High risk |
| `TextPrimary` | `#1E1B4B` | Main text |
| `TextMuted` | `#6B7280` | Secondary labels |
| `StatusBar` | `#E8ECF8` | Status bar background |

### Implementation

- Two `ResourceDictionary` files: `Themes/DarkTheme.xaml` and `Themes/LightTheme.xaml`
- Each defines the tokens above as `SolidColorBrush` resources with consistent keys
- Loaded/swapped at runtime via `Application.Current.Resources.MergedDictionaries`
- `ThemeHelper` updated with:
  - `ApplyTheme(ApplicationTheme theme)` — swaps dictionaries + calls `ThemeManager.Current.ApplicationTheme`
  - `GetSystemTheme()` — reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`
  - `LoadSavedOrSystemTheme()` — called on startup; reads `AppSettings.ThemeOverride`, falls back to system
- `AppSettings` gets a new `string? ThemeOverride` field (`"Dark"`, `"Light"`, or `null` = follow system)

---

## Layout Structure

### Overall Window Grid

```
┌──────────────────────────────────────────────────────┐
│  SIDEBAR (260px / 56px collapsed) │  MAIN CONTENT    │
│                                   │                  │
│  [always visible]                 │  [fills rest]    │
└──────────────────────────────────────────────────────┘
```

`MainWindow.xaml` root is a two-column `Grid`:
- `Column 0`: sidebar `Border`, width driven by `SidebarWidth` bound property
- `Column 1`: main content `Grid`

---

## Left Sidebar

### Expanded (260px) — Full Labels + Icons

```
┌───────────────────────────┐
│  🛡️  EDF MOD MGR    [◀]  │  ← Branding + collapse button
├───────────────────────────┤
│  GAME                     │
│  [🔵 EDF 4.1   ✅ Active] │  ← Active: purple glow border
│  [🟢 EDF 5     Ready    ] │
│  [🔴 EDF 6     Ready    ] │
├───────────────────────────┤
│  STATS                    │
│  ┌──────────┬──────────┐  │
│  │🧩 Total  │✅ Active │  │
│  │  12      │   8      │  │
│  ├──────────┼──────────┤  │
│  │⚠️ Confl. │🔥 Risk  │  │
│  │   3      │   1      │  │
│  └──────────┴──────────┘  │
├───────────────────────────┤
│  DEPLOY                   │
│  [🚀 Apply Mods         ] │  ← Solid purple, glow shadow
│  [🕹️ Apply + Launch     ] │  ← Outlined purple
│  [🎮 Launch Game        ] │  ← Outlined purple
│  ◉ StatusText (muted)    │  ← ProgressRing + status when busy
├───────────────────────────┤
│  (spacer)                 │
├───────────────────────────┤
│  [⚙️ Settings           ] │
│  [🌙 Dark Mode          ] │  ← Shows ☀️/🌙/🖥️ per state
│  [◀ Collapse            ] │
└───────────────────────────┘
```

### Collapsed (56px) — Icons Only

Elements shown when collapsed:
- 🛡️ logo icon only
- Active game colored dot (🔵/🟢/🔴)
- Four stat dots (hidden — too small to be useful at 56px)
- 🚀 / 🕹️ / 🎮 icons only, stacked
- ⚙️ / ☀️🌙🖥️ / ▶ at bottom

### Sidebar Visual Details

- Left edge: permanent 3px vertical stripe in `#6C63FF` (visible even when collapsed)
- Active game button: `BorderBrush=Accent`, `BorderThickness=2`, subtle `Accent` bg at 10% opacity
- `🚀 Apply Mods`: `Background=Accent`, `Foreground=White`, `DropShadowEffect(Color=Accent, BlurRadius=16, Opacity=0.6)`
- `🕹️` and `🎮` buttons: transparent bg, `BorderBrush=Accent`, `BorderThickness=1`
- Stats grid: 2×2 `UniformGrid` of mini-cards; big `FontSize=24` numbers, emoji kicker above
- Collapse animation: `DoubleAnimation` on `SidebarWidth`, 200ms `CubicEase EaseOut`
- Section kicker labels (GAME, STATS, DEPLOY): `FontSize=10`, `FontWeight=SemiBold`, letter-spacing via `CharacterSpacing`, `Foreground=TextMuted`
- Collapse/expand button: `◀` / `▶` at top-right corner of sidebar

---

## Main Content Area

### Filter + Toolbar Bar (single compact row)

```
[🔍 Search mods...     ] [✅ Active] [⚠️ Conflicts] [🔥 Risk] [🧹]  |  [🔄] [📥 Import▾] [🧰 Manage▾] [🗂️ View▾] [📚 Loadout▾]
```

- Search box: grows to fill available space, `CornerRadius=8`
- Filter chips: pill-shaped `ToggleButton` style — inactive = outlined `AccentCyan` border, active = filled `AccentCyan` bg
- Separator `|` between filters and toolbar buttons
- Toolbar buttons: no border, emoji + text, hover shows subtle bg (`Surface` tint)
- Loadout button opens a `Popup` or `ContextMenu` with preset management options (Save/Load/Rename/Delete + name input)

### Mod DataGrid

**Columns:**

| Header | Width | Notes |
|---|---|---|
| ✅ | 46px | Accent-colored when checked |
| # | 54px | Glowing `Accent` pill badge, editable on click |
| 📦 Mod | * | Mod name bold, description muted below in 11px |
| 📂 Group | 120px | `AccentCyan` italic, editable |
| 🗃️ Folders | 150px | Muted, `CharacterEllipsis` |
| ⚠️ | 50px | Centered emoji indicator |
| 🔄 Status | 120px | Colored pill: Active=`Accent` glow, Overridden=`Warning`, Inactive=`TextMuted` |
| ⬆⬇ | 76px | Up/down buttons, visible on row hover |
| ℹ️ | 44px | Tooltip trigger |

**Row visual details:**
- `MinHeight=48px`
- Active mod: 3px left `Success` glow stripe via nested `Border`
- High-risk mod: very faint `Danger` background tint (`Opacity=0.05`)
- Hover: `Surface` bg steps up 1 shade
- Selected: `Accent` left stripe, elevated surface bg
- Drag handle `⠿` appears at left edge on hover

**Group headers** (Group View):
- Expander with `📂 GroupName (N mods)`
- Header: darker surface bg, `Accent` left border 2px, `FontWeight=SemiBold`

### Recent Imports Drawer

- Slim `▲ 🕘 Recent Imports (3)` header bar when collapsed (always visible if `HasRecentImports`)
- Click header → `DoubleAnimation` on `Height` 0 → content height, 150ms
- Content: horizontal `ScrollViewer` with import cards
- Each card: `📦` emoji, mod name bold, source/date/files muted, Pin/Select/Open buttons
- Pinned cards: `📌` badge, appear first in scroll

### Conflict Radar Drawer

- Same collapsible pattern: `▲ ⚠️ 3 Conflicts Detected` header bar
- Header pulses with amber glow (`ColorAnimation` on `DropShadowEffect`) when conflicts > 0
- Content: vertical list of conflict cards (file path, 🏆 winner, 🧯 loser, action buttons)

### Status Bar

- `Height=32px`, `Background=StatusBar`
- Top edge: 1px border in `SurfaceBorder` with slight `Accent` tint
- Left: 🛰️ + `StatusText` (ellipsis trimmed)
- Right: `ProgressRing` (`Width=18`, `Height=18`) visible only when `IsBusy`

### Drag-and-Drop Overlay

- `DropOverlay` is a full-window overlay (existing behavior preserved)
- In the new 2-column grid layout it must use `Grid.ColumnSpan="2"` to cover both sidebar and main content
- Visual style updated to use `Accent` border + `Surface` bg consistent with new palette

---

## Typography

| Role | Size | Weight |
|---|---|---|
| App branding | 16px | Bold |
| Section kicker | 10px | SemiBold, muted |
| Section title | 14px | SemiBold |
| Body / mod name | 13px | SemiBold for names, Regular for description |
| Meta / muted | 11px | Regular, `TextMuted` color |
| Stat numbers | 24px | SemiBold |
| Status badge | 12px | SemiBold |

Font: Segoe UI (WPF default — no change needed).

---

## Animations & Polish

| Element | Animation | Duration |
|---|---|---|
| Sidebar collapse/expand | `DoubleAnimation` on `SidebarWidth` | 200ms `CubicEase EaseOut` |
| Drawer expand/collapse | `DoubleAnimation` on `Height` | 150ms |
| Toast notification | Slide in from bottom, fade out | 300ms in / 400ms out |
| Conflict drawer header | `ColorAnimation` on `DropShadowEffect` (amber pulse) | 800ms loop |
| Theme switch | None (instant) | — |
| Row hover / selection | Trigger-based color change | Instant |

---

## Theme Toggle Logic

**States:** Dark | Light | System (auto)
**Toggle cycles:** Dark → Light → System → Dark → ...

**Button display:**
- Dark mode active: shows `☀️ Light`
- Light mode active: shows `🌙 Dark`
- System auto: shows `🖥️ Auto`

**Startup flow:**
1. Read `AppSettings.ThemeOverride`
2. If set → apply that theme
3. If null → call `ThemeHelper.GetSystemTheme()` → apply result
4. Subscribe to system theme change events (`SystemEvents.UserPreferenceChanged`) to auto-update when override is null

---

## Files Changed

| File | Change |
|---|---|
| `Themes/DarkTheme.xaml` | New — dark palette `ResourceDictionary` |
| `Themes/LightTheme.xaml` | New — light palette `ResourceDictionary` |
| `Helpers/ThemeHelper.cs` | Updated — adds theme switching + system detection |
| `Models/AppSettings.cs` | Updated — adds `ThemeOverride` property |
| `MainWindow.xaml` | Full restructure — sidebar + main content layout |
| `MainWindow.xaml.cs` | Updated — sidebar collapse animation, theme toggle handler |
| `ViewModels/MainViewModel.cs` | Minor — adds `SidebarWidth`/`IsSidebarCollapsed` observable properties |
| `App.xaml` | Updated — initial theme dictionary merge |

---

## Out of Scope

- Settings window, Welcome window, Import Preview window, Apply Summary window — visual updates deferred to a follow-up pass
- Per-game accent colors — intentionally removed per design decision
- New features or business logic changes
