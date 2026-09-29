# Architecture

## Projects and dependency direction

```text
Fenestration.App        (WPF shell: App.xaml, MainWindow.xaml)            net8.0-windows
      ↓
Fenestration.Designer   (view models, viewport control, rendering, input)  net8.0-windows
      ↓
Fenestration.Core       (models, geometry, viewport math, commands, JSON)  net8.0 — NO WPF
```

`Fenestration.Tests` (net8.0-windows) references Core and Designer. `ArchitectureTests` fail if Core
ever references WPF, Designer or App.

## Core

| Folder | Contents |
|---|---|
| `Geometry/` | Point2D, Vector2D, LineSegment2D, Rectangle2D, BoundingBox2D, Transform2D, ViewportTransform, projection, intersection, tolerance, validation — see [geometry.md](geometry.md) |
| `Viewport/` | `ViewportSettings`, `GridSpacing`, `GridAxisRange`: viewport logic in world millimetres, with no WPF |
| `Models/` | Project, Frame, Profile, GlassPanel, Dimension |
| `Commands/` | `IUndoableCommand`, `CommandHistory` |
| `Interfaces/` | `IDesignService` (integration contract), `ISnapProvider` (snapping seam), `IRenderer` |
| `Serialization/` | versioned JSON project files |

## Designer — viewport (Milestone 3)

```text
MainWindow.xaml
  └─ ViewportControl  (Controls/)            FrameworkElement hosting two DrawingVisuals
       ├─ ViewportInteractionController      (Interaction/) mouse/keyboard → view model calls
       ├─ ViewportRenderer                   (Rendering/)   orchestrates the passes
       │    ├─ background visual: fill → GridRenderer → CoordinateRenderer (axes, ruler labels)
       │    └─ content visual:    IViewportLayer × n  (drawn through ViewportDrawingContext)
       └─ binds to CanvasViewModel           (ViewModels/)  the viewport view model/state
                └─ ViewportTransform (Core)  the only world ↔ screen math
```

| Class | Responsibility |
|---|---|
| `CanvasViewModel` | Viewport state and operations: zoom, pan, fit, reset, grid/axes flags, viewport size, cursor readout, content layers. Raises `ViewChanged` / `ContentChanged`. Never touches domain objects. (This is the spec's "ViewportViewModel"/"ViewportState"; the existing class was extended rather than duplicated.) |
| `ViewportControl` | Hosts the visuals, reports its size, and coalesces redraws to at most one per render pass. |
| `ViewportInteractionController` | Wheel → zoom at cursor; middle drag or Space+left drag → pan; F → fit; G → grid. Holds only gesture state. |
| `ViewportRenderer` | Draws the background pass (grid, axes, labels) and the content pass (layers, culled by bounds). |
| `ViewportDrawingContext` | A `DrawingContext` plus `ViewportTransform`. Layers draw in world mm; the context converts to pixels in one place. |
| `IViewportLayer` | Extension point: `Bounds` (for fit and culling) and `Render(context)`. |
| `ViewportTheme` | Frozen pens and brushes, created once. |

### Redraw rules
- Zoom, pan, resize, grid or axes change → redraw background and content.
- Content change (`CanvasViewModel.InvalidateContent()` or layers added/removed) → redraw content only.
- Mouse move → **no redraw**; only the cursor text in the status bar updates.
- Grid lines are streamed into one frozen `StreamGeometry` per class (minor/major), so the whole grid costs two draw calls.

## Frame designer (Milestone 4)

The domain model and its rules are documented in [domain-model.md](domain-model.md).

### Data flow

```text
User action (button, properties panel, mouse)
      ↓
MainViewModel / SelectTool
      ↓
Undoable command  (Core.Commands: CreateFrame, AddDivision, MoveDivision, ResizeFrame, Delete…)
      ↓
FrameEditor       (Core.Design) edits a COPY → FrameLayout validates → glass re-derived → written back
      ↓               (on failure: DesignValidationException, model untouched, message shown)
Domain model      (Project → Frame → Profiles / GlassPanels) = the only source of truth
      ↓
CommandHistory.HistoryChanged → MainViewModel.OnDesignChanged → Canvas.InvalidateContent + properties refresh
      ↓
ProjectLayer      reads the model → ViewportDrawingContext → ViewportTransform → WPF
```

### Core.Design (no WPF)

| Class | Responsibility |
|---|---|
| `FrameEditor` | Create, resize, add/move/delete divisions. Atomic and validated. |
| `FrameLayout` | Validates the structure and derives openings (glass); face-to-face member bodies. |
| `FrameMembers`, `Members` | Read profiles as structural members: axis, position, span, outer frame members. |
| `FrameSnapshot` | Deep copy of a frame with Ids preserved: atomic edits and undo mementos. |
| `FrameHitTester` | World-space picking: division → glass → frame. |
| `DivisionSnapper` | 1-D drag snapping: frame centre, bay centre, alignment, else whole mm or the snap grid. |
| `AutoDimensions` | Derived overall and chain dimensions. |
| `DesignRules` | Profile thicknesses (60 mm), minimum glass (50 mm), spacing between frames. |

### Designer

| Class | Responsibility |
|---|---|
| `ProjectLayer` (Rendering) | The content layer: glass (with size labels) → divisions (face to face) → outer frame → selection → dimensions. Culls off-screen frames. |
| `DimensionRenderer`, `DesignTheme` (Rendering) | CAD-style dimensions; frozen design pens and brushes. |
| `IViewportTool` (Interaction) | Tool contract: pointer down/move/up in world coordinates, cursor feedback, Esc/Delete. |
| `SelectTool` (Tools) | Click to select (Ctrl toggles); drag a mullion/transom live (snapped, clamped to the last valid position); the release records one undo step; Esc cancels. |
| `PropertiesViewModel` | Shows model data; frame width/height and division position are editable and applied through commands. |
| `MainViewModel` | Owns project, selection, history, rules and the design actions (create frame, add mullion/transom, delete). |

`ViewportInteractionController` keeps the wheel, middle-drag and Space-drag for navigation, and forwards
left-button, Esc and Delete to `ViewportControl.Tool`. The Milestone 3 viewport, transform and renderer are unchanged.
The temporary `DemoRectangleLayer` has been removed.

### Rendering rules
- Profiles are solid grey with a dark outline; glass is translucent light blue, labelled with its derived size; the selection is blue.
- Line widths, dimension offsets and text are in screen pixels, so drawings read the same at any zoom.
- Only the content visual is redrawn after an edit. Hovering redraws nothing.

## Snapping seam

`Core.Interfaces.ISnapProvider` (`FindSnap(worldPoint, toleranceMm, excludeIds)`) remains the contract for general
point snapping (Milestone 8). Milestone 4 adds only one-dimensional division snapping (`DivisionSnapper`).
Its tolerance is 8 px converted with `CanvasViewModel.ScreenToWorldDistance`; with Snap to Grid on, positions round to
`CanvasViewModel.GridSpacingMm`.
