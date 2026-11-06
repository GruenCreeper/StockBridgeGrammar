using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;

// Behavioral regressions against the actual plugin core, with analytic spatial
// solutions and geometry-derived lengths rather than a second copy of its solver.
var cases = new List<object>();
var failures = new List<string>();
void Test(string name, Action action)
{
    try { action(); cases.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception e) { cases.Add(new { name, passed = false, error = e.Message }); failures.Add(name + ": " + e.Message); Console.WriteLine("FAIL " + name + ": " + e.Message); }
}

Test("target above selects exact spatial stock intersection", () => {
    var r = Solve(Graph(), Params(1, 1, 4), Stock());
    CheckNode(r, 1, 1, Math.Sqrt(2)); CheckStock(r, Stock());
});
Test("target below selects the opposite mirror", () => {
    var r = Solve(Graph(), Params(1, 1, -4), Stock());
    CheckNode(r, 1, 1, -Math.Sqrt(2)); CheckStock(r, Stock());
});
Test("target is a preference rather than a forced coordinate", () => {
    var r = Solve(Graph(), Params(50, 80, 20), Stock());
    CheckNode(r, 1, 1, Math.Sqrt(2)); CheckStock(r, Stock());
});
Test("changing target changes selected lengths when stock permits", () => {
    var g = Graph();
    var v1 = new BridgeNode { X = .6, Y = .4, Z = 1.0 };
    var v2 = new BridgeNode { X = 1.4, Y = .8, Z = 2.2 };
    var stock = g.Nodes.Select((a, i) => Piece("L" + i, Dist(a, v1), 1))
        .Concat(g.Nodes.Select((a, i) => Piece("H" + i, Dist(a, v2), 1))).ToList();
    var r1 = Solve(Graph(), Params(v1.X, v1.Y, v1.Z), stock);
    var r2 = Solve(Graph(), Params(v2.X, v2.Y, v2.Z), stock);
    CheckNode(r1, v1.X, v1.Y, v1.Z); CheckNode(r2, v2.X, v2.Y, v2.Z);
    CheckStock(r1, stock); CheckStock(r2, stock);
    Assert(!r1.Graph.Members.Select(m => m.AssignedStockId).Order().SequenceEqual(
        r2.Graph.Members.Select(m => m.AssignedStockId).Order()), "target did not change chosen stock lengths");
});
Test("all six anchor permutations give the same guided position", () => {
    foreach (int[] order in new[] { new[]{0,1,2}, new[]{0,2,1}, new[]{1,0,2}, new[]{1,2,0}, new[]{2,0,1}, new[]{2,1,0} }) {
        var p = Params(1, 1, 3); p["points"] = Points(order.Select(i => Graph().Nodes[i]));
        CheckNode(Solve(Graph(), p, Stock()), 1, 1, Math.Sqrt(2));
    }
});
Test("planar target gives a deterministic off-plane tie solution", () => {
    BridgeNode first = null;
    foreach (int[] order in new[] { new[]{0,1,2}, new[]{0,2,1}, new[]{2,1,0} }) {
        var p = Params(1,1,0); p["points"] = Points(order.Select(i => Graph().Nodes[i]));
        var r = Solve(Graph(), p, Stock()); var n = NewNode(r);
        Assert(Math.Abs(n.Z) >= .01, "coplanar solution");
        if (first != null) Assert(Dist(first,n) < 1e-8, "tie depends on anchor order"); first = n;
    }
});
Test("explicit anchors work without a target ID", () => CheckNode(Solve(Graph(), Params(1,1,3), Stock(), ""), 1,1,Math.Sqrt(2)));
Test("three node IDs work without Position", () => {
    var p = Params(1,1,3); p.Remove("points"); CheckNode(Solve(Graph(),p,Stock(),"A,B,C"),1,1,Math.Sqrt(2));
});
Test("corners node IDs work without Position or target ID", () => {
    var p = Params(1,1,3); p.Remove("points"); p["corners"] = new JArray("A","B","C");
    CheckNode(Solve(Graph(),p,Stock(),""),1,1,Math.Sqrt(2));
});
Test("member-region compatibility uses the spatial solver", () => {
    var g = Triangle(); var p = Params(1,1,3); p.Remove("points");
    CheckNode(Solve(g,p,Stock(),"AB"),1,1,Math.Sqrt(2));
});
Test("target-only mode infers three nearby anchors", () => {
    var p = Params(1,1,1); p.Remove("points");
    CheckNode(Solve(Graph(),p,Stock(),""),1,1,Math.Sqrt(2));
});
Test("legacy at remains a location hint with explicit anchors", () => {
    var p = Params(1,1,-3); p["at"] = p["targetPoint"].DeepClone(); p.Remove("targetPoint");
    CheckNode(Solve(Graph(),p,Stock()),1,1,-Math.Sqrt(2));
});
foreach (int count in new[]{0,1,2}) {
    int captured = count;
    Test("count " + count + " cannot create three stock members", () => RefusedUnchanged(Graph(),Params(1,1,3),Stock(captured)));
}
Test("empty stock refuses cleanly", () => RefusedUnchanged(Graph(),Params(1,1,3),new()));
Test("duplicate Position points refuse cleanly", () => {
    var p = Params(1,1,3); p["points"] = JArray.Parse("[[0,0,0],[2,0,0],[0,0,0]]");
    RefusedUnchanged(Graph(),p,Stock());
});
Test("distinct points snapped to one anchor refuse cleanly", () => {
    var p = Params(1,1,3); p["points"] = JArray.Parse("[[0,0,0],[0.1,0,0],[0,2,0]]");
    RefusedUnchanged(Graph(),p,Stock());
});
foreach (string points in new[]{"[[0,0,0],[2,0,0]]", "[[0,0,0],[2,0,0],[0,2,0],[10,10,10]]"}) {
    string captured = points;
    Test("incorrect anchor count refuses without orphan nodes " + JArray.Parse(points).Count, () => {
        var p=Params(1,1,3); p["points"]=JArray.Parse(captured); RefusedUnchanged(new BridgeGraph(),p,Stock());
    });
}
Test("collinear anchors are rejected", () => {
    var g=Graph(); g.Nodes[1].X=1; g.Nodes[2].X=2; g.Nodes[2].Y=0;
    var p=Params(1,1,3); p["points"]=Points(g.Nodes); RefusedUnchanged(g,p,Stock());
});
Test("purely planar sphere intersection is rejected", () => RefusedUnchanged(Graph(),Params(1,1,3),new(){Piece("P",Math.Sqrt(2),3)}));
Test("near-plane intersection below minHeight is rejected", () => RefusedUnchanged(Graph(),Params(1,1,3),new(){Piece("P",Math.Sqrt(2+1e-6),3)}));
Test("minHeight is configurable and still nonzero", () => {
    var p=Params(1,1,3); p["minHeight"]=.0001;
    CheckNode(Solve(Graph(),p,new(){Piece("P",Math.Sqrt(2+1e-6),3)}),1,1,.001);
});
Test("degree two cannot bypass spatial bracing", () => {
    var p=Params(1,1,3); p["degree"]=2; RefusedUnchanged(Graph(),p,Stock());
});
Test("no sphere intersection refuses rather than trimming", () => RefusedUnchanged(Graph(),Params(1,1,3),new(){Piece("short",.5,3)}));
Test("isolated anchors are committed only after successful solving", () => {
    var g=new BridgeGraph(); var r=Solve(g,Params(1,1,3),Stock());
    Assert(r.Graph.Nodes.Count==4,"expected 3 anchors and 1 new node");
    Assert(r.Graph.Nodes.Count(n=>n.IsSupport==true)==3,"isolated anchor support flags lost");
    Assert(r.Graph.Nodes.All(n=>r.Graph.Members.Any(m=>m.From==n.Id || m.To==n.Id)),"orphan node committed");
    CheckStock(r,Stock());
});
Test("failed solving does not create pending support anchors", () => RefusedUnchanged(new BridgeGraph(),Params(1,1,3),Stock(2)));
Test("collision on preferred side chooses other valid mirror", () => {
    var g=Graph(); g.Nodes.Add(new(){Id="occupied",X=1,Y=1,Z=Math.Sqrt(2)});
    CheckNode(Solve(g,Params(1,1,3),Stock()),1,1,-Math.Sqrt(2));
});
Test("collisions at both stock intersections refuse cleanly", () => {
    var g=Graph(); g.Nodes.Add(new(){Id="up",X=1,Y=1,Z=Math.Sqrt(2)}); g.Nodes.Add(new(){Id="down",X=1,Y=1,Z=-Math.Sqrt(2)});
    RefusedUnchanged(g,Params(1,1,3),Stock());
});
Test("known physical allocation is reserved", () => {
    var g=Graph(); g.Members.Add(new(){Id="existing",From="A",To="B",Length=2,AssignedStockId="S2",AssignedStockPieceId="S2#1"});
    var r=Solve(g,Params(1,1,3),Stock(4)); CheckNode(r,1,1,Math.Sqrt(2)); CheckStock(r,Stock(4));
    Assert(NewMembers(r).All(m=>m.AssignedStockPieceId!="S2#1"),"reserved piece reused");
});
Test("reserved physical stock cannot be spent twice", () => {
    var g=Graph(); g.Members.Add(new(){Id="existing",From="A",To="B",Length=2,AssignedStockId="S2",AssignedStockPieceId="S2#1"});
    RefusedUnchanged(g,Params(1,1,3),Stock(3));
});
Test("legacy type-only assignment reserves one piece", () => {
    var g=Graph(); g.Members.Add(new(){Id="existing",From="A",To="B",Length=2,AssignedStockId="S2"});
    RefusedUnchanged(g,Params(1,1,3),Stock(3));
});
Test("duplicate recorded physical allocation is rejected", () => {
    var g=Graph(); foreach(string id in new[]{"old1","old2"}) g.Members.Add(new(){Id=id,From="A",To="B",Length=2,AssignedStockId="S2",AssignedStockPieceId="S2#1"});
    RefusedUnchanged(g,Params(1,1,3),Stock(8));
});
Test("second addition reserves the first addition's pieces", () => {
    var r1=Solve(Graph(),Params(1,1,3),Stock(6));
    var r2=Solve(r1.Graph,Params(1,1,-3),Stock(6)); CheckNode(r2,1,1,-Math.Sqrt(2));
    Assert(r2.Graph.Members.Select(m=>m.AssignedStockPieceId).Distinct().Count()==6,"repeated physical piece");
});
Test("three individual unequal lengths select the intended location", () => {
    var g=Graph(); var target=new BridgeNode{X=.7,Y=.9,Z=1.7};
    var stock=g.Nodes.Select((a,i)=>Piece("S"+i,Dist(a,target),1)).ToList();
    var r=Solve(g,Params(target.X,target.Y,target.Z),stock); CheckNode(r,target.X,target.Y,target.Z); CheckStock(r,stock);
});
Test("tilted and translated planes retain full-length spatial solutions", () => {
    for(int i=0;i<24;i++) {
        double a=.13*(i+1), b=.27*(i+1); double ca=Math.Cos(a),sa=Math.Sin(a),cb=Math.Cos(b),sb=Math.Sin(b);
        BridgeNode Map(BridgeNode n) => new(){Id=n.Id,X=cb*n.X+sb*sa*n.Y+sb*ca*n.Z+17*i,Y=ca*n.Y-sa*n.Z-9*i,Z=-sb*n.X+cb*sa*n.Y+cb*ca*n.Z+4*i};
        var g=Graph(); g.Nodes=g.Nodes.Select(Map).ToList();
        var v=Map(new(){X=.4+.01*i,Y=.8,Z=1.2+.03*i});
        var p=new JObject{["points"]=Points(g.Nodes),["targetPoint"]=new JArray(v.X,v.Y,v.Z)};
        var stock=g.Nodes.Select((n,k)=>Piece("R"+k,Dist(n,v),1)).ToList();
        var r=Solve(g,p,stock); CheckNode(r,v.X,v.Y,v.Z); CheckStock(r,stock);
    }
});
Test("stock count does not cause cubic enumeration of physical pieces", () => {
    var watch=System.Diagnostics.Stopwatch.StartNew();
    var r=Solve(Graph(),Params(1,1,3),Stock(100000)); CheckNode(r,1,1,Math.Sqrt(2));
    Assert(watch.Elapsed.TotalSeconds<5,"100000 identical pieces took more than five seconds");
});
Test("invalid numeric target rejects without a partial mutation", () => {
    var p=Params(1,1,3); p["targetPoint"]=new JArray(double.NaN,1,3); RefusedUnchanged(Graph(),p,Stock());
});

