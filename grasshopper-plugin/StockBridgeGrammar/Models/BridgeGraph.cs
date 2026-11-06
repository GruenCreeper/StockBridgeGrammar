using System.Collections.Generic;
using Newtonsoft.Json;

namespace StockBridgeGrammar.Models
{
    // The full bridge graph. Nodes + members only — no bays, no chord/role
    // classification, no auto-inferred supports.
    public class BridgeGraph
    {
        [JsonProperty("source")]
        public string Source { get; set; } = "rhinoRecognized";

        [JsonProperty("nodes")]
        public List<BridgeNode> Nodes { get; set; } = new List<BridgeNode>();

        [JsonProperty("members")]
        public List<BridgeMember> Members { get; set; } = new List<BridgeMember>();

        [JsonProperty("report", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Report { get; set; }

        [JsonProperty("warnings", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Warnings { get; set; }
    }
}
