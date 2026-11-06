using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;
using StockBridgeGrammar.Utils;

namespace StockBridgeGrammar.Components
{
    // Pick a member/node ID, and forward the original target-point coordinate
    // independently so a spatial grammar can use it as a location preference.
    public class TargetPickerComponent : GH_Component
    {
        public TargetPickerComponent()
          : base(
                "StockBridge Target Picker",
                "SBTarget",
                "Choose a Target ID for Apply Grammar Rule WITHOUT searching " +
                "JSON by hand. With Mode unwired, the priority is ID, then " +
                "Target Curve, then Target Point, then mismatch rank. Or pick a MEMBER (by index, " +
                "or a selected Rhino curve), or a NODE (by_point = a Rhino point " +
                "snapped to the nearest node, or by_node_id) — node modes feed " +
                "removeNode / dissolveNodeToDiagonal / uncrossPanel. " +
                "The Tp output forwards the original Target Point for Apply's " +
                "spatial addNodeToStock placement; connect Tp separately from T.",
                "StockBridge", "0. Helpers")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-bbbb-bbbb-bbbb-b00000000011");
        protected override Bitmap Icon => IconFactory.TargetPicker;
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter   ("Graph JSON",     "G",  "Graph JSON from Recognize or Assign.",                                       GH_ParamAccess.item);
            p.AddTextParameter   ("Mismatches JSON","M",  "Mismatches JSON from Assign Inventory (optional, for by_mismatch_rank).",    GH_ParamAccess.item, "[]");
            p[1].Optional = true;
            p.AddTextParameter   ("Mode",           "Mo", "Leave unwired for priority Id > Target Curve > Target Point > mismatch rank. Connect a mode to override: by_mismatch_rank | by_member_id | by_index | by_curve | by_point | by_node_id.", GH_ParamAccess.item, "by_mismatch_rank");
            p.AddTextParameter   ("Target ID",      "Id", "Enter an exact member ID (M0, M1...) or node ID (N0, N1...). With Mode unwired, a nonempty ID wins over Curve and Point; whitespace is ignored.", GH_ParamAccess.item, "");
            p[3].Optional = true;
            p.AddIntegerParameter("Mismatch Rank",  "R",  "Used when Mode = by_mismatch_rank (0 = most severe).",                        GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Member Index",   "I",  "Used when Mode = by_index (0..members.Count-1).",                             GH_ParamAccess.item, 0);
            p.AddTextParameter   ("Role",           "Ro", "Deprecated. Kept for canvas compatibility; role/chord classification is ignored.", GH_ParamAccess.item, "");
            p[6].Optional = true;
            p.AddCurveParameter  ("Target Curve",   "C",  "Select ONE Rhino curve/line. With Mode unwired, used when Id is empty; or set Mode = by_curve. Matches member endpoints, with nearest-midpoint fallback.", GH_ParamAccess.item);
            p[7].Optional = true;
            p.AddPointParameter  ("Target Point",   "Pt", "With Mode unwired, picks the nearest existing NODE when Id and Curve are empty. The original coordinate is always forwarded via Tp for Apply.Tp (for example addNodeToStock).", GH_ParamAccess.item);
            p[8].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Target ID",       "T",  "The chosen target id.",          GH_ParamAccess.item);
            p.AddTextParameter("Target Type",     "TT", "member / node / system.",        GH_ParamAccess.item);
            p.AddTextParameter("Target Summary",  "S",  "Human-readable target summary.", GH_ParamAccess.item);
            p.AddTextParameter("Suggested Rule",  "Sr", "Suggested rule id if the source was a mismatch.", GH_ParamAccess.item);
            p.AddTextParameter("Warnings",        "W",  "Validation notes.",              GH_ParamAccess.list);
            // Appended output preserves all existing output indices.
            p.AddPointParameter("Target Point", "Tp",
                "Original Target Point coordinate, independent of the selected ID. " +
                "Connect to Apply.Target Point (Tp) to guide addNodeToStock's spatial location.",
                GH_ParamAccess.item);
        }

        // Kept separate from curve geometry matching so automatic precedence is
        // deterministic even when all three target inputs are populated.
        internal static string ResolveAutomaticMode(string requestedMode, bool modeWired,
            bool hasId, bool hasCurve, bool hasPoint)
        {
            if (modeWired || requestedMode != "by_mismatch_rank") return requestedMode;
            if (hasId) return "auto_id";
            if (hasCurve) return "by_curve";
            if (hasPoint) return "by_point";
            return "by_mismatch_rank";
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Message = "";
            string graphJson = null, mmJson = "[]", mode = "by_mismatch_rank",
                   memberId = "", role = "";
            int rank = 0, idx = 0;
            if (!DA.GetData(0, ref graphJson) || string.IsNullOrWhiteSpace(graphJson))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Graph JSON is empty.");
                return;
            }
            Curve targetCurve = null;
            Point3d targetPoint = Point3d.Unset;
            DA.GetData(1, ref mmJson);
            DA.GetData(2, ref mode);
            DA.GetData(3, ref memberId);
            DA.GetData(4, ref rank);
            DA.GetData(5, ref idx);
            DA.GetData(6, ref role);
            DA.GetData(7, ref targetCurve);
            bool gotPoint = DA.GetData(8, ref targetPoint);
            memberId = (memberId ?? "").Trim();
            string requestedMode = (mode ?? "by_mismatch_rank").Trim().ToLowerInvariant();
            bool modeWired = Params.Input[2].SourceCount > 0;
            string resolvedMode = ResolveAutomaticMode(requestedMode, modeWired,
                memberId.Length > 0, targetCurve != null && targetCurve.IsValid,
                gotPoint && targetPoint.IsValid);
            bool autoId = resolvedMode == "auto_id";
            bool autoCurve = resolvedMode == "by_curve" && !modeWired && requestedMode == "by_mismatch_rank";
            bool autoPoint = resolvedMode == "by_point" && !modeWired && requestedMode == "by_mismatch_rank";

