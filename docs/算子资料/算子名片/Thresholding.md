# 全局阈值处理 / Threshold

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ThresholdOperator` |
| 枚举值 (Enum) | `OperatorType.Thresholding` |
| 分类 ID (CategoryId) | `SegmentationAndRegion` |
| 分类 (Category) | 分割与区域 |
| 分类顺序 (CategoryOrder) | 3 |
| 版本 (Version) | `1.1.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:SegmentationAndRegion`, `分类显示:分割与区域`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于执行全局阈值处理，支持二值、反二值、截断、ToZero 以及 Otsu/Triangle 自动阈值。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 4 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.CvtColor`
- `Cv2.Threshold`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Threshold` | 阈值 | `double` | 127 | - | Yes | 输入像素数值域中的阈值；合法范围在运行时按 Mat 位深校验。 |
| `MaxValue` | Max Value | `double` | 255 | - | Yes | 输出像素数值域中的最大值；合法范围在运行时按 Mat 位深校验。 |
| `Type` | Type | `enum` | 0 | 0/二值；1/二值反转；2/截断；3/置零；4/置零反转；8/大津法；16/三角形 | Yes | - |
| `UseOtsu` | Use Otsu | `bool` | false | - | Yes | 兼容旧工程的 Otsu 标志；true 时向 Type 添加 Otsu，不覆盖基础 Binary/BinaryInv 模式。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `UseOtsu` | metadata; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `THRESHOLD_USE_OTSU_COMPATIBILITY_ALIAS` |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 状态 | 支持位深 | 原生位深 | 支持通道 | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 失败码 | 证据 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | `Restricted` | CV_8U, CV_16U, CV_16S, CV_32F, CV_64F | CV_8U, CV_16U, CV_16S, CV_32F, CV_64F | 1, 3, 4 | Fixed modes accept native C1 values for 8U/16U/16S/32F/64F. C3/C4 are explicitly converted to gray only for 8U/16U/32F. | C3 BGR or C4 BGRA -> C1 Gray without depth scaling; no implicit depth conversion. | C1 output preserving the admitted gray input depth. | Threshold and ActualThreshold use the input pixel numeric domain; MaxValue uses the output numeric domain. | RejectNaNAndInfinity | `IMAGE_DEPTH_UNSUPPORTED` | `E2_EXECUTABLE_PROBE` | `2.0` |

### 模式限制 / Mode Restrictions
| 输入端口 | 模式 | 状态 | 位深 | 通道 | 转换 | 输出 | 动态范围 | 条件 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Fixed:Binary/BinaryInv/Trunc/ToZero/ToZeroInv | `Restricted` | CV_8U, CV_16U, CV_16S, CV_32F, CV_64F | 1, 3, 4 | Color conversion only; C1 is native. | Preserve admitted input depth; output C1. | Native numeric domain. | C3/C4 exclude CV_16S and CV_64F because OpenCV gray conversion does not admit those depths. | `IMAGE_MODE_DEPTH_UNSUPPORTED` | `E2_EXECUTABLE_PROBE` |
| `Image` | Otsu | `Restricted` | CV_8U, CV_16U | 1, 3, 4 | Color -> Gray; UseOtsu is a compatibility alias for the Otsu flag. | Preserve admitted input depth; output C1. | ActualThreshold uses the native 8U or 16U input domain. | Only Binary and BinaryInv base modes. | `IMAGE_MODE_DEPTH_UNSUPPORTED` | `E2_EXECUTABLE_PROBE` |
| `Image` | Triangle | `Restricted` | CV_8U | 1, 3, 4 | Color -> Gray. | CV_8UC1. | 8-bit input domain. | Only Binary and BinaryInv base modes. | `IMAGE_MODE_DEPTH_UNSUPPORTED` | `E2_EXECUTABLE_PROBE` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`ECB906B9B239D379B2937FF5D330F2A0C40166DD97251C1B5EBA1742808EDFBA`
- `type:ClearVision.Product.Infrastructure.Operators.ThresholdImageContractProvider`

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `ActualThreshold` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ColorConversion` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `InputMatType` | `String` | 源码通过输出字典索引赋值写入。 |
| `OtsuThreshold` | `Any` | 源码通过输出字典索引赋值写入。 |
| `OutputDepthPolicy` | `Any` | 源码通过输出字典索引赋值写入。 |
| `OutputMatType` | `String` | 源码通过输出字典索引赋值写入。 |
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
- 执行失败契约：源码中发现 4 条 `OperatorExecutionOutput.Failure(...)` 路径。

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
