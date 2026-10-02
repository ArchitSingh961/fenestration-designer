# eVA Fenestration Designer — Part 1: 2D Design Engine

WPF / .NET 8 CAD-style editor for aluminium & uPVC windows and doors, with a library-driven calculation engine
(glass and profile sizes, cut list, bill of materials and cost).

## Solution layout

```text
Fenestration.sln
src/
├── Fenestration.Core/          net8.0          — pure domain + geometry. NO WPF. Consumed by future modules.
│   ├── Geometry/               Point2D, Vector2D, LineSegment2D, Rectangle2D, BoundingBox2D, Transform2D,
│   │                           ViewportTransform, Projection2D, Intersection2D, AngleMath,
│   │                           GeometryTolerance, GeometryValidation   (see docs/geometry.md)
│   ├── Viewport/               ViewportSettings, GridSpacing, GridAxisRange (WPF-free viewport logic)
│   ├── Models/                 Project, Frame, Profile, GlassPanel, Dimension, ProfileType, DimensionOrientation
│   ├── Design/                 FrameEditor, FrameLayout (derived glass), FrameHitTester, DivisionSnapper,
│   │                           AutoDimensions, FrameSnapshot, DesignRules   (see docs/domain-model.md)
│   ├── Commands/               IUndoableCommand, ICommandHistory/CommandHistory, frame commands, CompositeCommand
│   ├── Snapping/               SnapEngine, snap providers, SnapSettings                 (see docs/snapping.md)
│   ├── Interaction/            SelectionService, drag/create operations, OperationPreview (see docs/interaction.md)
│   ├── Interfaces/             IDesignService (integration contract), ISnapProvider, IRenderer
│   ├── Library/                ProfileDefinition, GlassDefinition, MaterialDefinition, IProductLibrary/ProductLibrary,
│   │                           LibrarySerializer (JSON), LibraryQuery (search)          (see docs/calculation.md)
│   ├── Serialization/          ProjectSerializer, ProjectFile, ProjectFormatVersion (+ migrations), JSON converters
│   └── Utilities/              Units (constants), ValidationHelper
├── Fenestration.Calculation/   net8.0          — calculation engine. References Core only. NO WPF.
│                               CalculationEngine, CalculationRules, CalculationResult (lines, cut list, BOM, cost),
│                               CuttingOptimizer → CuttingPlan (stock bars, kerf, trim, remnants, waste, utilisation),
│                               CuttingRules, CalculationRulesSerializer,
│                               CalculationService (invalidate on change, recalculate/re-plan on read)
├── Fenestration.Data/          net8.0          — local SQLite persistence. References Core only. NO WPF.
│                               SqliteDatabase (create, version, upgrade), SqliteLibraryRepository, SqliteProjectRepository,
│                               LibraryService (validated CRUD, retire, delete rules, import/export), LocalStore
│                                                                                       (see docs/persistence.md)
├── Fenestration.Designer/      net8.0-windows  — view models, viewport control, rendering, interaction
│   ├── Controls/               ViewportControl (the 2D drawing surface)
│   ├── Rendering/              ViewportRenderer, GridRenderer, CoordinateRenderer, ViewportDrawingContext,
│   │                           IViewportLayer, ProjectLayer, DimensionRenderer, ViewportTheme, DesignTheme
│   ├── Interaction/            ViewportInteractionController, IViewportTool, InteractionMode, InteractionState
│   ├── Tools/                  SelectTool, PanTool, FrameTool, DivisionTool (DesignerToolBase)
│   ├── Views/                  EnumToBooleanConverter
│   └── ViewModels/             MainViewModel, CanvasViewModel (viewport), PropertiesViewModel, LibraryPickerViewModel,
│                               CuttingPlanViewModel, LibraryManagerViewModel, LibraryItemEditorViewModel,
│                               ProjectListViewModel, IDialogService, ViewModelBase, RelayCommand
├── Fenestration.App/           net8.0-windows  — WPF shell (App.xaml, MainWindow.xaml, Resources/Theme.xaml,
│                                                 Dialogs/ — Library Manager, Open Project, name prompt, WpfDialogService,
│                                                 Library/library.json — the sample library, imported on first run,
│                                                 Settings/calculation-rules.json — kerf, trim, minimum offcut)
└── Fenestration.Tests/         net8.0-windows  — xUnit tests (Core, Calculation, Data, Designer view models)
docs/
├── architecture.md             layers, viewport/rendering design, frame-designer data flow
├── calculation.md              product library, references, calculation rules, BOM, changing materials, cutting plan
├── persistence.md              SQLite database: schema, versioning, first run, library CRUD, deletion rules, projects
├── domain-model.md             frame, profiles, division model, derived glass, validation, commands
└── geometry.md                 coordinate system, tolerance, primitives, viewport math, grid
```

