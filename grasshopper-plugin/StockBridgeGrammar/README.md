# StockBridgeGrammar 1.0.0

A Rhino 8 / Grasshopper plugin for stock-constrained structural geometry. It recognises curves as a node/member graph, assigns available material, applies shape-grammar rules, and previews the result. The plugin provides **15 components and 19 rules**.

Read the [English user guide](README_UserGuide_EN.md), [中文使用说明](使用说明_中文.md), or [examples](examples/README.md).

## Install

The installation archive includes `StockBridgeGrammar.gha` in its top-level `StockBridgeGrammar` folder. Use that file directly; no build is needed.

Close Rhino, copy the `.gha` file to `%APPDATA%\Grasshopper\Libraries\`, unblock it in Windows file properties if necessary, and restart Rhino 8. The components appear in the **StockBridge** tab. Keep only one installed copy.

### Build from source

This step applies only to the source archive or a repository checkout. From the directory containing `StockBridgeGrammar.csproj`, build with a .NET SDK capable of targeting .NET Framework 4.8:

```powershell
dotnet build -c Release
```

Install the output, `bin/Release/StockBridgeGrammar.gha`, using the steps above.

## Basic workflow

1. Curves → **Recognize Structure.C**.
2. Recognize.J → **Assign Inventory.G**; stock JSON → **Assign.S and Apply Grammar Rule.S**.
3. Assign.G → **Apply.G and Target Picker.G**; Assign.M → **Target Picker.M**.
4. Supply one rule ID to Apply.R. Select a member or node using Target Picker and connect its T output to Apply.T.
5. Apply.G → **Decode Graph.G**. Read Apply.H/W for the operation record and diagnostics.

For successive edits, pass each updated graph to the next Apply.G. Coordinates and stock lengths must use the same units; the supplied examples use metres.

## Spatial stock-constrained nodes

For `addNodeToStock`, connect **three distinct, non-collinear anchors to Position (Pt)** and a **separate desired location to Target Point (Tp)**. Target Picker.Tp can forward that location; Target Picker.T carries only an ID.

The rule intersects three spheres with radii equal to three available stock-piece lengths and chooses the feasible spatial solution nearest Tp. Each piece is used once at its original length. The minimum distance from the anchor plane is controlled by `minHeight`, default `0.01` m. Insufficient stock or incompatible geometry produces a diagnostic without adding a node.

The [spatial example](examples/add_node_spatial/README.md) supplies three 2 m pieces, three anchors, and two targets with analytically known mirror solutions.

## Rules and checks

Rule Library lists the 19 rule IDs, target types, and parameters. [Parameter examples](examples/rule_params_examples.json) provide JSON values for each rule.

Section Check accepts supplied member forces. Connect **G2/Nr/Nt → Apply.G/R/T** to execute one suggested change, with stock connected to Apply.S. Recompute forces for the changed geometry before the next check. See the [section-check example](examples/section_check_limits.md).

The plugin provides geometry constraints, stock assignment, member checks, and topology diagnostics. It does not solve whole-structure forces or establish overall structural stability. Analyse each resulting structure independently.
