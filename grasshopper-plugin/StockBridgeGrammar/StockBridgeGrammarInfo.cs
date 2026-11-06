// Assembly metadata displayed by Grasshopper.

using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace StockBridgeGrammar
{
    public class StockBridgeGrammarInfo : GH_AssemblyInfo
    {
        public override string Name => "StockBridgeGrammar";
        public override Bitmap Icon => null; // Use the default Grasshopper assembly icon.
        public override string Description =>
            "Stock-responsive shape grammar for bridge structures. " +
            "Recognises existing Rhino geometry as a graph, runs material " +
            "assignment, and applies 19 grammar rules including spatial " +
            "node placement from available stock lengths and a target point.";
        public override Guid Id => new Guid("8c4b9d6a-3e1f-4f2a-8b5c-1a2d3e4f5a6b");
        public override string AuthorName => "GruenCreeper";
        public override string AuthorContact => "https://github.com/GruenCreeper";
        public override string Version => "1.0.0";
    }
}