// Candidate controls are independently observable through successful rule history.
Test("zero candidate iterations leaves every geometry unchanged", () => {
    var g=LongBar(); var candidates=CandidateGenerator.Generate(g,Stock(50),new(){MaxIterations=0,DesignDrama=1,GrammarIntensity=1,CandidateCount=5});
    Assert(candidates.All(c=>c.History.Count==0),"zero iterations applied a rule");
    Assert(candidates.All(c=>c.Graph.Nodes.Count==g.Nodes.Count && c.Graph.Members.Count==g.Members.Count),"zero iterations changed geometry");
});
Test("zero candidate strengths applies no rules", () => {
    var c=CandidateGenerator.Generate(LongBar(),Stock(50),new(){MaxIterations=8,DesignDrama=0,GrammarIntensity=0});
    Assert(c.All(x=>x.History.Count==0),"zero strengths changed geometry");
});
Test("candidate family switch excludes Lean Reuse", () => {
    var c=CandidateGenerator.Generate(LongBar(),Stock(50),new(){AllowAsymmetry=false,CandidateCount=5,MaxIterations=0});
    Assert(c.All(x=>x.FamilyName!="Lean Reuse"),"disabled family still generated");
});
Test("candidate iterations are bounded even for invalid settings", () => {
    var negative=CandidateGenerator.Generate(LongBar(),Stock(50),new(){MaxIterations=-2,DesignDrama=1,GrammarIntensity=1});
    Assert(negative.All(x=>x.History.Count==0),"negative iteration count changed graph");
    var oversized=CandidateGenerator.Generate(LongBar(),Stock(50),new(){MaxIterations=100,DesignDrama=1,GrammarIntensity=1});
    Assert(oversized.All(x=>x.History.Count<=8),"iteration upper bound ignored");
});
Test("candidate stock fit metric equals independent assignment", () => {
    var stock=Stock(50); var candidates=CandidateGenerator.Generate(LongBar(),stock,new(){MaxIterations=2,DesignDrama=1,GrammarIntensity=1});
    foreach(var candidate in candidates) {
        var copy=JsonIO.DeserializeGraph(JsonIO.Serialize(candidate.Graph));
        var assigned=Assigner.Assign(copy,stock);
        Assert(Math.Abs(candidate.Metrics.StockFitRate-assigned.assignment.StockFitRate)<1e-10,"stock fit uses a different metric");
    }
});
Test("same-length pieces from different types remain unique", () => {
    var stock=new List<StockItem>{Piece("Z",2,1),Piece("A",2,1),Piece("Q",2,1)};
    var r=Solve(Graph(),Params(1,1,3),stock); CheckNode(r,1,1,Math.Sqrt(2)); CheckStock(r,stock);
    Assert(NewMembers(r).Select(m=>m.AssignedStockId).Distinct().Count()==3,"one-count stock type reused");
});
Test("small spatial height on a large span is not classified as planar", () => {
    var g=Graph(); g.Nodes[1].X=100; g.Nodes[2].Y=100;
    var v=new BridgeNode{X=50,Y=50,Z=.02};
    var p=new JObject{["points"]=Points(g.Nodes),["targetPoint"]=new JArray(v.X,v.Y,v.Z)};
    var r=Solve(g,p,new(){Piece("L",Dist(g.Nodes[0],v),3)});
    CheckNode(r,v.X,v.Y,v.Z); Assert(r.Step.Note.Contains("3-D"),"spatial result incorrectly labelled 2-D");
});
Test("published JSON examples reproduce both spatial mirror solutions", () => {
    string example=Path.Combine(AppContext.BaseDirectory,"examples","add_node_spatial");
    var stock=JsonIO.DeserializeStock(File.ReadAllText(Path.Combine(example,"stock.json")));
    foreach(var entry in new[]{("params_above.json",1), ("params_below.json",-1)}) {
        var graph=JsonIO.DeserializeGraph(File.ReadAllText(Path.Combine(example,"graph.json")));
        var parameters=JObject.Parse(File.ReadAllText(Path.Combine(example,entry.Item1)));
        var result=Solve(graph,parameters,stock); CheckNode(result,1,1,entry.Item2*Math.Sqrt(2)); CheckStock(result,stock);
    }
});

