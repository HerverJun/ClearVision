# 候选框过滤 / BoundingBoxFilter

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `BoundingBoxFilterOperator` |
| 枚举值 (Enum) | `OperatorType.BoxFilter` |
| 分类 (Category) | 数据处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | `filter` |
| 关键词 (Keywords) | bounding box filter, detection filter, class filter, area filter, score |

## 算法原理 / Algorithm Principle
> **中文：** 对检测框列表（DetectionList）按 4 种模式进行过滤：
> - **Area**：按面积范围 `[MinArea, MaxArea]` 筛选
> - **Class**：按类别标签（逗号分隔，大小写不敏感）筛选
> - **Region**：按矩形区域内中心点命中筛选（检测框中心必须在 Region 内）
> - **Score**：按最低置信度筛选
>
> 所有非 Score 模式在主过滤后还会叠加 `MinScore` 作为通用后置阈值。
> 可选输入 Image 时，在结果图上绘制输入框（橙色）和保留框（绿色）的可视化叠加。
>
> **English:** Filters detection boxes (DetectionList) using 4 modes:
> - **Area**: by area range `[MinArea, MaxArea]`
> - **Class**: by label (comma-separated, case-insensitive)
> - **Region**: by center-point containment within a rectangle
> - **Score**: by minimum confidence threshold
>
> All non-Score modes additionally apply `MinScore` as a common post-filter.
> Optional Image input draws visualization overlays: input boxes (orange) and kept boxes (green).

## 实现策略 / Implementation Strategy
- 输入通过 `TryParseDetectionList` 统一解析，支持 `DetectionList`、`IEnumerable<DetectionResult>` 和 `IEnumerable`（字典回退）。
- `TargetClasses` 按逗号分割后构建 `HashSet<string>`（OrdinalIgnoreCase），实现 O(1) 类别查找。
- Region 模式使用 `IsCenterInsideRegion` 判断检测框中心是否在矩形区域内，而非框体相交。
- 可视化时内置轻量 NMS（IoU > 0.45 去重，置信度下限 0.25）避免重叠标签文字遮挡。
- 图像输出通过 `CreateImageOutput` 封装，自动附加 Width/Height 等字段。

## 核心 API 调用链 / Core API Call Chain
1. `TryParseDetectionList(detObj, out detections)` -> 统一解析输入
2. `GetStringParam(@operator, "FilterMode", "Area")` -> `GetDoubleParam/GetIntParam` 读取参数
3. `targetClassesRaw.Split(',').ToHashSet(OrdinalIgnoreCase)` -> 构建类别集合
4. 模式 switch：`d.Where(d => d.Area >= minArea && ...)` / `targetClasses.Contains(d.Label)` / `IsCenterInsideRegion` / `d.Confidence >= minScore`
5. 可选后置 `minScore` 过滤
6. `TryGetInputImage` -> `DrawDetections` (橙色 IN + 绿色 KEEP + 可选 Region 矩形)
7. `CreateImageOutput` 或纯数据 `OperatorExecutionOutput.Success`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `FilterMode` | `enum` | `"Area"` | `Area` / `Class` / `Region` / `Score` | 过滤模式。 |
| `MinArea` | `int` | `0` | [0, +inf) | Area 模式最小面积阈值。 |
| `MaxArea` | `int` | `9999999` | [0, +inf) | Area 模式最大面积阈值。 |
| `TargetClasses` | `string` | `""` | - | Class 模式目标类别，逗号分隔（如 `"cat,dog"`），大小写不敏感。 |
| `MinScore` | `double` | `0.0` | [0.0, 1.0] | 最低置信度阈值；Score 模式下为主过滤器，其他模式下为后置过滤器。 |
| `RegionX` | `int` | `0` | - | Region 模式矩形区域左上角 X 坐标。 |
| `RegionY` | `int` | `0` | - | Region 模式矩形区域左上角 Y 坐标。 |
| `RegionW` | `int` | `0` | - | Region 模式矩形区域宽度（<=0 时不筛选）。 |
| `RegionH` | `int` | `0` | - | Region 模式矩形区域高度（<=0 时不筛选）。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Detections` | Detections | `DetectionList` | Yes | 待过滤的检测框列表。 |
| `Image` | Image | `Image` | No | 输入图像，用于可视化叠加。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Detections` | Detections | `DetectionList` | 过滤后的检测框列表。 |
| `Image` | Image | `Image` | 带可视化叠加的结果图像（仅在 Image 输入时输出）。 |
| `Count` | Count | `Integer` | 过滤后保留的检测框数量。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `ReceivedCount` | `Integer` | 过滤前的输入检测框总数。 |
| `ReceivedVisualizationCount` | `Integer` | 可视化时绘制的输入框数量（经 NMS 去重后）。 |
| `VisualizationCount` | `Integer` | 可视化时绘制的保留框数量（经 NMS 去重后）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(n) 主过滤 + O(k^2) 可视化 NMS（k 为可视化候选数） |
| 典型耗时 (Typical Latency) | < 5ms（百级检测框，不含图像 I/O） |
| 内存特征 (Memory Profile) | O(n) 存储过滤结果；图像模式下额外 O(W*H) 克隆 |

## 适用场景 / Use Cases
- 适合 (Suitable)：在检测管线中按面积/类别/区域/置信度过滤候选框
- 适合 (Suitable)：调试时通过可视化叠加查看过滤前后的检测结果
- 适合 (Suitable)：Region 模式下限定感兴趣区域（ROI）内的检测
- 不适合 (Not Suitable)：需要基于 IoU 去重的场景（请使用 BoxNmsOperator）
- 不适合 (Not Suitable)：需要对非 DetectionResult 类型列表进行通用过滤
- 不适合 (Not Suitable)：Region 模式下需要框体相交判断（当前仅判断中心点）

## 已知限制 / Known Limitations
1. Region 模式仅判断检测框**中心点**是否在区域内，不判断框体是否与区域相交。
2. 可视化叠加内置固定的轻量 NMS（IoU=0.45, ScoreFloor=0.25），不可配置。
3. `TargetClasses` 为空时 Class 模式不过滤（返回全部），这可能是意外行为。
4. Region 模式下若 `RegionW` 或 `RegionH` <= 0，返回空结果而非失败。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面升级至 gold standard 文档；补充 4 种过滤模式详细行为、可视化 NMS 说明、运行时附加输出 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 |
