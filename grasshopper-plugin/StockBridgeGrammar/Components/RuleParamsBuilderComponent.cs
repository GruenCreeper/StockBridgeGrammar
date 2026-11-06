using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockBridgeGrammar.Utils;

namespace StockBridgeGrammar.Components
{
    // Preserve the original input indices and GUID for saved definitions.
    // Retired controls are labelled explicitly and never emitted as parameters.
    public class RuleParamsBuilderComponent : GH_Component
    {
        public RuleParamsBuilderComponent()
          : base(
                "StockBridge Rule Params Builder",
                "SBParams",
                "Build valid parameter defaults for all 19 supported rules. " +
                "Use Extra Params JSON to override defaults. Fan Count still controls " +
                "fanOutLoadPath; inputs labelled DEPRECATED are compatibility slots " +
                "and have no effect. Use Defaults ignores Fan Count and Extra Params JSON.",
                "StockBridge", "0. Helpers")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("12345678-aaaa-aaaa-aaaa-a00000000010");
        protected override Bitmap Icon => IconFactory.RuleParams;
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Rule ID", "R", "Supported rule id (see Rule Library).", GH_ParamAccess.item);
            p.AddBooleanParameter("Use Defaults", "Def", "If true, emit rule defaults and ignore Fan Count and Extra Params JSON.", GH_ParamAccess.item, false);
            p.AddIntegerParameter("DEPRECATED Split Count", "Sc", "Compatibility slot only; ignored. Split locations follow stock lengths. Use maxBridgesPerNode in Extra Params JSON for new-node bracing.", GH_ParamAccess.item, 2);
            p.AddNumberParameter("DEPRECATED Position Ratio", "Pr", "Compatibility slot only; ignored. addConnectionPoint derives its hub position from existing anchors. Use extraAnchors in Extra Params JSON.", GH_ParamAccess.item, 0.5);
            p.AddTextParameter("DEPRECATED Diagonal Direction", "Dd", "Compatibility slot only; ignored. triangulateBay chooses a feasible triangle-closing edge automatically.", GH_ParamAccess.item, "forward");
            p.AddTextParameter("DEPRECATED Target Stock ID", "Ts", "Compatibility slot only; ignored. shiftNodeToMatchStock searches the supplied stock list.", GH_ParamAccess.item, "auto");
            p.AddNumberParameter("DEPRECATED Max Shift Distance", "Ms", "Compatibility slot only; ignored. shiftNodeToMatchStock derives its search from stock lengths.", GH_ParamAccess.item, 0.5);
            p.AddIntegerParameter("Fan Count", "Fc", "fanOutLoadPath: 2..4 new edges. Extra Params JSON can override this value.", GH_ParamAccess.item, 3);
            p.AddBooleanParameter("DEPRECATED Preserve Chords", "Pc", "Compatibility slot only; ignored. removeRedundantMember uses connectivity, without chord classification.", GH_ParamAccess.item, true);
            p.AddNumberParameter("DEPRECATED Cage Height", "Ch", "Compatibility slot only; ignored. addTopBracingCage is no longer a supported rule.", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("DEPRECATED Amplitude", "Amp", "Compatibility slot only; ignored. createKinkedLowerChord is no longer a supported rule.", GH_ParamAccess.item, 0.3);
            p.AddTextParameter("Extra Params JSON", "X", "Optional JSON object merged over rule defaults (and Fan Count), e.g. {\"maxBridgesPerNode\":0} or {\"extraAnchors\":3}. For addNodeToStock: points contains exactly three anchors, targetPoint is a soft reference, degree is 3 and minHeight defaults to 0.01. Ignored when Use Defaults is true.", GH_ParamAccess.item, "{}");
            p[11].Optional = true;
            // An empty upstream wire on a retired slot must not prevent GH
            // from calling SolveInstance for the inputs that still matter.
            foreach (int index in new[] { 2, 3, 4, 5, 6, 8, 9, 10 })
                p[index].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Params JSON", "P", "Parameter JSON for Apply Grammar Rule. Connected geometry inputs in Apply can further override geometry parameters.", GH_ParamAccess.item);
            p.AddTextParameter("Effective Params", "EP", "Summary of the JSON emitted by this builder.", GH_ParamAccess.item);
            p.AddTextParameter("Warnings", "W", "Validation and compatibility messages.", GH_ParamAccess.list);
        }

