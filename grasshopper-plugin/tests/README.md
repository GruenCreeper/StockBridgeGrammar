# Tests

Run commands from the repository root. The suites compile the production source directly and return nonzero on failure.

| Suite | Requirements | Coverage |
| --- | --- | --- |
| [StockInventoryRegression](StockInventoryRegression/README.md) | .NET 9 SDK | Inventory parsing, normalization, validation, and assignment |
| [SpatialRegression](SpatialRegression/README.md) | .NET 9 SDK | Spatial grammar, physical stock conservation, candidate controls, and published JSON examples |
| [PooledStockIntegration](PooledStockIntegration/README.md) | Windows x64, .NET SDK, .NET Framework 4.8, Rhino 8 | Actual Grasshopper components, pooled input trees, section checks, target selection, and saved-component compatibility |

```powershell
dotnet run --project grasshopper-plugin/tests/StockInventoryRegression -c Release
dotnet run --project grasshopper-plugin/tests/SpatialRegression -c Release
dotnet build grasshopper-plugin/tests/PooledStockIntegration -c Release
& ./grasshopper-plugin/tests/PooledStockIntegration/bin/Release/net48/PooledStockIntegration.exe
dotnet build grasshopper-plugin/StockBridgeGrammar -c Release
```

The integration suite defaults to Rhino 8 under `%ProgramFiles%`. See its README to override the installation path. NuGet restores Newtonsoft.Json 13.0.3; the plugin also restores its pinned Grasshopper package. Generated binaries and reports are build output and are not source files.

These tests establish software behavior, not structural design certification. Before publishing a release, open the plugin in Rhino/Grasshopper and check the documented example workflow, actual curve selection, and geometry preview interactively.
