# 清晰度评估 / SharpnessEvaluation

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SharpnessEvaluationOperator` |
| 枚举值 (Enum) | `OperatorType.SharpnessEvaluation` |
| 分类 ID (CategoryId) | `FeatureExtraction` |
| 分类 (Category) | 特征提取 |
| 分类顺序 (CategoryOrder) | 4 |
| 版本 (Version) | `1.1.0` |
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
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:FeatureExtraction`, `分类显示:特征提取`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于评估图像的对焦清晰度。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 8 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.CvtColor`
- `Cv2.Rectangle`
- `Cv2.PutText`
- `Cv2.Laplacian`
- `Cv2.MeanStdDev`
- `Cv2.Sobel`
- `Math.Max`
- `Math.Abs`
- `Math.Clamp`
- `ImageWrapper`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Method` | 方法 | `enum` | Laplacian | Laplacian；Brenner；Tenengrad；SMD | Yes | - |
| `ThresholdMode` | 阈值模式 | `enum` | PerMethodDefault | PerMethodDefault；Manual/手动 | Yes | - |
| `Threshold` | 阈值 | `double` | 100 | >= 0 | Yes | - |
| `RoiX` | ROIX | `int` | 0 | - | Yes | - |
| `RoiY` | ROIY | `int` | 0 | - | Yes | - |
| `RoiW` | ROI宽度 | `int` | 0 | - | Yes | - |
| `RoiH` | ROI高度 | `int` | 0 | - | Yes | - |
| `OutputImagePolicy` | Output Image Policy | `enum` | FullOverlay | FullOverlay/Full Overlay；Passthrough；None/无 | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Score` | 分数 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `IsSharp` | Is Sharp | `Boolean` | 仅 DecisionReady=true 时产生。 |
| `Image` | Image | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:192, Rejected:24, Unknown:0 | Verified production support is present. | CV_8U, CV_16U, CV_32F | CV_8U, CV_16U, CV_32F | 1, 3, 4 | Admission is exact for Method + ThresholdMode + OutputImagePolicy + Depth + Channels. | Color -> Gray only for score computation; no dynamic-range conversion. | Passthrough preserves input type; None omits Image; overlay is variant-restricted. | Scores use the admitted input's native numeric domain. | RejectNaNAndInfinityForFloatingVariants | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Brenner:Manual:FullOverlay | CV_32FC1, CV_32FC3, CV_32FC4 | FullOverlay is undefined for uncalibrated floating display ranges. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_DYNAMIC_RANGE_UNDEFINED` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:Manual:FullOverlay | CV_8UC1, CV_16UC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:Manual:FullOverlay | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:Manual:None | CV_8UC1, CV_16UC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:Manual:None | CV_32FC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:Manual:None | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:Manual:None | CV_32FC3, CV_32FC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:Manual:Passthrough | CV_8UC1, CV_16UC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:Manual:Passthrough | CV_32FC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:Manual:Passthrough | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:Manual:Passthrough | CV_32FC3, CV_32FC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:PerMethodDefault:FullOverlay | CV_32FC1, CV_32FC3, CV_32FC4 | FullOverlay is undefined for uncalibrated floating display ranges. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_DYNAMIC_RANGE_UNDEFINED` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:PerMethodDefault:FullOverlay | CV_8UC1, CV_16UC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:PerMethodDefault:FullOverlay | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:PerMethodDefault:None | CV_8UC1, CV_16UC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:PerMethodDefault:None | CV_32FC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:PerMethodDefault:None | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:PerMethodDefault:None | CV_32FC3, CV_32FC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:PerMethodDefault:Passthrough | CV_8UC1, CV_16UC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:PerMethodDefault:Passthrough | CV_32FC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:PerMethodDefault:Passthrough | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Brenner:PerMethodDefault:Passthrough | CV_32FC3, CV_32FC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:Manual:FullOverlay | CV_32FC1, CV_32FC3, CV_32FC4 | FullOverlay is undefined for uncalibrated floating display ranges. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_DYNAMIC_RANGE_UNDEFINED` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:Manual:FullOverlay | CV_8UC1, CV_16UC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:Manual:FullOverlay | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:Manual:None | CV_8UC1, CV_16UC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:Manual:None | CV_32FC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:Manual:None | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:Manual:None | CV_32FC3, CV_32FC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:Manual:Passthrough | CV_8UC1, CV_16UC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:Manual:Passthrough | CV_32FC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:Manual:Passthrough | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:Manual:Passthrough | CV_32FC3, CV_32FC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:PerMethodDefault:FullOverlay | CV_32FC1, CV_32FC3, CV_32FC4 | FullOverlay is undefined for uncalibrated floating display ranges. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_DYNAMIC_RANGE_UNDEFINED` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:PerMethodDefault:FullOverlay | CV_8UC1, CV_16UC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:PerMethodDefault:FullOverlay | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:PerMethodDefault:None | CV_8UC1, CV_16UC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:PerMethodDefault:None | CV_32FC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:PerMethodDefault:None | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:PerMethodDefault:None | CV_32FC3, CV_32FC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:PerMethodDefault:Passthrough | CV_8UC1, CV_16UC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:PerMethodDefault:Passthrough | CV_32FC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:PerMethodDefault:Passthrough | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Laplacian:PerMethodDefault:Passthrough | CV_32FC3, CV_32FC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:Manual:FullOverlay | CV_32FC1, CV_32FC3, CV_32FC4 | FullOverlay is undefined for uncalibrated floating display ranges. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_DYNAMIC_RANGE_UNDEFINED` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:Manual:FullOverlay | CV_8UC1, CV_16UC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:Manual:FullOverlay | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:Manual:None | CV_8UC1, CV_16UC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:Manual:None | CV_32FC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:Manual:None | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:Manual:None | CV_32FC3, CV_32FC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:Manual:Passthrough | CV_8UC1, CV_16UC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:Manual:Passthrough | CV_32FC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:Manual:Passthrough | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:Manual:Passthrough | CV_32FC3, CV_32FC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:PerMethodDefault:FullOverlay | CV_32FC1, CV_32FC3, CV_32FC4 | FullOverlay is undefined for uncalibrated floating display ranges. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_DYNAMIC_RANGE_UNDEFINED` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:PerMethodDefault:FullOverlay | CV_8UC1, CV_16UC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:PerMethodDefault:FullOverlay | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:PerMethodDefault:None | CV_8UC1, CV_16UC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:PerMethodDefault:None | CV_32FC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:PerMethodDefault:None | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:PerMethodDefault:None | CV_32FC3, CV_32FC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:PerMethodDefault:Passthrough | CV_8UC1, CV_16UC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:PerMethodDefault:Passthrough | CV_32FC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:PerMethodDefault:Passthrough | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | SMD:PerMethodDefault:Passthrough | CV_32FC3, CV_32FC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:Manual:FullOverlay | CV_32FC1, CV_32FC3, CV_32FC4 | FullOverlay is undefined for uncalibrated floating display ranges. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_DYNAMIC_RANGE_UNDEFINED` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:Manual:FullOverlay | CV_8UC1, CV_16UC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:Manual:FullOverlay | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:Manual:None | CV_8UC1, CV_16UC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:Manual:None | CV_32FC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:Manual:None | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:Manual:None | CV_32FC3, CV_32FC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:Manual:Passthrough | CV_8UC1, CV_16UC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:Manual:Passthrough | CV_32FC1 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:Manual:Passthrough | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:Manual:Passthrough | CV_32FC3, CV_32FC4 | Manual threshold is interpreted in the method's native score unit. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:PerMethodDefault:FullOverlay | CV_32FC1, CV_32FC3, CV_32FC4 | FullOverlay is undefined for uncalibrated floating display ranges. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_DYNAMIC_RANGE_UNDEFINED` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:PerMethodDefault:FullOverlay | CV_8UC1, CV_16UC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:PerMethodDefault:FullOverlay | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Overlay preserves admitted integer input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:PerMethodDefault:None | CV_8UC1, CV_16UC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:PerMethodDefault:None | CV_32FC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:PerMethodDefault:None | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:PerMethodDefault:None | CV_32FC3, CV_32FC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | No Image output. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:PerMethodDefault:Passthrough | CV_8UC1, CV_16UC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:PerMethodDefault:Passthrough | CV_32FC1 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:PerMethodDefault:Passthrough | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Tenengrad:PerMethodDefault:Passthrough | CV_32FC3, CV_32FC4 | Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray for score computation; output policy remains explicit. | Preserve input type. | Native intensity squared score unit. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| `Image` | ALL(OutputImagePolicy != None) | `SHARPNESS_IMAGE_POLICY` |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`0FE236CADF8A6072A6617596CB03CB84BB56BDA1C3496F87EB73B69E19757F19`
- `type:ClearVision.Product.Infrastructure.Operators.SharpnessImageContractProvider`

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Brenner` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Confidence` | `Float` | 源码输出字典初始化中可见字段。 |
| `DecisionReady` | `Any` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `Laplacian` | `Any` | 源码通过输出字典索引赋值写入。 |
| `MarginToThreshold` | `Any` | 源码通过输出字典索引赋值写入。 |
| `NormalizedScore` | `Float` | 源码通过输出字典索引赋值写入。 |
| `SMD` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ScoreStdDev` | `Float` | 源码输出字典初始化中可见字段。 |
| `ScoreStdError` | `Float` | 源码输出字典初始化中可见字段。 |
| `ScoreUnit` | `Float` | 源码输出字典初始化中可见字段。 |
| `StatusCode` | `Any` | 源码输出字典初始化中可见字段。 |
| `StatusMessage` | `String` | 源码输出字典初始化中可见字段。 |
| `Tenengrad` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ThresholdCalibration` | `Float` | 源码输出字典初始化中可见字段。 |
| `ThresholdUsed` | `Any` | 源码输出字典初始化中可见字段。 |
| `TileCount` | `Integer` | 源码输出字典初始化中可见字段。 |
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
- 执行失败契约：源码中发现 6 条 `OperatorExecutionOutput.Failure(...)` 路径。

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
| 1.1.0 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
