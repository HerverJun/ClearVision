# 直线测量 / LineMeasurement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `LineMeasurementOperator` |
| 枚举值 (Enum) | `OperatorType.LineMeasurement` |
| 分类 ID (CategoryId) | `Measurement` |
| 分类 (Category) | 测量 |
| 分类顺序 (CategoryOrder) | 7 |
| 版本 (Version) | `1.2.1` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | No |
| AI 必须披露状态 (Requires Disclosure) | Yes |
| Execution | `Implemented` |
| AlgorithmQuality | `SyntheticBenchmarkEvidence` |
| ProductionReadiness | `Unknown` |
| FieldValidation | `NotValidated` |
| Quality Evidence Refs | quality/evals/reports/operator-product-e2e-baseline-acceptance.json<br>quality/evals/reports/operator-product-e2e-after-acceptance.json<br>quality/evals/reports/operator-product-e2e-phase5-comparison.json<br>quality/evals/reports/operator-quality-phase5-evidence.json |
| Evidence Scope | `ModeAggregate` |
| Evidence Identity | `clearvision-operator-quality-phase5-evidence@1.0.0:7ed1d600eead23e8185d6fef730b3995bec7db29c7d65a6cc3248a1ca95770cf/line-formal-product-e2e` |
| Mode Evidence | Method=FitLine; FitLoss=L2(default): SyntheticBenchmarkEvidence; adopted=True; default=True; Aggregate old/current default accuracy, failure and diagnostic-summary conformance on validation/test; no per-case diagnostic fingerprint is claimed.<br>Method=FitLine; FitLoss=Welsch: SyntheticBenchmarkValidated; adopted=True; default=False; Accepted opt-in: validation RMSE/P95 improved; independent test RMSE and P95 improved with unchanged failure/ambiguity and written cost budgets passed. |
| 标签 (Tags) | `AlgorithmQuality:SyntheticBenchmarkEvidence`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:Measurement`, `分类显示:测量`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于检测直线特征，输出方向、跨度和拟合质量诊断。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 5 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.CvtColor`
- `Cv2.Canny`
- `Cv2.FitLine`
- `Cv2.HoughLines`
- `Cv2.Line`
- `Cv2.HoughLinesP`
- `Convert.ToDouble`
- `Convert.ToInt32`
- `Math.PI`
- `Math.Round`
- `Math.Max`
- `Math.Atan2`
- `Math.Clamp`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Method` | 检测方法 | `enum` | ProbabilisticHough | HoughLines/霍夫直线；ProbabilisticHough/概率霍夫直线；FitLine/拟合直线 | Yes | - |
| `Threshold` | 累加阈值 | `int` | 100 | >= 1 | Yes | - |
| `MinLength` | 最小长度 | `double` | 50 | >= 0 | Yes | - |
| `MaxGap` | 最大间隙 | `double` | 10 | >= 0 | Yes | - |
| `FitLoss` | 拟合损失 | `enum` | L2 | L2/L2 兼容；Huber；Welsch | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Angle` | 角度 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Length` | 长度 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Line` | 直线数据 | `LineData` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `LineCount` | 直线数量 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MeasurementEvidence` | 测量证据 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:3, Rejected:0, Unknown:25 | Legacy 8U compatibility allowance — unverified | CV_8U | CV_8U | 1, 3, 4 | Legacy 8U compatibility allowance — unverified. Higher-depth and undeclared combinations remain Unknown and fail closed. | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit native numeric domain; no implicit MinMax conversion. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Default | CV_8UC1, CV_8UC3, CV_8UC4 | Legacy 8U execution path retained for compatibility; no per-operator E2 evidence. | `Allowed` | `LegacyCompatibilityAllowance` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit legacy numeric domain. | `Any` | `IMAGE_DEPTH_UNSUPPORTED` | `E0_SOURCE_AUDIT` |
| `Image` | Default | CV_8UC2, CV_8SC1, CV_8SC2, CV_8SC3, CV_8SC4, CV_16UC1, CV_16UC2, CV_16UC3, CV_16UC4, CV_16SC1, CV_16SC2, CV_16SC3, CV_16SC4, CV_32SC1, CV_32SC2, CV_32SC3, CV_32SC4, CV_32FC1, CV_32FC2, CV_32FC3, CV_32FC4, CV_64FC1, CV_64FC2, CV_64FC3, CV_64FC4 | No operator-specific executable evidence is registered. | `Unknown` | `Unknown` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | Undefined until verified. | `Any` | `IMAGE_CONTRACT_UNKNOWN` | `Unknown` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`49DA9D751D7373D509AEAF26D49ABC295CAC0868AEE2332ACFA03D49D56C61BA`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Confidence` | `Float` | 源码输出字典初始化中可见字段。 |
| `Covariance` | `Any` | 源码通过输出字典索引赋值写入。 |
| `CovarianceCalibrated` | `Any` | 源码通过输出字典索引赋值写入。 |
| `DiagnosticsAvailable` | `Any` | 源码通过输出字典索引赋值写入。 |
| `EndX` | `Any` | 源码输出字典初始化中可见字段。 |
| `EndY` | `Any` | 源码输出字典初始化中可见字段。 |
| `FitPointCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `Lines` | `Any` | 源码输出字典初始化中可见字段。 |
| `OutlierCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `RefineAlgorithm` | `Any` | 源码通过输出字典索引赋值写入。 |
| `RefineConverged` | `Any` | 源码通过输出字典索引赋值写入。 |
| `RefineFailure` | `Any` | 源码通过输出字典索引赋值写入。 |
| `RefineIterations` | `Float` | 源码通过输出字典索引赋值写入。 |
| `RefinedPointCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `ResidualMax` | `Float` | 源码通过输出字典索引赋值写入。 |
| `ResidualMean` | `Float` | 源码通过输出字典索引赋值写入。 |
| `ResidualRmse` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Rho` | `Any` | 源码输出字典初始化中可见字段。 |
| `RobustScale` | `Any` | 源码通过输出字典索引赋值写入。 |
| `SeedAlgorithm` | `Any` | 源码通过输出字典索引赋值写入。 |
| `SigmaAngleDegrees` | `Float` | 源码通过输出字典索引赋值写入。 |
| `SigmaOffsetPx` | `Any` | 源码通过输出字典索引赋值写入。 |
| `StartX` | `Any` | 源码输出字典初始化中可见字段。 |
| `StartY` | `Any` | 源码输出字典初始化中可见字段。 |
| `StatusCode` | `Any` | 源码通过输出字典索引赋值写入。 |
| `StatusMessage` | `String` | 源码输出字典初始化中可见字段。 |
| `Theta` | `Any` | 源码输出字典初始化中可见字段。 |
| `UncertaintyPx` | `Any` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 多数图像路径近似 `O(W*H)`；涉及轮廓、匹配或排序时会叠加候选数量相关开销。 |
| 典型耗时 (Typical Latency) | 未固定；取决于图像分辨率、ROI 范围、OpenCV 算法分支和输出可视化成本。 |
| 内存特征 (Memory Profile) | 通常需要输入图像、临时 Mat、结果图和输出封装内存；峰值随图像尺寸和中间副本数量增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 5 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：输入图像质量稳定、参数范围明确，需要在流程中完成图像处理、定位、测量或可视化输出的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
3. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.2.1 | 2026-08-31 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
