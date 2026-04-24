# Arc Caliper / ArcCaliper

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ArcCaliperOperator` |
| 枚举值 (Enum) | `OperatorType.ArcCaliper` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Detects edges along an arc path with subpixel accuracy.。
> English: Detects edges along an arc path with subpixel accuracy..

## 实现策略 / Implementation Strategy
> 中文：Samples one radial band profile per arc angle, applies polarity-aware edge detection on the profile, and converts the strongest edge position back to subpixel image coordinates.。
> English: Samples one radial band profile per arc angle, applies polarity-aware edge detection on the profile, and converts the strongest edge position back to subpixel image coordinates..

## 核心 API 调用链 / Core API Call Chain
- `arc sampling -> IndustrialCaliperKernel.SampleBandProfile -> DetectEdges -> InterpolatePosition`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| - | - | - | - | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Input Image | `Image` | Yes | - |
| `CenterX` | Arc Center X | `Integer` | Yes | - |
| `CenterY` | Arc Center Y | `Integer` | Yes | - |
| `Radius` | Arc Radius | `Integer` | Yes | - |
| `StartAngle` | Start Angle (deg) | `Float` | No | - |
| `EndAngle` | End Angle (deg) | `Float` | No | - |
| `Transition` | Transition Type | `String` | No | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Points` | Detected Edge Points | `Any` | - |
| `Image` | Visualization | `Image` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(A*S) |
| 典型耗时 (Typical Latency) | Avg 4.169 ms, max 5.747 ms over 31 synthetic golden cases |
| 内存特征 (Memory Profile) | O(S+P) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Measuring circular or annular edges when center, radius, and angular search span are already constrained.
- 不适合 (Not Suitable)：Discovering unknown circles without a prior center/radius estimate.
- 不适合 (Not Suitable)：Low-texture arcs where no edge response should be treated as a low-confidence measurement.

## 已知限制 / Known Limitations
1. The current scan step is fixed at one degree, so very short arcs may need a tighter dedicated measurement operator.
2. The output reports detected points and count, but does not yet expose per-point uncertainty or an explicit no-edge failure status.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
