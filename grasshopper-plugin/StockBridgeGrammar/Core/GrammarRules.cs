using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StockBridgeGrammar.Models;

namespace StockBridgeGrammar.Core
{
    // Stock-responsive graph transformations. Successful rules return an updated
    // graph and a history step; refused rules return a null Graph with warnings.
    public static class GrammarRules
    {
        public static readonly string[] SupportedIds =
        {
            "splitMemberByStockLength",
            "addConnectionPoint",
            "removeConnectionPoint",
            "triangulateBay",
            "densifyWithShortStock",
            "fanOutLoadPath",
            "removeRedundantMember",
            "shiftNodeToMatchStock",
            "articulateChainToStock",
            "connectNodeToStock",
            "mergeSplitChain",
            "addNodeToStock",
            "removeNode",
            "dissolveNodeToDiagonal",
            "crossBracePanel",
            "uncrossPanel",
            "dedupeMembers",
            "splitEdgeToPoint",
            "relocateMember",
        };

        public static GrammarRuleResult Apply(
            BridgeGraph graph, string ruleId, string targetId,
            JObject parameters, List<StockItem> stock)
        {
            var result = new GrammarRuleResult();
            parameters = parameters ?? new JObject();
            stock      = stock      ?? new List<StockItem>();

            switch (ruleId)
            {
                case "splitMemberByStockLength":
                    result = SplitMemberByStockLength(graph, targetId, parameters, stock); break;
                case "addConnectionPoint":
                    result = AddConnectionPoint(graph, targetId, parameters, stock); break;
                case "triangulateBay":
                    result = TriangulateBay(graph, targetId, parameters, stock); break;
                case "shiftNodeToMatchStock":
                    result = ShiftNodeToMatchStock(graph, targetId, parameters, stock); break;
                case "removeConnectionPoint":
                    result = RemoveConnectionPoint(graph, targetId, parameters, stock); break;
                case "densifyWithShortStock":
                    result = DensifyWithShortStock(graph, targetId, parameters, stock); break;
                case "fanOutLoadPath":
                    result = FanOutLoadPath(graph, targetId, parameters, stock); break;
                case "removeRedundantMember":
                    result = RemoveRedundantMember(graph, targetId, parameters); break;
                case "articulateChainToStock":
                    result = ArticulateChainToStock(graph, targetId, parameters, stock); break;
                case "connectNodeToStock":
                    result = ConnectNodeToStock(graph, targetId, parameters, stock); break;
                case "mergeSplitChain":
                    result = MergeSplitChain(graph, targetId, parameters, stock); break;
                case "addNodeToStock":
                    result = AddNodeToStock(graph, targetId, parameters, stock); break;
                case "removeNode":
                    result = RemoveNode(graph, targetId, parameters, stock); break;
                case "dissolveNodeToDiagonal":
                    result = DissolveNodeToDiagonal(graph, targetId, parameters, stock); break;
                case "crossBracePanel":
                    result = CrossBracePanel(graph, targetId, parameters, stock); break;
                case "uncrossPanel":
                    result = UncrossPanel(graph, targetId, parameters, stock); break;
                case "dedupeMembers":
                    result = DedupeMembers(graph, targetId, parameters, stock); break;
                case "splitEdgeToPoint":
                    result = SplitEdgeToPoint(graph, targetId, parameters, stock); break;
                case "relocateMember":
                    result = RelocateMember(graph, targetId, parameters, stock); break;
                default:
                    result.Warnings.Add($"unknown ruleId: {ruleId}");
                    return result;
            }

            // Add a geometric Maxwell member-count diagnostic to each successful
            // transformation. This does not test equilibrium, stiffness, support
            // constraints, or stability. Refusals (Graph == null) are untouched.
            if (result != null && result.Graph != null)
                AnnotateDeterminacy(result);
            return result;
        }

        // Maxwell count — auto 2-D vs 3-D from the geometry:
        //   planar nodes → DoF = m − (2n − 3)
        //   3-D nodes    → DoF = m − (3n − 6)
        // The historical DoF field is a member-count surplus/deficit, not a
        // measured degree of freedom. These formulas omit support constraints
        // and do not establish stability, stiffness, or static determinacy.
        private static void AnnotateDeterminacy(GrammarRuleResult res)
        {
            var g = res.Graph;
            if (g?.Nodes == null || g.Members == null) return;
            int n = g.Nodes.Count, m = g.Members.Count;
            bool planar = IsPlanar(g.Nodes);
            int dof = planar ? m - (2 * n - 3) : m - (3 * n - 6);
            string dim = planar ? "2-D" : "3-D";
            string formula = planar ? $"m−(2n−3) = {m}−(2·{n}−3)" : $"m−(3n−6) = {m}−(3·{n}−6)";
            string verdict = dof > 0 ? $"member-count surplus +{dof}"
                           : dof == 0 ? "balanced member count"
                           : $"member-count deficit {dof}";
            string line = $"Maxwell count [{dim}]: DoF = {formula} = {dof} → {verdict}. " +
                "Count excludes support constraints; it is not a stability, stiffness, or static-determinacy check.";
            if (res.Step != null)
                res.Step.Note = string.IsNullOrEmpty(res.Step.Note) ? line : res.Step.Note + "\n" + line;
            if (dof < 0)
                res.Warnings.Add(
                    $"Maxwell member-count deficit ({dim}, DoF = {dof} < 0): the graph may be under-braced. " +
                    "Verify geometry, support constraints, and structural response; this count alone does not determine stability.");
        }

        // Geometric plane classification for the count diagnostic only.
        private static bool IsPlanar(List<BridgeNode> nodes)
        {
            if (nodes == null || nodes.Count < 4) return true;     // ≤3 nodes are always coplanar
            var p0 = nodes[0];
            // span / scale for a relative tolerance
            double minX = nodes.Min(q => q.X), maxX = nodes.Max(q => q.X);
            double minY = nodes.Min(q => q.Y), maxY = nodes.Max(q => q.Y);
            double minZ = nodes.Min(q => q.Z), maxZ = nodes.Max(q => q.Z);
            double scale = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY) + (maxZ - minZ) * (maxZ - minZ));
            double tol = Math.Max(1e-4, 1e-8 * scale);             // metres; retain small but real spatial heights

