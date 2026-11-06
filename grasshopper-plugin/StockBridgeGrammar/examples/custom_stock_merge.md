# Custom stock input / 自定义库存

## Wiring

1. Connect each **Stock Item.J** output to a different input of a Grasshopper Merge component. Stock Item.D is descriptive text and is not a stock input.
2. Connect Merge to **Stock Inventory Builder.J**.
3. Connect **Stock Inventory Builder.J** to **Assign Inventory.S**, **Apply Grammar Rule.S**, and any other component using the same inventory.

Stock Inventory Builder reads all branches and produces one complete inventory JSON array; Flatten is not required. Its component label shows stock-type and piece counts, T lists the entries, and W reports input problems.

Assign Inventory, Apply Grammar Rule, Generate Candidates, and Section Check also accept multiple stock items or arrays directly. All S branches form **one shared inventory**, not separate stock alternatives. Separate Graph inputs are still calculated independently.

## Input rules

- Duplicate IDs receive a suffix such as `S1_2` or `S1_3`, with a warning. Give each stock type an explicit unique ID for clear records.
- Combining inputs retains every entry, including entries with equal dimensions.
- J accepts individual stock objects, arrays, or `{"items":[...]}`. A populated J takes precedence over the separate field inputs.
- For field inputs, leave J empty and supply a list of lengths at L. IDs can be omitted. W/H/M/N can contain one shared value or a list matching L.
- Field-mode defaults are width 0.08 m, height 0.12 m, and count 1. Use consistent units.
- Length must be finite and positive. Width, height, and count cannot be negative. Count 0 entries remain in the inventory but are unavailable for assignment.
- Invalid JSON, missing lengths, or incompatible list lengths stop output and report an error.

## Example

Create three Stock Item components with lengths 1.5, 2, and 3 m and counts 2, 3, and 4. With the wiring above, Stock Inventory Builder should show **3 types / 9 pieces**, output three T rows, and produce one J array containing three records.

## 中文要点

每个 **Stock Item.J → Merge → Stock Inventory Builder.J**，再把 Builder.J 接到 **Assign.S 和 Apply.S**。J 是库存数据，D 是说明文字。Builder 和各计算组件都会汇总所有库存分支，无需 Flatten；所有分支代表同一个库存池，独立 Graph 仍分别计算。

重复 ID 会加后缀并提示；合并不会删除相同条目，也不会把不同尺寸合成一种。J 支持单条对象、数组或 `{"items":[...]}`。使用字段模式时清空 J，输入长度列表 L，其他字段采用单一共用值或等长列表。长度须为有限正数，宽、高和数量不能为负；数量 0 不参与分配。字段模式默认宽 0.08 m、高 0.12 m、数量 1。

上述三种库存的期望汇总为 **3 types / 9 pieces**，可用来核对连线和数量。
