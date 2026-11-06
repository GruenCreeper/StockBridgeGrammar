using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;

int passed = 0;
void Check(string name, Action run)
{
    run();
    Console.WriteLine("PASS " + name);
    passed++;
}
void Require(bool ok) { if (!ok) throw new Exception("Assertion failed"); }
void Reject(Action run)
{
    bool rejected = false;
    try { run(); } catch (Exception) { rejected = true; }
    Require(rejected);
}
const string one = "{\"id\":\"S1\",\"length\":2,\"width\":0.08,\"height\":0.12,\"count\":4}";
List<StockItem> Fields(List<double> lengths, List<string> ids = null,
    List<double> widths = null, List<int> counts = null) =>
    StockInventory.FromFields(ids ?? new(), new(), lengths, widths ?? new(),
        new() { 0.12 }, new() { "timber" }, counts ?? new() { 4 });

Check("Merge of three default SBItem JSON outputs preserves all pieces", () => {
    var items = StockInventory.FromJson(new[] { one, one, one });
    var warnings = StockInventory.Normalize(items);
    Require(items.Count == 3 && items.Sum(x => x.Count) == 12);
    Require(items.Select(x => x.Id).SequenceEqual(new[] { "S1", "S1_2", "S1_3" }));
    Require(warnings.Count == 2);
});
Check("Individual item accepted by downstream parser", () => Require(JsonIO.DeserializeStock(one).Count == 1));
Check("Arrays, nested arrays, wrappers and individual items combine", () => {
    var items = StockInventory.FromJson(new[] { one, "[" + one + "]", "{\"items\":[[" + one + "]]}" });
    Require(items.Count == 3);
});
Check("Empty JSON entries do not discard valid items", () =>
    Require(StockInventory.FromJson(new[] { "", " ", one }).Count == 1));
Check("Bad JSON fails whole merge instead of emitting partial inventory", () =>
    Reject(() => StockInventory.FromJson(new[] { one, "SBItem description" })));
Check("Unknown object is not silently interpreted as empty inventory", () => Reject(() => JsonIO.DeserializeStock("{}")));
Check("Null array entry rejected", () => Reject(() => JsonIO.DeserializeStock("[null]")));
Check("Non-array wrapper rejected", () => Reject(() => JsonIO.DeserializeStock("{\"items\":null}")));
Check("Empty array remains supported", () => Require(JsonIO.DeserializeStock("[]").Count == 0));
Check("Single width, height, material and count broadcast to every length", () => {
    var items = Fields(new() { 1, 2, 3 }, widths: new() { 0.1 });
    StockInventory.Normalize(items);
    Require(items.Count == 3 && items.All(x => x.Width == 0.1 && x.Height == 0.12 && x.Count == 4 && x.Material == "timber"));
    Require(items.Select(x => x.Id).SequenceEqual(new[] { "S1", "S2", "S3" }));
});
Check("Single length broadcasts across explicit IDs", () => {
    var items = Fields(new() { 2 }, new() { "A", "B", "C" });
    Require(items.Count == 3 && items.All(x => x.Length == 2));
});
Check("Partial field lists rejected instead of filling with zero", () =>
    Reject(() => Fields(new() { 1, 2, 3 }, widths: new() { 0.1, 0.2 })));
Check("Missing lengths rejected", () => Reject(() => Fields(new(), new() { "A" })));
Check("Absent optional dimensions use SBItem defaults", () => {
    var items = Fields(new() { 2 }); Require(items[0].Width == 0.08 && items[0].Height == 0.12);
});
Check("Generated IDs avoid collisions with later explicit IDs", () => {
    var items = Fields(new() { 2, 2, 2, 2 }, new() { "S1", "S1", "S1_2", "" });
    StockInventory.Normalize(items);
    Require(items.Select(x => x.Id).SequenceEqual(new[] { "S1", "S1_3", "S1_2", "S2" }));
});
Check("Same ID with different dimensions preserves separate inventory entries", () => {
    var items = Fields(new() { 1, 3 }, new() { "S1", "S1" });
    StockInventory.Normalize(items);
    Require(items[0].Length == 1 && items[1].Length == 3 && items[0].Id != items[1].Id);
});
Check("Zero/negative/nonfinite length rejected", () => {
    foreach (double length in new[] { 0.0, -1, double.NaN, double.PositiveInfinity })
        Reject(() => StockInventory.Normalize(Fields(new() { length })));
});
Check("Negative count rejected, zero count retained with warning", () => {
    Reject(() => StockInventory.Normalize(Fields(new() { 2 }, counts: new() { -1 })));
    Require(StockInventory.Normalize(Fields(new() { 2 }, counts: new() { 0 })).Any(x => x.Contains("count is 0")));
});
Check("Negative or nonfinite section dimensions rejected", () => {
    foreach (double width in new[] { -1.0, double.NaN, double.PositiveInfinity })
        Reject(() => StockInventory.Normalize(Fields(new() { 2 }, widths: new() { width })));
});
Check("Material strength data survive merge and serialization", () => {
    var items = StockInventory.FromJson(new[] { "{\"id\":\"A\",\"length\":2,\"allowableStress\":14000,\"elasticModulus\":11000000}" });
    StockInventory.Normalize(items);
    var again = JsonIO.DeserializeStock(JsonIO.Serialize(items));
    Require(again[0].AllowableStress == 14000 && again[0].ElasticModulus == 11000000);
});
Check("Merged stock is usable by real assignment with distinct piece IDs", () => {
    var items = StockInventory.FromJson(new[] { one, one });
    StockInventory.Normalize(items);
    var graph = new BridgeGraph();
    for (int i = 0; i < 9; i++) graph.Nodes.Add(new BridgeNode { Id = "N" + i, X = i * 2 });
    for (int i = 0; i < 8; i++) graph.Members.Add(new BridgeMember {
        Id = "M" + i, From = "N" + i, To = "N" + (i + 1), Length = 2
    });
    var result = Assigner.Assign(graph, JsonIO.DeserializeStock(JsonIO.Serialize(items)));
    Require(result.assignment.UnresolvedIds.Count == 0);
    Require(result.assignment.Assignments.Select(x => x.AssignedStockPieceId).Distinct().Count() == 8);
});
Console.WriteLine($"{passed} regression checks passed.");
