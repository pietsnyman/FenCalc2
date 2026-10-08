# FenCalc2 Themes

Where themes live, how switching works, and the exact palettes. The palette itself
lives in `App.axaml` (`Application.Resources` → `ResourceDictionary.ThemeDictionaries`);
the switchable color themes are custom `ThemeVariant`s in `AppThemes.cs`.

## How switching works

- **Menu**: `View > Theme` (MainWindow.axaml) → code-behind `MainWindow.SetTheme`
  (MainWindow.axaml.cs) sets `Application.RequestedThemeVariant`, persists the choice
  to the **global `AppSettings` table** (key `Theme`), re-asserts the radio checks and
  calls `MainViewModel.RefreshThemedBrushes()` (re-raises bindings the variant system
  can't reach: compass tile brushes via `CompassConverters`, editor status colors).
- **Startup**: `App.axaml.cs` re-applies the saved value before any window parses XAML.
  Missing key = `Default` (follows the OS).
- **Variants**: `Light`, `Dark`, `Default` are built-in. The three color themes are
  custom variants: `new ThemeVariant(name, ThemeVariant.Light)` — base Light so
  FluentTheme control resources resolve through the variant chain
  (`ThemeVariant.InheritVariant`).
- **GOTCHA**: in `ThemeDictionaries`, a string `x:Key` only converts for built-in
  variants — custom ones MUST be `x:Key="{x:Static local:AppThemes.Olive}"` etc.,
  otherwise the app dies at launch with
  `NotSupportedException: ThemeVariant type converter supports only build in variants`.
- **Rule**: palette brushes must be referenced with **`DynamicResource`** in views and
  styles, or they freeze at load and won't follow a theme switch.

## Palette keys

Surfaces: `SurfaceBrush`, `PanelBrush`, `SunkenBrush`, `HairlineBrush` ·
Text: `TextPrimaryBrush`, `TextSecondaryBrush` ·
Accent: `AccentBrush`, `AccentHoverBrush`, `AccentPressedBrush`, `AccentSoftBrush`,
`AccentTintBrush`, `AccentForegroundBrush` ·
Semantic: `DangerBrush`, `DangerTintBrush`, `SuccessBrush`, `WarningBrush` ·
Compass: `CompassTileBrush` (direction boxes), `CompassNorthBrush` (the box currently
showing the true-North label — must stay a distinctly darker shade of the tile color).

## Color themes (all light; accents darkened for white-button contrast ≥ ~5:1)

### Light (default blue) — also the reference for unchanged keys
| Key | Value |
|---|---|
| Surface / Panel / Sunken | `#FFFFFF` / `#FCFCFD` / `#F7F8FA` |
| Hairline / TextPrimary / TextSecondary | `#E5E7EB` / `#111827` / `#6B7280` |
| Accent / Hover / Pressed | `#3D6DA8` / `#35608F` / `#2C5179` |
| AccentSoft / AccentTint / Foreground | `#E8F0F9` / `#EAF2FB` / `#FFFFFF` |
| Danger / DangerTint / Success / Warning | `#B42318` / `#FDF2F1` / `#15803D` / `#B45309` |
| Tile / North | `#E3F2FD` / `#BBDEFB` |

### Olive (desaturated — muted grayish-olive, round 3 tuning)
| Key | Value |
|---|---|
| Panel / Sunken | `#F5F6EF` / `#EBEDE4` |
| Accent / Hover / Pressed | `#5C644C` / `#4F5642` / `#424837` |
| AccentSoft / AccentTint | `#E8EBE0` / `#EFF0E8` |
| Tile / North | `#EEF0E7` / `#CCD1BC` |

### Burnt Orange (earthy ochre-brown — hue moved off red toward yellow + darkened,
### round 3 tuning)
| Key | Value |
|---|---|
| Panel / Sunken | `#F8F3E7` / `#F1E9D9` |
| Accent / Hover / Pressed | `#7E5A1C` / `#6C4D17` / `#5C4113` |
| AccentSoft / AccentTint | `#F2E8D4` / `#F8F0E1` |
| Tile / North | `#F7F0E1` / `#E4CB9D` |

### Mulberry (the "surprise" third theme — muted violet)
| Key | Value |
|---|---|
| Panel / Sunken | `#F7F4FB` / `#EFEAF4` |
| Accent / Hover / Pressed | `#6C4F89` / `#5C4275` / `#4E3864` |
| AccentSoft / AccentTint | `#E8E0EF` / `#F0EAF5` |
| Tile / North | `#F1ECF6` / `#CBB8DF` |

### Sage (user-specified RGB, 23 Sep)
All four main panels `rgb(245,247,244)`; output box/status bar (Sunken)
`rgb(226,230,223)`; tiles `rgb(193,201,186)`; North `rgb(142,148,133)`.
Because the compass/editor/output panels bind `SurfaceBrush`, Sage sets **Surface =
Panel = `#F5F7F4`**; the image well binds `PanelBrush` (was Sunken) so it stays
uncolored against the compass panel. Derived: sage-tinted hairline + muted sage
accent (white button text ≈6:1).

| Key | Value |
|---|---|
| Surface = Panel / Sunken | `#F5F7F4` / `#E2E6DF` |
| Hairline | `#DCE1D8` |
| Accent / Hover / Pressed | `#5C6553` / `#4F5747` / `#434A3C` |
| AccentSoft / AccentTint | `#E7EAE3` / `#EFF1EC` |
| Tile / North | `#C1C9BA` / `#8E9485` |

Color themes keep `Surface`, `Hairline`, text, semantic colors and
`AccentForegroundBrush` (= white) identical to Light — only the accent family,
Panel/Sunken tints and compass tiles are themed.

### Dark (built-in variant, own full dictionary)
Surfaces `#1A2230` / `#151C28` / `#10161F`, hairline `#2A3546`, text `#E5EAF2` /
`#8B98AB`, accent `#6E9BEF` (+hover/pressed/soft/tint), foreground `#10161F`,
semantic `#E5614F` / `#3A1F1C` / `#3FB27F` / `#D99A4E`, tiles `#1D2939` /
North `#2F4F7E`.

## Wall layer colours (Envelope window)

Defined **once** in the outer `Application.Resources` of App.axaml (outside the theme
dictionaries): `LayerFilmBrush` `#BFD0DE`, `LayerCavityBrush` `#F2F3F5`,
`LayerPlasterBrush` `#EBDCB2`, `LayerMasonryBrush` `#C67B5C`,
`LayerInsulationBrush` `#E5A8B8`, `LayerMetalBrush` `#9AA4AE`, `LayerOtherBrush`
`#C9BFAD`. Mid-tones chosen to read on light AND dark panels, so new themes need no
layer keys. Resolved through `WallConverters.CategoryBrush` at convert time — safe
because these tokens are theme-independent. The verdict brushes (semantic tokens)
are re-raised after a theme switch by `WallComplianceViewModel.RefreshThemedBrushes`
via `MainViewModel.RefreshThemedBrushes` — required now that the walls page is a TAB
and the View > Theme menu stays reachable while it is displayed.

## Adding a theme (checklist)

1. Add a static `ThemeVariant` in `AppThemes.cs` + a `FromName` case + a const name.
2. Add a **full** `ResourceDictionary` in `App.axaml` keyed
   `{x:Static local:AppThemes.YourTheme}` (copy Light, override the accent family,
   Panel/Sunken, and both compass keys — North must stay distinct from Tile).
3. `App.axaml.cs` startup switch: `YourName => AppThemes.YourTheme`.
4. `MainWindow.axaml`: menu item (x:Name + `ToggleType="Radio"` + Click handler).
5. `MainWindow.axaml.cs`: click handler → `SetTheme(AppThemes.YourName)`, add the
   mode to the `SetTheme` switch and the `IsChecked` sync (ctor + `SetTheme`).
6. Update this file's palette table.

Known limit: Fluent's own focus ring stays its default blue (theme-level accent
re-theme deliberately out of scope).
