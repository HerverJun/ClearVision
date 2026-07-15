# GLCM纹理特征 / GlcmTexture

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `GlcmTextureOperator` |
| 枚举值 (Enum) | `OperatorType.GlcmTexture` |
| 分类 ID (CategoryId) | `FeatureExtraction` |
| 分类 (Category) | 特征提取 |
| 分类顺序 (CategoryOrder) | 4 |
| 版本 (Version) | `1.0.2` |
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
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:FeatureExtraction`, `分类显示:特征提取`, `生命周期:Stable`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于计算灰度共生矩阵（GLCM）纹理特征。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Quantized gray-level co-occurrence matrix` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 9 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `ROI -> GlcmTexture.Compute -> quantize gray image -> per-direction GLCM -> averaged Haralick features`
- `OperatorBase.Get*Param(...)`
- `Math.Clamp`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Levels` | 量化级数 | `int` | 16 | [2, 256] | Yes | - |
| `Distance` | 距离 | `int` | 1 | [1, 64] | Yes | - |
| `DirectionsDeg` | 方向（度） | `string` | 0,45,90,135 | - | Yes | - |
| `Symmetric` | 对称 | `bool` | true | - | Yes | - |
| `Normalize` | 归一化 | `bool` | true | - | Yes | - |
| `RoiX` | ROIX | `int` | 0 | >= 0 | Yes | - |
| `RoiY` | ROIY | `int` | 0 | >= 0 | Yes | - |
| `RoiW` | ROI宽 | `int` | 0 | >= 0 | Yes | - |
| `RoiH` | ROI高 | `int` | 0 | >= 0 | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Contrast` | Contrast | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Correlation` | Correlation | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Energy` | Energy | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Homogeneity` | Homogeneity | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Entropy` | Entropy | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `PerDirection` | Per Direction Features | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:9, Rejected:0, Unknown:0 | Verified production support is present. | CV_8U, CV_16U, CV_32F | CV_8U | 1, 3, 4 | Exact support is declared for the verified gray conversion and quantization path. | Explicit color-to-gray conversion followed by per-ROI affine range mapping to CV_8U. | No image output. | Per-ROI 8-bit quantization domain. | RejectNaNAndInfinityForFloatingVariants | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Default | CV_8UC1 | The selected ROI is converted to gray and explicitly quantized to an 8-bit affine range domain before GLCM construction. | `Allowed` | `VerifiedSupport` | C3/C4 -> Gray; non-8U gray -> CV_32F -> per-ROI affine range mapping -> CV_8U. | No image output. | Texture features are computed in the explicit per-ROI 8-bit quantization domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Default | CV_8UC3, CV_8UC4, CV_16UC1, CV_16UC3, CV_16UC4 | The selected ROI is converted to gray and explicitly quantized to an 8-bit affine range domain before GLCM construction. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray; non-8U gray -> CV_32F -> per-ROI affine range mapping -> CV_8U. | No image output. | Texture features are computed in the explicit per-ROI 8-bit quantization domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Default | CV_32FC1, CV_32FC3, CV_32FC4 | The selected ROI is converted to gray and explicitly quantized to an 8-bit affine range domain before GLCM construction. | `Allowed` | `VerifiedConversion` | C3/C4 -> Gray; non-8U gray -> CV_32F -> per-ROI affine range mapping -> CV_8U. | No image output. | Texture features are computed in the explicit per-ROI 8-bit quantization domain. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`BB1DD1681A4A91CA18CC57A46C3CF25B2891F9A612481FDCF242B56D363CDFA9`
- `type:ClearVision.Product.Infrastructure.Operators.GlcmTextureImageContractProvider`

### 运行时附加输出 / Runtime Additional Outputs
- 未在源码中发现除声明输出端口外的稳定附加输出字段；下游连线以输出端口表为准。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(D*(W*H+L^2)) |
| 典型耗时 (Typical Latency) | No dedicated golden benchmark yet; covered by texture unit and integration tests |
| 内存特征 (Memory Profile) | O(L^2) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 5 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Texture inspection where contrast, energy, homogeneity, entropy, and correlation are meaningful summary features.
- 适合 (Suitable)：ROI-based material or surface comparison with fixed quantization and direction settings.
- 不适合 (Not Suitable)：Rotation-invariant texture classification without downstream aggregation or augmentation.
- 不适合 (Not Suitable)：Large images with high quantization levels when per-frame latency is tightly bounded.

## 已知限制 / Known Limitations
1. Supported directions are currently limited to 0, 45, 90, and 135 degrees.
2. The operator reports statistical texture features only; it does not classify texture defects by itself.
3. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
4. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.2 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
