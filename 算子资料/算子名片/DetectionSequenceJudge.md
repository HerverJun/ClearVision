# 检测顺序判定 / DetectionSequenceJudge

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `DetectionSequenceJudgeOperator` |
| 枚举值 (Enum) | `OperatorType.DetectionSequenceJudge` |
| 分类 ID (CategoryId) | `DefectDetection` |
| 分类 (Category) | 缺陷检测 |
| 分类顺序 (CategoryOrder) | 6 |
| 版本 (Version) | `1.0.1` |
| 生命周期 (Lifecycle) | 实验 `Experimental` |
| 生命周期说明 (Lifecycle Note) | 顺序、行聚类和槽位分配策略需针对现场布局及遮挡情况验证。 |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | Yes |
| 标签 (Tags) | `experimental`, `industrial-remediation`, `sequence-judge`, `分类:DefectDetection`, `分类显示:缺陷检测`, `生命周期:Experimental`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于对检测结果排序，并与期望标签序列进行比对。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Detections`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`SlotPoints`、`PerspectiveSrcPoints`、`PerspectiveDstPoints`。
- 参数解析覆盖 13 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.GetPerspectiveTransform`
- `JsonDocument.Parse`
- `Math.Abs`
- `Math.Clamp`
- `Math.Max`
- `Math.Min`
- `Math.Sqrt`
- `Enumerable.Repeat`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `ExpectedLabels` | 期望标签序列 | `string` | "" | - | Yes | 按顺序填写的期望标签，使用逗号分隔。 |
| `SortBy` | 排序字段 | `enum` | CenterX | CenterX/中心 X；CenterY/中心 Y；TopY/顶部 Y；Confidence/置信度；Area/面积 | Yes | 判定前用于排序检测结果的字段。 |
| `Direction` | 排序方向 | `enum` | Ascending | Ascending/升序；Descending/降序；LeftToRight/从左到右；RightToLeft/从右到左；TopToBottom/从上到下；BottomToTop/从下到上 | Yes | 排序后的方向。 |
| `ExpectedCount` | 期望数量 | `int` | 0 | [0, 256] | Yes | 期望检测数量；为 0 时从期望标签序列推导。 |
| `MinConfidence` | 最低置信度 | `double` | 0 | [0, 1] | Yes | 顺序判定前忽略低于该置信度的检测结果。 |
| `AllowMissing` | 允许缺失 | `bool` | false | - | Yes | 期望标签缺失时是否仍判为匹配。 |
| `AllowDuplicate` | 允许重复 | `bool` | false | - | Yes | 标签重复时是否仍判为匹配。 |
| `GroupingMode` | 分组模式 | `enum` | SingleRow | SingleRow/单行；RowCluster/行聚类；SlotAssignment/槽位分配；Auto/自动 | Yes | SingleRow 使用单行排序，RowCluster 按行分组，SlotAssignment 按槽位分配，Auto 优先使用槽位。 |
| `ExpectedSlots` | 期望槽位 | `string` | "" | - | Yes | 期望槽位中心点，支持 JSON 数组或 x:y;x:y 简写。 |
| `RowTolerance` | 行容差 | `double` | 0 | [0, 5000] | Yes | 行聚类允许的最大 Y 偏差；0 表示自动。 |
| `SlotTolerance` | 槽位容差 | `double` | 0 | [0, 5000] | Yes | 分配到期望槽位的最大距离；0 表示自动。 |
| `PerspectiveSrcPointsJson` | 透视源点 JSON | `string` | "" | - | Yes | 可选的 4 点透视源点 JSON 数组。 |
| `PerspectiveDstPointsJson` | 透视目标点 JSON | `string` | "" | - | Yes | 可选的 4 点透视目标点 JSON 数组。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Detections` | 检测结果 | `DetectionList` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `SlotPoints` | 槽位点 | `PointList` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `PerspectiveSrcPoints` | 透视源点 | `PointList` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `PerspectiveDstPoints` | 透视目标点 | `PointList` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `IsMatch` | 是否匹配 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `ActualOrder` | 实际顺序 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Count` | 数量 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MissingLabels` | 缺失标签 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `DuplicateLabels` | 重复标签 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `SortedDetections` | 排序后检测 | `DetectionList` | 检测列表结果，可连接筛选、NMS、顺序判定或结果输出节点。 |
| `Assignment` | 分配结果 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `UnassignedDetections` | 未分配检测 | `DetectionList` | 检测列表结果，可连接筛选、NMS、顺序判定或结果输出节点。 |
| `SlotDistances` | 槽位距离 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `RowCount` | 行数 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `PerspectiveApplied` | 已应用透视 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `Diagnostics` | 诊断信息 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Message` | 消息 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

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
- 组合指纹 (Generation Fingerprint)：`93D6CC25A3CD00BC6C56B7FBAB982BCBD08416750DD8B4AEFF91B8D15E1791B1`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `ActualLabel` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Assigned` | `Any` | 源码通过输出字典索引赋值写入。 |
| `DetectionCenterX` | `Any` | 源码通过输出字典索引赋值写入。 |
| `DetectionCenterY` | `Any` | 源码通过输出字典索引赋值写入。 |
| `DetectionCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `Distance` | `Float` | 源码通过输出字典索引赋值写入。 |
| `ExpectedLabel` | `Any` | 源码通过输出字典索引赋值写入。 |
| `FilteredCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `GroupingModeRequested` | `String` | 源码通过输出字典索引赋值写入。 |
| `GroupingModeResolved` | `String` | 源码通过输出字典索引赋值写入。 |
| `PerspectiveSource` | `String` | 源码通过输出字典索引赋值写入。 |
| `ReceivedCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `RequiredMinConfidence` | `Float` | 源码通过输出字典索引赋值写入。 |
| `SlotCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `SlotIndex` | `Any` | 源码通过输出字典索引赋值写入。 |
| `SlotX` | `Any` | 源码通过输出字典索引赋值写入。 |
| `SlotY` | `Any` | 源码通过输出字典索引赋值写入。 |
| `SortedCount` | `Integer` | 源码通过输出字典索引赋值写入。 |

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
| 1.0.1 | 2026-07-14 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
