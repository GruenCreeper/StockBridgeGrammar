using Newtonsoft.Json;

namespace StockBridgeGrammar.Models
{
    // A graph node. Z is preserved so 3-D / irregular structures work; the
    // recognizer no longer infers chord roles from elevation.
    public class BridgeNode
    {
        [JsonProperty("id")]    public string Id  { get; set; }
        [JsonProperty("x")]     public double X   { get; set; }
        [JsonProperty("y")]     public double Y   { get; set; }
        [JsonProperty("z")]     public double Z   { get; set; }

        [JsonProperty("originalRhinoId", NullValueHandling = NullValueHandling.Ignore)]
        public string OriginalRhinoId { get; set; }

        // Set true only when the user supplies a Support Point that snaps to
        // this node. No automatic inference.
        [JsonProperty("isSupport", NullValueHandling = NullValueHandling.Ignore)]
        public bool? IsSupport { get; set; }
    }
}
