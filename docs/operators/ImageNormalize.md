# 图像归一化 / ImageNormalize

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageNormalizeOperator` |
| 枚举值 (Enum) | `OperatorType.ImageNormalize` |
| 分类 ID (CategoryId) | `ImagePreprocessing` |
| 分类 (Category) | 图像预处理 |
| 分类顺序 (CategoryOrder) | 2 |
| 版本 (Version) | `1.0.3` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:ImagePreprocessing`, `分类显示:图像预处理`, `生命周期:Stable`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于MinMax 映射像素范围；ZScore 返回均值约 0、总体标准差约 1 的浮点标准分。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `MinMax range normalization / floating ZScore standardization / histogram equalization` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 4 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `Cv2.Normalize / Cv2.MeanStdDev / Cv2.Subtract / Cv2.Divide / Cv2.EqualizeHist`
- `OperatorBase.Get*Param(...)`
- `Cv2.Normalize`
- `Cv2.MeanStdDev`
- `Cv2.Subtract`
- `Cv2.Divide`
- `Cv2.EqualizeHist`
- `Cv2.CheckRange`
- `Cv2.Split`
- `Cv2.Merge`
- `Cv2.CvtColor`
- `Cv2.MinMaxLoc`
- `Math.Min`
- `Math.Max`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Method` | 方法 | `enum` | MinMax | MinMax/最小最大；ZScore/Z分数；Histogram/直方图 | Yes | MinMax 按目标范围映射；ZScore 返回浮点标准分；Histogram 执行 8 位直方图均衡。 |
| `Alpha` | Alpha | `double` | 0 | [-10000, 10000] | Yes | 仅用于 MinMax 的目标下界。 |
| `Beta` | Beta系数 | `double` | 255 | [-10000, 10000] | Yes | 仅用于 MinMax 的目标上界。 |
| `ColorMode` | Color Mode | `enum` | LumaOnly | LumaOnly；PerChannel | Yes | PerChannel 独立处理三个颜色通道；彩色 ZScore 不支持 LumaOnly，需显式选择 PerChannel。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Method` | 实际归一化方法 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ColorMode` | 实际颜色模式 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `Channels` | 输出通道数 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `OutputMatType` | 输出 Mat 类型 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `SigmaDegenerate` | 标准差退化 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `Alpha` | metadata; - | visible: -; hidden: ALL(Method != MinMax) | enabled: -; disabled: ALL(Method != MinMax) | ALL(Method != MinMax) | - | - | `IMAGE_NORMALIZE_RANGE_ONLY_FOR_MINMAX` |
| `Beta` | metadata; - | visible: -; hidden: ALL(Method != MinMax) | enabled: -; disabled: ALL(Method != MinMax) | ALL(Method != MinMax) | - | - | `IMAGE_NORMALIZE_RANGE_ONLY_FOR_MINMAX` |
| `ColorMode` | metadata; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `IMAGE_NORMALIZE_COLOR_MODE` |
| `Method` | metadata; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `IMAGE_NORMALIZE_METHOD` |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:30, Rejected:6, Unknown:0 | Verified production support is present. | CV_8U, CV_16U, CV_32F, CV_64F | CV_8U, CV_16U, CV_32F, CV_64F | 1, 3 | Admission is exact for Method + effective ColorMode + Depth + Channels. | Only explicitly listed normalization conversions are allowed. | MinMax preserves depth; ZScore outputs CV_32F; Histogram outputs CV_8U. | Explicit business-semantic normalization; no generic implicit MinMax conversion. | ModeSpecific | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Histogram:Gray | CV_8UC1, CV_16UC1 | C1 input in the explicit histogram-normalization business mode. | `Allowed` | `VerifiedConversion` | Explicit conversion to the 8-bit equalization domain. | CV_8U. | 8-bit equalization domain. | `Any` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | Histogram:Gray | CV_32FC1, CV_64FC1 | C1 input in the explicit histogram-normalization business mode. | `Allowed` | `VerifiedConversion` | Explicit conversion to the 8-bit equalization domain. | CV_8U. | 8-bit equalization domain. | `RejectNonFinite` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | Histogram:LumaOnly | CV_8UC3, CV_16UC3 | BGR->YUV luma equalization with explicit 8-bit fallback when required. | `Allowed` | `VerifiedConversion` | BGR->YUV, explicit 8-bit luma equalization, YUV->BGR. | CV_8UC3 when luma conversion requires the explicit byte domain. | 8-bit equalization domain. | `Any` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | Histogram:LumaOnly | CV_32FC3 | BGR->YUV luma equalization with explicit 8-bit fallback when required. | `Allowed` | `VerifiedConversion` | BGR->YUV, explicit 8-bit luma equalization, YUV->BGR. | CV_8UC3 when luma conversion requires the explicit byte domain. | 8-bit equalization domain. | `RejectNonFinite` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | Histogram:LumaOnly | CV_64FC3 | The installed BGR<->YUV conversion path does not admit CV_64F. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_MODE_DEPTH_UNSUPPORTED` | `E2_STAGE2_CLOSURE` |
| `Image` | Histogram:PerChannel | CV_8UC3, CV_16UC3 | Each channel is explicitly converted to and equalized in CV_8U. | `Allowed` | `VerifiedConversion` | Split, explicit 8-bit histogram normalization, merge. | CV_8UC3. | Per-channel 8-bit equalization domain. | `Any` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | Histogram:PerChannel | CV_32FC3, CV_64FC3 | Each channel is explicitly converted to and equalized in CV_8U. | `Allowed` | `VerifiedConversion` | Split, explicit 8-bit histogram normalization, merge. | CV_8UC3. | Per-channel 8-bit equalization domain. | `RejectNonFinite` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | MinMax:Gray | CV_8UC1, CV_16UC1 | C1 input; ColorMode is validated but has no channel-selection effect. | `Allowed` | `VerifiedSupport` | Explicit MinMax normalization. | Preserve input depth. | Explicit Alpha/Beta target range. | `Any` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | MinMax:Gray | CV_32FC1, CV_64FC1 | C1 input; ColorMode is validated but has no channel-selection effect. | `Allowed` | `VerifiedSupport` | Explicit MinMax normalization. | Preserve input depth. | Explicit Alpha/Beta target range. | `RejectNonFinite` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | MinMax:LumaOnly | CV_8UC3, CV_16UC3 | BGR->YUV luma normalization; CvtColor supports 8U/16U/32F. | `Allowed` | `VerifiedConversion` | BGR->YUV, normalize Y, YUV->BGR. | Preserve input depth and C3. | Luma Alpha/Beta target range. | `Any` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | MinMax:LumaOnly | CV_32FC3 | BGR->YUV luma normalization; CvtColor supports 8U/16U/32F. | `Allowed` | `VerifiedConversion` | BGR->YUV, normalize Y, YUV->BGR. | Preserve input depth and C3. | Luma Alpha/Beta target range. | `RejectNonFinite` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | MinMax:LumaOnly | CV_64FC3 | The installed BGR<->YUV conversion path does not admit CV_64F. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_MODE_DEPTH_UNSUPPORTED` | `E2_STAGE2_CLOSURE` |
| `Image` | MinMax:PerChannel | CV_8UC3, CV_16UC3 | Each channel is normalized independently. | `Allowed` | `VerifiedSupport` | Split/normalize/merge without depth conversion. | Preserve input depth and C3. | Per-channel Alpha/Beta target range. | `Any` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | MinMax:PerChannel | CV_32FC3, CV_64FC3 | Each channel is normalized independently. | `Allowed` | `VerifiedSupport` | Split/normalize/merge without depth conversion. | Preserve input depth and C3. | Per-channel Alpha/Beta target range. | `RejectNonFinite` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | ZScore:Gray | CV_8UC1, CV_16UC1 | C1 input; finite values must be representable after the explicit CV_32F narrowing. | `Allowed` | `VerifiedConversion` | Explicit conversion to CV_32F before z-score. | CV_32F. | Mean 0 / population sigma 1 when non-degenerate. | `Any` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | ZScore:Gray | CV_32FC1 | C1 input; finite values must be representable after the explicit CV_32F narrowing. | `Allowed` | `VerifiedConversion` | Explicit conversion to CV_32F before z-score. | CV_32F. | Mean 0 / population sigma 1 when non-degenerate. | `RejectNonFinite` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | ZScore:Gray | CV_64FC1 | C1 input; finite values must be representable after the explicit CV_32F narrowing. | `Allowed` | `VerifiedConversion` | Explicit conversion to CV_32F before z-score. | CV_32F. | Mean 0 / population sigma 1 when non-degenerate. | `RequireFiniteFloat32Representable` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | ZScore:LumaOnly | CV_8UC3, CV_16UC3, CV_32FC3, CV_64FC3 | Color ZScore requires ColorMode=PerChannel. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_MODE_UNSUPPORTED` | `E2_STAGE1_REGRESSION` |
| `Image` | ZScore:PerChannel | CV_8UC3, CV_16UC3 | Each channel is narrowed to CV_32F and standardized independently. | `Allowed` | `VerifiedConversion` | Split, explicit CV_32F conversion, z-score, merge. | CV_32FC3. | Per-channel mean 0 / population sigma 1 when non-degenerate. | `Any` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | ZScore:PerChannel | CV_32FC3 | Each channel is narrowed to CV_32F and standardized independently. | `Allowed` | `VerifiedConversion` | Split, explicit CV_32F conversion, z-score, merge. | CV_32FC3. | Per-channel mean 0 / population sigma 1 when non-degenerate. | `RejectNonFinite` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |
| `Image` | ZScore:PerChannel | CV_64FC3 | Each channel is narrowed to CV_32F and standardized independently. | `Allowed` | `VerifiedConversion` | Split, explicit CV_32F conversion, z-score, merge. | CV_32FC3. | Per-channel mean 0 / population sigma 1 when non-degenerate. | `RequireFiniteFloat32Representable` | `IMAGE_NORMALIZE_NONFINITE_INPUT` | `E2_STAGE2_CLOSURE` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| `Channels` | - | `IMAGE_NORMALIZE_OUTPUT` |
| `ColorMode` | - | `IMAGE_NORMALIZE_OUTPUT` |
| `Image` | - | `IMAGE_NORMALIZE_OUTPUT` |
| `Method` | - | `IMAGE_NORMALIZE_OUTPUT` |
| `OutputMatType` | - | `IMAGE_NORMALIZE_OUTPUT` |
| `SigmaDegenerate` | - | `IMAGE_NORMALIZE_OUTPUT` |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`F76410BF26206E59C4061C01FD677C18040D65346BF2FBE3412604C891E63ACD`
- `type:ClearVision.Product.Infrastructure.Operators.ImageNormalizeImageContractProvider`

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H*C) |
| 典型耗时 (Typical Latency) | 未固定；取决于图像分辨率、ROI 范围、OpenCV 算法分支和输出可视化成本。 |
| 内存特征 (Memory Profile) | O(W*H*C) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 5 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：MinMax for bounded display or downstream range contracts
- 适合 (Suitable)：ZScore for statistical standardization before floating-point processing
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. Histogram mode converts non-8U inputs to an 8-bit equalization domain
2. Color ZScore requires ColorMode=PerChannel
3. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
4. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
5. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.3 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
