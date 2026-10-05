# MARK — Milestone Plan

MARK is a Windows desktop program for window and door companies: design, pricing, quotes, production, orders,
purchasing, inventory and accounts in **separate areas** of one application. It is sold to companies as **licensed
accounts**: the owner (admin) creates each client company, sets its sign-in, product types (uPVC / Aluminium, sold
separately) and the **feature package** it paid for, and can extend, suspend or revoke it at any time.

Each milestone ends with tests, updated documentation and a commit to `main`. Large milestones are built in parts.

Decisions marked **(default — confirm)** are the recommended choice where the owner has not decided yet; they are
used unless changed before that milestone starts.

---

## Done

| # | Milestone | Result |
|---|---|---|
| 1 | Project setup | Solution, shared model, application shell |
| 2 | Geometry engine | Exact millimetre geometry, tolerances, validation |
| 3 | Drawing canvas | Grid, zoom, pan, fit, cursor read-out |
| 4 | Frame designer | Frames, mullions, transoms, derived glass, dimensions, resizing |
| 5 | Editing | Selection, box selection, dragging, resize handles, snapping, undo/redo |
| 6 | Library and costing | Product library, bill of materials, costs, material changes after design |
| 7 | Cutting plans | Stock bars, cut lengths, offcuts and waste |
| 8 | Local storage | SQLite database, Library Manager, save/open |
| 9 | Openings and design library | Fixed, casement, top/bottom hung, tilt & turn, pivot, sliding, mesh; click-or-drag design library; Inside/Outside view |
| 10 | Quotes and clients | Dashboard, quote list, client details, design cards, quote numbers |
| 11 | Pricing | Price structure per quote (cost lines, rates, discount, charges, GST), live summary, saved default; sash and mesh priced; quantities everywhere |
| 12 | Accounts, sign-in and licensing | Licence server, MARK Owner (companies, company types, packages, keys), sign-in, signed licences with daily check-in and 7-day grace, read-only when suspended or expired, features locked by package (see licensing.md) |
| 13 | Product catalogue, systems and bundles | Owner's master catalogue in MARK Owner; systems (uPVC / Aluminium) with default profiles and glass range; "used with"; steel reinforcement; bundles per profile and hardware sets per opening type (by size); per-company and per-type selection; delivered at check-in; prices-only library in MARK (see catalogue.md) |
| 14 | Areas, staff logins and who did what | Area bar (Sales, Design, Pricing, Library, Production, Orders, Purchasing, Inventory, Accounts) with tabs; staff logins with their own features, up to the account's users; staff see only what they were given; quotes record who created and saved them, with a history (see areas.md) |
| 13+ | A company's own items | Products the owner makes for one company only (profiles, glass, hardware, systems, bundles), using catalogue items, delivered with its catalogue (see catalogue.md) |
| 15 | Sales | Enquiries (two-step form, stages, sources, follow-ups) → quote → order; quote revisions; quotation PDF in the usual layout with the company's setup; sales charts by week/month, person and city (see sales.md) |
| 16 | Production | Production orders from confirmed orders (designs kept as they were); progress per window (cut, assembled, glazed, ready, dispatched); cutting list with steel and offcuts in stock used first; glass order; hardware pick list; shop drawings; piece labels; offcuts in stock (see production.md) |
| 17 | Orders | Order book from confirmation to installation: stages that follow production, dispatch and sign-off; advance, stage and final payments with balance; delivery and installation schedule; dispatch notes (PDF, DN-00001…); installation sign-off with certificate (PDF) (see orders.md) |
| — | Along the way | Product renamed to MARK; colour-coded bill of materials; rail and drop-down fixes |

---

## 12 — Accounts, sign-in, licensing and feature packages — done

Built as planned; see [licensing.md](licensing.md). Decisions taken: the owner side is a separate desktop program
(MARK Owner); key validity counts from generation; read-only when expired or suspended; 7-day offline grace; the trial
is the "Trial" company type (14 days, both products, Complete); locked features are shown locked. Restricting the
library by product (uPVC / Aluminium) comes with the product systems of Milestone 13.

**Goal:** the owner controls who may use MARK, with which products and features, for how long.

**Part A — Accounts and sign-in**
- A small **licence server** (web service + database) that the owner runs, holding companies, users, products,
  packages, validity and computers. Passwords stored only as salted hashes.
