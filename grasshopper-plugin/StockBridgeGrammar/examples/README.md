# Examples

These examples use metres for coordinates, stock lengths, and section dimensions. Install StockBridgeGrammar before running the Grasshopper scripts. See the [English guide](../README_UserGuide_EN.md) or [中文指南](../使用说明_中文.md) for component inputs.

| Example | Purpose |
| --- | --- |
| [Minimal workflow](minimal_workflow_notes.md) | Recognise, assign stock, apply one rule, and decode geometry |
| [不手写 JSON 的配线示例](no_json_workflow_notes.md) | Build stock and choose targets using components |
| [Demo workflow](demo_workflow/README_DEMO_EN.md) / [中文](demo_workflow/示例文件使用说明.md) | Create a truss in Rhino and generate its Grasshopper canvas |
| [Spatial add-node](add_node_spatial/README.md) | Three anchors, three 2 m pieces, and two analytically known mirror solutions |
| [Custom stock input](custom_stock_merge.md) | Combine stock items and check quantities |
| [Section-check limits](section_check_limits.md) | Supply forces and set independent stress/buckling limits |
| [Glulam and reclaimed timber](stock_glulam_reclaimed.md) | Two stock types with 39 physical pieces |

## Canvas scripts

Run the scripts below in a Grasshopper Python component with a Boolean input named `Run`. Start with a blank canvas, turn Run on once, then turn it off. The scripts instantiate components from the installed plugin.

- [build_demo_canvas.py](demo_workflow/build_demo_canvas.py): basic workflow with candidate selection. Use the [connection map](demo_workflow/connection_map.md) to inspect the wiring.
- [build_split_articulate_canvas.py](build_split_articulate_canvas.py): split a member by stock length, then articulate the resulting chain.
- [build_section_loop_canvas.py](build_section_loop_canvas.py): one Section Check → Apply step. Recompute forces externally after changing geometry; the script does not create a feedback solver.

Run [create_demo_truss_rhino.py](demo_workflow/create_demo_truss_rhino.py) inside Rhino with `_RunPythonScript` to create the demonstration geometry. Set the Rhino document to metres before running it.

## JSON inputs

Copy the complete JSON content into a Grasshopper Panel connected to the appropriate input:

- [simple_graph.json](simple_graph.json): graph input.
- [rule_params_examples.json](rule_params_examples.json): one parameter object per rule; copy the selected object's value into Apply.P.
- [stock_healthy.json](stock_healthy.json), [stock_short.json](stock_short.json), [stock_wasteful.json](stock_wasteful.json), and [stock_mixed_reclaimed.json](stock_mixed_reclaimed.json): contrasting stock inventories.
- [stock_glulam_reclaimed.json](stock_glulam_reclaimed.json): the glulam/reclaimed-timber inventory.

Example values illustrate software behaviour. They are not measured material properties or a complete structural design dataset.
