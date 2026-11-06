using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using StockBridgeGrammar.Components;
using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;

internal static class TargetPickerChecks
{
    private static void Assert(bool yes, string message)
    {
        if (!yes) throw new Exception(message);
    }

    private static TargetPickerComponent Picker(GH_Document doc, string id, string mode = null, bool wireMode = false)
    {
        doc.Enabled = true;
        var picker = new TargetPickerComponent();
        doc.AddObject(picker, false);
        var graph = new BridgeGraph();
        graph.Nodes.AddRange(new[] {
            new BridgeNode { Id = "N0", X = 0 },
            new BridgeNode { Id = "N1", X = 1 },
            new BridgeNode { Id = "N2", X = 2 },
        });
        graph.Members.Add(new BridgeMember { Id = "M0", From = "N0", To = "N1", Length = 1 });
        graph.Members.Add(new BridgeMember { Id = "M1", From = "N1", To = "N2", Length = 1 });
        ((Param_String)picker.Params.Input[0]).PersistentData.Append(new GH_String(JsonIO.Serialize(graph)));
        ((Param_String)picker.Params.Input[3]).PersistentData.Clear();
        ((Param_String)picker.Params.Input[3]).PersistentData.Append(new GH_String(id));
        if (mode != null && wireMode)
        {
            var source = new Param_String();
            source.PersistentData.Append(new GH_String(mode));
            doc.AddObject(source, false);
            picker.Params.Input[2].AddSource(source);
        }
        else if (mode != null)
        {
            var input = (Param_String)picker.Params.Input[2];
            input.PersistentData.Clear();
            input.PersistentData.Append(new GH_String(mode));
        }
        return picker;
    }

    private static string Output(TargetPickerComponent picker, int index) =>
        picker.Params.Output[index].VolatileData.AllData(true).Single().ToString();

    private static void WithCurve(TargetPickerComponent picker) =>
        ((Param_Curve)picker.Params.Input[7]).PersistentData.Append(new GH_Curve(
            new LineCurve(new Point3d(1, 0, 0), new Point3d(2, 0, 0))));

    private static bool NativeCurveAvailable()
    {
        try
        {
            using (var curve = new LineCurve(new Point3d(1, 0, 0), new Point3d(2, 0, 0)))
            {
                if (curve.IsValid) return true;
                Console.WriteLine("SKIP native curve integration: RhinoCore returned an invalid LineCurve in this headless process");
                return false;
            }
        }
        catch (System.Runtime.InteropServices.SEHException)
        {
            Console.WriteLine("SKIP native curve integration: RhinoCore cannot construct LineCurve in this headless process");
            return false;
        }
    }

    private static void WithPoint(TargetPickerComponent picker) =>
        ((Param_Point)picker.Params.Input[8]).PersistentData.Append(new GH_Point(new Point3d(2.05, 0, 0)));

