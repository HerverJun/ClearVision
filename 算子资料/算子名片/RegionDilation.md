# Region Dilation / RegionDilation

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionDilationOperator` |
| 枚举值 (Enum) | `OperatorType.RegionDilation` |
| 分类 (Category) | Morphology |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Dilates a region using a specified structuring element (Region-based morphology).。
> English: Dilates a region using a specified structuring element (Region-based morphology)..

## 实现策略 / Implementation Strategy
> 中文：Applies every structuring-element offset to each source point, de-duplicates expanded points, then converts the expanded set back to RLE runs.。
> English: Applies every structuring-element offset to each source point, de-duplicates expanded points, then converts the expanded set back to RLE runs..

## 核心 API 调用链 / Core API Call Chain
- `MorphologyKernel.GetOffsets -> HashSet expanded points -> PointsToRuns`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `KernelShape` | `enum` | Rectangle | - | - |
| `KernelWidth` | `int` | 3 | [1, 99] | - |
| `KernelHeight` | `int` | 3 | [1, 99] | - |
| `Iterations` | `int` | 1 | [1, 100] | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region` | Input Region | `Any` | Yes | - |
| `Image` | Reference Image (Optional) | `Image` | No | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Dilated Region | `Any` | - |
| `Image` | Visualization | `Image` | - |
| `Area` | Dilated Area | `Integer` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(I*P*K + P' log P') |
| 典型耗时 (Typical Latency) | Avg 0.536 ms, max 6.379 ms over 100 synthetic golden cases |
| 内存特征 (Memory Profile) | O(P'+K) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Expanding foreground masks, closing small gaps before boolean operations, and adding pixel-domain tolerance to ROIs.
- 不适合 (Not Suitable)：Workflows that require automatic clipping to the original image extent unless an explicit downstream clip is added.

## 已知限制 / Known Limitations
1. Dilation can emit coordinates outside the original region or image domain by design.
2. Kernel shapes are discrete Rectangle/Ellipse/Cross rasterizations rather than analytic continuous geometry.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