            // first in-plane axis u
            double ux = 0, uy = 0, uz = 0, ul = 0;
            foreach (var q in nodes)
            {
                double dx = q.X - p0.X, dy = q.Y - p0.Y, dz = q.Z - p0.Z, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d > ul) { ul = d; ux = dx; uy = dy; uz = dz; }
            }
            if (ul < 1e-9) return true;
            ux /= ul; uy /= ul; uz /= ul;
            // plane normal = u × (widest off-axis vector)
            double nx = 0, ny = 0, nz = 0, nl = 0;
            foreach (var q in nodes)
            {
                double wx = q.X - p0.X, wy = q.Y - p0.Y, wz = q.Z - p0.Z;
                double cx = uy * wz - uz * wy, cy = uz * wx - ux * wz, cz = ux * wy - uy * wx;
                double cl = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                if (cl > nl) { nl = cl; nx = cx; ny = cy; nz = cz; }
            }
            if (nl < 1e-9) return true;                            // all nodes collinear → planar
            nx /= nl; ny /= nl; nz /= nl;
            foreach (var q in nodes)
                if (Math.Abs((q.X - p0.X) * nx + (q.Y - p0.Y) * ny + (q.Z - p0.Z) * nz) > tol) return false;
            return true;
        }

        // ---------------- 1. splitMemberByStockLength ------------------
        // Stock-driven subdivision with connections to nearby nodes:
        //   (a) Cut positions come from greedily packing actual stock lengths,
        //       NOT from a splitCount param. If the stock pool cannot cover the
        //       member within the waste budget, the rule REFUSES (no equal-
        //       split fallback).
        //   (b) Each cut first tries to SNAP to an existing nearby node within
        //       snapRadius·L — reusing that node instead of creating a floating
        //       point in space.
        //   (c) For each genuinely NEW node we created, search for the nearest
        //       other node within bridgingRadius·L and add ONE bridging member
        //       so the new node is topologically integrated.
        //   (d) Endpoints a / b are NEVER moved. Stock is recorded on each
        //       sub-member via AssignedStockId but NOT consumed — the Assigner
        //       remains the single source of truth for inventory accounting.

        public static GrammarRuleResult SplitMemberByStockLength(
            BridgeGraph g, string memberId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            var m = g.Members.FirstOrDefault(x => x.Id == memberId);
            if (m == null) { res.Warnings.Add($"member {memberId} not found"); return res; }
            var a = g.Nodes.FirstOrDefault(n => n.Id == m.From);
            var b = g.Nodes.FirstOrDefault(n => n.Id == m.To);
            if (a == null || b == null)
            {
                res.Warnings.Add("member endpoints missing");
                return res;
            }
            double L = Dist(a, b);
            if (L < 1e-6) { res.Warnings.Add("member is zero-length"); return res; }

            if (stock == null || stock.Count == 0)
            {
                res.Warnings.Add(
                    "splitMemberByStockLength requires a non-empty Stock list — " +
                    "connect Stock JSON into Apply Grammar Rule's Stock (S) input.");
                return res;
            }

            // -------- params + DERIVED scales (no magic constants) --------
            // Both search radii are derived from the stock itself, so "form
            // follows stock" holds and there are no hand-tuned distances:
            //   • bridgingRadius = longest stock piece — a brace member must be
            //     buildable from one piece, so its reach IS one stock length.
            //   • snapRadius = half the shortest stock piece — the resolution
            //     below which an existing node and an ideal cut are "the same
            //     connection" (finer than the smallest buildable module).
            double minStock = stock.Min(s => s.Length);
            double maxStockLen = stock.Max(s => s.Length);
            double snapRadius = 0.5 * minStock;
            double bridgingRadius = maxStockLen;
            // maxBridges = 2 is a structural choice, not a tuning knob: a splice
            // node needs two transverse braces to be triangulated (not a hinge).
            int maxBridges = Math.Max(0, Math.Min(3,
                p["maxBridgesPerNode"]?.Value<int?>() ?? 2));

            var allowedIds = new HashSet<string>();
            if (p["allowedStockIds"] is JArray arr)
                foreach (var j in arr)
                {
                    var s = j?.Value<string>();
                    if (!string.IsNullOrEmpty(s)) allowedIds.Add(s);
                }

            // -------- step 1: build stock pool (expanded by Count) --------
            var pool = new List<(string id, double len)>();
            foreach (var s in stock)
            {
                if (allowedIds.Count > 0 && !allowedIds.Contains(s.Id)) continue;
                int c = Math.Max(0, s.Count);
                for (int i = 0; i < c; i++) pool.Add((s.Id, s.Length));
            }
            if (pool.Count == 0)
            {
                res.Warnings.Add("stock pool is empty after allowedStockIds filter");
                return res;
            }

            // -------- step 2: does this member even need splitting? --------
            // Offcuts return to inventory, so a member that fits within a
            // SINGLE stock piece never needs splitting — you cut it from one
            // piece and the remainder flows back to the pool. Splitting is
            // only required when the member is longer than the LONGEST single
            // piece (no one piece can span it). There is therefore no "waste
            // budget": any offcut is reusable, not waste.
            pool.Sort((x, y) => y.len.CompareTo(x.len));
            double maxStock = pool[0].len;
            if (L <= maxStock + 1e-6)
            {
                res.Warnings.Add(
                    $"member {m.Id} ({L:F2}m) fits within a single stock piece " +
                    $"(longest = {maxStock:F2}m); the {(maxStock - L):F2}m offcut returns " +
                    "to inventory — no split needed.");
                return res;
            }

            // Greedy longest-first: accumulate pieces until they span L. Each
            // piece's offcut returns to inventory; the only failure is running
            // out of total stock length.
            var picked = new List<(string id, double len)>();
            double cumulative = 0;
            foreach (var s in pool)
            {
                if (cumulative >= L - 1e-9) break;
                picked.Add(s);
                cumulative += s.len;
            }
            if (cumulative < L - 1e-6)
            {
                res.Warnings.Add(
                    $"member {L:F2}m is longer than the longest stock ({maxStock:F2}m) and even " +
                    $"the total available stock ({cumulative:F2}m) cannot span it — add longer / more stock.");
                return res;
            }

            // -------- step 3: raw cut t-positions along a→b --------
            var rawCuts = new List<double>();
            double acc = 0;
            for (int i = 0; i < picked.Count - 1; i++)
            {
                acc += picked[i].len;
                double t = acc / L;
                if (t >= 1.0 - 1e-6) break;
                rawCuts.Add(t);
            }
            if (rawCuts.Count == 0)
            {
                res.Warnings.Add("no interior cut points (all pieces past member end)");
                return res;
            }

            // -------- step 4: for each cut, snap to nearest node or create new --------
            var usedSnaps = new HashSet<string> { a.Id, b.Id };
            var cutNodes = new List<(BridgeNode node, bool isNew, double t)>();
            int newNodeSeed = g.Nodes.Count;
            foreach (var t in rawCuts)
            {
                double ix = a.X + (b.X - a.X) * t;
                double iy = a.Y + (b.Y - a.Y) * t;
                double iz = a.Z + (b.Z - a.Z) * t;
                BridgeNode bestSnap = null;
                double bestDist = double.MaxValue;
                foreach (var n in g.Nodes)
                {
                    if (usedSnaps.Contains(n.Id)) continue;
                    double dx = n.X - ix, dy = n.Y - iy, dz = n.Z - iz;
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d <= snapRadius && d < bestDist)
                    {
                        bestDist = d;
                        bestSnap = n;
                    }
                }
                if (bestSnap != null)
                {
                    usedSnaps.Add(bestSnap.Id);
                    cutNodes.Add((bestSnap, false, ProjectT(a, b, bestSnap, L)));
                }
                else
                {
                    var nn = new BridgeNode
                    {
                        Id = NextNodeId(g, "S", newNodeSeed++),
                        X = Math.Round(ix, 4),
                        Y = Math.Round(iy, 4),
                        Z = Math.Round(iz, 4),
                    };
                    g.Nodes.Add(nn);
                    usedSnaps.Add(nn.Id);
                    cutNodes.Add((nn, true, t));
                }
            }

            // Sort by projected t so the chain remains monotonic along a→b
            // (snapped nodes may have shifted ordering).
            cutNodes.Sort((x, y) => x.t.CompareTo(y.t));

            // Drop cuts that collapsed to a or b after snap.
            var dedup = new List<(BridgeNode node, bool isNew, double t)>();
            foreach (var c in cutNodes)
            {
                if (c.t <= 1e-4 || c.t >= 1.0 - 1e-4) continue;
                if (dedup.Count > 0 && Math.Abs(dedup[dedup.Count - 1].t - c.t) < 1e-4) continue;
                dedup.Add(c);
            }
            if (dedup.Count == 0)
            {
                // Undo any newly-added nodes that snapping orphaned.
                foreach (var c in cutNodes)
                    if (c.isNew) g.Nodes.RemoveAll(n => n.Id == c.node.Id);
                res.Warnings.Add("all cut points collapsed to endpoints after snap — no split applied");
                return res;
            }

            // -------- step 5: build chain, reject if any segment duplicates an existing edge --------
            var chain = new List<BridgeNode> { a };
            foreach (var c in dedup) chain.Add(c.node);
            chain.Add(b);

            var existingEdges = new HashSet<string>();
            foreach (var mm in g.Members)
            {
                if (mm.Id == m.Id) continue;
                existingEdges.Add($"{mm.From}|{mm.To}");
                existingEdges.Add($"{mm.To}|{mm.From}");
            }
            for (int i = 0; i < chain.Count - 1; i++)
            {
                var key = $"{chain[i].Id}|{chain[i + 1].Id}";
                if (existingEdges.Contains(key))
                {
                    foreach (var c in dedup)
                        if (c.isNew) g.Nodes.RemoveAll(n => n.Id == c.node.Id);
                    res.Warnings.Add(
                        $"chain segment {chain[i].Id}→{chain[i + 1].Id} would duplicate an " +
                        "existing member after snap — abandoning split");
                    return res;
                }
            }

            // -------- step 6: replace original member with sub-members --------
            g.Members.RemoveAll(x => x.Id == m.Id);
            var addedMemberIds = new List<string>();
            var addedNodeIds = dedup.Where(c => c.isNew).Select(c => c.node.Id).ToList();
            for (int i = 0; i < chain.Count - 1; i++)
            {
                var nid = $"{m.Id}.s{i + 1}";
                addedMemberIds.Add(nid);
                string assignedStockId = i < picked.Count ? picked[i].id : null;
                g.Members.Add(new BridgeMember
                {
                    Id = nid,
                    From = chain[i].Id,
                    To = chain[i + 1].Id,
                    Length = Round(Dist(chain[i], chain[i + 1])),
                    AssignedStockId = assignedStockId,
                });
            }

            // -------- step 7: bridging links from genuinely new nodes --------
            int bridgeCount = 0;
            var bridgeDiag = new List<string>();   // per-new-node diagnostic
            if (maxBridges > 0 && bridgingRadius > 0)
            {
                var adjacency = new Dictionary<string, HashSet<string>>();
                foreach (var mm in g.Members)
                {
                    if (!adjacency.TryGetValue(mm.From, out var sFrom))
                        adjacency[mm.From] = sFrom = new HashSet<string>();
                    sFrom.Add(mm.To);
                    if (!adjacency.TryGetValue(mm.To, out var sTo))
                        adjacency[mm.To] = sTo = new HashSet<string>();
                    sTo.Add(mm.From);
                }
                // Member axis (unit). A bridge braces the splice node only to
                // the extent it runs ACROSS the member, so we rank reachable
                // nodes by transverse fraction (sinθ) — most perpendicular
                // first — instead of applying a hand-picked angle cutoff. Only
                // numerically-collinear candidates (no transverse component at
                // all) are dropped.
                double axx = (b.X - a.X) / L, axy = (b.Y - a.Y) / L, axz = (b.Z - a.Z) / L;

                int bridgeSeed = 0;
                foreach (var c in dedup)
                {
                    if (!c.isNew) continue;
                    adjacency.TryGetValue(c.node.Id, out var adj);
                    var seen = adj ?? new HashSet<string>();
                    var ranked = g.Nodes
                        .Where(n => n.Id != c.node.Id && !seen.Contains(n.Id))
                        .Select(n =>
                        {
                            double d = Dist(n, c.node);
                            double ux = (n.X - c.node.X) / d, uy = (n.Y - c.node.Y) / d, uz = (n.Z - c.node.Z) / d;
                            double cos = Math.Abs(ux * axx + uy * axy + uz * axz);
                            double transverse = Math.Sqrt(Math.Max(0, 1 - cos * cos));   // sinθ
                            return new { n, d, transverse };
                        })
                        .Where(x => x.d > 1e-6 && x.transverse > 1e-6)   // drop only collinear
                        // Best brace = closest AND most transverse. Combine as
                        // (distance / sinθ): the effective bracing cost.
                        .OrderBy(x => x.d / x.transverse)
                        .ToList();
                    var inRange = ranked.Where(x => x.d <= bridgingRadius)
                                        .Take(maxBridges).ToList();
                    foreach (var cand in inRange)
                    {
                        var bid = $"{m.Id}.b{++bridgeSeed}";
                        g.Members.Add(new BridgeMember
                        {
                            Id = bid,
                            From = c.node.Id,
                            To = cand.n.Id,
                            Length = Round(cand.d),
                        });
                        addedMemberIds.Add(bid);
                        if (!adjacency.TryGetValue(c.node.Id, out var sA))
                            adjacency[c.node.Id] = sA = new HashSet<string>();
                        sA.Add(cand.n.Id);
                        if (!adjacency.TryGetValue(cand.n.Id, out var sB))
                            adjacency[cand.n.Id] = sB = new HashSet<string>();
                        sB.Add(c.node.Id);
                        bridgeCount++;
                    }
                    if (inRange.Count == 0)
                    {
                        var closest = ranked.FirstOrDefault();
                        if (closest != null)
                            bridgeDiag.Add(
                                $"{c.node.Id}: no bridge — closest non-adjacent node " +
                                $"{closest.n.Id} is {closest.d:F2}m away " +
                                $"(>bridgingRadius={bridgingRadius:F2}m). " +
                                $"Increase bridgingRadius if you want this connection.");
                        else
                            bridgeDiag.Add(
                                $"{c.node.Id}: no bridge candidates — graph has no other " +
                                "non-collinear node to brace to (are you feeding the FULL " +
                                "structure, not a single isolated member?)");
                    }
                    else
                        bridgeDiag.Add(
                            $"{c.node.Id}: bridged to " +
                            string.Join(",", inRange.Select(x => $"{x.n.Id}({x.d:F2}m)")));
                }
            }
            // Surface bridging diagnostics as warnings (visible on the
            // component itself) so the user sees them even without reading
            // the long Note string.
            foreach (var d in bridgeDiag)
                if (d.Contains("no bridge")) res.Warnings.Add(d);

            int snapReuses = dedup.Count(c => !c.isNew);
            int newCuts = dedup.Count(c => c.isNew);
            string stockNote = string.Join("+", picked.Select(s => $"{s.id}:{s.len:F2}m"));
            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "splitMemberByStockLength",
                RuleName = "Split Member by Stock Length",
                TargetId = m.Id,
                Note =
                    $"split {m.Id} (L={L:F2}m) into {chain.Count - 1} pieces using " +
                    $"{picked.Count} stock items [{stockNote}]; final offcut " +
                    $"{(cumulative - L):F2}m returns to inventory\n" +
                    $"endpoints PRESERVED: {a.Id}({a.X:F2},{a.Y:F2},{a.Z:F2}) " +
                    $"and {b.Id}({b.X:F2},{b.Y:F2},{b.Z:F2})\n" +
                    $"cuts: {snapReuses} snapped to existing nodes, {newCuts} new node(s) created\n" +
                    $"bridges: {bridgeCount} added (radius={bridgingRadius:F2}m, max={maxBridges}/node)\n" +
                    (bridgeDiag.Count == 0 ? "" : "  " + string.Join("\n  ", bridgeDiag)),
                AddedNodeIds = addedNodeIds,
                AddedMemberIds = addedMemberIds,
                RemovedMemberIds = new List<string> { m.Id },
            };
            return res;
        }

        // Project node p onto the line a→b, return parameter t (0 at a, 1 at b).
        private static double ProjectT(BridgeNode a, BridgeNode b, BridgeNode p, double L)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double px = p.X - a.X, py = p.Y - a.Y, pz = p.Z - a.Z;
            double L2 = L * L;
            if (L2 < 1e-12) return 0;
            return (px * dx + py * dy + pz * dz) / L2;
        }

        // A unit-ish vector perpendicular to axis (ux,uy,uz): axis × worldZ,
        // falling back to axis × worldX when the axis is (anti)parallel to Z.
        private static void CrossPerp(double ux, double uy, double uz,
            out double vx, out double vy, out double vz)
        {
            // axis × (0,0,1) = (uy, -ux, 0)
            vx = uy; vy = -ux; vz = 0;
            if (vx * vx + vy * vy + vz * vz < 1e-9)
            {
                // axis ∥ Z → use axis × (1,0,0) = (0, uz, -uy)
                vx = 0; vy = uz; vz = -uy;
            }
        }

        // Interior angle (degrees) opposite side `opp`, between sides `adj1`,
        // `adj2`, via the law of cosines. Returns 0 for degenerate triangles.
        private static double AngleDeg(double adj1, double adj2, double opp)
        {
            double denom = 2.0 * adj1 * adj2;
            if (denom < 1e-12) return 0;
            double cos = (adj1 * adj1 + adj2 * adj2 - opp * opp) / denom;
            cos = Math.Max(-1.0, Math.Min(1.0, cos));
            return Math.Acos(cos) * 180.0 / Math.PI;
        }

        // ---------------- 2. addConnectionPoint ------------------------
        // Add ONE new node that is connected to the target
        // member's two endpoints (a, b) PLUS nearby EXISTING nodes, and sits
        // at the CENTROID of those anchors. Because the centroid lies inside the
        // anchors' convex hull, the new node never cantilevers into empty space;
        // because the anchors are chosen to be non-coplanar, the new node is
        // braced in 3-D (a spatial hub, not a flat fan). The target member is
        // NOT cut and NOT moved (delta members = +new, −0); no node is placed
        // ON the member itself.
        //   anchors = a, b + nearest existing nodes (≥1 chosen to break
        //             coplanarity so the hub is genuinely spatial).
        //   N       = centroid(anchors)  → interior, never sticks out.
        //   stock   = every N→anchor member must fit one stock piece (offcut
        //             returns); reject if the region is larger than stock can span.

        public static GrammarRuleResult AddConnectionPoint(
            BridgeGraph g, string memberId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            var m = g.Members.FirstOrDefault(x => x.Id == memberId);
            if (m == null) { res.Warnings.Add($"member {memberId} not found"); return res; }
            var a = g.Nodes.FirstOrDefault(n => n.Id == m.From);
            var b = g.Nodes.FirstOrDefault(n => n.Id == m.To);
            if (a == null || b == null)
            {
                res.Warnings.Add("member endpoints missing"); return res;
            }
            double L = Dist(a, b);
            if (L < 1e-6) { res.Warnings.Add("member is zero-length"); return res; }
            if (stock == null || stock.Count == 0)
            {
                res.Warnings.Add(
                    "addConnectionPoint is stock-driven — connect Stock JSON into Apply " +
                    "Grammar Rule's Stock (S) input so new connecting members are buildable.");
                return res;
            }

            // -------- params --------
            // How many EXISTING nodes (beyond a, b) the new hub also connects to.
            // ≥2 recommended so the anchor set spans 3-D.
            int extraAnchors = Math.Max(1, Math.Min(5,
                p["extraAnchors"]?.Value<int?>() ?? 2));

            double maxStock = stock.Max(s => s.Length);
            var endpointIds = new HashSet<string> { a.Id, b.Id };
            var mid = new BridgeNode { X = (a.X + b.X) * 0.5, Y = (a.Y + b.Y) * 0.5, Z = (a.Z + b.Z) * 0.5 };

            // -------- gather candidate existing nodes near the member --------
            // Sorted by distance to the midspan; the hub will connect to these.
            var nearby = g.Nodes
                .Where(n => !endpointIds.Contains(n.Id))
                .Select(n => new { n, d = Dist(n, mid) })
                .Where(x => x.d > 1e-6 && x.d <= maxStock + 1e-6)
                .OrderBy(x => x.d)
                .Select(x => x.n)
                .ToList();
            if (nearby.Count == 0)
            {
                res.Warnings.Add(
                    "no existing node within one stock length of this member's midspan — " +
                    "nothing to connect a new hub to here (feed the full structure, or this " +
                    "region simply does not need densifying).");
                return res;
            }

            // -------- choose anchors: a, b + P1 (nearest, non-collinear) + P2
            //          (nearest that breaks coplanarity) + further nearest ----
            double abx = b.X - a.X, aby = b.Y - a.Y, abz = b.Z - a.Z;
            var anchors = new List<BridgeNode> { a, b };

            // P1 — nearest node not collinear with the member.
            BridgeNode p1 = null;
            foreach (var n in nearby)
            {
                double pax = n.X - a.X, pay = n.Y - a.Y, paz = n.Z - a.Z;
                double cx = aby * paz - abz * pay, cy = abz * pax - abx * paz, cz = abx * pay - aby * pax;
                if (cx * cx + cy * cy + cz * cz > 1e-9) { p1 = n; break; }   // non-collinear
            }
            if (p1 == null)
            {
                res.Warnings.Add("all nearby nodes are collinear with the member — cannot form a brace.");
                return res;
            }
            anchors.Add(p1);

            // Plane normal of (a, b, P1) — used to find a node OFF that plane so
            // the anchor set is non-coplanar (→ the hub is braced in 3-D).
            double n1x = aby * (p1.Z - a.Z) - abz * (p1.Y - a.Y);
            double n1y = abz * (p1.X - a.X) - abx * (p1.Z - a.Z);
            double n1z = abx * (p1.Y - a.Y) - aby * (p1.X - a.X);
            double n1len = Math.Sqrt(n1x * n1x + n1y * n1y + n1z * n1z);
            BridgeNode p2 = null;
            if (n1len > 1e-9)
            {
                n1x /= n1len; n1y /= n1len; n1z /= n1len;
                // Pick the nearby node with the LARGEST off-plane distance — the
                // most non-coplanar one — rather than the first past a fixed
                // threshold. No magic cutoff; reject only if the best is
                // numerically coplanar.
                double bestOff = 1e-9;
                foreach (var n in nearby)
                {
                    if (n.Id == p1.Id) continue;
                    double off = Math.Abs((n.X - a.X) * n1x + (n.Y - a.Y) * n1y + (n.Z - a.Z) * n1z);
                    if (off > bestOff) { bestOff = off; p2 = n; }
                }
            }
            if (p2 == null)
            {
                res.Warnings.Add(
                    "every nearby node is coplanar with the member and P1 — cannot form an " +
                    "out-of-plane (spatial) hub here; this region is essentially planar.");
                return res;
            }
            anchors.Add(p2);

            // Optionally add further nearest nodes up to extraAnchors total extras.
            foreach (var n in nearby)
            {
                if (anchors.Count >= 2 + extraAnchors) break;
                if (n.Id == p1.Id || n.Id == p2.Id) continue;
                anchors.Add(n);
            }

            // -------- N = centroid of anchors (interior → never sticks out) --------
            var nn = new BridgeNode
            {
                Id = NextNodeId(g, "CP", g.Nodes.Count),
                X = Math.Round(anchors.Average(n => n.X), 4),
                Y = Math.Round(anchors.Average(n => n.Y), 4),
                Z = Math.Round(anchors.Average(n => n.Z), 4),
            };
            // Guard: centroid must not coincide with an existing node.
            if (g.Nodes.Any(n => Dist(n, nn) < 1e-3))
            {
                res.Warnings.Add("hub centroid coincides with an existing node — no distinct hub to add.");
                return res;
            }

            // -------- stock feasibility: every N→anchor must fit one piece --------
            var connNote = new List<string>();
            var conns = new List<(BridgeNode t, double d, StockItem fit)>();
            foreach (var t in anchors)
            {
                double d = Dist(t, nn);
                if (d < 1e-6) continue;
                if (d > maxStock + 1e-6)
                {
                    res.Warnings.Add(
                        $"hub→{t.Id} is {d:F2}m > longest stock {maxStock:F2}m — the anchor " +
                        "region is larger than stock can span. Pick a denser region or add longer stock.");
                    return res;
                }
                var fit = stock.Where(s => s.Length >= d - 1e-6).OrderBy(s => s.Length - d).FirstOrDefault();
                conns.Add((t, d, fit));
            }
            // Must connect to BOTH a and b (brace the member) and ≥1 other node.
            bool hasA = conns.Any(c => c.t.Id == a.Id);
            bool hasB = conns.Any(c => c.t.Id == b.Id);
            if (!hasA || !hasB || conns.Count < 3)
            {
                res.Warnings.Add("could not connect the hub to both member endpoints plus another node within stock reach.");
                return res;
            }

            // -------- commit: M untouched, add hub + spoke members --------
            g.Nodes.Add(nn);
            var addedMemberIds = new List<string>();
            int seq = 0;
            foreach (var c in conns)
            {
                string bid = $"{m.Id}.cp{++seq}";
                g.Members.Add(new BridgeMember
                {
                    Id = bid, From = nn.Id, To = c.t.Id,
                    Length = Round(c.d), AssignedStockId = c.fit?.Id,
                });
                addedMemberIds.Add(bid);
                connNote.Add($"{c.t.Id}({c.d:F2}m→{c.fit?.Id ?? "?"})");
            }

            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "addConnectionPoint",
                RuleName = "Add Connection Point",
                TargetId = m.Id,
                Note =
                    $"added interior hub {nn.Id} (NOT on {m.Id}; M kept intact, NOT cut)\n" +
                    $"{m.Id} PRESERVED: {a.Id}({a.X:F2},{a.Y:F2},{a.Z:F2}) and {b.Id}({b.X:F2},{b.Y:F2},{b.Z:F2})\n" +
                    $"hub = centroid of {anchors.Count} anchors → lies INSIDE their hull (never cantilevers out)\n" +
                    $"spatial check: P2={p2.Id} is off plane(a,b,{p1.Id}) → non-coplanar anchors → 3-D braced\n" +
                    $"connected to: [{string.Join(", ", connNote)}] (each ≤ one stock piece, offcut returns)",
                AddedNodeIds = new List<string> { nn.Id },
                AddedMemberIds = addedMemberIds,
                RemovedMemberIds = new List<string>(),  // M not removed
            };
            return res;
        }

        // ---------------- 3. triangulateBay ----------------------------
        // Add exactly ONE new edge (no new node) that
        // CLOSES A TRIANGLE: the target member's endpoint a (and b) typically
        // share a common neighbour with another node that is not yet directly
        // connected to it. We find the pair (one of a/b, and a second-ring node
        // that shares a common neighbour) whose direct connection (a) does not
        // already exist, (b) fits one stock piece, and (c) closes the shortest
        // valid triangle. Pure topology repair — distinct from addConnectionPoint
        // (which adds a NEW node) and from split (which subdivides).

        public static GrammarRuleResult TriangulateBay(
            BridgeGraph g, string memberId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            var m = g.Members.FirstOrDefault(x => x.Id == memberId);
            if (m == null) { res.Warnings.Add($"member {memberId} not found"); return res; }
            var a = g.Nodes.FirstOrDefault(n => n.Id == m.From);
            var b = g.Nodes.FirstOrDefault(n => n.Id == m.To);
            if (a == null || b == null)
            {
                res.Warnings.Add("member endpoints missing"); return res;
            }
            if (stock == null || stock.Count == 0)
            {
                res.Warnings.Add("triangulateBay is stock-driven — connect Stock JSON.");
                return res;
            }
            double maxStock = stock.Max(s => s.Length);
            var byId = g.Nodes.ToDictionary(n => n.Id);

            // adjacency + existing-edge set
            var adj = new Dictionary<string, HashSet<string>>();
            var edge = new HashSet<string>();
            foreach (var mm in g.Members)
            {
                if (!adj.TryGetValue(mm.From, out var s1)) adj[mm.From] = s1 = new HashSet<string>();
                if (!adj.TryGetValue(mm.To, out var s2)) adj[mm.To] = s2 = new HashSet<string>();
                s1.Add(mm.To); s2.Add(mm.From);
                edge.Add($"{mm.From}|{mm.To}"); edge.Add($"{mm.To}|{mm.From}");
            }

            // Candidate triangle-closing edge (u, w): u ∈ {a, b}; w shares a
            // common neighbour with u (so u-w + the two existing edges form a
            // triangle); u-w not already an edge; |u-w| ≤ one stock piece.
            string bestU = null, bestW = null, bestVia = null;
            double bestD = double.MaxValue;
            foreach (var u in new[] { a, b })
            {
                if (!adj.TryGetValue(u.Id, out var uN)) continue;
                foreach (var viaId in uN)               // common neighbour 'via'
                {
                    if (!adj.TryGetValue(viaId, out var viaN)) continue;
                    foreach (var wId in viaN)           // w is two steps from u, via 'via'
                    {
                        if (wId == u.Id) continue;
                        if (edge.Contains($"{u.Id}|{wId}")) continue;   // already connected
                        if (!byId.TryGetValue(wId, out var w)) continue;
                        double d = Dist(u, w);
                        if (d < 1e-6 || d > maxStock + 1e-6) continue;  // must fit one piece
                        if (d < bestD) { bestD = d; bestU = u.Id; bestW = wId; bestVia = viaId; }
                    }
                }
            }
            if (bestU == null)
            {
                res.Warnings.Add(
                    "no triangle to close near this member within one stock length — " +
                    "either the region is already triangulated or the gap is larger than stock.");
                return res;
            }
            var fit = stock.Where(s => s.Length >= bestD - 1e-6).OrderBy(s => s.Length - bestD).FirstOrDefault();
            string id = $"TR.{memberId}";
            g.Members.Add(new BridgeMember { Id = id, From = bestU, To = bestW, Length = Round(bestD), AssignedStockId = fit?.Id });
            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId   = "triangulateBay",
                RuleName = "Triangulate (close a triangle)",
                TargetId = memberId,
                Note     = $"closed triangle {bestU}-{bestVia}-{bestW} by adding edge " +
                           $"{bestU}→{bestW} ({bestD:F2}m → {fit?.Id ?? "?"}); no new node, M intact",
                AddedMemberIds = new List<string> { id },
            };
            return res;
        }

        // ---------------- 4. shiftNodeToMatchStock --------------------
        // Move a node in any 3-D direction. A deterministic pattern search finds
        // the position that best aligns incident members to available stock lengths
        // (each member length → as close as possible to some stock piece, zero
        // offcut), subject to: every incident member ≤ longest stock, no node
        // collision, supports never move. "Along the bar" and "perpendicular
        // (kink off the straight line)" both emerge naturally — whichever
        // direction improves the stock fit. Target = a NODE id, or a MEMBER id
        // (then its non-support endpoint(s) are the candidates).
        public static GrammarRuleResult ShiftNodeToMatchStock(
            BridgeGraph g, string targetId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            if (stock == null || stock.Count == 0)
            {
                res.Warnings.Add("shiftNodeToMatchStock is stock-driven — connect Stock JSON.");
                return res;
            }
            double minStock = stock.Min(s => s.Length);
            double maxStock = stock.Max(s => s.Length);
            double collisionTol = 0.5 * minStock;

            // resolve target → candidate node(s) to move
            var candidates = new List<BridgeNode>();
            var asNode = g.Nodes.FirstOrDefault(n => n.Id == targetId);
            if (asNode != null)
            {
                if (asNode.IsSupport == true) { res.Warnings.Add($"node {targetId} is a support — refused."); return res; }
                candidates.Add(asNode);
            }
            else
            {
                var asMember = g.Members.FirstOrDefault(x => x.Id == targetId);
                if (asMember == null) { res.Warnings.Add($"target {targetId} not found (node or member id)."); return res; }
                var a = g.Nodes.FirstOrDefault(n => n.Id == asMember.From);
                var b = g.Nodes.FirstOrDefault(n => n.Id == asMember.To);
                if (a != null && a.IsSupport != true) candidates.Add(a);
                if (b != null && b.IsSupport != true) candidates.Add(b);
                if (candidates.Count == 0) { res.Warnings.Add("both endpoints are supports — refused."); return res; }
            }

            var byId = g.Nodes.ToDictionary(n => n.Id);
            BridgeNode bestNode = null; double bestX = 0, bestY = 0, bestZ = 0, bestGain = 0; double bestObj = 0, bestStart = 0;

            foreach (var node in candidates)
            {
                var incident = g.Members.Where(m => m.From == node.Id || m.To == node.Id).ToList();
                if (incident.Count == 0) continue;
                var neighIds = incident.Select(m => m.From == node.Id ? m.To : m.From).ToList();

                // objective at a trial position: Σ (member length's distance to nearest
                // stock length); INFEASIBLE (huge) if any incident member > longest stock
                // or the position collides with another node.
                Func<double, double, double, double> obj = (x, y, z) =>
                {
                    double f = 0;
                    foreach (var nid in neighIds)
                    {
                        if (!byId.TryGetValue(nid, out var q)) continue;
                        double dx = q.X - x, dy = q.Y - y, dz = q.Z - z;
                        double L = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (L > maxStock + 1e-6) return 1e9;            // unbuildable
                        double best = double.MaxValue;
                        foreach (var s in stock) best = Math.Min(best, Math.Abs(L - s.Length));
                        f += best;
                    }
                    foreach (var n in g.Nodes)
                    {
                        if (n.Id == node.Id) continue;
                        double dx = n.X - x, dy = n.Y - y, dz = n.Z - z;
                        if (dx * dx + dy * dy + dz * dz < collisionTol * collisionTol) return 1e9;  // collision
                    }
                    return f;
                };

                // deterministic coordinate pattern search (no RNG).
                double px = node.X, py = node.Y, pz = node.Z;  // current pos
                double fcur = obj(node.X, node.Y, node.Z);
                double start = fcur;
                double step = (maxStock - minStock) > 1e-6 ? (maxStock - minStock) : minStock;
                double[][] dirs = {
                    new[]{ 1.0,0,0}, new[]{-1.0,0,0}, new[]{0,1.0,0}, new[]{0,-1.0,0}, new[]{0,0,1.0}, new[]{0,0,-1.0}
                };
                for (int iter = 0; iter < 80 && step > collisionTol * 0.25; iter++)
                {
                    bool improved = false;
                    foreach (var d in dirs)
                    {
                        double tx = px + d[0] * step, ty = py + d[1] * step, tz = pz + d[2] * step;
                        double ft = obj(tx, ty, tz);
                        if (ft < fcur - 1e-9) { px = tx; py = ty; pz = tz; fcur = ft; improved = true; }
                    }
                    if (!improved) step *= 0.5;
                }

                double moved = Math.Sqrt((px - node.X) * (px - node.X) + (py - node.Y) * (py - node.Y) + (pz - node.Z) * (pz - node.Z));
                double gain = start - fcur;
                if (gain > 1e-6 && moved > 1e-4 && gain > bestGain)
                {
                    bestGain = gain; bestNode = node;
                    bestX = Math.Round(px, 4); bestY = Math.Round(py, 4); bestZ = Math.Round(pz, 4);
                    bestObj = fcur; bestStart = start;
                }
            }

            if (bestNode == null)
            {
                res.Warnings.Add(
                    "node already stock-optimal (or any feasible move overstretches a member / collides) — " +
                    "no better position within stock limits. (Member far too long? use split instead.)");
                return res;
            }

            // commit
            bestNode.X = bestX; bestNode.Y = bestY; bestNode.Z = bestZ;
            var connNote = new List<string>();
            double longest = 0;
            foreach (var mm in g.Members)
            {
                if (mm.From != bestNode.Id && mm.To != bestNode.Id) continue;
                if (!byId.TryGetValue(mm.From, out var pp)) continue;
                if (!byId.TryGetValue(mm.To, out var qq)) continue;
                mm.Length = Round(Dist(pp, qq));
                var fit = stock.Where(s => s.Length >= mm.Length - 1e-6).OrderBy(s => s.Length - mm.Length).FirstOrDefault();
                mm.AssignedStockId = fit?.Id;
                longest = Math.Max(longest, mm.Length);
                connNote.Add($"{mm.Id}={mm.Length:F2}m→{fit?.Id ?? "?"}");
            }

            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId   = "shiftNodeToMatchStock",
                RuleName = "Shift Node to Match Stock",
                TargetId = bestNode.Id,
                Note     =
                    $"moved node {bestNode.Id} freely in 3D to best fit stock (stock-mismatch {bestStart:F2}→{bestObj:F2}m); " +
                    $"direction emerges from inventory — along bar and/or off-axis kink\n" +
                    $"all incident ≤ longest stock {maxStock:F2}m, no collision (≥ {collisionTol:F2}m), longest now {longest:F2}m\n" +
                    $"incident: [{string.Join(", ", connNote)}]",
            };
            return res;
        }

        // ---------------- 5. removeConnectionPoint ---------------------
        // Remove a degree-2 node and merge its two members into one.
        // Refuse the merge if the new member exceeds the longest stock piece.
        // Target = a NODE id.
        public static GrammarRuleResult RemoveConnectionPoint(
            BridgeGraph g, string nodeId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            var adj = g.Members.Where(m => m.From == nodeId || m.To == nodeId).ToList();
            if (adj.Count != 2)
            {
                res.Warnings.Add($"node {nodeId} degree is {adj.Count} (need exactly 2 to merge)");
                return res;
            }
            Func<BridgeMember, string> other = m => m.From == nodeId ? m.To : m.From;
            var ends = new[] { other(adj[0]), other(adj[1]) };
            if (g.Members.Any(m =>
                (m.From == ends[0] && m.To == ends[1]) ||
                (m.From == ends[1] && m.To == ends[0])))
            {
                res.Warnings.Add("the two far endpoints are already directly connected — merge would duplicate an edge");
                return res;
            }
            var a = g.Nodes.FirstOrDefault(n => n.Id == ends[0]);
            var b = g.Nodes.FirstOrDefault(n => n.Id == ends[1]);
            if (a == null || b == null) { res.Warnings.Add("endpoints missing"); return res; }

            double mergedLen = Dist(a, b);
            double maxStock = (stock != null && stock.Count > 0) ? stock.Max(s => s.Length) : double.MaxValue;
            if (mergedLen > maxStock + 1e-6)
            {
                res.Warnings.Add(
                    $"merging would create a {mergedLen:F2}m member > longest stock {maxStock:F2}m " +
                    $"(unbuildable as one piece — it would just need splitting again). Refused.");
                return res;
            }

            g.Members.RemoveAll(m => m.Id == adj[0].Id || m.Id == adj[1].Id);
            g.Nodes.RemoveAll(n => n.Id == nodeId);
            var newId = $"{adj[0].Id}+{adj[1].Id}";
            var fit = (stock != null && stock.Count > 0)
                ? stock.Where(s => s.Length >= mergedLen - 1e-6).OrderBy(s => s.Length - mergedLen).FirstOrDefault()
                : null;
            g.Members.Add(new BridgeMember
            {
                Id = newId, From = a.Id, To = b.Id,
                Length = Round(mergedLen), AssignedStockId = fit?.Id,
            });
            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "removeConnectionPoint",
                RuleName = "Remove Connection Point",
                TargetId = nodeId,
                Note = $"removed node {nodeId}, merged {adj[0].Id}+{adj[1].Id} → {newId} " +
                       $"({mergedLen:F2}m ≤ longest stock {maxStock:F2}m → {fit?.Id ?? "n/a"})",
                AddedMemberIds = new List<string> { newId },
                RemovedMemberIds = new List<string> { adj[0].Id, adj[1].Id },
            };
            return res;
        }

        // ---------------- 6. densifyWithShortStock ---------------------
        // Subdivide the target member into the FINEST
        // granularity the SHORTEST stock allows, to use up short pieces and
        // increase local density. Distinct from split: split minimises joints
        // (longest-first, only when a member is too long for any piece);
        // densify maximises density (shortest-first) and may apply to any
        // member longer than the shortest piece. Each new interior node is
        // connected to nearby structure using split's bridging logic.
        // These local connections do not establish whole-structure stability.
        public static GrammarRuleResult DensifyWithShortStock(
            BridgeGraph g, string memberId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            var m = g.Members.FirstOrDefault(x => x.Id == memberId);
            if (m == null) { res.Warnings.Add($"member {memberId} not found"); return res; }
            var a = g.Nodes.FirstOrDefault(n => n.Id == m.From);
            var b = g.Nodes.FirstOrDefault(n => n.Id == m.To);
            if (a == null || b == null) { res.Warnings.Add("endpoints missing"); return res; }
            if (stock == null || stock.Count == 0)
            {
                res.Warnings.Add("densifyWithShortStock is stock-driven — connect Stock JSON.");
                return res;
            }
            double L = Dist(a, b);
            double minStock = stock.Min(s => s.Length);
            double maxStock = stock.Max(s => s.Length);
            if (L <= minStock + 1e-6)
            {
                res.Warnings.Add(
                    $"member {L:F2}m is already ≤ the shortest stock ({minStock:F2}m) — " +
                    "cannot densify it with shorter pieces.");
                return res;
            }
            var shortId = stock.First(s => Math.Abs(s.Length - minStock) < 1e-9).Id;

            // Finest granularity: each segment ≤ shortest piece.
            int nPieces = (int)Math.Ceiling(L / minStock - 1e-9);
            nPieces = Math.Max(2, nPieces);
            double snapRadius = 0.5 * minStock;

            // cut nodes (snap to existing where possible)
            var usedSnaps = new HashSet<string> { a.Id, b.Id };
            var cutNodes = new List<(BridgeNode node, bool isNew, double t)>();
            int seed = g.Nodes.Count;
            for (int i = 1; i < nPieces; i++)
            {
                double t = (double)i / nPieces;
                double ix = a.X + (b.X - a.X) * t, iy = a.Y + (b.Y - a.Y) * t, iz = a.Z + (b.Z - a.Z) * t;
                BridgeNode snap = null; double bd = double.MaxValue;
                foreach (var n in g.Nodes)
                {
                    if (usedSnaps.Contains(n.Id)) continue;
                    double dx = n.X - ix, dy = n.Y - iy, dz = n.Z - iz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d <= snapRadius && d < bd) { bd = d; snap = n; }
                }
                if (snap != null) { usedSnaps.Add(snap.Id); cutNodes.Add((snap, false, ProjectT(a, b, snap, L))); }
                else
                {
                    var nn = new BridgeNode { Id = NextNodeId(g, "DN", seed++), X = Round(ix), Y = Round(iy), Z = Round(iz) };
                    g.Nodes.Add(nn); usedSnaps.Add(nn.Id); cutNodes.Add((nn, true, t));
                }
            }
            cutNodes.Sort((x, y) => x.t.CompareTo(y.t));
            var dedup = cutNodes.Where(c => c.t > 1e-4 && c.t < 1.0 - 1e-4).ToList();
            if (dedup.Count == 0)
            {
                foreach (var c in cutNodes) if (c.isNew) g.Nodes.RemoveAll(n => n.Id == c.node.Id);
                res.Warnings.Add("no interior subdivision survived (all collapsed to endpoints).");
                return res;
            }

            // replace member with the dense chain
            g.Members.RemoveAll(x => x.Id == m.Id);
            var chain = new List<BridgeNode> { a }; chain.AddRange(dedup.Select(c => c.node)); chain.Add(b);
            var addedMemberIds = new List<string>();
            var addedNodeIds = dedup.Where(c => c.isNew).Select(c => c.node.Id).ToList();
            for (int i = 0; i < chain.Count - 1; i++)
            {
                var nid = $"{m.Id}.d{i + 1}";
                g.Members.Add(new BridgeMember { Id = nid, From = chain[i].Id, To = chain[i + 1].Id, Length = Round(Dist(chain[i], chain[i + 1])), AssignedStockId = shortId });
                addedMemberIds.Add(nid);
            }

            // triangulate each new interior node into nearby non-collinear structure
            double axx = (b.X - a.X) / L, axy = (b.Y - a.Y) / L, axz = (b.Z - a.Z) / L;
            int bridges = 0;
            var adjacency = new Dictionary<string, HashSet<string>>();
            foreach (var mm in g.Members)
            {
                if (!adjacency.TryGetValue(mm.From, out var s1)) adjacency[mm.From] = s1 = new HashSet<string>();
                if (!adjacency.TryGetValue(mm.To, out var s2)) adjacency[mm.To] = s2 = new HashSet<string>();
                s1.Add(mm.To); s2.Add(mm.From);
            }
            int bseed = 0;
            foreach (var c in dedup)
            {
                if (!c.isNew) continue;
                adjacency.TryGetValue(c.node.Id, out var seen);
                seen = seen ?? new HashSet<string>();
                var cand = g.Nodes
                    .Where(n => n.Id != c.node.Id && !seen.Contains(n.Id))
                    .Select(n => new { n, d = Dist(n, c.node) })
                    .Where(x => x.d > 1e-6 && x.d <= maxStock + 1e-6)
                    // perpendicularity to member axis (1 = perpendicular, 0 = parallel)
                    .Select(x => new { x.n, x.d, perp = 1.0 - Math.Abs(((x.n.X - c.node.X) * axx + (x.n.Y - c.node.Y) * axy + (x.n.Z - c.node.Z) * axz) / x.d) })
                    .Where(x => x.perp > 0.05)            // reject near-parallel (numerically ~collinear)
                    .OrderByDescending(x => x.perp).ThenBy(x => x.d)
                    .Take(2).ToList();
                foreach (var bnd in cand)
                {
                    var fit = stock.Where(s => s.Length >= bnd.d - 1e-6).OrderBy(s => s.Length - bnd.d).FirstOrDefault();
                    var bid = $"{m.Id}.db{++bseed}";
                    g.Members.Add(new BridgeMember { Id = bid, From = c.node.Id, To = bnd.n.Id, Length = Round(bnd.d), AssignedStockId = fit?.Id });
                    addedMemberIds.Add(bid);
                    if (!adjacency.TryGetValue(c.node.Id, out var sa)) adjacency[c.node.Id] = sa = new HashSet<string>();
                    sa.Add(bnd.n.Id);
                    bridges++;
                }
            }

            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "densifyWithShortStock",
                RuleName = "Densify with Short Stock",
                TargetId = m.Id,
                Note = $"densified {m.Id} (L={L:F2}m) into {chain.Count - 1} segments of ≈{L / nPieces:F2}m " +
                       $"using shortest stock {shortId} ({minStock:F2}m); {addedNodeIds.Count} new node(s), " +
                       $"{bridges} triangulating brace(s) added",
                AddedNodeIds = addedNodeIds,
                AddedMemberIds = addedMemberIds,
                RemovedMemberIds = new List<string> { m.Id },
            };
            return res;
        }

        // ---------------- 7. fanOutLoadPath ---------------------------
        // From ONE existing node (the target member's
        // lower-degree, non-support endpoint — the one most in need of extra
        // load paths) fan out up to fanCount NEW edges to nearby EXISTING nodes
        // that are not yet connected to it, each ≤ one stock piece. No new node
        // is created. Distinct from triangulateBay (which adds a single triangle-
        // closing edge) — fanOut sprays several edges from one hub to spread the
        // force path / branch.
        public static GrammarRuleResult FanOutLoadPath(
            BridgeGraph g, string memberId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            var m = g.Members.FirstOrDefault(x => x.Id == memberId);
            if (m == null) { res.Warnings.Add($"member {memberId} not found"); return res; }
            var a = g.Nodes.FirstOrDefault(n => n.Id == m.From);
            var b = g.Nodes.FirstOrDefault(n => n.Id == m.To);
            if (a == null || b == null) { res.Warnings.Add("endpoints missing"); return res; }
            if (stock == null || stock.Count == 0)
            {
                res.Warnings.Add("fanOutLoadPath is stock-driven — connect Stock JSON.");
                return res;
            }
            double maxStock = stock.Max(s => s.Length);

            // adjacency + degree
            var adj = new Dictionary<string, HashSet<string>>();
            foreach (var mm in g.Members)
            {
                if (!adj.TryGetValue(mm.From, out var s1)) adj[mm.From] = s1 = new HashSet<string>();
                if (!adj.TryGetValue(mm.To, out var s2)) adj[mm.To] = s2 = new HashSet<string>();
                s1.Add(mm.To); s2.Add(mm.From);
            }
            // Anchor = the non-support endpoint with FEWER connections (more in
            // need of bracing); tie → a.
            int degA = adj.TryGetValue(a.Id, out var aa) ? aa.Count : 0;
            int degB = adj.TryGetValue(b.Id, out var bb) ? bb.Count : 0;
            BridgeNode anchor = (b.IsSupport == true) ? a
                              : (a.IsSupport == true) ? b
                              : (degB < degA ? b : a);
            if (anchor.IsSupport == true)
            {
                res.Warnings.Add("both endpoints are supports — refused.");
                return res;
            }
            adj.TryGetValue(anchor.Id, out var seen);
            seen = seen ?? new HashSet<string>();

            int want = Math.Max(2, Math.Min(4, p["fanCount"]?.Value<int?>() ?? 3));
            var picks = g.Nodes
                .Where(n => n.Id != anchor.Id && !seen.Contains(n.Id))
                .Select(n => new { n, d = Dist(n, anchor) })
                .Where(x => x.d > 1e-6 && x.d <= maxStock + 1e-6)   // each fan edge fits one piece
                .OrderBy(x => x.d)
                .Take(want)
                .ToList();
            if (picks.Count == 0)
            {
                res.Warnings.Add(
                    $"no unconnected node within one stock length ({maxStock:F2}m) of {anchor.Id} — " +
                    "nothing to fan to here.");
                return res;
            }
            var added = new List<string>();
            var note = new List<string>();
            int seq = 0;
            foreach (var pk in picks)
            {
                var fit = stock.Where(s => s.Length >= pk.d - 1e-6).OrderBy(s => s.Length - pk.d).FirstOrDefault();
                var id = $"FN.{memberId}.{++seq}";
                g.Members.Add(new BridgeMember { Id = id, From = anchor.Id, To = pk.n.Id, Length = Round(pk.d), AssignedStockId = fit?.Id });
                added.Add(id);
                note.Add($"{pk.n.Id}({pk.d:F2}m→{fit?.Id ?? "?"})");
            }
            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "fanOutLoadPath",
                RuleName = "Fan Out Load Path",
                TargetId = anchor.Id,
                Note = $"fanned {picks.Count} new edges from {anchor.Id} (degree was " +
                       $"{(anchor.Id == a.Id ? degA : degB)}); no new node. to: [{string.Join(", ", note)}]",
                AddedMemberIds = added,
            };
            return res;
        }

        // ---------------- 8. removeRedundantMember --------------------
        // A member is "redundant" only if removing it leaves BOTH
        // its endpoints still connected (degree ≥ 2 each after removal).
        // This degree guard preserves local connectivity; it does not establish
        // whether the member is structurally redundant or the result is stable.
        public static GrammarRuleResult RemoveRedundantMember(
            BridgeGraph g, string memberId, JObject p)
        {
            var res = new GrammarRuleResult();
            var m = g.Members.FirstOrDefault(x => x.Id == memberId);
            if (m == null) { res.Warnings.Add($"member {memberId} not found"); return res; }

            int degFrom = g.Members.Count(x => x.From == m.From || x.To == m.From);
            int degTo   = g.Members.Count(x => x.From == m.To   || x.To == m.To);
            // After removing this member each endpoint loses one connection.
            if (degFrom - 1 < 2 || degTo - 1 < 2)
            {
                res.Warnings.Add(
                    $"not redundant — removing {m.Id} would drop node " +
                    $"{(degFrom - 1 < 2 ? m.From + $" to degree {degFrom - 1}" : m.To + $" to degree {degTo - 1}")} " +
                    "(< 2). This member holds a node in the structure; refused.");
                return res;
            }

            g.Members.RemoveAll(x => x.Id == m.Id);
            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "removeRedundantMember",
                RuleName = "Remove Redundant Member",
                TargetId = m.Id,
                Note = $"removed {m.Id} — both endpoints stay connected " +
                       $"({m.From}: {degFrom}→{degFrom - 1}, {m.To}: {degTo}→{degTo - 1}, both ≥ 2)",
                RemovedMemberIds = new List<string> { m.Id },
            };
            return res;
        }

        // ---------------- 9. articulateChainToStock -------------------
        // PURE ROTATION battery — meant to be chained AFTER
        // splitMemberByStockLength. Split leaves a straight chain of sub-bars
        // (M0.s1, M0.s2 …) tiled along the original line, each tile = a stock
        // piece's length EXCEPT the final tile, whose stock piece had an offcut
        // that "returned to inventory". This rule instead BUILDS every sub-bar
        // at its FULL assigned stock length (spending that offcut), so the chain
        // total Σ(stock) > span D. The surplus (= total offcut, the "slack") is
        // the only thing that lets the chain fold: with both original endpoints
        // PINNED, the interior split nodes rotate off the straight line into a
        // shallow arch. Fold direction = perpendicular to the span, toward +Z
        // (the load / up side) — option 1. Lengths stay locked to stock (zero
        // cutting waste); only the angles change. Target = the original member
        // id (e.g. "M0") or any of its sub-bar ids (e.g. "M0.s2").
        //
        // Geometry note: a chain whose total length equals the endpoint
        // distance is taut and CANNOT fold — so if split produced ~zero offcut
        // (Σ stock ≈ span) the rule refuses and tells you to split with
        // shorter / more pieces to create real slack.
        public static GrammarRuleResult ArticulateChainToStock(
            BridgeGraph g, string targetId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            if (stock == null || stock.Count == 0)
            {
                res.Warnings.Add("articulateChainToStock is stock-driven — connect Stock JSON.");
                return res;
            }

            // -------- resolve the chain prefix (accept "M0" or "M0.s2") --------
            string prefix = targetId;
            var pm = System.Text.RegularExpressions.Regex.Match(targetId ?? "", @"^(.*)\.s\d+$");
            if (pm.Success) prefix = pm.Groups[1].Value;

            var chainBars = g.Members
                .Where(x => x.Id != null && x.Id.StartsWith(prefix + ".s"))
                .ToList();
            if (chainBars.Count < 2)
            {
                res.Warnings.Add(
                    $"no multi-segment split chain found for '{prefix}' (need ≥2 sub-members " +
                    $"named {prefix}.s1, {prefix}.s2 …). Run splitMemberByStockLength on " +
                    $"{prefix} first — use maxBridgesPerNode:0 for a clean foldable chain.");
                return res;
            }

            // -------- degree map within the chain → must be a simple path --------
            var deg = new Dictionary<string, int>();
            var incidence = new Dictionary<string, List<(BridgeMember bar, string other)>>();
            foreach (var bar in chainBars)
                foreach (var pair in new[] { (bar.From, bar.To), (bar.To, bar.From) })
                {
                    string nid = pair.Item1, other = pair.Item2;
                    deg[nid] = deg.TryGetValue(nid, out var dprev) ? dprev + 1 : 1;
                    if (!incidence.TryGetValue(nid, out var lst))
                        incidence[nid] = lst = new List<(BridgeMember, string)>();
                    lst.Add((bar, other));
                }
            var endsList = deg.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
            if (endsList.Count != 2 || deg.Any(kv => kv.Value > 2))
            {
                res.Warnings.Add(
                    "the sub-members do not form a single simple chain (expected exactly two " +
                    "degree-1 endpoints + degree-2 interior). Cannot articulate a branched set.");
                return res;
            }

            // -------- linearise: ordered nodes P0..Pm and bars --------
            var nodeIds = new List<string>();
            var orderedBars = new List<BridgeMember>();
            var usedBar = new HashSet<string>();
            string prev = null, cur = endsList[0];
            nodeIds.Add(cur);
            while (true)
            {
                var step = incidence[cur].FirstOrDefault(
                    e => !usedBar.Contains(e.bar.Id) && e.other != prev);
                if (step.bar == null)
                    step = incidence[cur].FirstOrDefault(e => !usedBar.Contains(e.bar.Id));
                if (step.bar == null) break;
                usedBar.Add(step.bar.Id);
                orderedBars.Add(step.bar);
                nodeIds.Add(step.other);
                prev = cur; cur = step.other;
            }
            if (orderedBars.Count != chainBars.Count || nodeIds.Count != chainBars.Count + 1)
            {
                res.Warnings.Add("failed to linearise the chain (disconnected sub-members?).");
                return res;
            }

            var byId = g.Nodes.ToDictionary(n => n.Id);
            var P = nodeIds.Select(id => byId[id]).ToList();
            var a = P[0]; var b = P[P.Count - 1];
            int m = orderedBars.Count;
            for (int i = 1; i < P.Count - 1; i++)
                if (P[i].IsSupport == true)
                {
                    res.Warnings.Add(
                        $"interior node {P[i].Id} is a support — cannot fold a chain through a support.");
                    return res;
                }

            // -------- full (built) length per bar = its assigned stock piece --------
            var stockLen = new Dictionary<string, double>();
            foreach (var s in stock) if (!stockLen.ContainsKey(s.Id)) stockLen[s.Id] = s.Length;
            double maxStock = stock.Max(s => s.Length);
            double minStock = stock.Min(s => s.Length);
            double collisionTol = 0.5 * minStock;
            var Lbar = new double[m];
            int assignedCount = 0;
            for (int i = 0; i < m; i++)
            {
                var bar = orderedBars[i];
                if (bar.AssignedStockId != null && stockLen.TryGetValue(bar.AssignedStockId, out var sl))
                { Lbar[i] = sl; assignedCount++; }
                else
                    Lbar[i] = Dist(byId[bar.From], byId[bar.To]); // no slack from this bar
            }

            double D = Dist(a, b);
            if (D < 1e-6) { res.Warnings.Add("chain endpoints coincide — nothing to fold."); return res; }
            double S = Lbar.Sum();
            double slack = S - D;
            if (slack < Math.Max(1e-4, 0.02 * D))
            {
                res.Warnings.Add(
                    $"chain is taut: Σ(full stock)={S:F2}m ≈ span {D:F2}m (slack {slack:F2}m). " +
                    "No room to fold — split with shorter / more pieces so the offcuts add up to real slack.");
                return res;
            }

            // -------- fold frame: u along span, v perpendicular toward +Z --------
            double ux = (b.X - a.X) / D, uy = (b.Y - a.Y) / D, uz = (b.Z - a.Z) / D;
            double zu = uz;                                  // worldZ · u
            double vx = -zu * ux, vy = -zu * uy, vz = 1 - zu * uz;
            double vlen = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (vlen < 1e-6)                                 // span is vertical → use worldX
            {
                vx = 1 - ux * ux; vy = -ux * uy; vz = -ux * uz;
                vlen = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            }
            vx /= vlen; vy /= vlen; vz /= vlen;
            int foldSign = (p?["foldDown"]?.Value<bool?>() == true) ? -1 : 1;

            // -------- two-anchor FABRIK in the (u,v) plane (s,h coords) --------
            double[] s2 = new double[m + 1], h2 = new double[m + 1];
            double cum = 0;
            for (int i = 1; i <= m; i++) { cum += Lbar[i - 1]; s2[i] = Math.Min(D, cum * D / S); }
            s2[0] = 0; s2[m] = D;
            for (int i = 1; i < m; i++) h2[i] = 0.5 * slack * Math.Sin(Math.PI * i / m) + 1e-3;
            for (int iter = 0; iter < 200; iter++)
            {
                s2[0] = 0; h2[0] = 0;                        // forward, anchor a
                for (int i = 1; i <= m; i++)
                {
                    double dx = s2[i] - s2[i - 1], dy = h2[i] - h2[i - 1];
                    double d = Math.Sqrt(dx * dx + dy * dy); if (d < 1e-9) { dx = 1; dy = 0; d = 1; }
                    s2[i] = s2[i - 1] + Lbar[i - 1] * dx / d; h2[i] = h2[i - 1] + Lbar[i - 1] * dy / d;
                }
                s2[m] = D; h2[m] = 0;                        // backward, anchor b
                for (int i = m - 1; i >= 0; i--)
                {
                    double dx = s2[i] - s2[i + 1], dy = h2[i] - h2[i + 1];
                    double d = Math.Sqrt(dx * dx + dy * dy); if (d < 1e-9) { dx = -1; dy = 0; d = 1; }
                    s2[i] = s2[i + 1] + Lbar[i] * dx / d; h2[i] = h2[i + 1] + Lbar[i] * dy / d;
                }
                if (iter > 3 && Math.Abs(s2[0]) + Math.Abs(h2[0]) < 1e-7) break;
            }
            double meanH = 0; for (int i = 1; i < m; i++) meanH += h2[i];
            if (meanH < 0) for (int i = 0; i <= m; i++) h2[i] = -h2[i];   // keep on the +v (up) side

            // -------- map back to 3D, collision-check against non-chain nodes --------
            var chainSet = new HashSet<string>(nodeIds);
            var newPos = new Dictionary<string, double[]>();
            for (int i = 1; i < m; i++)
            {
                double X = a.X + ux * s2[i] + foldSign * vx * h2[i];
                double Y = a.Y + uy * s2[i] + foldSign * vy * h2[i];
                double Z = a.Z + uz * s2[i] + foldSign * vz * h2[i];
                newPos[P[i].Id] = new[] { Math.Round(X, 4), Math.Round(Y, 4), Math.Round(Z, 4) };
            }
            foreach (var kv in newPos)
                foreach (var n in g.Nodes)
                {
                    if (chainSet.Contains(n.Id)) continue;
                    double dx = n.X - kv.Value[0], dy = n.Y - kv.Value[1], dz = n.Z - kv.Value[2];
                    if (dx * dx + dy * dy + dz * dz < collisionTol * collisionTol)
                    {
                        res.Warnings.Add(
                            $"folded node {kv.Key} would collide with {n.Id} (< {collisionTol:F2}m). " +
                            "Refused — try fewer / longer pieces, or fold the other way (foldDown:true).");
                        return res;
                    }
                }

            // -------- commit: move interior nodes, lock bar lengths to full stock --------
            double apex = 0;
            for (int i = 1; i < m; i++)
            {
                apex = Math.Max(apex, Math.Abs(h2[i]));
                var np = newPos[P[i].Id];
                P[i].X = np[0]; P[i].Y = np[1]; P[i].Z = np[2];
            }
            for (int i = 0; i < m; i++) orderedBars[i].Length = Round(Lbar[i]);

            // -------- recompute any non-chain members touching moved nodes (braces) --------
            var movedIds = new HashSet<string>();
            for (int i = 1; i < m; i++) movedIds.Add(P[i].Id);
            var chainIds = new HashSet<string>(chainBars.Select(c => c.Id));
            var braceNote = new List<string>();
            foreach (var bar in g.Members)
            {
                if (chainIds.Contains(bar.Id)) continue;
                if (!movedIds.Contains(bar.From) && !movedIds.Contains(bar.To)) continue;
                if (!byId.TryGetValue(bar.From, out var pa) || !byId.TryGetValue(bar.To, out var pb)) continue;
                double nl = Dist(pa, pb);
                bar.Length = Round(nl);
                var fit = stock.Where(ss => ss.Length >= nl - 1e-6).OrderBy(ss => ss.Length - nl).FirstOrDefault();
                bar.AssignedStockId = fit?.Id;
                if (nl > maxStock + 1e-6)
                {
                    braceNote.Add($"{bar.Id}={nl:F2}m>{maxStock:F2}m");
                    res.Warnings.Add(
                        $"after fold, brace {bar.Id} stretched to {nl:F2}m > longest stock " +
                        $"{maxStock:F2}m — it would need its own split. Consider split maxBridgesPerNode:0.");
                }
                else braceNote.Add($"{bar.Id}={nl:F2}m→{fit?.Id ?? "?"}");
            }

            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "articulateChainToStock",
                RuleName = "Articulate Chain to Stock",
                TargetId = prefix,
                Note =
                    $"folded {m}-segment chain {prefix} (span {D:F2}m) into an arch by spending its " +
                    $"offcut slack: Σ(full stock)={S:F2}m → slack {slack:F2}m becomes rise " +
                    $"(apex {apex:F2}m, {(foldSign > 0 ? "up" : "down")} ⟂ span)\n" +
                    $"endpoints PINNED: {a.Id}({a.X:F2},{a.Y:F2},{a.Z:F2}) and " +
                    $"{b.Id}({b.X:F2},{b.Y:F2},{b.Z:F2}); {m - 1} interior node(s) rotated\n" +
                    $"each segment now built at FULL stock length → zero cutting offcut " +
                    $"({assignedCount}/{m} segments had an assigned stock id)\n" +
                    (braceNote.Count == 0 ? "" : "braces recomputed: " + string.Join(", ", braceNote)),
            };
            return res;
        }

        // ---------------- 10. connectNodeToStock ----------------------
        // Post-articulation bracing. The new node(s) produced by split +
        // articulate sit free in space; this rule connects them to OTHER nodes
        // of the structure, but ONLY where a brace is buildable from stock:
        // candidates farther than the longest stock piece (the bridgingRadius)
        // are skipped, and every brace that IS added gets the shortest stock
        // piece that still spans it. Meant to be chained after articulate:
        //   split → articulate → connectNodeToStock
        // Target = a node id (brace just that node) OR a split-chain prefix
        // like "M0" / "M0.s2" (brace every NEW interior node of that chain).
        // NOTE: there must be OTHER nodes within reach — on a single isolated
        // member there is nothing to connect to; feed the FULL structure.
        public static GrammarRuleResult ConnectNodeToStock(
            BridgeGraph g, string targetId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            if (stock == null || stock.Count == 0)
            {
                res.Warnings.Add("connectNodeToStock is stock-driven — connect Stock JSON.");
                return res;
            }

            double maxStock = stock.Max(s => s.Length);
            double bridgingRadius = p?["bridgingRadius"]?.Value<double?>() ?? maxStock;
            int maxConns = Math.Max(1, Math.Min(6, p?["maxConnectionsPerNode"]?.Value<int?>() ?? 2));

            var byId = g.Nodes.ToDictionary(n => n.Id);

            // -------- resolve which node(s) to brace --------
            var targetNodes = new List<BridgeNode>();
            string idBase = targetId;
            var directNode = g.Nodes.FirstOrDefault(n => n.Id == targetId);
            if (directNode != null)
            {
                targetNodes.Add(directNode);
            }
            else
            {
                string prefix = targetId;
                var pm = System.Text.RegularExpressions.Regex.Match(targetId ?? "", @"^(.*)\.s\d+$");
                if (pm.Success) prefix = pm.Groups[1].Value;
                idBase = prefix;
                var chainBars = g.Members
                    .Where(x => x.Id != null && x.Id.StartsWith(prefix + ".s")).ToList();
                if (chainBars.Count >= 1)
                {
                    var degc = new Dictionary<string, int>();
                    foreach (var bar in chainBars)
                    {
                        degc[bar.From] = degc.TryGetValue(bar.From, out var d1) ? d1 + 1 : 1;
                        degc[bar.To] = degc.TryGetValue(bar.To, out var d2) ? d2 + 1 : 1;
                    }
                    foreach (var kv in degc)            // interior = degree ≥ 2 within the chain = the NEW nodes
                        if (kv.Value >= 2 && byId.TryGetValue(kv.Key, out var nn)) targetNodes.Add(nn);
                }
            }
            if (targetNodes.Count == 0)
            {
                res.Warnings.Add(
                    $"no node or split-chain found for target '{targetId}'. Pass a node id, " +
                    $"or a prefix like 'M0' after splitting/articulating it.");
                return res;
            }

            // -------- adjacency so we never duplicate an existing member --------
            var adjacency = new Dictionary<string, HashSet<string>>();
            foreach (var mm in g.Members)
            {
                if (!adjacency.TryGetValue(mm.From, out var sF)) adjacency[mm.From] = sF = new HashSet<string>();
                sF.Add(mm.To);
                if (!adjacency.TryGetValue(mm.To, out var sT)) adjacency[mm.To] = sT = new HashSet<string>();
                sT.Add(mm.From);
            }

            int seq = 0;
            var added = new List<string>();
            var diag = new List<string>();
            foreach (var n in targetNodes)
            {
                adjacency.TryGetValue(n.Id, out var seen0);
                var seen = seen0 ?? new HashSet<string>();
                var ranked = g.Nodes
                    .Where(o => o.Id != n.Id && !seen.Contains(o.Id))
                    .Select(o => new { o, d = Dist(o, n) })
                    .Where(x => x.d > 1e-6)
                    .OrderBy(x => x.d)
                    .ToList();
                var inRange = ranked.Where(x => x.d <= bridgingRadius + 1e-6).Take(maxConns).ToList();
                foreach (var cand in inRange)
                {
                    var bid = $"{idBase}.c{++seq}";
                    var fit = stock.Where(s => s.Length >= cand.d - 1e-6)
                                   .OrderBy(s => s.Length - cand.d).FirstOrDefault();
                    g.Members.Add(new BridgeMember
                    {
                        Id = bid,
                        From = n.Id,
                        To = cand.o.Id,
                        Length = Round(cand.d),
                        AssignedStockId = fit?.Id,
                    });
                    added.Add(bid);
                    seen.Add(cand.o.Id);
                    if (!adjacency.TryGetValue(cand.o.Id, out var sB)) adjacency[cand.o.Id] = sB = new HashSet<string>();
                    sB.Add(n.Id);
                    adjacency[n.Id] = seen;
                    diag.Add($"{n.Id}→{cand.o.Id} ({cand.d:F2}m → {fit?.Id ?? "?"})");
                }
                if (inRange.Count == 0)
                {
                    var c0 = ranked.FirstOrDefault();
                    diag.Add(c0 != null
                        ? $"{n.Id}: no brace — nearest free node {c0.o.Id} is {c0.d:F2}m " +
                          $"(> bridgingRadius {bridgingRadius:F2}m / longest stock). " +
                          "Feed the full structure or raise bridgingRadius."
                        : $"{n.Id}: no free node to connect to (single isolated member?).");
                }
            }
            foreach (var d in diag)
                if (d.Contains("no brace") || d.Contains("no free")) res.Warnings.Add(d);

            if (added.Count == 0)
            {
                res.Warnings.Add(
                    "connectNodeToStock added no braces — every target node is already fully " +
                    "connected, or no other node lies within a buildable stock length.");
                return res;
            }

            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "connectNodeToStock",
                RuleName = "Connect Node to Stock",
                TargetId = idBase,
                Note =
                    $"braced {targetNodes.Count} node(s) with {added.Count} new member(s) within " +
                    $"bridgingRadius {bridgingRadius:F2}m (≤ longest stock {maxStock:F2}m), " +
                    $"max {maxConns}/node, each assigned the shortest stock that spans it:\n  " +
                    string.Join("\n  ", diag),
                AddedMemberIds = added,
            };
            return res;
        }

        // ---------------- 11. mergeSplitChain (inverse of split) ------
        // The INVERSE of split → articulate → connect: collapse a whole split
        // group back into the single original member. Remove the new interior
        // node(s), the sub-bars (.s*), and
        // the bridges/braces (.b*/.c*) that hung off them, then re-add the one
        // straight member between the chain's two endpoints. This is what makes
        // R1 a genuine BIDIRECTIONAL grammar rule. Target = the original member
        // id / prefix ("M0") or any sub-bar id ("M0.s2").
        //
        // Safety: an interior node that some OUTSIDE member also uses is NOT
        // deleted (that would orphan the foreign member) — it is kept and
        // reported. Removing braces may affect rigidity; the topology count
        // does not detect mechanisms or establish structural stability.
        public static GrammarRuleResult MergeSplitChain(
            BridgeGraph g, string targetId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();

            string prefix = targetId;
            var pm = System.Text.RegularExpressions.Regex.Match(targetId ?? "", @"^(.*)\.s\d+$");
            if (pm.Success) prefix = pm.Groups[1].Value;

            var chainBars = g.Members
                .Where(x => x.Id != null && x.Id.StartsWith(prefix + ".s")).ToList();
            if (chainBars.Count < 1)
            {
                res.Warnings.Add(
                    $"no split chain '{prefix}.s*' to merge — mergeSplitChain is the inverse of " +
                    "splitMemberByStockLength, so split that member first (or pass the right prefix).");
                return res;
            }

            // whole group = sub-bars + bridges + connect-braces of this prefix
            var group = g.Members.Where(x => x.Id != null &&
                    (x.Id.StartsWith(prefix + ".s") ||
                     x.Id.StartsWith(prefix + ".b") ||
                     x.Id.StartsWith(prefix + ".c")))
                .ToList();

            // linearise the sub-bars → the two endpoints a, b
            var deg = new Dictionary<string, int>();
            foreach (var bar in chainBars)
            {
                deg[bar.From] = deg.TryGetValue(bar.From, out var d1) ? d1 + 1 : 1;
                deg[bar.To] = deg.TryGetValue(bar.To, out var d2) ? d2 + 1 : 1;
            }
            var ends = deg.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
            if (ends.Count != 2)
            {
                res.Warnings.Add(
                    $"split chain '{prefix}.s*' is not a simple path ({ends.Count} endpoints found); " +
                    "cannot merge it back into one member safely.");
                return res;
            }
            string aId = ends[0], bId = ends[1];
            var interior = new HashSet<string>(deg.Where(kv => kv.Value >= 2).Select(kv => kv.Key));

            // what survives removal — to protect interior nodes used elsewhere
            var removeMemberIds = new HashSet<string>(group.Select(mm => mm.Id));
            var referencedAfter = new HashSet<string>();
            foreach (var mm in g.Members)
                if (!removeMemberIds.Contains(mm.Id)) { referencedAfter.Add(mm.From); referencedAfter.Add(mm.To); }

            var removeNodeIds = new HashSet<string>(interior.Where(nid => !referencedAfter.Contains(nid)));
            var keptInterior = interior.Where(nid => referencedAfter.Contains(nid)).ToList();

            var byId = g.Nodes.ToDictionary(n => n.Id);
            double L = (byId.ContainsKey(aId) && byId.ContainsKey(bId))
                ? Dist(byId[aId], byId[bId]) : 0;

            // apply removals
            g.Members.RemoveAll(mm => removeMemberIds.Contains(mm.Id));
            g.Nodes.RemoveAll(n => removeNodeIds.Contains(n.Id));

            // re-add the single original member a–b (unless one already exists)
            bool exists = g.Members.Any(mm =>
                (mm.From == aId && mm.To == bId) || (mm.From == bId && mm.To == aId));
            string addedId = null;
            if (!exists)
            {
                addedId = prefix;
                string fitId = null;
                if (stock != null && stock.Count > 0)
                    fitId = stock.Where(s => s.Length >= L - 1e-6)
                                 .OrderBy(s => s.Length - L).FirstOrDefault()?.Id;
                g.Members.Add(new BridgeMember
                {
                    Id = addedId, From = aId, To = bId, Length = Round(L), AssignedStockId = fitId,
                });
            }

            int braces = removeMemberIds.Count - chainBars.Count;
            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "mergeSplitChain",
                RuleName = "Merge Split Chain (inverse of Split)",
                TargetId = prefix,
                Note =
                    $"merged group '{prefix}' back into one member {prefix} ({aId}→{bId}, L={L:F2}m): " +
                    $"removed {removeMemberIds.Count} member(s) [{chainBars.Count} sub-bar(s) + " +
                    $"{braces} brace/bridge(s)] and {removeNodeIds.Count} interior node(s)" +
                    (keptInterior.Count > 0
                        ? $"\nKEPT {keptInterior.Count} interior node(s) still used by outside members " +
                          $"({string.Join(",", keptInterior)}) — their braces were removed, so check stability"
                        : "") +
                    (exists ? "\noriginal member a–b already present; not re-added" : ""),
                AddedMemberIds = addedId != null ? new List<string> { addedId } : new List<string>(),
                RemovedMemberIds = removeMemberIds.ToList(),
            };
            return res;
        }

        // ---------------- 12. addNodeToStock -------------------------
        // Position supplies three anchors; targetPoint is an approximate location.
        // Three distinct available stock pieces determine a non-coplanar node by
        // three-sphere intersection. Inventory lengths are used in full.
        public static GrammarRuleResult AddNodeToStock(
            BridgeGraph g, string memberId, JObject p, List<StockItem> stock)
        {
            return SpatialStockNode.Apply(g, memberId, p, stock);
        }

        // ---------------- 13. removeNode (inverse of R2) --------------
        // Delete a node and every member incident to it — the inverse of
        // addNodeToStock (and a general node-removal grammar move). Supports are
        // never removed. The resulting structure requires independent stability
        // analysis; node/member counts do not detect mechanisms. Target = node id.
        public static GrammarRuleResult RemoveNode(
            BridgeGraph g, string targetId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            var toks = (targetId ?? "").Split(new[] { ',', ' ', ';', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // Resolve which node(s) to remove:
            //  • several NODE ids  → remove ALL of them in one pass (so a multi-
            //    select isn't fanned out into graphs that each drop only one);
            //  • 2+ MEMBER ids     → their single common node (Target Picker only
            //    emits members, so pass the bars meeting at the node to delete);
            //  • one NODE id       → that node.
            var nodeIds = new List<string>();
            string via = null;
            var asNodes = toks.Where(t => g.Nodes.Any(x => x.Id == t)).Distinct().ToList();
            if (asNodes.Count == toks.Length && asNodes.Count >= 1)
            {
                nodeIds = asNodes;
                if (nodeIds.Count > 1) via = $" ({nodeIds.Count} nodes)";
            }
            else if (toks.Length >= 2 && toks.All(t => g.Members.Any(mm => mm.Id == t)))
            {
                var mems = toks.Select(t => g.Members.First(mm => mm.Id == t)).ToList();
                var common = new HashSet<string>(new[] { mems[0].From, mems[0].To });
                foreach (var mm in mems.Skip(1)) common.IntersectWith(new[] { mm.From, mm.To });
                if (common.Count != 1)
                {
                    res.Warnings.Add($"the bars [{string.Join(",", toks)}] do not all meet at ONE common node ({common.Count} shared).");
                    return res;
                }
                nodeIds.Add(common.First());
                via = $" (common node of [{string.Join(",", toks)}])";
            }
            if (nodeIds.Count == 0)
            {
                res.Warnings.Add($"'{targetId}' not found — pass a NODE id, several node ids, or 2+ members meeting at the node to remove.");
                return res;
            }

            var removedMembers = new HashSet<string>();
            var removedNodes = new List<string>();
            var skippedSupports = new List<string>();
            foreach (var nid in nodeIds)
            {
                var n = g.Nodes.FirstOrDefault(x => x.Id == nid);
                if (n == null) continue;
                if (n.IsSupport == true) { skippedSupports.Add(nid); continue; }
                foreach (var mm in g.Members.Where(mm => mm.From == nid || mm.To == nid))
                    removedMembers.Add(mm.Id);
                g.Members.RemoveAll(mm => mm.From == nid || mm.To == nid);
                g.Nodes.RemoveAll(x => x.Id == nid);
                removedNodes.Add(nid);
            }
            if (removedNodes.Count == 0)
            {
                res.Warnings.Add($"nothing removed — target(s) are supports ([{string.Join(",", skippedSupports)}]), which are never removed.");
                return res;
            }

            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "removeNode",
                RuleName = "Remove Node (inverse of Add Node)",
                TargetId = string.Join(",", removedNodes),
                Note =
                    $"removed {removedNodes.Count} node(s) [{string.Join(", ", removedNodes)}]{via} and their " +
                    $"{removedMembers.Count} incident member(s) [{string.Join(", ", removedMembers)}]" +
                    (skippedSupports.Count > 0 ? $"\nskipped support(s): [{string.Join(",", skippedSupports)}]" : ""),
                RemovedMemberIds = removedMembers.ToList(),
            };
            return res;
        }

        // ---------------- 14. dissolveNodeToDiagonal (R1 inverse, general) --
        // The topological inverse of splitEdgeToPoint: take a degree-3 hub H
        // (created by splitting one through-member A–B and bracing the splice to
        // a third node C), remove H and its three edges, and re-add the single
        // diagonal A–B. For a degree-3 hub, removing one node and two net edges
        // preserves the count m − (2n − 3), as in a planar Henneberg II inverse.
        // That count does not establish rigidity, stability, or static determinacy
        // in 2-D or 3-D. Unlike mergeSplitChain it is NOT name-based — it works on
        // ANY such hub (even after the chain was folded), identifying the
        // original through-member as the STRAIGHTEST neighbour pair at H (angle
        // closest to 180°). Override with params {"bridgeNodes":["Na","Nb"]}.
        // Target = the centre node id (the hub) to dissolve.
        public static GrammarRuleResult DissolveNodeToDiagonal(
            BridgeGraph g, string targetId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            var byId = g.Nodes.ToDictionary(n => n.Id);
            int Degree(string id) => g.Members.Count(mm => mm.From == id || mm.To == id);

            // Target is normally the hub NODE id. For convenience also accept a
            // MEMBER id (one of the hub's spokes): resolve to the endpoint that
            // is the degree-3 hub (non-support; ties → the smaller-degree one).
            // This lets you pick a LINE when the canvas has no node-picker.
            string via = null;
            BridgeNode h = null;

            // (a) spokes: params {"spokes":[ids]} OR a multi-token Target
            //     ("M1,M2,M3" or "M1 M2 M3") → the hub is their single COMMON
            //     node. This is the "just select the converging bars" interaction.
            List<string> spokeIds = null;
            var spokesTok = p?["spokes"] as Newtonsoft.Json.Linq.JArray;
            if (spokesTok != null && spokesTok.Count >= 2)
                spokeIds = spokesTok.Select(x => x.Value<string>()).ToList();
            else if (!string.IsNullOrWhiteSpace(targetId))
            {
                var toks = targetId.Split(new[] { ',', ' ', ';', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length >= 2 && toks.All(t => g.Members.Any(mm => mm.Id == t)))
                    spokeIds = toks.ToList();
            }
            if (spokeIds != null)
            {
                var mems = spokeIds.Select(id => g.Members.FirstOrDefault(mm => mm.Id == id)).ToList();
                if (mems.Any(x => x == null))
                {
                    res.Warnings.Add($"spokes: member id(s) not found: [{string.Join(",", spokeIds)}].");
                    return res;
                }
                var common = new HashSet<string>(new[] { mems[0].From, mems[0].To });
                foreach (var mm in mems.Skip(1)) common.IntersectWith(new[] { mm.From, mm.To });
                if (common.Count != 1)
                {
                    res.Warnings.Add($"spokes [{string.Join(",", spokeIds)}] do not all meet at ONE common node ({common.Count} shared).");
                    return res;
                }
                byId.TryGetValue(common.First(), out h);
                via = $" (hub = common node of {spokeIds.Count} spokes [{string.Join(",", spokeIds)}])";
            }

            // (a2) Target is TWO NODE ids → the endpoints you want CONNECTED; the
            //      hub is the node between them (their common neighbour). This is
            //      "select the two points to merge into one line" directly.
            string forceMergeA = null, forceMergeB = null;
            if (h == null && !string.IsNullOrWhiteSpace(targetId))
            {
                var nt = targetId.Split(new[] { ',', ' ', ';', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (nt.Length == 2 && nt.All(t => g.Nodes.Any(n => n.Id == t)))
                {
                    string aId = nt[0], bId = nt[1];
                    var adjA = new HashSet<string>(g.Members.Where(mm => mm.From == aId || mm.To == aId).Select(mm => mm.From == aId ? mm.To : mm.From));
                    var adjB = new HashSet<string>(g.Members.Where(mm => mm.From == bId || mm.To == bId).Select(mm => mm.From == bId ? mm.To : mm.From));
                    adjA.IntersectWith(adjB); adjA.Remove(aId); adjA.Remove(bId);
                    var hub = adjA.Select(id => byId[id]).Where(n => n.IsSupport != true).OrderBy(n => Degree(n.Id)).FirstOrDefault();
                    if (hub == null)
                    {
                        res.Warnings.Add($"no non-support node lies between {aId} and {bId} to dissolve (they share no common neighbour).");
                        return res;
                    }
                    h = hub; forceMergeA = aId; forceMergeB = bId;
                    via = $" (merging {aId}–{bId}; hub {hub.Id} between them)";
                }
            }

            // (b) target is the hub node id
            if (h == null) h = g.Nodes.FirstOrDefault(n => n.Id == targetId);

            // (c) target is a single spoke member → its degree-3 (hub) endpoint
            if (h == null)
            {
                var mem = g.Members.FirstOrDefault(mm => mm.Id == targetId);
                if (mem != null)
                {
                    byId.TryGetValue(mem.From, out var e1); byId.TryGetValue(mem.To, out var e2);
                    var cands = new[] { e1, e2 }.Where(x => x != null && x.IsSupport != true).ToList();
                    h = cands.FirstOrDefault(x => Degree(x.Id) == 3) ?? cands.OrderBy(x => Degree(x.Id)).FirstOrDefault();
                    if (h != null) via = $" (resolved from member {targetId})";
                }
            }
            if (h == null)
            {
                res.Warnings.Add($"'{targetId}' is neither a node nor a member — pass the hub node id, a spoke member, or params \"spokes\":[converging member ids].");
                return res;
            }
            if (h.IsSupport == true)
            {
                res.Warnings.Add($"node {h.Id} is a support — never dissolved.");
                return res;
            }
            string hubId = h.Id;

            var incident = g.Members.Where(mm => mm.From == hubId || mm.To == hubId).ToList();
            var nbrIds = incident.Select(mm => mm.From == hubId ? mm.To : mm.From).Distinct().ToList();
            if (nbrIds.Count < 2)
            {
                res.Warnings.Add($"node {hubId} has degree {nbrIds.Count} (<2) — nothing to merge into a diagonal.");
                return res;
            }
            // SAFETY: this rule dissolves a node where ~3 bars meet (a degree-3
            // node; degree 4 allowed for a split hub with a brace). It is NOT
            // meant for a MAIN structural joint where many bars converge —
            // refuse high-degree nodes so a mis-selection can't wipe out a major
            // node and its 8 members. Works on ANY qualifying node, no prior
            // split required. Raise maxHubDegree to override knowingly.
            int maxHubDegree = Math.Max(2, p?["maxHubDegree"]?.Value<int?>() ?? 4);
            if (nbrIds.Count > maxHubDegree)
            {
                res.Warnings.Add(
                    $"node {hubId} has degree {nbrIds.Count} (> maxHubDegree {maxHubDegree}) — more than ~3 " +
                    $"bars meet here, so this is a MAIN joint, not '3 bars meeting at a point'. Dissolving it " +
                    $"would remove {incident.Count} members. If you really mean it, set " +
                    $"\"maxHubDegree\":{nbrIds.Count} or pass \"bridgeNodes\":[\"Na\",\"Nb\"].");
                return res;
            }

            // ---- choose the diagonal pair (keepA, keepB) ----
            string keepA = null, keepB = null, pairNote = null;
            var bridgeTok = p?["bridgeNodes"] as Newtonsoft.Json.Linq.JArray;
            if (forceMergeA != null)            // Target gave the two endpoints directly
            {
                keepA = forceMergeA; keepB = forceMergeB;
            }
            else if (bridgeTok != null && bridgeTok.Count == 2)
            {
                keepA = bridgeTok[0].Value<string>(); keepB = bridgeTok[1].Value<string>();
                if (!nbrIds.Contains(keepA) || !nbrIds.Contains(keepB))
                {
                    res.Warnings.Add($"bridgeNodes must be two of {hubId}'s neighbours [{string.Join(",", nbrIds)}].");
                    return res;
                }
            }
            else
            {
                // Candidate diagonals = neighbour pairs NOT already joined by an
                // edge (you can't "merge into" an existing leg). Default picks the
                // STRAIGHTEST such pair = the original through-member that was
                // split (the inverse of split, restores the hypotenuse even if it
                // is over-long). pairBy:"stock" instead prefers the least-offcut
                // buildable pair. Stock always assigns the fit and warns if the
                // restored member exceeds the longest piece.
                double maxStock = (stock != null && stock.Count > 0) ? stock.Max(s => s.Length) : double.PositiveInfinity;
                var cands = new List<(string a, string b, double d, bool feas, double offcut, double deg)>();
                for (int i = 0; i < nbrIds.Count; i++)
                    for (int j = i + 1; j < nbrIds.Count; j++)
                    {
                        var A = byId[nbrIds[i]]; var B = byId[nbrIds[j]];
                        bool already = g.Members.Any(mm =>
                            (mm.From == A.Id && mm.To == B.Id) || (mm.From == B.Id && mm.To == A.Id));
                        if (already) continue;                 // skip existing legs
                        double d = Dist(A, B);
                        bool feas = d <= maxStock + 1e-6;
                        double off = double.PositiveInfinity;
                        if (feas && stock != null && stock.Count > 0)
                        {
                            var fit = stock.Where(s => s.Length >= d - 1e-6).OrderBy(s => s.Length - d).FirstOrDefault();
                            off = fit == null ? double.PositiveInfinity : fit.Length - d;
                        }
                        else if (feas) off = 0;
                        double deg = Math.Acos(Math.Max(-1, Math.Min(1, AngleCos(A, h, B)))) * 180.0 / Math.PI;
                        cands.Add((nbrIds[i], nbrIds[j], d, feas && !double.IsInfinity(off), off, deg));
                    }
                if (cands.Count == 0)
                {
                    res.Warnings.Add(
                        $"every pair of {hubId}'s neighbours is already directly connected — there is no NEW " +
                        "diagonal to merge into. Use removeNode if you just want to delete the node.");
                    return res;
                }
                string mode = (p?["pairBy"]?.Value<string>() ?? "straight").ToLowerInvariant();
                var chosen = mode == "stock"
                    ? (cands.Where(c => c.feas).OrderBy(c => c.offcut).ThenByDescending(c => c.deg).FirstOrDefault().a != null
                        ? cands.Where(c => c.feas).OrderBy(c => c.offcut).ThenByDescending(c => c.deg).First()
                        : cands.OrderByDescending(c => c.deg).First())
                    : cands.OrderByDescending(c => c.deg).ThenBy(c => c.offcut).First();   // straightest (default)
                if (!chosen.feas)
                    res.Warnings.Add(
                        $"the merged diagonal {chosen.a}–{chosen.b} ({chosen.d:F2}m) exceeds longest stock " +
                        $"{maxStock:F2}m — this is the original, pre-split, over-long member; re-split it to make it buildable.");
                keepA = chosen.a; keepB = chosen.b;
                pairNote = string.Join(", ", cands
                    .OrderByDescending(c => c.deg)
                    .Select(c => $"{c.a}–{c.b}({c.d:F2}m,{(c.feas ? $"offcut {c.offcut:F2}m" : ">stock")},{c.deg:F0}°)"));
            }
            double straightDeg = Math.Acos(Math.Max(-1, Math.Min(1,
                AngleCos(byId[keepA], h, byId[keepB])))) * 180.0 / Math.PI;

            // ---- apply: remove H + all incident, add the diagonal ----
            var removedIds = incident.Select(mm => mm.Id).ToList();
            var dropped = nbrIds.Where(id => id != keepA && id != keepB).ToList();   // un-braced neighbours
            g.Members.RemoveAll(mm => mm.From == hubId || mm.To == hubId);
            g.Nodes.RemoveAll(x => x.Id == hubId);

            string addedId = null;
            bool exists = g.Members.Any(mm =>
                (mm.From == keepA && mm.To == keepB) || (mm.From == keepB && mm.To == keepA));
            double L = Dist(byId[keepA], byId[keepB]);
            if (!exists)
            {
                string baseId = $"{keepA}_{keepB}"; addedId = baseId; int k = 1;
                while (g.Members.Any(mm => mm.Id == addedId)) addedId = $"{baseId}.{k++}";
                string fitId = null;
                if (stock != null && stock.Count > 0)
                    fitId = stock.Where(s => s.Length >= L - 1e-6).OrderBy(s => s.Length - L).FirstOrDefault()?.Id;
                g.Members.Add(new BridgeMember
                {
                    Id = addedId, From = keepA, To = keepB, Length = Round(L), AssignedStockId = fitId,
                });
            }

            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "dissolveNodeToDiagonal",
                RuleName = "Dissolve Node to Diagonal (Henneberg II inverse)",
                TargetId = hubId,
                Note =
                    $"dissolved degree-{nbrIds.Count} hub {hubId}{via}: removed it + {removedIds.Count} edge(s) " +
                    $"[{string.Join(", ", removedIds)}] and re-added the {(pairNote == null ? "specified" : "merged")} " +
                    $"diagonal {keepA}–{keepB} (L={L:F2}m, {straightDeg:F0}° at hub — the straightest non-existing pair)" +
                    (exists ? " — diagonal already existed, not duplicated" : $" as {addedId}") +
                    (pairNote != null ? $"\ncandidate pairs (straightest first; offcut shown): [{pairNote}]" : "") +
                    (dropped.Count > 0 ? $"\nun-braced neighbour(s): [{string.Join(",", dropped)}] (their brace to the hub is gone)" : ""),
                AddedMemberIds = addedId != null ? new List<string> { addedId } : new List<string>(),
                RemovedMemberIds = removedIds,
            };
            return res;
        }

        // ---------------- 15. crossBracePanel (R3, two bars → X) ------
        // Replace two members with four distinct endpoints by a centre node V
        // and an X of four spokes to the corners. The two input
        // members are removed (the X replaces them) unless keepSides. Each spoke
        // must fit stock. Pass the two members as a 2-id Target "M1,M2" or params
        // {"sides":["M1","M2"]}. Inverse = uncrossPanel (recovers them exactly).
        public static GrammarRuleResult CrossBracePanel(
            BridgeGraph g, string targetId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            if (stock == null || stock.Count == 0)
            {
                res.Warnings.Add("crossBracePanel is stock-driven — connect Stock JSON.");
                return res;
            }

            // resolve the two side members
            List<string> sideIds = null;
            var sidesTok = p?["sides"] as Newtonsoft.Json.Linq.JArray;
            if (sidesTok != null && sidesTok.Count == 2)
                sideIds = sidesTok.Select(x => x.Value<string>()).ToList();
            else if (!string.IsNullOrWhiteSpace(targetId))
            {
                var toks = targetId.Split(new[] { ',', ' ', ';', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length == 2) sideIds = toks.ToList();
            }
            if (sideIds == null || sideIds.Count != 2)
            {
                res.Warnings.Add("crossBracePanel needs TWO members (the parallel sides): Target \"M1,M2\" or params \"sides\":[\"M1\",\"M2\"].");
                return res;
            }
            var m1 = g.Members.FirstOrDefault(x => x.Id == sideIds[0]);
            var m2 = g.Members.FirstOrDefault(x => x.Id == sideIds[1]);
            if (m1 == null || m2 == null)
            {
                res.Warnings.Add($"side member(s) not found: [{sideIds[0]}, {sideIds[1]}].");
                return res;
            }
            var byId = g.Nodes.ToDictionary(n => n.Id);
            var four = new[] { byId[m1.From], byId[m1.To], byId[m2.From], byId[m2.To] };
            if (four.Select(n => n.Id).Distinct().Count() != 4)
            {
                res.Warnings.Add("the two members share an endpoint — they are not the two opposite sides of a panel.");
                return res;
            }

            double maxStock = stock.Max(s => s.Length);
            var V = new BridgeNode
            {
                Id = NextNodeId(g, "X", g.Nodes.Count),
                X = Math.Round(four.Average(n => n.X), 4),
                Y = Math.Round(four.Average(n => n.Y), 4),
                Z = Math.Round(four.Average(n => n.Z), 4),
            };
            if (g.Nodes.Any(n => Dist(n, V) < 1e-3))
            {
                res.Warnings.Add("panel centre coincides with an existing node.");
                return res;
            }
            var conns = new List<(BridgeNode t, double d, StockItem fit)>();
            foreach (var t in four)
            {
                double d = Dist(t, V);
                if (d > maxStock + 1e-6)
                {
                    res.Warnings.Add($"spoke {V.Id}→{t.Id} = {d:F2}m > longest stock {maxStock:F2}m — panel too big to cross-brace from stock.");
                    return res;
                }
                var fit = stock.Where(s => s.Length >= d - 1e-6).OrderBy(s => s.Length - d).FirstOrDefault();
                conns.Add((t, d, fit));
            }

            bool keepSides = p?["keepSides"]?.Value<bool?>() == true;
            var removed = new List<string>();
            if (!keepSides)
            {
                g.Members.RemoveAll(x => x.Id == m1.Id || x.Id == m2.Id);
                removed.Add(m1.Id); removed.Add(m2.Id);
            }
            g.Nodes.Add(V);
            var added = new List<string>(); var cn = new List<string>(); int seq = 0;
            foreach (var c in conns)
            {
                string bid = $"{m1.Id}_{m2.Id}.x{++seq}";
                while (g.Members.Any(mm => mm.Id == bid)) bid = $"{m1.Id}_{m2.Id}.x{++seq}";
                g.Members.Add(new BridgeMember { Id = bid, From = V.Id, To = c.t.Id, Length = Round(c.d), AssignedStockId = c.fit?.Id });
                added.Add(bid); cn.Add($"{c.t.Id}({c.d:F2}m→{c.fit?.Id ?? "?"})");
            }

            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "crossBracePanel",
                RuleName = "Cross-Brace Panel (parallel → X)",
                TargetId = $"{m1.Id},{m2.Id}",
                Note =
                    $"cross-braced cell ({four[0].Id},{four[1].Id},{four[2].Id},{four[3].Id}) with new centre {V.Id}: " +
                    $"{(keepSides ? "kept" : "removed")} the 2 members [{m1.Id},{m2.Id}] (any angle) and added an X of 4 spokes\n" +
                    $"spokes: [{string.Join(", ", cn)}] (each ≤ one stock piece)",
                AddedNodeIds = new List<string> { V.Id },
                AddedMemberIds = added,
                RemovedMemberIds = removed,
            };
            return res;
        }

        // ---------------- 16. uncrossPanel (inverse of R3) ------------
        // X → parallel. Take the X centre node V (degree 4), remove it + its 4
        // spokes, and restore TWO parallel sides between the four corners — the
        // pairing whose two members are the most parallel (the non-crossing
        // sides). Stock-fits the restored sides. Target = the centre node id, or
        // one of its spokes. Removing braces may affect stiffness; the resulting
        // structure needs independent analysis, beyond the topology count.
        public static GrammarRuleResult UncrossPanel(
            BridgeGraph g, string targetId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            int Deg(string id) => g.Members.Count(mm => mm.From == id || mm.To == id);
            BridgeNode v = null;
            string via = null;

            // (a) two+ crossing bars (spokes) → their single COMMON node = the X
            //     centre. Just pick two of the X's bars; they all meet at V.
            var toks = (targetId ?? "").Split(new[] { ',', ' ', ';', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length >= 2 && toks.All(t => g.Members.Any(mm => mm.Id == t)))
            {
                var mems = toks.Select(t => g.Members.First(mm => mm.Id == t)).ToList();
                var common = new HashSet<string>(new[] { mems[0].From, mems[0].To });
                foreach (var mm in mems.Skip(1)) common.IntersectWith(new[] { mm.From, mm.To });
                if (common.Count != 1)
                {
                    res.Warnings.Add($"the bars [{string.Join(",", toks)}] do not all meet at ONE common node ({common.Count} shared) — pick bars that share the X centre.");
                    return res;
                }
                v = g.Nodes.FirstOrDefault(n => n.Id == common.First());
                via = $" (centre = common node of [{string.Join(",", toks)}])";
            }

            // (b) target is the centre node id
            if (v == null) v = g.Nodes.FirstOrDefault(n => n.Id == targetId);

            // (c) target is a single spoke member → its degree-4 endpoint
            if (v == null)
            {
                var mem = g.Members.FirstOrDefault(mm => mm.Id == targetId);
                if (mem != null)
                {
                    var cands = new[] { mem.From, mem.To }
                        .Select(id => g.Nodes.FirstOrDefault(n => n.Id == id))
                        .Where(n => n != null && n.IsSupport != true).ToList();
                    v = cands.FirstOrDefault(x => Deg(x.Id) == 4) ?? cands.OrderByDescending(x => Deg(x.Id)).FirstOrDefault();
                    if (v != null) via = $" (resolved from member {targetId})";
                }
            }
            if (v == null)
            {
                res.Warnings.Add($"'{targetId}' not found — pass the X centre node id (degree 4), one spoke, or two bars that share the centre.");
                return res;
            }
            if (v.IsSupport == true) { res.Warnings.Add($"node {v.Id} is a support — not removed."); return res; }

            var incident = g.Members.Where(mm => mm.From == v.Id || mm.To == v.Id).ToList();
            var nbrIds = incident.Select(mm => mm.From == v.Id ? mm.To : mm.From).Distinct().ToList();
            if (nbrIds.Count != 4)
            {
                res.Warnings.Add($"node {v.Id} has degree {nbrIds.Count}, not 4 — uncrossPanel expects an X centre (4 spokes).");
                return res;
            }
            var byId = g.Nodes.ToDictionary(n => n.Id);
            var P = nbrIds.Select(id => byId[id]).ToList();
            bool Edge(string x, string y) => g.Members.Any(mm => (mm.From == x && mm.To == y) || (mm.From == y && mm.To == x));
            (double ux, double uy, double uz) Dir(BridgeNode a, BridgeNode b)
            {
                double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
                double l = Math.Sqrt(dx * dx + dy * dy + dz * dz); if (l < 1e-9) l = 1;
                return (dx / l, dy / l, dz / l);
            }

            // Recover the ORIGINAL pair first: crossBracePanel names its spokes
            // {..}.x1..x4 in endpoint order (x1-x2 = side 1, x3-x4 = side 2), so
            // restore EXACTLY those two members — correct at ANY angle, not just
            // parallel. If the centre wasn't made by crossBracePanel, fall back
            // to the most-parallel non-crossing pairing.
            (BridgeNode, BridgeNode)[] sides; string method;
            var indexed = incident
                .Select(mm => new { outer = byId[mm.From == v.Id ? mm.To : mm.From], k = XIndex(mm.Id) })
                .ToList();
            if (indexed.All(o => o.k >= 1) && indexed.Select(o => o.k).Distinct().Count() == 4)
            {
                var o = indexed.OrderBy(z => z.k).Select(z => z.outer).ToList();
                sides = new[] { (o[0], o[1]), (o[2], o[3]) };
                method = "original .x pairing";
            }
            else
            {
                // 3 ways to split 4 corners into 2 pairs; pick the most-parallel
                // one (the sides), preferring pairings without an existing edge.
                var pairings = new[] { new[] { 0, 1, 2, 3 }, new[] { 0, 2, 1, 3 }, new[] { 0, 3, 1, 2 } };
                int[] best = null; double bestScore = -1;
                foreach (var pr in pairings)
                {
                    if (Edge(P[pr[0]].Id, P[pr[1]].Id) || Edge(P[pr[2]].Id, P[pr[3]].Id)) continue;
                    var d1 = Dir(P[pr[0]], P[pr[1]]); var d2 = Dir(P[pr[2]], P[pr[3]]);
                    double par = Math.Abs(d1.ux * d2.ux + d1.uy * d2.uy + d1.uz * d2.uz);
                    if (par > bestScore) { bestScore = par; best = pr; }
                }
                if (best == null)
                    foreach (var pr in pairings)
                    {
                        var d1 = Dir(P[pr[0]], P[pr[1]]); var d2 = Dir(P[pr[2]], P[pr[3]]);
                        double par = Math.Abs(d1.ux * d2.ux + d1.uy * d2.uy + d1.uz * d2.uz);
                        if (par > bestScore) { bestScore = par; best = pr; }
                    }
                sides = new[] { (P[best[0]], P[best[1]]), (P[best[2]], P[best[3]]) };
                method = $"most-parallel pairing ({bestScore:F2})";
            }

            var removedIds = incident.Select(mm => mm.Id).ToList();
            g.Members.RemoveAll(mm => mm.From == v.Id || mm.To == v.Id);
            g.Nodes.RemoveAll(x => x.Id == v.Id);
            var added = new List<string>(); var sn = new List<string>();
            double maxStock = stock != null && stock.Count > 0 ? stock.Max(s => s.Length) : double.PositiveInfinity;
            foreach (var (a, b) in sides)
            {
                if (Edge(a.Id, b.Id)) continue;     // already there
                double d = Dist(a, b);
                string fitId = null;
                if (stock != null && stock.Count > 0)
                    fitId = stock.Where(s => s.Length >= d - 1e-6).OrderBy(s => s.Length - d).FirstOrDefault()?.Id;
                string bid = $"{a.Id}_{b.Id}"; int k = 1;
                while (g.Members.Any(mm => mm.Id == bid)) bid = $"{a.Id}_{b.Id}.{k++}";
                g.Members.Add(new BridgeMember { Id = bid, From = a.Id, To = b.Id, Length = Round(d), AssignedStockId = fitId });
                added.Add(bid); sn.Add($"{a.Id}–{b.Id}({d:F2}m→{fitId ?? "?"}{(d > maxStock + 1e-6 ? ",>stock" : "")})");
            }

            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "uncrossPanel",
                RuleName = "Uncross Panel (X → parallel)",
                TargetId = v.Id,
                Note =
                    $"removed X centre {v.Id}{via} + its {removedIds.Count} spoke(s) and restored the 2 most-parallel " +
                    $"restored sides: [{string.Join(", ", sn)}] (via {method})\nbrace removal may affect stiffness; analyse the resulting structure independently",
                AddedMemberIds = added,
                RemovedMemberIds = removedIds,
            };
            return res;
        }

        // ---------------- 17. dedupeMembers (cleanup) -----------------
        // Hygiene rule: collapse PARALLEL members (more than one bar between the
        // same node pair) down to one, and drop zero-length / self-loop bars.
        // These accumulate from repeatedly applying add/connect rules and inflate
        // the member count (and the topology-count diagnostic). Keeps the member that carries a
        // stock assignment when there's a choice. Target = a node id (clean only
        // its neighbourhood) or "*"/"all"/anything-non-node (clean whole graph).
        public static GrammarRuleResult DedupeMembers(
            BridgeGraph g, string targetId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            bool removeZero = p?["removeZeroLength"]?.Value<bool?>() ?? true;
            var byId = g.Nodes.ToDictionary(n => n.Id);

            var scopeNode = g.Nodes.FirstOrDefault(n => n.Id == targetId);
            bool whole = scopeNode == null;
            bool InScope(BridgeMember m) => whole || m.From == scopeNode.Id || m.To == scopeNode.Id;

            var toRemove = new HashSet<string>();
            int zeroCount = 0, dupCount = 0;

            // self-loops + zero-length
            foreach (var m in g.Members)
            {
                if (!InScope(m) || toRemove.Contains(m.Id)) continue;
                bool selfLoop = m.From == m.To;
                bool zeroLen = removeZero && byId.TryGetValue(m.From, out var a) &&
                               byId.TryGetValue(m.To, out var b) && Dist(a, b) < 1e-6;
                if (selfLoop || zeroLen) { toRemove.Add(m.Id); zeroCount++; }
            }

            // duplicates: same unordered node pair → keep one (prefer assigned stock)
            var keep = new Dictionary<string, BridgeMember>();
            foreach (var m in g.Members)
            {
                if (!InScope(m) || toRemove.Contains(m.Id) || m.From == m.To) continue;
                string key = string.CompareOrdinal(m.From, m.To) <= 0
                    ? m.From + "|" + m.To : m.To + "|" + m.From;
                if (!keep.TryGetValue(key, out var prev)) { keep[key] = m; continue; }
                bool prevHasStock = !string.IsNullOrEmpty(prev.AssignedStockId);
                bool curHasStock = !string.IsNullOrEmpty(m.AssignedStockId);
                var loser = (curHasStock && !prevHasStock) ? prev : m;
                if (loser == prev) keep[key] = m;
                toRemove.Add(loser.Id); dupCount++;
            }

            if (toRemove.Count == 0)
            {
                res.Warnings.Add(
                    $"no duplicate or zero-length members in {(whole ? "the whole graph" : "node " + targetId + "'s neighbourhood")} — already clean.");
                return res;
            }

            int before = g.Members.Count;
            g.Members.RemoveAll(m => toRemove.Contains(m.Id));

            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "dedupeMembers",
                RuleName = "Dedupe Members (cleanup)",
                TargetId = whole ? "*" : targetId,
                Note =
                    $"cleaned {(whole ? "the whole graph" : "node " + targetId + "'s neighbourhood")}: removed " +
                    $"{dupCount} duplicate + {zeroCount} zero-length/self-loop = {toRemove.Count} member(s) " +
                    $"({before} → {g.Members.Count})",
                RemovedMemberIds = toRemove.ToList(),
            };
            return res;
        }

        // ---------------- 18. splitEdgeToPoint (R1 forward, explicit) -
        // Replace edge A–B by A–V and V–B and connect V to chosen brace points.
        // Stock fit determines the split position and optional off-line fold.
        // One existing brace point gives the planar Henneberg II count change
        // (Δn+1, Δm+2), without establishing rigidity. Inverse = dissolveNodeToDiagonal
        // (give bridgeNodes=[A,B]) or mergeSplitChain. Target = the edge id with
        // params "to", or a comma-separated edge/brace-node list.
        public static GrammarRuleResult SplitEdgeToPoint(
            BridgeGraph g, string targetId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();
            if (stock == null || stock.Count == 0)
            {
                res.Warnings.Add("splitEdgeToPoint is stock-driven — connect Stock JSON.");
                return res;
            }
            // ---- resolve the edge + the brace point(s) ----
        // Two brace points add one fold node and three net members; one brace
        // point adds one fold node and two net members. These count changes do
        // not establish rigidity or stability. Target "edge,P1,P2" or params
        // "to":["P1","P2"]. A Rhino Point on Apply's Pt snaps to a graph node
        // or creates a support node, which adds to the total node count.
            string edgeId = targetId;
            var braceIds = new List<string>();
            var toTok = p?["to"];
            if (toTok is Newtonsoft.Json.Linq.JArray toArr) braceIds.AddRange(toArr.Select(x => x.Value<string>()));
            else if (toTok != null && !string.IsNullOrEmpty(toTok.Value<string>())) braceIds.Add(toTok.Value<string>());
            var toks = (targetId ?? "").Split(new[] { ',', ' ', ';', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length >= 2) { edgeId = toks[0]; if (braceIds.Count == 0) braceIds.AddRange(toks.Skip(1)); }

            var m = g.Members.FirstOrDefault(x => x.Id == edgeId);
            if (m == null) { res.Warnings.Add($"edge '{edgeId}' not found."); return res; }
            var A = g.Nodes.FirstOrDefault(n => n.Id == m.From);
            var B = g.Nodes.FirstOrDefault(n => n.Id == m.To);
            if (A == null || B == null) { res.Warnings.Add("edge endpoints missing."); return res; }

            // Rhino point(s) on Pt → params "points": each snaps to a node, or
            // (isolated, e.g. a hinge support) becomes a NEW support node there.
            var addedSupportIds = new List<string>();
            var pointsTok = p?["points"] as Newtonsoft.Json.Linq.JArray;
            bool pointSupport = p?["pointSupport"]?.Value<bool?>() ?? true;
            double snapTol = p?["snapTol"]?.Value<double?>() ?? 0.25;
            if (pointsTok != null)
                foreach (var pt in pointsTok.OfType<Newtonsoft.Json.Linq.JArray>())
                    if (pt.Count == 3)
                        braceIds.Add(ResolveOrAddNode(g, pt[0].Value<double>(), pt[1].Value<double>(), pt[2].Value<double>(), snapTol, pointSupport, addedSupportIds));
            braceIds = braceIds.Where(id => id != A.Id && id != B.Id).Distinct().ToList();
            if (braceIds.Count == 0)
            { res.Warnings.Add("specify the point(s) to brace to — Target \"edge,P1,P2\", params \"to\":[\"P1\",\"P2\"], or a Point on Pt. Two points provide two braces; structural stability requires separate analysis."); return res; }
            var braceNodes = braceIds.Select(id => g.Nodes.FirstOrDefault(n => n.Id == id)).Where(n => n != null).ToList();
            if (braceNodes.Count != braceIds.Count)
            { res.Warnings.Add($"brace point id(s) not found: [{string.Join(",", braceIds)}]."); return res; }

            double maxStock = stock.Max(s => s.Length);
            double L = Dist(A, B);
            if (L < 1e-6) { res.Warnings.Add("edge is zero-length."); return res; }
            string FitId(double d) => stock.Where(s => s.Length >= d - 1e-6).OrderBy(s => s.Length - d).FirstOrDefault()?.Id;
            Func<double, double> off = d =>
            {
                if (d < 1e-6 || d > maxStock + 1e-6) return double.PositiveInfinity;
                var f = stock.Where(s => s.Length >= d - 1e-6).OrderBy(s => s.Length - d).FirstOrDefault();
                return f == null ? double.PositiveInfinity : f.Length - d;
            };

            Func<BridgeNode, double, double, double, double> D3 = (nd, x, y, z) =>
                Math.Sqrt((nd.X - x) * (nd.X - x) + (nd.Y - y) * (nd.Y - y) + (nd.Z - z) * (nd.Z - z));
            double ux = (B.X - A.X) / L, uy = (B.Y - A.Y) / L, uz = (B.Z - A.Z) / L;
            double pX = 0, pY = 0, pZ = 0, foldApex = 0;
            bool folded = false, done = false;

            // (1) USER-PLACED fold: params "foldAt":[x,y,z] (Apply's Fp point) →
            // search a small neighbourhood of THAT point for the spot whose
            // halves + braces best fit stock, staying as close to the wanted point
            // as possible. Put the fold where YOU want it (e.g. mid-panel for an
            // X) and only fine-tune for stock. foldSearch sets the radius.
            var foldAtTok = p?["foldAt"] as Newtonsoft.Json.Linq.JArray;
            if (foldAtTok != null && foldAtTok.Count == 3)
            {
                double fx = foldAtTok[0].Value<double>(), fy = foldAtTok[1].Value<double>(), fz = foldAtTok[2].Value<double>();
                double rad = p?["foldSearch"]?.Value<double?>() ?? 0.6;
                double bestS = double.PositiveInfinity, cx = fx, cy = fy, cz = fz;
                for (int pass = 0; pass < 2; pass++)
                {
                    double r = pass == 0 ? rad : rad / 5.0, st = r / 10.0;
                    for (double dx = -r; dx <= r + 1e-9; dx += st)
                        for (double dy = -r; dy <= r + 1e-9; dy += st)
                            for (double dz = -r; dz <= r + 1e-9; dz += st)
                            {
                                double vx = cx + dx, vy = cy + dy, vz = cz + dz;
                                double sum = off(D3(A, vx, vy, vz)) + off(D3(B, vx, vy, vz));
                                if (double.IsInfinity(sum)) continue;
                                bool ok = true;
                                foreach (var P in braceNodes) { double o = off(D3(P, vx, vy, vz)); if (double.IsInfinity(o)) { ok = false; break; } sum += o; }
                                if (!ok) continue;
                                double distF = Math.Sqrt((vx - fx) * (vx - fx) + (vy - fy) * (vy - fy) + (vz - fz) * (vz - fz));
                                double score = sum + 0.5 * distF;   // best stock fit, but stay near the wanted point
                                if (score < bestS) { bestS = score; pX = vx; pY = vy; pZ = vz; }
                            }
                    if (!double.IsInfinity(bestS)) { cx = pX; cy = pY; cz = pZ; }
                }
                if (double.IsInfinity(bestS))
                { res.Warnings.Add($"no buildable fold within {rad:F2}m of the Fp point keeps halves + braces within stock ({maxStock:F2}m). Move Fp, widen foldSearch, or add longer stock."); return res; }
                double tpr = (pX - A.X) * ux + (pY - A.Y) * uy + (pZ - A.Z) * uz;
                foldApex = D3(A, A.X + tpr * ux, A.Y + tpr * uy, A.Z + tpr * uz);
                foldApex = Math.Sqrt((pX - (A.X + tpr * ux)) * (pX - (A.X + tpr * ux)) + (pY - (A.Y + tpr * uy)) * (pY - (A.Y + tpr * uy)) + (pZ - (A.Z + tpr * uz)) * (pZ - (A.Z + tpr * uz)));
                folded = foldApex > 0.05; done = true;
            }

            // (2) AUTO fold: two halves at FULL stock length put V on a CIRCLE
            // (radius h ⟂ A–B); search the circle for the spot whose braces fit
            // stock with least offcut. The stock pair sets the height.
            if (!done)
            {
                double rx = 0, ry = 0, rz = 0;
                if (Math.Abs(ux) <= Math.Abs(uy) && Math.Abs(ux) <= Math.Abs(uz)) rx = 1; else if (Math.Abs(uy) <= Math.Abs(uz)) ry = 1; else rz = 1;
                double e1x = uy * rz - uz * ry, e1y = uz * rx - ux * rz, e1z = ux * ry - uy * rx;
                double e1l = Math.Sqrt(e1x * e1x + e1y * e1y + e1z * e1z); e1x /= e1l; e1y /= e1l; e1z /= e1l;
                double e2x = uy * e1z - uz * e1y, e2y = uz * e1x - ux * e1z, e2z = ux * e1y - uy * e1x;
                var lens = stock.Select(s => s.Length).Where(x => x <= maxStock + 1e-6).Distinct().OrderBy(x => x).ToList();
                double bScore = double.PositiveInfinity, bVx = 0, bVy = 0, bVz = 0;
                foreach (var La in lens)
                    foreach (var Lb in lens)
                    {
                        if (La + Lb <= L + 0.02 || Math.Abs(La - Lb) >= L) continue;
                        double sp = (La * La - Lb * Lb + L * L) / (2 * L);
                        double h2 = La * La - sp * sp; if (h2 < 1e-9) continue;
                        double h = Math.Sqrt(h2);
                        double ox = A.X + sp * ux, oy = A.Y + sp * uy, oz = A.Z + sp * uz;
                        for (int dg = 0; dg < 360; dg += 6)
                        {
                            double th = dg * Math.PI / 180.0, cth = Math.Cos(th), sth = Math.Sin(th);
                            double Vx = ox + h * (cth * e1x + sth * e2x), Vy = oy + h * (cth * e1y + sth * e2y), Vz = oz + h * (cth * e1z + sth * e2z);
                            double sumOff = 0; bool ok = true;
                            foreach (var P in braceNodes) { double o = off(D3(P, Vx, Vy, Vz)); if (double.IsInfinity(o)) { ok = false; break; } sumOff += o; }
                            if (!ok) continue;
                            double score = sumOff + 0.05 * (La + Lb - L);
                            if (score < bScore) { bScore = score; bVx = Vx; bVy = Vy; bVz = Vz; foldApex = h; }
                        }
                    }
                if (!double.IsInfinity(bScore)) { pX = bVx; pY = bVy; pZ = bVz; folded = true; done = true; }
                else if (braceNodes.Count == 1)
                {
                    var C = braceNodes[0];
                    double bw = double.PositiveInfinity, bestT = 0.5; double[] step = { 0.02, 0.004 };
                    for (int pass = 0; pass < 2; pass++)
                    {
                        double lo = pass == 0 ? 0.02 : Math.Max(0.01, bestT - 0.03);
                        double hi = pass == 0 ? 0.98 : Math.Min(0.99, bestT + 0.03);
                        for (double t = lo; t <= hi + 1e-9; t += step[pass])
                        {
                            double vX = A.X + t * (B.X - A.X), vY = A.Y + t * (B.Y - A.Y), vZ = A.Z + t * (B.Z - A.Z);
                            double w = off(t * L) + off((1 - t) * L) + off(D3(C, vX, vY, vZ));
                            if (w < bw) { bw = w; bestT = t; }
                        }
                    }
                    if (double.IsInfinity(bw)) { res.Warnings.Add($"no split of {m.Id} fits stock ({maxStock:F2}m) — split further or add longer stock."); return res; }
                    pX = A.X + bestT * (B.X - A.X); pY = A.Y + bestT * (B.Y - A.Y); pZ = A.Z + bestT * (B.Z - A.Z);
                    folded = false; done = true;
                    res.Warnings.Add("could not fold from stock — placed the split point ON the line (straight, 2-D only).");
                }
                else
                { res.Warnings.Add($"could not fold {m.Id} so the point reaches both braces [{string.Join(",", braceIds)}] within stock ({maxStock:F2}m). Pick nearer brace points or add longer stock."); return res; }
            }

            var V = new BridgeNode { Id = NextNodeId(g, "S", g.Nodes.Count), X = Math.Round(pX, 4), Y = Math.Round(pY, 4), Z = Math.Round(pZ, 4) };
            if (g.Nodes.Any(n => Dist(n, V) < 1e-3))
            { res.Warnings.Add("the split point coincides with an existing node."); return res; }

            g.Members.RemoveAll(x => x.Id == m.Id);
            g.Nodes.Add(V);
            double lVA = Dist(A, V), lVB = Dist(V, B);
            var added = new List<string>();
            var s1 = new BridgeMember { Id = $"{m.Id}.s1", From = A.Id, To = V.Id, Length = Round(lVA), AssignedStockId = FitId(lVA) };
            var s2 = new BridgeMember { Id = $"{m.Id}.s2", From = V.Id, To = B.Id, Length = Round(lVB), AssignedStockId = FitId(lVB) };
            g.Members.Add(s1); g.Members.Add(s2); added.Add(s1.Id); added.Add(s2.Id);
            var bn = new List<string>(); int ci = 0;
            foreach (var P in braceNodes)
            {
                double d = Dist(V, P);
                var c = new BridgeMember { Id = $"{m.Id}.c{++ci}", From = V.Id, To = P.Id, Length = Round(d), AssignedStockId = FitId(d) };
                g.Members.Add(c); added.Add(c.Id);
                bn.Add($"{c.Id}→{P.Id}({d:F2}m→{c.AssignedStockId ?? "?"})");
            }

            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "splitEdgeToPoint",
                RuleName = "Split Edge to Point (R1 forward)",
                TargetId = m.Id,
                Note =
                    $"split edge {m.Id} ({A.Id}–{B.Id}, L={L:F2}m) at point {V.Id}, braced to {braceNodes.Count} point(s) [{string.Join(",", braceIds)}] — " +
                    (folded ? $"FOLDED into an angle (apex {foldApex:F2}m; halves {lVA:F2}+{lVB:F2}>{L:F2})" : "placed ON the line (straight)") + "\n" +
                    $"fold node +1, Δm+{1 + braceNodes.Count} ({(braceNodes.Count >= 2 ? "two or more braces" : "one brace")}; topology change only, not a stability check)\n" +
                    $"halves {s1.Id}({lVA:F2}m→{s1.AssignedStockId ?? "?"}), {s2.Id}({lVB:F2}m→{s2.AssignedStockId ?? "?"}); braces [{string.Join(", ", bn)}]; removed {m.Id}" +
                    (addedSupportIds.Count > 0 ? $"\ncreated support node(s) for isolated point(s): [{string.Join(",", addedSupportIds)}]" : ""),
                AddedNodeIds = new List<string> { V.Id }.Concat(addedSupportIds).ToList(),
                AddedMemberIds = added,
                RemovedMemberIds = new List<string> { m.Id },
            };
            return res;
        }

        // ---------------- 19. relocateMember -------------------------
        // Move a member: remove the target member and add a NEW member between
        // two chosen endpoints (Δm = 0).
        // Endpoints may be node ids (Target "M,Pa,Pb") or Rhino points on Apply's
        // Pt (each snaps to a node, or an isolated point becomes a support node).
        // It is its own inverse — relocate back to undo. Stock-fits the new bar.
        public static GrammarRuleResult RelocateMember(
            BridgeGraph g, string targetId, JObject p, List<StockItem> stock)
        {
            var res = new GrammarRuleResult();

            string memId = p?["member"]?.Value<string>();
            var endIds = new List<string>();
            var toks = (targetId ?? "").Split(new[] { ',', ' ', ';', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in toks)
            {
                if (memId == null && g.Members.Any(mm => mm.Id == t)) memId = t;
                else if (g.Nodes.Any(n => n.Id == t)) endIds.Add(t);
            }
            if (p?["to"] is Newtonsoft.Json.Linq.JArray toArr)
                endIds.AddRange(toArr.Select(x => x.Value<string>()).Where(id => g.Nodes.Any(n => n.Id == id)));
            var addedSupportIds = new List<string>();
            var pointsTok = p?["points"] as Newtonsoft.Json.Linq.JArray;
            bool pointSupport = p?["pointSupport"]?.Value<bool?>() ?? true;
            double snapTol = p?["snapTol"]?.Value<double?>() ?? 0.25;
            if (pointsTok != null)
                foreach (var pt in pointsTok.OfType<Newtonsoft.Json.Linq.JArray>())
                    if (pt.Count == 3)
                        endIds.Add(ResolveOrAddNode(g, pt[0].Value<double>(), pt[1].Value<double>(), pt[2].Value<double>(), snapTol, pointSupport, addedSupportIds));

            if (memId == null)
            { res.Warnings.Add("specify the member to move — Target \"M,Pa,Pb\" or params \"member\":\"M\"."); return res; }
            endIds = endIds.Distinct().ToList();
            if (endIds.Count < 2)
            { res.Warnings.Add("specify the TWO endpoints for the new member — Target \"M,Pa,Pb\", params \"to\":[\"Pa\",\"Pb\"], or two Points on Pt."); return res; }

            var m = g.Members.First(x => x.Id == memId);
            var byId = g.Nodes.ToDictionary(n => n.Id);
            var A = byId[endIds[0]]; var B = byId[endIds[1]];
            if (A.Id == B.Id)
            { res.Warnings.Add("the two endpoints are the same node."); return res; }

            double maxStock = (stock != null && stock.Count > 0) ? stock.Max(s => s.Length) : double.PositiveInfinity;
            double d = Dist(A, B);
            bool exists = g.Members.Any(mm =>
                (mm.From == A.Id && mm.To == B.Id) || (mm.From == B.Id && mm.To == A.Id));

            g.Members.RemoveAll(x => x.Id == memId);
            string addedId = null, fitId = null;
            if (!exists)
            {
                if (stock != null && stock.Count > 0)
                    fitId = stock.Where(s => s.Length >= d - 1e-6).OrderBy(s => s.Length - d).FirstOrDefault()?.Id;
                string baseId = $"{A.Id}_{B.Id}"; addedId = baseId; int k = 1;
                while (g.Members.Any(mm => mm.Id == addedId)) addedId = $"{baseId}.{k++}";
                g.Members.Add(new BridgeMember { Id = addedId, From = A.Id, To = B.Id, Length = Round(d), AssignedStockId = fitId });
                if (d > maxStock + 1e-6)
                    res.Warnings.Add($"new member {A.Id}–{B.Id} = {d:F2}m > longest stock {maxStock:F2}m — unbuildable; split it (splitMemberByStockLength / splitEdgeToPoint).");
            }
            else
                res.Warnings.Add($"a member between {A.Id} and {B.Id} already exists — removed {memId} but added nothing (net −1 member).");

            res.Graph = g;
            res.Step = new GrammarRuleHistoryStep
            {
                RuleId = "relocateMember",
                RuleName = "Relocate Member (move a bar)",
                TargetId = memId,
                Note =
                    $"moved member: removed {memId} ({m.From}–{m.To}) and " +
                    (exists ? $"the new position {A.Id}–{B.Id} already had a bar (nothing added)"
                            : $"added {addedId} ({A.Id}–{B.Id}, {d:F2}m→{fitId ?? "?"})") +
                    (addedSupportIds.Count > 0 ? $"\ncreated support node(s) for isolated point(s): [{string.Join(",", addedSupportIds)}]" : ""),
                AddedNodeIds = addedSupportIds,
                AddedMemberIds = addedId != null ? new List<string> { addedId } : new List<string>(),
                RemovedMemberIds = new List<string> { memId },
            };
            return res;
        }

        // ---------------- helpers --------------------------------------

        // Resolve a 3-D point to a node: snap to the nearest existing node within
        // snapTol, else ADD a new node there. Isolated points in this workflow are
        // usually hinge supports, so a newly-created node is flagged IsSupport when
        // asSupport is true. Returns the node id; records new ids in `created`.
        private static string ResolveOrAddNode(BridgeGraph g, double x, double y, double z,
            double snapTol, bool asSupport, List<string> created)
        {
            BridgeNode near = null; double bestD = double.MaxValue;
            foreach (var n in g.Nodes)
            {
                double d = (n.X - x) * (n.X - x) + (n.Y - y) * (n.Y - y) + (n.Z - z) * (n.Z - z);
                if (d < bestD) { bestD = d; near = n; }
            }
            if (near != null && Math.Sqrt(bestD) <= snapTol) return near.Id;
            var nn = new BridgeNode
            {
                Id = NextNodeId(g, asSupport ? "SUP" : "P", g.Nodes.Count),
                X = Math.Round(x, 4), Y = Math.Round(y, 4), Z = Math.Round(z, 4),
            };
            if (asSupport) nn.IsSupport = true;
            g.Nodes.Add(nn);
            created?.Add(nn.Id);
            return nn.Id;
        }

        // Trailing ".x<n>" index of a crossBracePanel spoke id, else 0.
        private static int XIndex(string id)
        {
            if (id == null) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(id, @"\.x(\d+)$");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        // cos of the angle A–O–B (at vertex O).
        private static double AngleCos(BridgeNode a, BridgeNode o, BridgeNode b)
        {
            double ax = a.X - o.X, ay = a.Y - o.Y, az = a.Z - o.Z;
            double bx = b.X - o.X, by = b.Y - o.Y, bz = b.Z - o.Z;
            double la = Math.Sqrt(ax * ax + ay * ay + az * az);
            double lb = Math.Sqrt(bx * bx + by * by + bz * bz);
            if (la < 1e-9 || lb < 1e-9) return 1;
            return (ax * bx + ay * by + az * bz) / (la * lb);
        }

        private static double Dist(BridgeNode a, BridgeNode b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        private static double Round(double v) => Math.Round(v, 4);

        private static string NextNodeId(BridgeGraph g, string prefix, int seed)
        {
            int i = seed;
            string candidate;
            do
            {
                candidate = $"{prefix}{i++}";
            } while (g.Nodes.Any(n => n.Id == candidate));
            return candidate;
        }
    }
}
