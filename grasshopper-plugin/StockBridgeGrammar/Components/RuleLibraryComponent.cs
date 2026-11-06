using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using StockBridgeGrammar.Utils;

namespace StockBridgeGrammar.Components
{
    // Reference component — outputs the supported rule ids, human-readable
    // names, descriptions, and a ready-to-copy example Params JSON for each.
    // Wire any item out of this component into Apply Grammar Rule's inputs.
    public class RuleLibraryComponent : GH_Component
    {
        public RuleLibraryComponent()
          : base(
                "StockBridge Rule Library",
                "SBRules?",
                "List every supported grammar rule id with name, description, " +
                "and a ready-to-copy example Params JSON. Pair this with " +
                "Apply Grammar Rule so users don't have to memorise the rule ids.",
                "StockBridge", "0. Helpers")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-7777-7777-7777-700000000007");
        protected override Bitmap Icon => IconFactory.RuleLibrary;
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager _) { }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Rule IDs",        "ID",  "Rule id string (copy into Apply Grammar Rule).",   GH_ParamAccess.list);
            p.AddTextParameter("Rule Names",      "N",   "Human-readable rule name.",                          GH_ParamAccess.list);
            p.AddTextParameter("Rule Targets",    "T",   "What target type the rule acts on (member / bay / node).", GH_ParamAccess.list);
            p.AddTextParameter("Rule Descriptions","D",  "One-line description.",                              GH_ParamAccess.list);
            p.AddTextParameter("Example Params",  "P",   "Ready-to-paste default Params JSON per rule.",       GH_ParamAccess.list);
            p.AddTextParameter("Summary",         "S",   "Single multi-line panel summary for quick reference.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var rules = new[]
            {
                new RuleDoc {
                    Id     = "splitMemberByStockLength",
                    Name   = "Split Member by Stock Length",
                    Target = "member",
                    Desc   = "Member longer than the longest stock piece → cut into stock-sized parts (greedy, offcut returns); new cut nodes are triangulated into nearby structure. Endpoints never move.",
                    Params = "{\"maxBridgesPerNode\": 2}",
                },
                new RuleDoc {
                    Id     = "addConnectionPoint",
                    Name   = "Add Connection Point",
                    Target = "member",
                    Desc   = "Add ONE new node at the centroid of (a, b + nearest non-coplanar existing nodes) and connect to them — a 3-D braced interior hub. Member is NOT cut.",
                    Params = "{\"extraAnchors\": 2}",
                },
                new RuleDoc {
                    Id     = "triangulateBay",
                    Name   = "Triangulate (close a triangle)",
                    Target = "member",
                    Desc   = "Add ONE edge between two existing nodes that share a common neighbour (closing a triangle), within one stock piece. No new node.",
                    Params = "{}",
                },
                new RuleDoc {
                    Id     = "shiftNodeToMatchStock",
                    Name   = "Shift Node to Match Stock",
                    Target = "node, or member (its endpoint)",
                    Desc   = "Move the node freely in 3-D to the position that best aligns its incident members to available stock lengths (along the bar and/or kinked off-axis — whichever fits stock). Every incident member stays ≤ longest stock; no collision; supports never move.",
                    Params = "{}",
                },
                new RuleDoc {
                    Id     = "removeConnectionPoint",
                    Name   = "Remove Connection Point",
                    Target = "node (degree 2)",
                    Desc   = "Remove a degree-2 node, merge its two members into one. Refused if the merged member would exceed the longest stock piece.",
                    Params = "{}",
                },
                new RuleDoc {
                    Id     = "densifyWithShortStock",
                    Name   = "Densify with Short Stock",
                    Target = "member",
                    Desc   = "Subdivide the member to the finest granularity the SHORTEST stock allows (use up short pieces); each new node is triangulated in.",
                    Params = "{}",
                },
                new RuleDoc {
                    Id     = "fanOutLoadPath",
                    Name   = "Fan Out Load Path",
                    Target = "member (its lower-degree endpoint)",
                    Desc   = "From the member's lower-degree endpoint, fan several new edges to nearby existing nodes (each ≤ one stock piece). No new node.",
                    Params = "{\"fanCount\": 3}",
                },
                new RuleDoc {
                    Id     = "removeRedundantMember",
                    Name   = "Remove Redundant Member",
                    Target = "member",
                    Desc   = "Remove the member only if BOTH endpoints stay connected (degree ≥ 2 each afterwards); otherwise refused as structural.",
                    Params = "{}",
                },
                new RuleDoc {
                    Id     = "articulateChainToStock",
                    Name   = "Articulate Chain to Stock (fold)",
                    Target = "split-chain prefix (e.g. M0) or sub-bar id",
                    Desc   = "Chain AFTER split: rebuild each sub-bar at its FULL stock length and fold the straight chain into an arch using the offcut slack (endpoints pinned, zero cutting waste). Refuses if the chain is taut (no slack).",
                    Params = "{\"foldDown\": false}",
                },
                new RuleDoc {
                    Id     = "connectNodeToStock",
                    Name   = "Connect Node to Stock (brace)",
                    Target = "node id, or split-chain prefix (e.g. M0)",
                    Desc   = "Brace the target node(s) to other nodes within one stock length (≤ longest piece), up to N each, every brace assigned the shortest stock that spans it. Needs other nodes in reach.",
                    Params = "{\"maxConnectionsPerNode\": 2}",
                },
                new RuleDoc {
                    Id     = "mergeSplitChain",
                    Name   = "Merge Split Chain (inverse of Split)",
                    Target = "split-chain prefix (e.g. M0) or sub-bar id",
                    Desc   = "INVERSE of split→articulate→connect: remove the new interior node(s), sub-bars (.s*) and braces (.b*/.c*) and re-add the single original straight member. Interior nodes used by outside members are kept.",
                    Params = "{}",
                },
                new RuleDoc {
                    Id     = "addNodeToStock",
                    Name   = "Add Spatial Node to Stock",
                    Target = "3 Position anchors / 3 node ids / member region; separate Target Point guides placement",
                    Desc   = "Connect a new node to THREE distinct non-collinear anchors using three available physical stock pieces at their original lengths. Solve the three sphere intersections and choose the non-coplanar solution nearest Target Point. Reserve recorded stock allocations, reject insufficient stock, collisions and heights below minHeight. No planar or cut-to-fit fallback. Position supplies anchors; Target Point supplies the approximate desired location.",
                    Params = "{\"degree\":3,\"minHeight\":0.01}",
                },
                new RuleDoc {
                    Id     = "removeNode",
                    Name   = "Remove Node (inverse of Add Node)",
                    Target = "node id",
                    Desc   = "Delete a node and every member incident to it (inverse of addNodeToStock). Supports are never removed. Analyse the resulting structure independently; topology counts do not detect mechanisms or establish stability.",
                    Params = "{}",
                },
                new RuleDoc {
                    Id     = "dissolveNodeToDiagonal",
                    Name   = "Dissolve Node to Diagonal (Henneberg II inverse)",
                    Target = "hub node id, a spoke member, or 2+ spoke ids",
                    Desc   = "General inverse of split: remove a ~degree-3 hub + its edges and re-add ONE diagonal between two neighbours. Default = straightest non-existing pair (the original through-member); pairBy:\"stock\" picks the least-offcut buildable pair. Identify the hub by node id, one spoke, or several converging spoke ids (Target \"M1,M2,M3\" or params spokes[]). Guarded by maxHubDegree (default 4).",
                    Params = "{\"pairBy\": \"stock\"}",
                },
                new RuleDoc {
                    Id     = "crossBracePanel",
                    Name   = "Cross-Brace Panel (parallel → X)",
                    Target = "two side member ids (\"M1,M2\") or params sides[]",
                    Desc   = "Replace two members with four distinct endpoints by a centre node and four stock-fit spokes to the corners (the original members are removed unless keepSides). This changes connectivity; it does not establish rigidity or structural stability.",
                    Params = "{\"sides\": [\"M1\", \"M2\"]}",
                },
                new RuleDoc {
                    Id     = "uncrossPanel",
                    Name   = "Uncross Panel (X → parallel)",
                    Target = "X centre node id (degree 4) or a spoke",
                    Desc   = "Inverse of crossBracePanel: remove the X centre + its 4 spokes and restore the 2 original members (recovered from the .x spoke order; falls back to most-parallel pairing). Brace removal may affect stiffness; analyse the resulting structure independently.",
                    Params = "{}",
                },
                new RuleDoc {
                    Id     = "splitEdgeToPoint",
                    Name   = "Split Edge to Point (R1 forward)",
                    Target = "\"edge,P1,P2\" or edge id + params to=[P1,P2]",
                    Desc   = "Split edge A–B, fold the two halves according to stock fit, and brace the fold point to chosen points. Two existing brace nodes give Δn+1, Δm+3; one gives Δn+1, Δm+2. These are topology counts, not rigidity or stability guarantees. Removes A–B, adds A–V, V–B and the braces. Inverse = dissolveNodeToDiagonal.",
                    Params = "{\"to\": [\"P1\", \"P2\"]}",
                },
                new RuleDoc {
                    Id     = "relocateMember",
                    Name   = "Relocate Member",
                    Target = "\"M,Pa,Pb\" (member to move + 2 new endpoints)",
                    Desc   = "Move a bar: remove the target member and add a new one between two chosen endpoints (Δm=0, pure repositioning). Endpoints = node ids in Target, or Rhino points on Pt (isolated point → new support node). Its own inverse. Stock-fits the new bar.",
                    Params = "{}",
                },
                new RuleDoc {
                    Id     = "dedupeMembers",
                    Name   = "Dedupe Members (cleanup)",
                    Target = "\"*\"/\"all\" (whole graph) or a node id",
                    Desc   = "Collapse parallel members (multiple bars between the same node pair) to one, and drop zero-length / self-loop bars. Removes duplicate members from the topology count. Keeps the bar that has a stock assignment.",
                    Params = "{\"removeZeroLength\": true}",
                },
            };

            var ids    = new List<string>();
            var names  = new List<string>();
            var tgts   = new List<string>();
            var descs  = new List<string>();
            var prms   = new List<string>();
            var summary = new System.Text.StringBuilder();
            summary.AppendLine("Supported rule ids (paste into Apply Grammar Rule → Rule ID):");
            summary.AppendLine();
            foreach (var r in rules)
            {
                ids.Add(r.Id);
                names.Add(r.Name);
                tgts.Add(r.Target);
                descs.Add(r.Desc);
                prms.Add(r.Params);
                summary.AppendLine($"• {r.Id}");
                summary.AppendLine($"    name   : {r.Name}");
                summary.AppendLine($"    target : {r.Target}");
                summary.AppendLine($"    desc   : {r.Desc}");
                summary.AppendLine($"    params : {r.Params}");
                summary.AppendLine();
            }
            DA.SetDataList(0, ids);
            DA.SetDataList(1, names);
            DA.SetDataList(2, tgts);
            DA.SetDataList(3, descs);
            DA.SetDataList(4, prms);
            DA.SetData    (5, summary.ToString());
        }

        private class RuleDoc
        {
            public string Id, Name, Target, Desc, Params;
        }
    }
}
