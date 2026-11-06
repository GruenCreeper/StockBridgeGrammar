using System;
using System.Collections.Generic;
using System.Linq;
using StockBridgeGrammar.Models;

namespace StockBridgeGrammar.Core
{
    public static class StockInventory
    {
        public static List<StockItem> FromJson(IEnumerable<string> jsons)
        {
            var items = new List<StockItem>();
            int index = 0;
            foreach (var json in jsons)
            {
                index++;
                if (string.IsNullOrWhiteSpace(json)) continue;
                try { items.AddRange(JsonIO.DeserializeStock(json)); }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Stock input {index}: {ex.Message}", ex);
                }
            }
            return items;
        }

        public static List<StockItem> FromFields(List<string> ids, List<string> types,
            List<double> lengths, List<double> widths, List<double> heights,
            List<string> materials, List<int> counts)
        {
            int n = new[] { ids.Count, types.Count, lengths.Count, widths.Count,
                heights.Count, materials.Count, counts.Count }.Max();
            var items = new List<StockItem>();
            if (n == 0) return items;
            if (lengths.Count == 0) throw new ArgumentException("Lengths (L) are required.");
            CheckCount("Ids", ids.Count, n); CheckCount("Types", types.Count, n);
            CheckCount("Lengths", lengths.Count, n); CheckCount("Widths", widths.Count, n);
            CheckCount("Heights", heights.Count, n); CheckCount("Materials", materials.Count, n);
            CheckCount("Counts", counts.Count, n);
            for (int i = 0; i < n; i++)
                items.Add(new StockItem
                {
                    Id = At(ids, i, null), Type = At(types, i, "linear"),
                    Length = At(lengths, i, 0), Width = At(widths, i, 0.08),
                    Height = At(heights, i, 0.12),
                    Material = At(materials, i, "reclaimed timber"), Count = At(counts, i, 1),
                });
            return items;
        }

        private static void CheckCount(string name, int count, int n)
        {
            if (count != 0 && count != 1 && count != n)
                throw new ArgumentException($"{name} has {count} values; provide one shared value or {n} values (one per stock entry).");
        }

        private static T At<T>(List<T> values, int i, T fallback) =>
            values.Count == 0 ? fallback : values[values.Count == 1 ? 0 : i];

        public static void ValidateItem(StockItem item)
        {
            if (item == null) throw new ArgumentException("Null stock entry.");
            if (!Finite(item.Length) || item.Length <= 0)
                throw new ArgumentException($"{item.Id}: Length must be finite and > 0 (metres).");
            if (!Finite(item.Width) || !Finite(item.Height) || item.Width < 0 || item.Height < 0)
                throw new ArgumentException($"{item.Id}: Width and Height must be finite and >= 0 (metres).");
            if (item.Count < 0) throw new ArgumentException($"{item.Id}: Count must be >= 0.");
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static List<string> Normalize(List<StockItem> items)
        {
            foreach (var item in items) ValidateItem(item);
            var warnings = new List<string>();
            // Reserve explicit IDs so generated names never steal a later item's ID.
            var reserved = new HashSet<string>(items.Where(x => !string.IsNullOrWhiteSpace(x.Id)).Select(x => x.Id.Trim()));
            var used = new HashSet<string>();
            int next = 1;
            foreach (var item in items)
            {
                string id = item.Id?.Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    do { id = "S" + next++; } while (reserved.Contains(id) || used.Contains(id));
                    warnings.Add($"Empty stock id assigned: {id}.");
                }
                else if (used.Contains(id))
                {
                    string original = id;
                    int suffix = 2;
                    do { id = original + "_" + suffix++; } while (reserved.Contains(id) || used.Contains(id));
                    warnings.Add($"Duplicate stock id '{original}' renamed to '{id}'; dimensions and count preserved.");
                }
                item.Id = id;
                used.Add(id);
                if (item.Count == 0) warnings.Add($"{id}: count is 0; this stock will not be used.");
                if (item.Width == 0 || item.Height == 0)
                    warnings.Add($"{id}: section dimension is 0; provide W and H for section checks.");
            }
            return warnings;
        }
    }
}
