# 局部可变形匹配 / LocalDeformableMatching

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `LocalDeformableMatchingOperator` |
| 枚举值 (Enum) | `OperatorType.LocalDeformableMatching` |
| 分类 ID (CategoryId) | `MatchingAndLocalization` |
| 分类 (Category) | 匹配与定位 |
| 分类顺序 (CategoryOrder) | 5 |
| 版本 (Version) | `1.1.1` |
| 生命周期 (Lifecycle) | 实验 `Experimental` |
| 生命周期说明 (Lifecycle Note) | TPS 局部形变与多候选搜索为实验能力，尚需目标域数据验证鲁棒性和性能。 |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | Yes |
| Execution | `Implemented` |
| AlgorithmQuality | `Unknown` |
| ProductionReadiness | `Experimental` |
| FieldValidation | `NotValidated` |
| Quality Evidence Refs |  |
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Experimental`, `分类:MatchingAndLocalization`, `分类显示:匹配与定位`, `生命周期:Experimental`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于实验性局部可变形匹配，基于移动最小二乘形变估计，并提供刚性匹配校验回退。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Coarse-to-fine local deformable matching` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Template`。
- 参数解析覆盖 15 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `candidate windows -> ORB pyramid matching -> homography seed -> MLS/TPS-style warp -> occlusion verification -> NMS`
- `OperatorBase.Get*Param(...)`
- `Cv2.PerspectiveTransform`
- `Cv2.MatchTemplate`
- `Cv2.MinMaxLoc`
- `Cv2.CvtColor`
- `Cv2.PyrDown`
- `Cv2.Absdiff`
- `Cv2.Threshold`
- `Cv2.BitwiseAnd`
- `Cv2.BitwiseNot`
- `Cv2.CountNonZero`
- `Cv2.ImRead`
- `Cv2.Line`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `TemplatePath` | 模板图像路径 | `file` | "" | - | Yes | - |
| `PyramidLevels` | 金字塔层级 | `int` | 3 | [1, 6] | Yes | - |
| `TPSGridSize` | Control Grid Size | `int` | 4 | [2, 8] | Yes | - |
| `TPSLambda` | MLS Smoothing (Legacy TPSLambda) | `double` | 0.01 | [0.001, 1] | Yes | - |
| `MaxDeformation` | 最大形变（px） | `double` | 20 | [5, 100] | Yes | - |
| `OcclusionThreshold` | 遮挡阈值 | `double` | 0.3 | [0.1, 0.9] | Yes | - |
| `MinMatchScore` | 最小匹配分数 | `double` | 0.6 | [0, 1] | Yes | - |
| `EnableFallback` | 启用刚性回退 | `bool` | false | - | Yes | - |
| `MaxIterations` | 最大细化迭代次数 | `int` | 5 | [1, 20] | Yes | - |
| `ConvergenceThreshold` | 收敛阈值 | `double` | 0.5 | [0.1, 5] | Yes | - |
| `MaxMatches` | 最大匹配数量 | `int` | 5 | [1, 20] | Yes | - |
| `CandidateThreshold` | 候选种子阈值 | `double` | 0.65 | [0.1, 1] | Yes | - |
| `EnableNms` | 启用NMS | `bool` | true | - | Yes | - |
| `NmsThreshold` | NMS IoU阈值 | `double` | 0.35 | [0, 1] | Yes | - |
| `ParallelCandidates` | 并行候选评估 | `bool` | true | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Search Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Template` | Template Image | `Image` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Result Image | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `MatchResult` | Match Result | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Matches` | Match List | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `MatchCount` | Match Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `DeformationField` | Deformation Field | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `OcclusionMask` | Occlusion Mask | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:2, Rejected:2, Unknown:0 | Verified production support is present. | CV_8U | CV_8U | 1, 3 | Verified local deformable matching input domain. | BGR inputs are converted to grayscale for ORB and template scoring. | 8-bit visualization, deformation, and occlusion evidence. | Legacy 0..255 intensity domain. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |
| `Template` | Allowed:2, Rejected:2, Unknown:0 | Verified production support is present. | CV_8U | CV_8U | 1, 3 | Verified local deformable matching input domain. | BGR inputs are converted to grayscale for ORB and template scoring. | 8-bit visualization, deformation, and occlusion evidence. | Legacy 0..255 intensity domain. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Default | CV_8UC1 | ORB/template/warp paths are verified for 8-bit grayscale and BGR inputs. | `Allowed` | `VerifiedSupport` | C3 -> Gray for feature and score computation; no depth scaling. | Visualization and occlusion outputs remain 8-bit. | Legacy 0..255 matching intensity domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_STAGE12_REGRESSION` |
| `Image` | Default | CV_8UC3 | ORB/template/warp paths are verified for 8-bit grayscale and BGR inputs. | `Allowed` | `VerifiedConversion` | C3 -> Gray for feature and score computation; no depth scaling. | Visualization and occlusion outputs remain 8-bit. | Legacy 0..255 matching intensity domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_STAGE12_REGRESSION` |
| `Image` | Default | CV_8UC2, CV_8UC4 | Unsupported image channel count for local deformable matching. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_CHANNELS_UNSUPPORTED` | `E2_STAGE12_REGRESSION` |
| `Template` | Default | CV_8UC1 | ORB/template/warp paths are verified for 8-bit grayscale and BGR inputs. | `Allowed` | `VerifiedSupport` | C3 -> Gray for feature and score computation; no depth scaling. | Visualization and occlusion outputs remain 8-bit. | Legacy 0..255 matching intensity domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_STAGE12_REGRESSION` |
| `Template` | Default | CV_8UC3 | ORB/template/warp paths are verified for 8-bit grayscale and BGR inputs. | `Allowed` | `VerifiedConversion` | C3 -> Gray for feature and score computation; no depth scaling. | Visualization and occlusion outputs remain 8-bit. | Legacy 0..255 matching intensity domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_STAGE12_REGRESSION` |
| `Template` | Default | CV_8UC2, CV_8UC4 | Unsupported image channel count for local deformable matching. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_CHANNELS_UNSUPPORTED` | `E2_STAGE12_REGRESSION` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`70B3157E202F7B7CA0CC97B0EF8BE03F6005BDF9ED1E4BAEA8CAA03012994F8A`
- `type:ClearVision.Product.Infrastructure.Operators.LocalDeformableMatchingImageContractProvider`

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `BoundingBox` | `Any` | 源码输出字典初始化中可见字段。 |
| `CandidateScore` | `Float` | 源码输出字典初始化中可见字段。 |
| `ControlPoints` | `Any` | 源码输出字典初始化中可见字段。 |
| `Corners` | `Any` | 源码输出字典初始化中可见字段。 |
| `DeformationMagnitude` | `Any` | 源码输出字典初始化中可见字段。 |
| `DeformationModel` | `String` | 源码输出字典初始化中可见字段。 |
| `FailureReason` | `String` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `InlierCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `InlierRatio` | `Float` | 源码输出字典初始化中可见字段。 |
| `IsFallback` | `Boolean` | 源码输出字典初始化中可见字段。 |
| `IsMatch` | `Boolean` | 源码输出字典初始化中可见字段。 |
| `LegacyParameterCompatibility` | `Any` | 源码输出字典初始化中可见字段。 |
| `Message` | `String` | 源码输出字典初始化中可见字段。 |
| `Method` | `Any` | 源码输出字典初始化中可见字段。 |
| `OcclusionRate` | `Any` | 源码输出字典初始化中可见字段。 |
| `OriginalFailureReason` | `String` | 源码输出字典初始化中可见字段。 |
| `ProcessingTimeMs` | `Any` | 源码输出字典初始化中可见字段。 |
| `Score` | `Float` | 源码输出字典初始化中可见字段。 |
| `VerificationPassed` | `Boolean` | 源码输出字典初始化中可见字段。 |
| `VerificationScore` | `Float` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(C*L*(F+M) + C*G*I*P) |
| 典型耗时 (Typical Latency) | No dedicated golden benchmark yet; covered by deformable matching operator tests |
| 内存特征 (Memory Profile) | O(W*H + C*G + F) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 2 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Textured templates that may undergo local deformation, mild occlusion, or multiple target instances.
- 适合 (Suitable)：Workflows that need deformation field, occlusion mask, and rigid fallback diagnostics in addition to match score.
- 不适合 (Not Suitable)：Blank or low-texture templates where ORB feature support is insufficient.
- 不适合 (Not Suitable)：Real-time high-throughput matching without constraining candidate count, pyramid levels, and deformation grid size.

## 已知限制 / Known Limitations
1. The implementation uses MLS-style deformation under the legacy TPS parameter names.
2. Candidate generation still starts from normalized template matching, so strong repetitive backgrounds can require ROI constraints or higher thresholds.
3. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
4. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
5. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
6. 外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.1.1 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
