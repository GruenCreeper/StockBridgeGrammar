# 三锚点空间加点 / Spatial add-node example (1.0.0)

这个例子只验证三球几何、目标方向和实物库存分配。图中只有三个已声明的支座锚点，没有已有杆件占用库存。坐标和库存长度单位为米。

## 用 JSON 直接验证

1. 把 `graph.json` 的完整内容放入 Panel → Apply.G。
2. 把 `stock.json` 的完整内容放入 Panel → Apply.S。
3. Rule ID (R) 填 `addNodeToStock`；Target ID (T) 留空。
4. 把 `params_above.json` 内容放入 Panel → Apply.P。Position (Pt)、Target Point (Tp)、Fold Point (Fp) 暂不接，因为锚点和期望点已包含在参数中。
5. Apply.G → Decode.G；查看新节点、三根线及 Apply.H/W。
6. 把参数替换成 `params_below.json`，仍使用原始 `graph.json` 作为输入，比较上下镜像。**不要用第一轮输出图作为第二轮输入**：第一轮已占用了三根料。

| 检查项 | 上方目标 | 下方目标 |
| --- | --- | --- |
| 目标点 | (1, 1, 2) | (1, 1, −2) |
| 预期新点 | (1, 1, √2) ≈ (1, 1, 1.41421356) | (1, 1, −√2) ≈ (1, 1, −1.41421356) |
| 到每个锚点的距离 | 2 m | 2 m |
| 离锚点平面的高度 | √2 m | √2 m |
| 新杆 / 库存占用 | 3 根，使用 3 个不同 S2#… 实物编号 | 3 根，使用 3 个不同 S2#… 实物编号 |

三个球心是 (0,0,0)、(2,0,0)、(0,2,0)，半径均为 2。三球交点满足 x = 1、y = 1、z² = 2。目标点只是决定选择哪个可行交点，所以输出 z 不等于目标的 ±2。

## 换成 Rhino 点输入

保留 Graph/Stock，Params 改为 `{"minHeight":0.01}`。建立三个 Rhino 锚点，合成一个列表接 Apply.**Position (Pt)**；另建一个期望点接 Apply.**Target Point (Tp)**。期望点也可以接到 Target Picker 的 Pt 输入，再从它的 **Tp 输出**接 Apply.Tp。

Target Picker.T 输出只有节点 ID；原始目标坐标通过 Tp 输出传递。三个锚点与期望点是四个独立输入点，不要把期望点混入 Position 列表。

## 拒绝检查

- 把 stock 的 count 改为 2：库存不够三根，应拒绝。
- 把唯一料长改为 1：三个球无法相交，应拒绝。
- 把 minHeight 改为 2：两个解的高度均不足，应拒绝。
- 重复一个锚点或改成三点共线：应拒绝。
- 所有拒绝都不应生成平面替代点或截短杆；查看 W 中原因。

这个孤立支座例子不是整桥结构分析模型。全图拓扑计数可能仍显示诊断信息，不影响对坐标、长度和实物料占用的检查。

## English quick check

Feed the complete graph and stock JSON files to Apply.G/S. Set R to `addNodeToStock`, leave T empty, and feed `params_above.json` to P. Leave point inputs disconnected for this JSON-only run. Change P to `params_below.json` while keeping the original graph input.

The expected new nodes are (1,1,+√2) and (1,1,−√2). Each of the three bars is exactly 2 m and uses a different physical stock ID. With point controls instead, send the three anchors to Position (Pt) and a separate preferred location to Target Point (Tp), directly or via Target Picker.Tp. Count 2, stock length 1, minHeight 2, or degenerate anchors must refuse rather than create a planar/shortened fallback.
