# Purchasing and inventory (Milestone 18)

Two areas, with the features **Purchasing** (`purchasing.orders`) and **Inventory** (`inventory.stock`), in the Complete
package or as add-ons. Everything is in the local database (schema 11: `suppliers`, `purchase_orders`, `stock_levels`,
`stock_moves`).

## What is stocked, in what unit

| Kind | Unit | What a job needs (`StockNeeds`) |
|---|---|---|
| Profiles and steel | bars | the new bars the cutting plan cuts the job from (every stock length counted as a bar) |
| Glass | m² | the glass area of every pane |
| Hardware, gaskets, accessories | pcs, m or m² (the item's unit) | the bill of materials |

Offcuts are kept separately (Production › Offcuts) and are used first by the cutting list (Milestone 16).

## Inventory › Stock

For every item that has stock, is needed or is on order: **on hand**, **for orders** (reserved: what the production
orders whose stock has not been issued yet need), **free** (on hand − for orders), **on order** (placed purchase orders
not yet received), **reorder at**, **to buy** and the orders it is for. Filters: all, low stock, needed by orders,
profiles, glass, hardware and accessories; search.

- An item is **low** when free stock is below zero or at/under its reorder level; the page says how many are low.
- **To buy** = what orders need beyond stock, or what brings free stock back up to the reorder level, less what is on
  order.
- **Keep stock of**: start keeping stock of any library item. For the selected item: **Count found** (on hand becomes
  the count), **Add / take out** (e.g. 10 or −2) with a note, **Reorder level** and **Kept at**.
- **Issue to production**: each production order whose stock is still reserved; **Issue** (asked first) takes what it
  needs out of stock — do it when the material goes to the workshop. If there was not enough, the stock goes below zero
  and MARK says which items to count.
- **Stock moves**: the ledger, newest first (of the selected item when one is chosen): when, item, quantity, why
  (received, issued, adjusted), reference (GRN, order number), by whom, note. On hand only changes through a move, so
  the ledger always adds up.

## Purchasing › Purchase orders

- The page says what has to be bought. Choose a **supplier** and **Order what is short**: a draft purchase order
  (PO-00001…) with everything that supplier supplies that is short, at library prices (a bar = cost per metre × the
  longest stock length), for the orders that need it. Or start an **Empty purchase order** and add items.
- A **draft** can be changed (quantities, rates, items, wanted-by date, note). **Place order** fixes it: its items are
  on order from then on. **Purchase order PDF…** writes it for the supplier. **Cancel order** (asked first).
- **Goods received**: enter what came in on each line (it starts with what is still due) and the supplier's invoice or
  challan, then **Receive goods**: a goods receipt (GRN-00001…) is added and the goods go into stock, together.
  The order becomes *part received* or *received*.

## Purchasing › Suppliers

Name, contact person, phone, e-mail, address, GSTIN, what it supplies (profiles, glass, hardware and accessories —
used for "Order what is short"), note, and whether it is active (offered for new purchase orders).

Staff logins: the **Stores** quick choice gives Inventory, Purchasing and Production orders.
