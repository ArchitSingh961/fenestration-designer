# Local Persistence (Milestone 8)

`Fenestration.Data` (plain `net8.0`, references **Core only**, `Microsoft.Data.Sqlite`) stores the product library and
saved projects in one SQLite file. Core and Calculation never reference it or SQLite (`ArchitectureTests`). The view
models call its services; there is no SQL outside `Fenestration.Data`.

```text
App (composition: LocalStore.Open, WpfDialogService)
 ↓
Designer view models ── MainViewModel (save/open/import/export), LibraryManagerViewModel, ProjectListViewModel
 ↓
Fenestration.Data ──── LocalStore → LibraryService (in-memory snapshot, validated CRUD) → ILibraryRepository
                                  → IProjectRepository
 ↓                      SqliteDatabase (create, check, upgrade) → SQLite file
Core (ProductLibrary, Project, ProjectSerializer)          Calculation (unchanged: reads IProductLibrary + Project)
```

## Database file and first run

- Location: `%LOCALAPPDATA%\Fenestration\fenestration.db` (`LocalStore.DefaultPath`).
- `SqliteDatabase.Open`: creates the folder and file if missing, runs `PRAGMA quick_check`, then:
  - new file → creates schema version 1 in one transaction, sets `PRAGMA application_id` (`FEN1`) and `user_version`;
  - our file, older schema → applies the upgrade scripts in order, each in its own transaction;
  - newer schema, a foreign SQLite file, or a damaged file → `DataStoreException`, and the file is **not modified**.
- Every connection has foreign keys on and is not pooled (the file is released after each operation).
- `LocalStore.Open`: if the library is empty, the shipped `Library\library.json` is imported in one transaction
  (ids, order and values kept). Later starts never re-import. An unreadable seed leaves the library empty and says so.
- If the database cannot be used, the app starts with the shipped library file, read-only, and explains why: designing
  and calculating still work; saving and library editing are disabled.

## Schema (version 1)

| Table | Key | Holds |
|---|---|---|
| `profiles` | `id` TEXT | `ProfileDefinition` scalars, `sort_order`, `properties_json`, `is_active` |
| `profile_roles` | (`profile_id`, `position`) | roles; FK → profiles (cascade) |
| `profile_stock_lengths` | (`profile_id`, `position`) | `StockLengthsMm`; FK → profiles (cascade) |
| `profile_material_usages` | (`profile_id`, `position`) | usages; FK → profiles (cascade), FK `material_id` → materials |
| `glass` | `id` TEXT | `GlassDefinition` scalars, `sort_order`, `properties_json`, `is_active` |
| `glass_material_usages` | (`glass_id`, `position`) | usages; FK → glass (cascade), FK → materials |
| `materials` | `id` TEXT | `MaterialDefinition` (hardware, gaskets, accessories, consumables), `sort_order`, `is_active` |
| `library_settings` | `id` = 1 | currency and the four defaults (FKs → profiles / glass) |
| `projects` | `id` TEXT (the project Guid) | name, `format_version`, `document_json`, created/modified UTC |
| `project_references` | (`project_id`, `kind`, `definition_id`) | explicit library references of a saved project; FK → projects (cascade) |

Indexes: product name, manufacturer, series/category; usages by material; `project_references (kind, definition_id)`;
`projects (modified_utc)`.

- **Ids** are the library's string ids (case-sensitive) and the design's Guids, unchanged, so references survive any
  number of saves and reloads. Ids are unique across all product kinds; `LibraryService` enforces this by validating
  the whole library before each write.
- **Prices** are `decimal`, stored as invariant text so they round-trip exactly. Lengths/weights are `REAL` (exact).
- **Order**: every read orders explicitly (`sort_order, id`; child rows by `position`; projects by
  `modified_utc DESC, name, id`). A new product is appended; an edited one keeps its place. So a library loaded from the
  database equals the library that was stored, and calculations and cutting plans never depend on storage order.
- **Project → product references are not foreign keys** on purpose: a project may reference a product this library
  does not have (M6 reports that as a calculation issue). `project_references` provides the lookup instead.
