# Calculation Engine, Product Library and Cutting Plan (Milestones 6–7)

```text
Project (design, mm)  +  IProductLibrary (products)  +  CalculationRules (fabrication)
                                   ↓
                     ICalculationEngine.Calculate(...)
                                   ↓
                           CalculationResult
        profiles · glass · materials · cut list · BOM · cost · weight · issues
```

`Mark.Calculation` is plain `net8.0` and references **Core only**: no WPF, no Designer, no view models
(`ArchitectureTests` enforce this). The view models only format its results.

## Product library (`Core/Library`)

Products are **data**, never code. The designer stores only a stable **Id**; everything product-specific is read
from the library when needed.

| Definition | Referenced by | Holds |
|---|---|---|
| `ProfileDefinition` | `Profile.ProfileDefinitionId` | roles (frame/mullion/transom), face width, depth, kg/m, cost/m, stock lengths (`stockLengthMm` + optional `stockLengthsMm` list), cut allowance per end, glazing bite, material usages |
| `GlassDefinition` | `GlassPanel.GlassDefinitionId` | category (free text: Float, Toughened…), thickness, cost/m², kg/m², minimum chargeable area, material usages |
| `MaterialDefinition` | `MaterialUsage.MaterialId` in a profile/glass definition | category (`Hardware`, `Gasket`, `Accessory`, `Consumable`), unit (piece, metre, m²), cost/unit |
| `LibraryDefaults` | — | the default frame/mullion/transom profile and glass for objects with no reference |

Hardware, gaskets and accessories are one `MaterialDefinition` type with a category, not separate classes: they
behave identically in the calculation, and a new kind of item is a new category value, not new code.

- `IProductLibrary` is read-only; `ProductLibrary` is immutable and **validated on construction** (unique,
  non-blank ids and names; non-negative prices and weights; valid thicknesses; usages point at existing materials;
  defaults exist and fit their role). An invalid library throws `LibraryValidationException` listing every error.
- `LibrarySerializer` reads/writes JSON (camelCase, enum names as strings, comments allowed, versioned). Since M8 the
  app's library lives in the local SQLite database (see [persistence.md](persistence.md)): `Library\library.json` is
  imported into it on first run and remains the import/export format. The calculation still receives an
  `IProductLibrary` snapshot and never touches the database.
- **Search.** `SearchProfiles` / `SearchGlass` / `SearchMaterials` take a `LibraryQuery`. Every word of the text must
  occur (case-insensitively) in the id, name, code, manufacturer or series/category, so `"8 tough"` finds
  "8mm Toughened". Optional filters: `Role`, `Manufacturer`, `Group` (profile series / glass category),
  `MaterialCategory`, `IncludeInactive`, `Limit`. The properties-panel pickers show the first 50 matches and narrow as
  you type, so large libraries stay usable.
- **Retired products** (`IsActive = false`) are not offered by the pickers but still resolve by id, so existing designs
  keep pricing; the calculation adds a warning for each design object that uses one.

### References

- `null` means "the library default for this role". New objects are created with no reference, and the drawing
  defaults (`DesignRules` face widths and glass thickness) follow the library defaults, so drawing and pricing agree.
- A reference is never validated against the library on load, because a project may be opened with a different
  library. A blank reference is rejected by `ValidationHelper.EnsureValidProject`. An id that is not in the library
  is reported by the calculation (see Issues).
- References survive every edit: snapshots, undo/redo, moves and splits. Splitting a pane gives both new panes
  the old pane's glass type.

## Calculation rules (`CalculationEngine`)

Deterministic: no clock, no randomness, ordinal sorting, fixed rounding (`CalculationRules`: lengths 1 dp, areas
4 dp, quantities/weights 3 dp, money 2 dp, midpoint away from zero). Money is `decimal`. The engine never modifies
the project.

| Item | Rule |
|---|---|
| Outer frame member | Mitre joint (default): cut to the outer width/height, 45° both ends. Butt joint: verticals run through, horizontals = width − both vertical face widths, 90° cuts. |
| Mullion / transom | Face-to-face body length + 2 × the definition's `CutAllowancePerEndMm`, square cuts. |
| Profile weight / cost | metres of cut length × kg/m / cost per metre. |
| Glass size | Face-to-face opening (`GlassPanel.Boundary`) + the glazing bite of each surrounding profile − 2 × `GlassEdgeClearanceMm`, per direction. |
| Glass area / cost | width × height in m²; cost = max(area, `MinChargeableAreaM2`) × cost per m². Weight = area × kg/m² when the library gives it. |
| Materials | Each `MaterialUsage` of the profile/glass definition: per piece (per profile piece / glass pane), per metre (of cut length / glass perimeter) or per m² (glass only). |

