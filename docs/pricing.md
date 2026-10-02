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

| Cost line basis | Multiplied by (per window) |
|---|---|
| % of profiles / glass / accessories | that part of the material cost |
| % of material cost | material + hardware + mesh + reinforcement |
| % of everything above | material, rated costs and every cost line above it (a margin) |
| per metre of profile | all bars, including sash and mesh bars |
| per m² of window / glass | outer window area / glass area |
| per window / per sash | 1 / the number of opening sashes |

Charges (after the discount) are fixed per quote, per window or per m² of window. The default structure ("Retail")
has profile wastage 10 %, glass wastage 5 %, powder coating 60/m, fabrication labour 750/m², installation 500/m²,
overheads and margin 20 % of everything above, transportation and loading 1,000 each, and GST 18 %; hardware rates
per sash (casement 1,200, top/bottom hung 950, tilt & turn 3,500, pivot 2,800, sliding 850) and mesh 450/m².
Every number is yours to change.

- **Pricing tab**: edit the structure as a form; the price summary and the per-design table on the right preview the
  changes live. **Apply to this quote** stores them (one undo step). **Save as my default** keeps them in the database
  (`app_settings`, schema 3): every new quote starts with them. **Use my default** loads them into the form.
- `PricingEditor` validates (names, rates ≥ 0, discount and tax 0–100 %, a fixed-per-quote amount only as a charge).
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
