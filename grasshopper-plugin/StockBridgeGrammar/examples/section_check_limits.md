# Section-check limits / 截面检查阈值

Section Check accepts independent limits for the stress and compression-buckling ratios.

| Input | Label | Default | Meaning |
| --- | --- | ---: | --- |
| Stress Limit | SL | 1.0 | Stress ratios above this limit fail the stress check |
| Buckling Limit | BL | 1.0 | Compression-buckling ratios above this limit fail the buckling check |

Connect a Number Slider to each input. Limits must be finite and positive; zero, negative values, NaN, and infinity are invalid. Comparisons use a numerical tolerance of 0.000001.

- With no sliders, both limits are 1.0.
- SL=0.8 triggers the stress check above a ratio of 0.8.
- BL=0.1 triggers the buckling check above a ratio of 0.1.
- Limits below 1.0 are more restrictive. Limits above 1.0 permit ratios greater than the default threshold.

R and Br output the original stress and buckling ratios. Limits do not change `allowableStress`, `elasticModulus`, Sig, or k. Sum and Dt report the limits used.

Inventory selection uses the same limits. If the assigned stock fails and another available piece passes, the check prefers a passing piece. If both stress and buckling limits are exceeded, the larger normalised exceedance, R/SL or Br/BL, determines the reported failure; ties favour buckling. Members in tension do not trigger a buckling failure.

These limits control Section Check and its rule suggestions. Other components do not automatically receive them. A `noForce` result means a force was not supplied; it does not indicate a successful check.

## Applying one suggestion

Connect **Section Check.G2/Nr/Nt → Apply.G/R/T** and stock to Apply.S. Supply any anchors or target point required by the suggested rule. After changing geometry, recompute member forces before checking again. Ru/T are per-member reports, not a sequence of executable operations.

[build_section_loop_canvas.py](build_section_loop_canvas.py) creates this single-step canvas. It includes manual force panels and a VGS Force Adapter; VGS itself is a separate plugin.

## 中文要点

SL 和 BL 分别是应力比与受压杆屈曲比的触发限值，默认均为 1.0。接 Number Slider 即可独立调整；输入必须为有限正数，比较容差为 0.000001。小于 1 的限值更严格，大于 1 则放宽条件。R、Br 保持原始比值，材料属性及 Sig、k 不变。

库存重选也使用这两个限值；原材料不通过而其他可用材料通过时，优先采用通过的材料。两项均超限时比较 R/SL 与 Br/BL，取相对超限较大者；相等时优先报告 buckling。受拉杆不触发屈曲失败。

单次建议使用 **G2/Nr/Nt → Apply.G/R/T**，库存接 Apply.S。补齐规则所需锚点或目标点，改形后重新求力再检查。直接把输出接回输入不构成迭代求解器。
