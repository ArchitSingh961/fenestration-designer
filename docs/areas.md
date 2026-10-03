# Areas, staff logins and who did what (Milestone 14)

MARK is organised into **areas**. Each person signs in with their own login and sees only the areas and features they
may use. Every saved quote records who created it, who saved it last and what changed.

## Areas

The **area bar** on the left shows the areas; the header shows the **tabs** of the chosen area. An area opens on the tab
last used there.

| Area | Tabs | Needs |
|---|---|---|
| **Sales** | Dashboard · Quotes · Client · Designs | Quotes and clients |
| **Design** | Designs · Drawing | Frame designer |
| **Pricing** | Price · Bill of materials | Price structure · Bill of materials and cost |
| **Library** | Library (systems, counts, **Open Library Manager**) | Library Manager (or a catalogue: own prices) |
| **Production** | Cutting plan | Cutting plans |
| **Orders · Purchasing · Inventory · Accounts** | Overview of what is coming | (later milestones) |

- The open quote is shown above the quote tabs (Client, Designs, Drawing, Price, Bill of materials, Cutting plan) with
  who created and last saved it, and **Open quote…**, so someone who works only in Design, Pricing or Production can
  pick a quote without the Sales area.
- **Designs** is in Sales and in Design; it stays in the area it was opened from.
- The **Account** page (company name in the header) and the **Staff logins** page have no area tabs.

### What each login sees

- **Account owner** (the login the MARK supplier sets up): every area. Features outside the package are shown
  **locked** (lock on the area or tab, and a notice saying what is missing); areas still to come are dimmed.
- **Staff login**: only the areas and tabs with something the account owner gave it. Everything else is **not shown**,
  and cannot be reached: a page it may not see (from a menu, a shortcut or code) sends it to its first area.
- A staff login with none of *Quotes and clients*, *Frame designer* or *Price structure* can open and view quotes but
  not save, delete or change them. Without *Quotes and clients*, *Bill of materials and cost* or *Price structure* it
  does not see prices (header total, cutting plan costs).
- The Edit, View, Design and Tools menus are shown only with the frame designer; **Go** lists the login's areas.

## Staff logins

The account owner manages them in MARK: **Account → Staff logins** (or File → Staff Logins).

- **Add staff login**: name, **User ID**, **password**, *Turned off*, and **what this person may use**: the account's
  features grouped by area, with quick choices **Sales**, **Design**, **Pricing**, **Production** and **Everything**.
  Only the account's own features are offered.
- **Change** a login (click it): new features, a new password (also unlocks it), turn it off or on, or **Remove login**.
- The number of logins is limited by **Users** in the account (set by the MARK supplier in MARK Owner): the account
  owner counts as one, turned-off logins do not count. "2 of 3 logins in use (you and 1 staff)".
- Changes reach the staff member's MARK at its next check-in (every 6 hours, or **Check now**), or when they sign in.
  A login that is turned off or removed is signed out at its next check-in.
- Staff sign in on the normal sign-in page. Two logins on the same computer each keep their own sign-in; the computer
  counts once towards **Computers**.
- Only the account owner can manage staff, and not while the account is suspended or has ended.

### MARK Owner

The account editor has **Users** next to **Computers** (a new account: 3; existing accounts got as many as their
computers) and a **Staff logins** list with **Remove**. *Users* cannot be set below the logins in use.

## Who did what

- Every save records the signed-in person (without a licence: the Windows user) with the quote: **created by** and
  **last saved by** (quote list column *Saved by*; the quote header).
- Each quote has a **history** (Client tab): created, saved — with what changed, e.g. *Status Active → Won ·
  Value 1,20,000.00 → 1,35,000.00 INR · 3 → 4 designs* — and deleted. The history is kept after a quote is deleted.
- The dashboard shows **Recent activity** across all quotes.

The history is stored in the computer's database (`project_history`); quotes on other computers are not combined (a
shared company database comes with Orders).

## How it is enforced

- The licence (signed) carries the login's **role** (owner or staff) and, for staff, its **permissions**; MARK uses only
  the account's features that the login was given (`LicenceEvaluator.ForLogin`). A staff member cannot give themselves
  more: the licence is signed by the server.
- The server keeps staff in the `users` table (`role = 'staff'`, `permissions`, `disabled`, `last_sign_in_utc`), checks
  the users limit, and accepts staff changes only from a computer signed in as the account owner.

## Code

| Where | What |
|---|---|
| `Mark.Licensing` | `Licence.Role` / `Permissions` / `MaxUsers`, `UserRoles`, `LicenceStatus.CompanyFeatures` / `IsWithheld`, staff contracts (`StaffInfo`, `StaffList`, `StaffEdit`), `LicenceManager.StaffAsync` / `SaveStaffAsync` / `DeleteStaffAsync` |
| `Mark.LicenceServer` | Schema 3 (users limit, staff columns, computers per login), `LicenceService.Staff` (client staff calls, admin `RemoveStaff`) |
| `Mark.Data` | Schema 5: `projects.created_by` / `modified_by`, `project_history`; `IProjectRepository.User`, `History`, `RecentHistory` |
| `Mark.Designer` | `Areas.cs` (`AppArea`, `AppView`, `AreaCatalog`), `MainViewModel.Areas` (navigation and enforcement), `StaffViewModel`, `MainViewModel.History`, `AccessViewModel` (`IsStaff`, `IsWithheld`, `CanEditQuotes`, `CanSeeQuoteValues`) |
| `Mark.App` | Area bar and tabs, Library / Overview / Staff / Bill of materials / Cutting plan pages, quote history |
| `Mark.Owner` | Users in the account editor, staff list with Remove |
