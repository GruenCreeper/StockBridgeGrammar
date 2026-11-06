# Spatial grammar regressions

Run from the repository root with the .NET 9 SDK:

```powershell
dotnet run --project grasshopper-plugin/tests/SpatialRegression -c Release
```

Newtonsoft.Json 13.0.3 is restored through NuGet. Rhino is not required. The project compiles the plugin's core source and copies the published spatial JSON examples into its build output.

The suite checks analytical sphere intersections, both spatial mirror solutions, original stock lengths, anchor permutations, stock availability and physical-piece reservations, invalid and degenerate input, collision handling, unchanged input on refusal, transformed geometry, large inventory counts, candidate controls and metrics, and the published examples.

Each check prints a result. A failed check produces a nonzero exit code. The detailed JSON report is written to `bin/Release/net9.0/results.json`; build output is excluded from the repository.
