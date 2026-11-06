# StockBridgeGrammar 1.0.0 — Demo workflow

## Files

- `create_demo_truss_rhino.py`: creates a 12 m × 1.6 m Warren-style truss in Rhino.
- `build_demo_canvas.py`: generates the basic Grasshopper workflow from the installed plugin.
- [connection_map.md](connection_map.md): wiring, including stock → Apply.S.
- `demo_stock_short.json` and `demo_stock_mixed_reclaimed.json`: example inventories.
- `demo_rule_params.json`: parameter objects for the current 19 rules.

## Run

1. Build/install the plugin following [the main guide](../../README_UserGuide_EN.md).
2. Set the Rhino 8 document units to metres, then run `create_demo_truss_rhino.py` with `_RunPythonScript`.
3. Open a new Grasshopper canvas. Add a GhPython/Python component with a Boolean input named `Run`, paste `build_demo_canvas.py`, then turn Run on. The script creates a basic-workflow subset of the plugin's 15 component types.
4. Turn Run off after placement. Set the Curve parameter to all demo member curves. If placement/wiring reports errors, use the connection map to repair them; script behaviour can vary with the Rhino Python runtime.
5. Choose Example Stock preset `short_stock`. Read Recognize.Report for node/member counts and Assign.M for stock mismatches.
6. The rule Panel starts with `splitMemberByStockLength`. Rule Params Builder uses default parameters. Stock is connected to both Assign and Apply. Read Apply.H/W to see whether the target can be split and whether bracing succeeded.
7. Decode previews the result. Candidate Selector can read Generate's tree directly; choose an index and read the actual candidate name/history.

To accumulate manual edits, use the previous updated graph as the next Apply input. A parameter list of rules does not create a sequential loop.

For the three-anchor rule, use [the spatial addNodeToStock example](../add_node_spatial/README.md). It explicitly separates Position anchors from Target Point and verifies upper/lower mirror solutions.

## Troubleshooting

- Plugin components unavailable: check the installed 1.0.0 build, Windows unblock status, and restart Rhino.
- Apply unchanged: inspect W for missing stock, unsuitable target, or geometric refusal.
- No desired target selected: use Target Picker `by_curve` / `by_point` as appropriate; Target Point coordinates must travel through the Tp wire.
- Canvas wiring differs: create a new canvas from the script or reconnect using the table.
- Repeated placement: turn Run off or remove the script after a successful build.

Layer names in the Rhino truss script describe that example's geometry; Recognize does not generate chord-role or bay classifications. Stock/topology checks do not certify whole-structure stability.
