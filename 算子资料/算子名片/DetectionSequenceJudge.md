# 检测序列判定 / DetectionSequenceJudge

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `DetectionSequenceJudgeOperator` |
| 枚举值 (Enum) | `OperatorType.DetectionSequenceJudge` |
| 分类 (Category) | AI Inspection |
| 显示名 (DisplayName) | Detection Sequence Judge |
| 图标 (Icon) | `rule` |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 实验性 Experimental |
| 标签 (Tags) | `experimental`, `industrial-remediation`, `sequence-judge` |
| 关键词 (Keywords) | `sequence`, `order`, `wire`, `wiring`, `terminal`, `connector`, `harness`, `线序`, `顺序`, `接线`, `端子`, `line-sequence`, `judge` |

## 算法原理 / Algorithm Principle

**中文：** 该算子把检测框转换成有顺序的业务序列，并与期望标签顺序比较。支持三种分组模式：

1. **SingleRow**：保持旧逻辑，按 `SortBy`（CenterX/CenterY/TopY/Confidence/Area）和 `Direction`（Ascending/Descending/LeftToRight/RightToLeft/TopToBottom/BottomToTop）排序后直接比对标签序列。
2. **RowCluster**：先按 Y 方向聚成多行（行聚类容差 `RowTolerance`），再按每行内部顺序展开，适合双排或多排端子。
3. **SlotAssignment**：根据期望槽位点（`SlotPoints` 输入或 `ExpectedSlots` 参数），使用匈牙利算法（Hungarian algorithm）把检测结果分配到最近槽位，再按槽位顺序判定，适合规则槽位布局。

若提供透视源点和目标点（`PerspectiveSrcPoints`/`PerspectiveDstPoints` 或 JSON 参数），会先用 `Cv2.GetPerspectiveTransform` 计算透视矩阵，再把检测中心和槽位点映射到校正平面，然后排序或分配槽位。

`Auto` 模式下自动选择：有槽位点时用 `SlotAssignment`，有多条检测且 `RowTolerance > 0` 时用 `RowCluster`，否则用 `SingleRow`。

**English:** This operator converts detection bounding boxes into an ordered business sequence and compares it against an expected label order. Three grouping modes are supported:

1. **SingleRow**: Legacy logic, sorts by `SortBy` (CenterX/CenterY/TopY/Confidence/Area) and `Direction` (Ascending/Descending/LeftToRight/RightToLeft/TopToBottom/BottomToTop), then directly compares label sequences.
2. **RowCluster**: Clusters detections into rows by Y direction (row tolerance `RowTolerance`), then flattens by intra-row order, suitable for dual-row or multi-row terminals.
3. **SlotAssignment**: Based on expected slot points (`SlotPoints` input or `ExpectedSlots` parameter), uses the Hungarian algorithm to assign detections to nearest slots, then judges by slot order, suitable for regular slot layouts.

When perspective source and destination points are provided, the operator computes a perspective transform matrix via `Cv2.GetPerspectiveTransform`, maps detection centers and slot points to the corrected plane, then sorts or assigns slots.

`Auto` mode selects automatically: `SlotAssignment` when slots are provided, `RowCluster` when multiple detections and `RowTolerance > 0`, otherwise `SingleRow`.

## 实现策略 / Implementation Strategy

**中文：** 源码中的关键实现策略：

