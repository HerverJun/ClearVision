# Detection Sequence Judge / DetectionSequenceJudge

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `DetectionSequenceJudgeOperator` |
| 枚举值 (Enum) | `OperatorType.DetectionSequenceJudge` |
| 分类 (Category) | AI Inspection |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `experimental`, `industrial-remediation`, `sequence-judge`, `功能域:检测`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Sorts detections and compares the resulting label order against an expected sequence。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
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
| `ExpectedLabels` | Expected Labels | `string` | "" | - | Yes | Comma-separated expected labels in order. |
| `SortBy` | Sort By | `enum` | CenterX | CenterX/Center X；CenterY/Center Y；TopY/Top Y；Confidence/Confidence；Area/Area | Yes | Field used to sort detections before judging the sequence. |
| `Direction` | Direction | `enum` | Ascending | Ascending/Ascending；Descending/Descending；LeftToRight/Left To Right；RightToLeft/Right To Left；TopToBottom/Top To Bottom；BottomToTop/Bottom To Top | Yes | Ordering direction after sorting. |
| `ExpectedCount` | Expected Count | `int` | 0 | [0, 256] | Yes | Expected detection count. Use 0 to derive from ExpectedLabels. |
| `MinConfidence` | Min Confidence | `double` | 0 | [0, 1] | Yes | Ignore detections below this confidence before sequence judgment. |
| `AllowMissing` | Allow Missing | `bool` | false | - | Yes | Whether missing expected labels should still be treated as a match. |
| `AllowDuplicate` | Allow Duplicate | `bool` | false | - | Yes | Whether duplicate labels should still be treated as a match. |
| `GroupingMode` | Grouping Mode | `enum` | SingleRow | SingleRow/Single Row；RowCluster/Row Cluster；SlotAssignment/Slot Assignment；Auto/Auto | Yes | SingleRow keeps legacy sorting, RowCluster groups detections into rows, SlotAssignment assigns detections to expected slot points, Auto prefers slots when provided. |
| `ExpectedSlots` | Expected Slots | `string` | "" | - | Yes | JSON array or shorthand x:y;x:y list of expected slot centers. |
| `RowTolerance` | Row Tolerance | `double` | 0 | [0, 5000] | Yes | Maximum Y delta for row clustering. Use 0 for auto. |
| `SlotTolerance` | Slot Tolerance | `double` | 0 | [0, 5000] | Yes | Maximum assignment distance to an expected slot. Use 0 for auto. |
| `PerspectiveSrcPointsJson` | Perspective Source Points JSON | `string` | "" | - | Yes | Optional 4-point JSON array for perspective source points. |
| `PerspectiveDstPointsJson` | Perspective Destination Points JSON | `string` | "" | - | Yes | Optional 4-point JSON array for perspective destination points. |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Detections` | Detections | `DetectionList` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `SlotPoints` | Slot Points | `PointList` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `PerspectiveSrcPoints` | Perspective Source Points | `PointList` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `PerspectiveDstPoints` | Perspective Destination Points | `PointList` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `IsMatch` | Is Match | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `ActualOrder` | Actual Order | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Count` | Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MissingLabels` | Missing Labels | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `DuplicateLabels` | Duplicate Labels | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `SortedDetections` | Sorted Detections | `DetectionList` | 检测列表结果，可连接筛选、NMS、顺序判定或结果输出节点。 |
| `Assignment` | Assignment | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `UnassignedDetections` | Unassigned Detections | `DetectionList` | 检测列表结果，可连接筛选、NMS、顺序判定或结果输出节点。 |
| `SlotDistances` | Slot Distances | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `RowCount` | Row Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `PerspectiveApplied` | Perspective Applied | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `Diagnostics` | Diagnostics | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Message` | Message | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

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
- 单元/契约测试：已在 `Acme.Product/tests/Acme.Product.Tests/Operators` 中发现对应测试入口。
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
| 1.0.0 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
