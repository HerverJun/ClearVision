# AKAZE特征匹配 / AkazeFeatureMatch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `AkazeFeatureMatchOperator` |
| 枚举值 (Enum) | `OperatorType.AkazeFeatureMatch` |
| 分类 (Category) | 匹配定位 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：AKAZE feature matching with verified homography gating for robust template localization.。
> English: AKAZE feature matching with verified homography gating for robust template localization..

## 实现策略 / Implementation Strategy
> 中文：Extract AKAZE binary features from the scene and template, optionally apply bidirectional symmetry filtering, estimate a RANSAC homography, and report both the configured reference Position and representative MatchPoint.。
> English: Extract AKAZE binary features from the scene and template, optionally apply bidirectional symmetry filtering, estimate a RANSAC homography, and report both the configured reference Position and representative MatchPoint..

## 核心 API 调用链 / Core API Call Chain
- `OpenCvSharp.AKAZE + BFMatcher(Hamming) + FindHomography(RANSAC)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `TemplatePath` | `file` | "" | - | - |
| `Threshold` | `double` | 0.001 | [0.0001, 0.1] | - |
| `MinMatchCount` | `int` | 10 | [3, 100] | - |
| `EnableSymmetryTest` | `bool` | true | - | - |
| `MaxFeatures` | `int` | 500 | [100, 2000] | - |
| `OriginMode` | `enum` | Center | - | - |
| `OriginX` | `double` | 0 | - | - |
| `OriginY` | `double` | 0 | - | - |

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
| `MatchPoint` | 代表匹配点 | `Point` | - |
| `IsMatch` | 是否匹配 | `Boolean` | - |
| `Score` | 匹配分数 | `Float` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(P + T*S) where P is image pixels and T/S are retained template and scene descriptors |
| 典型耗时 (Typical Latency) | FeatureMatchContractRunner baseline: 22 cases passed, avg runtime about 11.7 ms on synthetic contract images. |
| 内存特征 (Memory Profile) | O(P + T + S) plus bounded static template cache entries for TemplatePath mode. |

## 适用场景 / Use Cases
- 适合 (Suitable)：Textured labels, PCB marks, printed features, and local parts with enough corners or blob-like texture.
- 适合 (Suitable)：Template localization where moderate rotation, scale, or perspective variation is expected.
- 适合 (Suitable)：Pipelines that need a business-level NG result image instead of a framework-level failure for no-match cases.
- 不适合 (Not Suitable)：Weak-texture, pure-color, or strongly repetitive targets where homography inliers are ambiguous.
- 不适合 (Not Suitable)：Subpixel metrology or robot-pick centers that require calibrated geometric center output.
- 不适合 (Not Suitable)：Very high-texture full-frame scenes without ROI constraints, because scene descriptors are not globally capped.

## 已知限制 / Known Limitations
1. Score is a homography verification score based on inlier evidence, not a normalized template-correlation score.
2. Ratio-test and RANSAC thresholds remain fixed in code; only MinMatchCount and symmetry filtering are exposed.
3. TemplatePath mode uses a bounded in-process cache keyed by file fingerprint and detector configuration.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-04-28 | 自动生成文档骨架 / Generated skeleton |
