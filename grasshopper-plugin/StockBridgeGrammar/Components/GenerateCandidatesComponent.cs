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
    public class GenerateCandidatesComponent : GH_Component
    {
        public GenerateCandidatesComponent()
          : base(
                "StockBridge Generate Candidates",
                "SBCandidates",
                "Generate a base graph plus up to 5 alternatives (Material Fit, Dense Rhythm, " +
                "Fan/Branching, Reinforced Hub, optional Lean Reuse) using grammar rules. " +
                "The mean of Design Drama and Grammar Intensity scales the number of " +
                "rule attempts. Candidates are scored by stock assignment and graph changes.",
                "StockBridge", "4. Generate")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-4444-4444-4444-400000000004");
        protected override Bitmap Icon => IconFactory.Generate;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Graph JSON", "G", "Base BridgeGraph JSON",
                GH_ParamAccess.item);
            p.AddTextParameter("Stock JSON", "S", StockInput.Description,
                GH_ParamAccess.tree);
            p.AddNumberParameter("Design Drama", "D",
                "0..1. Averaged with Grammar Intensity to scale rule attempts, up to Max Iterations.", GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Grammar Intensity", "I",
                "0..1. Averaged with Design Drama to scale rule attempts; both zero means no transformations.", GH_ParamAccess.item, 0.5);
            p.AddIntegerParameter("Max Iterations", "M",
                "Maximum rule attempts per candidate (clamped to 0..8). Zero preserves every candidate's base geometry.",
                GH_ParamAccess.item, 3);
            p.AddBooleanParameter("Include Lean Reuse", "A",
                "Include the Lean Reuse exploration family. If false, at most four transformed families are returned. Local grammar rules do not guarantee mirror symmetry.",
                GH_ParamAccess.item, true);
            p.AddIntegerParameter("Candidate Count", "K",
                "How many candidate slots besides the base (1..5; at most 4 when Lean Reuse is disabled). Inapplicable rules may leave a slot unchanged.",
                GH_ParamAccess.item, 5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Candidate Graphs JSON", "G",
                "One JSON string per candidate (data-tree: branch per candidate)",
                GH_ParamAccess.tree);
            p.AddTextParameter("Candidate Names", "N",
                "Family name for each candidate",
                GH_ParamAccess.list);
            p.AddTextParameter("Candidate Metrics", "M",
                "Per-candidate metrics summary",
                GH_ParamAccess.list);
            p.AddLineParameter("Preview Lines", "L",
                "Member lines per candidate (data-tree: branch per candidate)",
                GH_ParamAccess.tree);
            p.AddTextParameter("Rule Histories", "H",
                "Rule sequence per candidate",
                GH_ParamAccess.list);
            p.AddTextParameter("Candidate Summary", "CS",
                "Multi-block panel-friendly summary for every candidate, " +
                "each block: name, rules, members, unresolved, stock fit.",
                GH_ParamAccess.item);
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
            double drama = 0.5, intensity = 0.5;
            int    maxIter = 3, count = 5;
            bool   allowAsym = true;
            if (!DA.GetData(0, ref graphJson) || string.IsNullOrWhiteSpace(graphJson))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Graph JSON is empty.");
                return;
            }
            DA.GetData(2, ref drama);
            DA.GetData(3, ref intensity);
            DA.GetData(4, ref maxIter);
            DA.GetData(5, ref allowAsym);
            DA.GetData(6, ref count);

            BridgeGraph baseGraph;
            List<StockItem> stock;
            try
            {
                baseGraph = JsonIO.DeserializeGraph(graphJson);
                stock     = StockInput.Read(DA, 1, this);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "JSON parse failed: " + ex.Message);
                return;
            }

            if (stock.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Stock JSON is empty — candidates will not be scored for material fit.");

            var opts = new CandidateGenerator.Options
            {
                DesignDrama      = drama,
                GrammarIntensity = intensity,
                MaxIterations    = maxIter,
                AllowAsymmetry   = allowAsym,
                CandidateCount   = count,
            };
            var candidates = CandidateGenerator.Generate(baseGraph, stock, opts);

            var graphsTree = new GH_Structure<GH_String>();
            var linesTree  = new GH_Structure<GH_Line>();
            var names    = new List<string>();
            var metrics  = new List<string>();
            var stories  = new List<string>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var path = new GH_Path(i);
                graphsTree.Append(new GH_String(JsonIO.Serialize(c.Graph)), path);
                var byId = new Dictionary<string, BridgeNode>();
                foreach (var n in c.Graph.Nodes) byId[n.Id] = n;
                foreach (var m in c.Graph.Members)
                {
                    if (!byId.TryGetValue(m.From, out var a)) continue;
                    if (!byId.TryGetValue(m.To,   out var b)) continue;
                    linesTree.Append(new GH_Line(new Line(
                        new Point3d(a.X, a.Y, a.Z),
                        new Point3d(b.X, b.Y, b.Z))), path);
                }
                names.Add($"{c.Id}  {c.FamilyName}");
                metrics.Add(string.Format(
                    "{0}  rules={1}  +mem={2}  unres={3}  usage={4:P0}  visD={5:F2}  score={6:F2}  fit={7:P0}",
                    c.Id, c.History.Count, c.Metrics.AddedMembers,
                    c.Metrics.Unresolved, c.Metrics.MaterialUsage,
                    c.Metrics.VisualDifference, c.Metrics.Score, c.Metrics.StockFitRate));
                stories.Add(c.History.Count == 0
                    ? $"{c.Id}: (base)"
                    : c.Id + ": " + string.Join(" → ",
                        c.History.ConvertAll(s => s.RuleName)));
            }

            // Build a human-readable multi-block summary.
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                sb.AppendLine($"Candidate {i} — {c.FamilyName}");
                if (c.History.Count == 0)
                    sb.AppendLine("  Rules: none");
                else
                    sb.AppendLine("  Rules: " + string.Join(" → ",
                        c.History.ConvertAll(s => s.RuleName)));
                sb.AppendLine($"  Members:    {c.Metrics.MemberCount}");
                sb.AppendLine($"  Unresolved: {c.Metrics.Unresolved}");
                sb.AppendLine($"  Stock fit:  {c.Metrics.StockFitRate:P0}");
                sb.AppendLine($"  Material use: {c.Metrics.MaterialUsage:P0}");
                sb.AppendLine($"  Visual Δ:   {c.Metrics.VisualDifference:F2}");
                sb.AppendLine($"  Score:      {c.Metrics.Score:F2}");
                sb.AppendLine();
            }

            DA.SetDataTree(0, graphsTree);
            DA.SetDataList(1, names);
            DA.SetDataList(2, metrics);
            DA.SetDataTree(3, linesTree);
            DA.SetDataList(4, stories);
            DA.SetData    (5, sb.ToString());

            int transformed = 0;
            foreach (var c in candidates)
                if (c.History.Count > 0) transformed++;
            if (transformed == 0 && candidates.Count > 1 && maxIter > 0 && (drama > 0 || intensity > 0))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No candidate applied a rule. Check whether the structure and stock " +
                    "meet the selected families' rule conditions; higher intensity permits more attempts.");
            else
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Generated {candidates.Count} candidate(s); " +
                    $"{transformed} transformed.");
        }
    }
}
