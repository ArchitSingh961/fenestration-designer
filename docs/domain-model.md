# Domain Model (Frame Designer)

All values are **millimetres**. The model is plain C# in `Fenestration.Core` with no WPF, so the calculation
engine, persistence and export layers can use it directly.

```text
Project
 └── Frame                      X, Y (world, top-left), Width, Height
      ├── Profiles[]            4 × Frame (outer) + Mullions + Transoms   ← source of truth
      ├── GlassPanels[]         derived from the profiles                 ← recomputed on every edit
      └── Dimensions[]          reserved for user-placed dimensions (automatic ones are computed, not stored)
```

Children of a frame use **frame-relative** coordinates: (0, 0) is the frame's outer top-left corner.
World position = `frame.X/Y + child point`.

## Frame

A rectangular window/door assembly. `Width`/`Height` are the outer size. It always has four outer
`Profile`s of type `Frame`, created by `FrameEditor.CreateFrame`:

| Member | Centreline (60 mm profiles, 1200 × 1500 frame) |
|---|---|
| Left | (30, 30) → (30, 1470) |
| Top (head) | (30, 30) → (1170, 30) |
| Right | (1170, 30) → (1170, 1470) |
| Bottom (sill) | (30, 1470) → (1170, 1470) |

With thickness applied they cover the frame border exactly; their corners overlap (as with mitred corners).
Limits: width and height from 1 to 30 000 mm, and big enough for two frame profiles plus the minimum glass.

## Profile

`ProfileType` is one of **Frame, Mullion, Transom, Sash, Generic**. `StartPoint`/`EndPoint` describe the
**centreline**, and `Thickness` is the visible face width (defaults in `DesignRules`: 60 mm). No manufacturer
data is stored; `Properties` is a free dictionary for later use (series, colour, article number…).

## Division model (mullions and transoms)

- A **mullion** is a vertical `Profile`; its **position** is its centreline X. A **transom** is horizontal; its position is its centreline Y.
  Positions are measured from the frame's outer left/top edge, e.g. "mullion at 600 mm".
- Each end of a division lies on the centreline of the member it meets: an outer frame member or another division.
  This is a **T-junction**, detected geometrically within 0.1 mm, so no extra link data is stored.
- A division added **with a glass panel selected** spans only that opening (a partial division).
  Otherwise it spans the whole frame opening, crossing any perpendicular divisions.
- **Moving** a division moves every perpendicular member that *ends* on it (their end follows);
  members it merely *crosses* are unaffected.
- **Resizing** the frame moves the right and bottom frame members the same way, so full-span divisions stretch.
  Divisions keep their positions; shrinking past one is rejected.
- **Deleting** a division is rejected while other divisions end on it.

### Validation (`FrameLayout.Compute`)
A layout is valid only if:
1. there are exactly four outer frame members;
2. every structural member is straight and axis-aligned (mullions vertical, transoms horizontal);
3. every division lies inside the frame;
4. no two parallel members share the same line over an overlapping span ("two divisions at the same position");
5. every member end meets a perpendicular member (no dangling divisions);
6. every opening is rectangular and at least `MinGlassSizeMm` (50 mm) wide and high, face to face.

Every edit (`FrameEditor.*`) runs on a **copy**, validates it, and writes back only if valid. Otherwise it throws
`DesignValidationException` with a user-facing message and the model is unchanged.

## GlassPanel (derived geometry)

Glass is never drawn or dragged by the user. After every edit, `FrameLayout` derives the openings:

1. The X positions of vertical members and the Y positions of horizontal members cut the frame's
   centreline rectangle into a grid of cells.
2. Neighbouring cells are merged (union-find) unless a member covers the edge between them.
3. Each merged group is one opening, `LayoutRegion.CenterlineBounds`.
4. Its glass, `GlassPanel.Boundary`, is that rectangle shrunk to the **faces** of the surrounding members
   (by each member's half-thickness).

Example: 1200 × 1500, 60 mm profiles, mullion at 600 and transom at 750 gives four panes of **510 × 660** mm
(X 60–570 and 630–1140, Y 60–720 and 780–1440).

`Boundary` is the geometric daylight size: no rebates, clearances or pricing. Those belong to the calculation engine.
Panel **Ids are kept** when openings move or resize (matched by order), and when an opening splits (matched by the
opening containing the old centre), so selection and any per-glass data survive edits.

## Automatic dimensions

`AutoDimensions.Compute(frame)` returns overall width (top) and height (left), plus chain dimensions to the
mullion centrelines (bottom) and transom centrelines (right). Values come from the geometry and are recomputed on
every draw; they can't be typed in or stored inconsistently.

## Editing and undo

| Action | Command (`Fenestration.Core.Commands`) |
|---|---|
| Create frame | `CreateFrameCommand` |
| Delete frame | `DeleteFrameCommand` |
| Resize frame | `ResizeFrameCommand` |
| Add mullion / transom | `AddDivisionCommand` (`.Mullion(...)`, `.Transom(...)`) |
| Move division (panel) | `MoveDivisionCommand` |
| Move division (mouse drag) | `FrameEditCommand.FromCompletedEdit` (the whole drag is one step) |
| Delete division | `DeleteDivisionCommand` |

Frame edits store before/after `FrameSnapshot`s (domain data, Ids preserved), so undo/redo restores exactly,
including re-attached divisions and re-derived glass. A command that fails validation is not recorded.

## Hit testing

`FrameHitTester.HitTest(project, worldPoint, toleranceMm)` works purely in world mm. The UI converts the mouse
with `ScreenToWorld` and a 5 px radius with `ScreenToWorldDistance`, so picking is identical at every zoom.
Priority: division (nearest centreline, using its face-to-face body) → glass → frame.

## Calculation-engine integration

```csharp
Frame frame = project.Frames[0];
IReadOnlyList<Profile> profiles = frame.Profiles;         // outer frame + mullions + transoms, centrelines + thickness
IReadOnlyList<GlassPanel> glass = frame.GlassPanels;      // face-to-face openings
FrameLayoutResult layout = FrameLayout.Compute(frame, rules);   // openings with centreline and glass bounds
Rectangle2D body = FrameLayout.GetMemberBody(frame, mullion);   // face-to-face member extent
```

None of this references WPF. The engine should work on `IDesignService.GetProjectSnapshot()` (a deep copy
with Ids preserved) and map results back by `Profile.Id` / `GlassPanel.Id`.
