# 区域交集 / Region Intersection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionIntersectionOperator` |
| 枚举值 (Enum) | `OperatorType.RegionIntersection` |
| 分类 (Category) | Region |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | Region, Intersection, Boolean, Overlap |
| 图标 (Icon) | region-intersection |

## 算法原理 / Algorithm Principle
区域交集运算计算两个区域的交集 A ∩ B，即同时属于 A 和 B 的所有像素：

```
Intersection = {p | p in A and p in B}
```

基于游程编码（RLE）逐行实现：对 Region1 的每个游程 `[s1, e1]`，查找 Region2 中同一行 Y 的所有游程 `[s2, e2]`，计算 X 区间重叠：

```
overlap_start = max(s1, s2)
overlap_end   = min(e1, e2)
if overlap_start <= overlap_end:
    output RunLength(Y, overlap_start, overlap_end)
```

最终对所有重叠游程合并相邻段。

> English: Computes A ∩ B by finding overlapping X intervals between runs of both regions on the same row Y.

## 实现策略 / Implementation Strategy
1. **逐行线性扫描**：对 Region1 的每个游程，通过 `Where(r => r.Y == run1.Y)` 筛选 Region2 的同 Y 行游程。
2. **区间重叠计算**：对每对同行游程计算 `Math.Max` 和 `Math.Min` 得到重叠区间，若 `start <= end` 则输出。
3. **合并相邻游程**：最终通过 `Region.MergeAdjacentRuns()` 合并可能的相邻游程。
4. **无预索引**：当前实现未对 Region2 游程按行建立索引字典。

与 Halcon `intersection` 算子对标。

> English: Scans Region1 runs, compares with Region2 runs on the same row, and emits overlapping X intervals. No row pre-indexing is used.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputRegion(inputs, "Region1")` / `TryGetInputRegion(inputs, "Region2")` -- 获取两个输入区域
2. `r1.RunLengths` -- 遍历 Region1 的所有游程
3. `r2.RunLengths.Where(r => r.Y == run1.Y)` -- 筛选 Region2 同行游程
4. `Math.Max(run1.StartX, run2.StartX)` / `Math.Min(run1.EndX, run2.EndX)` -- 计算重叠区间
5. `new Region(intersectRuns).MergeAdjacentRuns()` -- 构造交集区域并合并相邻游程
6. `CreateVisualization(r1, r2, inter)` -- 生成可视化（Region1 蓝色 + Region2 红色 + 交集绿色填充）
7. `CreateImageOutput(vis, { Region, Area, Region1Area, Region2Area, OverlapRatio, ProcessingTimeMs })` -- 封装输出

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
| `Region` | Intersection Region | `Any` | 交集区域（A ∩ B）。 |
| `Image` | Visualization | `Image` | 可视化图：Region1 蓝色 + Region2 红色 + 交集绿色填充。 |
| `Area` | Intersection Area | `Integer` | 交集区域面积（像素数）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Region1Area` | `Integer` | 第一个输入区域的面积。 |
| `Region2Area` | `Integer` | 第二个输入区域的面积。 |
| `OverlapRatio` | `Double` | 重叠率：交集面积 / min(Region1Area, Region2Area)。当其中一个区域面积为 0 时为 0。 |
| `ProcessingTimeMs` | `Long` | 算子执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(R1 * R2) 最坏情况；当游程按行稀疏分布时更快 |
| 典型耗时 (Typical Latency) | 平均 0.209 ms，最大 1.402 ms（100 组合成测试用例） |
| 内存特征 (Memory Profile) | O(K)，其中 K 为输出游程数 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：测量两个二值检测区域的重叠部分，如掩码与 ROI 的交集。
- **适合 (Suitable)**：验证检测区域是否落在有效范围内（通过与有效区域求交）。
- **适合 (Suitable)**：布尔区域运算中的交集步骤，提取两个区域的共同部分。
- **不适合 (Not Suitable)**：高碎片化掩码且需要保证行索引性能的场景（当前未预建行索引）。
- **不适合 (Not Suitable)**：需要保留源标签或置信度的语义级交集运算。

## 已知限制 / Known Limitations
1. 当前实现对 Region1 的每个游程执行简单同行查找，密集碎片化掩码应做性能评测。
2. 仅表示二值区域重叠，不保留源标签或置信度值。
3. 两个输入区域的坐标系必须一致。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充算法原理（RLE 区间重叠公式）、实现策略（无预索引同行查找）、详细输入输出端口说明、运行时附加输出（OverlapRatio）、性能分析与限制说明 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
