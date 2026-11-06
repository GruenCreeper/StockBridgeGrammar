using Newtonsoft.Json;

namespace StockBridgeGrammar.Models
{
    // One inventory entry. `count` is how many pieces of this size are
    // available; the assigner expands to one piece per slot at runtime.
    public class StockItem
    {
        [JsonProperty("id")]       public string Id       { get; set; }
        [JsonProperty("type")]     public string Type     { get; set; } = "linear";
        [JsonProperty("length")]   public double Length   { get; set; }
        [JsonProperty("width")]    public double Width    { get; set; }
        [JsonProperty("height")]   public double Height   { get; set; }
        [JsonProperty("material")] public string Material { get; set; } = "reclaimed timber";
        [JsonProperty("count")]    public int    Count    { get; set; } = 1;

        // Section and material properties used by Section Check.
        // Units: lengths in m, force in kN ⇒ stress in kPa.
        //   allowableStress σ — max axial stress the piece can carry (kPa).
        //   elasticModulus  E — Young's modulus (kPa), for the buckling check.
        [JsonProperty("allowableStress", NullValueHandling = NullValueHandling.Ignore)]
        public double? AllowableStress { get; set; }     // kPa (e.g. 14000 = 14 MPa)
        [JsonProperty("elasticModulus", NullValueHandling = NullValueHandling.Ignore)]
        public double? ElasticModulus { get; set; }       // kPa (e.g. 11_000_000 = 11 GPa)

        // Cross-sectional area W×H (m²). Not serialized — derived from W,H.
        [JsonIgnore] public double Area => Width * Height;
    }
}
