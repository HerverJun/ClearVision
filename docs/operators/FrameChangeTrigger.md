# 帧变化触发 / FrameChangeTrigger

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `FrameChangeTriggerOperator` |
| 枚举值 (Enum) | `OperatorType.FrameChangeTrigger` |
| 分类 ID (CategoryId) | `FlowControl` |
| 分类 (Category) | 流程控制 |
| 分类顺序 (CategoryOrder) | 12 |
| 版本 (Version) | `1.0.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:FlowControl`, `分类显示:流程控制`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于通过连续帧 ROI 变化判断端子是否到达；未到料时短路当前检测周期，避免空帧进入深度学习。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 20 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 源码包含短路输出路径，可在条件不满足时阻止后续节点继续执行。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `Math.Max`
- `Convert.ToInt32`
- `Convert.ToDouble`
- `Convert.ToBoolean`
- `Convert.ToString`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Enabled` | 启用检测 | `bool` | true | - | Yes | 关闭后图像直接放行，不做帧差判断。 |
| `ShortCircuitWhenNotTriggered` | 未触发时跳过本轮 | `bool` | true | - | Yes | 开启后，未检测到到料变化时短路当前流程，不执行后续 YOLO 和结果输出。 |
| `Profile` | 参数配置档 | `enum` | line_fast_default | line_fast_default；line_noise_guard；line_low_contrast | Yes | 默认 line_fast_default；line_noise_guard 和 line_low_contrast 必须作为证据 profile 显式启用。 |
| `PixelThreshold` | 像素差阈值 | `int` | 30 | [1, 255] | Yes | 单个像素灰度差超过该值才计入变化区域。现场反光/抖动多时可适当调高。 |
| `MinChangeRatio` | 最小变化比例 | `double` | 0.02 | [0, 1] | Yes | ROI 内变化像素占比达到该值才认为到料。误触发多时调高，漏检时调低。 |
| `MinChangePixels` | 最小变化像素数 | `int` | 500 | >= 0 | Yes | ROI 内变化像素数量下限，用于过滤小面积噪声。 |
| `CooldownMs` | 冷却时间(ms) | `int` | 1200 | [0, 60000] | Yes | 触发后在该时间内抑制重复触发，防止同一端子停留期间重复判定。 |
| `RoiX` | 检测区域X | `int` | 0 | >= 0 | Yes | 到料检测 ROI 左上角 X。 |
| `RoiY` | 检测区域Y | `int` | 0 | >= 0 | Yes | 到料检测 ROI 左上角 Y。 |
| `RoiW` | 检测区域宽度 | `int` | 0 | >= 0 | Yes | 到料检测 ROI 宽度；0 表示从 X 到图像右边界。 |
| `RoiH` | 检测区域高度 | `int` | 0 | >= 0 | Yes | 到料检测 ROI 高度；0 表示从 Y 到图像下边界。 |
| `BlurSize` | 降噪模糊核 | `int` | 0 | [0, 15] | Yes | 0 表示关闭；开启时必须为 3 到 15 的奇数。 |
| `MorphOpenSize` | 开运算核 | `int` | 0 | [0, 15] | Yes | 0 表示关闭；开启时必须为 3 到 15 的奇数，用于去除孤立噪声。 |
| `NormalizeMode` | 亮度归一化 | `enum` | None | None/无；MeanShift；PercentileClip | Yes | None、MeanShift 或 PercentileClip。 |
| `ReferenceUpdateMode` | 参考帧更新 | `enum` | PreviousFrame | PreviousFrame；StableBackground；ExponentialMovingAverage | Yes | PreviousFrame、StableBackground 或 ExponentialMovingAverage。 |
| `ReferenceUpdateAlpha` | 参考更新系数 | `double` | 0.05 | [0, 1] | Yes | 仅 ExponentialMovingAverage 使用，范围 0 到 1。 |
| `AdaptivePixelThreshold` | 自适应像素阈值 | `bool` | false | - | Yes | 默认关闭；低对比 evidence profile 可显式启用。 |
| `MinConsecutiveChangedFrames` | 连续变化帧数 | `int` | 1 | >= 1 | Yes | 至少连续多少帧达到变化阈值才触发，用于抑制单帧闪烁。 |
| `ResetAfterNoChangeFrames` | 无变化复位帧数 | `int` | 1 | >= 0 | Yes | 连续无变化达到该帧数后复位边沿状态；0 表示关闭。 |
| `TriggerOnRisingEdgeOnly` | 仅上升沿触发 | `bool` | true | - | Yes | 开启后持续变化只在进入变化状态的边沿触发一次。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 输出图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Triggered` | 是否触发 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `ChangeScore` | 变化比例 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `ChangedPixels` | 变化像素数 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Reason` | 判定原因 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `BaselineReady` | 基线已建立 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `TotalPixels` | 有效像素数 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `CooldownRemainingMs` | 剩余冷却时间(ms) | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `EffectivePixelThreshold` | 有效像素差阈值 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `EffectiveMinChangeRatio` | 有效最小变化比例 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 状态 | 支持位深 | 原生位深 | 支持通道 | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 失败码 | 证据 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | `Restricted` | CV_8U | CV_8U | 1, 3, 4 | Stage 2 conservative baseline: retain evidenced legacy 8U paths; reject higher depths until operator-specific evidence is added. | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit native numeric domain; no implicit MinMax conversion. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `E0_SOURCE_AUDIT` | `2.0` |

### 模式限制 / Mode Restrictions
| 输入端口 | 模式 | 状态 | 位深 | 通道 | 转换 | 输出 | 动态范围 | 条件 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - | - | - | - |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`CD69E83E49C90C934F77C55956269005FE803C5CAC56559B690B0BEB0518030A`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `ConsecutiveChangedFrames` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `NoChangeFrames` | `Any` | 源码通过输出字典索引赋值写入。 |
| `NoMaterialFrame` | `Any` | 源码通过输出字典索引赋值写入。 |
| `StateKey` | `Any` | 源码通过输出字典索引赋值写入。 |
| `StateScope` | `Any` | 源码通过输出字典索引赋值写入。 |
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
- 执行失败契约：源码中发现 3 条 `OperatorExecutionOutput.Failure(...)` 路径。
- 短路契约：算子可返回 `ShortCircuit`，用于阻止后续节点在当前周期继续执行。

## 适用场景 / Use Cases
- 适合 (Suitable)：需要对上游结果做判断、转换、聚合、计数、延时或流程路由的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
3. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
4. 源码包含状态缓存或实例级状态，长流程运行时需要关注状态清理、并发调用和实例复用边界。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
