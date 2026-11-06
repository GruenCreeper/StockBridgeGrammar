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
    // Bridge VGS → Section Check. VGS's "Extract Form Diagram" outputs the edge
    // lines (EdgeLn) and per-edge axial force magnitudes (EdgeMag). This matches
    // each VGS edge to a graph member by endpoint coincidence and emits two
    // parallel lists (Member IDs, Forces) ready to wire into SBCheck's Id / F.
    // NOTE on sign: VGS EdgeMag is a magnitude. If you have a tension/compression
    // classification, feed signed values (+tension/−compression) so the buckling
    // check works; otherwise compression members won't be detected as such.
    public class VgsForceAdapterComponent : GH_Component
    {
        public VgsForceAdapterComponent()
          : base(
                "StockBridge VGS Forces",
                "SBVgsF",
                "Match VGS edge forces to this structure's members. Wire VGS " +
                "EdgeLn + EdgeMag and the Graph JSON; outputs aligned Member IDs " +
                "and Forces (kN) for Section Check. Feed signed forces " +
                "(+tension/−compression) if you want the buckling check.",
                "StockBridge", "0. Helpers")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-eeee-eeee-eeee-e00000000014");
        protected override Bitmap Icon => IconFactory.Recognize;
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter  ("Graph JSON", "G",   "Graph JSON of this structure (its members/nodes).", GH_ParamAccess.item);
            p.AddLineParameter  ("Edge Lines", "Ln",  "VGS EdgeLn — the edge lines from VGS Extract Form Diagram.", GH_ParamAccess.list);
            p.AddNumberParameter("Edge Forces","Mag", "VGS EdgeMag — axial force per edge (kN), parallel to Edge Lines.", GH_ParamAccess.list);
            p.AddNumberParameter("Tolerance",  "Tol", "Endpoint match tolerance (m).", GH_ParamAccess.item, 0.05);
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter  ("Member IDs", "Id", "Member id per matched VGS edge (feed SBCheck 'Id').", GH_ParamAccess.list);
            p.AddNumberParameter("Forces",     "F",  "Force per matched member (kN) (feed SBCheck 'F').",  GH_ParamAccess.list);
            p.AddTextParameter  ("Report",     "Rep","Match report: matched / unmatched edges & members.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string graphJson = null;
            if (!DA.GetData(0, ref graphJson) || string.IsNullOrWhiteSpace(graphJson))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Graph JSON is empty."); return; }
            var lines = new List<Line>(); var mags = new List<double>();
            DA.GetDataList(1, lines); DA.GetDataList(2, mags);
            double tol = 0.05; DA.GetData(3, ref tol);

            BridgeGraph g;
            try { g = JsonIO.DeserializeGraph(graphJson); }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "JSON parse failed: " + ex.Message); return; }

            var nodeById = g.Nodes.ToDictionary(n => n.Id);
            // member endpoint coordinates
            var memberPts = new List<(BridgeMember m, Point3d a, Point3d b)>();
            foreach (var m in g.Members)
                if (nodeById.TryGetValue(m.From, out var na) && nodeById.TryGetValue(m.To, out var nb))
                    memberPts.Add((m, new Point3d(na.X, na.Y, na.Z), new Point3d(nb.X, nb.Y, nb.Z)));

            var oId = new List<string>(); var oF = new List<double>();
            var matchedMembers = new HashSet<string>();
            int matched = 0, unmatched = 0;
            var report = new System.Text.StringBuilder();

            for (int i = 0; i < lines.Count; i++)
            {
                double mag = i < mags.Count ? mags[i] : 0.0;
                var p0 = lines[i].From; var p1 = lines[i].To;
                BridgeMember best = null; double bestScore = double.MaxValue;
                foreach (var (m, a, b) in memberPts)
                {
                    double s = Math.Min(p0.DistanceTo(a) + p1.DistanceTo(b),
                                        p0.DistanceTo(b) + p1.DistanceTo(a));
                    if (s < bestScore) { bestScore = s; best = m; }
                }
                if (best != null && bestScore <= 2 * tol)
                {
                    oId.Add(best.Id); oF.Add(mag); matchedMembers.Add(best.Id); matched++;
                }
                else
                {
                    unmatched++;
                    report.AppendLine($"! VGS edge {i} (mid {((p0 + p1) / 2)}) matched no member within {tol:F3}m (nearest off by {bestScore / 2:F3}m).");
                }
            }

            var unmatchedMembers = g.Members.Where(m => !matchedMembers.Contains(m.Id)).Select(m => m.Id).ToList();
            var head = $"VGS→members: {matched} matched, {unmatched} VGS edges unmatched, " +
                       $"{unmatchedMembers.Count} members got no force.\n";
            if (unmatchedMembers.Count > 0)
                report.AppendLine($"members with NO force (will be 'noForce' in SBCheck): [{string.Join(",", unmatchedMembers)}]");

            DA.SetDataList(0, oId);
            DA.SetDataList(1, oF);
            DA.SetData    (2, head + report.ToString());

            if (unmatched > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{unmatched} VGS edge(s) did not match a member — raise Tolerance or check the diagram aligns with the structure.");
        }
    }
}