### Result (`CalculationResult`)

- `Profiles` / `Glass` / `Materials`: one line per design object (or per usage), carrying the `Profile.Id`,
  `GlassPanel.Id` and `Frame.Id` it came from, so results map back to the drawing.
- `CutList`: identical pieces grouped by profile, length and cut angles, longest first, with the stock length.
  This is the input for M7 cutting optimisation.
- `Bom`: aggregated rows. Profiles per definition (pieces, total length); glass per definition **and size**;
  materials per item, ordered by category. Each row has quantity, unit, weight and cost.
- `Cost` (`CostSummary`: profiles, glass, materials, total), `WeightKg`, and per-frame `Frames`.
- `Issues`: problems are reported, not thrown, so the rest of the design is still calculated.

| Situation | Severity | Effect |
|---|---|---|
| Reference not in the library / no reference and no default | Error | line kept with its geometry, `IsResolved = false`, not priced, not in BOM |
| Profile used in a role it does not support | Error | reported |
| No length / no glass left after the clearance | Error | reported |
| Drawn width/thickness differs from the library definition | Warning | priced from the library |

`IsComplete` is true when there are no errors.

## Changing a material after design

```text
Properties picker → MainViewModel.AssignGlass / AssignProfile(id)
  → AssignGlassCommand / AssignProfileCommand per frame (CompositeCommand when several frames)
  → FrameEditor.TryAssignGlass / TryAssignProfile: validate (id exists, role fits, objects belong to the frame)
  → edit a scratch copy, re-derive glass, validate the layout, write back (or change nothing)
  → CommandHistory records ONE undo step → HistoryChanged
  → CalculationService.Invalidate() → Result recalculates on next read → BOM, cost and properties refresh
```

- **Glass.** Sets `GlassDefinitionId` and mirrors the definition's thickness onto `GlassPanel.Thickness`. Geometry and
  Ids are unchanged.
- **Profile.** Sets `ProfileDefinitionId` and the member's face width from the definition. The outer size of the frame
  never changes: an outer member keeps its outside face, so its centreline moves and divisions ending on it follow.
  Divisions keep their centreline. Glass is re-derived (a wider frame gives smaller glass). Rejected if the section
  cannot be used in that role or the result would be invalid (e.g. glass smaller than the minimum).
- **What the selection stands for.** A glass panel stands for itself; a mullion or transom for itself; a frame for
  all its panes (glass) and its four outer members (profile). This works for any selection, including Shift+click,
  box selection and Ctrl+A, and gives one undo step across frames. It is **all-or-nothing**: if any frame
  rejects the change, the frames already changed are rolled back and nothing is recorded.
- The profile picker for a selection with several roles offers only sections usable in **every** role (e.g. a
  mullion and a transom together list mullion/transom sections only).
- Undo/redo restore references, thicknesses and geometry exactly (frame snapshots), and the calculation follows.

## Cutting plan (Milestone 7)

```text
CalculationResult.Profiles (one ProfileLine per piece: library id, cut length, angles, source Profile/Frame Id)
  + IProductLibrary (stock lengths, cost/m, kg/m)  + CalculationRules.Cutting (kerf, trim, minimum offcut)
        ↓  ICuttingOptimizer.Optimize
CuttingPlan → ProfileCuttingPlan per library profile → StockBar per bar → its ProfileLine cuts
```

`CuttingOptimizer` lives in `Mark.Calculation` (no WPF). `CalculationService.CuttingPlan` gives the plan
for the current design, computed on first read after a change and cached; the designer only formats it
(`CuttingPlanViewModel`).

### Inputs

