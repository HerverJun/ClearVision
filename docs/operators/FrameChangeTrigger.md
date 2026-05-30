# 帧变化触发 / FrameChangeTrigger

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `FrameChangeTriggerOperator` |
| 枚举值 (Enum) | `OperatorType.FrameChangeTrigger` |
| 分类 (Category) | 逻辑工具 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于通过连续帧 ROI 变化判断端子是否到达；未到料时短路当前检测周期，避免空帧进入深度学习。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
核心算法已收敛到 `FrameChangeTriggerKernel`：先裁剪 ROI 并转灰度，可选执行模糊、形态学开运算和亮度归一化，再依据像素差阈值、最小变化比例、冷却期、连续变化帧数和上升沿语义输出触发决策。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 20 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含短路输出路径，可在条件不满足时阻止后续节点继续执行。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `FrameChangeTriggerKernel.ResolveRoi(...)`
- `FrameChangeTriggerKernel.BuildGrayRoi(...)`
- `FrameChangeTriggerKernel.Evaluate(...)`
- `Cv2.Absdiff`
- `Cv2.Threshold`
- `Cv2.GaussianBlur`
- `Cv2.MorphologyEx`
- `Cv2.MeanStdDev`
- `Cv2.CountNonZero`
- `Math.Max`
- `Math.Clamp`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Enabled` | 启用检测 | `bool` | true | - | Yes | 关闭后图像直接放行，不做帧差判断。 |
| `ShortCircuitWhenNotTriggered` | 未触发时跳过本轮 | `bool` | true | - | Yes | 开启后，未检测到到料变化时短路当前流程，不执行后续 YOLO 和结果输出。 |
| `Profile` | 参数配置档 | `enum` | line_fast_default | line_fast_default / line_noise_guard / line_low_contrast | Yes | 模板默认使用 line_fast_default；其他 profile 需显式选择。 |
| `PixelThreshold` | 像素差阈值 | `int` | 30 | [1, 255] | Yes | 单个像素灰度差超过该值才计入变化区域。现场反光/抖动多时可适当调高。 |
| `MinChangeRatio` | 最小变化比例 | `double` | 0.02 | [0, 1] | Yes | ROI 内变化像素占比达到该值才认为到料。误触发多时调高，漏检时调低。 |
| `MinChangePixels` | 最小变化像素数 | `int` | 500 | >= 0 | Yes | ROI 内变化像素数量下限，用于过滤小面积噪声。 |
| `CooldownMs` | 冷却时间(ms) | `int` | 1200 | [0, 60000] | Yes | 触发后在该时间内抑制重复触发，防止同一端子停留期间重复判定。 |
| `RoiX` | 检测区域X | `int` | 0 | >= 0 | Yes | 到料检测 ROI 左上角 X。 |
| `RoiY` | 检测区域Y | `int` | 0 | >= 0 | Yes | 到料检测 ROI 左上角 Y。 |
| `RoiW` | 检测区域宽度 | `int` | 0 | >= 0 | Yes | 到料检测 ROI 宽度；0 表示从 X 到图像右边界。 |
| `RoiH` | 检测区域高度 | `int` | 0 | >= 0 | Yes | 到料检测 ROI 高度；0 表示从 Y 到图像下边界。 |
| `BlurSize` | 降噪模糊核 | `int` | 0 | 0 或 3-15 奇数 | Yes | 可选高斯模糊，用于抑制点噪声。 |
| `MorphOpenSize` | 开运算核 | `int` | 0 | 0 或 3-15 奇数 | Yes | 可选形态学开运算，用于去除孤立噪声。 |
| `NormalizeMode` | 亮度归一化 | `enum` | None | None / MeanShift / PercentileClip | Yes | 抑制整体亮度漂移的可选预处理。 |
| `ReferenceUpdateMode` | 参考帧更新 | `enum` | PreviousFrame | PreviousFrame / StableBackground / ExponentialMovingAverage | Yes | 控制参考帧如何随时间更新。 |
| `ReferenceUpdateAlpha` | 参考更新系数 | `double` | 0.05 | [0, 1] | Yes | EMA 参考更新的权重。 |
| `AdaptivePixelThreshold` | 自适应像素阈值 | `bool` | false | - | Yes | 低对比 evidence profile 可显式启用，默认关闭。 |
| `MinConsecutiveChangedFrames` | 连续变化帧数 | `int` | 1 | >= 1 | Yes | 需要连续多少帧达到变化阈值才触发。 |
| `ResetAfterNoChangeFrames` | 无变化复位帧数 | `int` | 1 | >= 0 | Yes | 连续无变化达到该帧数后复位边沿状态。 |
| `TriggerOnRisingEdgeOnly` | 仅上升沿触发 | `bool` | true | - | Yes | 持续变化只在进入变化状态时触发一次。 |

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
| `BaselineReady` | 基线已建立 | `Boolean` | 标识参考帧状态已经建立，可用于调试和面板展示。 |
| `TotalPixels` | 有效像素数 | `Integer` | 当前 ROI 中参与判定的像素数。 |
| `CooldownRemainingMs` | 剩余冷却时间(ms) | `Integer` | 当前帧若受冷却期抑制，输出剩余冷却时间。 |
| `EffectivePixelThreshold` | 有效像素差阈值 | `Integer` | 自适应阈值开启后记录实际使用阈值。 |
| `EffectiveMinChangeRatio` | 有效最小变化比例 | `Float` | 记录当前 profile 和参数解析后的最小变化比例。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `ConsecutiveChangedFrames` | `Integer` | 当前连续达到变化阈值的帧数。 |
| `NoChangeFrames` | `Integer` | 当前连续未达到变化阈值的帧数。 |
| `NoMaterialFrame` | `Any` | 源码通过输出字典索引赋值写入。 |
| `RoiX` | `Integer` | 裁剪后的有效 ROI X。 |
| `RoiY` | `Integer` | 裁剪后的有效 ROI Y。 |
| `RoiW` | `Integer` | 裁剪后的有效 ROI 宽度。 |
| `RoiH` | `Integer` | 裁剪后的有效 ROI 高度。 |
| `StateKey` | `Any` | 源码通过输出字典索引赋值写入。 |
| `StateScope` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 多数图像路径近似 `O(W*H)`；涉及轮廓、匹配或排序时会叠加候选数量相关开销。 |
| 典型耗时 (Typical Latency) | `FrameChangeTrigger_contract_baseline.md` 记录 31/31 passed；`FrameChangeTrigger_dataset_baseline.md` 记录 140/140 passed，并包含 256x256 ROI 的 P95 runtime gate。 |
| 内存特征 (Memory Profile) | 通常需要输入图像、临时 Mat、结果图和输出封装内存；峰值随图像尺寸和中间副本数量增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：`FrameChangeTriggerOperatorTests` 覆盖基础行为和新增参数边界。
- Quality contract evidence：`quality/evals/reports/FrameChangeTrigger_contract_baseline.md`，31/31 passed。
- Dataset evidence：`quality/evals/reports/FrameChangeTrigger_dataset_baseline.md`，140/140 passed，Trigger Precision/Recall 均为 1.0。
- Field-substitute replay：`quality/evals/reports/FrameChangeTrigger_field_substitute_baseline.md`，20/20 passed；声明为替代 replay，不声明真实产线签核。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：缺失图像、空图像和非法参数会返回稳定失败信息。
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
| 1.0.0 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
