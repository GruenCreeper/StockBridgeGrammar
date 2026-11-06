# StockBridgeGrammar 1.0.0 — User guide

StockBridgeGrammar provides **15 Grasshopper components and 19 rules** for recognising curves as nodes and members, assigning material stock, and rewriting geometry. Nodes retain 3-D coordinates. Supports are supplied explicitly; recognition does not infer chord roles or bays.

## Install

The installation archive includes `StockBridgeGrammar.gha` alongside this guide. Use that file directly; no build is needed. Close Rhino, copy the file to `%APPDATA%\Grasshopper\Libraries\`, unblock it in Windows file properties if necessary, and restart Rhino 8. Keep only one installed copy. Components appear in the **StockBridge** tab.

For the source archive or a repository checkout, first run `dotnet build -c Release` in the directory containing `StockBridgeGrammar.csproj`, using a .NET SDK capable of targeting .NET Framework 4.8. Install the resulting `bin/Release/StockBridgeGrammar.gha` as described above.

## Basic wiring

| Source | Destination |
| --- | --- |
| Curve parameter | Recognize.C |
| Recognize.J | Assign.G |
| Example Stock.J or Stock Inventory Builder.J | **Assign.S and Apply.S** |
| Assign.G | Apply.G and Target Picker.G |
| Assign.M | Target Picker.M |
| Target Picker.T | Apply.T, for rules that use a target ID |
| One rule ID in a Panel | Apply.R and Rule Params Builder.R |
| Rule Params Builder.P | Apply.P |
| Apply.G | Decode.G |

Read Apply.H and W after each operation. To accumulate edits, send an updated graph to the next Apply.G. A list of rules does not create a sequential operation loop.

Stock entries use `id`, `length`, `width`, `height`, `material`, and `count`. Stock Item and Stock Inventory Builder create them without hand-written JSON. The stock inputs read all connected branches as one shared inventory. Distinct graphs are still calculated separately. Physical assignments are identified by IDs such as `S1#2`.

Coordinate units must agree with stock units; supplied examples use metres. See [custom stock input](examples/custom_stock_merge.md) for input formats and a small count example.

## Selecting a target

With **Mode (Mo) unwired**, Target Picker uses the first available input in this order:

1. A nonempty **Target ID (Id)** selects an exact member or node in the input graph.
2. A valid **Target Curve (C)** selects a member.
3. A valid **Target Point (Pt)** selects the nearest existing node.
4. Otherwise, the mismatch rank selects a member. With no mismatches, it falls back to the first member.

An ID that cannot be found stops selection; it does not fall back to Curve or Point. IDs are case-sensitive and surrounding whitespace is trimmed. Connect the updated graph after a geometry change; Graph Label can display its member and node IDs.

An explicitly connected Mode overrides the automatic order:

| Mode | Input |
| --- | --- |
| `by_mismatch_rank` | Mismatches and zero-based rank R |
| `by_member_id` | Exact member ID at Id |
| `by_node_id` | Exact node ID at Id |
| `by_index` | Zero-based member index I |
| `by_curve` | One curve at C |
| `by_point` | One point at Pt; T returns the nearest existing node ID |

The **Tp output forwards the original Pt coordinate independently of the selected ID**. If both Curve and Point are connected, automatic selection uses Curve while Tp still forwards the point. T and Tp have different purposes: T identifies an existing target; Tp supplies a spatial preference to Apply.Target Point.

## addNodeToStock: anchors and desired location

1. Set Apply.R to `addNodeToStock` and connect the graph and stock.
2. Connect exactly three distinct, non-collinear anchor points to **Position (Pt)** in one list branch. Alternatively, provide `points` coordinates or three `corners` node IDs in Params JSON.
3. Connect a separate desired new-node position to **Target Point (Tp)**, directly or through Target Picker.Tp.
4. Leave Apply.T empty when supplying explicit anchors. Leave Params empty for defaults, or set `{"minHeight":0.01}`.

Each candidate is the intersection of three spheres centred at the anchors, with radii equal to three available physical stock-piece lengths. The nearest feasible candidate to Tp is chosen. Three bars are created at their original piece lengths, using each physical piece once. The new node must be at least `minHeight` from the anchor plane, default `0.01` m. Insufficient stock or incompatible geometry produces a diagnostic without adding a node. The rule does not shorten pieces or substitute a planar node; degree is fixed at three.

Moving Tp above or below the anchor plane selects between feasible mirror solutions. Tp is a preference among stock-compatible positions, so the result need not coincide with it. Connect Tp explicitly when the desired side matters. See the [upper/lower target example](examples/add_node_spatial/README.md).

`addConnectionPoint` is a separate centroid-based hub rule; it does not use this three-sphere construction.

## Rules and parameters

Rule Library lists all 19 rule IDs, target types, and parameters. [Parameter examples](examples/rule_params_examples.json) cover each rule. Split uses `maxBridgesPerNode`, addConnectionPoint uses `extraAnchors`, and fanOutLoadPath uses `fanCount`.

Rule Params Builder's **Extra Params JSON (X)** overlays named parameters on defaults when **Use Defaults (Def)=false**. Def=true ignores X and Fan Count. Fan Count controls `fanOutLoadPath` when Def=false. Inputs marked deprecated are retained for canvas compatibility and do not control the rules.

## Section checks and candidates

Section Check evaluates supplied axial forces and stock properties. **Stress Limit (SL)** and **Buckling Limit (BL)** set independent acceptance limits, both defaulting to 1.0. The output ratios remain unchanged by these limits. See [section-check limits](examples/section_check_limits.md) for details. A `noForce` result means no force was supplied for that member; it is not a successful check.

For one checked modification, wire **Section Check.G2/Nr/Nt → Apply.G/R/T**, with stock connected to Apply.S. G2 carries assignments; Nr/Nt carry one suggested operation. Ru/T are per-member report lists. Supply any anchors or target points required by the suggested rule. Recalculate forces after a geometry change before checking the next graph.

Generate Candidates creates alternative graphs in separate branches. Connect its G output directly to Candidate Selector.CG; it reads all candidate branches. Select an index and read the candidate name and operation history.

Design Drama and Grammar Intensity scale rule-attempt counts together; Max Iterations=0 preserves the base geometry. Include Lean Reuse (A) controls whether that candidate family is included and does not guarantee geometric symmetry.

## Examples and troubleshooting

The [example index](examples/README.md) includes basic wiring, canvas generators, inventory files, and the spatial construction with known expected coordinates.

- **Rule leaves the graph unchanged:** read W and confirm the target type, stock, and required inputs.
- **Spatial add-node refused:** check anchor distinctness, non-collinearity, available counts, piece lengths, and `minHeight`.
- **Moving the target point has no effect:** connect Target Picker.Tp to Apply.Tp. Different target points may also select the same nearest feasible solution.
- **Canvas wiring differs from the guide:** create a new canvas using the supplied scripts or reconnect by parameter name.

The plugin provides geometry constraints, stock assignment, force-based member checks when forces are supplied, and topology diagnostics. It does not establish whole-structure stability or automatically recompute forces. Review each operation and analyse each resulting structure.
