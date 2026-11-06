# Glulam + reclaimed timber 预设库存

在 **StockBridge Example Stock（SBStock?）** 的 **P / Preset** 输入端接一个 Panel，填写：

```text
glulam_reclaimed
```

将组件的 **J / Stock JSON** 输出接到 **Assign.S** 和 **Apply.S**。这是一份完整库存，无需再经过 Merge 或 Stock Inventory Builder。

| ID | 材料 | 长度（m） | 宽（m） | 高（m） | 数量 |
| --- | --- | ---: | ---: | ---: | ---: |
| g | Glulam | 4.5 | 0.100 | 0.150 | 12 |
| r | reclaimed timber | 3.5 | 0.075 | 0.150 | 27 |

两类材料的 Type 均为 linear，共 39 根，总长度 148.5 m。此示例未设置 allowableStress 或 elasticModulus；用于截面检查时，请提供适用的材料属性。

也可直接使用同目录下的 `stock_glulam_reclaimed.json`：将完整内容粘贴到一个 Grasshopper Panel 中，以单个多行字符串输出并连接到库存输入。
