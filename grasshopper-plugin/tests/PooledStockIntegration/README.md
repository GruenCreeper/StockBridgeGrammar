# Grasshopper component integration tests

This Windows x64 test harness starts Rhino 8 without a visible window, creates in-memory Grasshopper documents, and runs the actual component solution engine. Rhino 8 must be installed and able to initialize under the current user. No user canvas files are opened or changed.

From the repository root:

```powershell
dotnet build grasshopper-plugin/tests/PooledStockIntegration -c Release
& ./grasshopper-plugin/tests/PooledStockIntegration/bin/Release/net48/PooledStockIntegration.exe
```

The default installation path is `%ProgramFiles%\Rhino 8\System`. For another installation, supply its System directory both at build time and at runtime:

```powershell
dotnet build grasshopper-plugin/tests/PooledStockIntegration -c Release "-p:RhinoSystem=D:/Applications/Rhino 8/System"
& ./grasshopper-plugin/tests/PooledStockIntegration/bin/Release/net48/PooledStockIntegration.exe 'D:\Applications\Rhino 8\System'
```

The executable exits nonzero on failure. Checks cover:

- Pooled stock objects and arrays across one or multiple Grasshopper branches, with one graph and six detail rows.
- Assignment and section checks sharing a 39-piece pool of glulam and reclaimed timber.
- Stock input access migration for all four stock-consuming components.
- Independent stress and buckling limits, boundary equality, tension, invalid values, live slider changes, and stock reselection.
- Target selection precedence, unknown IDs, explicit mode overrides, forwarded points, and saved component port indices.
- Deserialization of the original six-input Section Check and prior Target Picker, using the XML fixtures in [`fixtures`](fixtures/README.md).

Native curve construction is unavailable in this headless test host. Curve precedence is checked through the production mode resolver; the Grasshopper engine tests ID, point, fallback, and explicit mode paths. Verify selection of actual Rhino curves in an interactive Grasshopper session before a release.
