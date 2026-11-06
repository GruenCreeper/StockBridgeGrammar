using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Newtonsoft.Json.Linq;
using Rhino.Geometry;
using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;
using StockBridgeGrammar.Utils;

namespace StockBridgeGrammar.Components
{
    public class ApplyGrammarRuleComponent : GH_Component
    {
        public ApplyGrammarRuleComponent()
          : base(
                "StockBridge Apply Grammar Rule",
                "SBApplyRule",
                "Apply ONE shape grammar rule on a chosen target with the " +
                "chosen parameters. Returns the transformed graph + a " +
                "history step. For addNodeToStock, Position contains three anchors and " +
                "Target Point guides a spatial node fitted to three available stock pieces. " +
                "Supported rule ids: " + string.Join(", ", GrammarRules.SupportedIds),
                "StockBridge", "3. Grammar")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-3333-3333-3333-300000000003");
        protected override Bitmap Icon => IconFactory.Apply;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Graph JSON", "G", "BridgeGraph JSON",
                GH_ParamAccess.item);
            p.AddTextParameter("Rule ID", "R",
                "Rule id (e.g. splitMemberByStockLength). " +
                "See Rule Library for the supported list.",
                GH_ParamAccess.item);
            p[1].Optional = true; // Empty Next Rule from Section Check is a valid pass-through.
            p.AddTextParameter("Target ID", "T",
                "Member or node id for the rule. Optional for addNodeToStock when anchors " +
                "are supplied by Position/corners or inferred near Target Point.",
                GH_ParamAccess.item);
            p[2].Optional = true;
            p.AddTextParameter("Params JSON", "P",
                "JSON object of rule parameters (e.g. {\"minHeight\":0.05} for addNodeToStock).",
                GH_ParamAccess.item, "{}");
            p[3].Optional = true;
            p.AddTextParameter("Stock JSON", "S",
                StockInput.Description + " Required by rules that create or fit members.",
                GH_ParamAccess.tree, "[]");
            p[4].Optional = true;
            p.AddPointParameter("Position", "Pt",
                "Connection anchors. addNodeToStock requires exactly THREE distinct, " +
                "non-collinear points; use Target Point (Tp) for the desired new-node location. " +
                "Points within snapTol (default 0.25m) snap to existing nodes; isolated points " +
                "become support nodes on success unless pointSupport=false. " +
                "For splitEdgeToPoint these are the points to brace to.",
                GH_ParamAccess.list);
            p[5].Optional = true;
            p.AddPointParameter("Fold Point", "Fp",
                "Optional. For splitEdgeToPoint: the DESIRED location of the fold/split point. " +
                "The rule searches a small neighbourhood of it (foldSearch radius) for the best " +
                "stock fit, staying near your point. Injected as params \"foldAt\":[x,y,z].",
                GH_ParamAccess.item);
            p[6].Optional = true;
            // Append new inputs so existing saved definitions keep their port indices.
            p.AddPointParameter("Target Point", "Tp",
                "Desired approximate location for addNodeToStock. Among non-coplanar " +
                "positions that use three available stock pieces at their original lengths, " +
                "choose the one nearest this point. Connect a Rhino point directly, or the " +
                "Target Picker's Tp output. Injected as params targetPoint=[x,y,z].",
                GH_ParamAccess.item);
            p[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Updated Graph JSON", "G", "Transformed graph",
                GH_ParamAccess.item);
            p.AddLineParameter("Updated Member Lines", "L",
                "All members of the transformed graph",
                GH_ParamAccess.list);
            p.AddPointParameter("Updated Node Points", "N",
                "All nodes of the transformed graph",
                GH_ParamAccess.list);
            p.AddTextParameter("Rule History Step", "H",
                "Human-readable summary: rule, target, params, delta " +
                "(+nodes / +members / −members).",
                GH_ParamAccess.list);
            p.AddTextParameter("Effective Params", "EP",
                "The parameter JSON actually used (after defaults filled in).",
                GH_ParamAccess.item);
            p.AddTextParameter("Warnings", "W",
                "Refusal messages if the rule could not act.",
                GH_ParamAccess.list);
        }

        private static readonly Dictionary<string, JObject> RuleDefaults =
            new Dictionary<string, JObject>
        {
            { "splitMemberByStockLength", JObject.Parse("{\"maxBridgesPerNode\":2}") },
            { "addConnectionPoint",       JObject.Parse("{\"extraAnchors\":2}") },
            { "removeConnectionPoint",    JObject.Parse("{}") },
            { "triangulateBay",           JObject.Parse("{}") },
            { "densifyWithShortStock",    JObject.Parse("{}") },
            { "fanOutLoadPath",           JObject.Parse("{\"fanCount\":3}") },
            { "removeRedundantMember",    JObject.Parse("{}") },
            { "shiftNodeToMatchStock",    JObject.Parse("{}") },
            { "articulateChainToStock",   JObject.Parse("{}") },
            { "connectNodeToStock",       JObject.Parse("{\"maxConnectionsPerNode\":2}") },
            { "mergeSplitChain",          JObject.Parse("{}") },
            { "addNodeToStock",           JObject.Parse("{\"degree\":3,\"minHeight\":0.01}") },
            { "removeNode",               JObject.Parse("{}") },
            { "dissolveNodeToDiagonal",   JObject.Parse("{}") },
            { "crossBracePanel",          JObject.Parse("{}") },
            { "uncrossPanel",             JObject.Parse("{}") },
            { "dedupeMembers",            JObject.Parse("{\"removeZeroLength\":true}") },
            { "splitEdgeToPoint",         JObject.Parse("{}") },
            { "relocateMember",           JObject.Parse("{}") },
        };

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            bool result = base.Read(reader);
            StockInput.Upgrade(this, 4);
            return result;
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Message = "";
            string graphJson = null, ruleId = null, targetId = null;
            string paramsJson = "{}";
            if (!DA.GetData(0, ref graphJson) || string.IsNullOrWhiteSpace(graphJson))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Graph JSON is empty. Connect the Graph JSON output of " +
                    "Recognize Structure or Assign Inventory.");
                return;
            }
            if (!DA.GetData(1, ref ruleId) || string.IsNullOrWhiteSpace(ruleId))
            {
                // Empty rule = nothing to do (e.g. the section-check loop reports
                // no fix left). Pass the graph through unchanged instead of
                // erroring, so feeding Section Check's "Next Rule" output is safe.
                DA.SetData(0, graphJson);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Rule ID empty — graph passed through unchanged (nothing to apply).");
                return;
            }
            ruleId = ruleId.Trim();
            DA.GetData(2, ref targetId);
            if (string.IsNullOrWhiteSpace(targetId) && ruleId != "addNodeToStock")
            {
                DA.SetData(0, graphJson);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Target ID empty — graph passed through unchanged (nothing to apply).");
                return;
            }
            // Trim stray whitespace/newlines from Panel text so " mergeSplitChain"
            // (a leading space) still matches the supported id.
            targetId = (targetId ?? "").Trim();
            DA.GetData(3, ref paramsJson);
            var positions = new List<Point3d>();
            DA.GetDataList(5, positions);
            Point3d foldPt = Point3d.Unset;
            bool gotFold = DA.GetData(6, ref foldPt);
            Point3d targetPt = Point3d.Unset;
            bool gotTargetPoint = DA.GetData(7, ref targetPt);

            if (System.Array.IndexOf(GrammarRules.SupportedIds, ruleId) < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Unsupported Rule ID: '{ruleId}'. Supported: " +
                    string.Join(", ", GrammarRules.SupportedIds));
                return;
            }

            BridgeGraph graph;
            List<StockItem> stock;
            JObject ps;
            try
            {
                graph = JsonIO.DeserializeGraph(graphJson);
                stock = StockInput.Read(DA, 4, this);
                ps    = string.IsNullOrWhiteSpace(paramsJson)
                        ? new JObject()
                        : JObject.Parse(paramsJson);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Params JSON could not be parsed — using rule defaults. " +
                    ex.Message);
                try
                {
                    graph = JsonIO.DeserializeGraph(graphJson);
                    stock = StockInput.Read(DA, 4, this);
                    ps    = new JObject();
                }
                catch (Exception ex2)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "Graph or Stock JSON parse failed: " + ex2.Message);
                    return;
                }
            }

            // Fill in defaults so the user always knows what was used.
            JObject effective = new JObject();
            if (RuleDefaults.TryGetValue(ruleId, out var defaults))
                foreach (var prop in defaults.Properties())
                    effective[prop.Name] = prop.Value.DeepClone();
            foreach (var prop in ps.Properties())
                effective[prop.Name] = prop.Value.DeepClone();
            // Position supplies anchors. For spatial addNodeToStock its first
            // anchor must never overwrite the separate desired location.
            if (positions != null && positions.Count > 0)
            {
                var pts = new JArray();
                foreach (var pt in positions)
                {
                    if (!pt.IsValid && ruleId == "addNodeToStock")
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            "Position contains an invalid anchor point.");
                        return;
                    }
                    if (pt.IsValid) pts.Add(new JArray(pt.X, pt.Y, pt.Z));
                }
                if (pts.Count > 0)
                {
                    effective["points"] = pts;
                    if (ruleId != "addNodeToStock") effective["at"] = pts[0].DeepClone();
                }
            }
            if (gotTargetPoint)
            {
                if (!targetPt.IsValid)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Target Point is invalid.");
                    return;
                }
                effective["targetPoint"] = new JArray(targetPt.X, targetPt.Y, targetPt.Z);
            }
            // A connected Fold Point → params "foldAt" (desired split-point spot).
            if (gotFold && foldPt.IsValid)
                effective["foldAt"] = new JArray(
                    Math.Round(foldPt.X, 4), Math.Round(foldPt.Y, 4), Math.Round(foldPt.Z, 4));
            string effectiveJson = effective.ToString(Newtonsoft.Json.Formatting.Indented);

            // Target id must exist somewhere — EXCEPT articulateChainToStock,
            // whose natural target is the ORIGINAL member id (e.g. "M0") that
            // splitMemberByStockLength already removed, replacing it with sub-
            // bars {id}.s1, {id}.s2 … So also accept a target that prefixes an
            // existing split chain.
            bool targetExists =
                graph.Members.Exists(m => m.Id == targetId) ||
                graph.Nodes.Exists(n => n.Id == targetId);
            bool isSplitChainPrefix =
                graph.Members.Exists(m => m.Id != null && m.Id.StartsWith(targetId + ".s"));
            // Some rules accept a multi-token target — several ids separated by
            // comma/space (converging spokes for dissolve/uncross, or several
            // nodes for removeNode). Accept it here if every token is a real
            // member OR node id.
            var tokens = targetId.Split(new[] { ',', ' ', ';', '\t', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            bool isMultiSpoke = tokens.Length >= 2 &&
                System.Array.TrueForAll(tokens, t =>
                    graph.Members.Exists(m => m.Id == t) || graph.Nodes.Exists(n => n.Id == t));
            // dedupeMembers accepts any target ("*"/"all" = whole graph, or a
            // node id to scope) so it is exempt from the existence check.
            // The spatial rule validates its own anchor modes; a member/node ID
            // is not required when points, corners or a target point provide them.
            if (!targetExists && !isSplitChainPrefix && !isMultiSpoke &&
                ruleId != "dedupeMembers" && ruleId != "addNodeToStock")
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Target ID '{targetId}' not found in the graph " +
                    "(no member or node with this id).");
                return;
            }

            var res = GrammarRules.Apply(graph, ruleId, targetId, effective, stock);
            if (res?.Graph == null)
            {
                DA.SetData    (0, graphJson);
                DA.SetDataList(3, new List<string> { "(rule did not apply)" });
                DA.SetData    (4, effectiveJson);
                DA.SetDataList(5, res?.Warnings ?? new List<string> { "rule did not apply" });
                if (res?.Warnings != null && res.Warnings.Count > 0)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        string.Join(" · ", res.Warnings));
                return;
            }

            // Lines + points for preview.
            var lines = new List<Line>();
            var points = new List<Point3d>();
            var byId = new Dictionary<string, BridgeNode>();
            foreach (var n in res.Graph.Nodes)
            {
                byId[n.Id] = n;
                points.Add(new Point3d(n.X, n.Y, n.Z));
            }
            foreach (var m in res.Graph.Members)
            {
                if (!byId.TryGetValue(m.From, out var a)) continue;
                if (!byId.TryGetValue(m.To,   out var b)) continue;
                lines.Add(new Line(
                    new Point3d(a.X, a.Y, a.Z),
                    new Point3d(b.X, b.Y, b.Z)));
            }
            var history = new List<string>();
            if (res.Step != null)
            {
                int addedN = res.Step.AddedNodeIds?.Count ?? 0;
                int addedM = res.Step.AddedMemberIds?.Count ?? 0;
                int rmM    = res.Step.RemovedMemberIds?.Count ?? 0;
                history.Add(
                    $"rule    : {res.Step.RuleName} ({res.Step.RuleId})\n" +
                    $"target  : {res.Step.TargetId}\n" +
                    $"params  : {effectiveJson.Replace("\n", " ")}\n" +
                    $"delta   : +{addedN} nodes, +{addedM} members, −{rmM} members\n" +
                    $"note    : {res.Step.Note}");
            }

            DA.SetData    (0, JsonIO.Serialize(res.Graph));
            DA.SetDataList(1, lines);
            DA.SetDataList(2, points);
            DA.SetDataList(3, history);
            DA.SetData    (4, effectiveJson);
            DA.SetDataList(5, res.Warnings);
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Applied {ruleId} on {targetId}");
        }
    }
}
