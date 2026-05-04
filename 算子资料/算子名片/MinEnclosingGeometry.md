# Min Enclosing Geometry / MinEnclosingGeometry

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MinEnclosingGeometryOperator` |
| 枚举值 (Enum) | `OperatorType.MinEnclosingGeometry` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Computes minimum enclosing geometry (circle, rectangle, triangle) and robust arc fitting with RANSAC.。
> English: Computes minimum enclosing geometry (circle, rectangle, triangle) and robust arc fitting with RANSAC..

## 实现策略 / Implementation Strategy
> 中文：Segments the input image into external contours, selects contour points by the requested policy, then computes the selected enclosing or fitting geometry and overlays the result on the source image.。
> English: Segments the input image into external contours, selects contour points by the requested policy, then computes the selected enclosing or fitting geometry and overlays the result on the source image..

## 核心 API 调用链 / Core API Call Chain
- `Threshold -> FindContours -> contour selection -> MinEnclosingCircle/MinAreaRect/ConvexHull/RANSAC fit`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Operation` | `enum` | SmallestCircle | - | - |
| `Threshold` | `double` | 127 | [0, 255] | - |
| `MinArea` | `int` | 100 | >= 0 | - |
| `ContourSelection` | `enum` | LargestContour | - | - |
| `RansacIterations` | `int` | 500 | [10, 5000] | - |
| `RansacInlierThreshold` | `double` | 2 | [0.1, 50] | - |
| `MinArcAngle` | `double` | 30 | [5, 350] | - |
| `MaxArcAngle` | `double` | 330 | [10, 360] | - |
| `OutlierRatio` | `double` | 0.3 | [0, 0.9] | - |
| `CheckConditionNumber` | `bool` | true | - | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Input Image | `Image` | Yes | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Result Image | `Image` | - |
| `GeometryResult` | Geometry Result | `Any` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H + P log P + I*P) |
| 典型耗时 (Typical Latency) | No dedicated golden benchmark yet; covered by geometry operator tests |
| 内存特征 (Memory Profile) | O(W*H + P) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Measuring minimum enclosing circle, rotated rectangle, triangle, or convex hull for segmented parts.
- 适合 (Suitable)：Fitting circles, arcs, or ellipses when contour points are available and outliers are expected.
- 不适合 (Not Suitable)：Low-contrast scenes where threshold segmentation does not isolate the target contour.
- 不适合 (Not Suitable)：Metrology that requires calibrated subpixel edge extraction before geometry fitting.

## 已知限制 / Known Limitations
1. Contour extraction is threshold-based and uses external contours only.
2. Robust arc and circle fitting depend on RANSAC iteration and inlier-threshold parameters.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