## Designing a window

1. **Create Frame**: enter width × height in the left panel (default 1200 × 1500 mm).
2. **Add Mullion / Add Transom**: splits the selected glass, or spans the whole frame.
3. **Select** by clicking a frame, mullion, transom or glass. The properties panel shows its data.
4. **Edit**: type a frame width/height or a division position and press Enter (or Apply). You can also **drag** a mullion/transom;
   it snaps to the frame centre, bay centre or aligned divisions, and stops at the last valid position.
5. Glass sizes and dimensions update automatically. **Del** deletes, **Esc** cancels a drag, and **Ctrl+Z / Ctrl+Y** undo and redo.
6. **Materials**: the properties panel has searchable **Glass** and **Profile** pickers. Choosing an item changes it in
   place, as one undo step (e.g. 6mm Clear → 8mm Toughened, 60mm Frame → 50mm Frame). With several objects selected
   (Shift+click, box, Ctrl+A) it changes all of them; a selected frame stands for its panes and outer members.
7. The **Calculation** section shows the selection's manufacturing sizes, cut length, weight and cost. The **Bill of
   materials** shows the whole project and its total, recalculated after every change, undo and redo.
8. The **Cutting plan** shows the stock bars to cut per profile, with pieces, remnant and waste per bar, utilisation and
   bar cost. Kerf, trim and minimum offcut come from `Settings\calculation-rules.json`; stock lengths from the library.
9. **Save / Open** (Ctrl+S / Ctrl+O): projects are saved in the local database
   (`%LOCALAPPDATA%\Fenestration\fenestration.db`). The first save asks for a name; **Save a Copy As** saves a copy with
   new Ids. **Import / Export Project File** read and write the same JSON as a project file. Closing, New and Open ask
   before discarding unsaved changes (the title shows `*`).
10. **Library Manager** (File menu or toolbar): search and filter profiles, glass and materials (manufacturer,
    series/category, retired), add and edit them, retire/reinstate, delete unused ones, import/export library files.
    Changes are validated against the whole library, saved at once, and the open design is recalculated.

Dependency direction: `App → Designer → Calculation, Data → Core`, `Tests → Designer, Calculation, Data, Core`. Core,
Calculation and Data never reference WPF; Calculation and Data reference only Core, and neither Core nor Calculation
references SQLite (enforced by tests).

## Viewport controls

| Action | Input |
|---|---|
| Zoom around cursor | Mouse wheel |
| Pan | Middle-drag, or hold Space + left-drag |
| Fit to screen | `F`, or the toolbar's Fit |
| Toggle grid | `G`, or the toolbar's Grid |
| Zoom in / out / reset | Toolbar or View menu |

Keyboard shortcuts work while the drawing view has focus (it takes focus on startup and on click).

## Units and coordinate system

- **All geometry is stored in millimetres.** Screen pixels never enter the model.
- World axes: **X → right, Y → down** (same orientation as WPF, so no axis flip).
- `Frame.X/Y` is the frame's top-left corner in world mm.
- Children of a frame (`Profile`, `GlassPanel`, `Dimension`) store coordinates **relative to the frame origin**,
  so moving a frame moves its contents automatically. World position = `frame.X/Y + child point`.
- Angles are measured from +X; because Y is down, positive angles turn **clockwise on screen**
  (a profile running downwards is 90°).

```text
World (mm) ──ViewportTransform──► Screen (DIP)
screen = (world − Pan) × Zoom          Zoom = pixels per mm
world  = screen / Zoom + Pan           Pan  = world point at the canvas top-left
```