- **Why projects are documents.** A frame is a validated aggregate that is always loaded whole, and glass is derived
  from it; storing frames/profiles/glass in tables would duplicate the domain schema and its format migrations.
  `ProjectSerializer` (versioned, validated, Id-preserving) is reused, so a saved project and a project file are the
  same format.

### Changing the schema

Add the upgrade script for `CurrentVersion → CurrentVersion + 1` to `DatabaseSchema.Upgrades`, then increment
`DatabaseSchema.CurrentVersion`. Upgrades must preserve data. Older applications refuse the newer file.

## Library management (`LibraryService`)

- `Current` is an immutable, validated `ProductLibrary` held in memory. Pickers, rendering and calculations read it;
  they never touch the database. After a committed change the snapshot is reloaded and `Changed` is raised;
  `MainViewModel.ReplaceLibrary` then switches the pickers and `CalculationService.UseLibrary` recalculates the BOM,
  cost and cutting plan.
- **Add / Update** build the whole candidate library first (`new ProductLibrary(...)` applies the same validation as a
  library file: unique ids, names, non-negative prices, roles, usages pointing at materials, defaults that fit their
  role) and only then write, in one transaction. A rejected change writes nothing. Ids are permanent: `Update` keeps
  the id; a new id is an `Add`.
- **Search / filter**: `LibraryQuery` text (all words, in id/name/code/manufacturer/series/category), `Role`,
  `Manufacturer`, `Group` (profile series or glass category), `MaterialCategory`, `IncludeInactive`, `Limit`.
- **Retire** (`SetActive(false)`): the product is hidden from pickers and searches (unless `IncludeInactive`) but still
  resolves by id, so existing designs keep pricing; the calculation adds a warning "… is retired in the library".
- **Delete** is allowed only when nothing uses the product. `FindBlockers` lists: library defaults, other products'
  usages (materials), the open project, and saved projects (`project_references`; the saved version counts even for
  the open project). Otherwise `LibraryOperationException` explains why and suggests retiring.
- **Import** adds the products whose id is new and **skips** (reports) existing ids, never overwriting. Currency and
  defaults are imported only into an empty library. The merged library is validated first; it is all or nothing.
- **Export** writes the library-file JSON (`LibrarySerializer`).

## Projects (`IProjectRepository`)

| Action | What happens |
|---|---|
| Save | `ProjectSerializer.Serialize` → upsert `projects` row and rewrite its `project_references`, one transaction. The created time is kept, the modified time updated. |
| Open | load the document → `ProjectSerializer.Deserialize` (upgrades older formats, validates) → the designer shows it; undo history and selection are cleared (session state, never saved). |
| Save a Copy As | `Project.Clone()` (new Ids everywhere) with a new name, saved and opened. |
| Import project file | read a project JSON file; if its Id is already saved, it becomes a copy (new Ids) so nothing is overwritten. Not saved until Save. |
| Export project file | write the open project as project JSON. |
| Delete (Open dialog) | after confirmation; the project open in the designer cannot be deleted. |

A missing or damaged saved project gives a `DataStoreException` with a readable message; the open design is unchanged.

## Errors while running

Every repository operation runs inside `SqliteDatabase.Guard`: if the file is locked by another copy of the application
(after waiting up to 5 s), moved, deleted or damaged while the application runs, the operation fails with a
`DataStoreException` ("The local database could not …"), never a raw SQLite error. Writes are single transactions, so
nothing is half-saved, and `LibraryService` keeps its previous snapshot. The view models show the message and keep the
open design. As a last resort, `App` handles any unexpected error on the UI thread by showing it instead of closing.
A deleted database file is recreated (and seeded) on the next start.

## Not stored in the database

- `Settings\calculation-rules.json` (saw kerf, trim, minimum offcut, glass clearance): workshop settings, still a file.
- Undo/redo history, selection, viewport: session state.
- Cutting-plan remnants: reported per plan, not yet kept as stock.
