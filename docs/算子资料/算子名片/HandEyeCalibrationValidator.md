# Hand-Eye Calibration Validator / HandEyeCalibrationValidator

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `HandEyeCalibrationValidatorOperator` |
| 枚举值 (Enum) | `OperatorType.HandEyeCalibrationValidator` |
| 分类 (Category) | 标定 |
| 版本 (Version) | `1.0.1` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:第三方SDK` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Validates a hand-eye CalibrationBundleV2 payload and produces quality metrics, HTML report, and pose suggestions。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Hand-Eye Consistency Validation` 为主；元数据未声明更多细分时，以当前源码实现为准。
处理过程遵循统一算子框架：输入检查、参数解析、核心计算、输出封装和可选参数校验分层完成。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`RobotPoses`、`CalibrationBoardPoses`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`CalibrationData`。
- 参数解析覆盖 1 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `Pose consistency over static-reference transforms`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `CalibrationType` | Calibration Type | `enum` | eye_in_hand | eye_in_hand/Eye In Hand；eye_to_hand/Eye To Hand | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `RobotPoses` | Robot Poses | `Any` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `CalibrationBoardPoses` | Calibration Board Poses | `Any` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `CalibrationData` | Calibration Data | `String` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `CalibrationData` | Calibration Data | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `MeanError` | Mean Error | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MaxError` | Max Error | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MeanRotationError` | Mean Rotation Error | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Quality` | Quality | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `HtmlReport` | HTML Report | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `Suggestions` | Suggestions | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `SuggestedValidationPoses` | Suggested Validation Poses | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

### 运行时附加输出 / Runtime Additional Outputs
- 未在源码中发现除声明输出端口外的稳定附加输出字段；下游连线以输出端口表为准。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) |
| 典型耗时 (Typical Latency) | HandEyeCalibrationValidatorContractRunner baseline: 24 cases passed, avg runtime about 1.8 ms on synthetic pose bundles. |
| 内存特征 (Memory Profile) | O(N) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 3 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Offline or commissioning checks for hand-eye calibration consistency using known robot and board pose samples.
- 适合 (Suitable)：Quality-gating a CalibrationBundleV2 before downstream pixel/world or robot-coordinate transforms consume it.
- 适合 (Suitable)：Producing operator-facing diagnostics and suggested validation poses after calibration.
- 不适合 (Not Suitable)：Solving the hand-eye transform itself; use HandEyeCalibration for AX=XB estimation.
- 不适合 (Not Suitable)：Validating arbitrary malformed pose arrays without first normalizing them to the platform pose schema.
- 不适合 (Not Suitable)：Treating the HTML report as a machine contract; consume scalar errors and Quality for automation.

## 已知限制 / Known Limitations
1. Missing RobotPoses or CalibrationBoardPoses fail the contract, but callers should not bind to exact localized error text.
2. Pose JSON must follow the platform Matrix4x4 row-order serialization used by Pose3DSerialization.
3. Quality thresholds are consistency gates for validation samples, not a complete production metrology uncertainty model.
4. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
5. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
