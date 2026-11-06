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
    // Labels member midpoints and nodes in the Rhino viewport and outputs
    // their geometry and IDs for Target Picker.
    public class GraphLabelComponent : GH_Component
    {
        public GraphLabelComponent()
          : base(
                "StockBridge Label",
                "SBLabel",
                "Show member ids and node ids in the Rhino viewport for a Graph " +
                "JSON, so an id (e.g. from Section Check) maps to a visible bar / " +
                "joint. Also outputs member lines, node points and their ids.",
                "StockBridge", "0. Helpers")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-ffff-ffff-ffff-f00000000015");
        protected override Bitmap Icon => IconFactory.Decode;
        public override GH_Exposure Exposure => GH_Exposure.primary;

        private readonly List<(Point3d pt, string txt, Color col)> _labels = new List<(Point3d, string, Color)>();
        private readonly List<Line> _lines = new List<Line>();
        private double _size = 1.0;
        private BoundingBox _bbox = BoundingBox.Empty;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter   ("Graph JSON",  "G",  "Graph JSON to label.", GH_ParamAccess.item);
            p.AddNumberParameter ("Text Size",   "Sz", "Label height (model units). 0 = auto (~2% of the structure size).", GH_ParamAccess.item, 0.0);
            p[1].Optional = true;
            p.AddBooleanParameter("Show Members", "M", "Draw member ids at bar midpoints.", GH_ParamAccess.item, true);
            p[2].Optional = true;
            p.AddBooleanParameter("Show Nodes",   "N", "Draw node ids at joints.", GH_ParamAccess.item, true);
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddLineParameter ("Member Lines", "L",  "One line per member.",          GH_ParamAccess.list);
            p.AddTextParameter ("Member IDs",   "Mi", "Member id per line.",            GH_ParamAccess.list);
            p.AddPointParameter("Node Points",  "Np", "One point per node.",            GH_ParamAccess.list);
            p.AddTextParameter ("Node IDs",     "Ni", "Node id per point.",             GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _labels.Clear(); _lines.Clear(); _bbox = BoundingBox.Empty;
            string graphJson = null;
            if (!DA.GetData(0, ref graphJson) || string.IsNullOrWhiteSpace(graphJson))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Graph JSON is empty."); return; }
            double size = 0.0; DA.GetData(1, ref size);
            bool showM = true, showN = true; DA.GetData(2, ref showM); DA.GetData(3, ref showN);

            BridgeGraph g;
            try { g = JsonIO.DeserializeGraph(graphJson); }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "JSON parse failed: " + ex.Message); return; }

            var byId = g.Nodes.ToDictionary(n => n.Id);
            var lineOut = new List<Line>(); var midOut = new List<string>();
            var ptOut = new List<Point3d>(); var nidOut = new List<string>();

            foreach (var n in g.Nodes)
            {
                var pt = new Point3d(n.X, n.Y, n.Z);
                ptOut.Add(pt); nidOut.Add(n.Id);
                _bbox.Union(pt);
                if (showN) _labels.Add((pt, n.Id, n.IsSupport == true ? Color.OrangeRed : Color.RoyalBlue));
            }
            foreach (var m in g.Members)
            {
                if (!byId.TryGetValue(m.From, out var a) || !byId.TryGetValue(m.To, out var b)) continue;
                var pa = new Point3d(a.X, a.Y, a.Z); var pb = new Point3d(b.X, b.Y, b.Z);
                var ln = new Line(pa, pb);
                _lines.Add(ln); lineOut.Add(ln); midOut.Add(m.Id);
                if (showM) _labels.Add(((pa + pb) * 0.5, m.Id, Color.FromArgb(20, 110, 60)));
            }

            // auto text size from structure extent
            double diag = _bbox.IsValid ? _bbox.Diagonal.Length : 10.0;
            _size = size > 1e-6 ? size : Math.Max(0.05, 0.02 * diag);

            DA.SetDataList(0, lineOut);
            DA.SetDataList(1, midOut);
            DA.SetDataList(2, ptOut);
            DA.SetDataList(3, nidOut);
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            // faint bars for context
            foreach (var ln in _lines)
                args.Display.DrawLine(ln, Color.FromArgb(120, 120, 120), 1);
            // billboard text facing the camera
            var cx = args.Viewport.CameraX; var cy = args.Viewport.CameraY;
            foreach (var (pt, txt, col) in _labels)
            {
                var pl = new Plane(pt, cx, cy);
                args.Display.Draw3dText(txt, col, pl, _size, "Arial");
            }
        }

        public override BoundingBox ClippingBox => _bbox.IsValid ? _bbox : base.ClippingBox;
        public override bool IsPreviewCapable => true;
    }
}
