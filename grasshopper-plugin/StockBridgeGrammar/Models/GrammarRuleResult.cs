using System.Collections.Generic;
using Newtonsoft.Json;

namespace StockBridgeGrammar.Models
{
    // One applied transformation, suitable for a rule-history log.
    public class GrammarRuleHistoryStep
    {
        [JsonProperty("index")]    public int    Index    { get; set; }
        [JsonProperty("ruleId")]   public string RuleId   { get; set; }
        [JsonProperty("ruleName")] public string RuleName { get; set; }
        [JsonProperty("targetId")] public string TargetId { get; set; }
        [JsonProperty("note")]     public string Note     { get; set; }
        [JsonProperty("addedNodeIds")]    public List<string> AddedNodeIds    { get; set; } = new List<string>();
        [JsonProperty("addedMemberIds")]  public List<string> AddedMemberIds  { get; set; } = new List<string>();
        [JsonProperty("removedMemberIds")] public List<string> RemovedMemberIds { get; set; } = new List<string>();
    }

    // Returned by GrammarRules.Apply(...) — new graph + history step + log.
    public class GrammarRuleResult
    {
        public BridgeGraph Graph { get; set; }
        public GrammarRuleHistoryStep Step { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public bool Success => Graph != null;
    }
}