            BridgeGraph graph;
            List<GrammarMismatch> mismatches;
            try
            {
                graph = JsonIO.DeserializeGraph(graphJson);
                // Only parse mismatches if mismatch rank will actually be used.
                mismatches = resolvedMode == "by_mismatch_rank"
                    ? JsonIO.DeserializeMismatches(mmJson)
                    : new List<GrammarMismatch>();
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "JSON parse failed: " + ex.Message);
                return;
            }

            var warns = new List<string>();
            string targetId = "";
            string targetType = "member";
            string summary = "";
            string suggested = "";
            string effectiveMode = resolvedMode;
            if (autoId)
            {
                bool isMember = graph.Members.Any(m => m.Id == memberId);
                bool isNode = graph.Nodes.Any(n => n.Id == memberId);
                if (isMember && isNode)
                    warns.Add($"ID '{memberId}' matches both a member and a node. Connect Mo = by_member_id or by_node_id to choose explicitly.");
                else if (isMember)
                    effectiveMode = "by_member_id";
                else if (isNode)
                    effectiveMode = "by_node_id";
                else
                    warns.Add($"ID '{memberId}' was not found in the current Graph JSON. Check the displayed M/N labels or connect an updated graph.");
                if (warns.Count > 0) effectiveMode = "invalid_id";
            }
            else if (autoCurve)
                effectiveMode = "by_curve";
            else if (autoPoint)
                effectiveMode = "by_point";
            else if (memberId.Length > 0 && requestedMode != "by_member_id" && requestedMode != "by_node_id")
                warns.Add($"Id '{memberId}' is ignored while Mode = '{requestedMode}'. Set Mo to by_member_id / by_node_id, or disconnect Mo to select by ID automatically.");

            // A higher-priority ID intentionally overrides the curve. Warn only
            // for an explicitly selected non-curve mode or an invalid curve.
            if (targetCurve != null &&
                !targetCurve.IsValid)
                warns.Add("Target Curve is invalid; using the next available selection input.");
            else if (targetCurve != null && effectiveMode != "by_curve" && !autoId)
            {
                warns.Add(
                    $"Target Curve is ignored because Mode = '{mode}'. " +
                    "Set Mode = by_curve or disconnect Mo to pick by curve automatically.");
            }

