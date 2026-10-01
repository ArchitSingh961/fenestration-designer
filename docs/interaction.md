# Interaction Engine

Everything is computed in **world millimetres**. Screen pixels are used only to read the mouse and to size
things that must look the same at every zoom (hit radius, handle size, snap radius, drag threshold). Each is
converted to mm at the current zoom before any geometry is tested.

## Flow

```text
Mouse / keyboard (WPF)
   ↓  ViewportInteractionController   screen → world (ScreenToWorld); wheel/middle-drag/Space-drag stay viewport-only
Active tool (Designer/Tools)         IViewportTool: pointer down/move/up, keys, Cancel
   ↓
Operation (Core/Interaction)         Update(world point) → OperationPreview   (snap → validate → preview)
   ↓  on release
Undoable command (Core/Commands)     executed once through ICommandHistory
   ↓
Domain model (Core/Models)           the only source of truth; glass re-derived by FrameEditor
   ↓
ProjectLayer + InteractionOverlayLayer → viewport
```

No tool changes a WPF element as a business operation; the renderer only reflects domain state plus the preview.

## Interaction modes and tools

| `InteractionMode` | Tool | Behaviour |
|---|---|---|
| `Select` (default) | `SelectTool` | click / Shift / Ctrl selection, box selection, move, resize handles |
| `Pan` | `PanTool` | left-drag pans the view |
| `CreateFrame` | `FrameTool` | drag a rectangle to draw a frame |
| `AddMullion` / `AddTransom` | `DivisionTool` | hover a glass panel to preview, click to add (Shift = whole frame) |

Switching mode (toolbar radio buttons or the Tools menu) cancels the previous tool's operation. A new tool, such as
`AddSash`, `Measure` or `Dimension`, is one class implementing `IViewportTool` (usually via `DesignerToolBase`),
one enum value and one dictionary entry in `MainViewModel`. The controller and viewport don't change.

## Selection

- `ISelectionService` / `SelectionService` (Core) holds **domain Ids** (frames, profiles, glass), raises one
  `Changed` per real change, and is session state only (never saved).
- Click selects one object; **Shift+click** adds; **Ctrl+click** toggles; click on empty space clears (unless
  Shift/Ctrl is held). Clicking an object that is part of a multi-selection keeps the selection for a drag, and
  narrows to that object if you release without dragging.
- **Box selection** (drag from empty space), `SelectionQuery.InRectangle`:
  - **left → right = window**: objects entirely inside the box (solid blue);
  - **right → left = crossing**: objects that intersect or are inside the box (dashed green);
  - tested geometry: frame outer bounds, division face-to-face bodies, glass boundaries;
  - Shift/Ctrl adds the result to the existing selection.
- **Ctrl+A** selects all frames, divisions and glass (only while the drawing has focus, so text boxes keep their own Ctrl+A).
- The properties panel reads the selection and the model; it never stores its own copy.

## Hit testing (priority)

Deterministic, never dependent on collection order:

1. **Resize handles** of the single selected frame (square, 7 px grab radius);
2. **Mullions / transoms**: face-to-face body within 5 px; the nearest centreline wins;
3. **Glass panels**;
4. **Frame** (outer profiles / border).

Tolerances are pixels converted to mm (`ScreenToWorldDistance`), so picking behaves the same at every zoom.

## Drag lifecycle: preview vs committed

```text
Down (hit) → Pending ──(moved ≥ 3 px)──► Begin operation (snap targets collected once)
   Move → Update: candidate → snap → validate on a COPY → OperationPreview → redraw
   Up   → CreateCommand → ICommandHistory.Execute  (exactly ONE command per drag)
   Esc / lost capture / tool switch / Undo → discard preview (the model was never touched)
```

- **Preview** (`OperationPreview`): validated candidate copies of frames with re-derived glass, drawn in place of the
  committed frames; ghost outlines; a snap marker; and a message (e.g. "Mullion at 700 mm", or why the
  candidate is invalid).
- **Invalid candidate**: the preview stops at the **last valid** position (binary search, whole mm) and the rejected
  candidate is drawn as a red dashed ghost, with the reason in red in the status bar. Releasing commits the last valid
  position, never the invalid one. Nothing invalid ever enters the model.
- A click without movement (< 3 px) is a selection, not a drag.

## Operations (Core/Interaction, WPF-free)

| Operation | What moves | Command on release |
|---|---|---|
| `MoveElementsOperation` | selected frames (free) and divisions (mullion X, transom Y); a frame carries its own divisions | `MoveFrameCommand`, `MoveDivisionsCommand`, or a `CompositeCommand` over several frames |
| `ResizeFrameOperation` | right edge, bottom edge or bottom-right corner (left/top anchor the frame) | `ResizeFrameCommand` |
| `CreateFrameOperation` | a new rectangle | `CreateFrameCommand` |
| `AddDivisionOperation` | a new mullion/transom through the hovered opening (or whole frame) | `AddDivisionCommand` |

## Constraints

Snapping proposes; validation decides. The rules live **only** in Core (`FrameEditor` / `FrameLayout`) and are
reused for typed values, drags and commands alike:

- frame width/height between 1 and 30 000 mm, and at least 2 × profile + 50 mm glass;
- divisions inside the frame; no two divisions on the same line; every division end connected;
- every glass opening at least 50 mm wide and high (face to face);
- resizing never moves existing divisions: a size that would invalidate them is rejected;
- deleting a division that others end on is rejected unless those are deleted too (e.g. in the same multi-delete).

## Keyboard

| Key | Action |
|---|---|
| Esc | cancel the current drag; otherwise clear the selection (Select tool) or return to Select (other tools) |
| Delete | delete the selection (one undo step) |
| Ctrl+Z / Ctrl+Y | undo / redo (a drag in progress is cancelled first) |
| Ctrl+A | select all |
| G / F | toggle grid / fit to screen |
| Space + drag, middle-drag, wheel | pan / pan / zoom (all tools) |

Esc, Delete, Ctrl+A, G, F and Space only act while the drawing view has focus, so they never interfere with typing.

## Performance

- Snap targets are collected once per interaction. Each mouse move only compares distances.
- A move validates small frame copies (a few profiles); nothing touches disk, network or the future calculation engine.
- The overlay (box, handles, markers, ghosts) is its own visual. A box-selection drag doesn't re-render the design.
