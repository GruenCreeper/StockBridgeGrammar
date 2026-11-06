# StockBridgeGrammar

StockBridgeGrammar is a Rhino 8 / Grasshopper plugin for stock-responsive bridge geometry. It converts curves into a node-and-member graph, assigns available material, applies shape-grammar rules, and previews the resulting structure. Version **1.0.0** includes **15 components and 19 rules**.

The workflow supports inventory-aware design exploration. Section checks require supplied member forces and material properties; the plugin does not solve structural equilibrium or certify a design for construction.

Author: [GruenCreeper](https://github.com/GruenCreeper). Download the [v1.0.0 release](https://github.com/GruenCreeper/StockBridgeGrammar/releases/tag/v1.0.0) or browse the [source repository](https://github.com/GruenCreeper/StockBridgeGrammar).

## Install

1. Build the plugin as described below, or use `StockBridgeGrammar.gha` from a release package.
2. Close Rhino. Copy the file to `%APPDATA%\Grasshopper\Libraries\` and keep only one installed copy.
3. If Windows blocks the downloaded file, open its Properties and select **Unblock**.
4. Restart Rhino 8 and open Grasshopper. Components appear under **StockBridge**.

The plugin targets .NET Framework 4.8 on Windows. Rhino supplies Grasshopper, RhinoCommon, and Newtonsoft.Json at runtime.

## Use

Connect curves to **Recognize Structure**, assign stock with **Assign Inventory**, select a rule and target, then pass the graph through **Apply Grammar Rule** and **Decode Graph**. Connect the same stock inventory to each operation that consumes stock. Geometry coordinates and stock lengths must use consistent units; the supplied examples use metres.

- [English user guide](grasshopper-plugin/StockBridgeGrammar/README_UserGuide_EN.md)
- [中文使用说明](grasshopper-plugin/StockBridgeGrammar/使用说明_中文.md)
- [Examples and wiring](grasshopper-plugin/StockBridgeGrammar/examples/README.md)
- [Spatial node placement example](grasshopper-plugin/StockBridgeGrammar/examples/add_node_spatial/README.md)

## Build

Install a .NET SDK and the .NET Framework 4.8 targeting pack on Windows, then run from the repository root:

```powershell
dotnet build grasshopper-plugin/StockBridgeGrammar/StockBridgeGrammar.csproj -c Release
```

The output is `grasshopper-plugin/StockBridgeGrammar/bin/Release/StockBridgeGrammar.gha`. Package versions are specified in the project file. NuGet access is needed when dependencies are not already cached.

## Tests

See [the test guide](grasshopper-plugin/tests/README.md) for inventory, spatial geometry, and Grasshopper integration checks. Core tests run independently of Rhino; integration tests require a local Rhino 8 installation.

## Reproduce a result

Keep the plugin version, Rhino version, model units, input geometry, stock inventory, ordered rule applications, targets, and rule parameters with each result. For section checks, also retain the source of the member forces and the material properties. Recompute forces after changing the structure. The supplied example inventories are demonstration inputs, not measured research datasets.

When citing a released version, record its repository URL, release tag, and commit identifier alongside the example or dataset used. A versioned release lets readers identify the code associated with a reported result.

For version 1.0.0: **GruenCreeper (2026). StockBridgeGrammar (version 1.0.0) [Software].** Use the [version-specific source](https://github.com/GruenCreeper/StockBridgeGrammar/tree/v1.0.0) when linking this version from a publication. Citation metadata is provided in [CITATION.cff](CITATION.cff).

## Release packages

After building and running the relevant tests:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-release.ps1
```

The script creates a source archive, an installation archive, and SHA-256 checksums in `release/`. Only the listed source, documentation, examples, and test files enter the source archive; installation packages contain the plugin and user documentation.
