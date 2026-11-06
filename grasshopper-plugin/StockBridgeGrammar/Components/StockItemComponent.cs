using System;
using System.Drawing;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;
using StockBridgeGrammar.Utils;

namespace StockBridgeGrammar.Components
{
    // Build ONE stock item from typed inputs (no JSON writing required).
    // Feed multiple of these into StockBridge Stock Inventory Builder.
    public class StockItemComponent : GH_Component
    {
        public StockItemComponent()
          : base(
                "StockBridge Stock Item",
                "SBItem",
                "Build a single stock item from typed inputs. Wire several " +
                "of these into Stock Inventory Builder to assemble a Stock JSON.",
                "StockBridge", "0. Helpers")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-8888-8888-8888-800000000008");
        protected override Bitmap Icon => IconFactory.StockItem;
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter  ("Id",       "Id",  "Unique stock id (e.g. S1).",           GH_ParamAccess.item, "S1");
            p.AddTextParameter  ("Type",     "T",   "linear (default).",                    GH_ParamAccess.item, "linear");
            p.AddNumberParameter("Length",   "L",   "Per-piece length in metres.",          GH_ParamAccess.item, 1.8);
            p.AddNumberParameter("Width",    "W",   "Section width in metres.",             GH_ParamAccess.item, 0.08);
            p.AddNumberParameter("Height",   "H",   "Section height in metres.",            GH_ParamAccess.item, 0.12);
            p.AddTextParameter  ("Material", "M",   "Descriptive material label.",          GH_ParamAccess.item, "reclaimed timber");
            p.AddIntegerParameter("Count",   "N",   "How many pieces of this size.",        GH_ParamAccess.item, 4);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Stock Item JSON", "J", "One stock entry as JSON.", GH_ParamAccess.item);
            p.AddTextParameter("Description",     "D", "Human-readable description.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string id = "S1", type = "linear", material = "reclaimed timber";
            double length = 1.8, width = 0.08, height = 0.12;
            int count = 4;
            DA.GetData(0, ref id);
            DA.GetData(1, ref type);
            DA.GetData(2, ref length);
            DA.GetData(3, ref width);
            DA.GetData(4, ref height);
            DA.GetData(5, ref material);
            DA.GetData(6, ref count);

            if (string.IsNullOrWhiteSpace(id))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Id is empty.");
            if (count < 1)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Count < 1 — this stock will not be used.");

            var item = new StockItem
            {
                Id = id, Type = type, Length = length, Width = width,
                Height = height, Material = material, Count = count,
            };
            try { StockInventory.ValidateItem(item); }
            catch (ArgumentException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }
            DA.SetData(0, JsonConvert.SerializeObject(item, Formatting.Indented));
            DA.SetData(1, $"{id}: {length:F2}m × {width:F2}×{height:F2}, ×{count}, {material}");
        }
    }
}
