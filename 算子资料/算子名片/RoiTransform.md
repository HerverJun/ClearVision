# ROI跟踪 / RoiTransform

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RoiTransformOperator` |
| 枚举值 (Enum) | `OperatorType.RoiTransform` |
| 分类 (Category) | 辅助 |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
中文：该算子将上游形状匹配或平面匹配输出的**位姿（Pose）**转换为一个跟踪后的搜索区域（SearchRegion），用于桥接匹配算子与下游测量算子（如 CaliperTool）。

核心算法流程：
1. 从 `BaseRoi` 输入解析基准矩形（支持 `Rect` 类型、`IDictionary<string,object>`、`IDictionary`）。
2. 从 `Matches` 输入中按 `MatchIndex` 选取匹配结果（支持字典、索引序列、嵌套 `MatchResult`）。
3. 从匹配结果中解析位姿：中心点（`CenterX/CenterY` 或 `Center/Position/ReferenceX/ReferenceY/BoundingBox/Corners/X+Y`）、角度（`Angle/AngleDeg/Rotation/RotationDeg`）、缩放（`Scale/ScaleFactor`）。
4. 调用 `RoiTracker.TransformRoi(baseRoi, center, angle, scale)` 将基准 ROI 围绕其中心进行平移、旋转和缩放变换，输出变换后的矩形。

> English: This operator converts a match pose (center, angle, scale) from upstream shape/planar matching into a tracked search ROI rectangle for downstream measurement operators. It normalizes match dictionaries, extracts pose fields from multiple supported formats, and transforms the base ROI via `RoiTracker.TransformRoi`.

## 实现策略 / Implementation Strategy
- **宽泛的输入兼容**：`TryParseRect` 支持 `Rect` 类型、泛型字典和非泛型字典；`TryGetMatchDictionary` 支持单字典、索引序列（`IEnumerable`）和嵌套 `MatchResult`。
- **多格式位姿解析**：`TryReadPose` 按优先级尝试多种字段名组合（`CenterX/CenterY` > `Center` > `Position` > `ReferenceX/ReferenceY` > `BoundingBox` 中心 > `Corners` 中心 > `X/Y` + 可选 `Width/Height`），确保与不同匹配算子的输出格式兼容。
- **嵌套 MatchResult 支持**：如果字典中存在 `MatchResult` 键，会递归进入该嵌套字典解析位姿。
- **Position 对象支持**：`TryGetPoint` 同时支持 `Position` 值对象和字典格式的 `{X, Y}`。
- **Corners 中心计算**：当匹配结果包含 `Corners` 数组时，取所有角点的算术平均值作为中心。
- **Scale 安全钳位**：非正 Scale 值被钳位到 1.0，避免反向或零缩放。
- **整数输出**：最终 `SearchRegion` 为整数矩形（`X/Y/Width/Height`），变换后的浮点坐标会被四舍五入。

> English: The implementation accepts broad input formats (Rect, generic/non-generic dictionaries, indexed sequences), resolves pose from multiple field name combinations with recursive MatchResult support, computes center from BoundingBox/Corners when needed, clamps non-positive scale to 1.0, and outputs integer bounding rectangles.

## 核心 API 调用链 / Core API Call Chain
1. `TryParseRect(baseObj, out baseRoi)` -- 解析基准 ROI（支持 Rect/IDictionary/非泛型字典）
2. `TryGetMatchDictionary(matchesObj, matchIndex, out match)` -- 从 Matches 中按索引选取匹配结果
   - 单字典直接返回
   - `IEnumerable` 按 `MatchIndex` 索引
   - 支持 `IDictionary<string,object>` 和 `IDictionary`
3. `TryReadPose(match, out centerX, out centerY, out angleDeg, out scale)` -- 解析位姿
   - `TryGetMatchResult(match, out nestedMatch)` -- 尝试递归进入 `MatchResult`
   - `TryGetDouble(match, "CenterX"/"CenterY", ...)` -- 直接读取中心坐标
   - `TryGetPoint(match, "Center"/"Position", ...)` -- 从 Position 对象或字典读取
   - `TryGetBoundingBoxCenter(match, ...)` -- 从 BoundingBox 计算中心
   - `TryGetCornersCenter(match, ...)` -- 从 Corners 数组计算平均中心
   - `TryGetDouble(match, "Angle"/"Scale", ...)` -- 读取角度和缩放
4. `RoiTracker.TransformRoi(baseRoi, center, angle, scale)` -- 核心变换
5. 输出 `SearchRegion` 字典：`{X, Y, Width, Height}`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `MatchIndex` | `int` | `0` | [0, 100] | 当 Matches 输入为序列时，选取第 N 个匹配结果（0-based）。当 Matches 为单字典时忽略。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `BaseRoi` | Base ROI | `Rectangle` | Yes | 基准搜索区域矩形。支持 `Rect` 类型或字典 `{X, Y, Width, Height}`。 |
| `Matches` | Matches | `Any` | Yes | 上游匹配结果。支持单字典、匹配列表（配合 `MatchIndex`）或嵌套 `MatchResult`。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `SearchRegion` | Search Region | `Rectangle` | 变换后的搜索区域，为字典 `{X, Y, Width, Height}`。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 | `O(I + C)`，其中 `I` 为输入解析开销，`C` 为 Corners 数组长度（仅 Corners 模式）。核心变换 `RoiTracker.TransformRoi` 为 `O(1)`。 |
| 典型耗时 (Typical Latency) | 无独立基准测试；通常 < 0.1ms，开销主要在字典遍历和类型转换。 |
| 内存特征 (Memory Profile) | `O(C)`，其中 `C` 为 Corners 数组长度。正常场景下内存开销极小。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：将形状匹配或平面匹配的位姿传递给下游测量算子（如 CaliperTool）作为跟踪搜索区域。
- **适合 (Suitable)**：帧间参考 ROI 的平移、旋转和缩放调整，实现简单的目标跟踪。
- **适合 (Suitable)**：桥接不同格式的匹配结果与 ROI 消费者，自动适配多种位姿字段命名。
- **不适合 (Not Suitable)**：完整的目标跟踪或多目标管理（当前仅处理单个匹配结果）。
- **不适合 (Not Suitable)**：透视变换或非刚性 ROI 形变（输出始终为轴对齐矩形）。

## 已知限制 / Known Limitations
1. 输出为变换后 ROI 角点的整数包围矩形（Axis-Aligned Bounding Box），旋转后可能比原始 ROI 大。
2. SearchRegion 不会裁剪到图像边界内，下游算子需自行处理越界情况。
3. 非正 Scale 值被静默钳位到 1.0，不会报错。
4. 每次执行仅处理一个匹配结果（由 `MatchIndex` 选择），不支持同时输出多个跟踪区域。
5. 角度解析仅支持角度制（Degree），不支持弧度制。
6. `TryReadPose` 递归进入 `MatchResult` 时仅支持一层嵌套，不会无限递归。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充宽泛输入兼容机制、多格式位姿解析优先级、嵌套 MatchResult 递归、Position/Corners/BB 中心计算、Scale 钳位策略等核心实现细节；重写算法原理、实现策略、API 调用链、参数语义、适用场景与已知限制 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
