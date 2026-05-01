# ROI跟踪 / RoiTransform

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RoiTransformOperator` |
| 枚举值 (Enum) | `OperatorType.RoiTransform` |
| 分类 (Category) | 辅助 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Transforms a base ROI using match pose (CenterX/CenterY/Angle/Scale) and outputs SearchRegion.。
> English: Transforms a base ROI using match pose (CenterX/CenterY/Angle/Scale) and outputs SearchRegion..

## 实现策略 / Implementation Strategy
> 中文：Normalizes a match dictionary or indexed match sequence, resolves center, angle, and scale from supported match-result shapes, then transforms the base ROI around its center and emits the tracked SearchRegion rectangle.。
> English: Normalizes a match dictionary or indexed match sequence, resolves center, angle, and scale from supported match-result shapes, then transforms the base ROI around its center and emits the tracked SearchRegion rectangle..

## 核心 API 调用链 / Core API Call Chain
- `BaseRoi + selected match -> TryReadPose/TryNormalizeDictionary -> RoiTracker.TransformRoi`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `MatchIndex` | `int` | 0 | [0, 100] | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `BaseRoi` | Base ROI | `Rectangle` | Yes | - |
| `Matches` | Matches | `Any` | Yes | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `SearchRegion` | Search Region | `Rectangle` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(I+C) |
| 典型耗时 (Typical Latency) | No dedicated golden benchmark yet; covered by ROI tracker and caliper bridge tests |
| 内存特征 (Memory Profile) | O(C) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Passing shape-matching or planar-matching poses into downstream measurement operators as a tracked search ROI.
- 适合 (Suitable)：Translation, rotation, and scale adjustment of a known reference ROI between frames.
- 不适合 (Not Suitable)：Full multi-object tracking or selecting the best match by score inside this operator.
- 不适合 (Not Suitable)：Perspective or non-rigid ROI deformation where a rectangle bounding box is insufficient.

## 已知限制 / Known Limitations
1. Output is an integer bounding rectangle around the transformed ROI corners.
2. The operator does not clip the SearchRegion to image bounds and clamps non-positive scale values back to 1.0.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
