# ORB特征匹配 / OrbFeatureMatch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `OrbFeatureMatchOperator` |
| 枚举值 (Enum) | `OperatorType.OrbFeatureMatch` |
| 分类 (Category) | 匹配定位 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：ORB feature matching with homography verification for fast template localization.。
> English: ORB feature matching with homography verification for fast template localization..

## 实现策略 / Implementation Strategy
> 中文：Extract ORB descriptors from the scene and template, filter candidate matches with ratio and optional symmetry tests, verify geometry with a RANSAC homography, then emit Position, MatchPoint, Score, and NG diagnostics.。
> English: Extract ORB descriptors from the scene and template, filter candidate matches with ratio and optional symmetry tests, verify geometry with a RANSAC homography, then emit Position, MatchPoint, Score, and NG diagnostics..

## 核心 API 调用链 / Core API Call Chain
- `OpenCvSharp.ORB + BFMatcher(Hamming) + FindHomography(RANSAC)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `TemplatePath` | `file` | "" | - | - |
| `MaxFeatures` | `int` | 500 | [100, 2000] | - |
| `ScaleFactor` | `double` | 1.2 | [1, 2] | - |
| `NLevels` | `int` | 8 | [1, 12] | - |
| `EdgeThreshold` | `int` | 31 | [3, 100] | - |
| `EnableSymmetryTest` | `bool` | true | - | - |
| `MinMatchCount` | `int` | 10 | [3, 100] | - |
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
| 典型耗时 (Typical Latency) | FeatureMatchContractRunner baseline: 22 cases passed, avg runtime about 7.6 ms on synthetic contract images. |
| 内存特征 (Memory Profile) | O(P + T + S) plus bounded static template cache entries for TemplatePath mode. |

## 适用场景 / Use Cases
- 适合 (Suitable)：Realtime feature-based localization when the template has enough repeatable ORB corners.
- 适合 (Suitable)：Moderate rotation and small scale changes where a homography can explain the target pose.
- 适合 (Suitable)：Contract-driven pipelines that use IsMatch and FailureReason rather than execution status alone.
- 不适合 (Not Suitable)：Low-texture or repetitive-pattern templates that produce unstable descriptor matches.
- 不适合 (Not Suitable)：Precision measurement workflows that require a calibrated target center or subpixel edge result.
- 不适合 (Not Suitable)：Scenes where a large number of background features should be searched without ROI or threshold tuning.

## 已知限制 / Known Limitations
1. Score is a homography verification score based on inlier evidence, not a descriptor distance.
2. The inlier-ratio gate is fixed in code, while MinMatchCount and ORB detector settings are configurable.
3. TemplatePath mode uses a bounded in-process cache keyed by file fingerprint and detector configuration.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-04-28 | 自动生成文档骨架 / Generated skeleton |
