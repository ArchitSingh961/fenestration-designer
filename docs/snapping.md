# Snapping

`Mark.Core.Snapping`: pure C#, world millimetres, modular.

## Architecture

```text
SnapEngine (settings + providers)
   BeginSession(project, excludeIds)   ← once per interaction; collects targets
        │  EndpointSnapProvider      frame corners, centreline endpoints
        │  MidpointSnapProvider      frame-edge and centreline midpoints
        │  IntersectionSnapProvider  centreline crossings / T-junctions (Core Intersection2D)
        │  CenterSnapProvider        frame centre, opening centres (recomputed without the moving parts)
        │  EdgeSnapProvider          frame outer edge, inner opening, division faces (line targets)
        │  GridSnapProvider          world-grid quantisation (no target list)
        ▼
   SnapSession.SnapPoint(point, tolMm)          → SnapResult { Point, Type, Distance }
   SnapSession.SnapCoordinate(axis, v, tolMm)   → AxisSnapResult { Value, Type, Target }
```

- **Add a provider**: implement `ISnapTargetProvider` (`Type`, `CollectTargets(scene, list)`) and pass it to the engine.
- The Milestone 1 contract `ISnapProvider` is still available through `ProjectSnapProvider` (one-off point queries).
- **Exclusion**: the objects being edited are not targets, and neither is geometry that moves with them: an excluded
  frame's contents, divisions ending on an excluded division, and points lying on an excluded centreline. A mullion
  therefore never snaps to its own stale position.

## Priority (deterministic)

Default `SnapSettings.Priority`:

1. **Intersection**
2. **Endpoint**
3. **Midpoint**
4. **Center**
5. **Edge**
6. **Grid**

The engine walks this list; the **first type with a target within tolerance wins**, and within that type the
**nearest** target wins. So a closer midpoint does not beat an intersection that is also within tolerance. The
order is configurable. **Grid** is used only when no geometry target is within tolerance, and then it quantises the
position (598 → 600 with a 10 mm grid). With nothing applicable the raw position is kept and rounded to
`RoundingIncrementMm` (1 mm), so committed geometry is always in whole millimetres unless it snapped to something
that isn't.

## One-axis snapping

A mullion only moves in X, so `SnapCoordinate(X, …)` measures distance **along X only**. Point targets contribute
their X; edge targets contribute only if they are vertical. This makes a mullion align with any target in its column:
the frame centre, the bay centre (equal split), or a mullion in another bay. The UI draws the marker at the target with a
dashed alignment line to the mullion.

## Tolerance

`SnapSettings.TolerancePixels` (default **8 px**) is the user setting, shown in the left panel. It is converted per
interaction with `ToleranceMm(zoom)` (8 px at 50 % zoom = 16 mm), so snapping feels the same at every zoom. Nothing in
the code compares against a literal distance.

## Settings (`SnapSettings`)

| Setting | Default | UI |
|---|---|---|
| `ObjectSnapEnabled` | on | "Object snap" checkbox, Tools menu |
| `GridEnabled` | off | "Grid snap" checkbox, Tools menu |
| `GridSpacingMm` | 10 | "Grid step" |
| `TolerancePixels` | 8 | "Tolerance" |
| `RoundingIncrementMm` | 1 | — |
| per-type flags, `Priority` | all on, see above | — |

## Feedback

While dragging, `OperationPreview.Snap` carries the target point and type; the overlay draws a type-specific glyph
(□ endpoint, △ midpoint, ✕ intersection, ○ centre, edge tick, + grid) with its name, before the mouse is released.

## Snapping is not validation

```text
candidate → snap → validate (FrameEditor on a copy) → accept, or stop at the last valid position
```

A snapped position can still be rejected, e.g. snapping a mullion onto the frame centreline reports
"Two profiles are at the same position".
