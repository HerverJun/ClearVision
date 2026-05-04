# 区域补集 / Region Complement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionComplementOperator` |
| 枚举值 (Enum) | `OperatorType.RegionComplement` |
| 分类 (Category) | Region |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | Region, Complement, Invert, Background |
| 图标 (Icon) | region-complement |

## 算法原理 / Algorithm Principle
区域补集运算计算输入区域相对于一个有限图像域的补集。给定输入区域 A 和图像尺寸 W x H，补集结果为图像域内所有不属于 A 的像素集合：

```
Complement(A) = {(x, y) | 0 <= x < W, 0 <= y < H} \ A
```

基于游程编码（Run-Length Encoding, RLE）实现：将输入区域的游程按行分组并排序，然后逐行扫描，将相邻游程之间的间隙（gap）作为补集区域的游程输出。对于某一行 y，若输入游程为 `[s1, e1], [s2, e2], ...`，则补集游程为 `[0, s1-1], [e1+1, s2-1], ..., [en+1, W-1]`。

> English: The complement operator computes the set difference between a bounded image domain (W x H) and the input region. It scans RLE runs row by row, emitting gap intervals between consecutive runs as the complement.

## 实现策略 / Implementation Strategy
1. **边界获取**：优先使用 `Image` 输入端口获取图像尺寸；其次使用 `ImageWidth`/`ImageHeight` 显式参数；若均未提供，则回退到区域边界框（BoundingBox）扩展 10 像素作为默认边界。
2. **游程裁剪**：对输入区域的所有游程先按行过滤（`Y >= 0 && Y < height`），再裁剪到 `[0, width-1]` 范围内，忽略越界游程。
3. **逐行间隙填充**：对每行已排序的游程列表，从 `currentPos = 0` 开始扫描，遇到游程起始位置大于 `currentPos` 时输出间隙游程，然后更新 `currentPos` 到游程结束位置之后。
4. **合并相邻游程**：最终通过 `Region.MergeAdjacentRuns()` 合并可能的相邻游程。

与 Halcon `complement` 算子对标，但增加了显式边界裁剪和回退策略。

> English: Clips input runs to explicit image bounds, groups by row, and emits gap intervals as the complement region. Falls back to bounding-box + 10px when no explicit bounds are provided.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputRegion(inputs, "Region")` -- 获取输入区域
2. 获取图像尺寸（优先级：`Image` > `ImageWidth`/`ImageHeight` > `BoundingBox` + 10px）
3. `region.RunLengths` -- 获取输入区域的游程列表
4. `ClipRunToBounds(run, width)` -- 将每个游程裁剪到 `[0, width-1]` 范围
5. `.GroupBy(run => run.Y)` -- 按行分组
6. 逐行扫描间隙，生成补集游程 `new RunLength(y, gapStart, gapEnd)`
7. `new Region(compRuns).MergeAdjacentRuns()` -- 构造补集区域并合并相邻游程
8. `CreateVisualization(region, complement, width, height)` -- 生成可视化（原始区域蓝色 + 补集绿色轮廓）
9. `CreateImageOutput(vis, { Region, Area, InputArea, ClippedInputArea, TotalArea, FillRatio, ProcessingTimeMs })` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| - | - | - | - | 本算子无可配置参数。图像尺寸通过输入端口或参考图像隐式确定。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region` | Input Region | `Any` | Yes | 待取补集的输入区域。 |
| `ImageWidth` | Image Width | `Integer` | No | 图像宽度（像素）。用于确定补集的边界。 |
| `ImageHeight` | Image Height | `Integer` | No | 图像高度（像素）。用于确定补集的边界。 |
| `Image` | Reference Image (optional) | `Image` | No | 参考图像。提供时自动从中提取宽度和高度，优先级高于显式 Width/Height 参数。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Complement Region | `Any` | 补集区域。 |
| `Image` | Visualization | `Image` | 可视化图：原始区域蓝色叠加，补集区域绿色轮廓。 |
| `Area` | Complement Area | `Integer` | 补集区域面积（像素数）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `InputArea` | `Integer` | 原始输入区域面积。 |
| `ClippedInputArea` | `Integer` | 裁剪到图像边界后的输入区域面积。 |
| `TotalArea` | `Integer` | 图像域总面积（W x H）。 |
| `FillRatio` | `Double` | 输入区域占图像域的面积比（ClippedInputArea / TotalArea）。 |
| `ProcessingTimeMs` | `Long` | 算子执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(R log R + H + K)，其中 R 为输入游程数，H 为图像高度，K 为输出游程数 |
| 典型耗时 (Typical Latency) | 平均 0.186 ms，最大 4.522 ms（100 组合成测试用例） |
| 内存特征 (Memory Profile) | O(R+H+K)，主要为游程列表和按行分组的字典开销 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：构建背景掩码（Background Mask），当需要获取前景区域之外的背景部分时。
- **适合 (Suitable)**：生成反向 ROI（Inverse ROI），用于排除已知区域、只处理剩余区域。
- **适合 (Suitable)**：布尔区域运算链中的取反步骤，如先检测目标区域再取补集得到非目标区域。
- **不适合 (Not Suitable)**：无界几何补集运算。本算子需要有限的图像域（宽度和高度），无法处理无限平面的补集。
- **不适合 (Not Suitable)**：输入区域本身就跨越多个不连续图像域的场景，因为边界裁剪是全局统一的。

## 已知限制 / Known Limitations
1. 必须提供 `ImageWidth`/`ImageHeight` 或参考图像，否则回退到边界框 + 10 像素，结果可能不完整或不一致。
2. 输入游程超出显式边界的部分会被裁剪或忽略，不会产生越界补集。
3. 当前实现对每一行执行线性扫描，未对 Region2 游程做行索引预建；对于极度碎片化的区域，性能可能退化。
4. 输出为二值区域（Region），不保留源标签或置信度信息。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充算法原理（RLE 间隙填充公式）、实现策略（边界获取优先级、游程裁剪逻辑）、详细输入输出端口说明、运行时附加输出、性能分析与限制说明 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
