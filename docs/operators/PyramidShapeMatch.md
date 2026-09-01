# 金字塔形状匹配 / PyramidShapeMatch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PyramidShapeMatchOperator` |
| 枚举值 (Enum) | `OperatorType.PyramidShapeMatch` |
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
该算子用于基于 LINEMOD 的金字塔模板匹配。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `LINEMOD Pyramid Shape Matching` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Template`。
- 参数解析覆盖 15 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `TemplateMatcher or ShapeDescriptorMatcher over OpenCvSharp Mats`
- `OperatorBase.Get*Param(...)`
- `Cv2.ImRead`
- `Cv2.Rectangle`
- `Cv2.DrawMarker`
- `Cv2.PutText`
- `File.Exists`
- `Math.Max`
- `Math.Abs`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `TemplatePath` | 模板路径 | `file` | "" | - | Yes | - |
| `MinScore` | 最小分数(%) | `double` | 80 | [0, 100] | Yes | - |
| `AngleRange` | 角度范围(度) | `int` | 180 | [0, 180] | Yes | - |
| `AngleStep` | 角度步长 | `int` | 5 | [1, 45] | Yes | - |
| `PyramidLevels` | 金字塔层数 | `int` | 3 | [1, 5] | Yes | - |
| `MagnitudeThreshold` | 梯度阈值 | `int` | 30 | [0, 255] | Yes | - |
| `WeakThreshold` | 弱梯度阈值 | `double` | 30 | [0, 255] | Yes | - |
| `StrongThreshold` | 强梯度阈值 | `double` | 60 | [0, 255] | Yes | - |
| `NumFeatures` | 特征点数量 | `int` | 150 | [50, 8191] | Yes | - |
| `SpreadT` | 方向扩展范围 | `int` | 4 | [1, 16] | Yes | - |
| `MaxMatches` | 最大匹配数 | `int` | 10 | [1, 100] | Yes | - |
| `MatchMode` | 匹配模式 | `enum` | Template | Template/模板匹配；ShapeDescriptor/形状描述符匹配 | Yes | - |
| `DescriptorTypes` | 描述符类型 | `enum` | Hu+Fourier | Hu/Hu矩；Fourier/傅里叶描述符；Hu+Fourier/全部 | Yes | - |
| `PreFilterArea` | 面积预筛选 | `bool` | true | - | Yes | - |
| `AreaTolerance` | 面积容差 | `double` | 0.3 | [0, 1] | Yes | - |

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
| `Angle` | 旋转角度 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `IsMatch` | 是否匹配 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `Score` | 匹配分数 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |

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
- 组合指纹 (Generation Fingerprint)：`E45766A3B443EEF67F850EF888D9DAB04D242E15BC03B668EC0C20B7DCB082B1`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `AppliedParameters` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `LegacyParameters` | `Any` | 源码通过输出字典索引赋值写入。 |
| `MatchCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `MatcherConfig` | `Any` | 源码通过输出字典索引赋值写入。 |
| `MatcherDiagnostics` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Matches` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Metadata` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Mode` | `String` | 源码通过输出字典索引赋值写入。 |
| `Reason` | `String` | 源码通过输出字典索引赋值写入。 |
| `Scale` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ScoreScale` | `Float` | 源码通过输出字典索引赋值写入。 |
| `ShapeDescriptorOnly` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | Template mode is roughly O(L*A*P) for pyramid levels, angle samples, and searched pixels; descriptor mode depends on contour count. |
| 典型耗时 (Typical Latency) | PyramidShapeMatchContractRunner baseline: 24 cases passed, avg runtime about 4.4 ms on synthetic contract images. |
| 内存特征 (Memory Profile) | O(P + F) for image pyramids, gradient maps, template features, and candidate match diagnostics. |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 5 条 `OperatorExecutionOutput.Failure(...)` 路径。

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
4. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
5. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
6. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
7. 外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-09-01 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
