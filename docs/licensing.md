# Accounts, sign-in and licensing (Milestone 12)

MARK is sold to companies as **licensed accounts**. The owner of MARK (the **admin**) creates each company's account in
**MARK Owner**, with its sign-in, company type, products, package and validity, and can change, suspend or remove it at
any time. MARK on the company's computers signs in with the User ID and password the admin set, and follows the
admin's changes at its next check-in.

```text
MARK Owner (admin, Windows)  ──HTTP──►  MARK Licence Server  ◄──HTTP──  MARK (client companies, Windows)
  companies, packages,                   SQLite database,                 sign-in, signed licence,
  company types, keys                    signing key                      check-ins, licence keys
```

## The three programs

| Program | File | Who uses it |
|---|---|---|
| **MARK Licence Server** | `MARK.LicenceServer.exe` | Runs on the admin's computer or a server. Holds all accounts and signs licences. |
| **MARK Owner** | `MARK.Owner.exe` | The admin only: companies, licence keys, packages, company types. |
| **MARK** | `MARK.exe` | Client companies (and the admin, with an account of their own). |

## First start (on the admin's computer)

1. **Start the licence server**: run `MARK.LicenceServer.exe` and keep its window open. It listens on
   `http://localhost:5180` and keeps its data in `%LOCALAPPDATA%\MARK Licence Server` (`licences.db` and
   `signing-key.pem`).
2. **Start MARK Owner**. On a new server it offers **Set up the admin account**: your name, admin User ID and password.
   This is only possible on the server's own computer, and only once.
3. In MARK Owner, **+ New account** for your own company (and later for each client).
4. **Start MARK** and sign in with that account's User ID and password.

**Keep a backup of `signing-key.pem`.** Every licence is signed with it, and MARK accepts only licences signed with
this key (its public half is built into MARK, `LicenceKeys.PublicKey`). It is never stored in the repository.

## MARK Owner

- **Companies**: every company with its type, products, package, validity, computers in use and status (Active, Ends
  in N days, Suspended, Expired). **+ New account** / select a company to open the account editor:
  - company name and logo (only the admin can change them), **company type**;
  - **sign-in for MARK**: account owner's name, **User ID** and **password** (set by the admin; a new password can be
    set at any time and also unlocks the User ID);
  - **products**: uPVC and Aluminium, each sold separately with its own *valid until* date and its own *Suspended*
    switch; **+14 days … +2 years** extend from today or from the current end, whichever is later;
  - **package**, **account valid until** and the number of **computers**;
  - **features**: the package's features are ticked; untick one to leave it out for this company, tick another to give
    it as an **add-on** with its own end date. Quotes and the frame designer are always included;
  - **computers signed in**, each with **Free** (signs that computer out so another can be used);
  - **Suspend / Reactivate** (MARK becomes read-only), **Delete** (the User ID stops working, computers are signed out,
    unused keys are cancelled), notes only the admin sees.
- **Licence keys**: generate keys for one company or any company that give **account validity**, **uPVC**,
  **Aluminium** or **a feature**, valid for 14 days … 2 years **counted from the day the key is generated**. Keys look
  like `MARK-7KQ2M-X9TPA-3HRWD-ZC4NE`, are used once, never shorten anything, and can be cancelled while unused.
- **Packages**: named sets of features (starts with Basic, Professional, Complete). Changes reach the companies of the
  package at their next check-in. A package in use cannot be deleted.
- **Company types**: uPVC fabricator, Aluminium fabricator, uPVC + Aluminium fabricator, Trial (14 days, both
  products, Complete). A type gives a new account its products, package and validity.

## MARK

- **Sign-in page** before anything opens: User ID, password, *Keep me signed in on this computer*, and the licence
  server address under **Connection**. After 5 wrong passwords the User ID is locked for 15 minutes.
- The **company name and logo** are in the header; they open the **Account** page: company, signed-in user, package,
  validity, computers, products, every feature (included, until a date, not in the package, or coming later), a field
  for **licence keys**, **Check now** and **Sign out** (frees the computer).
