# Region Intersection / RegionIntersection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionIntersectionOperator` |
| 枚举值 (Enum) | `OperatorType.RegionIntersection` |
| 分类 (Category) | Region |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Computes the intersection of two regions (A ∩ B).。
> English: Computes the intersection of two regions (A ∩ B)..

## 实现策略 / Implementation Strategy
> 中文：Scans Region1 runs, compares them with Region2 runs on the same row, and emits overlapping X intervals as the intersection.。
> English: Scans Region1 runs, compares them with Region2 runs on the same row, and emits overlapping X intervals as the intersection..

## 核心 API 调用链 / Core API Call Chain
- `Region.RunLengths -> same-row run overlap -> MergeAdjacentRuns -> Region`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| - | - | - | - | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region1` | First Region | `Any` | Yes | - |
| `Region2` | Second Region | `Any` | Yes | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Intersection Region | `Any` | - |
| `Image` | Visualization | `Image` | - |
| `Area` | Intersection Area | `Integer` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(R1*R2) worst case, lower when runs are sparse by row |
| 典型耗时 (Typical Latency) | Avg 0.209 ms, max 1.402 ms over 100 synthetic golden cases |
| 内存特征 (Memory Profile) | O(K) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Measuring overlap between binary inspection regions, masks, or ROI-derived foreground regions.
- 不适合 (Not Suitable)：High-fragmentation masks that need guaranteed indexed-row performance without profiling.

## 已知限制 / Known Limitations
1. The current implementation performs a simple same-row lookup per Region1 run, so dense fragmented masks should be profiled.
2. Only binary region overlap is represented; source labels and confidence values are not preserved.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
