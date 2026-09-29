# Geometry Engine

`Fenestration.Core.Geometry` — pure C# (no WPF), usable from a console app, the calculation engine or a server.
An automated test (`ArchitectureTests`) fails if Core ever references WPF or the UI projects.

## 1. Coordinate system

```text
(0,0) ───────── X → ────────── (1200,0)
  │                               │
  │           frame               │
  Y                               │
  ↓                               │
(0,1500) ─────────────────── (1200,1500)
```

- Origin is the **top-left** of the world. **X → right, Y → down** — the same orientation as the screen,
  so world ↔ screen needs no axis flip.
- Angles are in **degrees** at every public API, measured from +X towards +Y.
  Because Y points down, **positive angles turn clockwise on screen**: 0° →, 90° ↓, 180° ←, 270° ↑.
- `Vector2D.Cross(a, b) > 0` means `b` is clockwise (on screen) from `a`.
- In the domain model, `Frame.X/Y` is in world mm; a frame's profiles, glass and dimensions are
  **relative to the frame origin**.

## 2. World units

All model geometry is `double` **millimetres**. Screen pixels are never stored in the model.

## 3. Screen units

WPF device-independent pixels (1/96 inch). They exist only inside `ViewportTransform` and the renderer.

## 4. Transformations

**`Transform2D`** — immutable affine transform:

```text
x' = M11·x + M12·y + OffsetX
y' = M21·x + M22·y + OffsetY
```

Factories: `Identity`, `Translation`, `Scale` (optionally about a centre), `Rotation` (optionally about a centre).
Compose with `a.Then(b)` (apply `a`, then `b`). `Inverse()` / `TryInvert()` handle undo mapping; singular
transforms (zero scale) can't be inverted. `TransformVector` ignores translation. Rotations by multiples of 90°
are exact (no `6e-17` residue), since fenestration geometry is mostly orthogonal.

**`ViewportTransform`** — the world ↔ screen mapping:

```text
screen = (world − Pan) × Zoom        Zoom = pixels per mm, clamped to [0.01, 50]
world  = screen / Zoom + Pan         Pan  = world point (mm) shown at the canvas top-left
```

- `ZoomAt(screenAnchor, newZoom)` keeps the world point under the cursor fixed:
  it records `w = ScreenToWorld(anchor)`, sets the zoom, then solves `Pan = w − anchor / Zoom`.
  This still holds when the zoom is clamped.
- `ZoomBy(anchor, factor)`, `PanByScreenDelta(delta)`, `FitTo(box, w, h, margin)`, `Reset()`.
- `WorldToScreenTransform` returns a `Transform2D` that the renderer converts to its own matrix type.
- `VisibleWorldBounds(w, h)` returns the world rectangle on screen, for grid drawing and culling.

## 4a. Viewport (Milestone 3)

**World coordinates** are millimetres in the model. **Screen coordinates** are WPF device-independent pixels,
with (0, 0) at the viewport's top-left. The only conversion between them is `ViewportTransform`,
owned by `CanvasViewModel` and used by every renderer and by the cursor readout.

- **Zoom limits** default to 0.05–50 px/mm via `ViewportSettings` (`MinZoom`, `MaxZoom`), applied with
  `ViewportTransform.SetZoomLimits`. The `MinZoom`/`MaxZoom` constants are only the default for a bare transform.
- **Wheel zoom** multiplies the zoom by `ZoomFactor^(delta / 120)` (1.15 per notch) through `ZoomBy(cursor, factor)`,
  so the world point under the cursor stays fixed. It still stays fixed when the zoom hits a limit.
- **Zoom In/Out** buttons use the same operation, anchored at the viewport centre.
- **Pan** (middle drag or Space+left drag) passes the screen delta to `PanByScreenDelta`, so content follows the mouse exactly.
  Pan changes only the view, never the model.
- **Fit** calls `FitTo(contentBounds, viewportSize, FitMarginFraction)`, centring the content and leaving 10 % margin.
- **Reset** sets 100 % zoom (1 mm = 1 px) with the world origin 40 px in from the top-left.
- **Cursor readout** is `ScreenToWorld(mousePosition)`, never a hand-written formula.

**Grid.** `GridSpacing.ForZoom(zoom, minPixels)` picks the finest step from the ladder
1, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000 mm whose on-screen spacing is at least 12 px.
Major lines are the next ladder step that is ≥ 4× minor and an exact multiple of it (e.g. 25 → 100, 50 → 250).
`GridAxisRange.Visible(min, max, spacing)` returns only the line indices inside the visible world rectangle,
and positions are computed as `index × spacing`, so there is no accumulated drift. No grid line is ever stored.

See [architecture.md](architecture.md) for rendering responsibilities and how Milestone 4 adds content.

## 5. Tolerance

All tolerances live in `GeometryTolerance`:

| Constant | Value | Meaning |
|-----------|--------|---------|
| `Default` | 0.1 mm | Modelling tolerance: points closer than this are "the same" for design decisions (on-segment, touching, collinear). Well below fabrication accuracy (~±0.5 mm), well above floating-point drift. |
| `Epsilon` | 1e-9   | Numerical guard for divisions and zero-length vectors. Not a design tolerance. |

