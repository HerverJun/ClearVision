# Planar Matching / PlanarMatching

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PlanarMatchingOperator` |
| 枚举值 (Enum) | `OperatorType.PlanarMatching` |
| 分类 (Category) | 匹配定位 |
| 版本 (Version) | `1.1.2` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Feature-based planar matching with homography verification. Suitable for textured planar targets under perspective change。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Feature homography planar matching` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Template`。
- 参数解析覆盖 20 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `ORB/AKAZE/BRISK DetectAndCompute -> BFMatcher ratio test -> HomographyVerificationHelper -> multi-scale score selection`
- `OperatorBase.Get*Param(...)`
- `Cv2.Resize`
- `Cv2.CvtColor`
- `Cv2.ImRead`
- `Cv2.Line`
- `Cv2.Circle`
- `Cv2.PutText`
- `Cv2.MatchTemplate`
- `Cv2.MinMaxLoc`
- `File.Exists`
- `File.OpenRead`
- `Math.Min`
- `Math.Abs`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `TemplatePath` | Template Image Path | `file` | "" | - | Yes | - |
| `DetectorType` | Feature Detector | `enum` | ORB | ORB/ORB；AKAZE/AKAZE；BRISK/BRISK | Yes | - |
| `MaxFeatures` | Max Features | `int` | 1000 | [100, 5000] | Yes | - |
| `ScaleFactor` | Scale Factor | `double` | 1.2 | [1.01, 2] | Yes | - |
| `NLevels` | Pyramid Levels | `int` | 8 | [1, 16] | Yes | - |
| `MatchRatio` | Match Ratio (Lowe's) | `double` | 0.75 | [0.5, 0.95] | Yes | - |
| `RansacThreshold` | RANSAC Threshold (px) | `double` | 3 | [0.5, 10] | Yes | - |
| `MinMatchCount` | Min Match Count | `int` | 10 | [4, 100] | Yes | - |
| `MinInliers` | Min Inliers | `int` | 8 | [4, 100] | Yes | - |
| `MinInlierRatio` | Min Inlier Ratio | `double` | 0.25 | [0.1, 1] | Yes | - |
| `ScoreThreshold` | Score Threshold | `double` | 0.5 | [0, 1] | Yes | - |
| `AllowCenterOnlyProjection` | Allow Center-Only Projection | `bool` | false | - | Yes | - |
| `UseRoi` | Use ROI | `bool` | false | - | Yes | - |
| `RoiX` | ROI X | `int` | 0 | - | Yes | - |
| `RoiY` | ROI Y | `int` | 0 | - | Yes | - |
| `RoiWidth` | ROI Width | `int` | 0 | - | Yes | - |
| `RoiHeight` | ROI Height | `int` | 0 | - | Yes | - |
| `EnableMultiScale` | Enable Multi-Scale | `bool` | true | - | Yes | - |
| `ScaleRange` | Scale Range (±) | `double` | 0.2 | [0, 1] | Yes | - |
| `EnableEarlyExit` | Enable Early Exit | `bool` | false | - | Yes | - |

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
| `IsMatch` | Is Match | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `Score` | Score | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MatchCount` | Match Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Method` | Method | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `FailureReason` | Failure Reason | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `CandidateScore` | Candidate Score | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `InlierCount` | Inlier Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `InlierRatio` | Inlier Ratio | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MeanReprojectionError` | Mean Reprojection Error | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MaxReprojectionError` | Max Reprojection Error | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `AreaRatio` | Area Ratio | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `CornersInsideCount` | Corners Inside Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `ProjectedCenterInside` | Projected Center Inside | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `HomographyFailureReason` | Homography Failure Reason | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `VerificationPassed` | Verification Passed | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `MatchResult` | Match Result | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Homography` | Homography Matrix | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Corners` | Detected Corners | `PointList` | 点集结果，可连接几何测量、定位或标定相关节点。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Center` | `Any` | 源码通过输出字典索引赋值写入。 |
| `DetectorParameterDiagnostics` | `Any` | 源码输出字典初始化中可见字段。 |
| `FeatureMatchCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `MaxFeaturesApplied` | `Float` | 源码输出字典初始化中可见字段。 |
| `Message` | `String` | 源码输出字典初始化中可见字段。 |
| `NLevelsApplied` | `Any` | 源码输出字典初始化中可见字段。 |
| `Notes` | `Any` | 源码输出字典初始化中可见字段。 |
| `ProcessingTimeMs` | `Any` | 源码输出字典初始化中可见字段。 |
| `ScaleFactorApplied` | `Any` | 源码输出字典初始化中可见字段。 |
| `SearchFeatures` | `Any` | 源码输出字典初始化中可见字段。 |
| `TemplateFeatures` | `Any` | 源码输出字典初始化中可见字段。 |
| `VerificationScore` | `Float` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(S*(F log F + M + R*I)) |
| 典型耗时 (Typical Latency) | No dedicated golden benchmark yet; covered by planar matching regression and operator tests |
| 内存特征 (Memory Profile) | O(F + M + W*H) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `Acme.Product/tests/Acme.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 3 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Textured planar objects under perspective change where homography is a valid geometric model.
- 适合 (Suitable)：Inspection flows that need match score, projected corners, inlier metrics, and failure diagnostics.
- 不适合 (Not Suitable)：Non-planar, strongly deformable, or textureless targets.
- 不适合 (Not Suitable)：Scenes with many repeated local features unless ROI, detector, and score thresholds are constrained.

## 已知限制 / Known Limitations
1. Detector support is limited to ORB, AKAZE, and BRISK.
2. Multi-scale search uses a small fixed scale candidate set rather than exhaustive scale-space optimization.
3. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
4. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
5. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
6. 外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.1.2 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
