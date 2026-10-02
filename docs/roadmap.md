# Roadmap: from designer to quoting system

Milestones 1–8 built the 2D designer, product library, calculation, cutting plans and local storage. The next
milestones follow a reference workflow recorded from a web-based window-quoting system (October 2026): create an
opportunity, open a quote, design each window type from a library of typologies, price it, then produce documents.
We keep our own name, look and code; the reference only defines the workflow. Everything stays a WPF desktop app
on top of the existing Core / Calculation / Data projects.

| # | Milestone | What the user gets |
|---|-----------|--------------------|
| 9 ✅ | **Openings and design library** | Every opening can be fixed, side-hung (left/right), top-hung, bottom-hung, tilt & turn, pivot or sliding, with an optional insect-mesh shutter ("twin sash"). A design library panel (Dividers, Casement, Tilt & turn, Twin sash, Pivot, Sliding, Vertical sliding) with drawn thumbnails: click to apply to the selected opening or frame, or drag onto an opening. Whole-frame templates ask before clearing an existing design. CAD opening symbols, handle marks and handle heights, sash/mesh/glass/frame labels (S1, M1, ①, F1), design reference and quantity (W1 × 2), location, floor and note, a floor line with the sill height, an Inside / Outside view. |
| 10 ✅ | **Quotes and designs** | A quote holds customer/site details and a list of designs (window types) with quantities. Quote list (Active / Won / Lost / All, search, quote numbers), quote page with Client / Designs / Drawing tabs (Pricing, Documents and Report tabs follow in M11 and M13), design cards with thumbnail, size, glass, quantity and price; edit a design in the canvas. |
| 11 ✅ | **Pricing structure** | Rate tables (profile, reinforcement, hardware, glass, mesh) and an ordered list of cost heads (material cost, wastage %, coating per perimeter, labour per area, transport, loading, discount, tax). Per-design and project price summaries (basic value → sub-total → total → GST → grand total). Sash and mesh members enter the bill of materials and cutting plan. |
| 12 | **Opportunities and dashboard** | Two-step opportunity form (basic info + site address, official info: stage, source, owner, value, dates). The basic dashboard (tiles, value by status, win rate, recent quotes) came with M10; this adds charts by period and opportunities. |
| 13 | **Documents and reports** | Printable quotation with drawings, cutting list, material purchase list, production and dispatch reports; attached documents per quote. |
| 14 | **Shell and polish** | Navigation rail, toasts ("Saved"), keyboard shortcuts everywhere, accessibility and performance pass, installer. |

Each milestone ends with tests, updated docs and a commit to `main`.
