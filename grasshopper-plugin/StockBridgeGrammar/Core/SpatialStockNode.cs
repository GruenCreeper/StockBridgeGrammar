using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using StockBridgeGrammar.Models;

namespace StockBridgeGrammar.Core
{
    /// <summary>Place a new spatial node at the intersection of three available stock-length spheres.</summary>
    internal static class SpatialStockNode
    {
        internal static GrammarRuleResult Apply(BridgeGraph graph, string targetId, JObject parameters, List<StockItem> stock)
        {
            var result = new GrammarRuleResult();
            var p = parameters ?? new JObject();
            if (graph?.Nodes == null || graph.Members == null)
                return Refuse(result, "addNodeToStock requires a graph with node and member lists.");
            if (graph.Nodes.Any(n => n == null || string.IsNullOrWhiteSpace(n.Id) || !Finite(n.X) || !Finite(n.Y) || !Finite(n.Z)) ||
                graph.Nodes.Select(n => n.Id).Distinct(StringComparer.Ordinal).Count() != graph.Nodes.Count ||
                graph.Members.Any(m => m == null))
                return Refuse(result, "addNodeToStock requires finite node coordinates and unique, non-empty node IDs.");
            if (!Number(p, "degree", 3, out var degree) || degree != 3)
                return Refuse(result, "addNodeToStock now creates a spatial node with exactly 3 stock-length members; degree must be 3.");
            if (!Number(p, "minHeight", 0.01, out var minHeight) || minHeight <= 0 ||
                !Number(p, "snapTol", 0.25, out var snapTol) || snapTol < 0 ||
                !Number(p, "collisionTol", 0.001, out var collisionTol) || collisionTol < 0)
                return Refuse(result, "minHeight must be positive; snapTol and collisionTol must be non-negative finite distances in metres.");
            if (p["pointSupport"] != null && p["pointSupport"].Type != JTokenType.Boolean)
                return Refuse(result, "pointSupport must be true or false.");
            bool pointSupport = p["pointSupport"]?.Value<bool>() ?? true;
            collisionTol = Math.Max(1e-8, collisionTol);

            BridgeNode hint = null;
            JToken hintToken = p["targetPoint"] ?? p["at"];
            if (hintToken != null && !Point(hintToken, out hint))
                return Refuse(result, "targetPoint (or legacy at) must contain exactly 3 finite coordinates [x,y,z].");
            if (p["targetPoint"] == null && p["at"] != null)
                result.Warnings.Add("Legacy 'at' is used as the approximate target point; use targetPoint for new definitions.");

            // Existing objects are read-only during search. New anchors and braces are
            // staged in private lists and committed only after a complete solution exists.
            var work = new BridgeGraph { Nodes = new List<BridgeNode>(graph.Nodes), Members = new List<BridgeMember>(graph.Members) };
            var addedAnchors = new List<string>();
            if (!Anchors(work, targetId, p, hint, snapTol, pointSupport, addedAnchors, result, out var triples))
                return result;
            if (!Inventory(graph, stock, result, out var groups)) return result;

            Candidate best = null;
            int nonCollinear = 0;
            bool foundSpatialIntersection = false;
            foreach (var anchors in triples)
            {
                var a = Vec.Of(anchors[0]);
                var ab = Vec.Of(anchors[1]) - a;
                var ac = Vec.Of(anchors[2]) - a;
                double d = ab.Length, acLength = ac.Length;
                double bcLength = (Vec.Of(anchors[2]) - Vec.Of(anchors[1])).Length;
                double scale = Math.Max(d, Math.Max(acLength, bcLength));
                if (!Finite(scale) || scale <= 1e-8 || ab.Cross(ac).Length <= 1e-6 * scale * scale) continue;
                nonCollinear++;
                var ex = ab / d;
                double i = ex.Dot(ac);
                var transverse = ac - ex * i;
                double j = transverse.Length;
                var ey = transverse / j;
                var normal = ex.Cross(ey);

                foreach (var ga in groups)
                foreach (var gb in groups)
                {
                    if (!CanMeet(ga.Length, gb.Length, d) || (ga == gb && ga.Pieces.Count < 2)) continue;
                    double x = (ga.Length * ga.Length - gb.Length * gb.Length + d * d) / (2 * d);
                    foreach (var gc in groups)
                    {
                        int needC = 1 + (gc == ga ? 1 : 0) + (gc == gb ? 1 : 0);
                        if (gc.Pieces.Count < needC || !CanMeet(ga.Length, gc.Length, acLength) || !CanMeet(gb.Length, gc.Length, bcLength)) continue;
                        double y = (ga.Length * ga.Length - gc.Length * gc.Length + i * i + j * j - 2 * i * x) / (2 * j);
                        double heightSquared = ga.Length * ga.Length - x * x - y * y;
                        if (!Finite(heightSquared) || heightSquared < minHeight * minHeight) continue;
                        double height = Math.Sqrt(heightSquared);
                        var inPlane = a + ex * x + ey * y;
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            var position = inPlane + normal * (sign * height);
                            if (!position.IsFinite) continue;
                            var lengths = new[] { ga.Length, gb.Length, gc.Length };
                            bool exact = true;
                            for (int index = 0; index < 3; index++)
                                if (Math.Abs((position - Vec.Of(anchors[index])).Length - lengths[index]) > 1e-8 * Math.Max(1, lengths[index])) exact = false;
                            if (!exact) continue;
                            foundSpatialIntersection = true;
                            var slots = new[] { ga.Pieces[0], gb.Pieces[ga == gb ? 1 : 0], gc.Pieces[needC - 1] };
                            var candidate = new Candidate
                            {
                                Position = position, Anchors = anchors, Slots = slots, Height = height,
                                Distance = hint == null ? 0 : (position - Vec.Of(hint)).Length,
                                TotalLength = lengths.Sum()
                            };
                            // A worse candidate cannot win even if collision-free.
                            // Avoid rescanning every graph node for those candidates.
                            if (best != null && Compare(candidate, best) >= 0) continue;
                            if (work.Nodes.Any(n => (position - Vec.Of(n)).Length <= collisionTol)) continue;
                            best = candidate;
                        }
                    }
                }
            }
            if (best == null)
            {
                if (nonCollinear == 0)
                    return Refuse(result, "The 3 anchors are coincident or nearly collinear; choose a triangle with a non-zero area.");
                if (foundSpatialIntersection)
                    return Refuse(result, "Every stock-length spatial solution collides with an existing node; change anchors or stock.");
                return Refuse(result, $"No available combination of 3 distinct stock pieces forms a spatial node at least {minHeight:G6}m from the anchor plane. Stock lengths are used in full; planar/cut-to-fit substitutes are not generated.");
            }

