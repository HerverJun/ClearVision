# AKAZE特征匹配 / AkazeFeatureMatch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `AkazeFeatureMatchOperator` |
| 枚举值 (Enum) | `OperatorType.AkazeFeatureMatch` |
| 暴露分类 (Exposure) | `package-public` |
| 暴露原因 (Exposure Reason) | Supported package-public operator. |
| 分类 ID (CategoryId) | `MatchingAndLocalization` |
| 分类 (Category) | 匹配与定位 |
| 分类顺序 (CategoryOrder) | 5 |
| 版本 (Version) | `1.0.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | No |
| AI 必须披露状态 (Requires Disclosure) | Yes |
| Execution | `Implemented` |
| AlgorithmQuality | `Unknown` |
| ProductionReadiness | `Unknown` |
| FieldValidation | `NotValidated` |
| Quality Evidence Refs |  |
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:MatchingAndLocalization`, `分类显示:匹配与定位`, `生命周期:Stable`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于使用 AKAZE 局部特征与单应性校验进行模板定位，适合纹理目标的稳健匹配。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `AKAZE Homography Feature Match` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Template`。
- 参数解析覆盖 14 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OpenCvSharp.AKAZE + BFMatcher(Hamming) + FindHomography(RANSAC)`
- `OperatorBase.Get*Param(...)`
- `Cv2.DrawMarker`
- `Cv2.PutText`
- `Cv2.CvtColor`
- `Cv2.PerspectiveTransform`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `TemplatePath` | 模板路径 | `file` | "" | - | Yes | - |
| `Threshold` | 检测阈值 | `double` | 0.001 | [0.0001, 0.1] | Yes | - |
| `MinMatchCount` | 最小匹配数 | `int` | 10 | [3, 100] | Yes | - |
| `EnableSymmetryTest` | 对称测试 | `bool` | true | - | Yes | - |
| `MaxFeatures` | 最大特征点 | `int` | 500 | [100, 2000] | Yes | - |
| `EnableCandidateProfile` | Enable Candidate Profile | `bool` | false | - | Yes | - |
| `CandidateProfile` | Candidate Profile | `enum` | default | default/默认；default_v3/AKAZE default_v3 | Yes | - |
| `MatchRatio` | 匹配比率（Lowe） | `double` | 0.75 | [0.5, 0.95] | Yes | - |
| `RansacThreshold` | RANSAC阈值（px） | `double` | 5 | [0.5, 10] | Yes | - |
| `MinInlierRatio` | 最小内点比例 | `double` | 0.25 | [0.1, 1] | Yes | - |
| `AllowCenterOnlyProjection` | Allow Center-Only Projection | `bool` | false | - | Yes | - |
| `OriginMode` | Origin Mode | `enum` | Center | Center；TopLeft；Custom/自定义 | Yes | - |
| `OriginX` | Origin X | `double` | 0 | - | Yes | - |
| `OriginY` | Origin Y | `double` | 0 | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 搜索图像 | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Template` | 模板图像 | `Image` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Position` | 匹配位置 | `Point` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `MatchPoint` | 代表匹配点 | `Point` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `IsMatch` | 是否匹配 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `Score` | 匹配分数 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `InlierRatio` | Inlier Ratio | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MeanReprojectionError` | Mean Reprojection Error | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MaxReprojectionError` | Max Reprojection Error | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `AreaRatio` | Projected Area Ratio | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `CornersInsideCount` | Projected Corners Inside | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `ProjectedCenterInside` | Projected Center Inside | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `Corners` | Projected Corners | `PointList` | 点集结果，可连接几何测量、定位或标定相关节点。 |
| `HomographyFailureReason` | Homography Failure Reason | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:3, Rejected:0, Unknown:25 | Legacy 8U compatibility allowance — unverified | CV_8U | CV_8U | 1, 3, 4 | Legacy 8U compatibility allowance — unverified. Higher-depth and undeclared combinations remain Unknown and fail closed. | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit native numeric domain; no implicit MinMax conversion. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |
| `Template` | Allowed:3, Rejected:0, Unknown:25 | Legacy 8U compatibility allowance — unverified | CV_8U | CV_8U | 1, 3, 4 | Legacy 8U compatibility allowance — unverified. Higher-depth and undeclared combinations remain Unknown and fail closed. | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit native numeric domain; no implicit MinMax conversion. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Default | CV_8UC1, CV_8UC3, CV_8UC4 | Legacy 8U execution path retained for compatibility; no per-operator E2 evidence. | `Allowed` | `LegacyCompatibilityAllowance` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit legacy numeric domain. | `Any` | `IMAGE_DEPTH_UNSUPPORTED` | `E0_SOURCE_AUDIT` |
| `Image` | Default | CV_8UC2, CV_8SC1, CV_8SC2, CV_8SC3, CV_8SC4, CV_16UC1, CV_16UC2, CV_16UC3, CV_16UC4, CV_16SC1, CV_16SC2, CV_16SC3, CV_16SC4, CV_32SC1, CV_32SC2, CV_32SC3, CV_32SC4, CV_32FC1, CV_32FC2, CV_32FC3, CV_32FC4, CV_64FC1, CV_64FC2, CV_64FC3, CV_64FC4 | No operator-specific executable evidence is registered. | `Unknown` | `Unknown` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | Undefined until verified. | `Any` | `IMAGE_CONTRACT_UNKNOWN` | `Unknown` |
| `Template` | Default | CV_8UC1, CV_8UC3, CV_8UC4 | Legacy 8U execution path retained for compatibility; no per-operator E2 evidence. | `Allowed` | `LegacyCompatibilityAllowance` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit legacy numeric domain. | `Any` | `IMAGE_DEPTH_UNSUPPORTED` | `E0_SOURCE_AUDIT` |
| `Template` | Default | CV_8UC2, CV_8SC1, CV_8SC2, CV_8SC3, CV_8SC4, CV_16UC1, CV_16UC2, CV_16UC3, CV_16UC4, CV_16SC1, CV_16SC2, CV_16SC3, CV_16SC4, CV_32SC1, CV_32SC2, CV_32SC3, CV_32SC4, CV_32FC1, CV_32FC2, CV_32FC3, CV_32FC4, CV_64FC1, CV_64FC2, CV_64FC3, CV_64FC4 | No operator-specific executable evidence is registered. | `Unknown` | `Unknown` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | Undefined until verified. | `Any` | `IMAGE_CONTRACT_UNKNOWN` | `Unknown` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`81A958291FEA9DF861AC2F4562C8D920DD581A73D79D500735080ED9CD49F561`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `FailureReason` | `String` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `Inliers` | `Any` | 源码输出字典初始化中可见字段。 |
| `Message` | `String` | 源码输出字典初始化中可见字段。 |
| `ScoreDefinition` | `Float` | 源码输出字典初始化中可见字段。 |
| `TotalMatches` | `Any` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(P + T*S) where P is image pixels and T/S are retained template and scene descriptors |
| 典型耗时 (Typical Latency) | FeatureMatchContractRunner baseline: 22 cases passed, avg runtime about 11.7 ms on synthetic contract images. |
| 内存特征 (Memory Profile) | O(P + T + S) plus bounded static template cache entries for TemplatePath mode. |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 1 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Textured labels, PCB marks, printed features, and local parts with enough corners or blob-like texture.
- 适合 (Suitable)：Template localization where moderate rotation, scale, or perspective variation is expected.
- 适合 (Suitable)：Pipelines that need a business-level NG result image instead of a framework-level failure for no-match cases.
- 不适合 (Not Suitable)：Weak-texture, pure-color, or strongly repetitive targets where homography inliers are ambiguous.
- 不适合 (Not Suitable)：Subpixel metrology or robot-pick centers that require calibrated geometric center output.
- 不适合 (Not Suitable)：Very high-texture full-frame scenes without ROI constraints, because scene descriptors are not globally capped.

## 已知限制 / Known Limitations
1. Score is a homography verification score based on inlier evidence, not a normalized template-correlation score.
2. MatchRatio, RANSAC threshold, MinMatchCount, and MinInlierRatio are configurable and should be validated with replay evidence.
3. TemplatePath mode uses a bounded in-process cache keyed by file fingerprint and detector configuration.
4. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
5. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
6. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-09-01 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