Queries that use tolerance take an optional `tolerance` parameter defaulting to `GeometryTolerance.Default`.
Negative or non-finite tolerances throw.

Equality: `==`/`Equals`/`GetHashCode` on geometry structs are **exact**, so they're safe as dictionary and set keys.
Use `AlmostEquals(other, tolerance)` for geometric comparison.

## 6. Primitives

| Type | Notes |
|------|-------|
| `Point2D` | `DistanceTo`, `DistanceSquaredTo`, `Lerp`, `MidpointTo`, `AlmostEquals`, `IsFinite`; `Point − Point = Vector`, `Point ± Vector = Point`. |
| `Vector2D` | `Length`, `LengthSquared`, `Normalized()` (zero → `Zero`, never NaN), `Dot`, `Cross`, `Perpendicular`; `/ 0` throws. |
| `LineSegment2D` | `Length`, `LengthSquared`, `Direction`, `Midpoint`, `PointAt(t)` (t = 0 → Start, 1 → End), `ClosestPoint`, `DistanceTo`, `ContainsPoint(p, tol)`, `IsDegenerate`. |
| `Rectangle2D` | **Normalized**: `(X, Y)` is the top-left corner and `Width`/`Height` ≥ 0; the constructor throws otherwise. `FromCorners` normalizes any two corners. `Contains` (point with tolerance, or rectangle), `Intersects`, `Intersection` (→ `Rectangle2D?`), `Offset`, `Inflate`. Edges are closed (on-edge = inside). |
| `BoundingBox2D` | Immutable. `Empty`/`default` encloses nothing, and reading its extents throws, so a missing bound never silently becomes (0,0). Built `FromPoints`/`FromSegment(s)`/`FromRectangle(s)`; `Expand`, `Union`, `Contains`, `Intersects`. |
| `AngleMath` | `DegreesToRadians`, `RadiansToDegrees`, `NormalizeAngle` → [0, 360), `NormalizeSignedAngle` → (−180, 180], `DirectionAngle`, `AngleBetween`, `SignedAngleBetween`. |
| `GeometryValidation` | `EnsureFinite`, `EnsureNonNegative`, `EnsurePositive`, `EnsureValidTolerance`, `EnsureNonDegenerate` — each throws an `ArgumentException` subtype naming the argument. |

Invalid geometry is rejected where it enters: the `Rectangle2D`, `Transform2D` and `BoundingBox2D` factories,
the `ViewportTransform` setters, `Frame.Create`/`Profile.Create`, and project loading (`ValidationHelper.EnsureValidProject`).
`Point2D`/`Vector2D` constructors stay unchecked because they're on the hot path; check `IsFinite` or call `EnsureFinite` at boundaries.

## 7. Intersection behaviour

`Projection2D`
- `ProjectPointOntoLine` → foot of the perpendicular on the **infinite** line; `T` may be outside [0, 1].
- `ProjectPointOntoSegment` → same, clamped to the segment.
- Both return `(Point, T, Distance)`. A degenerate line projects to its start.

`Intersection2D`
- `IntersectLines(a, b)` — **infinite** lines. Returns `null` when parallel or coincident (no unique point).
  Throws for zero-length defining segments.
- `IntersectSegments(a, b, tol)` — **finite** segments. Returns `Kind`:
  - `None` — they don't meet (including parallel-offset, and crossing lines whose segments stop short).
  - `Point` — a crossing, a T-junction, a shared corner, or collinear end-to-end contact. `ParameterA`/`B` locate it on each segment.
  - `Overlap` — collinear with a shared stretch longer than `tol`; `Overlap` holds it (oriented like `a`).
  - Ends within `tol` of the other segment count as touching.
  - Segments are **parallel** when, over the longer one's length, they drift apart by ≤ `tol` mm.
    This is a length-based test in mm, not an angle, so long and short members are judged the same way.
- `IntersectSegmentWithRectangle(seg, rect)` — boundary crossings ordered from the segment start, with corners de-duplicated.
- `ClipSegmentToRectangle(seg, rect)` — the part inside the rectangle (Liang–Barsky), or `null`.

## 8. How snapping will use this layer (Milestone 8)

An `ISnapProvider` implementation will, for the cursor's world point `p` and tolerance `tol` (a pixel radius
converted with `ViewportTransform.ScreenToWorldDistance`):

1. Cull candidates whose `BoundingBox2D.Expand(tol)` doesn't contain `p`.
2. Collect candidate points: segment endpoints and `Midpoint`s, rectangle corners and `Center`,
   and `IntersectSegments(...).Point` between profile centre lines.
3. Collect candidate curves: frame edges (`Rectangle2D.GetEdges()`) and profile edges or centre lines,
   snapped with `Projection2D.ProjectPointOntoSegment(p, edge)`.
4. Pick the lowest `Distance` ≤ `tol`, with points preferred over edges, and return a `SnapResult`.

Grid snapping rounds `p` to the grid spacing in world mm, so it is independent of zoom.
