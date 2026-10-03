# MARK

WPF / .NET 8 CAD-style editor for aluminium & uPVC windows and doors, with a library-driven calculation engine
(glass and profile sizes, cut list, bill of materials and cost).

## Solution layout

```text
Mark.sln
src/
├── Mark.Core/          net8.0          — pure domain + geometry. NO WPF. Consumed by future modules.
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
├── Mark.Calculation/   net8.0          — calculation engine. References Core only. NO WPF.
│                               CalculationEngine, CalculationRules, CalculationResult (lines, cut list, BOM, cost),
│                               CuttingOptimizer → CuttingPlan (stock bars, kerf, trim, remnants, waste, utilisation),
│                               CuttingRules, CalculationRulesSerializer,
│                               CalculationService (invalidate on change, recalculate/re-plan on read)
├── Mark.Data/          net8.0          — local SQLite persistence. References Core only. NO WPF.
│                               SqliteDatabase (create, version, upgrade), SqliteLibraryRepository, SqliteProjectRepository,
│                               LibraryService (validated CRUD, retire, delete rules, import/export), LocalStore
│                                                                                       (see docs/persistence.md)
├── Mark.Designer/      net8.0-windows  — view models, viewport control, rendering, interaction
│   ├── Controls/               ViewportControl (the 2D drawing surface)
│   ├── Rendering/              ViewportRenderer, GridRenderer, CoordinateRenderer, ViewportDrawingContext,
│   │                           IViewportLayer, ProjectLayer, DimensionRenderer, ViewportTheme, DesignTheme
│   ├── Interaction/            ViewportInteractionController, IViewportTool, InteractionMode, InteractionState
│   ├── Tools/                  SelectTool, PanTool, FrameTool, DivisionTool (DesignerToolBase)
│   ├── Views/                  EnumToBooleanConverter
│   └── ViewModels/             MainViewModel, CanvasViewModel (viewport), PropertiesViewModel, LibraryPickerViewModel,
│                               CuttingPlanViewModel, LibraryManagerViewModel, LibraryItemEditorViewModel,
│                               ProjectListViewModel, IDialogService, ViewModelBase, RelayCommand
├── Mark.Licensing/     net8.0          — accounts and licensing: signed licence, evaluation, feature catalogue,
│                               packages, API contracts and clients, LicenceManager. NO WPF  (see docs/licensing.md)
├── Mark.LicenceServer/ net8.0 (ASP.NET Core) — MARK.LicenceServer.exe: companies, users, computers, keys; signs licences
├── Mark.Owner/         net8.0-windows  — MARK.Owner.exe: the admin program (companies, keys, packages, company types)
├── Mark.App/           net8.0-windows  — WPF shell (App.xaml, MainWindow.xaml, sign-in, Licensing/ — machine id, DPAPI,
│                                                 Dialogs/ — Library Manager, Open Project, name prompt, WpfDialogService,
│                                                 Library/library.json — the sample library, imported on first run,
│                                                 Settings/calculation-rules.json — kerf, trim, minimum offcut)
└── Mark.Tests/         net8.0-windows  — xUnit tests (Core, Calculation, Data, Designer view models)
docs/
├── architecture.md             layers, viewport/rendering design, frame-designer data flow
├── calculation.md              product library, references, calculation rules, BOM, changing materials, cutting plan
├── persistence.md              SQLite database: schema, versioning, first run, library CRUD, deletion rules, projects
├── domain-model.md             frame, profiles, division model, derived glass, validation, commands
├── pricing.md                  price structure (cost lines, rates, discount, charges, tax), sash pricing, quantities
├── quotes.md                   quotes, clients, design cards, quote list, dashboard, quote numbers
├── openings.md                 opening types (sashes), design library, design details, Inside/Outside view
├── licensing.md                accounts, sign-in, licences, packages, keys, MARK Owner, running the licence server
├── catalogue.md                product systems, used with, reinforcement, bundles, the owner's catalogue
├── areas.md                    areas and tabs, staff logins and permissions, who did what
├── roadmap.md                  the milestone plan
└── geometry.md                 coordinate system, tolerance, primitives, viewport math, grid
```

## Accounts and sign-in

MARK is sold as licensed accounts. Start **MARK Licence Server**, set up your admin account in **MARK Owner**, create
an account for each company (name, logo, company type, User ID and password, uPVC / Aluminium each with its own
validity, package, add-ons, computers), then sign in to **MARK** with it. The admin can extend, suspend, free a
computer or delete an account at any time, and generate **licence keys** with a validity; MARK follows at its next
check-in and becomes read-only when the account is suspended or expired. Features outside the company's package are
shown locked. See [docs/licensing.md](docs/licensing.md).

## Areas, staff and who did what

