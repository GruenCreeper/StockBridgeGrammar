using System.Collections.Generic;
using Newtonsoft.Json;

namespace StockBridgeGrammar.Models
{
    // Per-member assignment record: which stock piece, how much waste,
    // resulting status.
    public class MemberAssignment
    {
        [JsonProperty("memberId")]       public string MemberId       { get; set; }
        [JsonProperty("assignedStockId")] public string AssignedStockId { get; set; }
        [JsonProperty("assignedStockPieceId")] public string AssignedStockPieceId { get; set; }
        [JsonProperty("memberLength")]   public double MemberLength   { get; set; }
        [JsonProperty("stockLength")]    public double StockLength    { get; set; }
        [JsonProperty("waste")]          public double Waste          { get; set; }
        [JsonProperty("status")]         public string Status         { get; set; }
    }

    // Aggregated assignment summary.
    public class AssignmentResult
    {
        [JsonProperty("assignments")]    public List<MemberAssignment> Assignments { get; set; } = new List<MemberAssignment>();
        [JsonProperty("unresolvedIds")]  public List<string>           UnresolvedIds { get; set; } = new List<string>();
        [JsonProperty("totalStockUsed")] public double TotalStockUsed { get; set; }
        [JsonProperty("totalMemberLen")] public double TotalMemberLength { get; set; }
        [JsonProperty("totalMatchedMemberLen")] public double TotalMatchedMemberLength { get; set; }
        [JsonProperty("materialUsageRate")] public double MaterialUsageRate { get; set; }
        [JsonProperty("avgWaste")]       public double AvgWaste { get; set; }
        [JsonProperty("stockFitRate")]   public double StockFitRate { get; set; }
    }
}