    public static void Run()
    {
        bool nativeCurve = NativeCurveAvailable();
        foreach (var test in new[] {
            new { Id = "M1", Mode = (string)null, Wire = false, Target = "M1", Type = "member" },
            new { Id = " N1 \r\n", Mode = (string)null, Wire = false, Target = "N1", Type = "node" },
            new { Id = " M1 ", Mode = "by_member_id", Wire = true, Target = "M1", Type = "member" },
            new { Id = "N2", Mode = "by_node_id", Wire = true, Target = "N2", Type = "node" },
            new { Id = "N0", Mode = "by_node_id", Wire = false, Target = "N0", Type = "node" },
        })
        using (var doc = new GH_Document())
        {
            var picker = Picker(doc, test.Id, test.Mode, test.Wire);
            doc.NewSolution(true);
            Assert(Output(picker, 0) == test.Target, "ID selection returned wrong target");
            Assert(Output(picker, 1) == test.Type, "ID selection returned wrong type");
            Assert(picker.Message == test.Target, "Selected ID not shown on component");
            Console.WriteLine("PASS target ID: " + test.Target + (test.Mode == null ? " automatic" : " explicit"));
        }

        foreach (var test in new[] {
            new { Name = "none", Id = "", Curve = false, Point = false, Target = "M0", Type = "member" },
            new { Name = "ID only", Id = "M0", Curve = false, Point = false, Target = "M0", Type = "member" },
            new { Name = "curve only", Id = "", Curve = true, Point = false, Target = "M1", Type = "member" },
            new { Name = "point only", Id = "", Curve = false, Point = true, Target = "N2", Type = "node" },
            new { Name = "ID and curve", Id = "M0", Curve = true, Point = false, Target = "M0", Type = "member" },
            new { Name = "ID and point", Id = "M0", Curve = false, Point = true, Target = "M0", Type = "member" },
            new { Name = "curve and point", Id = "", Curve = true, Point = true, Target = "M1", Type = "member" },
            new { Name = "all three", Id = "M0", Curve = true, Point = true, Target = "M0", Type = "member" },
        })
        using (var doc = new GH_Document())
        {
            if (test.Curve && !nativeCurve)
            {
                string resolved = TargetPickerComponent.ResolveAutomaticMode("by_mismatch_rank", false,
                    test.Id.Length > 0, true, test.Point);
                string expectedMode = test.Id.Length > 0 ? "auto_id" : "by_curve";
                Assert(resolved == expectedMode, "Curve priority failed: " + test.Name);
                Console.WriteLine("PASS default Picker mode priority: " + test.Name);
                continue;
            }
            var picker = Picker(doc, test.Id);
            if (test.Curve) WithCurve(picker);
            if (test.Point) WithPoint(picker);
            doc.NewSolution(true);
            Assert(Output(picker, 0) == test.Target && Output(picker, 1) == test.Type,
                test.Name + " selected " + Output(picker, 0) + " / " + Output(picker, 1));
            if (test.Point)
            {
                var forwarded = ((GH_Point)picker.Params.Output[5].VolatileData.AllData(true).Single()).Value;
                Assert(forwarded.DistanceTo(new Point3d(2.05, 0, 0)) < 1e-9, "Point coordinate was lost");
            }
            Console.WriteLine("PASS default Picker priority: " + test.Name);
        }

        using (var doc = new GH_Document())
        {
            var picker = Picker(doc, "NO_SUCH_ID");
            if (nativeCurve) WithCurve(picker);
            WithPoint(picker);
            doc.NewSolution(true);
            Assert(Output(picker, 0) == "", "Unknown ID silently fell back to curve/point");
            Assert(picker.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count > 0, "Missing ID must be visible as a warning");
            Console.WriteLine("PASS target ID: unknown ID refuses without fallback");
        }

        using (var doc = new GH_Document())
        {
            var picker = Picker(doc, "M1", "by_mismatch_rank", true);
            if (nativeCurve) WithCurve(picker);
            WithPoint(picker);
            doc.NewSolution(true);
            Assert(Output(picker, 0) == "M0", "Explicit mismatch mode was overridden by ID");
            Assert(picker.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(x => x.Contains("ignored")),
                "Ignored ID must produce a visible warning");
            Console.WriteLine("PASS target ID: explicit mismatch mode warns that ID is ignored");
        }

        using (var doc = new GH_Document())
        {
            var picker = Picker(doc, "", "by_point", true);
            WithPoint(picker);
            doc.NewSolution(true);
            Assert(Output(picker, 0) == "N2" && Output(picker, 1) == "node", "Point did not pick nearest existing node");
            Assert(picker.Params.Output[5].VolatileData.DataCount == 1, "Original point coordinate not forwarded at Tp");
            Console.WriteLine("PASS target ID: point selection returns nearest node and forwards coordinate");
        }

        foreach (var test in new[] {
            new { Id = "M0", Curve = false, Point = false, Target = "M0" },
            new { Id = "", Curve = true, Point = false, Target = "M1" },
            new { Id = "", Curve = false, Point = true, Target = "N2" },
        })
        using (var doc = new GH_Document())
        {
            if (test.Curve && !nativeCurve)
            {
                Assert(TargetPickerComponent.ResolveAutomaticMode("by_mismatch_rank", false,
                    test.Id.Length > 0, true, false) == "by_curve", "Invalid mismatch should not block curve selection");
                continue;
            }
            var picker = Picker(doc, test.Id);
            if (test.Curve) WithCurve(picker);
            if (test.Point) WithPoint(picker);
            var mismatches = (Param_String)picker.Params.Input[1];
            mismatches.PersistentData.Clear();
            mismatches.PersistentData.Append(new GH_String("{not valid JSON"));
            doc.NewSolution(true);
            Assert(Output(picker, 0) == test.Target, "Unused mismatch JSON blocked a direct target");
        }
        Console.WriteLine("PASS target priority: unused invalid mismatch data does not block ID, curve or point");

        if (nativeCurve) using (var doc = new GH_Document())
        {
            var picker = Picker(doc, "M0", "by_curve", true);
            WithCurve(picker);
            WithPoint(picker);
            doc.NewSolution(true);
            Assert(Output(picker, 0) == "M1", "Explicit by_curve did not override ID");
        }
        using (var doc = new GH_Document())
        {
            var picker = Picker(doc, "M0", "by_point", true);
            if (nativeCurve) WithCurve(picker);
            WithPoint(picker);
            doc.NewSolution(true);
            Assert(Output(picker, 0) == "N2", "Explicit by_point did not override ID/curve");
        }
        Assert(TargetPickerComponent.ResolveAutomaticMode("by_curve", true, true, true, true) == "by_curve", "Explicit curve override lost");
        Assert(TargetPickerComponent.ResolveAutomaticMode("by_point", true, true, true, true) == "by_point", "Explicit point override lost");
        Assert(TargetPickerComponent.ResolveAutomaticMode("by_mismatch_rank", true, true, true, true) == "by_mismatch_rank", "Explicit rank override lost");
        Console.WriteLine(nativeCurve
            ? "PASS target priority: explicitly wired curve and point modes override automatic priority"
            : "PASS target priority: explicit mode resolver and wired point mode override automatic priority");

        // Verify the existing saved component still has the same input/output slots.
        var chunk = new GH_IO.Serialization.GH_LooseChunk("old-picker");
        chunk.Deserialize_Xml(System.IO.File.ReadAllText(System.IO.Path.Combine(
            AppContext.BaseDirectory, "fixtures", "target-picker-legacy.xml")));
        var upgraded = new TargetPickerComponent();
        Assert(upgraded.Read(chunk), "Could not load prior Picker");
        Assert(upgraded.Params.Input.Count == 9 && upgraded.Params.Output.Count == 6, "Picker port indices changed");
        Console.WriteLine("PASS target ID: saved Picker preserves all port indices");
    }
}