MARK is organised into areas (Sales, Design, Pricing, Library, Production, and Orders, Purchasing, Inventory and
Accounts to come) in a bar on the left, each with its tabs in the header. The account owner gives staff their own
logins in **Account → Staff logins**, each with only the parts of MARK they need (a cutter sees only Production), up to
the users the MARK supplier allows. Every quote records who created and saved it, with a history of what changed. See
[docs/areas.md](docs/areas.md).

## Product catalogue, systems and bundles

The owner keeps one master catalogue in **MARK Owner → Catalogue** (profiles, glass, hardware, **systems** such as
*62mm Casement – uPVC* and **bundles** of parts that go together, e.g. a sliding frame's track rail and seals, or a
casement's hinges by size) and ticks per company which systems and items it gets. MARK follows it at check-in; the
company enters only its own prices. A window is made in a system: its profiles, sash, glass, steel reinforcement and
bundle parts are picked automatically. See [docs/catalogue.md](docs/catalogue.md).

## Quotes

MARK opens on **Sales › Dashboard**. **Create quote** opens the quote's **Client** tab (project name, status, client,
site address, requirements, history); **Designs** shows a card per window type with picture, quantity and price;
**Design › Drawing** is the designer below. **Save** (Ctrl+S) gives a new quote its number (QT-00001…). The **Quotes** page lists them
(Active / Won / Lost / All, search). See [docs/quotes.md](docs/quotes.md).

**Your own pricing**: product prices are in the Library Manager (per metre of profile, per m² of glass, per piece of
hardware). Each quote's **Pricing › Price** tab adds your cost lines (wastage, coating, labour, margin…), hardware rates per
sash type, mesh and reinforcement rates, discount, charges (transport, loading) and tax, with a live price summary.
**Save as my default** makes it the start of every new quote. See [docs/pricing.md](docs/pricing.md).

## Designing a window

0. **Design library** (left rail: Dividers, Openable, Sliding, Mesh): click a design to apply it to the selected
   opening or frame, or drag it onto an opening. With no frame yet it creates one (W1). Replacing a whole frame's design
   asks first. Every opening can be fixed, side hung, top/bottom hung, tilt & turn, pivot or sliding, with an optional
   mesh shutter; change it in the properties panel (OPENING). See [docs/openings.md](docs/openings.md).
1. **Create Frame** (rail: Frame): enter width × height (default 1200 × 1500 mm), or draw one with the Frame tool.
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
   (`%LOCALAPPDATA%\MARK\mark.db`). The first save asks for a name; **Save a Copy As** saves a copy with
   new Ids. **Import / Export Project File** read and write the same JSON as a project file. Closing, New and Open ask
   before discarding unsaved changes (the title shows `*`).
10. **Library Manager** (File menu or toolbar): search and filter profiles, glass and materials (manufacturer,
    series/category, retired), add and edit them, retire/reinstate, delete unused ones, import/export library files.
    Changes are validated against the whole library, saved at once, and the open design is recalculated.
11. **Design details** (select a frame): reference (W1), quantity, name, location, floor, note and floor distance (sill
    height). The reference and quantity are drawn above the frame; a floor distance adds a floor line.
12. **Inside / Outside** (switch under the drawing, or View menu): the Outside view shows the design mirrored with
    hinges swapped, as seen from outside. It is read-only.

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

`Mark.Core.Interfaces.IDesignService` (implemented by `MainViewModel`):

```csharp
Project GetCurrentProject();              // live model (read; mutate only via commands)
Project GetProjectSnapshot();             // isolated deep copy, Ids preserved — use this for calculations
Frame? GetSelectedFrame();
IReadOnlyList<Frame> GetFrames();
IReadOnlyList<Profile> GetAllProfiles();
IReadOnlyList<GlassPanel> GetAllGlassPanels();
```

`Mark.Calculation` references **only** `Mark.Core`:

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
dotnet build Mark.sln
dotnet test src/Mark.Tests
dotnet run --project src/Mark.App
```

Or open `Mark.sln` in Visual Studio 2022 and set `Mark.App` as the startup project.

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
| M9 | Openings and design library: fixed / side hung / top & bottom hung / tilt & turn / pivot / sliding openings with mesh shutters, sash drawing with CAD symbols, handles and labels, design library (click or drag), whole-frame vs single-opening templates, design details (reference, quantity, floor distance), Inside / Outside view | ✅ |
| M10 | Quotes, clients and designs: client/site/notes form, quote status and numbers, design cards with price × quantity, duplicate/delete/edit, quote list (Active/Won/Lost/All, search), dashboard (tiles, value by status, win rate, recent quotes), database schema 2 | ✅ |
| M11 | Pricing: your price structure per quote (cost lines, hardware/mesh/reinforcement rates, discount, charges, tax) with live summary and saved default; sash and mesh bars priced from the library; quantities in BOM, cut list and totals; database schema 3 | ✅ |
| M12 | Opportunities and dashboard charts (basic dashboard done in M10) | |
| M13 | Documents and reports | |
| M14 | Shell and polish | |

The plan for M10-M14 is in [docs/roadmap.md](docs/roadmap.md).
