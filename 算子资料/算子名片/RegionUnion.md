# Region Union / RegionUnion

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionUnionOperator` |
| 枚举值 (Enum) | `OperatorType.RegionUnion` |
| 分类 (Category) | Region |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Computes the union of two regions (A ∪ B).。
> English: Computes the union of two regions (A ∪ B)..

## 实现策略 / Implementation Strategy
> 中文：Concatenates both input RLE run lists, sorts by row and start X, then merges overlapping or adjacent runs on the same row.。
> English: Concatenates both input RLE run lists, sorts by row and start X, then merges overlapping or adjacent runs on the same row..

## 核心 API 调用链 / Core API Call Chain
- `Region.RunLengths -> OrderBy(Y, StartX) -> MergeOverlappingRuns -> Region`

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
| `Region` | Union Region | `Any` | - |
| `Image` | Visualization | `Image` | - |
| `Area` | Union Area | `Integer` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O((R1+R2) log(R1+R2)) |
| 典型耗时 (Typical Latency) | Avg 0.312 ms, max 1.415 ms over 100 synthetic golden cases |
| 内存特征 (Memory Profile) | O(R1+R2) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Combining inspected foreground masks or reconnecting split region outputs before measurement.
- 不适合 (Not Suitable)：Semantic instance merging where labels or per-component identities must be preserved.

## 已知限制 / Known Limitations
1. Inputs must already be Region objects; labeled masks are flattened into one binary region.
2. Visualization uses bounding-box-relative drawing and is diagnostic rather than a calibrated overlay.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |