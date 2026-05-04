# 几何公差 / Geometric Tolerance

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `GeometricToleranceOperator` |
| 枚举值 (Enum) | `OperatorType.GeometricTolerance` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

几何公差算子实现 2D GD&T（Geometric Dimensioning and Tolerancing）子集，基于特征/基准语义和公差带模型评估平行度、垂直度、位置度和同心度。

The Geometric Tolerance operator implements a constrained 2D GD&T subset, evaluating Parallelism, Perpendicularity, Position, and Concentricity using feature/datum semantics and tolerance-zone models.

**测量模型 / Measurement Model:**
- 当前模型为 `DatumZone2D`，基于 2D 基准坐标系（datum frame）评估公差
- 输出 `Accepted` 位表示是否在公差带内

**公差类型详解 / Tolerance Types:**

**1. 平行度 (Parallelism):**
- 输入：FeaturePrimary（线）+ DatumA（线）
- 评估：计算特征线两端点到基准无限直线的距离差，即线性带偏差 = |dist(start) - dist(end)|
- 角度偏差 = 两线方向夹角
- 判定：linearBand <= zoneSize 时 Accepted

**2. 垂直度 (Perpendicularity):**
- 输入：FeaturePrimary（线）+ DatumA（线）
- 评估：
  - 角度偏差 = |两线夹角 - 90度|
  - 线性带 = 特征线两端点在基准方向上的投影差 = |projection(end) - projection(start)|
- 判定：perpendicularBand <= zoneSize 时 Accepted

**3. 位置度 (Position):**
- 输入：FeaturePrimary（点或圆）+ DatumA（线）+ DatumB（线）
- 评估：
  - DatumA 和 DatumB 的交点定义基准坐标系原点
  - DatumA 方向为 X 轴，DatumB 正交化后为 Y 轴
  - 将特征中心投影到基准坐标系，与名义位置 (NominalX, NominalY) 比较
  - 偏差计算取决于 EvaluationMode:
    - CircularZone: sqrt(deltaX^2 + deltaY^2)
    - RectangularZone: max(|deltaX|, |deltaY|)
    - Projected2D: |deltaX| + |deltaY|
- 判定：zoneDeviation <= zoneSize/2 时 Accepted

**4. 同心度 (Concentricity):**
- 输入：FeaturePrimary（圆）+ DatumA（圆）
- 评估：两圆圆心距 = sqrt((cx1-cx2)^2 + (cy1-cy2)^2)
- 判定：centerOffset <= zoneSize/2 时 Accepted

**不确定度传播 / Uncertainty Propagation:**
- 每种公差类型都有专门的不确定度传播函数
- 使用 `MeasurementGeometryHelper.PropagateCustomCoordinateUncertainty` 通过蒙特卡洛方法传播
- 输入元素可携带显式 `UncertaintyPx` 字段，否则使用启发式估计
- 置信度：margin >= 0 时 = clamp(0.5 + margin/(limit+sigma), 0, 1)；margin < 0 时 = clamp(1/(1+|margin|+sigma), 0, 1)

## 实现策略 / Implementation Strategy

- **多态输入解析**：`TryParsePoint` / `TryParseLine` / `TryParseCircle` 支持强类型和字典格式
- **基准坐标系构建**：Position 公差需要 DatumA 和 DatumB 两条线定义正交坐标系，通过 Gram-Schmidt 正交化确保轴垂直
- **可选图像输出**：Image 输入为可选，有图像时绘制特征/基准几何和 PASS/FAIL 标注
- **公差裕度**：输出 `ToleranceMargin = acceptanceLimit - zoneDeviation`，正值表示在公差内
- **退化检测**：退化线（零长度）和退化基准（平行线无法定义坐标系）会返回明确错误

## 核心 API 调用链 / Core API Call Chain

1. `inputs.TryGetValue("FeaturePrimary" / "DatumA" / "DatumB")` -- 获取特征和基准
2. `GetStringParam(@operator, "ToleranceType" / "EvaluationMode")` -- 读取参数
3. `TryEvaluate(toleranceType, evaluationMode, zoneSize, nominalX, nominalY, ...)` -- 核心评估:
   - 分支 Parallelism:
     a. `TryParseLine(featureObj)` / `TryParseLine(datumAObj)`
     b. `MeasurementGeometryHelper.AngleBetweenLineDirections(feature, datum)` -- 角度偏差
     c. `MeasurementGeometryHelper.DistancePointToInfiniteLine(...)` -- 端点到基准距离
     d. linearBand = |distStart - distEnd|
   - 分支 Perpendicularity:
     a. 角度偏差 = |夹角 - 90|
     b. 基准方向投影，计算线性带
   - 分支 Position:
     a. `TryResolveCenter(featureObj)` -- 获取特征中心
     b. `MeasurementGeometryHelper.TryGetInfiniteLineIntersection(datumA, datumB)` -- 基准交点
     c. `TryCreateDatumFrame(origin, datumA, datumB)` -- 构建基准坐标系
     d. `ProjectToFrame(featureCenter, frame)` -- 投影到基准坐标系
     e. `ComputePositionZoneDeviation(mode, deltaX, deltaY)` -- 偏差计算
   - 分支 Concentricity:
     a. `TryParseCircle(featureObj)` / `TryParseCircle(datumAObj)`
     b. `MeasurementGeometryHelper.Distance(cx1, cy1, cx2, cy2)` -- 圆心距
