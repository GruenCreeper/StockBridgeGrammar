using System.Collections.Generic;
using System.Linq;
using StockBridgeGrammar.Models;

namespace StockBridgeGrammar.Core
{
    // Transparent greedy stock assignment.
    //
    //   1. Expand stock by Count to a flat pool.
    //   2. Sort members longest-first so the hard ones pick first.
    //   3. For each member, pick the pool entry with length >= memberLen
    //      and smallest leftover. Mark assigned + record waste.
    //   4. If nothing fits, mark unresolved (mismatch tooLong).
    //   5. After all members, scan for tooMuchWaste, angleTooShallow,
    //      jointTooDense, insufficientInventory, spatialFlatness, sectionMismatch.
    //
    // Conceptual model — not an engineering structural check.
    public static class Assigner
    {
        // Default thresholds (callers can override).
        public const double DefaultWasteThreshold       = 0.30;   // 30% waste → tooMuchWaste
        public const double DefaultUtilizationLimit     = 1.0;    // demand / capacity
        public const double DefaultShallowAngleDeg      = 18.0;
        public const int    DefaultDenseJointDegree     = 6;

        public static (BridgeGraph graph, AssignmentResult assignment,
                       List<GrammarMismatch> mismatches, List<string> warnings)
            Assign(BridgeGraph graph, List<StockItem> stock,
                   double wasteThreshold = DefaultWasteThreshold,
                   double utilizationLimit = DefaultUtilizationLimit)
        {
            var warnings = new List<string>();
            graph = graph ?? new BridgeGraph();
            stock = stock ?? new List<StockItem>();

            // --- expand stock pool ---
            var pool = new List<StockSlot>();
            var nextPieceIndex = new Dictionary<string, int>();
            foreach (var s in stock)
            {
                int count = System.Math.Max(0, s.Count);
                string stockId = string.IsNullOrWhiteSpace(s.Id) ? "stock" : s.Id;
                int start = nextPieceIndex.TryGetValue(stockId, out var used) ? used : 0;
                for (int i = 0; i < count; i++)
                    pool.Add(new StockSlot
                    {
                        Item = s,
                        PieceId = $"{stockId}#{start + i + 1}",
                        Used = false,
                    });
                nextPieceIndex[stockId] = start + count;
            }

            // --- sort members longest-first ---
            var ordered = graph.Members
                .OrderByDescending(m => m.Length)
                .ToList();

            var result = new AssignmentResult();
            double totalMember = 0;
            double totalMatchedMember = 0;
            double totalUsed   = 0;
            foreach (var m in ordered)
            {
                totalMember += m.Length;
                var best = pool
                    .Where(p => !p.Used && p.Item.Length >= m.Length - 1e-9)
                    .OrderBy(p => p.Item.Length - m.Length)   // smallest leftover
                    .ThenByDescending(p => p.Item.Length)
                    .FirstOrDefault();
                if (best == null)
                {
                    m.AssignedStockId = null;
                    m.AssignedStockPieceId = null;
                    m.Status   = "unresolved";
                    m.Waste    = null;
                    result.UnresolvedIds.Add(m.Id);
                    result.Assignments.Add(new MemberAssignment
                    {
                        MemberId        = m.Id,
                        AssignedStockId = null,
                        AssignedStockPieceId = null,
                        MemberLength    = m.Length,
                        StockLength     = 0,
                        Waste           = 0,
                        Status          = "unresolved",
                    });
                }
                else
                {
                    best.Used = true;
                    double waste = best.Item.Length - m.Length;
                    m.AssignedStockId = best.Item.Id;
                    m.AssignedStockPieceId = best.PieceId;
                    m.Status   = "ok";
                    m.Waste    = waste;
                    totalUsed += best.Item.Length;
                    totalMatchedMember += m.Length;
                    result.Assignments.Add(new MemberAssignment
                    {
                        MemberId        = m.Id,
                        AssignedStockId = best.Item.Id,
                        AssignedStockPieceId = best.PieceId,
                        MemberLength    = m.Length,
                        StockLength     = best.Item.Length,
                        Waste           = waste,
                        Status          = "ok",
                    });
                }
            }

            int assignedCount = graph.Members.Count - result.UnresolvedIds.Count;
            int totalCount    = System.Math.Max(1, graph.Members.Count);
            result.TotalMemberLength = totalMember;
            result.TotalMatchedMemberLength = totalMatchedMember;
            result.TotalStockUsed    = totalUsed;
            // Material efficiency is defined only over matched members and
            // their assigned stock pieces. Unresolved demand is reflected by
            // StockFitRate instead of inflating the material-efficiency ratio.
            result.MaterialUsageRate = totalUsed > 0 ? totalMatchedMember / totalUsed : 0;
            result.AvgWaste = result.Assignments
                .Where(a => a.AssignedStockId != null)
                .Select(a => a.Waste)
                .DefaultIfEmpty(0).Average();
            result.StockFitRate = (double)assignedCount / totalCount;

            // --- mismatch detection ---
            var mismatches = DetectMismatches(graph, stock, result, wasteThreshold);
            return (graph, result, mismatches, warnings);
        }