var report=new { suite="StockBridgeGrammar 1.0.0 spatial regression", runtime=Environment.Version.ToString(), passed=cases.Count-failures.Count, failed=failures.Count, cases };
File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"results.json"),JsonConvert.SerializeObject(report,Formatting.Indented)+Environment.NewLine);
Console.WriteLine($"{cases.Count-failures.Count}/{cases.Count} passed");
if(failures.Count>0) { foreach(var failure in failures) Console.Error.WriteLine(failure); Environment.ExitCode=1; }

static void Assert(bool yes,string message) { if(!yes) throw new Exception(message); }
static double Dist(BridgeNode a,BridgeNode b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(a.Z-b.Z,2));
static BridgeGraph Graph()=>new(){Nodes=new(){new(){Id="A",X=0,Y=0,Z=0},new(){Id="B",X=2,Y=0,Z=0},new(){Id="C",X=0,Y=2,Z=0}}};
static BridgeGraph Triangle() { var g=Graph(); g.Members=new(){new(){Id="AB",From="A",To="B",Length=2},new(){Id="BC",From="B",To="C",Length=Math.Sqrt(8)},new(){Id="CA",From="C",To="A",Length=2}}; return g; }
static BridgeGraph LongBar()=>new(){Nodes=new(){new(){Id="A"},new(){Id="B",X=6}},Members=new(){new(){Id="AB",From="A",To="B",Length=6}}};
static StockItem Piece(string id,double length,int count)=>new(){Id=id,Length=length,Count=count,Width=.1,Height=.1};
static List<StockItem> Stock(int count=3)=>new(){Piece("S2",2,count)};
static JArray Points(IEnumerable<BridgeNode> nodes)=>new(nodes.Select(n=>new JArray(n.X,n.Y,n.Z)));
static JObject Params(double x,double y,double z)=>new(){["points"]=Points(Graph().Nodes),["targetPoint"]=new JArray(x,y,z)};
static GrammarRuleResult Solve(BridgeGraph g,JObject p,List<StockItem> stock,string target="") {
    var r=GrammarRules.Apply(g,"addNodeToStock",target,p,stock);
    Assert(r?.Graph!=null,"rule refused: "+string.Join(" | ",r?.Warnings??new())); return r;
}
static BridgeNode NewNode(GrammarRuleResult r) {
    var ids=NewMembers(r).SelectMany(m=>new[]{m.From,m.To}).GroupBy(x=>x).OrderByDescending(x=>x.Count()).ToList();
    Assert(ids.Count>0 && ids[0].Count()==3,"new three-brace hub missing"); return r.Graph.Nodes.Single(n=>n.Id==ids[0].Key);
}
static List<BridgeMember> NewMembers(GrammarRuleResult r)=>r.Graph.Members.Where(m=>r.Step.AddedMemberIds.Contains(m.Id)).ToList();
static void CheckNode(GrammarRuleResult r,double x,double y,double z) {
    var n=NewNode(r); Assert(Dist(n,new(){X=x,Y=y,Z=z})<1e-7,$"expected ({x},{y},{z}), got ({n.X},{n.Y},{n.Z})");
}
static void CheckStock(GrammarRuleResult r,List<StockItem> stock) {
    var added=NewMembers(r); Assert(added.Count==3,"expected exactly three new members");
    Assert(added.All(m=>!string.IsNullOrWhiteSpace(m.AssignedStockPieceId)),"missing physical piece assignment");
    Assert(added.Select(m=>m.AssignedStockPieceId).Distinct().Count()==3,"new braces reuse a physical piece");
    foreach(var m in added) {
        var length=Dist(r.Graph.Nodes.Single(n=>n.Id==m.From),r.Graph.Nodes.Single(n=>n.Id==m.To));
        var s=stock.Single(s=>s.Id==m.AssignedStockId);
        Assert(Math.Abs(length-s.Length)<1e-7,"geometry does not equal stock original length");
        Assert(Math.Abs(m.Length-length)<1e-7,"stored length differs from geometry");
    }
}
static void RefusedUnchanged(BridgeGraph g,JObject p,List<StockItem> stock) {
    string before=JsonConvert.SerializeObject(g); var r=GrammarRules.Apply(g,"addNodeToStock","",p,stock);
    Assert(r?.Graph==null,"expected refusal"); Assert(r!=null && r.Warnings.Count>0,"missing refusal explanation");
    Assert(JsonConvert.SerializeObject(g)==before,"failed rule mutated the input graph");
}
