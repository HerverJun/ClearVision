# 梯度形状匹配 / GradientShapeMatch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `GradientShapeMatchOperator` |
| 枚举值 (Enum) | `OperatorType.GradientShapeMatch` |
| 分类 (Category) | 匹配定位 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：基于梯度方向特征的形状匹配，支持可选 ROI 搜索。。
> English: 基于梯度方向特征的形状匹配，支持可选 ROI 搜索。.

## 实现策略 / Implementation Strategy
> 中文：Train a bank of rotated gradient templates by quantizing edge directions into 8 bins. Match scene positions by directional agreement ratio. Supports TopK multi-match output with position-based NMS, optional ROI search, and SHA256-based template cache with LRU eviction.。
> English: Train a bank of rotated gradient templates by quantizing edge directions into 8 bins. Match scene positions by directional agreement ratio. Supports TopK multi-match output with position-based NMS, optional ROI search, and SHA256-based template cache with LRU eviction..

## 核心 API 调用链 / Core API Call Chain
- `Custom GradientShapeMatcher (OpenCvSharp.Mat gradient computation, 8-bin direction quantization, coarse-to-fine peak search with per-template NMS)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `TemplatePath` | `file` | "" | - | - |
| `MinScore` | `double` | 80 | [0, 100] | - |
| `TopK` | `int` | 1 | [1, 10] | - |
| `AngleRange` | `int` | 180 | [0, 180] | - |
| `AngleStep` | `int` | 1 | [1, 10] | - |
| `MagnitudeThreshold` | `int` | 30 | [0, 255] | - |
| `EnableCache` | `bool` | true | - | - |
| `UseRoi` | `bool` | false | - | - |
| `RoiX` | `int` | 0 | [0, 100000] | - |
| `RoiY` | `int` | 0 | [0, 100000] | - |
| `RoiWidth` | `int` | 0 | [0, 100000] | - |
| `RoiHeight` | `int` | 0 | [0, 100000] | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 搜索图像 | `Image` | Yes | - |
| `Template` | 模板图像 | `Image` | No | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | - |
| `Position` | 匹配位置 | `Point` | - |
| `Angle` | 旋转角度 | `Float` | - |
| `IsMatch` | 是否匹配 | `Boolean` | - |
| `Score` | 匹配分数 | `Float` | - |
| `Matches` | 匹配列表 | `Any` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(T * R * S) where T is template feature count, R is rotated template count, and S is scene pixels under search |
| 典型耗时 (Typical Latency) | GradientShapeMatchGoldenRunner baseline: 130 cases passed, avg runtime about 92 ms on 512x384 synthetic images. |
| 内存特征 (Memory Profile) | O(R * T) for rotated template storage plus bounded LRU cache (max 8 entries) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Edge-defined object localization under moderate lighting changes.
- 适合 (Suitable)：Rotation-invariant matching when target has clear gradient structure and limited symmetry.
- 适合 (Suitable)：Multi-instance detection with TopK output and position NMS.
- 不适合 (Not Suitable)：Low-texture or blank templates that yield fewer than 10 gradient features.
- 不适合 (Not Suitable)：Scenes with heavy scale variation (fixed-scale template matching only).
- 不适合 (Not Suitable)：Sub-pixel precision measurement workflows.

## 已知限制 / Known Limitations
1. Score is a directional agreement ratio (matching features / total template features) x 100, not a correlation coefficient.
2. Template cache is bounded to 8 entries with LRU eviction.
3. Low-feature templates (< 10 valid gradient features) return structured FailureReason=InvalidTemplate.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.1.0 | 2026-04-28 | 自动生成文档骨架 / Generated skeleton |
