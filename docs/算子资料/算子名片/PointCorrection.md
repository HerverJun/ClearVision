# 点位刚性补偿 / PointCorrection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PointCorrectionOperator` |
| 枚举值 (Enum) | `OperatorType.PointCorrection` |
| 分类 ID (CategoryId) | `MatchingAndLocalization` |
| 分类 (Category) | 匹配与定位 |
| 分类顺序 (CategoryOrder) | 5 |
| 版本 (Version) | `1.0.4` |
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
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:MatchingAndLocalization`, `分类显示:匹配与定位`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于根据检测点/角度与参考点/角度计算像素空间二维刚性补偿量和变换矩阵；按 PixelSize 输出 mm 前需确认标定尺度。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`DetectedPoint`、`ReferencePoint`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`DetectedAngle`、`ReferenceAngle`。
- 参数解析覆盖 4 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Math.Sqrt`
- `Math.PI`
- `Math.Cos`
- `Math.Sin`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `CorrectionMode` | 校正模式 | `enum` | TranslationOnly | TranslationOnly/仅平移；TranslationRotation/平移+旋转 | Yes | - |
| `OutputUnit` | 输出单位 | `enum` | Pixel | Pixel/像素；mm | Yes | - |
| `PixelSize` | 像素尺寸 | `double` | 1 | [1E-09, 1000000] | Yes | - |
| `MaxAllowedDistance` | Max Allowed Distance | `double` | 0 | [0, 1000000] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `DetectedPoint` | Detected Point | `Point` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `DetectedAngle` | Detected Angle | `Float` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `ReferencePoint` | Reference Point | `Point` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `ReferenceAngle` | 参考角度 | `Float` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `CorrectionX` | Correction X | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `CorrectionY` | Correction Y | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `CorrectionAngle` | Correction Angle | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `TransformMatrix` | Transform Matrix | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `TransformUnit` | Transform Unit | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

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
- 组合指纹 (Generation Fingerprint)：`6D2D29704E5A0F25542D595814E8BB0BAF66FA8DCFC82F90DF5E9ED306F1DE50`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
- 未在源码中发现除声明输出端口外的稳定附加输出字段；下游连线以输出端口表为准。

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
- 执行失败契约：源码中发现 12 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：输入图像质量稳定、参数范围明确，需要在流程中完成图像处理、定位、测量或可视化输出的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.4 | 2026-07-14 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
