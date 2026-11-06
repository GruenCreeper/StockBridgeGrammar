# Minimal workflow — StockBridgeGrammar 1.0.0

1. Curves → Recognize.C; read Report to check node/member recognition.
2. Example Stock.J → Assign.S **and Apply.S**. Recognize.J → Assign.G.
3. Assign.G → Apply.G and Target Picker.G; Assign.M → Target Picker.M.
4. Pick a member with Target Picker `by_curve` and C, or use its mismatch selection. Target Picker.T → Apply.T.
5. Put one rule ID, such as `splitMemberByStockLength`, in a Panel → Apply.R. Params can remain empty for defaults. Split uses `maxBridgesPerNode`; its number of segments follows stock lengths.
6. Apply.G → Decode.G. Read Apply.H/W for the actual change/refusal.
7. For another operation, pass the updated graph to the next Apply.G.

To try spatial `addNodeToStock`, use [the three-anchor example](add_node_spatial/README.md). Position carries three anchors; the separate Target Point carries the desired position.

For a Section Check step, use G2/Nr/Nt → Apply.G/R/T. Recompute forces on changed geometry before the next check. Per-member Ru/T report lists do not implement a sequential loop.
