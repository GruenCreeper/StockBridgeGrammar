using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using StockBridgeGrammar.Models;

namespace StockBridgeGrammar.Core
{
    // Converts each curve to one graph member and merges endpoints by tolerance.
    // Preserves 3-D coordinates without inferring member roles or bays.
    // Supports are marked only from explicitly supplied support points.
    public static class Recognizer
    {
        public class Options
        {
            public double Tolerance         = 0.01;
            public Vector3d SpanDirection   = new Vector3d(1, 0, 0); // retained for downstream rules that may want a hint
            public IEnumerable<Point3d> SupportPoints;
        }

        public static BridgeGraph Recognize(IEnumerable<Curve> curves, Options opts)
        {
            opts = opts ?? new Options();
            var report   = new List<string>();
            var warnings = new List<string>();

            // 1. Collect curve endpoints.
            var segments = new List<(Curve curve, Point3d a, Point3d b)>();
            // Each curve = ONE member, regardless of how many internal vertices
            // it has. A polyline's interior vertices are just its drawn shape,
            // NOT structural nodes — only its two endpoints become graph nodes.
            foreach (var c in curves ?? Enumerable.Empty<Curve>())
            {
                if (!TryEndpoints(c, out var a, out var b))
                {
                    warnings.Add("skipped a curve with no endpoints");
                    continue;
                }
                segments.Add((c, a, b));
            }
            if (segments.Count == 0)
            {
                warnings.Add("no usable curves provided");
                return new BridgeGraph { Warnings = warnings, Report = report };
            }

            // 2. Merge endpoints by tolerance.
            var raw = new List<Point3d>(segments.Count * 2);
            foreach (var s in segments) { raw.Add(s.a); raw.Add(s.b); }
            var merged = MergeNearbyPoints(raw, opts.Tolerance, out var idxFor);
            report.Add($"merged {raw.Count} endpoints to {merged.Count} nodes (tol={opts.Tolerance})");

            // 3. Build nodes.
            var nodes = new List<BridgeNode>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
            {
                nodes.Add(new BridgeNode
                {
                    Id = $"N{i}",
                    X  = Math.Round(merged[i].X, 4),
                    Y  = Math.Round(merged[i].Y, 4),
                    Z  = Math.Round(merged[i].Z, 4),
                });
            }

            // 4. Build members (no Role).
            var members = new List<BridgeMember>(segments.Count);
            for (int k = 0; k < segments.Count; k++)
            {
                int aIdx = idxFor[2 * k];
                int bIdx = idxFor[2 * k + 1];
                if (aIdx == bIdx)
                {
                    warnings.Add("skipped zero-length member after merge");
                    continue;
                }
                var a = merged[aIdx];
                var b = merged[bIdx];
                // Length = the curve's TRUE length (handles bent polylines so
                // stock matching uses the real material length), falling back
                // to the straight endpoint distance if the curve is unavailable.
                double len;
                try { len = segments[k].curve?.GetLength() ?? a.DistanceTo(b); }
                catch { len = a.DistanceTo(b); }
                if (len <= 0) len = a.DistanceTo(b);
                members.Add(new BridgeMember
                {
                    Id     = $"M{k}",
                    From   = $"N{aIdx}",
                    To     = $"N{bIdx}",
                    Length = Math.Round(len, 4),
                });
            }
            report.Add($"built {members.Count} members");

            // 5. Supports: only what the caller provided. No auto inference.
            ApplyUserSupports(nodes, opts, report, warnings);

            return new BridgeGraph
            {
                Source   = "rhinoRecognized",
                Nodes    = nodes,
                Members  = members,
                Report   = report,
                Warnings = warnings,
            };
        }

        // ------------------------- helpers ----------------------------

        private static bool TryEndpoints(Curve c, out Point3d a, out Point3d b)
        {
            if (c == null) { a = Point3d.Unset; b = Point3d.Unset; return false; }
            a = c.PointAtStart;
            b = c.PointAtEnd;
            return a.IsValid && b.IsValid;
        }

        public static List<Point3d> MergeNearbyPoints(
            List<Point3d> pts, double tol, out int[] indexFor)
        {
            var merged = new List<Point3d>();
            indexFor = new int[pts.Count];
            double tol2 = tol * tol;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                int match = -1;
                for (int j = 0; j < merged.Count; j++)
                {
                    var q = merged[j];
                    double dx = p.X - q.X, dy = p.Y - q.Y, dz = p.Z - q.Z;
                    if (dx * dx + dy * dy + dz * dz <= tol2)
                    {
                        match = j;
                        break;
                    }
                }
                if (match < 0)
                {
                    merged.Add(p);
                    indexFor[i] = merged.Count - 1;
                }
                else
                {
                    var q = merged[match];
                    merged[match] = new Point3d(
                        (q.X + p.X) * 0.5,
                        (q.Y + p.Y) * 0.5,
                        (q.Z + p.Z) * 0.5);
                    indexFor[i] = match;
                }
            }
            return merged;
        }

        private static void ApplyUserSupports(
            List<BridgeNode> nodes, Options opts,
            List<string> report, List<string> warnings)
        {
            if (opts.SupportPoints == null) return;
            int marked = 0;
            foreach (var sp in opts.SupportPoints)
            {
                var match = nodes.OrderBy(n =>
                    sp.DistanceTo(new Point3d(n.X, n.Y, n.Z))).FirstOrDefault();
                if (match != null)
                {
                    match.IsSupport = true;
                    marked++;
                }
            }
            if (marked > 0) report.Add($"user-provided supports: {marked}");
        }
    }
}
