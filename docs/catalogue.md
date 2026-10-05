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

A window (frame) is made **in a system**, and **the design decides which**: a sliding design goes in a sliding system, a
casement design in a casement system, keeping the brand and material the window was in (a system "makes" an opening
when its opening sets cover it, or, without opening sets, by its frame: a track frame is sliding). Applying a design to
a window moves it to such a system on its own, in the same undo step.

A new design (**New design**, the Frame tool, or a ready-made design put on an empty spot) opens **New design** over the
properties: **Design ref.** and **Quantity** (required), **Location**, the **brand** (the maker of the system's frame
profile, "Other" when it names none; asked only when the catalogue has systems of two or more makers, and not for a
company's own ready-made designs, which come in their own system) and the **glass** for every pane (what fits the
system). **Apply** sets them in one undo step; **Cancel** keeps the design as it was made. The last brand and glass are
offered first next time. Afterwards, in MARK's properties panel,
**System** changes it: every member takes the system's profile (outer size unchanged, outer members keep their outside
faces), and glass that does not fit falls back to the system's glass. Profile and glass pickers offer only what fits the
frame's system; glass outside its range is a warning.

## "Used with"

Every profile, glass and hardware item can be marked as **used with** some systems (none = any system). Pickers use it,
and the catalogue sends an item with the systems it is used with.

Every item is also marked **For: Casement, Sliding or both** — required for a new item (it cannot be saved without),
shown for existing ones (an item never marked counts as both). Casement covers side hung, top / bottom hung, tilt &
turn and pivot. When designing, the profile and glass pickers offer only the items for the selected window's openings:
a casement pane only items for casement, a sliding pane only items for sliding, a fixed pane any item.

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

## How glass looks

Every glass has a **look**: clear, tinted, frosted, reflective, patterned or designer, and a colour (clear blue, grey,
bronze, green, blue, smoke, gold, milky white), set in the Library Manager under *Look in drawings*. The canvas, the
design cards, the library thumbnails and the quotation draw each pane with its glass's look, so frosted glass shows
frosted and bronze glass bronze. The look comes with the catalogue like everything else.

## The owner's catalogue (MARK Owner → Catalogue)

1. **Use the sample catalogue**, **Import file…** (a library file), or **Edit catalogue…** to open the Library Manager on a
   working copy. Closing it with changes asks to **publish** them. **Import file…** asks where the file goes:
   - **Universal catalogue: add these items** — everything in the catalogue stays; items whose id is already used are
     kept as they are;
   - **Universal catalogue: replace it** — the whole catalogue becomes the file;
   - **One company's own items** — only that company gets them (see below), its profiles in a tab per series.

   For the catalogue, **Give its systems to** ticks the file's systems in the chosen companies' accounts at once (a
   company gets a system only with a licence for its material; the message says when one is missing).
   The page lists the catalogue's **systems** and its **items**: profiles by series, then glass and hardware.
2. In each company's account (and in each company type, for new accounts) tick, under **Catalogue**, the **systems** it
   gets — they bring everything they use — and any further single items under **Universal items**, grouped like the
   Catalogue page (profiles by series, then glass and hardware; a group's tick box ticks all of it). Items that come
   with a ticked system show as "with its system".
3. The server cuts each company's catalogue from the master (only systems of products it is licensed for), and its
   fingerprint goes into the company's signed licence.
4. At its next check-in MARK downloads the catalogue, checks it against the licence, and applies it: new items are added,
   changed ones updated (the company's own prices are kept), and ones no longer given are retired, so saved quotes still
   open and price.

A company with nothing ticked and no own items keeps its own library (as before Milestone 13).

## A company's own items

Products made for **one company only**: its own profiles, glass, hardware, systems and bundles (for example a special
frame, its own brand of handle, or a system only it sells). On MARK Owner's **Catalogue** page, under **Companies' own
items**, **Edit own items…** next to a company opens the Library Manager with the catalogue and the company's own items:

- everything **added** there is the company's own (ids must differ from the catalogue's); own items can **use catalogue
  items** — an own system with the catalogue's sash, an own frame with the catalogue's steel, an own hardware set with
  catalogue hinges — and those come along automatically;
- choosing another **default system** there makes it the system the company's new windows start in;
- changes to **catalogue items** made there are not kept (MARK Owner says so): change those on the Catalogue page.

In the Library Manager — in MARK Owner's *Edit own items…* and in the company's MARK — they are listed apart, under a
heading with the company's name (**Sozluk — own items**), after the catalogue's items.

**Tabs.** The owner can sort a company's own items into tabs, e.g. **50 Series** and **60 Series**. Above the list are
the tabs **All · Catalogue · 50 Series · 60 Series · Other own items**; a click shows only that tab's items, and the list
heads them **Sozluk — 50 Series**. In *Edit own items…*:

- **+ New tab** makes one (it is shown at once, empty); **Rename** and **Remove** act on the tab chosen (removing a tab
  keeps its items, under *Other own items*);
- **Tab** at the top of an own item's form moves it to another tab at once;
- **New** while a tab is shown puts the new item in that tab.

The tabs are saved with the company's own items when the Library Manager is closed (also when only the tabs changed),
and the company sees the tabs that have items in them in its MARK, where it cannot change them.

The company's own **systems** also appear in its MARK as ready-made designs: the design library on the Drawing tab gets
a category named after the company, with a section per own system holding the designs that suit it (those its hardware
sets cover — sliding designs for a sliding system, casement and tilt & turn for a casement one). Clicking or dragging one
makes the window in that system, in one undo step.

The company always gets its own items, on top of what is ticked from the catalogue (systems only for products it is
licensed for). No other company sees them. Saving them changes the company's catalogue fingerprint, so MARK fetches them
at its next check-in. A catalogue that would clash with a company's own items (same id, or removing something they use)
is not published, and MARK Owner says which company is affected.

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
| `Mark.Core/Library/CompanyItems.cs` | A company's own items: `Combine` with the catalogue, `Split` an edited copy, `CatalogueChanges`; their tabs (`OwnItemTab`, `WithTabs`) |
| `Mark.Core/Library/CatalogueSelector.cs` | A company's part of the master (closure of everything needed, licensed materials only) |
| `Mark.Core/Design/FrameEditor.Systems.cs` | `TrySetSystem`; `SetFrameSystemCommand` |
| `Mark.Calculation/CalculationEngine.cs` | System defaults, member bundles, opening sets, reinforcement |
| `Mark.Data` | Schema 4 (systems, bundles, JSON columns; schema 5 adds quote history, see areas.md), `LibraryService.ApplyCatalogue` |
| `Mark.LicenceServer` | Schema 2 (catalogue, selections), schema 4 (`company_items`), `PublishCatalogue`, `SaveCompanyItems`, `ClientCatalogue`, catalogue hash in licences |
| `Mark.Designer` | `CatalogueSync`, system picker, Library Manager (shared with MARK Owner) |
| `Mark.Owner` | Catalogue page, `CatalogueChoiceViewModel` in the account and company-type editors, own items (`LibraryWorkingCopy`) |

## Not yet

- Door frames and inward/outward variants as separate design choices (systems already say windows, doors or both).
- Couplers joining frames in a design (coupler profiles can be in the catalogue).
