# 点位对齐 / PointAlignment

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PointAlignmentOperator` |
| 枚举值 (Enum) | `OperatorType.PointAlignment` |
| 分类 (Category) | 数据处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | `align-point` |
| 关键词 (Keywords) | alignment, offset, reference point, distance |
| 版本标记 (Version Tag) | `1.0.3` |

## 算法原理 / Algorithm Principle
> **中文：** 计算当前点与参考点之间的像素域偏移量和欧氏距离。
> 修正方向固定为 `OffsetX = CurrentPoint.X - ReferencePoint.X`，`OffsetY = CurrentPoint.Y - ReferencePoint.Y`。
> 距离公式：`Distance = sqrt(OffsetX^2 + OffsetY^2)`。
> 当 `OutputUnit=mm` 时，三个输出均乘以 `PixelSize`（像素当量，单位 mm/px）进行单位转换。
> 本算子仅提供像素域偏移能力，**不替代真实物理标定**。
>
> **English:** Computes pixel-space offset and Euclidean distance between a current point and a reference point.
> Direction is fixed: `OffsetX = CurrentPoint.X - ReferencePoint.X`, `OffsetY = CurrentPoint.Y - ReferencePoint.Y`.
> Distance formula: `Distance = sqrt(OffsetX^2 + OffsetY^2)`.
> When `OutputUnit=mm`, all three outputs are scaled by `PixelSize` (mm/px) for unit conversion.
> This operator provides pixel-space offset only and **does not substitute for physical calibration**.

## 实现策略 / Implementation Strategy
- 输入点位通过 `TryParsePoint` 统一解析，支持 `Position`/`Point`/`Point2f`/`Point2d` 和字典格式。
- 所有点位坐标必须为有限数（finite double），NaN/Infinity 直接拒绝。
- `PixelSize` 必须为正的有限数，<=0 或 NaN/Infinity 直接拒绝。
- `OutputUnit` 校验仅接受 `Pixel` 或 `mm`（大小写不敏感）。
- 单位转换仅在 `OutputUnit=mm` 时生效，乘以 `PixelSize`。

## 核心 API 调用链 / Core API Call Chain
1. `inputs.TryGetValue("CurrentPoint", ...)` -> `TryParsePoint(currentObj, out currentPoint)`
2. `inputs.TryGetValue("ReferencePoint", ...)` -> `TryParsePoint(referenceObj, out referencePoint)`
3. `GetStringParam(@operator, "OutputUnit", "Pixel")` -> 校验合法性
4. `TryGetFiniteDoubleParameter(@operator, "PixelSize", 1.0, out pixelSize)` -> 校验正有限数
5. `offsetX = currentPoint.X - referencePoint.X` + `offsetY = currentPoint.Y - referencePoint.Y`
6. `distance = sqrt(offsetX^2 + offsetY^2)`
7. 条件：`if (outputUnit == "mm") { offsetX *= pixelSize; ... }`
8. `OperatorExecutionOutput.Success(...)` -> OffsetX, OffsetY, Distance

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `OutputUnit` | `enum` | `"Pixel"` | `Pixel` / `mm` | 输出单位。Pixel=像素值；mm=按 PixelSize 缩放。 |
| `PixelSize` | `double` | `1.0` | [1E-9, 1000000.0] | 像素当量（mm/px），必须为正的有限数。仅在 OutputUnit=mm 时生效。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `CurrentPoint` | Current Point | `Point` | Yes | 当前检测点位（Position/Point/Point2f/Point2d/字典）。 |
| `ReferencePoint` | Reference Point | `Point` | Yes | 参考基准点位。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `OffsetX` | Offset X | `Float` | X 偏移量（Current - Reference）。 |
| `OffsetY` | Offset Y | `Float` | Y 偏移量（Current - Reference）。 |
| `Distance` | Distance | `Float` | 偏移欧氏距离。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1)（单点计算） |
| 典型耗时 (Typical Latency) | < 0.1ms |
| 内存特征 (Memory Profile) | O(1)（仅存储输入输出标量） |

## 适用场景 / Use Cases
- 适合 (Suitable)：像素域的点位偏移量和距离计算
- 适合 (Suitable)：重复性检查（repeatability check）中的偏移分析
- 适合 (Suitable)：配合 PointCorrectionOperator 使用时的参考点准备
- 不适合 (Not Suitable)：需要物理坐标系偏移的场景（需先通过标定转换坐标）
- 不适合 (Not Suitable)：多点集的批量对齐（本算子处理单点对）
- 不适合 (Not Suitable)：需要旋转对齐或刚体变换的场景（请使用 PointCorrectionOperator）

## 已知限制 / Known Limitations
1. 仅支持单点对点的偏移计算，不支持批量点集对齐。
2. 仅提供像素域偏移，物理坐标解释需要额外标定步骤。
3. 输入点位支持多种类型（Position/Point/Point2f/Point2d/字典），但不支持 `DetectionResult`。
4. `PixelSize` 参数在 `OutputUnit=Pixel` 时被忽略但不会报错。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面升级至 gold standard 文档；补充算法公式、输入类型支持、单位转换细节 |
| 1.0.3 | 2026-04-12 | 明确锁定 `Current-Reference` 符号语义；补充非有限值拒绝规则 |
| 1.0.2 | 2026-03-14 | 文档补全 |
| 1.0.0 | 2026-03-03 | 初版 |
