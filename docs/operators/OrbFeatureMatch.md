# ORB特征匹配 / OrbFeatureMatch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `OrbFeatureMatchOperator` |
| 枚举值 (Enum) | `OperatorType.OrbFeatureMatch` |
| 分类 ID (CategoryId) | `MatchingAndLocalization` |
| 分类 (Category) | 匹配与定位 |
| 分类顺序 (CategoryOrder) | 5 |
| 版本 (Version) | `1.0.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:MatchingAndLocalization`, `分类显示:匹配与定位`, `生命周期:Stable`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于使用 ORB 特征和单应性校验进行快速模板定位。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `ORB Homography Feature Match` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Template`。
- 参数解析覆盖 17 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OpenCvSharp.ORB + BFMatcher(Hamming) + FindHomography(RANSAC)`
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
| `MaxFeatures` | 最大特征点 | `int` | 500 | [100, 2000] | Yes | - |
| `ScaleFactor` | 尺度因子 | `double` | 1.2 | [1, 2] | Yes | - |
| `NLevels` | 金字塔层数 | `int` | 8 | [1, 12] | Yes | - |
| `EdgeThreshold` | 边缘阈值 | `int` | 31 | [3, 100] | Yes | - |
| `EnableSymmetryTest` | 对称测试 | `bool` | true | - | Yes | - |
| `MinMatchCount` | 最小匹配数 | `int` | 10 | [3, 100] | Yes | - |
| `EnableCandidateProfile` | Enable Candidate Profile | `bool` | false | - | Yes | - |
| `CandidateProfile` | Candidate Profile | `enum` | default | default/默认；replay_safe_dense_strict/ORB replay_safe_dense_strict | Yes | - |
| `MatchRatio` | 匹配比率（Lowe） | `double` | 0.75 | [0.5, 0.95] | Yes | - |
| `RansacThreshold` | RANSAC阈值（px） | `double` | 5 | [0.5, 10] | Yes | - |
| `MinInlierRatio` | 最小内点比例 | `double` | 0.25 | [0.1, 1] | Yes | - |
| `AllowCenterOnlyProjection` | Allow Center-Only Projection | `bool` | false | - | Yes | - |
| `FastThreshold` | ORB FAST Threshold | `int` | 20 | [1, 100] | Yes | - |
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

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`EF069F000E6111E69050DCF36C6D0218D9ABEE2914BDE25F7FD66DE6B1ACB132`
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
| 典型耗时 (Typical Latency) | FeatureMatchContractRunner baseline: 22 cases passed, avg runtime about 7.6 ms on synthetic contract images. |
| 内存特征 (Memory Profile) | O(P + T + S) plus bounded static template cache entries for TemplatePath mode. |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 1 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Realtime feature-based localization when the template has enough repeatable ORB corners.
- 适合 (Suitable)：Moderate rotation and small scale changes where a homography can explain the target pose.
- 适合 (Suitable)：Contract-driven pipelines that use IsMatch and FailureReason rather than execution status alone.
- 不适合 (Not Suitable)：Low-texture or repetitive-pattern templates that produce unstable descriptor matches.
- 不适合 (Not Suitable)：Precision measurement workflows that require a calibrated target center or subpixel edge result.
- 不适合 (Not Suitable)：Scenes where a large number of background features should be searched without ROI or threshold tuning.

## 已知限制 / Known Limitations
1. Score is a homography verification score based on inlier evidence, not a descriptor distance.
2. MatchRatio, RANSAC threshold, MinMatchCount, MinInlierRatio, and ORB FAST threshold are configurable and should be validated with replay evidence.
3. TemplatePath mode uses a bounded in-process cache keyed by file fingerprint and detector configuration.
4. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
5. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
6. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-07-14 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
