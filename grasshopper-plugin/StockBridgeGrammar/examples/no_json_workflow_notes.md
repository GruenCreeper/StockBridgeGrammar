# 不手写 JSON 的工作流（1.0.0）

## 库存与基本连线

用 Stock Item 的长度、截面、材料、数量输入创建库存条目，多个条目接 Stock Inventory Builder.J。也可用 Builder 的平行列表输入，或直接使用 Example Stock。

- Curves → Recognize.C；Recognize.J → Assign.G。
- 库存 JSON → **Assign.S 和 Apply.S**。
- Assign.G → Apply.G、Target Picker.G；Assign.M → Target Picker.M。
- 选一个规则 ID 接 Apply.R 和 Rule Params Builder.R；Builder.P → Apply.P。
- Target Picker.T → Apply.T（使用目标 ID 的规则）。
- Apply.G → Decode.G；连续改写时传递更新图。

## Target Picker

**Mo 留空时的自动顺序：**非空 `Id` → 有效 `Target Curve (C)` → 有效 `Target Point (Pt)` → 不匹配排序。Id 在当前图中查找杆件或已有节点；C 按曲线找杆件；Pt 选最近的已有节点。不匹配列表也为空时才回退到第一根杆件。Id 存在但找不到时会报错并停止，不会悄悄换用 C 或 Pt。C 与 Pt 同时连接时 C 选目标，但原 Pt 坐标仍从 Tp 输出。

按 ID 输入：把当前 Graph JSON 接到 Picker.G，把一个 Panel（如 `M1` 或 `N2`）接到 Picker.Id，Mo 留空。选中的 ID 显示在组件下方和 T 输出；将 Picker.T 接到 Apply.T。ID 会去掉前后空格，仍区分大小写。如果 Mo 已连接别的模式，显式模式优先，忽略 Id 时会显示警告；需要指定时可将 Mo 接 `by_member_id`、`by_node_id`、`by_curve` 或 `by_point`。要显式保持不匹配排序，需要将 `by_mismatch_rank` 用 Panel 接到 Mo。Graph Label 组件可显示当前图中的 M/N 编号。结构更新后要把更新的 Graph 接到 Picker.G，因为 ID 可能变化。

**按点选择：**Mo 留空且 Id/C 为空，或显式 `by_point`，Pt 返回离输入点最近的**已有节点 ID**；Tp 输出仍是原来的点坐标。要指定 addNodeToStock 的新点位置，应将 Tp 接到 Apply.Tp，而三个锚点接 Apply.Pt。也可以直接把文本 ID 接 Apply.T，不必经过 Picker，但不会在 Picker 中验证这个 ID。

| Mode | 用法 |
| --- | --- |
| `by_mismatch_rank` | 按不匹配严重度取目标，R=0 为首项 |
| `by_member_id` | Id 填杆件 ID |
| `by_index` | I 为从 0 开始的杆件索引；I=5 是第六根 |
| `by_curve` | C 接一根曲线，匹配杆件 |
| `by_point` | Pt 接一个点；T 返回最近已有节点 ID |
| `by_node_id` | Id 填节点 ID |

原始 Pt 坐标从 **Tp** 输出透传。T 输出只含 ID。

## 规则参数

打开 Rule Library 查看规则、目标与参数。Rule Params Builder 可生成默认参数或覆盖指定参数。标记为 deprecated 的输入仅用于画布兼容，不参与规则控制。

默认工作流无需手写参数。需要覆盖特定参数时，可使用 Extra Params JSON (X)，并设 Def=false；Def=true 会忽略 X 和 Fan Count。Fan Count 仍可控制 fanOutLoadPath。

## 三点空间加点

1. Apply.R 选择 `addNodeToStock`，接 Graph 和 Stock。
2. 三个互异且不共线锚点合成一个列表接 **Position (Pt)**。
3. 独立期望点接 **Target Point (Tp)**，也可接 **Target Picker.Tp → Apply.Tp**。
4. 已有明确锚点时，Apply.T 可空；默认最小离面高度是 0.01 米。
5. 结果是三根可用实物库存原长的三球空间交点中，距 Tp 最近的一点。没有空间解或数量不足会拒绝，不会退回共面节点。

验证坐标和镜像选择请看 [2 米库存例子](add_node_spatial/README.md)。

## 候选与检查

Generate.G 输出是候选分支树，直接接 Candidate Selector.CG 即可读取全部分支，索引选择一个候选，再送 Decode。以实际候选名称与历史判断应用了哪些规则。

Section Check 单步输出 **G2/Nr/Nt → Apply.G/R/T**；Ru/T 是逐杆报告。每次改形后重新计算受力，再检查更新图。几何诊断与节点/杆件计数不能证明整体稳定性。
