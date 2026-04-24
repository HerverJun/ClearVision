# Contour Extrema / ContourExtrema

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ContourExtremaOperator` |
| 枚举值 (Enum) | `OperatorType.ContourExtrema` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Finds extremal points of a contour in specified directions.。
> English: Finds extremal points of a contour in specified directions..

## 实现策略 / Implementation Strategy
> 中文：Projects every contour point onto the selected axis or distance metric, then selects minimum and maximum points with stable tie-breaking for repeatable measurement output.。
> English: Projects every contour point onto the selected axis or distance metric, then selects minimum and maximum points with stable tie-breaking for repeatable measurement output..

## 核心 API 调用链 / Core API Call Chain
- `contour points -> scalar projection/distance -> deterministic extrema ordering -> visualization`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| - | - | - | - | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Contour` | Input Contour (Points) | `Any` | Yes | - |
| `Direction` | Search Direction | `String` | No | - |
| `ReferencePoint` | Reference Point (optional) | `Any` | No | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `ExtremaPoints` | Extremal Points | `Any` | - |
| `MinPoint` | Minimum Point | `Any` | - |
| `MaxPoint` | Maximum Point | `Any` | - |
| `Image` | Visualization | `Image` | - |
| `MinValue` | Minimum Value | `Float` | - |
| `MaxValue` | Maximum Value | `Float` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N log N) |
| 典型耗时 (Typical Latency) | Avg 0.244 ms, max 1.171 ms over 22 synthetic golden cases |
| 内存特征 (Memory Profile) | O(N) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Finding left/right, top/bottom, or nearest/farthest points on a known contour.
- 适合 (Suitable)：Stable downstream measurement where deterministic tie-breaking matters for collinear or duplicate extrema.
- 不适合 (Not Suitable)：Extracting the contour from an image; use FindContours before this operator.
- 不适合 (Not Suitable)：Subpixel contour fitting or curvature extrema estimation beyond the provided contour points.

## 已知限制 / Known Limitations
1. Direction defaults to horizontal for unknown direction strings.
2. Distance mode requires a reference point and reports Euclidean distance extrema only.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
