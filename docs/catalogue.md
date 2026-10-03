# Product catalogue, systems and bundles (Milestone 13)

The owner of MARK keeps one **master catalogue** of everything companies can use — profiles, glass, hardware and
accessories, **product systems** and **bundles** — and gives each company the part it bought. Companies cannot add,
change or remove catalogue items; they enter only **their own prices**.

## Systems

A **system** is a product range, e.g. *62mm Casement – uPVC* or *Series 60 Sliding – Aluminium*:

| Field | Meaning |
|---|---|
| Material | uPVC or Aluminium (licensed separately: a company gets only systems of products it is licensed for) |
| For | Windows, doors, or both |
| Frame / mullion / transom / sash / mesh shutter | The profiles a design in this system uses |
| Glass, glass thickness | The default glass and the range the system takes (e.g. 20–28 mm) |

A window (frame) is made **in a system**. New frames use the library's default system. In MARK's properties panel,
**System** changes it: every member takes the system's profile (outer size unchanged, outer members keep their outside
faces), and glass that does not fit falls back to the system's glass. Profile and glass pickers offer only what fits the
frame's system; glass outside its range is a warning.

## "Used with"

Every profile, glass and hardware item can be marked as **used with** some systems (none = any system), and hardware also
with opening types. Pickers use it, and the catalogue sends an item with the systems it is used with.

## Reinforcement

A profile can have **steel reinforcement**: the steel section (a profile with the *Reinforcement* role), from which bar
length on (empty = every bar) and how much shorter it is cut. Every bar of that profile (frame members, mullions,
transoms, sash bars) gets its steel, listed in the bill of materials under **Reinforcement**, in the cutting plan and in the
price. The pricing's per-metre reinforcement rate then no longer applies to that window.

## Bundles

A **bundle** is a set of parts that always go together:

- **With every bar of a profile** (a *member bundle*), e.g. *Sliding 2-track frame*: track rail and drainage on the
  **bottom** bar, brush seal on every bar. A part can be limited to a side: top, bottom, left, right, horizontal, vertical.
- **With every opening of some types** (an *opening set*), e.g. *Casement hardware*: hinges **by size** (2 up to 1200 mm
  sash height, 3 above), a handle, a multipoint lock. Opening sets replace the pricing's per-sash hardware rate for those
  openings.

Each part is a profile (cut to the bar length, or the measured size, minus a deduction, and put on the cutting list) or a
hardware/accessory item counted **per piece**, **per metre** or **by size** (measured on the bar, or on the opening's
width, height, longest side or perimeter). A bundle can belong to one system or to any.

## The owner's catalogue (MARK Owner → Catalogue)

1. **Use the sample catalogue**, **Import file…** (a library file), or **Edit catalogue…** to open the Library Manager on a
   working copy. Closing it with changes asks to **publish** them.
2. In each company's account (and in each company type, for new accounts) tick, under **Catalogue**, the **systems** it
   gets — they bring everything they use — and any further single items. Items that come with a ticked system show as
   "with its system".
3. The server cuts each company's catalogue from the master (only systems of products it is licensed for), and its
   fingerprint goes into the company's signed licence.
4. At its next check-in MARK downloads the catalogue, checks it against the licence, and applies it: new items are added,
   changed ones updated (the company's own prices are kept), and ones no longer given are retired, so saved quotes still
   open and price.

A company with nothing ticked keeps its own library (as before Milestone 13).

## In MARK

- With a catalogue, the **Library Manager** opens in **prices-only** mode: products, systems and bundles are read-only,
  and only cost per metre / m² / unit can be changed. It is available to every company with a catalogue (pricing needs it).
- Without a catalogue, the Library Manager edits everything, including **Systems** and **Bundles** (Show: Systems /
  Bundles), reinforcement and "used with".
- Existing libraries get the sample systems (with the items they need) once, when MARK starts.

## Code

| Where | What |
|---|---|
| `Mark.Core/Library/Systems.cs` | `ProductSystem`, `Bundle`, `BundlePart`, `UsedWith`, `ReinforcementRule`, `CatalogueSelection` |
| `Mark.Core/Library/CatalogueSelector.cs` | A company's part of the master (closure of everything needed, licensed materials only) |
| `Mark.Core/Design/FrameEditor.Systems.cs` | `TrySetSystem`; `SetFrameSystemCommand` |
| `Mark.Calculation/CalculationEngine.cs` | System defaults, member bundles, opening sets, reinforcement |
| `Mark.Data` | Schema 4 (systems, bundles, JSON columns; schema 5 adds quote history, see areas.md), `LibraryService.ApplyCatalogue` |
| `Mark.LicenceServer` | Schema 2 (catalogue, selections), `PublishCatalogue`, `ClientCatalogue`, catalogue hash in licences |
| `Mark.Designer` | `CatalogueSync`, system picker, Library Manager (shared with MARK Owner) |
| `Mark.Owner` | Catalogue page, `CatalogueChoiceViewModel` in the account and company-type editors |

## Not yet

- Door frames and inward/outward variants as separate design choices (systems already say windows, doors or both).
- Couplers joining frames in a design (coupler profiles can be in the catalogue).