1. **匈牙利算法槽位分配**：`AssignToSlots` 构建代价矩阵（距离 + 置信度 tie-breaker），使用 `SolveHungarian`（O(N^3) 实现）求解最优一对一匹配。超过 `SlotTolerance` 的分配被拒绝（代价设为无穷大），未分配检测输出到 `UnassignedDetections`。
2. **透视校正**：`ResolvePerspectiveContext` 支持端口输入（`PerspectiveSrcPoints`/`PerspectiveDstPoints`）和 JSON 参数（`PerspectiveSrcPointsJson`/`PerspectiveDstPointsJson`）两种方式，至少需要 4 个点。变换应用到检测中心和槽位点的 `EvalX/EvalY`。
3. **行聚类**：`ClusterRows` 按 Y 坐标升序遍历，将检测分配到最近的已有行（均值距离 <= `RowTolerance`），否则创建新行。`FlattenRows` 按行均值 Y 排序后展开。
4. **自动容差推断**：`RowTolerance=0` 时自动推断为 `max(6, medianHeight * 0.75)`；`SlotTolerance=0` 时自动推断为 `max(12, medianSize * 1.5)`。
5. **输入解析**：`TryParsePointCollection` 支持 JSON 数组、`x:y;x:y` 速记格式、`Point2f`/`Point`/`Position` 对象和字典格式。`DetectionOutputInspector.ExtractDetections` 从各种检测结果格式中提取 `DetectionResult` 列表。
6. **判定逻辑**：多条件组合判定——缺失标签（`AllowMissing` 控制）、重复标签（`AllowDuplicate` 控制）、数量不匹配、顺序不匹配。任一条件不满足则 `IsMatch=false`，`Message` 包含所有失败原因。
7. **方向归一化**：`NormalizeSortBy` 在 TopToBottom/BottomToTop 方向时强制使用 CenterY 排序（替代 CenterX 等无意义排序键）。

**English:** Key implementation strategies:

1. **Hungarian algorithm slot assignment**: `AssignToSlots` builds a cost matrix (distance + confidence tie-breaker), uses `SolveHungarian` (O(N^3) implementation) for optimal one-to-one matching. Assignments exceeding `SlotTolerance` are rejected (cost set to infinity); unassigned detections go to `UnassignedDetections`.
2. **Perspective correction**: `ResolvePerspectiveContext` supports port inputs (`PerspectiveSrcPoints`/`PerspectiveDstPoints`) and JSON parameters (`PerspectiveSrcPointsJson`/`PerspectiveDstPointsJson`), requiring at least 4 points. Transform is applied to detection centers and slot points' `EvalX/EvalY`.
3. **Row clustering**: `ClusterRows` iterates detections in ascending Y order, assigning each to the nearest existing row (mean distance <= `RowTolerance`) or creating a new row. `FlattenRows` sorts rows by mean Y then expands.
4. **Automatic tolerance inference**: When `RowTolerance=0`, auto-infers as `max(6, medianHeight * 0.75)`; when `SlotTolerance=0`, auto-infers as `max(12, medianSize * 1.5)`.
5. **Input parsing**: `TryParsePointCollection` supports JSON arrays, `x:y;x:y` shorthand, `Point2f`/`Point`/`Position` objects, and dictionary formats. `DetectionOutputInspector.ExtractDetections` extracts `DetectionResult` lists from various detection result formats.
6. **Judgment logic**: Multi-condition combination -- missing labels (controlled by `AllowMissing`), duplicate labels (controlled by `AllowDuplicate`), count mismatch, order mismatch. Any failing condition sets `IsMatch=false`; `Message` contains all failure reasons.
7. **Direction normalization**: `NormalizeSortBy` forces CenterY sorting in TopToBottom/BottomToTop directions (replacing meaningless sort keys like CenterX).

## 核心 API 调用链 / Core API Call Chain
1. `DetectionOutputInspector.ExtractDetections(detectionValue)` -- 提取检测结果
2. `GetStringParam / GetDoubleParam / GetIntParam / GetBoolParam` -- 读取参数
3. `ParseLabels(expectedLabels)` -- 解析期望标签
4. `NormalizeGroupingMode(groupingMode)` -- 归一化分组模式
5. `TryResolveSlotPoints(inputs, @operator, ...)` -- 解析槽位点
6. `ResolvePerspectiveContext(inputs, @operator, ...)` -- 解析透视上下文
   - `TryResolvePointSet(...)` -- 解析源/目标点集
   - `Cv2.GetPerspectiveTransform(...)` -- 计算透视矩阵
7. `ApplyPerspective(filteredDetections, perspective)` -- 应用透视校正
8. `ResolveGroupingMode(...)` -- 解析实际分组模式（Auto -> SlotAssignment/RowCluster/SingleRow）
9. **SlotAssignment 路径**：
   - `BuildSlotOrder(slotPoints, direction, rowTolerance)` -- 构建槽位顺序
   - `AssignToSlots(detections, slotOrder, expectedLabels, ...)` -- 匈牙利匹配
