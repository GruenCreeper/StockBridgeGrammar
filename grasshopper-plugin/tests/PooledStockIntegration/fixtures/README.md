# Saved-component fixtures

These XML fragments were produced with `GH_Component.Write` and `GH_LooseChunk.Serialize_Xml` from the earlier plugin components. They contain only component and parameter state, without a user canvas, geometry, external file paths, or a compiled plugin.

- `section-check-six-inputs.xml`: the six-input Section Check, with its existing allowable stress (`Sig`) set to `12345`. Deserialization into the current component must preserve this value and append the two utilization limits with defaults of `1`.
- `target-picker-legacy.xml`: the prior Target Picker with nine inputs and six outputs. Deserialization must retain its port layout.

Descriptions are normalized; parameter order, types, and saved values are retained. The tests read these fixtures from the build output; no historical plugin binary is required.