            var node = new BridgeNode { Id = NextId(work.Nodes.Select(n => n.Id), "H", work.Nodes.Count), X = best.Position.X, Y = best.Position.Y, Z = best.Position.Z };
            work.Nodes.Add(node);
            var addedMembers = new List<string>();
            for (int index = 0; index < 3; index++)
            {
                var slot = best.Slots[index];
                string memberId = NextId(work.Members.Select(m => m.Id), best.Anchors[0].Id + ".h", 1);
                work.Members.Add(new BridgeMember
                {
                    Id = memberId, From = node.Id, To = best.Anchors[index].Id,
                    Length = slot.Length, AssignedStockId = slot.StockId, AssignedStockPieceId = slot.PieceId, Waste = 0
                });
                addedMembers.Add(memberId);
            }
            graph.Nodes = work.Nodes;
            graph.Members = work.Members;
            result.Graph = graph;
            result.Step = new GrammarRuleHistoryStep
            {
                RuleId = "addNodeToStock", RuleName = "Add Spatial Node to Stock", TargetId = targetId ?? string.Join(",", best.Anchors.Select(n => n.Id)),
                AddedNodeIds = new List<string> { node.Id }.Concat(addedAnchors).ToList(), AddedMemberIds = addedMembers,
                Note = $"Added spatial node {node.Id} at ({Format(node.X)},{Format(node.Y)},{Format(node.Z)}) using 3 full-length, distinct stock pieces; " +
                    $"height from anchor plane = {Format(best.Height)}m. Anchors: [{string.Join(",", best.Anchors.Select(n => n.Id))}]. " +
                    $"Reserved pieces: [{string.Join(",", best.Slots.Select(s => s.PieceId + "=" + Format(s.Length) + "m"))}]. " +
                    (hint == null ? "No target point supplied; shortest total stock, then stable coordinate order selects the solution." :
                        $"Approximate target point ({Format(hint.X)},{Format(hint.Y)},{Format(hint.Z)}); selected feasible solution is {Format(best.Distance)}m away.")
            };
            return result;
        }