10. **RowCluster 路径**：
    - `ClusterRows(filteredDetections, rowTolerance)` -- 行聚类
    - `FlattenRows(rowClusters, sortBy, direction)` -- 展平
11. **SingleRow 路径**：
    - `SortSequenceDetections(filteredDetections, sortBy, direction)` -- 排序
12. `ComputeMissingLabels(expectedLabels, actualOrder)` -- 计算缺失标签
13. 重复标签检测 + 数量不匹配检测 + 顺序不匹配检测 -- 综合判定
14. `SolveHungarian(costMatrix)` -- 匈牙利算法（仅 SlotAssignment 路径）

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ExpectedLabels` | `string` | `""` | 逗号分隔 | 期望标签顺序。为空时跳过顺序比对。 |
| `SortBy` | `enum` | `CenterX` | `CenterX` / `CenterY` / `TopY` / `Confidence` / `Area` | 排序依据字段。TopToBottom/BottomToTop 方向时自动归一化为 CenterY。 |
| `Direction` | `enum` | `Ascending` | `Ascending` / `Descending` / `LeftToRight` / `RightToLeft` / `TopToBottom` / `BottomToTop` | 排序方向。 |
| `ExpectedCount` | `int` | `0` | `[0, 256]` | 期望检测数量。0 表示由 ExpectedLabels 推导。 |
| `MinConfidence` | `double` | `0.0` | `[0.0, 1.0]` | 过滤低置信检测的阈值。低于此值的检测在序列判定前被移除。 |
| `AllowMissing` | `bool` | `false` | `true` / `false` | 是否允许缺失期望标签仍判为匹配。 |
| `AllowDuplicate` | `bool` | `false` | `true` / `false` | 是否允许重复标签仍判为匹配。 |
| `GroupingMode` | `enum` | `SingleRow` | `SingleRow` / `RowCluster` / `SlotAssignment` / `Auto` | 分组模式。`Auto` 根据是否有槽位点和 RowTolerance 自动选择。 |
| `ExpectedSlots` | `string` | `""` | JSON array 或 `x:y;x:y` | 期望槽位点列表。优先级低于 `SlotPoints` 端口输入。 |
| `RowTolerance` | `double` | `0.0` | `[0.0, 5000.0]` | 行聚类容差（像素）。0 表示自动推断。 |
| `SlotTolerance` | `double` | `0.0` | `[0.0, 5000.0]` | 槽位分配最大距离（像素）。0 表示自动推断。 |
| `PerspectiveSrcPointsJson` | `string` | `""` | JSON array | 透视源点 JSON（至少 4 个点）。须与目标点配对使用。 |
| `PerspectiveDstPointsJson` | `string` | `""` | JSON array | 透视目标点 JSON（至少 4 个点）。须与源点配对使用。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Detections` | Detections | `DetectionList` | Yes | 上游检测结果。 |
| `SlotPoints` | Slot Points | `PointList` | No | 槽位点输入，优先于 `ExpectedSlots` 参数。 |
| `PerspectiveSrcPoints` | Perspective Source Points | `PointList` | No | 透视源点（至少 4 个）。 |
| `PerspectiveDstPoints` | Perspective Destination Points | `PointList` | No | 透视目标点（至少 4 个）。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `IsMatch` | Is Match | `Boolean` | 是否判定匹配。 |
| `ActualOrder` | Actual Order | `Any` | 实际排序标签序列（`List<string>`）。 |
| `Count` | Count | `Integer` | 有效检测数量（排序后）。 |
| `MissingLabels` | Missing Labels | `Any` | 缺失标签列表（`List<string>`）。 |
| `DuplicateLabels` | Duplicate Labels | `Any` | 重复标签列表（`List<string>`）。 |
| `SortedDetections` | Sorted Detections | `DetectionList` | 排序后的检测结果。 |
| `Assignment` | Assignment | `Any` | 槽位分配详情（`List<Dictionary>`）。仅 SlotAssignment 模式有值。 |
| `UnassignedDetections` | Unassigned Detections | `DetectionList` | 未分配到槽位的检测结果。 |
| `SlotDistances` | Slot Distances | `Any` | 槽位距离数组（`double[]`）。 |
| `RowCount` | Row Count | `Integer` | 识别出的行数。 |
| `PerspectiveApplied` | Perspective Applied | `Boolean` | 是否应用了透视校正。 |
| `Diagnostics` | Diagnostics | `Any` | 分组模式、容差、槽位数、过滤数量等诊断信息。 |
| `Message` | Message | `String` | 最终判定说明（成功时显示序列，失败时显示所有失败原因）。 |

