# Local Deformable Matching / LocalDeformableMatching

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `LocalDeformableMatchingOperator` |
| 枚举值 (Enum) | `OperatorType.LocalDeformableMatching` |
| 分类 (Category) | 匹配定位 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Experimental local deformable matching backed by moving least squares deformation and verified rigid fallback.。
> English: Experimental local deformable matching backed by moving least squares deformation and verified rigid fallback..

## 实现策略 / Implementation Strategy
> 中文：Generates template-match candidate windows, evaluates each candidate through coarse-to-fine ORB feature alignment, refines the control grid with moving-least-squares deformation, verifies occlusion and deformation limits, and applies NMS across accepted matches.。
> English: Generates template-match candidate windows, evaluates each candidate through coarse-to-fine ORB feature alignment, refines the control grid with moving-least-squares deformation, verifies occlusion and deformation limits, and applies NMS across accepted matches..

## 核心 API 调用链 / Core API Call Chain
- `candidate windows -> ORB pyramid matching -> homography seed -> MLS/TPS-style warp -> occlusion verification -> NMS`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `TemplatePath` | `file` | "" | - | - |
| `PyramidLevels` | `int` | 3 | [1, 6] | - |
| `TPSGridSize` | `int` | 4 | [2, 8] | - |
| `TPSLambda` | `double` | 0.01 | [0.001, 1] | - |
| `MaxDeformation` | `double` | 20 | [5, 100] | - |
| `OcclusionThreshold` | `double` | 0.3 | [0.1, 0.9] | - |
| `MinMatchScore` | `double` | 0.6 | [0, 1] | - |
| `EnableFallback` | `bool` | false | - | - |
| `MaxIterations` | `int` | 5 | [1, 20] | - |
| `ConvergenceThreshold` | `double` | 0.5 | [0.1, 5] | - |
| `MaxMatches` | `int` | 5 | [1, 20] | - |
| `CandidateThreshold` | `double` | 0.65 | [0.1, 1] | - |
| `EnableNms` | `bool` | true | - | - |
| `NmsThreshold` | `double` | 0.35 | [0, 1] | - |
| `ParallelCandidates` | `bool` | true | - | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Search Image | `Image` | Yes | - |
| `Template` | Template Image | `Image` | No | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Result Image | `Image` | - |
| `MatchResult` | Match Result | `Any` | - |
| `Matches` | Match List | `Any` | - |
| `MatchCount` | Match Count | `Integer` | - |
| `DeformationField` | Deformation Field | `Any` | - |
| `OcclusionMask` | Occlusion Mask | `Image` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(C*L*(F+M) + C*G*I*P) |
| 典型耗时 (Typical Latency) | No dedicated golden benchmark yet; covered by deformable matching operator tests |
| 内存特征 (Memory Profile) | O(W*H + C*G + F) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Textured templates that may undergo local deformation, mild occlusion, or multiple target instances.
- 适合 (Suitable)：Workflows that need deformation field, occlusion mask, and rigid fallback diagnostics in addition to match score.
- 不适合 (Not Suitable)：Blank or low-texture templates where ORB feature support is insufficient.
- 不适合 (Not Suitable)：Real-time high-throughput matching without constraining candidate count, pyramid levels, and deformation grid size.

## 已知限制 / Known Limitations
1. The implementation uses MLS-style deformation under the legacy TPS parameter names.
2. Candidate generation still starts from normalized template matching, so strong repetitive backgrounds can require ROI constraints or higher thresholds.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.1.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
