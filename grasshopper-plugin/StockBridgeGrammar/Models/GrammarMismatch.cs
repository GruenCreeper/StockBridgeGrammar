using System.Collections.Generic;
using Newtonsoft.Json;

namespace StockBridgeGrammar.Models
{
    // Why a member / bay / node is flagged + which rules might fix it.
    // Reason strings match the web tool's `GrammarMismatchReason` union.
    public class GrammarMismatch
    {
        [JsonProperty("id")]               public string Id          { get; set; }
        [JsonProperty("targetId")]         public string TargetId    { get; set; }
        [JsonProperty("targetType")]       public string TargetType  { get; set; } // "member"|"bay"|"node"|"deckPlate"|"system"
        [JsonProperty("reason")]           public string Reason      { get; set; }
        [JsonProperty("severity")]         public double Severity    { get; set; }
        [JsonProperty("message")]          public string Message     { get; set; }
        [JsonProperty("suggestedRuleIds")] public List<string> SuggestedRuleIds { get; set; } = new List<string>();
    }
}
