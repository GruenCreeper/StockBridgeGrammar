using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;
using StockBridgeGrammar.Utils;

namespace StockBridgeGrammar.Components
{
    // Pick ONE candidate from the data-tree output of Generate Candidates
    // and surface it as a single Graph JSON + preview lines. Works around
    // the standard data-tree workflow which is awkward for casual users.
    public class CandidateSelectorComponent : GH_Component
    {
        public CandidateSelectorComponent()
          : base(
                "StockBridge Candidate Selector",
                "SBPick",
                "Pick one candidate by index from Generate Candidates' " +
                "data-tree output. Outputs the selected Graph JSON + " +
                "preview geometry. The index runs across all input branches in tree order; " +
                "no Flatten is required. Wire Selected Graph JSON into Decode Graph.",
                "StockBridge", "0. Helpers")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-cccc-cccc-cccc-c00000000012");
        protected override Bitmap Icon => IconFactory.CandidateSelector;
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter   ("Candidate Graphs JSON", "CG", "Candidate graphs as a tree or list; read across all branches in tree order.", GH_ParamAccess.tree);
            p.AddIntegerParameter("Candidate Index",       "i",  "Which candidate to select (0 = base).", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter ("Selected Graph JSON", "G", "The chosen candidate's Graph JSON.", GH_ParamAccess.item);
            p.AddTextParameter ("Selected Summary",    "S", "Short summary of the chosen candidate.", GH_ParamAccess.item);
            p.AddLineParameter ("Preview Lines",       "L", "Member lines of the chosen candidate.", GH_ParamAccess.list);
            p.AddPointParameter("Preview Points",      "N", "Node points of the chosen candidate.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int idx = 0;
            GH_Structure<GH_String> tree;
            if (!DA.GetDataTree(0, out tree))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Candidate Graphs JSON is empty. Connect Generate Candidates' Graphs output.");
                return;
            }
            var jsons = new List<string>();
            foreach (var branch in tree.Branches)
                foreach (var item in branch)
                    jsons.Add(item?.Value);
            if (jsons.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Candidate Graphs JSON contains no candidates.");
                return;
            }
            DA.GetData(1, ref idx);
            int safe = Math.Max(0, Math.Min(jsons.Count - 1, idx));
            if (safe != idx)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Index {idx} out of range; clamped to {safe}.");
            BridgeGraph g;
            if (string.IsNullOrWhiteSpace(jsons[safe]))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Candidate {safe} has empty Graph JSON.");
                return;
            }
            try { g = JsonIO.DeserializeGraph(jsons[safe]); }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Candidate JSON parse failed: " + ex.Message);
                return;
            }

            var byId = new Dictionary<string, BridgeNode>();
            var points = new List<Point3d>();
            foreach (var n in g.Nodes)
            {
                byId[n.Id] = n;
                points.Add(new Point3d(n.X, n.Y, n.Z));
            }
            var lines = new List<Line>();
            foreach (var m in g.Members)
            {
                if (!byId.TryGetValue(m.From, out var a)) continue;
                if (!byId.TryGetValue(m.To,   out var b)) continue;
                lines.Add(new Line(
                    new Point3d(a.X, a.Y, a.Z),
                    new Point3d(b.X, b.Y, b.Z)));
            }
            DA.SetData    (0, jsons[safe]);
            DA.SetData    (1, $"Candidate {safe}: {g.Nodes.Count} nodes · {g.Members.Count} members · source={g.Source ?? "—"}");
            DA.SetDataList(2, lines);
            DA.SetDataList(3, points);
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Selected candidate {safe} of {jsons.Count - 1}.");
        }
    }
}
