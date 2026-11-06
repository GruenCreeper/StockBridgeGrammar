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
    // Checks stock length and availability, axial stress, and Euler buckling.
    // Suggests a rule and target for failing members; forces must be recomputed
    // after a geometry change. Units: lengths m, force kN, stress kPa.
    public class SectionCheckComponent : GH_Component
    {
        public SectionCheckComponent()
          : base(
                "StockBridge Section Check",
                "SBCheck",
                "Check each member against stock on length AND section/stress. " +
                "Supply signed axial forces directly or through VGS Force Adapter. " +
                "Failing members get a suggested rule (splitEdgeToPoint if too " +
                "long, addNodeToStock if over-stressed) and target id.",
                "StockBridge", "0. Helpers")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-dddd-dddd-dddd-d00000000013");
        protected override Bitmap Icon => IconFactory.Assign;
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter   ("Graph JSON",    "G",  "Graph JSON (members, ideally with assignedStockId).", GH_ParamAccess.item);
            p.AddTextParameter   ("Stock JSON",    "S",  StockInput.Description, GH_ParamAccess.tree);
            p.AddTextParameter   ("Member IDs",    "Id", "Member ids that carry a force (parallel to Forces).", GH_ParamAccess.list);
            p[2].Optional = true;
            p.AddNumberParameter ("Forces",        "F",  "Axial force per member id (kN, +tension/−compression). Parallel to Member IDs.", GH_ParamAccess.list);
            p[3].Optional = true;
            p.AddNumberParameter ("Default σ",     "Sig","Fallback allowable stress (kPa) when a stock piece has none.", GH_ParamAccess.item, 14000.0);
            p[4].Optional = true;
            p.AddNumberParameter ("Eff. Length k", "k",  "Effective-length factor for the compression-buckling check (1.0 pinned-pinned, 0.5 fixed-fixed, 2.0 cantilever).", GH_ParamAccess.item, 1.0);
            p[5].Optional = true;
            // Append ports so existing canvas connections retain their indices.
            p.AddNumberParameter("Stress Limit", "SL",
                "Stress-ratio trigger threshold. Default 1.0; smaller values trigger earlier. Must be finite and > 0. Does not change the reported stress ratio or material properties.",
                GH_ParamAccess.item, 1.0);
            p[6].Optional = true;
            p.AddNumberParameter("Buckling Limit", "BL",
                "Compression buckling-ratio trigger threshold. Default 1.0; smaller values trigger earlier. Must be finite and > 0. Does not change the reported buckling ratio or material properties.",
                GH_ParamAccess.item, 1.0);
            p[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter  ("Member IDs",       "Id", "Member id per row.",                          GH_ParamAccess.list);
            p.AddTextParameter  ("Status",           "St", "ok / tooLong / insufficientInventory / overStressed / buckling / noForce.", GH_ParamAccess.list);
            p.AddNumberParameter("Stress Ratio",     "R",  "Raw axial stress / allowable. Compared with Stress Limit (SL); not divided by SL. −1 if not evaluated.", GH_ParamAccess.list);
            p.AddNumberParameter("Buckling Ratio",   "Br", "Raw |F| / Euler load for compression members. Compared with Buckling Limit (BL); not divided by BL. −1 if not evaluated.", GH_ParamAccess.list);
            p.AddTextParameter  ("Suggested Rule",   "Ru", "splitEdgeToPoint / addNodeToStock / (empty if ok).", GH_ParamAccess.list);
            p.AddTextParameter  ("Suggested Target", "T",  "Target id to feed the suggested rule.",        GH_ParamAccess.list);
            p.AddTextParameter  ("Summary",          "Sum","Human-readable report of all failing members.", GH_ParamAccess.item);
            p.AddTextParameter  ("Assigned Stock",   "As", "Unique physical stock-piece id chosen per member (for example S1#2).", GH_ParamAccess.list);
            p.AddTextParameter  ("Updated Graph JSON","G2","Graph JSON with assignedStockId and assignedStockPieceId written back.", GH_ParamAccess.item);
            p.AddTextParameter  ("Next Rule",        "Nr", "The ONE rule to apply next (the first failing member). Wire to Apply.R. Empty when all ok.", GH_ParamAccess.item);
            p.AddTextParameter  ("Next Target",      "Nt", "Target for Next Rule (the failing member id). Wire to Apply.T. Empty when all ok.", GH_ParamAccess.item);
            p.AddTextParameter  ("Detail",           "Dt", "One line PER member: status | length | force | assigned stock | stress & buckling ratios | suggested rule. Panel it to see every member's assignment & reason.", GH_ParamAccess.list);
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
            if (!DA.GetData(0, ref graphJson) || string.IsNullOrWhiteSpace(graphJson))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Graph JSON is empty."); return; }

            var ids = new List<string>(); var forces = new List<double>();
            DA.GetDataList(2, ids); DA.GetDataList(3, forces);
            double defSigma = 14000.0; DA.GetData(4, ref defSigma);
            double kFactor = 1.0; DA.GetData(5, ref kFactor);
            double stressLimit = 1.0;
            double bucklingLimit = 1.0;
            bool gotStressLimit = DA.GetData(6, ref stressLimit);
            bool gotBucklingLimit = DA.GetData(7, ref bucklingLimit);
            if (!gotStressLimit || !gotBucklingLimit || !ValidLimit(stressLimit) || !ValidLimit(bucklingLimit))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Stress Limit (SL) and Buckling Limit (BL) must be finite numbers greater than 0.");
                return;
            }
            const double defE = 11000000.0;   // kPa, fallback Young's modulus

            BridgeGraph g; List<StockItem> stock;
            try { g = JsonIO.DeserializeGraph(graphJson); stock = StockInput.Read(DA, 1, this); }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "JSON parse failed: " + ex.Message); return; }

            if (stock.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "stock is empty."); return; }
            var piecePool = ExpandStock(stock);
            if (piecePool.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "stock contains no available physical pieces (all counts are zero)."); return; }
            double maxStockLen = piecePool.Max(s => s.Item.Length);
            var nodeById = g.Nodes.ToDictionary(n => n.Id);

            // Reserve piece-level assignments produced by AssignInventory.
            // Older graphs that contain only assignedStockId are upgraded by
            // reserving one unused piece of that stock type.
            ReserveExistingAssignments(g.Members, piecePool);

            // force per member id
            var forceOf = new Dictionary<string, double>();
            for (int i = 0; i < ids.Count && i < forces.Count; i++) forceOf[ids[i]] = forces[i];

            var oId = new List<string>(); var oSt = new List<string>(); var oR = new List<double>();
            var oBr = new List<double>(); var oRule = new List<string>(); var oTgt = new List<string>();
            var oDetail = new List<string>();
            var report = new System.Text.StringBuilder();
            int nLong = 0, nInventory = 0, nStress = 0, nBuck = 0, nOk = 0, nNoForce = 0;

            var oAssigned = new List<string>();
            foreach (var m in g.Members)
            {
                double len = m.Length;
                if (len <= 1e-9 && nodeById.TryGetValue(m.From, out var a) && nodeById.TryGetValue(m.To, out var b))
                    len = Dist(a, b);

                string status, rule = "", target = "", assigned = "";
                double ratio = -1, bRatio = -1;

                // Piece-level selection. The member's existing physical piece
                // is retained whenever it passes. Reassignment, if necessary,
                // can only use a piece that is not reserved by another member.
                var owned = piecePool.FirstOrDefault(p => p.ReservedFor == m.Id);
                var available = piecePool
                    .Where(p => p.ReservedFor == null || p.ReservedFor == m.Id)
                    .ToList();
                var spanning = available.Where(p => p.Item.Length >= len - 1e-6).ToList();
                bool anySpanningInInventory = piecePool.Any(p => p.Item.Length >= len - 1e-6);

                StockPieceSlot chosen = null;
                if (!anySpanningInInventory)
                {
                    ReleaseAssignment(m, owned);
                    status = "tooLong"; rule = "splitEdgeToPoint"; target = m.Id; nLong++;
                    report.AppendLine($"• {m.Id}: tooLong  {len:F2}m > {maxStockLen:F2}m → splitEdgeToPoint");
                }
                else if (spanning.Count == 0)
                {
                    ReleaseAssignment(m, owned);
                    status = "insufficientInventory"; rule = "addNodeToStock"; target = m.Id; nInventory++;
                    report.AppendLine($"• {m.Id}: insufficientInventory  no unused physical piece can span {len:F2}m → addNodeToStock");
                }
                else if (!forceOf.TryGetValue(m.Id, out var f))
                {
                    chosen = owned != null && owned.Item.Length >= len - 1e-6
                        ? owned
                        : spanning.OrderBy(p => p.Item.Length - len).First();
                    AssignPiece(m, chosen, owned);
                    assigned = chosen.PieceId;
                    status = "noForce"; nNoForce++;
                }
                else
                {
                    double mlen = len;
                    Func<StockPieceSlot, double> Sig = p => p.Item.AllowableStress ?? defSigma;
                    Func<StockPieceSlot, double> Str = p => p.Item.Area > 1e-9 ? Math.Abs(f) / p.Item.Area / Sig(p) : double.PositiveInfinity;
                    Func<StockPieceSlot, double> Buck = p =>
                    {
                        if (f >= 0 || mlen < 1e-6) return -1;            // tension / N-A → no buckling
                        var s = p.Item;
                        double Iw = Math.Min(s.Width * Math.Pow(s.Height, 3), s.Height * Math.Pow(s.Width, 3)) / 12.0;
                        double Ef = s.ElasticModulus ?? defE;
                        double Fcr = Math.PI * Math.PI * Ef * Iw / (kFactor * mlen * kFactor * mlen);
                        return Fcr > 1e-9 ? Math.Abs(f) / Fcr : double.PositiveInfinity;
                    };
                    Func<StockPieceSlot, bool> Passes = p =>
                        p.Item.Area > 1e-9 && Sig(p) > 1e-9 &&
                        Str(p) <= stressLimit + 1e-6 && Buck(p) <= bucklingLimit + 1e-6;

                    var feasible = spanning.Where(Passes).ToList();
                    if (feasible.Count > 0)
                    {
                        chosen = owned != null && feasible.Contains(owned)
                            ? owned
                            : feasible.OrderBy(p => p.Item.Area).ThenBy(p => p.Item.Length - len).First();
                        AssignPiece(m, chosen, owned);
                        assigned = chosen.PieceId; status = "ok"; nOk++;
                        ratio = Str(chosen); bRatio = Buck(chosen);
                    }
                    else
                    {
                        chosen = owned != null && spanning.Contains(owned)
                            ? owned
                            : spanning.OrderByDescending(p => p.Item.Area).First();
                        AssignPiece(m, chosen, owned);
                        assigned = chosen.PieceId; ratio = Str(chosen); bRatio = Buck(chosen);
                        // Compare exceedance relative to each user-selected limit.
                        // A lone buckling failure must remain buckling even if the
                        // passing stress ratio happens to have a larger raw value.
                        if (f < 0 && bRatio > bucklingLimit + 1e-6 &&
                            (ratio <= stressLimit + 1e-6 || bRatio / bucklingLimit >= ratio / stressLimit))
                        {
                            status = "buckling"; rule = "splitEdgeToPoint"; target = m.Id; nBuck++;
                            report.AppendLine($"• {m.Id}: buckling  F={f:F1}kN(compr), L={len:F2}m, ratio {bRatio:F2} > limit {bucklingLimit:G6} ({chosen.PieceId}) → splitEdgeToPoint (shorten)");
                        }
                        else
                        {
                            status = "overStressed"; rule = "addNodeToStock"; target = m.Id; nStress++;
                            report.AppendLine($"• {m.Id}: overStressed  F={f:F1}kN, ratio {ratio:F2} > limit {stressLimit:G6} ({chosen.PieceId}) → addNodeToStock");
                        }
                    }
                }

                oId.Add(m.Id); oSt.Add(status); oR.Add(Math.Round(ratio, 3)); oBr.Add(Math.Round(bRatio, 3));
                oRule.Add(rule); oTgt.Add(target); oAssigned.Add(assigned);

                string fstr = forceOf.TryGetValue(m.Id, out var fv) ? $"{fv:F0}kN" : "—";
                oDetail.Add($"{m.Id}: {status}  | L={len:F2}m  F={fstr}  → {(string.IsNullOrEmpty(assigned) ? "—" : assigned)}" +
                            (ratio >= 0 ? $"  | stress {ratio:F2} (limit {stressLimit:G6})" : "") +
                            (bRatio >= 0 ? $", buckling {bRatio:F2} (limit {bucklingLimit:G6})" : "") +
                            (string.IsNullOrEmpty(rule) ? "" : $"  ⇒ {rule}"));
            }

            var head = $"Section check: {g.Members.Count} members → ok {nOk}, tooLong {nLong}, insufficientInventory {nInventory}, overStressed {nStress}, buckling {nBuck}, noForce {nNoForce}\n" +
                $"Limits: stress {stressLimit:G6}, buckling {bucklingLimit:G6}\n";
            DA.SetDataList(0, oId);
            DA.SetDataList(1, oSt);
            DA.SetDataList(2, oR);
            DA.SetDataList(3, oBr);
            DA.SetDataList(4, oRule);
            DA.SetDataList(5, oTgt);
            DA.SetData    (6, head + (report.Length == 0 ? "(all members satisfy stock)" : report.ToString()));
            DA.SetDataList(7, oAssigned);
            DA.SetData    (8, JsonIO.Serialize(g));
            // the single next fix = first member with a suggested rule
            int firstFix = oRule.FindIndex(r => !string.IsNullOrEmpty(r));
            DA.SetData(9, firstFix >= 0 ? oRule[firstFix] : "");
            DA.SetData(10, firstFix >= 0 ? oTgt[firstFix] : "");
            DA.SetDataList(11, oDetail);

            if (nLong + nInventory + nStress + nBuck > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{nLong + nInventory + nStress + nBuck} member(s) need a rule (tooLong {nLong}, insufficientInventory {nInventory}, overStressed {nStress}, buckling {nBuck}).");
        }

        private static bool ValidLimit(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;

        private static List<StockPieceSlot> ExpandStock(IEnumerable<StockItem> stock)
        {
            var pieces = new List<StockPieceSlot>();
            var nextPieceIndex = new Dictionary<string, int>();
            foreach (var item in stock ?? Enumerable.Empty<StockItem>())
            {
                int count = Math.Max(0, item.Count);
                string stockId = string.IsNullOrWhiteSpace(item.Id) ? "stock" : item.Id;
                int start = nextPieceIndex.TryGetValue(stockId, out var used) ? used : 0;
                for (int i = 0; i < count; i++)
                    pieces.Add(new StockPieceSlot
                    {
                        Item = item,
                        PieceId = $"{stockId}#{start + i + 1}",
                    });
                nextPieceIndex[stockId] = start + count;
            }
            return pieces;
        }

        private static void ReserveExistingAssignments(
            IEnumerable<BridgeMember> members, List<StockPieceSlot> pieces)
        {
            foreach (var member in members)
            {
                if (string.IsNullOrWhiteSpace(member.AssignedStockPieceId)) continue;
                var piece = pieces.FirstOrDefault(p =>
                    p.PieceId == member.AssignedStockPieceId && p.ReservedFor == null);
                if (piece != null)
                {
                    piece.ReservedFor = member.Id;
                    member.AssignedStockId = piece.Item.Id;
                }
                else
                {
                    // Missing or duplicated piece id: fall back to the stock
                    // type reservation path below.
                    member.AssignedStockPieceId = null;
                }
            }

            foreach (var member in members)
            {
                if (!string.IsNullOrWhiteSpace(member.AssignedStockPieceId) ||
                    string.IsNullOrWhiteSpace(member.AssignedStockId)) continue;
                var piece = pieces.FirstOrDefault(p =>
                    p.ReservedFor == null && p.Item.Id == member.AssignedStockId);
                if (piece == null) continue;
                piece.ReservedFor = member.Id;
                member.AssignedStockPieceId = piece.PieceId;
            }
        }

        private static void AssignPiece(
            BridgeMember member, StockPieceSlot chosen, StockPieceSlot previous)
        {
            if (previous != null && previous != chosen) previous.ReservedFor = null;
            chosen.ReservedFor = member.Id;
            member.AssignedStockId = chosen.Item.Id;
            member.AssignedStockPieceId = chosen.PieceId;
        }

        private static void ReleaseAssignment(BridgeMember member, StockPieceSlot previous)
        {
            if (previous != null) previous.ReservedFor = null;
            member.AssignedStockId = null;
            member.AssignedStockPieceId = null;
        }

        private class StockPieceSlot
        {
            public StockItem Item;
            public string PieceId;
            public string ReservedFor;
        }

        private static double Dist(BridgeNode a, BridgeNode b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
