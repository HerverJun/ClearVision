# 拉普拉斯锐化 / LaplacianSharpen

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `LaplacianSharpenOperator` |
| 枚举值 (Enum) | `OperatorType.LaplacianSharpen` |
| 分类 ID (CategoryId) | `ImagePreprocessing` |
| 分类 (Category) | 图像预处理 |
| 分类顺序 (CategoryOrder) | 2 |
| 版本 (Version) | `1.0.3` |
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
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:ImagePreprocessing`, `分类显示:图像预处理`, `生命周期:Stable`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于在浮点域保留拉普拉斯响应符号，并按 dst = src - strength × laplacian 锐化。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Signed Laplacian sharpening` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 3 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `Cv2.CvtColor / Cv2.Laplacian / Cv2.AddWeighted / Mat.ConvertTo`
- `OperatorBase.Get*Param(...)`
- `Cv2.CvtColor`
- `Cv2.Laplacian`
- `Cv2.AddWeighted`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `KernelSize` | 核大小 | `int` | 3 | [1, 7] | Yes | 范围 1-7；偶数按兼容规则向上规范化为下一奇数，并在输出元数据返回实际值。 |
| `Scale` | 缩放因子 | `double` | 1 | [0.1, 10] | Yes | 缩放有符号 Laplacian 响应。 |
| `SharpenStrength` | 锐化强度 | `double` | 1 | [0, 5] | Yes | 公式 dst = src - SharpenStrength × laplacian；0 为严格恒等。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 锐化图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `KernelSize` | 实际核大小 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Scale` | 实际拉普拉斯缩放 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `SharpenStrength` | 实际锐化强度 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `OutputMatType` | 输出 Mat 类型 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ColorPolicy` | 彩色图策略 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `KernelSize` | metadata; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `LAPLACIAN_KERNEL_SIZE` |
| `Scale` | metadata; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `LAPLACIAN_SCALE` |
| `SharpenStrength` | metadata; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `LAPLACIAN_SHARPEN_STRENGTH` |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:6, Rejected:2, Unknown:0 | Verified production support is present. | CV_8U, CV_16U, CV_32F | CV_8U, CV_16U, CV_32F | 1, 3 | Stage 1 native-value Laplacian sharpening contract. | Color -> Gray for derivative computation; result is restored in the source numeric domain. | Preserve source depth and channel count. | No MinMax conversion; native-domain Laplacian response. | RejectNaNAndInfinityForFloatingVariants | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Default | CV_8UC1, CV_16UC1 | Stage 1 native-value Laplacian sharpening path. | `Allowed` | `VerifiedSupport` | Color -> Gray for derivative computation; restore result in source domain. | Preserve source depth and channel count. | No MinMax conversion; native-domain Laplacian response. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_STAGE1_REGRESSION` |
| `Image` | Default | CV_32FC1 | Stage 1 native-value Laplacian sharpening path. | `Allowed` | `VerifiedSupport` | Color -> Gray for derivative computation; restore result in source domain. | Preserve source depth and channel count. | No MinMax conversion; native-domain Laplacian response. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_STAGE1_REGRESSION` |
| `Image` | Default | CV_8UC3, CV_16UC3 | Stage 1 native-value Laplacian sharpening path. | `Allowed` | `VerifiedConversion` | Color -> Gray for derivative computation; restore result in source domain. | Preserve source depth and channel count. | No MinMax conversion; native-domain Laplacian response. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_STAGE1_REGRESSION` |
| `Image` | Default | CV_32FC3 | Stage 1 native-value Laplacian sharpening path. | `Allowed` | `VerifiedConversion` | Color -> Gray for derivative computation; restore result in source domain. | Preserve source depth and channel count. | No MinMax conversion; native-domain Laplacian response. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_STAGE1_REGRESSION` |
| `Image` | Default | CV_64FC1, CV_64FC3 | The Stage 1 Laplacian contract intentionally excludes CV_64F. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_DEPTH_UNSUPPORTED` | `E2_STAGE1_REGRESSION` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| `ColorPolicy` | - | `LAPLACIAN_SHARPEN_OUTPUT` |
| `Image` | - | `LAPLACIAN_SHARPEN_OUTPUT` |
| `KernelSize` | - | `LAPLACIAN_SHARPEN_OUTPUT` |
| `OutputMatType` | - | `LAPLACIAN_SHARPEN_OUTPUT` |
| `Scale` | - | `LAPLACIAN_SHARPEN_OUTPUT` |
| `SharpenStrength` | - | `LAPLACIAN_SHARPEN_OUTPUT` |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`5FEA5C648C6BBA2E5A2697EC32535C0C1911926620CA771CBEE53726A8AA2E71`
- `type:ClearVision.Product.Infrastructure.Operators.LaplacianSharpenImageContractProvider`

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H*K^2) |
| 典型耗时 (Typical Latency) | 未固定；取决于图像分辨率、ROI 范围、OpenCV 算法分支和输出可视化成本。 |
| 内存特征 (Memory Profile) | O(W*H*C) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 4 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Second-derivative sharpening for grayscale and BGR industrial images
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. Supported input depths are 8U, 16U and 32F
2. Color sharpening uses a luminance-broadcast correction rather than independent channel Laplacians
3. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
4. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
5. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.3 | 2026-07-16 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
