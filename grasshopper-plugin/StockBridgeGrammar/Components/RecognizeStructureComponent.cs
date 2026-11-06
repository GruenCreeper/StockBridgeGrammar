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
    public class RecognizeStructureComponent : GH_Component
    {
        public RecognizeStructureComponent()
          : base(
                "StockBridge Recognize Structure",
                "SBRecognize",
                "Read Rhino curves / lines / polylines as a bridge graph " +
                "(nodes + members), without chord/role/bay inference. " +
                "the output is a flat undirected graph suitable for " +
                "irregular, non-traditional truss layouts.",
                "StockBridge", "1. Recognize")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-1111-1111-1111-100000000001");
        protected override Bitmap Icon => IconFactory.Recognize;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Curves", "C", "Truss curves / lines / polylines",
                GH_ParamAccess.list);
            p.AddNumberParameter("Tolerance", "Tol",
                "Node merge tolerance (m). Endpoints within this distance fuse.",
                GH_ParamAccess.item, 0.01);
            p.AddPointParameter("Support Points", "S",
                "Optional manual support points. Each point snaps to the " +
                "nearest node and marks IsSupport = true. No automatic " +
                "support inference.",
                GH_ParamAccess.list);
            p[2].Optional = true;
            p.AddVectorParameter("Span Direction", "D",
                "Optional span direction hint. Kept for downstream rules " +
                "that may use it; the recognizer itself ignores it now.",
                GH_ParamAccess.item, new Vector3d(1, 0, 0));
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPointParameter("Nodes",       "N", "Merged node points",            GH_ParamAccess.list);
            p.AddTextParameter("Members",      "M", "Per-member text summary",        GH_ParamAccess.list);
            p.AddLineParameter("Member Lines", "L", "Recognized member geometry",     GH_ParamAccess.list);
            p.AddTextParameter("Graph JSON",   "J", "Full BridgeGraph as JSON",       GH_ParamAccess.item);
            p.AddTextParameter("Report",       "R", "Recognition steps",              GH_ParamAccess.list);
            p.AddTextParameter("Warnings",     "W", "Tolerance / topology warnings",  GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var curves      = new List<Curve>();
            double tol      = 0.01;
            var supportPts  = new List<Point3d>();
            var spanDir     = new Vector3d(1, 0, 0);

            if (!DA.GetDataList(0, curves) || curves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Please connect Rhino line / curve / polyline members " +
                    "to the Curves (C) input.");
                return;
            }
            DA.GetData(1, ref tol);
            if (tol <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Tolerance must be > 0. Using 0.01.");
                tol = 0.01;
            }
            DA.GetDataList(2, supportPts);
            DA.GetData(3, ref spanDir);

            var opts = new Recognizer.Options
            {
                Tolerance     = tol,
                SpanDirection = spanDir,
                SupportPoints = (supportPts != null && supportPts.Count > 0)
                                ? supportPts : null,
            };

            BridgeGraph graph;
            try { graph = Recognizer.Recognize(curves, opts); }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            // Emit outputs.
            var nodesOut = new List<Point3d>(graph.Nodes.Count);
            foreach (var n in graph.Nodes)
                nodesOut.Add(new Point3d(n.X, n.Y, n.Z));

            var lines    = new List<Line>(graph.Members.Count);
            var summary  = new List<string>(graph.Members.Count);
            var byId     = new Dictionary<string, BridgeNode>();
            foreach (var n in graph.Nodes) byId[n.Id] = n;
            foreach (var m in graph.Members)
            {
                if (!byId.TryGetValue(m.From, out var a)) continue;
                if (!byId.TryGetValue(m.To,   out var b)) continue;
                lines.Add(new Line(
                    new Point3d(a.X, a.Y, a.Z),
                    new Point3d(b.X, b.Y, b.Z)));
                summary.Add(string.Format(
                    "{0}  {1}→{2}  L={3:F3}",
                    m.Id, m.From, m.To, m.Length));
            }

            DA.SetDataList(0, nodesOut);
            DA.SetDataList(1, summary);
            DA.SetDataList(2, lines);
            DA.SetData    (3, JsonIO.Serialize(graph));
            DA.SetDataList(4, graph.Report   ?? new List<string>());
            DA.SetDataList(5, graph.Warnings ?? new List<string>());

            if (graph.Nodes.Count < 2)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Only {graph.Nodes.Count} node(s) recognized. " +
                    "Check tolerance — endpoints may not be merging.");
            else if (graph.Members.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No members built. Are the input curves zero-length?");
            else if (graph.Warnings != null && graph.Warnings.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    string.Join(" · ", graph.Warnings));
        }
    }
}