- **Sign-in page** in MARK: User ID and password; lock-out after repeated failures.
- Roles: **Admin** (the owner) and **Account owner** (a client company).
- **MARK Owner** — the owner side **(default — confirm: a separate desktop program on the owner's PC)**:
  - all companies at a glance: name, logo, products, package, users/computers used, validity, status, last check-in;
  - **Company types** made by the admin (e.g. uPVC fabricator, Aluminium fabricator, uPVC + Aluminium, Trial), each
    with its products, package and validity as a starting point;
  - **New account**: company name and logo (only the admin can change them), **company type**, User ID and password
    set by the admin, products **uPVC / Aluminium** (each licensed separately, with its own validity), validity
    (1 month / 3 months / 6 months / 1 year / exact date), number of computers;
  - suspend / reactivate an account or one product, extend validity, reset a password, change the account type,
    free a computer.
- **Keys**: the admin can also generate keys per product with a validity period, extend or revoke them
  **(default — confirm: validity counts from when the key is generated)**.

**Part B — Licensing in MARK and feature packages**
- A **signed licence file** (company, logo, products, features, validity, computers) that MARK verifies offline; it
  cannot be edited without breaking the signature. Clock-rollback protection.
- **Daily check-in** when online; **offline grace period (default — confirm: 7 days)**.
- When expired or suspended: **(default — confirm: read-only — open and export old quotes, no changes)**.
- **Free trial**: a "Trial" company type (default — confirm: 14 days, both products, everything included).
- The client's **company name and logo** shown in MARK's header (and later on quotations).
- **Feature catalogue** of everything in MARK, grouped by area (products, sales, design, pricing, library,
  production, orders, purchasing, inventory, accounts, limits on users and computers).
- **Packages** created by the admin by ticking features (starter set **Basic / Professional / Complete**, editable);
  per account: one package plus add-ons or removals, **each with its own validity (default — confirm)**.
- Features not included are **shown locked with an upgrade message (default — confirm)**.

**Done when:** the admin can create an account with sign-in, type, package and validity; the client signs in and
sees only what was sold; suspending, expiring or changing a package takes effect at the next check-in or after the
grace period; a tampered licence file is rejected.

---

## 13 — Product catalogue, systems and bundles — done

Built as planned; see [catalogue.md](catalogue.md). Decisions taken: companies enter their own prices; untick-to-hide is
by system plus single items; items no longer given are retired (saved quotes keep working); hardware sets and listed
reinforcement replace the pricing rates for those windows. Still to come: door frames and inward/outward as design
choices, couplers joining frames in a design.

**Goal:** the admin decides what every company can use, and the designer uses the right parts automatically.

- **Master catalogue on the admin side only** (MARK Owner): profiles, glass, hardware/equipment, reinforcement and
  accessories. Only the admin adds, changes or removes items.
- **"Used with" for every item**: material (uPVC / Aluminium), system (e.g. "62mm Casement – uPVC", "Series 60
  Sliding – Aluminium"), window or door, position (outer frame, sash, mullion incl. Z mullion, transom, coupler,
  glazing bead, interlock, track, mesh sash…), inward/outward, opening types, glass thickness range.
- **Bundles**: parts that always go together in one position, e.g. sliding 2-track frame = frame profile + track
  rail + bottom cover + gasket; sliding sash = sash profile + interlock + brush seal + rollers; hardware sets.
  Each part has a quantity rule: per length (e.g. gasket 2 × length), per piece, by size (e.g. 2 hinges up to
  1200 mm, 3 above), and a cut deduction for profiles.
- **Reinforcement (RI)** per profile: which steel bar, when it is needed (always / above a length), cut deduction.
- **Per company**: the admin ticks which systems, bundles and items each company gets; each company type brings a
  starting set. Changes reach the company at its next check-in.
- In MARK the library becomes **read-only** for client companies: they see and use only what they were given and
  **(default — confirm) enter only their own purchase prices**.
- Designs choose a system (and window or door); the whole bundle goes into the bill of materials, cutting list and
  price; only compatible glass is offered. Old quotes keep their copy when an item is removed.
- Existing libraries and designs are converted to a default system per material.

**Done when:** a design in a 62mm uPVC casement system lists the right frame, sash, mullion, coupler and
reinforcement with correct cut lengths and prices; a company sees only what the admin gave it; incompatible items
cannot be chosen.

---

## 14 — Separate areas and permissions — done

Built as planned; see [areas.md](areas.md). Decisions taken: the account owner adds staff in MARK (Account › Staff
logins) up to the account's **Users** (set in MARK Owner; the owner counts as one, turned-off logins do not); staff see
only the areas they were given (the account owner sees locked areas); logins without anything to edit with can view
quotes only, and without sales or costing see no prices; Bill of materials and Cutting plan became pages of Pricing and
Production; two logins on one PC count it once. The quote history is per computer until Orders brings a shared
database.

**Goal:** MARK is organised into areas, and each user sees only what they may use.

- New main navigation with areas: **Sales, Design, Pricing, Library (Engineering), Production, Orders, Purchasing,
  Inventory, Accounts**; existing screens moved into their areas.
- Areas and features follow the account's licence (hidden or locked).
- **Staff logins (default — confirm: the account owner may add staff up to the licensed number of users)** with a
  subset of the company's features per person (e.g. a cutter sees only Production).
- Who did what: quotes and changes record the user.

**Done when:** each area opens on its own; a user without access to an area cannot reach it; staff permissions work.

---

## 15 — Sales — done

Built as planned; see [sales.md](sales.md). Decisions taken: the quotation layout follows the usual Indian fabricator
quotation (letter, two designs a page with drawing and computed values, quote total, terms with bank details and both
signatures); everything printed comes from the company's Quotation setup and the MARK account; areas in square feet by
default; an order is a number on the won quote until Milestone 17; enquiries and quotation PDF are in the Professional
package for new servers (existing servers: add them to the packages in MARK Owner).

- **Enquiry form** in two steps (client and site; stage, source, owner, expected value, dates).
- Enquiry → quote → **order** conversion; quote revisions.
- **Quotation PDF** with the company logo, drawings, specifications, prices and terms.
- **Dashboard charts** by week / month: created, quoted, won, lost, value; sales by person and by city.

---

## 16 — Production and engineering — done

Built as planned; see [production.md](production.md). Decisions taken: one production order per order, keeping the
designs as they were when production started; progress counted per window for designs made more than once; papers as
PDFs (labels 3 × 7 on A4, 70 × 40 mm); offcuts in stock are a simple list in this milestone (full stock with Milestone
18), used first by the cutting list and updated once per order after cutting; Production orders is in the Complete
package (others: as an add-on).

- **Production order** per confirmed order.
- Cutting lists for profiles **and reinforcement steel**; optimisation using stock bars **and offcuts in stock**.
- Glass order list (sizes, types, quantities) for the glass supplier; hardware pick list.
- Shop drawings per window; piece labels.
- Progress per window: cut, assembled, glazed, ready, dispatched.

---

## 17 — Orders — done

Built as planned; see [orders.md](orders.md). Decisions taken: one order record per quote converted to an order (the
designs and the price stay with the quote; the value is the quote's total); six stages (Confirmed, In production, Ready,
Dispatched, Installed, Closed) that move forward on their own with production, dispatch and sign-off and can be set by
hand; dispatch notes numbered DN-00001… across all orders; dispatch note and installation certificate as PDFs; a
Schedule tab for every order's deliveries, installations and site visits.

- Order status from confirmation to installation; advance and stage payments recorded.
- Delivery and installation schedule; dispatch notes; installation sign-off.

---

## 18 — Purchasing and inventory

- Suppliers; purchase orders from what orders need minus stock; goods received.
- Stock of bars, glass and hardware; **offcuts kept and reused by the cutting plan (default — confirm)**;
  stock reserved for orders; low-stock alerts.

---

## 19 — Accounts

- GST invoices from orders; payments and receipts; outstanding amounts by client.
- **Export to Tally / Excel (default — confirm: no full bookkeeping inside MARK)**.

---

## 20 — Finishing and installer

- Installer for customers, update checks, backup and restore.
- Final look-and-feel, keyboard and speed pass.

---

## Possible later
- Online payment of renewals (e.g. Razorpay / Stripe) that extends a licence automatically.
- A cloud version with data on the owner's server, shared across a company's computers.

## Order of work
12 → 13 → 14 → 15 → 16 → 17 → 18 → 19 → 20. Each later area milestone registers its features in the package catalogue
as it is built.
