# Demo connection map — 1.0.0

Run `build_demo_canvas.py` on a new Grasshopper canvas, or place the workflow components and use this table. The plugin has 15 component types; this example uses a subset plus a second Decode instance.

| # | Source | Destination |
| --- | --- | --- |
| 1 | Curve parameter | Recognize.C |
| 2 | Recognize.J | Assign.G |
| 3 | Example Stock.J | Assign.S |
| 4 | Assign.G | Target Picker.G |
| 5 | Assign.M | Target Picker.M |
| 6 | Assign.G | Apply.G |
| 7 | Target Picker.T | Apply.T |
| 8 | Rule Params Builder.P | Apply.P |
| 9 | One rule-ID Panel | Apply.R |
| 10 | Same rule-ID Panel | Rule Params Builder.R |
| 11 | Example Stock.J | **Apply.S** |
| 12 | Apply.G | Decode.G |
| 13 | Recognize.J | Generate Candidates.G |
| 14 | Example Stock.J | Generate Candidates.S |
| 15 | Generate Candidates.G | Candidate Selector.CG |
| 16 | Candidate Selector.G | Second Decode.G |

Candidate Selector reads all incoming candidate branches; no Flatten is required.

## Initial controls

| Input | Suggested value |
| --- | --- |
| Recognize.Tol | 0.01, in the same units as the model |
| Example Stock.P | `short_stock` |
| Target Picker.Mo | `by_mismatch_rank`, or `by_curve` with one curve at C |
| Target Picker.R | 0 |
| Rule ID Panel | `splitMemberByStockLength` |
| Rule Params Builder.Def | true |
| Candidate Selector.i | 0, then inspect other candidate names |

Split segment count follows stock lengths. To supply named parameters, turn Def off and use Rule Params Builder's Extra Params JSON (X).

## Spatial add-node wiring

For `addNodeToStock`, connect three anchors to Apply.Pt and a separate desired location to Apply.Tp. Target Picker's Tp output forwards the original point coordinate; wire **Target Picker.Tp → Apply.Tp**. Its T output is still an ID. With explicit anchors, Apply.T may be empty.

Use [the spatial example](../add_node_spatial/README.md) for two mirrored targets and three original-length stock pieces.

## Section Check

Use the separate `../build_section_loop_canvas.py` script for one check/apply step. Its correct output wiring is **G2/Nr/Nt → Apply.G/R/T**. Ru/T are per-member report lists. Recompute forces for the updated geometry before another step.