- **Check-ins**: at start and every 6 hours MARK fetches a fresh licence. Offline, MARK keeps working for **7 days**
  after the last check-in, with a warning after 2 days.
- **Features not in the package are shown locked** with "Not in your package" (Pricing tab, bill of materials, cutting
  plan, design library) or refused with that message (openings, Library Manager, project files).
- **Read-only** when the account is suspended or expired, no product is valid, MARK has been offline longer than 7
  days, or the computer's clock was moved back: quotes can be opened, viewed and exported, but not saved, deleted or
  used as default; the Library Manager is closed. A red bar says why.
- When the admin **frees the computer** or **deletes the account**, MARK becomes read-only at the next check-in and
  asks for sign-in at the next start.

## How it is protected

- The server issues a **licence** per computer: company, logo, products, features with their end dates, validity,
  suspension, the computer's id and the time of issue. It is **signed with ECDSA P-256**; MARK verifies it offline with
  the public key and rejects any change (`LicenceVerifier`).
- The licence is for one computer: a hash of Windows' machine GUID. A copied licence does not work elsewhere.
- The offline grace counts from the signed issue time, so it cannot be extended by editing anything; a clock moved back
  (before the issue time, or before the latest time MARK saw) makes MARK read-only until it checks in.
- On the computer the licence, the device token and a password hash (for signing in offline) are kept in
  `%LOCALAPPDATA%\MARK\licence.dat`, encrypted for the Windows user (DPAPI).
- The server stores **only salted PBKDF2 hashes** of passwords and SHA-256 hashes of device and session tokens.
- Admin calls need an admin session (12 hours). The first admin can only be created on the server's own computer.

## Running the server for client companies

For companies on other computers the server must be reachable from the internet, for example on a small cloud server:

1. Copy the `MARK Licence Server` folder and the data folder (`licences.db`, **`signing-key.pem`**) to the server.
2. Start it with its public address, preferably behind HTTPS (a reverse proxy such as Caddy or IIS, or Kestrel with a
   certificate): `MARK.LicenceServer.exe --Urls http://0.0.0.0:5180 --DataFolder D:\MARK\data`.
3. In MARK and MARK Owner enter that address under **Connection** / **Licence server**.

Options (command line or `appsettings.json` next to the server): `--DataFolder`, `--SigningKeyPath`, `--Urls`.

## Code

| Where | What |
|---|---|
| `Mark.Licensing` (net8.0, no WPF, no Mark references) | `Licence`, `LicenceSigner` / `LicenceVerifier`, `LicenceEvaluator` (full / read-only, grace, clock), `FeatureCatalog` + `Features` + `StarterPackages`, `PasswordHasher`, `Secrets`, API contracts, `LicenceApiClient`, `OwnerApiClient`, `LicenceManager` (MARK's sign-in, check-in, keys, sign-out) |
| `Mark.LicenceServer` (ASP.NET Core) | `LicenceDatabase` (SQLite schema and starter data), `LicenceService` (admin, companies, packages, types, keys, client calls), `LicenceServerApp` (endpoints, errors) |
| `Mark.Owner` (WPF) | sign-in / first setup, Companies + account editor, Licence keys, Packages, Company types |
| `Mark.Designer` | `AccessViewModel` (feature gates, read-only), `SignInViewModel`, `AccountViewModel`, `MainViewModel.Licence` |
| `Mark.App` | sign-in before the main window, `MachineIdentity`, `DpapiProtector`, check-in timers, sign-out |

Adding a feature later: add its id to `Features` and `FeatureCatalog.All` (with `IsBuilt: false` until it ships), and
gate it in MARK with `Access.Allows(...)`.

## Not yet (later milestones)

- Product licences (uPVC / Aluminium) restrict the library from Milestone 13, when every item belongs to a system
  with its material; in Milestone 12 a company needs at least one valid product to work.
- Staff logins and per-person permissions: Milestone 14.
