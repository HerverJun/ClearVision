# 位置修正 / Position Correction

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PositionCorrectionOperator` |
| 枚举值 (Enum) | `OperatorType.PositionCorrection` |
| 分类 (Category) | 定位 |
| 成熟度 (Maturity) | 稳定 Stable |
| 当前版本 (Version) | `1.0.2` |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

该算子在像素空间对目标位置进行偏差校正，支持纯平移和刚性（平移+旋转）两种模式。

纯平移模式（Translation）：
- 计算参考点与基准点的偏移量：`offsetX = ReferencePoint.X - BasePoint.X`，`offsetY = ReferencePoint.Y - BasePoint.Y`
- 校正后位置：`CorrectedX = RoiX + offsetX`，`CorrectedY = RoiY + offsetY`
- 输出 2x3 平移变换矩阵：`[[1, 0, offsetX], [0, 1, offsetY]]`

刚性模式（TranslationRotation）：
- 在纯平移基础上引入旋转补偿
- 角度差：`angleDelta = NormalizeAngle(ReferenceAngle - CurrentAngle)`
- 将 ROI 坐标转换到基准点局部坐标系，应用旋转变换，再平移到参考点
- 校正公式：`correctedX = ReferencePoint.X + (localX * cos - localY * sin)`
- 输出 2x3 刚性变换矩阵：`[[cos, -sin, tx], [sin, cos, ty]]`

角度归一化：
- `NormalizeAngle` 将角度差归一化到 [-180, 180] 范围

输入点格式兼容：
- 支持 `Position`、`Point`、`Point2f`、`Point2d`、`IDictionary<string, object>` 和字符串解析（如 "(1.5, 2.3)"）

This operator performs pixel-space position correction with two modes: Translation (offset only) and TranslationRotation (rigid transform with rotation compensation). The angle difference is normalized to [-180, 180] degrees. Input points are accepted in multiple formats including Position, OpenCV Point types, dictionaries, and string parsing.

## 实现策略 / Implementation Strategy
- 纯计算算子，不涉及图像处理，执行效率极高
- 输入点解析使用 `TryParsePoint`，支持多种格式自动识别
- `GetInputOrParamDouble` 优先从输入端口读取，回退到参数值
- 刚性模式下 `CurrentAngle` 支持从输入端口 `BaseAngle` 覆盖
- 变换矩阵以 `double[][]` 形式输出，兼容下游坐标变换算子

## 核心 API 调用链 / Core API Call Chain
1. `inputs.TryGetValue("ReferencePoint", ...)` + `TryParsePoint(referenceObj, out referencePoint)`
2. `inputs.TryGetValue("BasePoint", ...)` + `TryParsePoint(baseObj, out basePoint)`
3. `GetStringParam(@operator, "CorrectionMode", "Translation")`
4. `GetDoubleParam(@operator, "ReferenceAngle", 0.0, -360.0, 360.0)`
5. `GetInputOrParamDouble(inputs, @operator, "RoiX", 0.0)` / `GetInputOrParamDouble(inputs, @operator, "RoiY", 0.0)`
6. 计算 `offsetX`, `offsetY`, `correctedX`, `correctedY`
7. Translation 模式：`BuildTranslationMatrix(offsetX, offsetY)` -- 2x3 平移矩阵
8. TranslationRotation 模式：
   - `GetInputOrParamDouble(inputs, @operator, "CurrentAngle", 0.0)` -- 可从 BaseAngle 输入覆盖
   - `NormalizeAngle(referenceAngle - currentAngle)` -- 角度归一化
   - 局部坐标旋转：`rotatedX = localX * cos - localY * sin`
   - `BuildRigidTransformMatrix(basePoint, referencePoint, cos, sin)` -- 2x3 刚性矩阵
9. 输出所有端口值

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `CorrectionMode` | `enum` | `Translation` | Translation / TranslationRotation | 工作模式。Translation 仅平移，TranslationRotation 平移+旋转 |
| `ReferenceAngle` | `double` | `0.0` | [-360.0, 360.0] | 参考角度（度），TranslationRotation 模式下使用 |
| `CurrentAngle` | `double` | `0.0` | [-360.0, 360.0] | 当前角度（度），可被 BaseAngle 输入端口覆盖 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `ReferencePoint` | Reference Point | `Point` | Yes | 参考点（目标位置） |
| `BasePoint` | Base Point | `Point` | Yes | 基准点（实际位置） |
| `RoiX` | ROI X | `Integer` | No | 待校正的 ROI X 坐标，优先从输入端口读取，回退到参数值 |
| `RoiY` | ROI Y | `Integer` | No | 待校正的 ROI Y 坐标，优先从输入端口读取，回退到参数值 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `CorrectedX` | Corrected X | `Integer` | 校正后的 X 坐标（四舍五入） |
| `CorrectedY` | Corrected Y | `Integer` | 校正后的 Y 坐标（四舍五入） |
| `OffsetX` | Offset X | `Float` | 参考点与基准点的 X 偏移量 |
| `OffsetY` | Offset Y | `Float` | 参考点与基准点的 Y 偏移量 |
| `Angle` | Angle | `Float` | 角度差（度），Translation 模式下为 0 |
| `AppliedOffsetX` | Applied Offset X | `Float` | 实际应用的 X 偏移（含旋转影响） |
| `AppliedOffsetY` | Applied Offset Y | `Float` | 实际应用的 Y 偏移（含旋转影响） |
| `TransformMatrix` | Transform Matrix | `Any` | 2x3 变换矩阵（平移或刚性） |
| `RotationCenter` | Rotation Center | `Point` | 旋转中心点（即 BasePoint） |
| `CompensationMode` | Compensation Mode | `String` | 实际使用的补偿模式 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1)，纯数学计算，无循环 |
| 典型耗时 (Typical Latency) | < 0.1ms，可忽略不计 |
| 内存特征 (Memory Profile) | 仅分配输出字典和 2x3 矩阵，内存开销极小 |

## 适用场景 / Use Cases
- 适合 (Suitable)：检测结果与标定参考之间的像素级偏差校正
- 适合 (Suitable)：机器人引导前的坐标补偿（需配合标定算子使用）
- 适合 (Suitable)：TranslationRotation 模式下的旋转工件定位补偿
- 不适合 (Not Suitable)：直接作为物理世界补偿（需先通过标定算子建立像素-物理映射）
- 不适合 (Not Suitable)：标定数据质量差时的精密校正

## 已知限制 / Known Limitations
1. 输出为像素空间坐标，不直接提供物理世界补偿
2. 纯平移模式不考虑旋转影响，RotationCenter 和 Angle 输出无意义
3. 角度归一化到 [-180, 180]，超过 180 度的旋转差会被折叠

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档，补充 AppliedOffsetX/Y、TransformMatrix、RotationCenter、CompensationMode 输出端口和刚性变换矩阵说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
