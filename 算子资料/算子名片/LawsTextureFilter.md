# Laws Texture Filter / LawsTextureFilter

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `LawsTextureFilterOperator` |
| 枚举值 (Enum) | `OperatorType.LawsTextureFilter` |
| 分类 (Category) | Texture |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Apply 5x5 Laws texture filtering and compute local energy.。
> English: Apply 5x5 Laws texture filtering and compute local energy..

## 实现策略 / Implementation Strategy
> 中文：Converts the image to normalized gray float data, optionally subtracts local mean illumination, applies the configured 5x5 separable Laws kernel pair, and computes a local energy image from squared filter response.。
> English: Converts the image to normalized gray float data, optionally subtracts local mean illumination, applies the configured 5x5 separable Laws kernel pair, and computes a local energy image from squared filter response..

## 核心 API 调用链 / Core API Call Chain
- `LawsTextureFilter.Apply -> OpenCV Filter2D -> LawsTextureFilter.ComputeEnergy -> local mean squared response`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `KernelCombo` | `string` | E5E5 | - | - |
| `SubtractLocalMean` | `bool` | true | - | - |
| `LocalMeanWindowSize` | `int` | 15 | [3, 101] | - |
| `EnergyWindowSize` | `int` | 15 | [3, 101] | - |
| `BorderType` | `enum` | 1 | - | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `FilteredImage` | Filtered Image | `Image` | - |
| `EnergyImage` | Energy Image | `Image` | - |
| `MeanEnergy` | Mean Energy | `Float` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H*(K^2+M^2+E^2)) |
| 典型耗时 (Typical Latency) | No dedicated golden benchmark yet; covered by texture unit and integration tests |
| 内存特征 (Memory Profile) | O(W*H) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Highlighting local texture energy for material, surface, or defect pre-screening.
- 适合 (Suitable)：Comparing fixed Laws kernel responses such as E5E5, E5L5, S5S5, W5W5, and R5R5.
- 不适合 (Not Suitable)：Semantic texture classification without downstream thresholds or model features.
- 不适合 (Not Suitable)：Images whose illumination drift cannot be corrected by local mean subtraction alone.

## 已知限制 / Known Limitations
1. Kernel combo must use the classic L/E/S/W/R 5-tap Laws codes.
2. Output energy depends on the selected window size and is not normalized across unrelated acquisition setups.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
