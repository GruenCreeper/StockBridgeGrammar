using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;
using StockBridgeGrammar.Utils;

namespace StockBridgeGrammar.Components
{
    public class DecodeGraphComponent : GH_Component
    {
        public DecodeGraphComponent()
          : base(
                "StockBridge Decode Graph",
                "SBDecode",
                "Split any BridgeGraph JSON into Rhino-friendly parallel " +
                "lists: node points, member lines, member ids, assigned " +
                "stock ids, and an aggregate metrics summary. Use after " +
                "Recognize / Assign / Apply / Generate.",
                "StockBridge", "5. Decode")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-5555-5555-5555-500000000005");
        protected override Bitmap Icon => IconFactory.Decode;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Graph JSON", "G", "BridgeGraph JSON",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPointParameter("Node Points",      "N",  "All node Point3d", GH_ParamAccess.list);
            p.AddTextParameter ("Node IDs",         "Nid","Node id per point", GH_ParamAccess.list);
            p.AddLineParameter ("Member Lines",     "L",  "All member Lines",  GH_ParamAccess.list);
            p.AddTextParameter ("Member IDs",       "Mid","Member id per line", GH_ParamAccess.list);
            p.AddTextParameter ("Assigned Stock IDs","S", "assignedStockId per line", GH_ParamAccess.list);
            p.AddTextParameter ("Metrics",          "K",  "Aggregate metrics", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string graphJson = null;
            if (!DA.GetData(0, ref graphJson) || string.IsNullOrWhiteSpace(graphJson))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Graph JSON is empty. Wire any Graph JSON output here " +
                    "(Recognize / Assign / Apply / Generate).");
                return;
            }

            BridgeGraph g;
            try { g = JsonIO.DeserializeGraph(graphJson); }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "JSON parse failed: " + ex.Message);
                return;
            }
            if (g.Members == null || g.Members.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Graph has no members.");

            var pts = new List<Point3d>(g.Nodes.Count);
            var nIds = new List<string>(g.Nodes.Count);
            var byId = new Dictionary<string, BridgeNode>();
            foreach (var n in g.Nodes)
            {
                pts.Add(new Point3d(n.X, n.Y, n.Z));
                nIds.Add(n.Id);
                byId[n.Id] = n;
            }

            var lines = new List<Line>(g.Members.Count);
            var mIds  = new List<string>(g.Members.Count);
            var sIds  = new List<string>(g.Members.Count);
            int assigned = 0, unresolved = 0;
            double totalLen = 0;
            foreach (var m in g.Members)
            {
                if (!byId.TryGetValue(m.From, out var a)) continue;
                if (!byId.TryGetValue(m.To,   out var b)) continue;
                lines.Add(new Line(
                    new Point3d(a.X, a.Y, a.Z),
                    new Point3d(b.X, b.Y, b.Z)));
                mIds.Add(m.Id);
                sIds.Add(m.AssignedStockId ?? "");
                totalLen += m.Length;
                if (!string.IsNullOrEmpty(m.AssignedStockId)) assigned++;
                if (m.Status == "unresolved") unresolved++;
            }

            var metrics = new List<string>
            {
                $"source:        {g.Source ?? "—"}",
                $"nodes:         {g.Nodes.Count}",
                $"members:       {g.Members.Count}",
                $"assigned:      {assigned}",
                $"unresolved:    {unresolved}",
                $"total length:  {totalLen:F2} m",
            };

            DA.SetDataList(0, pts);
            DA.SetDataList(1, nIds);
            DA.SetDataList(2, lines);
            DA.SetDataList(3, mIds);
            DA.SetDataList(4, sIds);
            DA.SetDataList(5, metrics);
        }
    }
}
