# 区域差集 / Region Difference

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionDifferenceOperator` |
| 枚举值 (Enum) | `OperatorType.RegionDifference` |
| 分类 (Category) | Region |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | Region, Difference, Boolean, Subtract |
| 图标 (Icon) | region-difference |

## 算法原理 / Algorithm Principle
区域差集运算计算两个区域的集合差 A - B，即所有属于 A 但不属于 B 的像素：

```
Difference = {p | p in A and p not in B}
```

基于游程编码（RLE）逐行实现：对于 A 的每个游程，查找 B 中同一行的所有游程，按顺序减去重叠区间，保留剩余区间。具体地，对 A 的游程 `[s, e]` 和 B 的同行游程序列 `[s1, e1], [s2, e2], ...`：
- 若 B 无同行游程，整个 `[s, e]` 保留
- 否则，从 `currentStart = s` 开始，对每个 B 游程 `[si, ei]`：
  - 若 `si > currentStart`，输出 `[currentStart, si-1]`
  - 更新 `currentStart = max(currentStart, ei+1)`
- 最后若 `currentStart <= e`，输出 `[currentStart, e]`

> English: Computes A - B by scanning each run in A against sorted runs of B on the same row, preserving the non-overlapping X intervals.

## 实现策略 / Implementation Strategy
1. **逐行线性扫描**：对 Region1 的每个游程，通过 `Where(r => r.Y == run1.Y)` 筛选 Region2 的同 Y 行游程，再按 `StartX` 排序。
2. **分段减法**：对 Region1 的一个游程，依次与 Region2 同行游程比较，输出重叠前的剩余区间，跳过重叠部分。
3. **合并相邻游程**：最终通过 `Region.MergeAdjacentRuns()` 合并可能的相邻游程。
4. **无预索引**：当前实现未对 Region2 游程按行建立索引字典，每次通过 LINQ `Where` 筛选，对于碎片化区域可能有性能退化。

与 Halcon `difference` 算子对标。

> English: Uses row-by-row linear subtraction without pre-indexing Region2 runs. Suitable for typical inspection masks; extremely fragmented inputs should be profiled.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputRegion(inputs, "Region1")` / `TryGetInputRegion(inputs, "Region2")` -- 获取两个输入区域
2. `r1.RunLengths` -- 遍历 Region1 的所有游程
3. `r2.RunLengths.Where(r => r.Y == run1.Y).OrderBy(r => r.StartX)` -- 筛选 Region2 同行游程并排序
4. 分段减法：对每个 Region1 游程，逐个减去 Region2 同行游程的重叠区间
5. `new Region(diffRuns).MergeAdjacentRuns()` -- 构造差集区域并合并相邻游程
6. `CreateVisualization(r1, r2, diff)` -- 生成可视化（Region1 蓝色 + Region2 红色 + 差集绿色轮廓）
7. `CreateImageOutput(vis, { Region, Area, Region1Area, Region2Area, RemovedArea, ProcessingTimeMs })` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| - | - | - | - | 本算子无可配置参数。运算完全由两个输入区域决定。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region1` | First Region (Minuend) | `Any` | Yes | 被减区域（Minuend）。差集结果中的像素来自此区域。 |
| `Region2` | Second Region (Subtrahend) | `Any` | Yes | 减去区域（Subtrahend）。从此区域中移除与 Region1 重叠的部分。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Difference Region | `Any` | 差集区域（A - B）。 |
| `Image` | Visualization | `Image` | 可视化图：Region1 蓝色半透明，Region2 红色半透明，差集绿色轮廓。 |
| `Area` | Difference Area | `Integer` | 差集区域面积（像素数）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Region1Area` | `Integer` | 被减区域（Region1）的面积。 |
| `Region2Area` | `Integer` | 减去区域（Region2）的面积。 |
| `RemovedArea` | `Integer` | 被移除的面积（Region1Area - DifferenceArea）。 |
| `ProcessingTimeMs` | `Long` | 算子执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(R1 * R2) 最坏情况；当游程按行稀疏分布时更快 |
| 典型耗时 (Typical Latency) | 平均 0.267 ms，最大 2.112 ms（100 组合成测试用例） |
| 内存特征 (Memory Profile) | O(K)，其中 K 为输出游程数 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：从前景区域中移除忽略掩码（Ignore Mask）或夹具区域（Fixture Region）。
- **适合 (Suitable)**：布尔区域运算中的减法步骤，如从检测结果中排除已处理区域。
- **适合 (Suitable)**：ROI 裁剪后的区域清理，去除不需要的子区域。
- **不适合 (Not Suitable)**：需要保留不同源类别标签的语义级减法，因为输出为单一二值区域。
- **不适合 (Not Suitable)**：高碎片化掩码的高性能场景，当前实现未预索引 Region2 行。

## 已知限制 / Known Limitations
1. 当前实现未对 Region2 游程按行建立索引，每次通过 LINQ `Where` 筛选，密集碎片化掩码应做性能评测。
2. 输出为二值 Region，不保留源标签或置信度信息。
3. 两个输入区域的坐标系必须一致；若偏移不同，差集结果可能为空或不正确。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充算法原理（RLE 逐行分段减法公式）、实现策略（无预索引 LINQ 筛选）、详细输入输出端口说明、运行时附加输出、性能分析与限制说明 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
