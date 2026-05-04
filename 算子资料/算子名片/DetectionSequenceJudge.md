# 线序判定 / DetectionSequenceJudge

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `DetectionSequenceJudgeOperator` |
| 枚举值 (Enum) | `OperatorType.DetectionSequenceJudge` |
| 分类 (Category) | AI Inspection |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | Experimental |
| 标签 (Tags) | `experimental`, `industrial-remediation`, `sequence-judge` |

## 算法说明 / Algorithm
该算子把检测框转换成有顺序的业务序列，并与期望标签顺序比较。当前实现支持三条路径：

1. `SingleRow`：保持旧逻辑，按坐标或置信度排序后直接比对标签序列。
2. `RowCluster`：先按 Y 方向聚成多行，再按每行内部顺序展开，适合双排或多排端子。
3. `SlotAssignment`：根据期望槽位点，把检测结果分配到最近槽位，再按槽位顺序判定，适合规则槽位布局。

若提供透视源点和目标点，会先把检测中心映射到校正平面，再排序或分配槽位。

## 参数 / Parameters
| 名称 (Name) | 类型 (Type) | 默认值 (Default) | 说明 (Description) |
|------|------|------|------|
| `ExpectedLabels` | `string` | `""` | 逗号分隔的期望标签顺序。 |
| `SortBy` | `enum` | `CenterX` | 排序依据：中心点、顶部、置信度或面积。 |
| `Direction` | `enum` | `Ascending` | 排序方向。 |
| `ExpectedCount` | `int` | `0` | 期望检测数量；0 表示由标签推导。 |
| `MinConfidence` | `double` | `0.0` | 过滤低置信检测。 |
| `AllowMissing` | `bool` | `false` | 是否允许缺失标签。 |
| `AllowDuplicate` | `bool` | `false` | 是否允许重复标签。 |
| `GroupingMode` | `enum` | `SingleRow` | `SingleRow` / `RowCluster` / `SlotAssignment` / `Auto`。 |
| `ExpectedSlots` | `string` | `""` | JSON 数组或 `x:y;x:y` 槽位列表。 |
| `RowTolerance` | `double` | `0.0` | 行聚类容差，0 表示自动。 |
| `SlotTolerance` | `double` | `0.0` | 槽位分配最大距离，0 表示自动。 |
| `PerspectiveSrcPointsJson` | `string` | `""` | 透视源点 JSON。 |
| `PerspectiveDstPointsJson` | `string` | `""` | 透视目标点 JSON。 |

## 输入/输出 / Inputs & Outputs
### 输入 / Inputs
| 名称 (Name) | 类型 (Type) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|
| `Detections` | `DetectionList` | 是 | 上游检测结果。 |
| `SlotPoints` | `PointList` | 否 | 槽位点输入，优先于 `ExpectedSlots`。 |
| `PerspectiveSrcPoints` | `PointList` | 否 | 透视源点。 |
| `PerspectiveDstPoints` | `PointList` | 否 | 透视目标点。 |

### 输出 / Outputs
| 名称 (Name) | 类型 (Type) | 说明 (Description) |
|------|------|------|
| `IsMatch` | `Boolean` | 是否判定匹配。 |
| `ActualOrder` | `Any` | 实际排序标签序列。 |
| `Count` | `Integer` | 有效检测数量。 |
| `MissingLabels` | `Any` | 缺失标签。 |
| `DuplicateLabels` | `Any` | 重复标签。 |
| `SortedDetections` | `DetectionList` | 排序后的检测结果。 |
| `Assignment` | `Any` | 槽位分配详情。 |
| `UnassignedDetections` | `DetectionList` | 未分配检测。 |
| `SlotDistances` | `Any` | 槽位距离数组。 |
| `RowCount` | `Integer` | 识别出的行数。 |
| `PerspectiveApplied` | `Boolean` | 是否应用透视校正。 |
| `Diagnostics` | `Any` | 分组模式、容差、槽位数、过滤数量等诊断信息。 |
| `Message` | `String` | 最终判定说明。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 排序约为 `O(N log N)`；槽位分配约为 `O(N * S)`；透视映射为 `O(N)`。 |
| 典型耗时 (Typical Latency) | `P2InspectionResidual_baseline.md` 记录 DetectionSequenceJudge 24/24 passed，平均约 32.1 ms；首个 expected-order oracle 场景受初始化影响较大。 |
| 内存特征 (Memory Profile) | 随检测数量、槽位数量、排序结果和诊断输出线性增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- Contract baseline：`quality/evals/reports/P2InspectionResidual_baseline.md`，DetectionSequenceJudge 24/24 passed。
- 覆盖范围：期望顺序、缺失标签、顺序 mismatch、RowCluster、SlotAssignment、透视点/槽位解析、缺失检测输入和非法槽位点。
- 失败契约包括缺失 `Detections`、非法槽位点、非法透视点、非法排序/方向/分组参数、负容差和无法满足期望顺序。

## 适用场景 / Use Cases
- 适合：线束、端子、连接器等需要按位置输出标签顺序的工装。
- 适合：单排、多排、规则槽位布局，并且上游检测框/标签质量相对稳定的流程。
- 不适合：严重遮挡、弯折、标签误识别或槽位定义不稳定的任务直接作为唯一判定依据。

## 已知限制 / Known Limitations
1. 透视校正只消费已提供点位，不会自动估计透视模型。
2. `SlotAssignment` 依赖稳定槽位配置；槽位点错误会直接导致错序或未分配。
3. 该 baseline 锁定序列判定契约，不代表上游检测模型的识别准确率。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-28 | 回写 P2InspectionResidual 24/24 baseline、序列判定失败契约和限制说明 |
| 1.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