        private static readonly Dictionary<string, string> DefaultJson =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "splitMemberByStockLength", "{\"maxBridgesPerNode\":2}" },
            { "addConnectionPoint", "{\"extraAnchors\":2}" },
            { "removeConnectionPoint", "{}" },
            { "triangulateBay", "{}" },
            { "densifyWithShortStock", "{}" },
            { "fanOutLoadPath", "{\"fanCount\":3}" },
            { "removeRedundantMember", "{}" },
            { "shiftNodeToMatchStock", "{}" },
            { "articulateChainToStock", "{\"foldDown\":false}" },
            { "connectNodeToStock", "{\"maxConnectionsPerNode\":2}" },
            { "mergeSplitChain", "{}" },
            { "addNodeToStock", "{\"degree\":3,\"minHeight\":0.01,\"pointSupport\":true,\"snapTol\":0.25}" },
            { "removeNode", "{}" },
            { "dissolveNodeToDiagonal", "{\"maxHubDegree\":4,\"pairBy\":\"straight\"}" },
            { "crossBracePanel", "{\"keepSides\":false}" },
            { "uncrossPanel", "{}" },
            { "dedupeMembers", "{\"removeZeroLength\":true}" },
            { "splitEdgeToPoint", "{\"pointSupport\":true,\"snapTol\":0.25,\"foldSearch\":0.6}" },
            { "relocateMember", "{\"pointSupport\":true,\"snapTol\":0.25}" },
        };

        internal static JObject BuildParams(string ruleId, bool useDefaults, int fanCount,
            string extraJson, List<string> warnings)
        {
            ruleId = (ruleId ?? "").Trim();
            if (!DefaultJson.TryGetValue(ruleId, out var defaults))
                throw new ArgumentException($"Unsupported Rule ID '{ruleId}'. See Rule Library.");

            var parameters = JObject.Parse(defaults);
            if (!useDefaults)
            {
                if (ruleId == "fanOutLoadPath")
                {
                    if (fanCount < 2 || fanCount > 4)
                        warnings.Add($"fanCount {fanCount} out of 2..4 — clamped.");
                    parameters["fanCount"] = Math.Max(2, Math.Min(4, fanCount));
                }
                var extra = string.IsNullOrWhiteSpace(extraJson) ? new JObject() : JObject.Parse(extraJson);
                foreach (var property in extra.Properties())
                    parameters[property.Name] = property.Value.DeepClone();
            }

            // Never present names retired by the core as effective parameters.
            foreach (var retired in new[] { "splitCount", "positionRatio", "diagonalDirection",
                "targetStockId", "maxShiftDistance", "preserveChords", "cageHeight", "amplitude" })
            {
                if (parameters.Property(retired) == null) continue;
                parameters.Remove(retired);
                warnings.Add($"Deprecated parameter '{retired}' was ignored and removed.");
            }
            if (ruleId == "addNodeToStock" &&
                (parameters["degree"]?.Type != JTokenType.Integer || parameters["degree"].Value<int>() != 3))
                throw new ArgumentException("addNodeToStock requires degree 3: three full-stock-length spatial braces. The old degree-2 planar mode is no longer supported.");
            return parameters;
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string ruleId = "", extraJson = "{}";
            bool useDefaults = false;
            int fanCount = 3;
            DA.GetData(0, ref ruleId);
            DA.GetData(1, ref useDefaults);
            DA.GetData(7, ref fanCount);
            DA.GetData(11, ref extraJson);

            var warnings = new List<string>();
            JObject parameters;
            try
            {
                parameters = BuildParams(ruleId, useDefaults, fanCount, extraJson, warnings);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }
            foreach (int index in new[] { 2, 3, 4, 5, 6, 8, 9, 10 })
                if (Params.Input[index].Sources.Count > 0)
                    warnings.Add($"{Params.Input[index].Name}: compatibility input is ignored.");

            string json = parameters.ToString(Formatting.Indented);
            DA.SetData(0, json);
            DA.SetData(1, $"rule: {ruleId.Trim()}\nparams: {json.Replace("\n", " ")}");
            DA.SetDataList(2, warnings);
            if (warnings.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "See Warnings for ignored compatibility inputs or adjusted parameters.");
        }
    }
}
