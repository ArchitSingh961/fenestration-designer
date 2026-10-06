# Openings, Design Library and Design Details (Milestone 9)

## Openings (sashes)

Every glass panel is an **opening** of the frame. `GlassPanel.Opening` (`OpeningType`) says how it opens, as seen from
**inside**; `GlassPanel.HasMesh` adds an insect-mesh shutter ("twin sash" = glass sash + mesh sash).

| `OpeningType` | Symbol on the drawing | Handle |
|---|---|---|
| `Fixed` | none (glazed into the frame) | — |
| `SideHungLeft` / `SideHungRight` | dashed lines from the hinged edge's corners to the middle of the other edge | opposite the hinges |
| `TopHung` / `BottomHung` | same, hinged at the top / bottom | bottom / top |
| `TiltTurnLeft` / `TiltTurnRight` | the turn triangle plus the tilt triangle (hinged at the bottom) | opposite the side hinges |
| `PivotVertical` / `PivotHorizontal` | dashed diamond and its axis | right / bottom |
| `SlidingLeft` / `SlidingRight` / `SlidingUp` / `SlidingDown` | arrow in the direction it opens | on the edge you pull from |

The opening belongs to the panel, so it survives moves, resizes, profile changes, undo/redo and save/load. When an
opening is split, the half that keeps the panel (the one containing the old centre, or else overlapping it most) keeps
its type; the new half is fixed.

**Rules** (`DesignRules`, checked after every edit in `FrameEditor`): an openable opening must be at least
`MinSashOpeningMm` (250) wide and high, face to face. A change that would make a sash smaller (moving a mullion,
resizing, applying a design) is rejected as a whole, with the reason ("opening 1 would be 160 × 1380 mm, too small for
a side hung sash…").

**Sash geometry** (`OpeningGeometry.SashOf`, Core, no WPF): the sash fills its opening; sliding panels that meet another
panel sliding the same way across a division run to the division's centreline (interlock). The band is
`SashFaceWidthMm` (55) wide, the handle sits in the middle of the band on its side, and `HandleHeightMm` is the height
of a side handle above the bottom of the frame ("HH = 750"). Sash bars are drawn but not yet priced (Milestone 11).

## Design library

`DesignTemplates` (Core) is the built-in library: **Dividers, Openable (casement, tilt & turn, twin sash, pivot),
Sliding (horizontal, vertical, monorail) and Mesh**. A `DesignTemplate` is a small tree:

```text
TemplateSplit(Vertical, [TemplateLeaf(SlidingRight), TemplateLeaf(Fixed), TemplateLeaf(SlidingLeft)])   // 3 panel, fixed centre
TemplateSplit(Horizontal, [Leaf(TopHung), Split(Vertical, [Leaf(SideHungLeft), Leaf(SideHungRight)])], weights 1:3)
TemplateLeaf(null, Mesh: true)                                                                          // add mesh, keep type
```

`FrameEditor.TryApplyTemplate(frame, template, targetGlassId, rules)`:

- **On one opening** (a selected or hovered glass panel) it splits only that opening and sets each part's type.
- **On a whole frame** it first removes every mullion and transom, then builds the design in the frame opening. The
  outer size, position, profile choices and glass type are kept. The UI asks first ("The existing design of W1 will
  be cleared. Do you want to proceed?") when the frame already has divisions, sashes or mesh.
- A **mesh-only** design (`KeepsLayout`) never touches divisions; on a frame it changes every opening.
- Divisions are placed so the parts' **glass** sizes follow the weights (equal by default), even when the mullion is
  thicker than the frame.
- Atomic: if any part would be too small, nothing changes and the reason is shown.

In the app the left **rail** picks a category; the panel shows its sections with thumbnails drawn by the same
`FrameRenderer` as the drawing (`DesignThumbnails`). **Click** a design to apply it to the selected openings or frames
(with nothing selected: the only frame, or a new frame of the design's suggested size). **Drag** a design onto the
drawing: the opening (or frame) under the mouse is highlighted, and dropping applies it there; dropping on empty space
creates a new frame at that point. Every application is one undo step.

## Design details

`Frame.Design` (`DesignInfo`) holds what the frame is as a product: **reference** (W1, W2… given automatically on
creation), **quantity**, name, location, floor, note and the **floor distance** (sill height above the finished floor).
They are edited in the properties panel (DESIGN) and stored through `SetDesignInfoCommand` (undoable, validated:
quantity 1–100 000, reference ≤ 30 characters, floor distance 0–20 000 mm). The drawing shows "W1 × 2 · Bedroom" above
the frame and, when a floor distance is set, a hatched floor line with "Floor distance = 900".

## Inside / Outside view

The switch at the bottom of the drawing shows the design as seen from outside: each frame is drawn from a mirrored copy
(`OpeningGeometry.Mirror`: geometry flipped about the frame's centre, left/right hinges and sliding directions swapped,
Ids and opening numbers kept). The model is never changed for display. The Outside view is read-only: the mouse only
pans, and design changes are refused until the Inside view is shown again.

## Labels

① ② … opening numbers (top-to-bottom, left-to-right) with the glass size under them; **S1, S2…** sashes; **M1…** mesh
shutters; **F1, F2…** frames (by position in the project); **HH = …** handle heights. Labels are screen-sized and are
left out when an opening is too small on screen to hold them.

## Fixed panes and mesh on any design

- A fixed pane (glass in the frame, no sash) is drawn with the usual glass mark — two short diagonal strokes at its
  top left — and a small **FIXED** at the bottom, on the canvas, in the design library thumbnails and in quotation and
  shop drawings.
- **Mesh for any design** is the first section of every design category: *Add mesh (keeps the design)* puts an insect
  mesh on the selected opening (or every opening of the selected frame) without changing how it opens or its divisions;
  *Remove mesh* takes it off.
