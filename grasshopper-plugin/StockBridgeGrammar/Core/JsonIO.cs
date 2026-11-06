using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockBridgeGrammar.Models;

namespace StockBridgeGrammar.Core
{
    // Centralized JSON (de)serialization helpers. Indented output so the
    // strings Grasshopper components emit are human-readable in panels.
    // Field names match the web tool (src/types.ts).
    public static class JsonIO
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting        = Formatting.Indented,
            FloatFormatHandling = FloatFormatHandling.DefaultValue,
        };

        public static string Serialize(object o) =>
            JsonConvert.SerializeObject(o, _settings);

        public static BridgeGraph DeserializeGraph(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return new BridgeGraph();
            return JsonConvert.DeserializeObject<BridgeGraph>(s) ?? new BridgeGraph();
        }

        public static List<StockItem> DeserializeStock(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return new List<StockItem>();
            // The same parser is used by the builder and all stock consumers.
            var items = new List<StockItem>();
            ReadStockToken(JToken.Parse(s), items);
            return items;
        }

        private static void ReadStockToken(JToken token, List<StockItem> items)
        {
            if (token is JArray array)
            {
                foreach (var child in array) ReadStockToken(child, items);
                return;
            }
            if (token is JObject obj)
            {
                var wrapped = obj.GetValue("items", StringComparison.OrdinalIgnoreCase);
                if (wrapped != null)
                {
                    if (!(wrapped is JArray))
                        throw new JsonSerializationException("Stock 'items' must be an array.");
                    ReadStockToken(wrapped, items);
                    return;
                }
                if (obj.GetValue("length", StringComparison.OrdinalIgnoreCase) != null)
                {
                    items.Add(obj.ToObject<StockItem>());
                    return;
                }
            }
            throw new JsonSerializationException(
                "Expected a stock item with 'length', a stock array, or {\"items\":[...]}. " +
                "Connect SBItem.J (not D) through SBStockBuild before downstream operations.");
        }

        public static List<GrammarMismatch> DeserializeMismatches(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return new List<GrammarMismatch>();
            return JsonConvert.DeserializeObject<List<GrammarMismatch>>(s)
                   ?? new List<GrammarMismatch>();
        }

        public static Dictionary<string, object> ParseParams(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return new Dictionary<string, object>();
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(s)
                       ?? new Dictionary<string, object>();
            }
            catch (Exception)
            {
                return new Dictionary<string, object>();
            }
        }

    }
}