            switch (effectiveMode)
            {
                case "invalid_id":
                    break;
                case "by_member_id":
                    if (graph.Members.Any(m => m.Id == memberId))
                    {
                        var m = graph.Members.First(x => x.Id == memberId);
                        targetId = m.Id;
                        summary = $"{m.Id}  {m.From}→{m.To}  L={m.Length:F2}m";
                    }
                    else warns.Add($"member id '{memberId}' not found");
                    break;

                case "by_index":
                    if (graph.Members.Count == 0)
                    { warns.Add("graph has no members"); break; }
                    int safeIdx = Math.Max(0, Math.Min(graph.Members.Count - 1, idx));
                    if (safeIdx != idx) warns.Add($"index {idx} clamped to {safeIdx}");
                    var mi = graph.Members[safeIdx];
                    targetId = mi.Id;
                    summary = $"[{safeIdx}] {mi.Id}  {mi.From}→{mi.To}  L={mi.Length:F2}m";
                    break;

                case "by_role":
                    // Deprecated role selection falls back to member 0.
                    warns.Add("by_role mode is deprecated (Role classification removed). Falling back to first member.");
                    if (graph.Members.Count == 0) { warns.Add("graph has no members"); break; }
                    var mr = graph.Members[0];
                    targetId = mr.Id;
                    summary = $"fallback (by_role deprecated): {mr.Id}";
                    break;

                case "by_curve":
                    if (targetCurve == null || !targetCurve.IsValid)
                    { warns.Add("No valid Target Curve (C) is available for curve selection."); break; }
                    if (graph.Members.Count == 0)
                    { warns.Add("graph has no members"); break; }
                    var ca = targetCurve.PointAtStart;
                    var cb = targetCurve.PointAtEnd;
                    var nodeById = graph.Nodes.ToDictionary(n => n.Id);
                    BridgeMember bestM = null;
                    double bestScore = double.MaxValue;
                    bool bestIsEndpointMatch = false;
                    foreach (var cm_ in graph.Members)
                    {
                        if (!nodeById.TryGetValue(cm_.From, out var na)) continue;
                        if (!nodeById.TryGetValue(cm_.To,   out var nb)) continue;
                        var pa = new Point3d(na.X, na.Y, na.Z);
                        var pb = new Point3d(nb.X, nb.Y, nb.Z);
                        // Endpoint match score (orientation-independent): smaller is better.
                        double epScore = Math.Min(
                            pa.DistanceTo(ca) + pb.DistanceTo(cb),
                            pa.DistanceTo(cb) + pb.DistanceTo(ca));
                        // Midpoint fallback score.
                        var curveMid = (ca + cb) * 0.5;
                        var memMid = new Point3d((pa.X + pb.X) * 0.5, (pa.Y + pb.Y) * 0.5, (pa.Z + pb.Z) * 0.5);
                        double midScore = curveMid.DistanceTo(memMid);
                        // Prefer endpoint match when it is tight. Threshold is on
                        // the SUM of the two endpoint distances, so 0.20m here ≈
                        // 0.10m per endpoint — comfortably above the recognizer's
                        // node-merge tolerance, far below inter-member spacing.
                        double score = epScore < 0.20 ? epScore : (1.0 + midScore);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestM = cm_;
                            bestIsEndpointMatch = epScore < 0.20;
                        }
                    }
                    if (bestM == null)
                    { warns.Add("no member matched the curve"); break; }
                    targetId = bestM.Id;
                    summary = bestIsEndpointMatch
                        ? $"by_curve (endpoint match): {bestM.Id}  {bestM.From}→{bestM.To}  L={bestM.Length:F2}m"
                        : $"by_curve (nearest-midpoint fallback): {bestM.Id}  L={bestM.Length:F2}m — " +
                          "endpoints did not coincide within 0.20m; check you selected a structure line.";
                    if (!bestIsEndpointMatch)
                        warns.Add("curve endpoints did not match any member within 0.20m — used nearest midpoint instead.");
                    break;

                case "by_point":
                    if (!gotPoint || !targetPoint.IsValid)
                    { warns.Add("Mode = by_point but no Target Point (Pt) connected."); break; }
                    if (graph.Nodes.Count == 0)
                    { warns.Add("graph has no nodes"); break; }
                    BridgeNode bestN = null; double bestNd = double.MaxValue;
                    foreach (var nd in graph.Nodes)
                    {
                        double d = targetPoint.DistanceTo(new Point3d(nd.X, nd.Y, nd.Z));
                        if (d < bestNd) { bestNd = d; bestN = nd; }
                    }
                    targetId = bestN.Id; targetType = "node";
                    summary = $"by_point: nearest node {bestN.Id} ({bestNd:F3}m from the point)";
                    if (bestNd > 0.5)
                        warns.Add($"nearest node is {bestNd:F2}m away — did you click near a joint?");
                    break;

                case "by_node_id":
                    if (graph.Nodes.Any(n => n.Id == memberId))
                    {
                        var nn = graph.Nodes.First(n => n.Id == memberId);
                        targetId = nn.Id; targetType = "node";
                        summary = $"node {nn.Id}  ({nn.X:F2},{nn.Y:F2},{nn.Z:F2})";
                    }
                    else warns.Add($"node id '{memberId}' not found (Mode = by_node_id reads the Member ID 'Id' input).");
                    break;

                case "by_mismatch_rank":
                default:
                    var sorted = mismatches
                        .OrderByDescending(x => x.Severity).ToList();
                    if (sorted.Count == 0)
                    {
                        warns.Add("no mismatches available — falling back to member 0");
                        if (graph.Members.Count > 0)
                        {
                            var m0 = graph.Members[0];
                            targetId = m0.Id; targetType = "member";
                            summary = $"fallback: {m0.Id}  {m0.From}→{m0.To}";
                        }
                        break;
                    }
                    int r = Math.Max(0, Math.Min(sorted.Count - 1, rank));
                    if (r != rank) warns.Add($"rank {rank} clamped to {r}");
                    var mm = sorted[r];
                    targetId = mm.TargetId;
                    targetType = mm.TargetType ?? "member";
                    suggested = mm.SuggestedRuleIds?.FirstOrDefault() ?? "";
                    summary = $"rank #{r}  {mm.Reason}  target={mm.TargetId}  sev={mm.Severity:F2}\n"
                            + $"message: {mm.Message}\n"
                            + $"suggested: {string.Join(", ", mm.SuggestedRuleIds ?? new List<string>())}";
                    break;
            }

            DA.SetData    (0, targetId);
            DA.SetData    (1, targetType);
            DA.SetData    (2, summary);
            DA.SetData    (3, suggested);
            DA.SetDataList(4, warns);
            if (gotPoint && targetPoint.IsValid) DA.SetData(5, targetPoint);
            Message = targetId;

            if (string.IsNullOrEmpty(targetId))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No target picked. " + string.Join(" · ", warns));
            else if (warns.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Picked target: " + targetId + ". " + string.Join(" · ", warns));
            else
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Picked target: {targetId}");
        }
    }
}
