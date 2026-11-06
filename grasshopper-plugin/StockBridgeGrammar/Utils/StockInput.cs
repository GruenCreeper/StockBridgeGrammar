using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using StockBridgeGrammar.Core;
using StockBridgeGrammar.Models;

namespace StockBridgeGrammar.Utils
{
    // A stock port represents one shared inventory, including every input branch.
    // Item access causes GH to solve once PER incoming JSON before parsing runs.
    public static class StockInput
    {
        public const string Description = "One shared inventory across ALL input branches. " +
            "Accepts merged SBItem JSON, multiple stock arrays, or SBStockBuild.J. " +
            "Stock entries are pooled, not evaluated as separate scenarios.";

        public static List<StockItem> Read(IGH_DataAccess data, int index, GH_Component owner)
        {
            GH_Structure<GH_String> tree;
            data.GetDataTree(index, out tree);
            return ReadTree(tree, owner);
        }

        public static List<StockItem> ReadTree(GH_Structure<GH_String> tree, GH_Component owner)
        {
            var jsons = new List<string>();
            if (tree != null)
                foreach (var branch in tree.Branches)
                    foreach (var value in branch)
                    {
                        if (value == null) throw new ArgumentException("Stock input contains a null item.");
                        jsons.Add(value.Value);
                    }
            var items = StockInventory.FromJson(jsons);
            foreach (var warning in StockInventory.Normalize(items))
                owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
            owner.Message = $"{items.Count} types / {items.Sum(x => (long)x.Count)} pieces";
            return items;
        }

        public static void Upgrade(GH_Component owner, int index)
        {
            // Old saved documents serialize the previous item-access setting.
            owner.Params.Input[index].Access = GH_ParamAccess.tree;
            owner.Params.Input[index].Description = Description;
        }
    }
}
