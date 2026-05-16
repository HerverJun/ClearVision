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
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 10 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含短路输出路径，可在条件不满足时阻止后续节点继续执行。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.Absdiff`
- `Cv2.Threshold`
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
| `PixelThreshold` | 像素差阈值 | `int` | 30 | [1, 255] | Yes | 单个像素灰度差超过该值才计入变化区域。现场反光/抖动多时可适当调高。 |
| `MinChangeRatio` | 最小变化比例 | `double` | 0.02 | [0, 1] | Yes | ROI 内变化像素占比达到该值才认为到料。误触发多时调高，漏检时调低。 |
| `MinChangePixels` | 最小变化像素数 | `int` | 500 | >= 0 | Yes | ROI 内变化像素数量下限，用于过滤小面积噪声。 |
| `CooldownMs` | 冷却时间(ms) | `int` | 1200 | [0, 60000] | Yes | 触发后在该时间内抑制重复触发，防止同一端子停留期间重复判定。 |
| `RoiX` | 检测区域X | `int` | 0 | >= 0 | Yes | 到料检测 ROI 左上角 X。 |
| `RoiY` | 检测区域Y | `int` | 0 | >= 0 | Yes | 到料检测 ROI 左上角 Y。 |
| `RoiW` | 检测区域宽度 | `int` | 0 | >= 0 | Yes | 到料检测 ROI 宽度；0 表示从 X 到图像右边界。 |
| `RoiH` | 检测区域高度 | `int` | 0 | >= 0 | Yes | 到料检测 ROI 高度；0 表示从 Y 到图像下边界。 |

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

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
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
- 单元/契约测试：已在 `Acme.Product/tests/Acme.Product.Tests/Operators` 中发现对应测试入口。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 2 条 `OperatorExecutionOutput.Failure(...)` 路径。
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
