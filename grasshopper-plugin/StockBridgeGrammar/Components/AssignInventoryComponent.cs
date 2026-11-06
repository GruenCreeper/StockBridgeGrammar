using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;
using StockBridgeGrammar.Utils;

namespace StockBridgeGrammar.Components
{
    public class AssignInventoryComponent : GH_Component
    {
        public AssignInventoryComponent()
          : base(
                "StockBridge Assign Inventory",
                "SBAssign",
                "Greedy material assignment: for each member pick the smallest " +
                "fitting stock piece, then detect mismatches (tooLong, " +
                "tooMuchWaste, angleTooShallow, jointTooDense, spatialFlatness).",
                "StockBridge", "2. Assign")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-2222-2222-2222-200000000002");
        protected override Bitmap Icon => IconFactory.Assign;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Graph JSON", "G", "BridgeGraph JSON",
                GH_ParamAccess.item);
            p.AddTextParameter("Stock JSON", "S", StockInput.Description,
                GH_ParamAccess.tree);
            p.AddNumberParameter("Utilization Limit", "U",
                "Utilization threshold (conceptual; 1.0 = capacity).",
                GH_ParamAccess.item, Assigner.DefaultUtilizationLimit);
            p.AddNumberParameter("Waste Threshold", "W",
                "Waste-fraction threshold for tooMuchWaste (default 0.30).",
                GH_ParamAccess.item, Assigner.DefaultWasteThreshold);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Assigned Graph JSON", "G",
                "Graph with per-member status / assignedStockId / waste",
                GH_ParamAccess.item);
            p.AddTextParameter("Assignment Table", "T",
                "Per-member assignment summary (one row each)",
                GH_ParamAccess.list);
            p.AddTextParameter("Mismatches JSON", "M",
                "Detected mismatches with suggestedRuleIds",
                GH_ParamAccess.item);
            p.AddTextParameter("Metrics", "K",
                "Aggregate metrics (stockFitRate, usage, avgWaste, …)",
                GH_ParamAccess.list);
            p.AddTextParameter("Warnings", "W", "Notes / warnings",
                GH_ParamAccess.list);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            bool result = base.Read(reader);
            StockInput.Upgrade(this, 1);
            return result;
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Message = "";
            string graphJson = null;
            double utilLimit = Assigner.DefaultUtilizationLimit;
            double wasteThr  = Assigner.DefaultWasteThreshold;
            if (!DA.GetData(0, ref graphJson) || string.IsNullOrWhiteSpace(graphJson))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Graph JSON is empty. Connect the Graph JSON output of " +
                    "Recognize Structure (or paste a graph into a Panel).");
                return;
            }
            DA.GetData(2, ref utilLimit);
            DA.GetData(3, ref wasteThr);

            BridgeGraph graph;
            List<StockItem> stock;
            try
            {
                graph = JsonIO.DeserializeGraph(graphJson);
                stock = StockInput.Read(DA, 1, this);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "JSON parse failed: " + ex.Message +
                    "\nExpected Graph JSON like { \"nodes\":[...], \"members\":[...] }" +
                    "\nExpected Stock JSON like [{ \"id\":\"S1\",\"length\":1.8,\"count\":4 }, ...]");
                return;
            }
            if (graph.Members.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Graph contains no members. Nothing to assign.");
                return;
            }
            if (stock.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Stock list is empty after parsing.");
            }

            var (g2, asn, mm, warn) = Assigner.Assign(graph, stock, wasteThr, utilLimit);

            // Build the assignment table rows.
            var rows = new List<string>(asn.Assignments.Count);
            foreach (var a in asn.Assignments)
            {
                rows.Add(string.Format(
                    "{0,-10} {1,-14} mem={2:F2}m stock={3:F2}m waste={4:F2}m status={5}",
                    a.MemberId, a.AssignedStockPieceId ?? "—",
                    a.MemberLength, a.StockLength, a.Waste, a.Status));
            }

            int assignedCount = asn.Assignments.Count(x => x.Status == "ok");
            var metrics = new List<string>
            {
                $"members:        {graph.Members.Count}",
                $"assigned:       {assignedCount}",
                $"unresolved:     {asn.UnresolvedIds.Count}",
                $"stock fit rate: {asn.StockFitRate:P0}",
                $"material usage: {asn.MaterialUsageRate:P0}",
                $"avg waste:      {asn.AvgWaste:F3} m",
                $"mismatches:     {mm.Count}",
            };

            DA.SetData    (0, JsonIO.Serialize(g2));
            DA.SetDataList(1, rows);
            DA.SetData    (2, JsonIO.Serialize(mm));
            DA.SetDataList(3, metrics);
            DA.SetDataList(4, warn);

            // --- runtime messages -----------------------------------------
            double unresolvedRatio = (double)asn.UnresolvedIds.Count
                                   / System.Math.Max(1, graph.Members.Count);
            if (unresolvedRatio > 0.4)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{asn.UnresolvedIds.Count} of {graph.Members.Count} " +
                    "members are unresolved — stock is too short or " +
                    "insufficient. See Mismatches output for suggested rules.");
            else if (asn.UnresolvedIds.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{asn.UnresolvedIds.Count} unresolved member(s). " +
                    "Run Apply Grammar Rule with the suggested ids.");
            if (mm.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{mm.Count} mismatch(es) detected.");
        }
    }
}
