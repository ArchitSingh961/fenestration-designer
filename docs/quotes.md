# Quotes, Clients and Designs (Milestone 10)

## The model

A **project is a quote**. `Project.Quote` (`QuoteInfo`) holds its number, status (`Active`, `Won`, `Lost`), the client
(`ClientInfo`: title, name, company, phone, email, site address) and free-text notes (the client's requirements). The
quote's **designs** (window types) are the project's frames; each frame's `DesignInfo` gives its reference (W1, W2…)
and **quantity**.

- `QuoteEditor.TrySetQuote` validates and stores name and details (name required; email and phone checked; text
  trimmed and length-limited). `SetQuoteInfoCommand` makes it one undo step.
- `QuoteTotals.Of(project)`: designs, pieces (quantities added up) and window area in m² (each design × its quantity).
- `DuplicateFrameCommand` copies a design (new Ids, next reference, same quantity) to the right of the others.
- A copy of a project (`Clone`, Save a Copy As) is a new quote: its number is cleared.

## Numbers and the quote list (database schema 2)

The `projects` table gained summary columns (`quote_number`, `client_name`, `status`, `design_count`, `quantity`,
`area_m2`, `value`, `currency`), rewritten on every save; the document stays the source of truth.

- **Numbering**: the first save gives a quote `QT-00001`, `QT-00002`, … (one more than the highest in use, inside the
  save transaction). The number is written into the document. A failed save does not consume a number.
- **Value**: the designer saves each quote with its priced value (each design's calculated price × its quantity).
- **Upgrade**: a schema-1 database is upgraded in place; quotes saved before get numbers (in creation order), client,
  status and totals from their documents when the store opens. Their value shows as "—" until saved again.

## Screens

| Page | What it shows |
|---|---|
| **Dashboard** | quotes created this month, active, won, lost (count, value, pieces); value by status; win rate; recent quotes (click to open) |
| **Quotes** | Active / Won / Lost / All tabs with counts, search (number, project, client); number, project, client, status, designs, qty, area, value, last change. Double-click or Open; Delete (not the open quote); Create quote |
| **Quote → Client** | project name, quote number, status; client; site address; requirements and notes. "Save details" stores them as one undo step; "Discard changes" reloads the form |
| **Quote → Designs** | a card per design: picture, reference, name, quantity, size, openings, glass, location, price each and total; Edit design / Duplicate / Delete; totals and value; New design |
| **Quote → Drawing** | the designer (library, canvas, properties) |

The header always shows the open quote (number and name), its pieces and value, **New quote** and **Save**. Opening
another quote or starting a new one asks before discarding unsaved changes. A quote named on its Client tab is saved
under that name without asking again.

## Not yet

Prices come from the product library (Library Manager: price per metre of profile, per m² of glass, per unit of
material). Cost heads such as wastage, coating, labour, transport, discount and tax, and pricing of sash and mesh bars,
come with Milestone 11. The bill of materials and cutting plan still count each design once (quantities are applied to
the quote value only).
