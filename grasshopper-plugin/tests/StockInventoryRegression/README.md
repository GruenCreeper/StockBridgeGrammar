# Stock inventory regressions

Run from the repository root with the .NET 9 SDK:

```powershell
dotnet run --project grasshopper-plugin/tests/StockInventoryRegression -c Release
```

Newtonsoft.Json 13.0.3 is restored through NuGet. Rhino is not required.

The console exits nonzero on failure. It covers merge parsing, duplicate and generated IDs, preservation of stock dimensions/count/material properties, shared field broadcasting, rejected invalid inputs, and the actual assignment algorithm consuming the merged inventory.

This suite tests the shared core. Build the plugin to check Grasshopper API compatibility and run the [component integration tests](../PooledStockIntegration/README.md) for tree collection and saved-component migration.
