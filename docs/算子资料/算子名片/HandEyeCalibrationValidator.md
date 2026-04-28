# Hand-Eye Calibration Validator / HandEyeCalibrationValidator

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `HandEyeCalibrationValidatorOperator` |
| 枚举值 (Enum) | `OperatorType.HandEyeCalibrationValidator` |
| 分类 (Category) | 标定 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Validates a hand-eye CalibrationBundleV2 payload and produces quality metrics, HTML report, and pose suggestions.。
> English: Validates a hand-eye CalibrationBundleV2 payload and produces quality metrics, HTML report, and pose suggestions..

## 实现策略 / Implementation Strategy
> 中文：Parse robot poses, board poses, and a CalibrationBundleV2 hand-eye transform, evaluate eye-in-hand or eye-to-hand pose consistency, then emit scalar errors, quality, HTML report, suggestions, and an updated calibration bundle.。
> English: Parse robot poses, board poses, and a CalibrationBundleV2 hand-eye transform, evaluate eye-in-hand or eye-to-hand pose consistency, then emit scalar errors, quality, HTML report, suggestions, and an updated calibration bundle..

## 核心 API 调用链 / Core API Call Chain
- `Pose consistency over static-reference transforms`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `CalibrationType` | `enum` | eye_in_hand | - | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `RobotPoses` | Robot Poses | `Any` | Yes | - |
| `CalibrationBoardPoses` | Calibration Board Poses | `Any` | Yes | - |
| `CalibrationData` | Calibration Data | `String` | No | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `CalibrationData` | Calibration Data | `String` | - |
| `MeanError` | Mean Error | `Float` | - |
| `MaxError` | Max Error | `Float` | - |
| `MeanRotationError` | Mean Rotation Error | `Float` | - |
| `Quality` | Quality | `String` | - |
| `HtmlReport` | HTML Report | `String` | - |
| `Suggestions` | Suggestions | `Any` | - |
| `SuggestedValidationPoses` | Suggested Validation Poses | `String` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) |
| 典型耗时 (Typical Latency) | HandEyeCalibrationValidatorContractRunner baseline: 24 cases passed, avg runtime about 1.8 ms on synthetic pose bundles. |
| 内存特征 (Memory Profile) | O(N) |

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

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.2 | 2026-04-28 | Backfilled HandEyeCalibrationValidatorContractRunner evidence (24/24 passed), pose bundle and failure contract notes |
| 1.0.1 | 2026-04-28 | 自动生成文档骨架 / Generated skeleton |
