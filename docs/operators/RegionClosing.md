# 区域闭运算 / RegionClosing

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionClosingOperator` |
| 枚举值 (Enum) | `OperatorType.RegionClosing` |
| 分类 ID (CategoryId) | `SegmentationAndRegion` |
| 分类 (Category) | 分割与区域 |
| 分类顺序 (CategoryOrder) | 3 |
| 版本 (Version) | `1.0.2` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:SegmentationAndRegion`, `分类显示:分割与区域`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于执行闭运算（先膨胀后腐蚀），用于填补小孔洞并连接相近区域。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Region morphology closing` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Region`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Image`。
- 参数解析覆盖 3 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `MorphologyKernel.GetOffsets -> Dilate -> Erode -> Region`
- `OperatorBase.Get*Param(...)`
- `Cv2.BitwiseAnd`
- `Cv2.AddWeighted`
- `Cv2.PutText`
- `Math.Max`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `KernelShape` | 结构元素形状 | `enum` | Rectangle | Rectangle/矩形；Ellipse/椭圆；Cross/十字 | Yes | - |
| `KernelWidth` | 核宽度 | `int` | 3 | [1, 99] | Yes | - |
| `KernelHeight` | 核高度 | `int` | 3 | [1, 99] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region` | 输入区域 | `Region` | Yes | 区域闭运算的主输入，必须是 Region/像素区域；Image 或 Contour 不能直接替代。 |
| `Image` | 参考图像（可选） | `Image` | No | 仅用于参考图和结果可视化，不参与区域闭运算计算，也不是主输入。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | 闭运算后区域 | `Region` | 闭运算得到的 Region/像素区域。 |
| `Image` | 可视化图像 | `Image` | 在参考图或区域底图上绘制的预览结果。 |
| `Area` | Closed Area | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |

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
- 组合指纹 (Generation Fingerprint)：`7588707944E622D331D051FC733B22A0B13AFB33018D8EC82A0CCAACA888B549`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `AreaChange` | `Any` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `Kernel` | `Any` | 源码输出字典初始化中可见字段。 |
| `Message` | `String` | 源码输出字典初始化中可见字段。 |
| `OriginalArea` | `Any` | 源码输出字典初始化中可见字段。 |
| `ProcessingTimeMs` | `Any` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(P*K + P' * K * log Rrow) |
| 典型耗时 (Typical Latency) | Avg 0.963 ms, max 32.974 ms over 100 synthetic golden cases |
| 内存特征 (Memory Profile) | O(P+P'+K) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 1 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Filling small holes, connecting nearby components, and stabilizing fragmented foreground before measurement.
- 不适合 (Not Suitable)：Maintaining strict separation between adjacent components closer than the selected kernel.

## 已知限制 / Known Limitations
1. Closing can bridge nearby components when the gap is within kernel reach.
2. The operation uses a single dilation+erosion pair; repeated closing requires explicit workflow repetition.
3. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
4. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
5. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.2 | 2026-07-14 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
