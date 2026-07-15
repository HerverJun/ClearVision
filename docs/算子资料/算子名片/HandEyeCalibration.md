# 手眼标定 / HandEyeCalibration

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `HandEyeCalibrationOperator` |
| 枚举值 (Enum) | `OperatorType.HandEyeCalibration` |
| 分类 ID (CategoryId) | `CalibrationAndCoordinates` |
| 分类 (Category) | 标定与坐标 |
| 分类顺序 (CategoryOrder) | 8 |
| 版本 (Version) | `1.0.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| Execution | `Implemented` |
| AlgorithmQuality | `Unknown` |
| ProductionReadiness | `Unknown` |
| FieldValidation | `NotValidated` |
| Quality Evidence Refs |  |
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:CalibrationAndCoordinates`, `分类显示:标定与坐标`, `生命周期:Stable`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于根据机器人位姿和标定板位姿求解手眼或简化眼在手外标定。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `OpenCV Hand-Eye Calibration` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`RobotPoses`、`CalibrationBoardPoses`；缺失时通常返回失败结果。
- 参数解析覆盖 4 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OpenCvSharp.Cv2.CalibrateHandEye`
- `OperatorBase.Get*Param(...)`
- `Cv2.CalibrateHandEye`
- `Convert.ToString`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `CalibrationType` | 标定类型 | `enum` | eye_in_hand | eye_in_hand/手眼；eye_to_hand/眼到手 | Yes | - |
| `Method` | 方法 | `enum` | TSAI | TSAI/Tsai；PARK/Park；HORAUD/Horaud；ANDREFF/Andreff；DANIILIDIS/Daniilidis | Yes | - |
| `CameraMatrix` | 相机矩阵 | `string` | "" | - | Yes | - |
| `DistortionCoeffs` | 畸变系数 | `string` | "" | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `RobotPoses` | Robot Poses | `Any` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `CalibrationBoardPoses` | Calibration Board Poses | `Any` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `CalibrationData` | Calibration Data | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ReprojectionError` | Reprojection Error | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `CalibrationQuality` | Calibration Quality | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `MatrixConvention` | Matrix Convention | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `HtmlReport` | HTML Report | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `Suggestions` | Suggestions | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `SuggestedValidationPoses` | Suggested Validation Poses | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

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
- 组合指纹 (Generation Fingerprint)：`CB4D33407E47D93FBA7918B42B5F24CC74F5B1E536970E14E5889ACEF0C8D7E6`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
- 未在源码中发现除声明输出端口外的稳定附加输出字段；下游连线以输出端口表为准。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) |
| 典型耗时 (Typical Latency) | 未固定；取决于图像分辨率、ROI 范围、OpenCV 算法分支和输出可视化成本。 |
| 内存特征 (Memory Profile) | O(N) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 3 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：相机、坐标、像素到世界坐标或工装几何关系需要被显式建模和复用的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-07-14 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
