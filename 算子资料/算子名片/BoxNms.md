# 候选框抑制 / BoxNms

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `BoxNmsOperator` |
| 枚举值 (Enum) | `OperatorType.BoxNms` |
| 分类 (Category) | 数据处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | `nms` |
| 关键词 (Keywords) | nms, box, iou, suppression |

## 算法原理 / Algorithm Principle
> **中文：** 对检测框执行经典非极大值抑制（NMS）去重。
> 算法按类别分组，每组内按置信度降序排列后贪心选取：保留当前最高置信度框，
> 抑制所有与之 IoU 超过阈值的后续框。最终按 `MaxDetections` 上限截断。
>
> 排序优先级：Confidence DESC -> Area DESC -> X ASC -> Y ASC -> Label ASC。
>
> **English:** Applies classic Non-Maximum Suppression (NMS) to detection boxes.
> Groups detections by label, sorts each group by confidence descending, then greedily
> selects: keeps the highest-confidence box and suppresses all subsequent boxes whose
> IoU exceeds the threshold. Finally truncates to `MaxDetections` limit.
>
> Sort priority: Confidence DESC -> Area DESC -> X ASC -> Y ASC -> Label ASC.

## 实现策略 / Implementation Strategy
- 先按 `ScoreThreshold` 过滤低置信度候选，再按 `Label` 分组执行 NMS。
- 每组内使用 `OrderDetections` 统一排序（Confidence DESC, Area DESC, X ASC, Y ASC, Label ASC）。
- NMS 使用 O(n^2) 双层循环，内层维护 `removed[]` 位数组标记被抑制的框。
- 超过 `MaxDetections` 的保留框按排序结果截断，溢出部分移入 suppressed 列表。
- 可选输入 `SourceImage`（干净图）优先于 `Image`（可能已有绘图），用于可视化叠加。
- 可视化：保留框绿色（thickness=2, tag="K"），被抑制框红色（thickness=1, tag="S"）。

## 核心 API 调用链 / Core API Call Chain
1. `TryParseDetectionList(detObj, out detections)` -> 统一解析输入
2. `GetDoubleParam(@operator, "IouThreshold", 0.45, 0.1, 1.0)` + `ScoreThreshold` + `MaxDetections` + `ShowSuppressed`
3. `detections.Where(d => d.Confidence >= scoreThreshold)` -> `OrderDetections` -> 预排序
4. `candidates.GroupBy(d => d.Label)` -> 按类别分组
5. 组内双层循环 NMS：`IoU(current, groupCandidates[j]) > iouThreshold` -> `removed[j] = true`
6. `kept.Count > maxDetections` -> 截断
7. `CreateDiagnostics(...)` -> 构建诊断字典
8. `TryGetInputImage(inputs, "SourceImage", ...)` -> fallback `TryGetInputImage(inputs, out imageWrapper)`
9. `DrawDetections` (绿色 K + 红色 S) -> `CreateImageOutput` 或纯数据输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `IouThreshold` | `double` | `0.45` | [0.1, 1.0] | IoU 重叠阈值；超过此值的框被抑制。 |
| `ScoreThreshold` | `double` | `0.25` | [0.0, 1.0] | 最低置信度阈值；低于此值的候选在 NMS 前被过滤。 |
| `MaxDetections` | `int` | `100` | [1, 1000] | 最终保留的最大检测框数量。 |
| `ShowSuppressed` | `bool` | `true` | - | 是否在可视化图像上绘制被抑制的框（红色）。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Detections` | Detections | `DetectionList` | Yes | 待去重的检测框列表。 |
| `Image` | Image | `Image` | No | 输入图像（可能已有绘图），用于可视化叠加。 |
| `SourceImage` | Source Image | `Image` | No | 干净源图像，优先于 Image 用于可视化叠加。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Detections` | Detections | `DetectionList` | NMS 后保留的检测框列表。 |
| `Image` | Image | `Image` | 带可视化叠加的结果图像（仅在图像输入时输出）。 |
| `Count` | Count | `Integer` | NMS 后保留的检测框数量。 |
| `InputCount` | Input Count | `Integer` | NMS 前的输入检测框总数。 |
| `SuppressedCount` | Suppressed Count | `Integer` | 被抑制的检测框数量。 |
| `SuppressedDetections` | Suppressed Detections | `DetectionList` | 被抑制的检测框列表（按置信度降序）。 |
| `Diagnostics` | Diagnostics | `Any` | NMS 诊断信息字典（含阈值、计数等）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(n log n) 排序 + O(n^2) NMS（n 为同类候选框数量） |
| 典型耗时 (Typical Latency) | < 10ms（千级检测框） |
| 内存特征 (Memory Profile) | O(n) 存储 kept/suppressed 列表 + removed 位数组；图像模式下额外 O(W*H) |

## 适用场景 / Use Cases
- 适合 (Suitable)：目标检测模型输出后去除重叠冗余框
- 适合 (Suitable)：多类别场景下按类别独立执行 NMS
- 适合 (Suitable)：调试时查看被抑制的框（红色标注）
- 不适合 (Not Suitable)：需要 Soft-NMS 或加权 NMS 等高级去重策略
- 不适合 (Not Suitable)：需要旋转框（Rotated Box）IoU 计算的场景
- 不适合 (Not Suitable)：实时性要求极高的场景（O(n^2) 可能成为瓶颈）

## 已知限制 / Known Limitations
1. NMS 使用经典 O(n^2) 双层循环，大量同类检测框时性能下降。
2. `SourceImage` 和 `Image` 同时提供时优先使用 `SourceImage`；`Image` 被忽略。
3. `ShowSuppressed=false` 时仅跳过被抑制框的绘制，不影响其他输出。
4. IoU 计算基于轴对齐矩形（AABB），不支持旋转框。
5. 空标签（`Label` 为 null 或空字符串）的检测框被归入同一组处理。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面升级至 gold standard 文档；补充 NMS 算法细节、SourceImage 优先级、诊断输出说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 |
