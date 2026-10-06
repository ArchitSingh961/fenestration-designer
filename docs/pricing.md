# Pricing (Milestone 11)

Prices come from two places you control:

1. **Product prices** in the library (Library Manager, or "Product prices..." on the Pricing tab): profiles per metre,
   glass per m² (with a minimum chargeable area), accessories per piece / metre / m². These give each window's
   **material cost**.
2. **The price structure** of each quote (Pricing tab): what is added on top of the material cost.

## The price structure (`PriceStructure`, stored in the quote)

```text
per window   material cost (library: profiles incl. sash and mesh bars, glass, accessories)
           + rated costs     hardware per sash by opening type, insect mesh per m², reinforcement per metre of profile
           + cost lines      in order, e.g. wastage %, powder coating per m, labour per m², margin %
           = basic price     × (1 − discount %) = price each;  × quantity = design total
quote        basic value (all designs × quantities)
           − discount %                                 = sub-total
           + charges          fixed per quote, per window or per m² (transport, loading…)   = total
           + tax %            e.g. GST 18 %             = grand total
```

| Calculation type | Amount (per window) |
|---|---|
| Custom formula | what its formula works out to, e.g. `#AREASQFT * 75` or `@Profile Cost + @Glass Cost` |
| Percentage of … | rate % of its formula, e.g. 20 % of `@Sub Total Including Labour`, 10 % of `#RICOST` |
| Subtotal | everything above it; shown on the sheet and usable below, but **not added again** |
| Extra cost (per design) | the design's own extra cost (Design panel › Extra cost) |
| % of profiles / glass / accessories | that part of the material cost |
| % of material cost | material + hardware + mesh + reinforcement |
| % of everything above | material, rated costs and every cost line above it (a margin) |
| per metre / running ft of profile | all bars, including sash and mesh bars |
| per m² / sq. ft. of window or glass | outer window area / glass area |
| per window / per sash | 1 / the number of opening sashes |

## The cost sheet

Every window's sheet starts with six **material lines**, then the cost heads in order, then basic value, discount and
unit price (Pricing tab › **Cost sheet**, per design, with a "Calculation type" column that can be hidden):

| Material line | Formula value | What it is |
|---|---|---|
| Profile Cost | `#PROFILECOST` | profiles from the library (frame, mullions, sashes, mesh bars, parts) |
| RI Cost | `#RICOST` | reinforcement from the library, or the reinforcement rate |
| Hardware Cost | `#HWCOST` | the library's hardware sets, plus the hardware rates per sash |
| Glass Cost | `#GLASSCOST` | glass from the library |
| Accessories Cost | `#ACCCOST` | gaskets, cleats, screws and other consumables |
| Mesh Cost | `#MESHCOST` | insect mesh at the mesh rate |

Formulas (`CostFormula`) use numbers, `+ − * /`, brackets, the lines above by name (`@Profile Cost`, also written
`@Profile Cost.value`; not case-sensitive) and a window's values: `#MATERIALCOST`, `#EXTRACOST`, `#AREASQFT`, `#AREAM2`,
`#GLASSSQFT`, `#GLASSM2`, `#PROFILEM`, `#PROFILEFT`, `#PERIMETERM`, `#SASHES`, `#WIDTH`, `#HEIGHT`, `#QTY`. A formula
can only use lines **above** it, so names must be unique and cannot repeat a material line. A formula that does not
make sense is refused when the structure is applied or saved, with what is wrong ("uses @Profit, which is not a cost
line above it").

Charges (after the discount) are fixed per quote, per window or per m² / sq. ft. of window. The default structure
("Retail") reads like a cost sheet: Profile Wastage 10 % of `#PROFILECOST`, RI Wastage 10 % of `#RICOST`, Glass Wastage
5 % of `#GLASSCOST`, Powder Coating 60/m, **Total Raw Material Cost**, Fabrication Labour 750/m², Installation Labour
500/m², Extra Cost (per design), **Sub Total Including Labour**, Profit 20 % of `@Sub Total Including Labour`; then
Transportation Cost and Loading And Unloading 1,000 each, and GST 18 %; hardware rates per sash (casement 1,200,
top/bottom hung 950, tilt & turn 3,500, pivot 2,800, sliding 850) and mesh 450/m². It prices exactly like the earlier
default. Every number is yours to change.

- **Pricing tab**: edit the structure as a form; the price summary and the per-design table on the right preview the
  changes live. **Apply to this quote** stores them (one undo step). **Save as my default** keeps them in the database
  (`app_settings`, schema 3): every new quote starts with them. **Use my default** loads them into the form.
- `PricingEditor` validates (unique names, rates ≥ 0, formulas, discount and tax 0–100 %, a fixed-per-quote amount only as a charge).
- `PricingEngine.Price(project, calculation, structure)` is pure and deterministic; money is rounded per line, so the
  summary adds up exactly. The quote's **value** everywhere (header, Designs tab, quote list, dashboard) is its grand total.

## Sashes, mesh and quantities in the calculation

- An opening sash adds **four mitred sash bars** around its opening, cut from the library's sash profile (the first
  active profile with the **Sash** role). Its glass sits in the sash: sash glass + the sash profile's glazing bite.
  The sash band in the drawing is the sash profile's face width.
- A mesh shutter adds four bars from the **Mesh sash** profile and a mesh area (inside those bars) for the mesh rate.
- If the library has no such profile, the bars are listed but reported as not priced. Libraries from earlier versions
  get the shipped "55mm Casement Sash" and "30mm Mesh Shutter" once when MARK starts.
- **Quantities**: calculation lines stay per window, but the bill of materials, cut list (and so the cutting plan),
  total cost and weight now include every window of every design.

## The Pricing tab's pages and the quote's own rates

The Pricing tab has a list of pages on the left:

- **Project price structure**: the cost heads, discount, tax and charges (as above).
- **Profile rate**, **Reinforcement rate**, **Hardware rate**, **Accessory rate**, **Glass rate**: the library items
  this quote's designs use, each with the library's price and the rate this quote uses. Typing a rate gives the quote its
  own (highlighted, "this quote"); **Use library rates** puts the ticked rows (or all rows) back on the library's price.
  The per-sash hardware rates and the flat reinforcement rate are on the Hardware and Reinforcement pages.
- **Mesh rate**: a rate per m² for each mesh type the designs use (Design properties › Mesh type), and the rate for
  any other mesh.
- **Design add-on cost heads**: each design's extra cost per window (the Extra cost line of its cost sheet). This
  changes the design at once (one undo step).

The rates are part of the form like the rest of the tab: the price summary previews them, and **Apply to this quote**
(or leaving the tab, or Save) puts them in the quote. They are stored in `PriceStructure.ItemRates` (keys from
`RateKey`: `profile:ID`, `glass:ID`, `material:ID`) and `PriceStructure.MeshRates` (by mesh type), saved with the quote.
The calculation engine prices with `RatedLibrary.For(library, project.Pricing)`: the library's products with the quote's
rates in place of their prices, so the bill of materials, the cost sheet, the quotation and the invoice all use them.
The library itself (and purchasing, which buys at the library's price) does not change. An item without a rate of its
own follows the library's price.
