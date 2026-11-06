using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;
using StockBridgeGrammar.Utils;

namespace StockBridgeGrammar.Components
{
    // Combine multiple Stock Item JSON strings into a single Stock JSON array.
    // Two ways to drive it: (1) feed a list of `Stock Item JSON` strings from
    // StockBridge Stock Item; or (2) feed parallel lists of fields.
    public class StockInventoryBuilderComponent : GH_Component
    {
        public StockInventoryBuilderComponent()
          : base(
                "StockBridge Stock Inventory Builder",
                "SBStockBuild",
                "Assemble a Stock JSON array from individual Stock Item " +
                "JSON strings OR from parallel lists of Id/Length/Width/" +
                "Height/Material/Count. All branches form one inventory; a single field value applies to every item. Wire the output into Assign Inventory.",
                "StockBridge", "0. Helpers")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-9999-9999-9999-900000000009");
        protected override Bitmap Icon => IconFactory.StockInventoryBuilder;
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter   ("Stock Item JSON", "J",  "Stock item JSON or inventory arrays, collected across ALL branches. Connect SBItem.J through Merge, or connect several sources here. Leave empty to use fields.", GH_ParamAccess.tree);
            p[0].Optional = true;
            p.AddTextParameter   ("Ids",       "Id", "Stock ids; omitted ids are generated. Duplicate ids get a suffix.", GH_ParamAccess.tree); p[1].Optional = true;
            p.AddTextParameter   ("Types",     "T",  "One shared type or one per item; default linear.", GH_ParamAccess.tree); p[2].Optional = true;
            p.AddNumberParameter ("Lengths",   "L",  "Lengths (m), required in field mode. One shared value or one per item.", GH_ParamAccess.tree); p[3].Optional = true;
            p.AddNumberParameter ("Widths",    "W",  "Widths (m). One shared value or one per item; default 0.08.", GH_ParamAccess.tree); p[4].Optional = true;
            p.AddNumberParameter ("Heights",   "H",  "Heights (m). One shared value or one per item; default 0.12.", GH_ParamAccess.tree); p[5].Optional = true;
            p.AddTextParameter   ("Materials", "M",  "One shared material or one per item; default reclaimed timber.", GH_ParamAccess.tree); p[6].Optional = true;
            p.AddIntegerParameter("Counts",    "N",  "One shared count or one per item; default 1.", GH_ParamAccess.tree); p[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Stock JSON",        "J", "Combined Stock JSON array (ready for Assign Inventory).", GH_ParamAccess.item);
            p.AddTextParameter("Stock Table",       "T", "Human-readable summary, one row per stock entry.",         GH_ParamAccess.list);
            p.AddTextParameter("Warnings",          "W", "Validation messages.",                                     GH_ParamAccess.list);
        }

        // Persisted canvases may contain the old list-access parameter settings.
        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            bool result = base.Read(reader);
            foreach (var input in Params.Input) input.Access = GH_ParamAccess.tree;
            return result;
        }

        private static List<TValue> ReadValues<TGoo, TValue>(IGH_DataAccess data, int index,
            Func<TGoo, TValue> value) where TGoo : class, IGH_Goo
        {
            GH_Structure<TGoo> tree;
            var values = new List<TValue>();
            if (!data.GetDataTree(index, out tree)) return values;
            foreach (var branch in tree.Branches)
                foreach (var item in branch)
                {
                    if (item == null || !item.IsValid)
                        throw new ArgumentException($"Input {index + 1} contains a null or invalid value.");
                    values.Add(value(item));
                }
            return values;
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Message = "";
            List<StockItem> items;
            List<string> warns;
            try
            {
                var jsons = ReadValues<GH_String, string>(DA, 0, x => x.Value);
                if (jsons.Any(x => !string.IsNullOrWhiteSpace(x)))
                {
                    items = StockInventory.FromJson(jsons);
                }
                else
                {
                    items = StockInventory.FromFields(
                        ReadValues<GH_String, string>(DA, 1, x => x.Value),
                        ReadValues<GH_String, string>(DA, 2, x => x.Value),
                        ReadValues<GH_Number, double>(DA, 3, x => x.Value),
                        ReadValues<GH_Number, double>(DA, 4, x => x.Value),
                        ReadValues<GH_Number, double>(DA, 5, x => x.Value),
                        ReadValues<GH_String, string>(DA, 6, x => x.Value),
                        ReadValues<GH_Integer, int>(DA, 7, x => x.Value));
                }
                warns = StockInventory.Normalize(items);
                if (jsons.Any(x => !string.IsNullOrWhiteSpace(x)) &&
                    Params.Input.Skip(1).Any(x => x.SourceCount > 0 || !x.VolatileData.IsEmpty))
                    warns.Add("JSON input J is in use; field inputs Id/T/L/W/H/M/N are ignored.");
            }
            catch (Exception ex)
            {
                // Do not emit a partial inventory that appears to have merged successfully.
                DA.SetDataList(2, new[] { ex.Message });
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            if (items.Count == 0)
                warns.Add("Inventory is empty. Connect SBItem.J through Merge to J, or supply lengths at L.");
            var rows = items.Select(it =>
                $"{it.Id,-8} L={it.Length:F2}m  {it.Width:F2}×{it.Height:F2}  ×{it.Count}  {it.Material}").ToList();
            DA.SetData(0, JsonIO.Serialize(items));
            DA.SetDataList(1, rows);
            DA.SetDataList(2, warns);
            Message = $"{items.Count} types / {items.Sum(x => (long)x.Count)} pieces";
            if (warns.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", warns));
        }
    }
}
