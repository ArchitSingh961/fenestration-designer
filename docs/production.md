# Production (Milestone 16)

The Production area: **Production orders · Cutting plan · Offcuts**. Production orders and offcuts need the
*Production orders* feature (in the Complete package, or as an add-on in the company's account); the Cutting plan of the
open quote needs *Cutting plans*, as before.

## Production orders

A production order is the workshop's job for a **confirmed order** (a quote converted to an order, OR-00001…).

- **Start production**: on an order's header (Sales or Design, next to *New revision*), or on the Production orders
  page, *Start production of an order*, which lists the orders not yet in production. The order must be saved.
- The designs are **kept as they were** when production started: later changes to the quote do not change what is
  being made. One production order per order; starting again opens it.
- The page lists every production order with its stage and how far it is (a bar and a percentage). The chosen one
  shows its quote, client, who started it and when, the **due date**, **notes for the workshop**, its papers and its
  progress. **Delete** removes the production order and its progress (the order stays).

### Progress per window

Each design of the order has five steps: **Cut, Assembled, Glazed, Ready, Dispatched**.

- Click a step to mark it done for all of the design's windows (click again to undo); for a design made more than once,
  **−** and **+** count one window at a time ("2 / 3").
- **All cut**, **All assembled** … above the columns mark a step done for every window of the order.
- The stage is the furthest step every window has reached ("All glazed", "Dispatched"), "In production" once anything
  is done, or "Not started"; the percentage counts every step of every window.

### Workshop papers (PDF, opened for printing)

| Paper | What it holds |
|---|---|
| **Cutting list** | Per profile, then the **steel reinforcement**: the stock to take (e.g. "3 × 6000 mm new · 1 offcut from stock"), and bar by bar the pieces in cutting order, each with its **label number** (P1, P2…), length, cut angles and what it is for ("W1 · frame top", "W2 · sash, opening 1", "W1 · steel in frame left"), and the leftover (an offcut back to stock, or waste). Offcuts from stock are shaded green. |
| **Glass order** | For the glass supplier: glass type, thickness, cut size (width × height), quantity, area each and in all, and the windows it is for; totals per glass type. |
| **Hardware pick list** | Hardware and accessories for the whole order (every window, times its quantity): item, code, kind, quantity, unit, windows; a column to tick when taken from the store. |
| **Shop drawings** | A page per design: its drawing with dimensions and glass sizes (view from inside), size, quantity, system, location and openings, and its pieces (profiles and steel with lengths, angles and positions), glass and hardware per window. |
| **Piece labels** | A label for every profile piece (P1…, numbered as on the cutting list) and every glass pane (G1…): order number, window, item, size and angles, position — 3 × 7 labels (70 × 40 mm) on an A4 sheet. |

The papers use the company's saw rules (kerf, trim, minimum usable offcut) and the current library.

## Offcuts in stock

**Production › Offcuts** lists the reusable leftover bars kept in the workshop, by profile (length, where it came from,
when it was added), and adds offcuts by hand (profile, length, how many) or removes them.

- With **Cut from offcuts in stock first** (on, by default), the cutting list places pieces in offcuts of the same
  profile before new bars (best fit, no trim allowance on an offcut); those bars cost nothing and are not counted as
  stock to take.
- After the order is cut, **Update offcuts…** takes the offcuts it used out of stock and puts its new reusable leftovers
  in (marked with the order number). This is done once per order.

Full stock of bars, glass and hardware, purchase orders and reservations come with Milestone 18.

## Code

| Where | What |
|---|---|
| `Mark.Core/Production` | `ProductionOrder` (designs kept as JSON, progress per design and step, due date, notes), `ProductionStep`, `Offcut` |
| `Mark.Calculation` | `CuttingOptimizer` cuts from `StockOffcut`s first (`IOffcutCuttingOptimizer`); `StockBar.OffcutId`, `ProfileCuttingPlan.OffcutsUsed`, `CuttingPlan.OffcutIdsUsed` |
| `Mark.Data` | Schema 8: `production_orders`, `offcuts`; `SqliteProductionRepository` (`LocalStore.Production`) |
| `Mark.Reports` | `ProductionDocument`, `ProductionPdf` (the five papers) |
| `Mark.Designer` | `ProductionBuilder` (order → papers), `ProductionViewModel`, `OffcutsViewModel`, `MainViewModel.Production` (`StartProduction`) |