        private static bool Anchors(BridgeGraph work, string targetId, JObject p, BridgeNode hint, double snapTol, bool pointSupport,
            List<string> added, GrammarRuleResult result, out List<BridgeNode[]> triples)
        {
            triples = new List<BridgeNode[]>();
            if (p["points"] != null)
            {
                var points = p["points"] as JArray;
                if (points == null || points.Count != 3)
                    return Fail(result, "Position / points must contain exactly 3 anchor points; extra points are not ignored.");
                var requested = new List<BridgeNode>();
                foreach (var token in points)
                {
                    if (!Point(token, out var point)) return Fail(result, "Each anchor point must contain exactly 3 finite coordinates [x,y,z].");
                    requested.Add(point);
                }
                var resolved = new List<BridgeNode>();
                foreach (var point in requested.OrderBy(n => n.X).ThenBy(n => n.Y).ThenBy(n => n.Z))
                {
                    var nearest = work.Nodes.Select(n => new { Node = n, Distance = (Vec.Of(n) - Vec.Of(point)).Length })
                        .Where(x => x.Distance <= snapTol).OrderBy(x => x.Distance).ThenBy(x => x.Node.Id, StringComparer.Ordinal).FirstOrDefault();
                    var node = nearest?.Node;
                    if (node == null)
                    {
                        node = new BridgeNode { Id = NextId(work.Nodes.Select(n => n.Id), pointSupport ? "SUP" : "P", work.Nodes.Count), X = point.X, Y = point.Y, Z = point.Z,
                            IsSupport = pointSupport ? (bool?)true : null };
                        work.Nodes.Add(node);
                        added.Add(node.Id);
                    }
                    resolved.Add(node);
                }
                if (resolved.Select(n => n.Id).Distinct(StringComparer.Ordinal).Count() != 3)
                    return Fail(result, "The 3 Position points resolve to fewer than 3 distinct anchors after snapping; choose distinct points or reduce snapTol.");
                triples.Add(Sort(resolved));
                return true;
            }
            if (p["corners"] != null)
            {
                var corners = p["corners"] as JArray;
                if (corners == null || corners.Count != 3 || corners.Any(t => t.Type != JTokenType.String))
                    return Fail(result, "corners must contain exactly 3 distinct existing node IDs.");
                return NodeIds(work, corners.Select(t => t.Value<string>()).ToArray(), result, triples);
            }
            var tokens = (targetId ?? "").Split(new[] { ',', ' ', ';', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 3)
                return NodeIds(work, tokens, result, triples);
            var member = work.Members.FirstOrDefault(m => m.Id == targetId);
            if (member != null)
            {
                var a = work.Nodes.FirstOrDefault(n => n.Id == member.From);
                var b = work.Nodes.FirstOrDefault(n => n.Id == member.To);
                if (a == null || b == null || a.Id == b.Id) return Fail(result, "The target member must have 2 distinct, existing endpoints.");
                bool Edge(string x, string y) => work.Members.Any(m => (m.From == x && m.To == y) || (m.From == y && m.To == x));
                var candidates = work.Nodes.Where(n => n.Id != a.Id && n.Id != b.Id && Edge(a.Id, n.Id) && Edge(b.Id, n.Id));
                if (p["apex"] != null)
                {
                    if (p["apex"].Type != JTokenType.String) return Fail(result, "apex must be an existing node ID adjacent to both member endpoints.");
                    candidates = candidates.Where(n => n.Id == p["apex"].Value<string>());
                }
                foreach (var c in candidates.OrderBy(n => n.Id, StringComparer.Ordinal)) triples.Add(Sort(new[] { a, b, c }));
                if (triples.Count == 0)
                    return Fail(result, "The target member has no closed adjacent triangle; supply exactly 3 Position anchors or corners instead.");
                result.Warnings.Add("Legacy member Target selected anchor triangles. Position supplies anchors; targetPoint supplies the approximate new-node location.");
                return true;
            }
            if (hint != null && work.Nodes.Count >= 3)
            {
                triples.Add(Sort(work.Nodes.OrderBy(n => (Vec.Of(n) - Vec.Of(hint)).Length).ThenBy(n => n.Id, StringComparer.Ordinal).Take(3)));
                result.Warnings.Add("No explicit anchors supplied; using the 3 existing nodes nearest the target point. Supply 3 Position points to fix the anchors.");
                return true;
            }
            return Fail(result, "Supply exactly 3 Position anchor points, 3 corners node IDs, a 3-node Target, or a target member with an adjacent triangle.");
        }

        private static bool NodeIds(BridgeGraph work, string[] ids, GrammarRuleResult result, List<BridgeNode[]> triples)
        {
            if (ids.Length != 3 || ids.Distinct(StringComparer.Ordinal).Count() != 3)
                return Fail(result, "Exactly 3 distinct anchor node IDs are required.");
            var nodes = ids.Select(id => work.Nodes.FirstOrDefault(n => n.Id == id)).ToArray();
            if (nodes.Any(n => n == null)) return Fail(result, "One or more anchor node IDs do not exist in the graph.");
            triples.Add(Sort(nodes));
            return true;
        }

        private static bool Inventory(BridgeGraph graph, List<StockItem> stock, GrammarRuleResult result, out List<LengthGroup> groups)
        {
            groups = new List<LengthGroup>();
            var ranges = new List<StockRange>();
            var nextIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in stock ?? new List<StockItem>())
            {
                if (item == null || item.Count <= 0) continue;
                string id = string.IsNullOrWhiteSpace(item.Id) ? "stock" : item.Id;
                int start = nextIndex.TryGetValue(id, out var previous) ? previous : 0;
                if (item.Count > int.MaxValue - start) return Fail(result, "Stock piece counts exceed the supported ID range.");
                ranges.Add(new StockRange { StockId = id, Start = start, Count = item.Count, Length = item.Length });
                nextIndex[id] = start + item.Count;
            }
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in graph.Members.Where(m => !string.IsNullOrWhiteSpace(m.AssignedStockPieceId)))
            {
                string pieceId = member.AssignedStockPieceId;
                int separator = pieceId.LastIndexOf('#');
                int pieceIndex = 0;
                var range = separator > 0 && int.TryParse(pieceId.Substring(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out pieceIndex)
                    ? ranges.FirstOrDefault(r => r.StockId == pieceId.Substring(0, separator) && pieceIndex > r.Start && pieceIndex <= r.Start + r.Count)
                    : null;
                var slot = range == null ? null : Piece(range, pieceIndex);
                if (slot == null || slot.PieceId != pieceId)
                    return Fail(result, $"Recorded piece {member.AssignedStockPieceId} on member {member.Id} is absent from the supplied inventory. Supply the complete inventory or update its assignments first.");
                if (!string.IsNullOrWhiteSpace(member.AssignedStockId) && member.AssignedStockId != slot.StockId)
                    return Fail(result, $"Member {member.Id} has inconsistent stock type and physical piece IDs.");
                if (!used.Add(slot.PieceId)) return Fail(result, $"Physical stock piece {slot.PieceId} is already assigned to more than one member; resolve that conflict first.");
            }
            var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            double MemberLength(BridgeMember m) => nodes.TryGetValue(m.From ?? "", out var a) && nodes.TryGetValue(m.To ?? "", out var b)
                ? (Vec.Of(a) - Vec.Of(b)).Length : m.Length;
            foreach (var member in graph.Members.Where(m => string.IsNullOrWhiteSpace(m.AssignedStockPieceId) && !string.IsNullOrWhiteSpace(m.AssignedStockId))
                .OrderByDescending(MemberLength).ThenBy(m => m.Id, StringComparer.Ordinal))
            {
                double length = MemberLength(member);
                var slot = ranges.Where(r => r.StockId == member.AssignedStockId && Finite(r.Length) && r.Length > 0 && r.Length >= length - 1e-8)
                    .SelectMany(r => AvailablePieces(r, used, 1)).OrderBy(s => s.Length).ThenBy(s => s.Index).FirstOrDefault();
                if (slot == null) return Fail(result, $"Insufficient inventory to reserve the recorded stock assignment on member {member.Id} ({member.AssignedStockId}); no new pieces were allocated.");
                used.Add(slot.PieceId);
            }
            int unassigned = graph.Members.Count(m => string.IsNullOrWhiteSpace(m.AssignedStockId) && string.IsNullOrWhiteSpace(m.AssignedStockPieceId));
            if (unassigned > 0)
                result.Warnings.Add($"Inventory scope: reserved existing recorded assignments only; {unassigned} unassigned existing member(s) do not reserve stock. This result does not establish inventory feasibility for the whole graph.");
            // Counts can be very large. Keep only the first three free pieces in
            // each range; no candidate can consume more than three of any length.
            var available = ranges.Where(r => Finite(r.Length) && r.Length > 0).SelectMany(r => AvailablePieces(r, used, 3)).ToList();
            if (available.Count < 3) return Fail(result, $"Insufficient inventory: need 3 distinct available physical pieces; {available.Count} remain after existing assignments are reserved. Count <= 0 supplies no pieces.");
            groups = available.GroupBy(s => s.Length).OrderBy(g => g.Key)
                .Select(g => new LengthGroup { Length = g.Key, Pieces = g.OrderBy(s => s.StockId, StringComparer.Ordinal).ThenBy(s => s.Index).Take(3).ToList() }).ToList();
            return true;
        }

