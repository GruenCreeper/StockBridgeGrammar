using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StockBridgeGrammar.Models;

namespace StockBridgeGrammar.Core
{
    // Lightweight candidate generator. Available families:
    //   C0 Base, C1 Material Fit, C2 Dense Rhythm, C3 Fan/Branching,
    //   C4 Reinforced Hub, C5 Lean Reuse (optional).
    // Number of applied rules per non-initial candidate ramps from mild to
    // strong with the user's Design Drama / Grammar Intensity controls.
    public static class CandidateGenerator
    {
        public class Options
        {
            public double DesignDrama       = 0.5;
            public double GrammarIntensity  = 0.5;
            public int    MaxIterations     = 3;
            // Compatibility name: controls inclusion of the Lean Reuse family.
            // Individual local rules do not enforce global mirror symmetry.
            public bool   AllowAsymmetry    = true;
            public int    CandidateCount    = 5;
        }

        public class Candidate
        {
            public string Id;
            public string FamilyName;
            public BridgeGraph Graph;
            public List<GrammarRuleHistoryStep> History = new List<GrammarRuleHistoryStep>();
            public CandidateMetrics Metrics;
        }

        public class CandidateMetrics
        {
            public int NodeCount;
            public int MemberCount;
            public int AddedMembers;
            public int Unresolved;
            public double MaterialUsage;
            public double StockFitRate;
            public double Waste;
            public double VisualDifference;
            public double Score;
        }

        private static readonly (string Id, string Family, string[] Rules)[] FAMILIES =
        {
            // Each family has a distinct structural character. C5 is the
            // optional family controlled by the former asymmetry input.
            ("C1", "Material Fit",        new[] { "splitMemberByStockLength", "addConnectionPoint", "shiftNodeToMatchStock" }),
            ("C2", "Dense Rhythm",        new[] { "densifyWithShortStock", "triangulateBay" }),
            ("C3", "Fan / Branching",     new[] { "fanOutLoadPath", "triangulateBay" }),
            ("C4", "Reinforced Hub",      new[] { "addConnectionPoint", "triangulateBay" }),
            ("C5", "Lean Reuse",          new[] { "shiftNodeToMatchStock", "removeRedundantMember", "fanOutLoadPath" }),
        };

        public static List<Candidate> Generate(
            BridgeGraph baseGraph, List<StockItem> stock, Options opts)
        {
            opts = opts ?? new Options();
            var list = new List<Candidate>();

            // C0 — base baseline.
            list.Add(new Candidate
            {
                Id = "C0",
                FamilyName = "Base",
                Graph = Clone(baseGraph),
                Metrics = ScoreCandidate(baseGraph, baseGraph, stock, 0, 0),
            });

            var families = FAMILIES.Where(f => opts.AllowAsymmetry || f.Id != "C5").ToArray();
            int want = Math.Max(1, Math.Min(opts.CandidateCount, families.Length));
            int maxIterations = Math.Max(0, Math.Min(8, opts.MaxIterations));
            double strength = 0.5 * (ClampUnit(opts.DesignDrama) + ClampUnit(opts.GrammarIntensity));

            for (int c = 1; c <= want; c++)
            {
                var fam = families[c - 1];
                double ramp = want > 1 ? (double)(c - 1) / Math.Max(1, want - 1) : 1.0;
                int target = (int)Math.Ceiling(maxIterations * strength * (0.35 + 0.65 * ramp));
                var g = Clone(baseGraph);
                var history = new List<GrammarRuleHistoryStep>();
                for (int k = 0; k < target; k++)
                {
                    var ruleId = fam.Rules[k % fam.Rules.Length];
                    var targetId = PickTargetForRule(g, ruleId);
                    if (targetId == null) break;
                    var ps = new JObject();
                    var res = GrammarRules.Apply(g, ruleId, targetId, ps, stock);
                    if (res?.Graph == null) continue;
                    g = res.Graph;
                    if (res.Step != null)
                    {
                        res.Step.Index = history.Count + 1;
                        history.Add(res.Step);
                    }
                }
                list.Add(new Candidate
                {
                    Id         = fam.Id,
                    FamilyName = fam.Family,
                    Graph      = g,
                    History    = history,
                    Metrics    = ScoreCandidate(baseGraph, g, stock, history.Count, ramp),
                });
            }
            return list;
        }

        // Heuristic target picker so the family rules have something to act
        // on. Picks the longest available member for split / connection
        // point / shift; picks a mid-graph member for triangulate.
        private static string PickTargetForRule(BridgeGraph g, string ruleId)
        {
            if (g.Members.Count == 0) return null;
            switch (ruleId)
            {
                case "splitMemberByStockLength":
                case "shiftNodeToMatchStock":
                case "addConnectionPoint":
                    return g.Members.OrderByDescending(m => m.Length).First().Id;
                case "triangulateBay":
                    return g.Members[g.Members.Count / 2].Id;
                default:
                    return g.Members[0].Id;
            }
        }

        // Approximate visual difference: added/removed members + moved nodes.
        private static CandidateMetrics ScoreCandidate(
            BridgeGraph baseGraph, BridgeGraph cand,
            List<StockItem> stock, int ruleCount, double ramp)
        {
            var (assigned, assignment, _, _) = Assigner.Assign(Clone(cand), stock);
            var baseMemberIds = new HashSet<string>(baseGraph.Members.Select(m => m.Id));
            int added   = cand.Members.Count(m => !baseMemberIds.Contains(m.Id));
            int removed = baseGraph.Members.Count(m => !cand.Members.Any(x => x.Id == m.Id));
            var baseNodeById = baseGraph.Nodes.ToDictionary(n => n.Id);
            int moved = 0;
            foreach (var n in cand.Nodes)
            {
                if (!baseNodeById.TryGetValue(n.Id, out var bn)) continue;
                double dx = n.X - bn.X, dy = n.Y - bn.Y, dz = n.Z - bn.Z;
                if (dx * dx + dy * dy + dz * dz > 1e-6) moved++;
            }
            double baseMC = Math.Max(1, baseGraph.Members.Count);
            double baseNC = Math.Max(1, baseGraph.Nodes.Count);
            double visDiff = Math.Min(1,
                0.5 * ((added + removed) / baseMC) +
                0.3 * (moved / baseNC) +
                0.2 * Math.Min(1, ruleCount / 5.0));
            double score = 2.5 * assignment.StockFitRate
                          - 3.0 * (assignment.UnresolvedIds.Count / baseMC)
                          - 1.2 * assignment.AvgWaste
                          + 1.5 * visDiff * (0.5 + ramp);
            return new CandidateMetrics
            {
                NodeCount        = cand.Nodes.Count,
                MemberCount      = cand.Members.Count,
                AddedMembers     = added,
                Unresolved       = assignment.UnresolvedIds.Count,
                MaterialUsage    = assignment.MaterialUsageRate,
                StockFitRate     = assignment.StockFitRate,
                Waste            = assignment.AvgWaste,
                VisualDifference = Math.Round(visDiff, 3),
                Score            = Math.Round(score, 3),
            };
        }

        private static BridgeGraph Clone(BridgeGraph g) =>
            JsonIO.DeserializeGraph(JsonIO.Serialize(g));

        private static double ClampUnit(double value) =>
            double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(1, value));
    }
}