| Data | Source |
|---|---|
| Pieces | `ProfileLine`s from M6, so the cut length already includes `CutAllowancePerEndMm` (not added again). Unresolved lines are reported, not planned. |
| Stock lengths | `ProfileDefinition.AvailableStockLengthsMm()`: `stockLengthMm` plus the optional `stockLengthsMm` list, deduplicated, shortest first. |
| Price / weight | `CostPerMetre`, `WeightKgPerMetre` of the definition. |
| Kerf, trim, minimum usable offcut | `CuttingRules` in `CalculationRules.Cutting`. The app reads them from `Settings\calculation-rules.json` (`CalculationRulesSerializer`). Defaults are neutral: 0, 0, 0. |

### Rules of a bar

- Usable length = stock length − `TrimAllowanceMm` (trim squares the factory end, its own cut included).
- Every piece is separated from the rest of the bar by one saw cut costing `KerfMm`, except a piece that ends
  exactly at the end of the usable length. So n pieces cost n − 1 or n kerfs.
- `RemainingMm = stock − trim − cuts − kerf`. If it is at least `MinUsableOffcutMm` (equal counts as usable) it is a
  reusable **remnant**, otherwise **waste**.
- `WasteMm = trim + kerf + an unusable leftover`, so every bar balances: `stock = cuts + remnant + waste`.

### Algorithm (deterministic heuristic)

1. Expand quantities into single pieces; group by library profile id; process groups in ordinal id order.
2. Sort pieces by length descending, then by position in the input (stable, never by hash or dictionary order).
3. **Best-Fit Decreasing:** each piece goes into the open bar it leaves the least room in (earliest bar on ties);
   if none fits, a new bar is opened.
4. **Multiple stock lengths:** step 3 runs once per available stock length, used as the length new bars are opened
   with (a piece longer than that opens the shortest bar it fits). Then every bar is shrunk to the shortest stock
   length that still holds its pieces. The run that takes the least stock material wins; ties go to fewer bars,
   then the shorter opening length.
5. A piece longer than the longest usable bar, or a profile with no stock length, is reported and listed in
   `Unplaced`. The rest is still planned.

Cost is O(s · n · b) per profile (s stock lengths, n pieces, b bars). A 3,000-piece batch plans in well under a
second. It is a heuristic, not a proven optimum.

### Results and formulas

| Value | Formula |
|---|---|
| `Utilization` | `TotalCutMm / (TotalStockMm − TotalRemnantMm)`. Share of the **consumed** material that became pieces; remnants return to stock, so they do not count against it. |
| `WasteFraction` | `TotalWasteMm / (TotalStockMm − TotalRemnantMm)` = `1 − Utilization` |
| `StockCost` | Σ bars of stock length (m) × cost per metre (the price of the bars to take) |
| `RemnantValue` | remnant length (m) × cost per metre |
| `NetCost` | `StockCost − RemnantValue` (the material consumed) |
| `Remnants` | one `Remnant(DefinitionId, Name, LengthMm, BarNumber, StockLengthMm)` per bar whose leftover is reusable; `CuttingPlan.Remnants` lists all of them |
| `WasteOffcutCount` | bars whose leftover is shorter than the minimum usable offcut (`StockBar.HasWasteOffcut`) |

The M6 BOM still prices profiles per metre of cut length; both use the same `CostPerMetre`. The plan's `StockCost`
is what the bars cost to buy, and its `NetCost` is (up to rounding) the BOM profile cost plus the cost of the waste.
`ProfileCuttingPlan.Stock` lists the bars to take per length (e.g. 1 × 6500, 1 × 6000).

### Limitations

- Mitred pieces are placed as straight lengths: consecutive mitres are not nested to save material.
- The choice between stock lengths minimises the stock taken; it does not weigh the value of the remnants left over.
- Remnants are reported but not yet fed back as stock for later plans (needs persistent stock, M8).

## Headless use (server, batch)

```csharp
var library = await LibrarySerializer.LoadAsync("library.json");
var project = await ProjectSerializer.LoadAsync("window.json");
var rules   = CalculationRulesSerializer.Load("calculation-rules.json");
var result  = new CalculationEngine().Calculate(project, library, rules);
var plan    = new CuttingOptimizer().Optimize(result, library, rules);
// result.Bom, result.Cost.Total, plan.Profiles[i].Bars, plan.Utilization, plan.StockCost, plan.Issues

// Or keep both up to date for a live design:
var service = new CalculationService(() => project, library, rules);
history.HistoryChanged += service.Invalidate;      // cheap; recalculates lazily on the next read
var current = service.CuttingPlan;
```
