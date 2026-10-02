# Architecture

## Projects and dependency direction

```text
Mark.App        (WPF shell: App.xaml, MainWindow.xaml)            net8.0-windows
      ↓
Mark.Designer   (view models, viewport control, rendering, input)  net8.0-windows
      ↓                                   ↓
Mark.Calculation (calculation engine,     Mark.Data (SQLite: library, saved   net8.0 — NO WPF,
      BOM, cost, cutting plan)                         projects; see persistence.md)         reference Core only
      ↓                                   ↓
Mark.Core       (models, geometry, viewport math, commands, JSON,  net8.0 — NO WPF, NO SQLite
                         product library)
```

`Mark.Tests` (net8.0-windows) references Core, Calculation, Data and Designer. `ArchitectureTests` fail if
Core ever references WPF, Designer, App, Calculation or Data; if Calculation or Data references anything but Core; if
Core or Calculation references SQLite; if any command holds a WPF object; or if Core, Calculation or Data turns on WPF
or a Windows-only target.

```text
                      Core  (models, geometry, design rules, snapping, interaction ops, commands, library model)
                        │
          ┌─────────────┼──────────────┐
          ↓             ↓              ↓
      Designer     Calculation       Data   (SQLite; only the application layer uses it)
          │             │              │
          └─────────────┼──────────────┘
                        ↓
                        UI (App: composition root — opens the LocalStore, provides WpfDialogService)
```

## Core

| Folder | Contents |
|---|---|
| `Geometry/` | Point2D, Vector2D, LineSegment2D, Rectangle2D, BoundingBox2D, Transform2D, ViewportTransform, projection, intersection, tolerance, validation — see [geometry.md](geometry.md) |
| `Viewport/` | `ViewportSettings`, `GridSpacing`, `GridAxisRange`: viewport logic in world millimetres, with no WPF |
| `Models/` | Project, Frame, Profile, GlassPanel, Dimension |
| `Design/` | `FrameEditor` (validated `Xxx` / `TryXxx` edits), `FrameLayout`, hit testing, handles, box-selection query — see [domain-model.md](domain-model.md) |
| `Commands/` | `IUndoableCommand`, `ICommandHistory` / `CommandHistory`, frame commands, `CompositeCommand` — see [commands.md](commands.md) |
| `Snapping/` | `SnapEngine`, providers, `SnapSettings` — see [snapping.md](snapping.md) |
| `Interaction/` | `ISelectionService`, `OperationPreview`, drag/create operations, `DragClamp` — see [interaction.md](interaction.md) |
| `Interfaces/` | `IDesignService` (integration contract), `ISnapProvider` (legacy snapping contract, adapted by `ProjectSnapProvider`), `IRenderer` |
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
| `SelectTool` (Tools) | Selection, box selection, move and resize. Rewritten in Milestone 5 on top of the Core interaction operations (see below). |
| `PropertiesViewModel` | Shows model data; frame width/height and division position are editable and applied through commands. |
| `MainViewModel` | Owns project, selection, history, rules and the design actions (create frame, add mullion/transom, delete). |

`ViewportInteractionController` keeps the wheel, middle-drag and Space-drag for navigation, and forwards
left-button, Esc and Delete to `ViewportControl.Tool`. The Milestone 3 viewport, transform and renderer are unchanged.
The temporary `DemoRectangleLayer` has been removed.

### Rendering rules
- Profiles are solid grey with a dark outline; glass is translucent light blue, labelled with its derived size; the selection is blue.
- Line widths, dimension offsets and text are in screen pixels, so drawings read the same at any zoom.
- Only the content visual is redrawn after an edit. Hovering redraws nothing.

## Openings and design library (Milestone 9)

Details: [openings.md](openings.md).

```text
Core   Models/OpeningType, DesignInfo        what an opening is; what a frame is as a product (W1 × 2)
       Design/DesignTemplates                the built-in library (template trees)
       Design/FrameEditor.Openings           TrySetOpening, TryApplyTemplate, TrySetDesignInfo (atomic, validated)
       Design/OpeningGeometry                sash band, interlocks, handle position/height; Mirror for the Outside view
       Commands/OpeningCommands              SetOpeningCommand, ApplyTemplateCommand, SetDesignInfoCommand
Designer Rendering/FrameRenderer             one frame: glass → divisions → sashes/symbols/handles → frame → labels
       Rendering/DesignThumbnails            library pictures drawn by FrameRenderer (vector, cached)
       ViewModels/DesignLibraryViewModel     rail categories, sections, tiles (click = apply)
       ViewModels/MainViewModel.Designs      targets (selection / drop), confirmation, Inside/Outside, editors
       Interaction/IViewportDropTarget       drag a design onto the drawing (ViewportControl → view model)
```

`ProjectLayer` now delegates each frame to `FrameRenderer`; in the Outside view it renders a mirrored copy. The
drop-target highlight is drawn by `InteractionOverlayLayer` from `InteractionState.DropTarget`.

## Interaction engine (Milestone 5)

Details: [interaction.md](interaction.md), [snapping.md](snapping.md), [commands.md](commands.md).

```text
ViewportInteractionController (screen → world) → active IViewportTool (Designer/Tools)
    → Core/Interaction operation: snap (Core/Snapping) → validate on a copy (FrameEditor.TryXxx) → OperationPreview
    → on release: ONE IUndoableCommand → ICommandHistory → domain model → OnDesignChanged → redraw
```

- **Layering.** Everything below the tools is WPF-free Core code: selection (`ISelectionService`), snapping, the
  drag/create operations, validation and commands. Tools are thin adapters from pointer events to those operations,
  so another front end (web, CLI, tests) can drive the same interaction logic.
- **Preview vs committed.** During a drag `InteractionState.Preview` holds validated candidate frame copies.
  `ProjectLayer` draws them in place of the committed frames, and `InteractionOverlayLayer` draws ghosts, the snap
  marker, handles and the selection box on a third visual. The model changes once, on release; cancel discards the preview.
- **Modes.** `InteractionMode` { Select, Pan, CreateFrame, AddMullion, AddTransom } maps to `SelectTool`, `PanTool`,
  `FrameTool` and `DivisionTool` (×2). New tools plug in without touching the controller.
- **Snapping.** `SnapEngine` with modular providers replaces the Milestone 4 `DivisionSnapper` in the tools (the class
  remains for compatibility). The Milestone 1 `ISnapProvider` contract is served by `ProjectSnapProvider`.
- **Commands.** `ICommandHistory` abstracts the existing `CommandHistory`. `CompositeCommand` makes multi-object
  operations one undo step, and `MoveFrameCommand`, `MoveDivisionsCommand` and `DeleteDivisionsCommand` were added.
