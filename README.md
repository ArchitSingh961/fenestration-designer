# eVA Fenestration Designer — Part 1: 2D Design Engine

WPF / .NET 8 CAD-style editor for aluminium & uPVC windows and doors.

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
│   ├── Commands/               IUndoableCommand, CommandHistory, frame commands (create/resize/add/move/delete)
│   ├── Interfaces/             IDesignService (integration contract), ISnapProvider, IRenderer
│   ├── Serialization/          ProjectSerializer, ProjectFile, ProjectFormatVersion (+ migrations), JSON converters
│   └── Utilities/              Units (constants), ValidationHelper
├── Fenestration.Designer/      net8.0-windows  — view models, viewport control, rendering, interaction
│   ├── Controls/               ViewportControl (the 2D drawing surface)
│   ├── Rendering/              ViewportRenderer, GridRenderer, CoordinateRenderer, ViewportDrawingContext,
│   │                           IViewportLayer, ProjectLayer, DimensionRenderer, ViewportTheme, DesignTheme
│   ├── Interaction/            ViewportInteractionController, IViewportTool
│   ├── Tools/                  SelectTool (select, drag divisions)
│   └── ViewModels/             MainViewModel, CanvasViewModel (viewport), PropertiesViewModel, ViewModelBase, RelayCommand
├── Fenestration.App/           net8.0-windows  — WPF shell (App.xaml, MainWindow.xaml, Resources/Theme.xaml)
└── Fenestration.Tests/         net8.0-windows  — xUnit tests (Core + Designer view models)
docs/
├── architecture.md             layers, viewport/rendering design, frame-designer data flow
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

Dependency direction: `App → Designer → Core`, `Tests → Designer, Core`. Core never references WPF (enforced by a test).

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

## Integration contract (for the Calculation Engine)

`Fenestration.Core.Interfaces.IDesignService` (implemented by `MainViewModel`):

```csharp
Project GetCurrentProject();              // live model (read; mutate only via commands)
Project GetProjectSnapshot();             // isolated deep copy, Ids preserved — use this for calculations
Frame? GetSelectedFrame();
IReadOnlyList<Frame> GetFrames();
IReadOnlyList<Profile> GetAllProfiles();
IReadOnlyList<GlassPanel> GetAllGlassPanels();
```

A future `Fenestration.Calculation` project references **only** `Fenestration.Core`:

```csharp
Project snapshot = designService.GetProjectSnapshot();
CalculationResult result = calculationEngine.Calculate(snapshot);
// result rows carry Profile.Id / GlassPanel.Id so they can be linked back to the drawing
```

It can also load a saved `.json` project headlessly with `ProjectSerializer.LoadAsync(path)`,
with no WPF involved. Manufacturer rules (deductions, cut lengths, glass sizes) belong in that engine,
not in the Core models; per-object `Properties`/`Metadata` dictionaries carry extra inputs such as series or finish.

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
