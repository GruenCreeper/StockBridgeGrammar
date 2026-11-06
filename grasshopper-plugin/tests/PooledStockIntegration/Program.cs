using System;
using System.Linq;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using StockBridgeGrammar.Components;
using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;

internal static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string path);

    [STAThread]
    private static void Main(string[] args)
    {
        SetDllDirectory(args.Length > 0 ? args[0] : System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rhino 8", "System"));
        try
        {
            using (var rhino = new Rhino.Runtime.InProcess.RhinoCore(
                new[] { "/nosplash" }, Rhino.Runtime.InProcess.WindowStyle.NoWindow))
            using (var doc = Rhino.RhinoDoc.CreateHeadless(null))
                Run();
        }
        catch (Exception ex)
        {
            for (var error = ex; error != null; error = error.InnerException)
                Console.Error.WriteLine(error.GetType().FullName + ": " + error.Message + "\n" + error.StackTrace);
            Environment.ExitCode = 1;
        }
    }

    private const string Glulam = "{\"id\":\"g\",\"length\":4.5,\"width\":0.1,\"height\":0.15,\"material\":\"Glulam\",\"count\":12}";
    private const string Reclaimed = "{\"id\":\"r\",\"length\":3.5,\"width\":0.075,\"height\":0.15,\"material\":\"reclaimed timber\",\"count\":27}";

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static string Graph()
    {
        var graph = new BridgeGraph();
        var lengths = new[] { 4.62, 4.46, 0.98, 7.42, 4.46, 7.42 };
        double x = 0;
        graph.Nodes.Add(new BridgeNode { Id = "N0" });
        for (int i = 0; i < lengths.Length; i++)
        {
            x += lengths[i];
            graph.Nodes.Add(new BridgeNode { Id = "N" + (i + 1), X = x });
            graph.Members.Add(new BridgeMember { Id = "M" + i, From = "N" + i, To = "N" + (i + 1), Length = lengths[i] });
        }
        return JsonIO.Serialize(graph);
    }

    private static Param_String Source(GH_Document doc, string[] values, bool branches)
    {
        var source = new Param_String();
        for (int i = 0; i < values.Length; i++)
            source.PersistentData.Append(new GH_String(values[i]), branches ? new GH_Path(0, 0, i) : new GH_Path(0));
        doc.AddObject(source, false);
        return source;
    }

    private static void Run()
    {
        GH_Document.EnableSolutions = true;
        SectionLimitChecks.Run();
        TargetPickerChecks.Run();
        foreach (bool branches in new[] { false, true })
        foreach (bool arrays in new[] { false, true })
        {
            using (var doc = new GH_Document())
            {
                doc.Enabled = true;
                var check = new SectionCheckComponent();
                doc.AddObject(check, false);
                check.Params.Input[0].AddSource(Source(doc, new[] { Graph() }, false));
                check.Params.Input[1].AddSource(Source(doc, arrays ? new[] { "[" + Glulam + "]", "[" + Reclaimed + "]" } : new[] { Glulam, Reclaimed }, branches));
                var ids = (Param_String)check.Params.Input[2];
                var forces = (Param_Number)check.Params.Input[3];
                double[] loads = { 0, -9, -1, -10, -9, -10 };
                for (int i = 0; i < 6; i++)
                {
                    ids.PersistentData.Append(new GH_String("M" + i));
                    forces.PersistentData.Append(new GH_Number(loads[i]));
                }
                doc.NewSolution(true);
                string errors = string.Join("; ", check.RuntimeMessages(GH_RuntimeMessageLevel.Error));
                Assert(errors.Length == 0, "Check errors: " + errors);
                Assert(check.Params.Output[8].VolatileData.DataCount == 1, "Expected one updated graph, got " + check.Params.Output[8].VolatileData.DataCount);
                Assert(check.Params.Output[11].VolatileData.PathCount == 1, "Expected one detail branch");
                var details = check.Params.Output[11].VolatileData.AllData(true).Select(x => x.ToString()).ToArray();
                Assert(details.Length == 6, "Expected 6 rows, got " + details.Length);
                Assert(details[1].Contains("g#") && details[2].Contains("r#") && details[4].Contains("g#"), "Mixed pool not used: " + string.Join("\n", details));
                Assert(check.Message == "2 types / 39 pieces", "Wrong pool totals: " + check.Message);
                Console.WriteLine($"PASS real GH SectionCheck: branches={branches}, arrays={arrays}, one graph / six rows / 39 pooled pieces");
                if (branches && arrays) foreach (var line in details) Console.WriteLine(line);
            }
        }

        using (var doc = new GH_Document())
        {
            doc.Enabled = true;
            var assign = new AssignInventoryComponent();
            var check = new SectionCheckComponent();
            doc.AddObject(assign, false);
            doc.AddObject(check, false);
            var stock = Source(doc, new[] { Glulam, Reclaimed }, true);
            assign.Params.Input[0].AddSource(Source(doc, new[] { Graph() }, false));
            assign.Params.Input[1].AddSource(stock);
            check.Params.Input[0].AddSource(assign.Params.Output[0]);
            check.Params.Input[1].AddSource(stock);
            doc.NewSolution(true);
            Assert(assign.Params.Output[0].VolatileData.DataCount == 1, "Assign duplicated the graph");
            Assert(check.Params.Output[8].VolatileData.DataCount == 1, "Assign -> Check duplicated the graph");
            var graph = JsonIO.DeserializeGraph(check.Params.Output[8].VolatileData.AllData(true).Single().ToString());
            Assert(graph.Members.Single(x => x.Id == "M1").AssignedStockId == "g", "Long member did not use glulam");
            Assert(graph.Members.Single(x => x.Id == "M2").AssignedStockId == "r", "Short member did not use reclaimed timber");
            Console.WriteLine("PASS real GH Assign -> Check: one graph and both materials used");
        }

        foreach (var component in new GH_Component[] { new AssignInventoryComponent(), new SectionCheckComponent(), new GenerateCandidatesComponent(), new ApplyGrammarRuleComponent() })
        {
            int index = component is ApplyGrammarRuleComponent ? 4 : 1;
            Assert(component.Params.Input[index].Access == GH_ParamAccess.tree, component.Name + " stock port must use tree access");
            component.Params.Input[index].Access = GH_ParamAccess.item;
            var chunk = new GH_IO.Serialization.GH_LooseChunk("old-component");
            Assert(component.Write(chunk), "Could not serialize old component");
            Assert(component.Read(chunk), "Could not read old component");
            Assert(component.Params.Input[index].Access == GH_ParamAccess.tree, "Saved canvas migration failed: " + component.Name);
            Console.WriteLine("PASS saved-canvas migration: " + component.Name);
        }
    }
}