4. `ComputeUncertaintyPx(...)` -- 不确定度传播
5. `GetAcceptanceLimit(toleranceType, evaluationMode, zoneSize)` -- 验收限
6. `ComputeConfidence(acceptanceLimit, deviation, uncertaintyPx)` -- 置信度
7. `DrawOverlay(resultImage, feature, datumA, datumB, evaluation)` (可选) -- 可视化
8. 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ToleranceType` | `enum` | `"Parallelism"` | Parallelism / Perpendicularity / Position / Concentricity | 公差类型。 |
| `ZoneSize` | `double` | `2.0` | [0.0, +inf) | 公差带大小（像素）。Parallelism/Perpendicularity 直接比较；Position/Concentricity 与 zoneSize/2 比较。 |
| `EvaluationMode` | `enum` | `"CircularZone"` | CircularZone / RectangularZone / Projected2D | 位置度评估模式。CircularZone: 圆形公差带 (L2)；RectangularZone: 矩形公差带 (Linf)；Projected2D: 投影公差带 (L1)。 |
| `NominalX` | `double` | `0.0` | - | 位置度的名义 X 坐标（基准坐标系下）。仅 Position 公差生效。 |
| `NominalY` | `double` | `0.0` | - | 位置度的名义 Y 坐标（基准坐标系下）。仅 Position 公差生效。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | No | 可选输入图像，用于可视化叠加。 |
| `FeaturePrimary` | Primary Feature | `Any` | Yes | 被测特征。支持 Position (Point)、LineData (Line)、CircleData (Circle) 或字典格式。 |
| `DatumA` | Datum A | `Any` | Yes | 基准 A。支持 LineData 或 CircleData。 |
| `DatumB` | Datum B | `Any` | No | 基准 B。仅 Position 公差需要，必须为 LineData。 |
| `DatumC` | Datum C | `Any` | No | 基准 C。当前实现未使用。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 可视化结果图（仅在提供 Image 输入时输出）。绘制特征（红色）、基准A（绿色）、基准B（黄绿色）和 PASS/FAIL 标注。 |
| `Tolerance` | 公差带 | `Float` | 设定的公差带大小。 |
| `ZoneDeviation` | 偏离公差带 | `Float` | 实际偏差值。 |
| `AngularDeviationDeg` | 角度偏差(度) | `Float` | 角度偏差（度）。仅 Parallelism/Perpendicularity 非零。 |
| `LinearBand` | 线性偏差带(像素) | `Float` | 线性偏差带（像素）。 |
| `MeasurementModel` | 测量模型 | `String` | 固定为 "DatumZone2D"。 |
| `Accepted` | Accepted | `Boolean` | 是否在公差带内。true = PASS, false = FAIL。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `ToleranceMargin` | `double` | 公差裕度 = acceptanceLimit - zoneDeviation。正值表示在公差内。 |
| `ToleranceType` | `string` | 公差类型。 |
| `EvaluationMode` | `string` | 评估模式。 |
| `Result` | `string` | 结果描述文本。 |
| `StatusCode` | `string` | `"OK"` 或 `"OutOfTolerance"`。 |
| `StatusMessage` | `string` | 状态描述。 |
| `Confidence` | `double` | 置信度。 |
| `UncertaintyPx` | `double` | 偏差测量的合成不确定度（像素）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) -- 纯数学计算（不含图像绘制时） |
| 典型耗时 (Typical Latency) | < 0.1ms（不含图像处理和不确定度蒙特卡洛传播）。含图像绘制约 0.5-2ms。 |
| 内存特征 (Memory Profile) | 极低。仅分配输出字典和少量临时变量。含图像时需要结果图克隆。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：工业零件的平行度、垂直度检测。
- 适合 (Suitable)：孔位位置度评估（需要 DatumA + DatumB 定义基准坐标系）。
- 适合 (Suitable)：同心度检测（两个圆的圆心偏差）。
- 适合 (Suitable)：需要不确定度传播和置信度评估的 GD&T 测量流水线。
- 不适合 (Not Suitable)：3D GD&T 评估（当前仅支持 2D）。
- 不适合 (Not Suitable)：圆度、圆柱度等需要 3D 数据的公差类型。
- 不适合 (Not Suitable)：需要 ASME Y14.5 完整公差带语义的场景（当前为简化的 2D 子集）。

## 已知限制 / Known Limitations
1. 仅实现 Parallelism、Perpendicularity、Position、Concentricity 四种公差类型，不支持圆度、圆柱度等。
2. 测量模型为简化的 `DatumZone2D`，不完全符合 ASME Y14.5 或 ISO 1101 标准。
3. Position 公差需要 DatumA 和 DatumB 两条线相交定义基准坐标系，平行基准线会返回错误。
4. DatumC 输入端口当前未使用，预留为未来扩展。
5. 不确定度传播使用蒙特卡洛方法，计算成本随输入元素复杂度增加。
6. 退化线（零长度）会返回明确错误，但不会自动修复或降级。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码重写为金标准文档：补充 DatumZone2D 模型原理、四种公差类型评估算法、基准坐标系构建、不确定度传播、验收判定逻辑 |
| 1.0.0 | 2026-03-03 | 初始版本，包含基本公差评估功能 |