## Source of truth

Derived values (`Profile.Length`, `Profile.Angle`, `Frame.Bounds`, `Dimension.Value`, …) are get-only
and are **never serialized**; they are always recomputed from stored geometry.

## File format

```json
{
  "version": 1,
  "project": { "id": "…", "name": "Demo Window", "units": "mm", "frames": [ … ], "metadata": {} }
}
```

`ProjectFormatVersion.Current` is written on save. On load the raw JSON is upgraded step by step through
`ProjectFormatVersion.MigrateToCurrent` before being deserialized. Files newer than the app are rejected.
The local database stores each saved project as exactly this document, so a saved project and an exported project
file are interchangeable (see [docs/persistence.md](docs/persistence.md)).

## Integration contract (Calculation Engine)

`Fenestration.Core.Interfaces.IDesignService` (implemented by `MainViewModel`):

```csharp
Project GetCurrentProject();              // live model (read; mutate only via commands)
Project GetProjectSnapshot();             // isolated deep copy, Ids preserved — use this for calculations
Frame? GetSelectedFrame();
IReadOnlyList<Frame> GetFrames();
IReadOnlyList<Profile> GetAllProfiles();
IReadOnlyList<GlassPanel> GetAllGlassPanels();
```

`Fenestration.Calculation` references **only** `Fenestration.Core`:

```csharp
Project snapshot = designService.GetProjectSnapshot();
CalculationResult result = new CalculationEngine().Calculate(snapshot, library, new CalculationRules());
// result rows carry Frame.Id / Profile.Id / GlassPanel.Id so they can be linked back to the drawing
```

It can also load a saved `.json` project and library headlessly (`ProjectSerializer.LoadAsync`,
`LibrarySerializer.LoadAsync`), with no WPF involved. Product data (sizes, weights, prices, glazing bite, cut
allowances, accessories) lives in the library, generic fabrication rules (joint type, clearance, rounding) in
`CalculationRules`, and the design only references products by Id. See [docs/calculation.md](docs/calculation.md).

## Build, test, run

Requires the **.NET 8 SDK** (or newer) on Windows.

```bash
dotnet build Fenestration.sln
dotnet test src/Fenestration.Tests
dotnet run --project src/Fenestration.App
```

Or open `Fenestration.sln` in Visual Studio 2022 and set `Fenestration.App` as the startup project.

## Milestones

| # | Milestone | Status |
|---|-----------|--------|
| 1 | Solution, projects, shared models, geometry primitives, interfaces, WPF shell | ✅ |
| 2 | Geometry engine: primitives, tolerance, validation, projection, intersection, transforms, viewport math | ✅ |
| 3 | 2D viewport: rendering infrastructure, adaptive grid, axes, zoom/pan/fit/reset, cursor readout | ✅ |
| 4 | Frame designer: frames, mullions/transoms, derived glass, selection, drag/edit, resize, auto dimensions | ✅ |
| M5 | Interaction engine: tools/modes, multi & box selection, preview → validate → commit drags, resize handles, modular snapping, composite undo/redo | ✅ |
| M6 | Calculation engine + product library: library-referenced glass/profiles/materials, glass sizes & area, cut lengths, cut list, BOM, cost, searchable pickers, material change after design (single & multi-selection, undoable) | ✅ |
| M7 | Cutting optimisation: deterministic Best-Fit Decreasing over library stock lengths (several per profile), kerf, trim, remnants vs waste, utilisation, bar cost, cutting-plan panel | ✅ |
| M8 | SQLite persistence & Library Manager: local database (schema versioning, first-run import of library.json), saved projects with stable Ids and tracked library references, validated library CRUD, search/filter, retire vs delete, JSON import/export | ✅ |
| M9 | Local MVP integration | |
| 5 | Profile tool | |
| 6 | Selection | |
| 7 | Move | |
| 8 | Snapping | |
| 9 | Dimensions | |
| 10 | Glass panels | |
| 11 | Zoom / pan interaction | |
| 12 | Undoable domain commands | |
| 13 | Save / Open UI | |
| 14 | Integration testing | |