### Assignment 字段详情 / Assignment Record Fields
| 字段名 (Field) | 类型 (Type) | 说明 (Description) |
|------|------|------|
| `SlotIndex` | `Integer` | 槽位索引。 |
| `ExpectedLabel` | `String` | 该槽位的期望标签。 |
| `ActualLabel` | `String` | 实际分配的检测标签。空字符串表示未分配。 |
| `Assigned` | `Boolean` | 是否成功分配。 |
| `SlotX` / `SlotY` | `Double` | 槽位坐标。 |
| `Distance` | `Double` | 检测中心到槽位的距离。-1 表示未分配。 |
| `DetectionCenterX` / `DetectionCenterY` | `Double` | 分配的检测中心坐标。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | SingleRow 排序约为 `O(N log N)`；RowCluster 聚类约为 `O(N * R)`（R 为行数）；SlotAssignment 匈牙利算法约为 `O((N+S)^3)`（N 为检测数，S 为槽位数）；透视映射为 `O(N + S)`。 |
| 典型耗时 (Typical Latency) | `P2InspectionResidual_baseline.md` 记录 DetectionSequenceJudge 24/24 passed，平均约 32.1 ms。首个 expected-order oracle 场景受初始化影响较大。 |
| 内存特征 (Memory Profile) | 随检测数量、槽位数量、排序结果和诊断输出线性增长。匈牙利算法需要 `O((N+S)^2)` 的代价矩阵。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：线束、端子、连接器等需要按位置输出标签顺序的工装检测。
- **适合 (Suitable)**：单排、多排、规则槽位布局，并且上游检测框/标签质量相对稳定的流程。
- **适合 (Suitable)**：需要透视校正以补偿相机角度偏差的场景。
- **适合 (Suitable)**：`Auto` 模式自动选择最合适的分组策略。
- **不适合 (Not Suitable)**：严重遮挡、弯折、标签误识别或槽位定义不稳定的任务直接作为唯一判定依据。
- **不适合 (Not Suitable)**：槽位点数量极大（> 100）且对实时性要求极高的场景（匈牙利算法 O(N^3) 复杂度）。

## 已知限制 / Known Limitations
1. 透视校正只消费已提供点位，不会自动估计透视模型。源/目标点须配对提供且至少各 4 个。
2. `SlotAssignment` 依赖稳定槽位配置；槽位点错误会直接导致错序或未分配。
3. 该 baseline 锁定序列判定契约，不代表上游检测模型的识别准确率。
4. `Direction` 为 TopToBottom/BottomToTop 时，`SortBy` 会被强制归一化为 CenterY（TopY 保留），此时 CenterX/Confidence/Area 排序键不生效。
5. 匈牙利算法的 tie-breaker 使用 `confidence * 1e-6`，在极端情况下（大量等距离槽位）可能不完全按置信度排序。
6. `AllowMissing=true` 时，顺序比对采用松弛匹配（允许跳过期望标签），但不允许在期望序列中间插入未期望的标签。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写文档至金标准质量：补全所有 [OperatorParam]/[InputPort]/[OutputPort] 属性元数据；新增匈牙利算法细节、透视校正流程、Auto 模式解析逻辑、自动容差推断、Assignment 字段详情、输入格式解析；统一五列参数表；补全英文算法原理 |
| 1.0.1 | 2026-04-28 | 回写 P2InspectionResidual 24/24 baseline、序列判定失败契约和限制说明 |
| 1.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