        private static int Compare(Candidate a, Candidate b)
        {
            int c = CompareNumber(a.Distance, b.Distance);
            if (c == 0) c = CompareNumber(a.TotalLength, b.TotalLength);
            if (c == 0) c = a.Position.X.CompareTo(b.Position.X);
            if (c == 0) c = a.Position.Y.CompareTo(b.Position.Y);
            if (c == 0) c = a.Position.Z.CompareTo(b.Position.Z);
            for (int index = 0; c == 0 && index < 3; index++) c = StringComparer.Ordinal.Compare(a.Anchors[index].Id, b.Anchors[index].Id);
            for (int index = 0; c == 0 && index < 3; index++) c = StringComparer.Ordinal.Compare(a.Slots[index].PieceId, b.Slots[index].PieceId);
            return c;
        }
        private static int CompareNumber(double a, double b) => Math.Abs(a - b) <= 1e-10 + 1e-14 * Math.Max(Math.Abs(a), Math.Abs(b)) ? 0 : a.CompareTo(b);
        private static bool CanMeet(double a, double b, double distance) => Math.Abs(a - b) <= distance + 1e-9 && distance <= a + b + 1e-9;
        private static BridgeNode[] Sort(IEnumerable<BridgeNode> nodes) => nodes.OrderBy(n => n.Id, StringComparer.Ordinal).ToArray();
        private static GrammarRuleResult Refuse(GrammarRuleResult result, string warning) { result.Warnings.Add(warning); return result; }
        private static bool Fail(GrammarRuleResult result, string warning) { result.Warnings.Add(warning); return false; }
        private static string Format(double number) => number.ToString("G8", CultureInfo.InvariantCulture);
        private static bool Finite(double number) => !double.IsNaN(number) && !double.IsInfinity(number);
        private static string NextId(IEnumerable<string> ids, string prefix, int start)
        {
            var used = new HashSet<string>(ids, StringComparer.Ordinal);
            string id;
            do { id = prefix + (start++).ToString(CultureInfo.InvariantCulture); } while (used.Contains(id));
            return id;
        }
        private static bool Number(JObject p, string key, double fallback, out double value)
        {
            value = fallback;
            var token = p[key];
            if (token == null) return true;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) return false;
            value = token.Value<double>();
            return Finite(value);
        }
        private static bool Point(JToken token, out BridgeNode node)
        {
            node = null;
            var coordinates = token as JArray;
            if (coordinates == null || coordinates.Count != 3 || coordinates.Any(t => t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return false;
            var values = coordinates.Select(t => t.Value<double>()).ToArray();
            if (values.Any(v => !Finite(v))) return false;
            node = new BridgeNode { X = values[0], Y = values[1], Z = values[2] };
            return true;
        }
        private static Slot Piece(StockRange range, int index) => new Slot
        {
            StockId = range.StockId, PieceId = range.StockId + "#" + index.ToString(CultureInfo.InvariantCulture), Length = range.Length, Index = index
        };
        private static IEnumerable<Slot> AvailablePieces(StockRange range, HashSet<string> used, int maximum)
        {
            int yielded = 0;
            for (long index = (long)range.Start + 1; index <= (long)range.Start + range.Count && yielded < maximum; index++)
            {
                var piece = Piece(range, (int)index);
                if (used.Contains(piece.PieceId)) continue;
                yielded++;
                yield return piece;
            }
        }
        private sealed class Slot { public string StockId, PieceId; public double Length; public int Index; }
        private sealed class StockRange { public string StockId; public double Length; public int Start, Count; }
        private sealed class LengthGroup { public double Length; public List<Slot> Pieces; }
        private sealed class Candidate { public Vec Position; public BridgeNode[] Anchors; public Slot[] Slots; public double Distance, TotalLength, Height; }
        private readonly struct Vec
        {
            internal readonly double X, Y, Z;
            internal Vec(double x, double y, double z) { X = x; Y = y; Z = z; }
            internal static Vec Of(BridgeNode n) => new Vec(n.X, n.Y, n.Z);
            internal double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
            internal bool IsFinite => Finite(X) && Finite(Y) && Finite(Z);
            internal double Dot(Vec v) => X * v.X + Y * v.Y + Z * v.Z;
            internal Vec Cross(Vec v) => new Vec(Y * v.Z - Z * v.Y, Z * v.X - X * v.Z, X * v.Y - Y * v.X);
            public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vec operator *(Vec a, double k) => new Vec(a.X * k, a.Y * k, a.Z * k);
            public static Vec operator /(Vec a, double k) => new Vec(a.X / k, a.Y / k, a.Z / k);
        }
    }
}
