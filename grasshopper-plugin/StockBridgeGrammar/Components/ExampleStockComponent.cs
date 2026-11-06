using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using StockBridgeGrammar.Models;
using StockBridgeGrammar.Utils;

namespace StockBridgeGrammar.Components
{
    // Helper — no need to hand-write Stock JSON. Pick a preset and wire the
    // output into Assign Inventory / Apply Grammar Rule.
    public class ExampleStockComponent : GH_Component
    {
        public ExampleStockComponent()
          : base(
                "StockBridge Example Stock",
                "SBStock?",
                "Generate a ready-to-use Stock JSON for one of the built-in " +
                "presets (healthy / long / short_stock / wasteful / mixed_reclaimed / glulam_reclaimed). " +
                "Wire the JSON output into Assign Inventory.",
                "StockBridge", "0. Helpers")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-6666-6666-6666-600000000006");
        protected override Bitmap Icon => IconFactory.ExampleStock;
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Preset", "P",
                "One of: healthy, long, short_stock, wasteful, mixed_reclaimed, glulam_reclaimed (39-piece inventory).",
                GH_ParamAccess.item, "healthy");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Stock JSON", "J",
                "Stock list JSON, ready for Assign Inventory.",
                GH_ParamAccess.item);
            p.AddTextParameter("Description", "D",
                "What this preset is meant to demonstrate.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string preset = "healthy";
            DA.GetData(0, ref preset);
            preset = (preset ?? "healthy").Trim().ToLowerInvariant();

            List<StockItem> items;
            string description;
            switch (preset)
            {
                case "glulam_reclaimed":
                    items = GlulamReclaimed();
                    description = "Custom inventory: g = 12 Glulam pieces, 4.5 m long, section 0.100 x 0.150 m; " +
                        "r = 27 reclaimed timber pieces, 3.5 m long, section 0.075 x 0.150 m. " +
                        "2 stock types, 39 pieces, total length 148.5 m. Material strength properties are unspecified.";
                    break;
                case "long":
                    items = Long();
                    description = "Full range including LONG pieces (up to 6.0m). For larger structures (members/triangles up to ~5–6m) and rules that need long reach — addNodeToStock / splitEdgeToPoint over big spans, where 'healthy' (max 3.5m) cannot reach all corners.";
                    break;
                case "short_stock":
                    items = ShortStock();
                    description = "Only short pieces (≤1.2m). Long members will be flagged 'tooLong' and the engine will suggest Split Member / Add Connection Point.";
                    break;
                case "wasteful":
                    items = Wasteful();
                    description = "Only very long pieces (3.0m). Most assignments waste >40% and trigger 'tooMuchWaste' → suggest Shift Node / Remove Redundant.";
                    break;
                case "mixed_reclaimed":
                    items = MixedReclaimed();
                    description = "Realistic reclaimed mix — long, medium, short with varied sections. Triggers the full mismatch palette.";
                    break;
                case "healthy":
                default:
                    items = Healthy();
                    description = "Calibrated to a 1.0–5.1 m structure. Longest piece 3.5 m, so members up to ~3.5 m assign from a single piece (offcut returns to inventory); members longer than 3.5 m (up to 5.1 m) are flagged 'tooLong' and trigger Split Member.";
                    break;
            }

            var json = JsonConvert.SerializeObject(items, Formatting.Indented);
            DA.SetData(0, json);
            DA.SetData(1, description);

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Preset '{preset}': {items.Count} stock entries");
        }

        // -------- presets --------

        // Example inventory with specified dimensions and counts.
        // Strength and stiffness are unspecified; optional properties remain unset.
        private static List<StockItem> GlulamReclaimed() => new List<StockItem>
        {
            new StockItem { Id = "g", Type = "linear", Length = 4.5, Width = 0.100, Height = 0.150, Material = "Glulam", Count = 12 },
            new StockItem { Id = "r", Type = "linear", Length = 3.5, Width = 0.075, Height = 0.150, Material = "reclaimed timber", Count = 27 },
        };

        // Calibrated to a structure with members ~1.0–5.1 m. Longest piece is
        // 3.5 m on purpose: members up to 3.5 m assign from one piece (offcut
        // returns to inventory), while members longer than 3.5 m need Split.
        private static List<StockItem> Healthy() => new List<StockItem>
        {
            new StockItem { Id = "H_35", Length = 3.5, Width = 0.12, Height = 0.16, Material = "reclaimed timber", Count = 16, AllowableStress = 14000, ElasticModulus = 11000000 },
            new StockItem { Id = "H_25", Length = 2.5, Width = 0.10, Height = 0.14, Material = "reclaimed timber", Count = 20, AllowableStress = 14000, ElasticModulus = 11000000 },
            new StockItem { Id = "H_15", Length = 1.5, Width = 0.10, Height = 0.12, Material = "reclaimed timber", Count = 24, AllowableStress = 14000, ElasticModulus = 11000000 },
            new StockItem { Id = "H_10", Length = 1.0, Width = 0.08, Height = 0.10, Material = "reclaimed timber", Count = 24, AllowableStress = 14000, ElasticModulus = 11000000 },
        };

        // Full range incl. long pieces — for larger structures and rules that
        // need a node to reach several corners within one piece.
        private static List<StockItem> Long() => new List<StockItem>
        {
            new StockItem { Id = "L_60", Length = 6.0, Width = 0.16, Height = 0.20, Material = "reclaimed beam, long",  Count = 8,  AllowableStress = 14000, ElasticModulus = 11000000 },
            new StockItem { Id = "L_45", Length = 4.5, Width = 0.14, Height = 0.18, Material = "reclaimed beam",        Count = 12, AllowableStress = 14000, ElasticModulus = 11000000 },
            new StockItem { Id = "L_35", Length = 3.5, Width = 0.12, Height = 0.16, Material = "reclaimed timber",      Count = 16, AllowableStress = 14000, ElasticModulus = 11000000 },
            new StockItem { Id = "L_25", Length = 2.5, Width = 0.10, Height = 0.14, Material = "reclaimed timber",      Count = 20, AllowableStress = 14000, ElasticModulus = 11000000 },
            new StockItem { Id = "L_15", Length = 1.5, Width = 0.10, Height = 0.12, Material = "reclaimed timber",      Count = 24, AllowableStress = 14000, ElasticModulus = 11000000 },
            new StockItem { Id = "L_10", Length = 1.0, Width = 0.08, Height = 0.10, Material = "reclaimed short",       Count = 24, AllowableStress = 14000, ElasticModulus = 11000000 },
        };

        private static List<StockItem> ShortStock() => new List<StockItem>
        {
            new StockItem { Id = "S_06", Length = 0.6, Width = 0.06, Height = 0.08, Material = "short reclaimed", Count = 40 },
            new StockItem { Id = "S_09", Length = 0.9, Width = 0.06, Height = 0.08, Material = "short reclaimed", Count = 30 },
            new StockItem { Id = "S_12", Length = 1.2, Width = 0.08, Height = 0.12, Material = "short reclaimed", Count = 20 },
        };

        private static List<StockItem> Wasteful() => new List<StockItem>
        {
            new StockItem { Id = "W_30", Length = 3.0, Width = 0.12, Height = 0.16, Material = "new timber, full length", Count = 40 },
        };

        private static List<StockItem> MixedReclaimed() => new List<StockItem>
        {
            new StockItem { Id = "M_long_28",  Length = 2.8, Width = 0.12, Height = 0.16, Material = "reclaimed beam",  Count = 6  },
            new StockItem { Id = "M_med_18",   Length = 1.8, Width = 0.10, Height = 0.14, Material = "reclaimed timber", Count = 10 },
            new StockItem { Id = "M_med_15",   Length = 1.5, Width = 0.08, Height = 0.12, Material = "reclaimed timber", Count = 12 },
            new StockItem { Id = "M_short_10", Length = 1.0, Width = 0.08, Height = 0.10, Material = "reclaimed short",  Count = 18 },
            new StockItem { Id = "M_short_07", Length = 0.7, Width = 0.06, Height = 0.08, Material = "reclaimed short",  Count = 24 },
        };
    }
}
