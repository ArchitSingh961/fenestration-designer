# Sales (Milestone 15)

The Sales area: **Dashboard · Enquiries · Quotes · Client · Designs · Quotation setup**.

## Enquiries

**Sales › Enquiries** lists every enquiry: number (EN-00001…), client, city, phone, stage, source, salesperson, expected
value, follow-up and the quote made from it. Tabs **Open · Quoted · Won · Lost · All**, and a search over number, client,
city, phone, source, salesperson and quote. Follow-ups due today or earlier are highlighted and counted.

**New enquiry** opens the form in two steps:

1. **Client and site** — title, name, company, phone (or e-mail), site address.
2. **Enquiry** — stage (New, Contacted, Site visit, Quoted, Won, Lost), source (walk-in, phone, website, referral,
   architect, builder, dealer, exhibition, social media, or any other), salesperson, expected value, follow-up date,
   expected decision, requirements; why it was lost.

**Create quote** saves the enquiry and opens a new quote with its client, site and requirements, linked to it; the
enquiry becomes *Quoted*. When the quote is saved won or lost, the enquiry follows. Enquiries need the *Enquiries*
feature.

## Enquiry → quote → order, and revisions

On an open, saved quote (header of its pages):

- **Convert to order** marks it won and gives it the next order number (OR-00001…), shown as a badge, in the quote list
  (*Order* column) and in its history. Orders are managed in Milestone 17.
- **New revision** keeps the saved quote as it is (R0) and continues as the next revision: the quote number becomes
  *QT-00012 R1*. The Client tab lists the earlier revisions (when, by whom, value) with **Open a copy**, which opens one
  as a new, unsaved quote.

## Quotation PDF

**Quotation PDF…** (with the *Quotation PDF* feature) writes the saved quote as an A4 PDF and opens it. The layout:

- on every page: the brand at the top left; at the top right the company logo, the partner line, name, address, contact,
  e-mail, website and GSTIN; a rule and "Quote No. / Project / Date"; in the footer the brand, "page of pages" and
  "powered by MARK";
- a covering letter to the client (name and address), with the enclosures and "Authorized Signatory";
- each design: code, size (W × H), name, profile system, location, glass by pane ("(1,2) 5mm Frosted Toughened"); its
  drawing with dimensions, "View From Inside"; computed values (area per window, value per area, unit price, quantity,
  value); profiles (outer, sash, mullion, reinforcement…, mesh) and accessories (hardware); remarks — about two per page;
- the quote total: components, total area, basic value, discount, sub-total, charges, total project cost, tax (e.g.
  GST 18 %), grand total, average price per area without and with tax; notes;
- terms and conditions (numbered), then — when filled in — **Cancellation Policy**, **Warranty** and **Pre-requisites
  for Installation**, each numbered; bank details, the acceptance sentence and both signatures (authorized signatory,
  customer). Lines the company numbered or lettered itself ("1. Payment terms:", "a. 100% advance …") are printed as
  written (its own headings such as "Bank Details :" in bold); a letter that starts with its own "Dear …" or lists its
  own enclosures does not get MARK's as well;
- optionally a last page with one picture (e.g. care instructions).

**What it prints comes from two places:**

- **The admin (MARK Owner)**, in each company's account under *Quotation details*: the line above the company name
  (e.g. "Authorised partner"), address, contact, e-mail, website, GSTIN; the brand's name and logo; the bank details;
  and an optional last page with one picture. Only the admin can set them. They reach MARK at its next check-in (their
  fingerprint is in the signed licence, like the catalogue's); Quotation setup does not show them at all, only the PDF
  prints them. The company name and logo are the account's, as before.
- **The company (Sales › Quotation setup)**: the covering letter (one paragraph per line), terms (one per line), the
  cancellation policy, warranty and pre-requisites for installation (one point per line; left out when empty, **Use
  MARK's text** on each fills in a standard text to change), the acceptance sentence, notes under the total, square feet
  or square metres, and the money label. **Use MARK's texts** restores the standard letter, terms and acceptance. The
  setup is saved with **Save setup**, and also when leaving the page, before every quotation PDF and when MARK closes.

Without a MARK account (no sign-in) the company fills in everything itself.

## Sales charts

On the dashboard (with the *Sales charts* feature), **by week** (the last 12) or **by month** (the last 12):

- enquiries, quotes created, won and lost per period (grouped bars; colours from the validated default order with a
  legend), and the value won per period; **Show the numbers** gives the same figures as a table;
- **sales by person** (who created the quote: quotes, won, win rate, value won) and **by city** (the client's city).

Won and lost count on the day the quote was decided (stored when its status changes).

## Code

| Where | What |
|---|---|
| `Mark.Core/Quotes` | `Enquiry`, `EnquiryStage`; `QuotationSettings`; `QuoteInfo.Revision` / `OrderNumber` / `OrderedUtc` / `EnquiryId` |
| `Mark.Data` | Schema 7: `enquiries`, `project_revisions`, projects' `client_city`, `decided_utc`, `order_number`, `revision`; `SqliteEnquiryRepository`; `KeepRevision` / `Revisions` / `LoadRevision`; quotation settings |
| `Mark.Reports` | `QuotationDocument` and `QuotationPdf` (PDFsharp / MigraDoc, MIT) — layout only, no calculation |
| `Mark.Designer` | `QuotationBuilder` (quote → document, drawings rendered like the canvas), `EnquiriesViewModel`, `QuotationSetupViewModel`, `SalesChartsViewModel`, `MainViewModel.Sales` |
