# Orders (Milestone 17)

The Orders area: **Orders · Schedule**. Both need the *Order management* feature (in the Complete package, or as an
add-on in the company's account).

## Orders

Every quote converted to an order (Sales or Design › the quote's header › **Convert to order**, OR-00001…) is in the
order book. **Order** on an order's header opens its page.

The list on the left shows each order's number, name, client, value, stage and how much is paid, with a search box and
**Open orders · All orders · With a balance · Closed**.

The chosen order shows:

| Part | What it holds |
|---|---|
| **Header** | Order and quote numbers, client and phone, the site (the client's address on the quote), production progress, and **Open quote** / **Production**. On the right the order's **value** (the quote's total), **received** and **balance**. |
| **Stage** | Confirmed → In production → Ready → Dispatched → Installed → Closed, with the day each was reached. Click a stage to set it by hand (closing an order with a balance asks first). |
| **Payments** | Advance, stage, final or other payments: amount, day, how paid (cash, bank transfer, UPI, cheque, card) and a reference. Removing one asks first. |
| **Delivery and installation** | Deliveries, installations and site visits: day, time, team and a note; tick when done. Overdue visits are marked. |
| **Dispatch** | Each design with how many have gone; enter how many go now (what is left, by default), the day, vehicle, driver and a note. **Make dispatch note (PDF)…** saves it (DN-00001…, numbered across all orders) and makes the note to print and sign on delivery. Earlier notes can be printed again or removed. |
| **Installation sign-off** | The day, who signed for the client, who installed, and remarks (snags). **Installation certificate (PDF)…** makes the certificate for the client to sign. |
| **Notes** | Free notes on the order. |

### The stage moves on by itself

- **In production** when the order's production order is started (Production › Production orders).
- **Ready** when every window is marked Ready (or further) in production.
- **Dispatched** when a dispatch note sends the last windows.
- **Installed** when the sign-off is recorded.

It only ever moves forward on its own; setting it by hand can also move it back.

## Schedule

Every delivery, installation and site visit of every order: **Coming up** (not done, soonest first), **All visits** or
**Done**, with how many are in the next 7 days and how many are overdue. Tick a visit done, or **Open order**.

## Code

| Where | What |
|---|---|
| `Mark.Core/Orders` | `CustomerOrder` (stage and history, payments, visits, dispatch notes, sign-off; paid and balance), `OrderStage`, `PaymentKind`, `PaymentMethod`, `VisitKind` |
| `Mark.Data` | Schema 9: `customer_orders`; `SqliteOrderRepository` (`LocalStore.Orders`) |
| `Mark.Reports` | `OrderPdf`: dispatch note and installation certificate |
| `Mark.Designer` | `OrdersViewModel` (Orders and Schedule), `MainViewModel.Orders` (`ShowOrder`, `OrderBook`) |
