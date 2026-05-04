# Region Opening / RegionOpening

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionOpeningOperator` |
| 枚举值 (Enum) | `OperatorType.RegionOpening` |
| 分类 (Category) | Morphology |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Opening operation (erosion followed by dilation) for noise removal and smooth region boundaries.。
> English: Opening operation (erosion followed by dilation) for noise removal and smooth region boundaries..

## 实现策略 / Implementation Strategy
> 中文：Runs one erosion pass followed by one dilation pass with the same discrete structuring element to suppress small foreground noise.。
> English: Runs one erosion pass followed by one dilation pass with the same discrete structuring element to suppress small foreground noise..

## 核心 API 调用链 / Core API Call Chain
- `MorphologyKernel.GetOffsets -> Erode -> Dilate -> Region`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `KernelShape` | `enum` | Rectangle | - | - |
| `KernelWidth` | `int` | 3 | [1, 99] | - |
| `KernelHeight` | `int` | 3 | [1, 99] | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region` | Input Region | `Any` | Yes | - |
| `Image` | Reference Image (Optional) | `Image` | No | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Opened Region | `Any` | - |
| `Image` | Visualization | `Image` | - |
| `Area` | Opened Area | `Integer` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(P*K*log Rrow + P' * K) |
| 典型耗时 (Typical Latency) | Avg 0.437 ms, max 3.141 ms over 100 synthetic golden cases |
| 内存特征 (Memory Profile) | O(P+P'+K) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Removing isolated foreground pixels or small protrusions while retaining larger region structure.
- 不适合 (Not Suitable)：Preserving tiny defects that are smaller than the selected structuring element.

## 已知限制 / Known Limitations
1. Opening can delete thin components or narrow bridges when the kernel is larger than the feature.
2. The operation uses a single erosion+dilation pair; repeated opening requires explicit workflow repetition.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
