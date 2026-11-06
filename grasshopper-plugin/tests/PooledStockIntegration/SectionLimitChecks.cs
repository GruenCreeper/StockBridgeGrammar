using System;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using StockBridgeGrammar.Components;
using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;

internal static class SectionLimitChecks
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void SetNumber(SectionCheckComponent check, int index, double value)
    {
        var parameter = (Param_Number)check.Params.Input[index];
        parameter.PersistentData.Clear();
        parameter.PersistentData.Append(new GH_Number(value));
    }

    private static SectionCheckComponent Setup(GH_Document doc, double stress = 0.8, double buckling = 0.4, bool tension = false)
    {
        doc.Enabled = true;
        var check = new SectionCheckComponent();
        doc.AddObject(check, false);
        var graph = new BridgeGraph();
        graph.Nodes.Add(new BridgeNode { Id = "A" });
        graph.Nodes.Add(new BridgeNode { Id = "B", X = 1 });
        graph.Members.Add(new BridgeMember { Id = "AB", From = "A", To = "B", Length = 1 });
        double force = stress * 0.01 * 1000;
        var stock = new StockItem { Id = "S", Length = 1, Width = 0.1, Height = 0.1, Count = 1,
            AllowableStress = 1000, ElasticModulus = force / (Math.PI * Math.PI * (0.1 * Math.Pow(0.1, 3) / 12) * buckling) };
        ((Param_String)check.Params.Input[0]).PersistentData.Append(new GH_String(JsonIO.Serialize(graph)));
        ((Param_String)check.Params.Input[1]).PersistentData.Append(new GH_String(JsonIO.Serialize(stock)));
        ((Param_String)check.Params.Input[2]).PersistentData.Append(new GH_String("AB"));
        SetNumber(check, 3, tension ? force : -force);
        return check;
    }

    private static string Output(SectionCheckComponent check, int index) =>
        check.Params.Output[index].VolatileData.AllData(true).Single().ToString();

    public static void Run()
    {
        foreach (var test in new[] {
            new { Name = "default limits", SL = 1.0, BL = 1.0, Expected = "ok" },
            new { Name = "stress independently triggers", SL = 0.7, BL = 1.0, Expected = "overStressed" },
            new { Name = "buckling triggers despite larger passing raw stress", SL = 1.0, BL = 0.3, Expected = "buckling" },
            new { Name = "both exceeded, relative stress governs", SL = 0.4, BL = 0.3, Expected = "overStressed" },
            new { Name = "both exceeded, relative buckling governs", SL = 0.7, BL = 0.1, Expected = "buckling" },
            new { Name = "equality passes", SL = 0.8, BL = 0.4, Expected = "ok" },
        })
        using (var doc = new GH_Document())
        {
            var check = Setup(doc);
            SetNumber(check, 6, test.SL); SetNumber(check, 7, test.BL);
            doc.NewSolution(true);
            Assert(Output(check, 1) == test.Expected, test.Name + ": " + Output(check, 1));
            Assert(((GH_Number)check.Params.Output[2].VolatileData.AllData(true).Single()).Value == 0.8, "Stress output was normalized by limit");
            Assert(((GH_Number)check.Params.Output[3].VolatileData.AllData(true).Single()).Value == 0.4, "Buckling output was normalized by limit");
            string rule = Output(check, 9);
            Assert(rule == (test.Expected == "ok" ? "" : test.Expected == "buckling" ? "splitEdgeToPoint" : "addNodeToStock"), "Incorrect rule");
            Console.WriteLine("PASS limits: " + test.Name);
        }

        using (var doc = new GH_Document())
        {
            var check = Setup(doc, 1.2, 1.1);
            SetNumber(check, 6, 1.3); SetNumber(check, 7, 1.2);
            doc.NewSolution(true);
            Assert(Output(check, 1) == "ok", "Raised limits ignored");
            Console.WriteLine("PASS limits: thresholds above one are honored");
        }
        using (var doc = new GH_Document())
        {
            var check = Setup(doc, tension: true);
            SetNumber(check, 7, 0.01);
            doc.NewSolution(true);
            Assert(Output(check, 1) == "ok", "Tension incorrectly triggered buckling");
            Console.WriteLine("PASS limits: tension skips buckling");
        }
        foreach (int index in new[] { 6, 7 })
        foreach (double value in new[] { 0.0, -1, double.NaN, double.PositiveInfinity })
        using (var doc = new GH_Document())
        {
            var check = Setup(doc);
            SetNumber(check, index, value);
            doc.NewSolution(true);
            Assert(check.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0, "Invalid limit accepted");
            Assert(!check.Params.Output[8].VolatileData.AllData(true).Any(x => x != null && !string.IsNullOrWhiteSpace(x.ToString())),
                $"Invalid limit emitted a graph: input={index}, value={value}");
        }
        Console.WriteLine("PASS limits: zero, negative and nonfinite values rejected at both inputs");

        using (var doc = new GH_Document())
        {
            var check = Setup(doc);
            var slider = new GH_NumberSlider();
            slider.Slider.Minimum = 0.01M; slider.Slider.Maximum = 2M;
            slider.Slider.DecimalPlaces = 2;
            slider.SetSliderValue(1M);
            doc.AddObject(slider, false);
            check.Params.Input[6].AddSource(slider);
            doc.NewSolution(true);
            Assert(Output(check, 1) == "ok", "Slider default failed");
            slider.SetSliderValue(0.7M);
            slider.ExpireSolution(false);
            doc.NewSolution(false);
            Assert(Output(check, 1) == "overStressed", "Live slider update did not trigger");
            slider.SetSliderValue(1M);
            slider.ExpireSolution(false);
            doc.NewSolution(false);
            Assert(Output(check, 1) == "ok", "Live slider update did not clear failure");
            Console.WriteLine("PASS limits: real slider updates and resets status");
        }

        using (var doc = new GH_Document())
        {
            var check = Setup(doc);
            var stockParameter = (Param_String)check.Params.Input[1];
            var original = JsonIO.DeserializeStock(stockParameter.PersistentData.AllData(true).Single().ToString()).Single();
            var stronger = new StockItem { Id = "T", Length = original.Length, Width = original.Width,
                Height = original.Height, Count = 1, AllowableStress = 2000, ElasticModulus = original.ElasticModulus };
            stockParameter.PersistentData.Append(new GH_String(JsonIO.Serialize(stronger)));
            var graphParameter = (Param_String)check.Params.Input[0];
            var graph = JsonIO.DeserializeGraph(graphParameter.PersistentData.AllData(true).Single().ToString());
            graph.Members[0].AssignedStockId = "S";
            graph.Members[0].AssignedStockPieceId = "S#1";
            graphParameter.PersistentData.Clear();
            graphParameter.PersistentData.Append(new GH_String(JsonIO.Serialize(graph)));
            SetNumber(check, 6, 0.6);
            doc.NewSolution(true);
            Assert(Output(check, 1) == "ok" && Output(check, 7) == "T#1", "New threshold did not switch to available passing stock");
            Console.WriteLine("PASS limits: tighter threshold reassigns an existing piece to passing stock");
        }

        // This fixture was serialized from the original six-input component,
        // with Sig set to 12345, using Grasshopper's component writer.
        var chunk = new GH_IO.Serialization.GH_LooseChunk("six-port-check");
        chunk.Deserialize_Xml(System.IO.File.ReadAllText(System.IO.Path.Combine(
            AppContext.BaseDirectory, "fixtures", "section-check-six-inputs.xml")));
        var upgraded = new SectionCheckComponent();
        Assert(upgraded.Read(chunk), "Could not migrate baseline");
        Assert(upgraded.Params.Input.Count == 8, "Missing appended limit inputs");
        Assert(((GH_Number)((Param_Number)upgraded.Params.Input[4]).PersistentData.AllData(true).Single()).Value == 12345, "Existing Sig lost");
        foreach (int index in new[] { 6, 7 })
            Assert(((GH_Number)((Param_Number)upgraded.Params.Input[index]).PersistentData.AllData(true).Single()).Value == 1, "Migrated limit is not 1");
        Console.WriteLine("PASS limits: actual six-port saved component keeps Sig and gains defaults");
    }
}
