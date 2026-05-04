# 点位修正 / PointCorrection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PointCorrectionOperator` |
| 枚举值 (Enum) | `OperatorType.PointCorrection` |
| 分类 (Category) | 数据处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | `point-correction` |
| 关键词 (Keywords) | correction, compensation, robot, pick place |
| 版本标记 (Version Tag) | `1.0.3` |

## 算法原理 / Algorithm Principle
> **中文：** 根据参考点和参考角度对检测点执行刚体修正（平移或平移+旋转）。
>
> - **TranslationOnly 模式**：计算平移向量 `tx = Ref.X - Det.X`, `ty = Ref.Y - Det.Y`，输出 2x3 单位矩阵 + 平移。
> - **TranslationRotation 模式**：先计算角度修正 `correctionAngle = NormalizeAngle(RefAngle - DetAngle)`，
>   再通过旋转矩阵 `R(angle)` 计算刚体变换的平移分量 `tx, ty`，输出 2x3 旋转+平移矩阵。
>
> 角度归一化到 `[-180, 180)` 范围。`CorrectionX/Y` 受 `OutputUnit` 影响（Pixel 或 mm），
> 但 `TransformMatrix` 始终是**像素域** 2x3 矩阵，`TransformUnit` 固定输出 `"Pixel"`。
>
> **English:** Applies rigid correction (translation or translation+rotation) to a detected point
> based on a reference point and reference angle.
>
> - **TranslationOnly**: computes translation vector, outputs 2x3 identity + translation.
> - **TranslationRotation**: normalizes angle correction to `[-180, 180)`, computes rotation matrix
>   `R(angle)` to derive rigid transform translation components.
>
> `CorrectionX/Y` are affected by `OutputUnit` (Pixel or mm), but `TransformMatrix` is always
> in **pixel space** with `TransformUnit` fixed to `"Pixel"`.

## 实现策略 / Implementation Strategy
- 输入点位通过 `TryParsePoint` 统一解析，支持 `Position`/`Point`/`Point2f`/`Point2d` 和字典格式。
- 角度输入优先从端口读取，回退到参数（`TryResolveFiniteInputOrParameter`）。
- 所有数值（坐标、角度、PixelSize、MaxAllowedDistance）必须为有限数，NaN/Infinity 直接拒绝。
- `MaxAllowedDistance > 0` 时，在修正前检查检测点与参考点的距离，超阈值直接失败。
- `PixelSize` 用于 `OutputUnit=mm` 时的单位转换和 `MaxAllowedDistance` 的距离计算。
- `TransformMatrix` 始终输出像素域 2x3 矩阵：`[[cos, -sin, tx], [sin, cos, ty]]`（TranslationRotation）或 `[[1, 0, tx], [0, 1, ty]]`（TranslationOnly）。

## 核心 API 调用链 / Core API Call Chain
1. `inputs.TryGetValue("DetectedPoint", ...)` -> `TryParsePoint(detectedObj, out detectedPoint)`
2. `inputs.TryGetValue("ReferencePoint", ...)` -> `TryParsePoint(referenceObj, out referencePoint)`
3. `GetStringParam(@operator, "CorrectionMode", "TranslationOnly")` + `OutputUnit` 校验
4. `TryGetFiniteDoubleParameter(@operator, "PixelSize", 1.0, ...)` + `MaxAllowedDistance`
5. `TryResolveFiniteInputOrParameter(inputs, @operator, "DetectedAngle", 0.0, ...)` + `ReferenceAngle`
6. 距离检查：`detectedToReferenceDistance > maxAllowedDistance` -> 失败
7. TranslationOnly: `tx = Ref.X - Det.X`, `ty = Ref.Y - Det.Y` -> 矩阵 `[[1,0,tx],[0,1,ty]]`
8. TranslationRotation: `correctionAngle = NormalizeAngle(RefAngle - DetAngle)` -> `R(angle)` -> 刚体矩阵
9. 条件单位转换：`if (OutputUnit == "mm") { correctionX *= pixelSize; ... }`
10. `OperatorExecutionOutput.Success(...)` -> CorrectionX, CorrectionY, CorrectionAngle, TransformMatrix, TransformUnit

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `CorrectionMode` | `enum` | `"TranslationOnly"` | `TranslationOnly` / `TranslationRotation` | 修正模式。TranslationOnly=仅平移；TranslationRotation=平移+旋转。 |
| `OutputUnit` | `enum` | `"Pixel"` | `Pixel` / `mm` | CorrectionX/Y 的输出单位。 |
| `PixelSize` | `double` | `1.0` | [1E-9, 1000000.0] | 像素当量（mm/px），必须为正的有限数。用于 mm 转换和距离阈值。 |
| `MaxAllowedDistance` | `double` | `0.0` | [0.0, 1000000.0] | 检测点与参考点的最大允许距离。0=禁用检查；>0 时超阈值直接失败。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `DetectedPoint` | Detected Point | `Point` | Yes | 检测到的点位。 |
| `DetectedAngle` | Detected Angle | `Float` | No | 检测到的角度（度）。优先于参数。 |
| `ReferencePoint` | Reference Point | `Point` | Yes | 参考基准点位。 |
| `ReferenceAngle` | Reference Angle | `Float` | No | 参考基准角度（度）。优先于参数。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `CorrectionX` | Correction X | `Float` | X 修正量（按 OutputUnit 输出）。 |
| `CorrectionY` | Correction Y | `Float` | Y 修正量（按 OutputUnit 输出）。 |
| `CorrectionAngle` | Correction Angle | `Float` | 角度修正量（归一化到 [-180, 180)）。 |
| `TransformMatrix` | Transform Matrix | `Any` | 像素域 2x3 刚体变换矩阵。 |
| `TransformUnit` | Transform Unit | `String` | 固定为 `"Pixel"`。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1)（单点刚体变换计算） |
| 典型耗时 (Typical Latency) | < 0.1ms |
| 内存特征 (Memory Profile) | O(1)（仅存储输入输出标量 + 2x3 矩阵） |

## 适用场景 / Use Cases
- 适合 (Suitable)：机器人抓取引导中的点位修正（pick-and-place）
- 适合 (Suitable)：像素域检测结果到参考位置的偏移补偿
- 适合 (Suitable)：需要角度修正的刚体对齐场景
- 不适合 (Not Suitable)：需要物理坐标系修正的场景（需先标定，TransformMatrix 始终为像素域）
- 不适合 (Not Suitable)：非刚体变形（如仿射、透视变换）
- 不适合 (Not Suitable)：批量点集的修正（本算子处理单点对）

## 已知限制 / Known Limitations
1. `TransformMatrix` 始终是像素域矩阵，即使 `OutputUnit=mm` 时 `CorrectionX/Y` 为毫米值。
2. `TransformUnit` 固定输出 `"Pixel"`，不随 `OutputUnit` 变化。
3. 角度输入优先从端口读取，回退到参数；同时提供时端口值优先。
4. `MaxAllowedDistance` 的距离计算在 `OutputUnit=mm` 时乘以 `PixelSize`，在 `OutputUnit=Pixel` 时为像素距离。
5. 仅处理单点对，不支持批量修正。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面升级至 gold standard 文档；补充刚体变换公式、角度归一化、距离阈值、TransformMatrix 语义 |
| 1.0.3 | 2026-04-12 | 新增角度归一化、`TransformUnit`、`MaxAllowedDistance`；`TransformMatrix` 固定为像素域；补充非有限值拒绝规则 |
| 1.0.2 | 2026-03-14 | 文档补全 |
| 1.0.0 | 2026-03-03 | 初版 |
