# 区域并集 / Region Union

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionUnionOperator` |
| 枚举值 (Enum) | `OperatorType.RegionUnion` |
| 分类 (Category) | Region |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | Region, Union, Boolean, Merge, Combine |
| 图标 (Icon) | region-union |

## 算法原理 / Algorithm Principle
区域并集运算计算两个区域的并集 A ∪ B，即属于 A 或属于 B（或两者）的所有像素：

```
Union = {p | p in A or p in B}
```

基于游程编码（RLE）实现：将两个区域的游程列表合并，按 (Y, StartX) 排序，然后线性扫描合并同 Y 行上重叠或相邻的游程。两个游程 `[y, s1, e1]` 和 `[y, s2, e2]` 在满足 `s2 <= e1 + 1` 时合并为 `[y, s1, max(e1, e2)]`。

> English: Computes A ∪ B by concatenating both run lists, sorting by (Y, StartX), and merging overlapping or adjacent runs on the same row.

## 实现策略 / Implementation Strategy
1. **游程合并排序**：将两个区域的游程列表合并为一个列表，按 `Y` 升序、`StartX` 升序排序。
2. **线性扫描合并**：遍历排序后的游程列表，若当前游程与下一游程在同一行且 `next.StartX <= current.EndX + 1`（重叠或相邻），则合并；否则输出当前游程。
3. **结果区域构造**：直接用合并后的游程列表构造 `Region` 对象（无需额外 `MergeAdjacentRuns`，因为已在扫描中完成合并）。

与 Halcon `union2` 算子对标。

> English: Concatenates and sorts both run lists, then linearly merges overlapping/adjacent runs on the same row in a single pass.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputRegion(inputs, "Region1")` / `TryGetInputRegion(inputs, "Region2")` -- 获取两个输入区域
2. `r1.RunLengths.Concat(r2.RunLengths)` -- 合并游程列表
3. `.OrderBy(r => r.Y).ThenBy(r => r.StartX)` -- 按行和起始 X 排序
4. `MergeOverlappingRuns(allRuns)` -- 线性扫描合并同行重叠/相邻游程
5. `new Region(merged)` -- 构造并集区域
6. `CreateVisualization(r1, r2, uni)` -- 生成可视化（Region1 蓝色 + Region2 红色 + 并集绿色轮廓）
7. `CreateImageOutput(vis, { Region, Area, Region1Area, Region2Area, ProcessingTimeMs })` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| - | - | - | - | 本算子无可配置参数。运算完全由两个输入区域决定。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region1` | First Region | `Any` | Yes | 第一个输入区域。 |
| `Region2` | Second Region | `Any` | Yes | 第二个输入区域。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Union Region | `Any` | 并集区域（A ∪ B）。 |
| `Image` | Visualization | `Image` | 可视化图：Region1 蓝色 + Region2 红色半透明，并集绿色轮廓。 |
| `Area` | Union Area | `Integer` | 并集区域面积（像素数）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Region1Area` | `Integer` | 第一个输入区域的面积。 |
| `Region2Area` | `Integer` | 第二个输入区域的面积。 |
| `ProcessingTimeMs` | `Long` | 算子执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O((R1 + R2) log(R1 + R2))，主要为排序开销 |
| 典型耗时 (Typical Latency) | 平均 0.312 ms，最大 1.415 ms（100 组合成测试用例） |
| 内存特征 (Memory Profile) | O(R1 + R2)，存储合并后的游程列表 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：合并多个检测区域的前景掩码，如将分块检测结果拼合为完整区域。
- **适合 (Suitable)**：重连因阈值或分割操作而断裂的区域。
- **适合 (Suitable)**：布尔区域运算中的并集步骤。
- **不适合 (Not Suitable)**：需要保留语义实例标签的合并场景，因为输出为单一二值区域，标签信息会丢失。
- **不适合 (Not Suitable)**：需要精确面积重叠扣除的场景（并集不会去除重叠部分的重复面积）。

## 已知限制 / Known Limitations
1. 输入必须已经是 Region 对象；带标签的掩码会被展平为单一二值区域。
2. 可视化使用边界框相对坐标绘制，为诊断用途而非标定叠加。
3. 两个输入区域的坐标系必须一致。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充算法原理（RLE 合并排序 + 线性扫描合并公式）、实现策略（单次排序扫描）、详细输入输出端口说明、运行时附加输出、性能分析与限制说明 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
