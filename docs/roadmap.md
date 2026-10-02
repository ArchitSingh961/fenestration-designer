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
| — | Along the way | Product renamed to MARK; colour-coded bill of materials; rail and drop-down fixes |

---

## 12 — Accounts, sign-in, licensing and feature packages

**Goal:** the owner controls who may use MARK, with which products and features, for how long.

**Part A — Accounts and sign-in**
- A small **licence server** (web service + database) that the owner runs, holding companies, users, products,
  packages, validity and computers. Passwords stored only as salted hashes.
- **Sign-in page** in MARK: User ID and password; lock-out after repeated failures.
- Roles: **Admin** (the owner) and **Account owner** (a client company).
- **MARK Owner** — the owner side **(default — confirm: a separate desktop program on the owner's PC)**:
  - all companies at a glance: name, logo, products, package, users/computers used, validity, status, last check-in;
  - **New account**: company name and logo (only the admin can change them), User ID and password set by the admin,
    account type **Aluminium / uPVC / Aluminium + uPVC** (each product licensed separately), validity
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
- **Free trial (default — confirm: 14 days, both products)**.
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

## 13 — Separate areas and permissions

**Goal:** MARK is organised into areas, and each user sees only what they may use.

- New main navigation with areas: **Sales, Design, Pricing, Library (Engineering), Production, Orders, Purchasing,
  Inventory, Accounts**; existing screens moved into their areas.
- Areas and features follow the account's licence (hidden or locked).
- **Staff logins (default — confirm: the account owner may add staff up to the licensed number of users)** with a
  subset of the company's features per person (e.g. a cutter sees only Production).
- Who did what: quotes and changes record the user.

**Done when:** each area opens on its own; a user without access to an area cannot reach it; staff permissions work.

---

## 14 — Product systems and "used with" rules

**Goal:** the library describes real product systems, and the designer uses the right parts automatically.

- **Systems** (e.g. "62mm Casement – uPVC", "Series 60 Sliding – Aluminium") with material uPVC or Aluminium
  (replaces a separate uPVC/Aluminium split).
- **Parts by role**, with window/door and inward/outward variants: outer frame, sash, mullion (incl. Z mullion),
  transom, coupler, glazing bead, interlock, track, mesh sash…
- **Reinforcement (RI)** per profile: which steel bar, when it is needed (always / above a length) and its cut
  deduction.
- **Hardware sets** per system and opening type with size rules (e.g. 2 hinges up to 1200 mm, 3 above).
- **Glass compatibility**: thickness range per system; glazing bead chosen by glass thickness.
- **Add your own** glass, profiles, reinforcement, couplers and hardware, and tick the systems, roles and opening
  types each may be used with.
- Designs choose a system (and window or door); parts, reinforcement, bead and hardware are picked automatically;
  only compatible glass is offered. Reinforcement appears in the bill of materials, cutting list and pricing.
- Existing libraries and designs are converted to a default system per material.

**Done when:** a design in a 62mm uPVC casement system lists the right frame, sash, mullion, coupler and
reinforcement with correct cut lengths and prices, and incompatible items cannot be chosen.

---

## 15 — Sales

- **Enquiry form** in two steps (client and site; stage, source, owner, expected value, dates).
- Enquiry → quote → **order** conversion; quote revisions.
- **Quotation PDF** with the company logo, drawings, specifications, prices and terms.
- **Dashboard charts** by week / month: created, quoted, won, lost, value; sales by person and by city.

---

## 16 — Production and engineering

- **Production order** per confirmed order.
- Cutting lists for profiles **and reinforcement steel**; optimisation using stock bars **and offcuts in stock**.
- Glass order list (sizes, types, quantities) for the glass supplier; hardware pick list.
- Shop drawings per window; piece labels.
- Progress per window: cut, assembled, glazed, ready, dispatched.

---

## 17 — Orders

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
12 → 13 → 14 → 15 → 16 → 17 → 18 → 19 → 20 **(default — confirm)**. Milestone 14 can move before 12 if product
systems are needed sooner; each later area milestone registers its features in the package catalogue as it is built.
