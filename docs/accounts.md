# Accounts (Milestone 19)

The Accounts area (feature **Invoices and payments**, `accounts.invoices`) makes GST tax invoices and payment receipts,
shows what each client owes, and exports to Excel and Tally. MARK keeps no books itself: the books stay in Tally.
Invoices are in the local database (schema 12, `invoices`); the setup is in `app_settings` (`accounts`).

## Invoices

- **Invoice an order** › **Make invoice**: a draft with every design not invoiced yet (at the order's unit price after
  discount, before tax; HSN by the system's material — aluminium 7610, uPVC 3925 by default) and the charges (transport,
  loading…) once. Quantity, rate and HSN can be changed; 0 leaves a line for a later invoice. A part invoice leaves the
  rest for the next one.
- **Tax**: within the company's state (the place of supply is its state) **CGST + SGST**, half each; otherwise **IGST**.
  The company's state comes from its GSTIN (Quotation setup), else from Export and setup; the place of supply from the
  client's GSTIN, else the client's state. The total is rounded to the rupee (round off shown).
- If today's prices differ from the value the order was confirmed at, MARK says so; **Use the order's value** scales the
  windows' rates so the invoice comes to the agreed value (charges stay as they are).
- **Create invoice** gives the next number — `INV/2026-27/0001` by financial year (April–March), or `INV-00001` — and
  writes the PDF: company, invoice number and date, bill to (with GSTIN, or "unregistered (consumer)"), place of supply,
  reverse charge, site, lines with HSN/SAC, taxable value, CGST/SGST or IGST, round off, total, amount in words, bank
  details, declaration and signatures.
- **Cancel invoice** (with a reason): the number stays used; what it had can be invoiced again.

The client's **GSTIN** is on the Client tab (checked: state code, PAN, entity number, Z and the check character). With
it the invoice is B2B, without it B2C.

## Receipts

Every payment of every order (payments are recorded on the order, Orders › Orders). **Receipt PDF…** gives the payment
the next receipt number (`RCPT-00001`…) the first time and writes the receipt: received from, amount and words,
payment, mode, reference, against which order, order value, received so far and balance.

## Outstanding

By client: orders, order value, invoiced, received, **due on invoices** (invoiced − received), **to receive on orders**
(value − received), and the oldest invoice not yet covered by payments (payments go to the oldest invoices first) with
its age in days.

## Export and setup

For a period (from the start of the financial year to today by default):

| Export | What it is |
|---|---|
| Invoices (Excel) | invoice register: number, date, client, GSTIN, place of supply, B2B/B2C, order, taxable, CGST, SGST, IGST, round off, total, status |
| HSN summary (Excel) | taxable value and tax by HSN/SAC and rate, for the GST return |
| Receipts (Excel) | date, receipt number, client, order, amount, mode, reference |
| Tally (XML) | client ledgers (under Sundry Debtors, with GSTIN and state), a Sales voucher per invoice and a Receipt voucher per payment; import in Tally with Gateway › Import › Transactions |

CSV files are UTF-8 for Excel; text that Excel would read as a formula is quoted. **Setup**: invoice prefix and
numbering by financial year, HSN codes (aluminium, uPVC, charges), the company's state, and the Tally ledger names
(sales, output CGST/SGST/IGST, round off, cash, bank, debtors group, Tally company) — they must match the ledgers in Tally.

Staff logins: the **Accounts** quick choice gives Invoices and payments, and Order management.