        public static List<GrammarMismatch> DetectMismatches(
            BridgeGraph graph, List<StockItem> stock,
            AssignmentResult assignment, double wasteThreshold)
        {
            var ms = new List<GrammarMismatch>();
            int seq = 0;
            double maxStockLen = stock.Count == 0 ? 0 : stock.Max(s => s.Length);

            // tooLong — unresolved + length > max stock.
            foreach (var id in assignment.UnresolvedIds)
            {
                var m = graph.Members.FirstOrDefault(x => x.Id == id);
                if (m == null) continue;
                if (m.Length > maxStockLen + 1e-6)
                {
                    ms.Add(new GrammarMismatch
                    {
                        Id         = $"MM{seq++}",
                        TargetId   = m.Id,
                        TargetType = "member",
                        Reason     = "tooLong",
                        Severity   = maxStockLen > 0
                            ? System.Math.Min(1.5, 0.4 + (m.Length - maxStockLen) / maxStockLen)
                            : 1,
                        Message    = $"{m.Id}: longer than any usable stock ({m.Length:F2}m > {maxStockLen:F2}m)",
                        SuggestedRuleIds = new List<string>
                        {
                            "splitMemberByStockLength",
                            "addConnectionPoint",
                        },
                    });
                }
                else
                {
                    // A sufficiently long stock type exists, but all physical
                    // pieces have already been assigned.
                    ms.Add(new GrammarMismatch
                    {
                        Id         = $"MM{seq++}",
                        TargetId   = m.Id,
                        TargetType = "member",
                        Reason     = "insufficientInventory",
                        Severity   = 0.8,
                        Message    = $"{m.Id}: sufficiently long stock exists, but no unused physical piece remains",
                        SuggestedRuleIds = new List<string>
                        {
                            "triangulateBay",
                            "densifyWithShortStock",
                        },
                    });
                }
            }

            // tooMuchWaste — assigned single piece with high waste ratio.
            foreach (var a in assignment.Assignments)
            {
                if (a.AssignedStockId == null) continue;
                if (a.StockLength <= 0) continue;
                double ratio = a.Waste / a.StockLength;
                if (ratio >= wasteThreshold)
                {
                    ms.Add(new GrammarMismatch
                    {
                        Id         = $"MM{seq++}",
                        TargetId   = a.MemberId,
                        TargetType = "member",
                        Reason     = "tooMuchWaste",
                        Severity   = System.Math.Min(1, ratio),
                        Message    = $"{a.MemberId}: {ratio:P0} waste from stock {a.AssignedStockId}",
                        SuggestedRuleIds = new List<string>
                        {
                            "shiftNodeToMatchStock",
                            "removeRedundantMember",
                        },
                    });
                }
            }

            // angleTooShallow — flag any non-horizontal-band member with a
            // shallow rise/run ratio. Skips members that are nearly horizontal
            // overall (low Δ-elevation against length) since on irregular
            // graphs "horizontal" doesn't imply chord any more.
            var byId = graph.Nodes.ToDictionary(n => n.Id);
            foreach (var m in graph.Members)
            {
                if (!byId.TryGetValue(m.From, out var a)) continue;
                if (!byId.TryGetValue(m.To, out var b))   continue;
                double dx = System.Math.Abs(b.X - a.X);
                double dy = System.Math.Abs(b.Y - a.Y);
                double dz = System.Math.Abs(b.Z - a.Z);
                double horiz = System.Math.Max(1e-6, System.Math.Sqrt(dx * dx + dy * dy));
                double angleDeg = System.Math.Atan2(dz, horiz) * 180.0 / System.Math.PI;
                // Skip near-horizontal members entirely (they read as chord-like).
                if (angleDeg < 2.0) continue;
                if (angleDeg < DefaultShallowAngleDeg && m.Length > 0.3)
                {
                    ms.Add(new GrammarMismatch
                    {
                        Id         = $"MM{seq++}",
                        TargetId   = m.Id,
                        TargetType = "member",
                        Reason     = "angleTooShallow",
                        Severity   = (DefaultShallowAngleDeg - angleDeg) / DefaultShallowAngleDeg,
                        Message    = $"{m.Id}: angle {angleDeg:F0}° below {DefaultShallowAngleDeg:F0}°",
                        SuggestedRuleIds = new List<string>
                        {
                            "triangulateBay",
                            "fanOutLoadPath",
                        },
                    });
                }
            }

            // jointTooDense — node degree > threshold.
            var degree = new Dictionary<string, int>();
            foreach (var m in graph.Members)
            {
                degree[m.From] = degree.TryGetValue(m.From, out var d1) ? d1 + 1 : 1;
                degree[m.To]   = degree.TryGetValue(m.To,   out var d2) ? d2 + 1 : 1;
            }
            foreach (var kv in degree)
            {
                if (kv.Value > DefaultDenseJointDegree)
                {
                    ms.Add(new GrammarMismatch
                    {
                        Id         = $"MM{seq++}",
                        TargetId   = kv.Key,
                        TargetType = "node",
                        Reason     = "jointTooDense",
                        Severity   = System.Math.Min(1.0, (kv.Value - DefaultDenseJointDegree) / 4.0),
                        Message    = $"{kv.Key}: {kv.Value} members meet here",
                        SuggestedRuleIds = new List<string>
                        {
                            "removeConnectionPoint",
                            "removeRedundantMember",
                        },
                    });
                }
            }

            // spatialFlatness — system-level proxy: low Z range.
            double zRange = graph.Nodes.Count == 0
                ? 0
                : graph.Nodes.Max(n => n.Z) - graph.Nodes.Min(n => n.Z);
            double yRange = graph.Nodes.Count == 0
                ? 0
                : graph.Nodes.Max(n => n.Y) - graph.Nodes.Min(n => n.Y);
            double articulation = System.Math.Max(zRange, yRange);
            if (articulation < 1.0)   // very flat truss
            {
                ms.Add(new GrammarMismatch
                {
                    Id         = $"MM{seq++}",
                    TargetId   = "system",
                    TargetType = "system",
                    Reason     = "spatialFlatness",
                    Severity   = 0.4,
                    Message    = $"structure articulation only {articulation:F2}m — quite planar",
                    SuggestedRuleIds = new List<string>
                    {
                        "addConnectionPoint",
                        "fanOutLoadPath",
                    },
                });
            }
            return ms;
        }

        private class StockSlot
        {
            public StockItem Item;
            public string PieceId;
            public bool Used;
        }
    }
}
