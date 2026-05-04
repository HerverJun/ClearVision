# Phase Closure / PhaseClosure

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PhaseClosureOperator` |
| 枚举值 (Enum) | `OperatorType.PhaseClosure` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Unwraps wrapped phase maps while preserving the original phase domain semantics.。
> English: Unwraps wrapped phase maps while preserving the original phase domain semantics..

## 实现策略 / Implementation Strategy
> 中文：Normalizes phase input to a wrapped float map, unwraps adjacent phase differences with Itoh, quality-guided, or flood-fill traversal, and emits the unwrapped phase plus discontinuity visualization.。
> English: Normalizes phase input to a wrapped float map, unwraps adjacent phase differences with Itoh, quality-guided, or flood-fill traversal, and emits the unwrapped phase plus discontinuity visualization..

## 核心 API 调用链 / Core API Call Chain
- `ImageWrapper -> wrapped CV_32F phase -> Itoh/quality/floodfill unwrap -> discontinuity map`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| - | - | - | - | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `PhaseImage` | Wrapped Phase Image | `Image` | Yes | - |
| `Wavelength` | Wavelength (nm) | `Float` | No | - |
| `UnwrapMethod` | Unwrapping Method | `String` | No | - |
| `QualityMap` | Quality Map (optional) | `Image` | No | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `UnwrappedPhase` | Unwrapped Phase | `Image` | - |
| `Discontinuities` | Phase Discontinuities | `Image` | - |
| `Quality` | Quality Metric | `Float` | - |
| `Image` | Visualization | `Image` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H log(W*H)) for quality-guided mode, O(W*H) for Itoh/floodfill |
| 典型耗时 (Typical Latency) | Avg 1.429 ms, max 4.590 ms over 22 synthetic golden cases |
| 内存特征 (Memory Profile) | O(W*H) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Smooth wrapped phase maps whose adjacent phase step stays within the unwrap assumptions.
- 适合 (Suitable)：Interferometry-style inspection where a discontinuity map and quality metric are needed with the unwrapped phase.
- 不适合 (Not Suitable)：Severely noisy phase maps without masking or preprocessing.
- 不适合 (Not Suitable)：Topology-heavy phase fields that require branch-cut optimization or domain-specific residue handling.

## 已知限制 / Known Limitations
1. Quality-guided mode uses a local gradient-derived quality map when no external map is provided.
2. The current output quality is a stability heuristic, not a calibrated metrology uncertainty.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
