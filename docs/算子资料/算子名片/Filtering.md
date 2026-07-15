# 滤波 / GaussianBlur

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `GaussianBlurOperator` |
| 枚举值 (Enum) | `OperatorType.Filtering` |
| 分类 ID (CategoryId) | `ImagePreprocessing` |
| 分类 (Category) | 图像预处理 |
| 分类顺序 (CategoryOrder) | 2 |
| 版本 (Version) | `1.2.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:ImagePreprocessing`, `分类显示:图像预处理`, `生命周期:Stable`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于统一空间平滑滤波入口，支持高斯、均值/Box、中值和双边滤波；默认保持历史高斯滤波行为。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Unified spatial smoothing filters (OpenCV)` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 8 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `Cv2.GaussianBlur / Cv2.Blur / Cv2.MedianBlur / Cv2.BilateralFilter`
- `OperatorBase.Get*Param(...)`
- `Cv2.GaussianBlur`
- `Cv2.Blur`
- `Cv2.MedianBlur`
- `Cv2.BilateralFilter`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `FilterMode` | 滤波模式 | `enum` | Gaussian | Gaussian/高斯滤波；Mean/均值/Box滤波；Median/中值滤波；Bilateral/双边滤波 | Yes | 默认 Gaussian 保持旧流程行为。 |
| `KernelSize` | 核大小 | `int` | 5 | [1, 63] | Yes | Gaussian/Median 范围 1-31 且偶数核向上调整为奇数；Mean/Box 范围 1-63 并保留配置尺寸。 |
| `SigmaX` | X方向Sigma | `double` | 1 | [0.1, 10] | Yes | - |
| `SigmaY` | Y方向Sigma | `double` | 0 | [0, 10] | Yes | - |
| `BorderType` | 边界类型 | `enum` | 4 | 0/常量；1/复制；2/反射；3/环绕；4/默认 | Yes | - |
| `Diameter` | 双边直径 | `int` | 9 | [1, 25] | Yes | - |
| `SigmaColor` | 双边色彩Sigma | `double` | 75 | [1, 255] | Yes | - |
| `SigmaSpace` | 双边空间Sigma | `double` | 75 | [1, 255] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `FilterMode` | 实际滤波模式 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `FilterDiagnostics` | 滤波诊断 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `BorderType` | required; - | visible: -; hidden: ALL(FilterMode == Median) | enabled: -; disabled: ALL(FilterMode == Median) | ALL(FilterMode == Median) | - | - | `FILTERING_BORDER_NOT_USED_BY_MEDIAN` |
| `Diameter` | required; - | visible: -; hidden: ALL(FilterMode != Bilateral) | enabled: -; disabled: ALL(FilterMode != Bilateral) | ALL(FilterMode != Bilateral) | - | - | `FILTERING_BILATERAL_PARAMETERS_ONLY_FOR_BILATERAL` |
| `FilterMode` | metadata; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `FILTERING_MODE` |
| `KernelSize` | metadata; - | visible: -; hidden: ALL(FilterMode == Bilateral) | enabled: -; disabled: ALL(FilterMode == Bilateral) | ALL(FilterMode == Bilateral) | - | - | `FILTERING_KERNEL_SIZE_NOT_USED_BY_BILATERAL` |
| `SigmaColor` | required; - | visible: -; hidden: ALL(FilterMode != Bilateral) | enabled: -; disabled: ALL(FilterMode != Bilateral) | ALL(FilterMode != Bilateral) | - | - | `FILTERING_BILATERAL_PARAMETERS_ONLY_FOR_BILATERAL` |
| `SigmaSpace` | required; - | visible: -; hidden: ALL(FilterMode != Bilateral) | enabled: -; disabled: ALL(FilterMode != Bilateral) | ALL(FilterMode != Bilateral) | - | - | `FILTERING_BILATERAL_PARAMETERS_ONLY_FOR_BILATERAL` |
| `SigmaX` | metadata; - | visible: -; hidden: ALL(FilterMode != Gaussian) | enabled: -; disabled: ALL(FilterMode != Gaussian) | ALL(FilterMode != Gaussian) | - | - | `FILTERING_SIGMA_ONLY_FOR_GAUSSIAN` |
| `SigmaY` | metadata; - | visible: -; hidden: ALL(FilterMode != Gaussian) | enabled: -; disabled: ALL(FilterMode != Gaussian) | ALL(FilterMode != Gaussian) | - | - | `FILTERING_SIGMA_ONLY_FOR_GAUSSIAN` |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:61, Rejected:29, Unknown:0 | Verified production support is present. | CV_8U, CV_16U, CV_16S, CV_32F, CV_64F | CV_8U, CV_16U, CV_16S, CV_32F, CV_64F | 1, 3, 4 | Unified FilterMode selects the shared Gaussian/Mean/Median/Bilateral admission matrix. | None | Preserve input depth and channel count. | Preserve native numeric domain; floating inputs containing NaN/Infinity are rejected. | RejectNaNAndInfinity | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Bilateral | CV_8UC1, CV_8UC3 | Diameter 1..25; effective diameter=max(3,2*floor(d/2)+1); border 0/1/2/4. | `Allowed` | `VerifiedSupport` | None | Preserve input depth/channels. | Preserve native numeric domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Bilateral | CV_32FC1, CV_32FC3 | Diameter 1..25; effective diameter=max(3,2*floor(d/2)+1); border 0/1/2/4. | `Allowed` | `VerifiedSupport` | None | Preserve input depth/channels. | Preserve native numeric domain. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Bilateral | CV_8UC4, CV_32FC4 | The installed bilateral path admits C1/C3 only. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_CHANNELS_UNSUPPORTED` | `E2_EXECUTABLE_PROBE` |
| `Image` | Bilateral | CV_16UC1, CV_16UC3, CV_16UC4, CV_16SC1, CV_16SC3, CV_16SC4, CV_64FC1, CV_64FC3, CV_64FC4 | The installed bilateral path admits CV_8U/CV_32F only. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_DEPTH_UNSUPPORTED` | `E2_EXECUTABLE_PROBE` |
| `Image` | Gaussian | CV_8UC1, CV_8UC3, CV_8UC4, CV_16UC1, CV_16UC3, CV_16UC4, CV_16SC1, CV_16SC3, CV_16SC4 | KernelSize 1..31; effective kernel is odd; border 0/1/2/4. | `Allowed` | `VerifiedSupport` | None | Preserve input depth/channels. | Preserve native numeric domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Gaussian | CV_32FC1, CV_32FC3, CV_32FC4, CV_64FC1, CV_64FC3, CV_64FC4 | KernelSize 1..31; effective kernel is odd; border 0/1/2/4. | `Allowed` | `VerifiedSupport` | None | Preserve input depth/channels. | Preserve native numeric domain. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Mean | CV_8UC1, CV_8UC3, CV_8UC4, CV_16UC1, CV_16UC3, CV_16UC4, CV_16SC1, CV_16SC3, CV_16SC4 | KernelSize 1..63; even kernels remain even; border 0/1/2/4. | `Allowed` | `VerifiedSupport` | None | Preserve input depth/channels. | Preserve native numeric domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Mean | CV_32FC1, CV_32FC3, CV_32FC4, CV_64FC1, CV_64FC3, CV_64FC4 | KernelSize 1..63; even kernels remain even; border 0/1/2/4. | `Allowed` | `VerifiedSupport` | None | Preserve input depth/channels. | Preserve native numeric domain. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Median:Identity | CV_8UC1, CV_8UC3, CV_8UC4, CV_16UC1, CV_16UC3, CV_16UC4, CV_16SC1, CV_16SC3, CV_16SC4 | Effective kernel equals 1 and behaves as identity. | `Allowed` | `VerifiedSupport` | None | Preserve input depth/channels. | Preserve native numeric domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Median:Identity | CV_32FC1, CV_32FC3, CV_32FC4, CV_64FC1, CV_64FC3, CV_64FC4 | Effective kernel equals 1 and behaves as identity. | `Allowed` | `VerifiedSupport` | None | Preserve input depth/channels. | Preserve native numeric domain. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Median:Kernel3Or5 | CV_8UC1, CV_8UC3, CV_8UC4, CV_16UC1, CV_16UC3, CV_16UC4 | Effective kernel is 3 or 5. | `Allowed` | `VerifiedSupport` | None | Preserve input depth/channels. | Preserve native numeric domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Median:Kernel3Or5 | CV_32FC1, CV_32FC3, CV_32FC4 | Effective kernel is 3 or 5. | `Allowed` | `VerifiedSupport` | None | Preserve input depth/channels. | Preserve native numeric domain. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Median:Kernel3Or5 | CV_16SC1, CV_16SC3, CV_16SC4, CV_64FC1, CV_64FC3, CV_64FC4 | The installed median kernel 3/5 path admits only 8U/16U/32F. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_MODE_DEPTH_UNSUPPORTED` | `E2_EXECUTABLE_PROBE` |
| `Image` | Median:Kernel7Plus | CV_8UC1, CV_8UC3, CV_8UC4 | Effective kernel is >=7 and <=31. | `Allowed` | `VerifiedSupport` | None | Preserve input depth/channels. | Preserve native numeric domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Median:Kernel7Plus | CV_16UC1, CV_16UC3, CV_16UC4, CV_16SC1, CV_16SC3, CV_16SC4, CV_32FC1, CV_32FC3, CV_32FC4, CV_64FC1, CV_64FC3, CV_64FC4 | The installed median kernels >=7 path admits CV_8U only. | `Rejected` | `VerifiedRejection` | None | No output; rejected before the native image call. | Not applicable. | `Any` | `IMAGE_MODE_DEPTH_UNSUPPORTED` | `E2_EXECUTABLE_PROBE` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| `FilterDiagnostics` | - | `FILTERING_OUTPUT` |
| `FilterMode` | - | `FILTERING_OUTPUT` |
| `Image` | - | `FILTERING_OUTPUT` |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`DC2DDAD5E61261B2949E1A3856F0BE29C5D2FBFFBE9DAF8FBE5BC1CB7316AB23`
- `type:ClearVision.Product.Infrastructure.Operators.SpatialFilterImageContractProvider`
- `type:ClearVision.Product.Infrastructure.Operators.SpatialFilterKernel`

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `BorderTypeApplied` | `String` | 源码通过输出字典索引赋值写入。 |
| `DiameterApplied` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `KernelSizeApplied` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Mode` | `String` | 源码通过输出字典索引赋值写入。 |
| `SigmaColorApplied` | `Any` | 源码通过输出字典索引赋值写入。 |
| `SigmaSpaceApplied` | `Any` | 源码通过输出字典索引赋值写入。 |
| `SigmaXApplied` | `Float` | 源码通过输出字典索引赋值写入。 |
| `SigmaYApplied` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H*K^2) |
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
| 1.2.0 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
