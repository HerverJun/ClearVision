# 金字塔形状匹配 / PyramidShapeMatch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PyramidShapeMatchOperator` |
| 枚举值 (Enum) | `OperatorType.PyramidShapeMatch` |
| 分类 (Category) | 匹配定位 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：基于 LINEMOD 的金字塔模板匹配。。
> English: 基于 LINEMOD 的金字塔模板匹配。.

## 实现策略 / Implementation Strategy
> 中文：Train either a LINEMOD-style template matcher or a contour descriptor matcher from the provided template, search the scene with configured pyramid, angle, and feature parameters, and return the best match plus diagnostics.。
> English: Train either a LINEMOD-style template matcher or a contour descriptor matcher from the provided template, search the scene with configured pyramid, angle, and feature parameters, and return the best match plus diagnostics..

## 核心 API 调用链 / Core API Call Chain
- `TemplateMatcher or ShapeDescriptorMatcher over OpenCvSharp Mats`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `TemplatePath` | `file` | "" | - | - |
| `MinScore` | `double` | 80 | [0, 100] | - |
| `AngleRange` | `int` | 180 | [0, 180] | - |
| `AngleStep` | `int` | 5 | [1, 45] | - |
| `PyramidLevels` | `int` | 3 | [1, 5] | - |
| `MagnitudeThreshold` | `int` | 30 | [0, 255] | - |
| `WeakThreshold` | `double` | 30 | [0, 255] | - |
| `StrongThreshold` | `double` | 60 | [0, 255] | - |
| `NumFeatures` | `int` | 150 | [50, 8191] | - |
| `SpreadT` | `int` | 4 | [1, 16] | - |
| `MaxMatches` | `int` | 10 | [1, 100] | - |
| `MatchMode` | `enum` | Template | - | - |
| `DescriptorTypes` | `enum` | Hu+Fourier | - | - |
| `PreFilterArea` | `bool` | true | - | - |
| `AreaTolerance` | `double` | 0.3 | [0, 1] | - |

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

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | Template mode is roughly O(L*A*P) for pyramid levels, angle samples, and searched pixels; descriptor mode depends on contour count. |
| 典型耗时 (Typical Latency) | PyramidShapeMatchContractRunner baseline: 24 cases passed, avg runtime about 4.4 ms on synthetic contract images. |
| 内存特征 (Memory Profile) | O(P + F) for image pyramids, gradient maps, template features, and candidate match diagnostics. |

## 适用场景 / Use Cases
- 适合 (Suitable)：Shape-led template localization where edge orientation is more stable than raw grayscale intensity.
- 适合 (Suitable)：Coarse positioning or presence checks that can consume score, angle, match count, and matcher diagnostics.
- 适合 (Suitable)：Comparing Template and ShapeDescriptor modes under controlled ROI and threshold settings.
- 不适合 (Not Suitable)：Weak-edge templates or scenes where the trained template has too few stable gradient features.
- 不适合 (Not Suitable)：Dense multi-object retrieval that requires a fully ranked candidate list beyond the current primary output contract.
- 不适合 (Not Suitable)：Subpixel metrology tasks where a dedicated edge or caliper operator should own the measurement contract.

## 已知限制 / Known Limitations
1. Template mode and ShapeDescriptor mode use different position semantics; downstream flows should consume MatcherDiagnostics.
2. The baseline locks current allowed-position tolerance before a stricter center contract is introduced.
3. MagnitudeThreshold is exposed for template mode and should be tuned together with weak and strong thresholds.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-28 | Backfilled PyramidShapeMatchContractRunner evidence (24/24 passed), Template/ShapeDescriptor modes and failure contract notes |
| 1.0.0 | 2026-04-28 | 自动生成文档骨架 / Generated skeleton |
