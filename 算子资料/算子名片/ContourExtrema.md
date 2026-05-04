# 轮廓极值点 / Contour Extrema

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ContourExtremaOperator` |
| 枚举值 (Enum) | `OperatorType.ContourExtrema` |
| 分类 (Category) | 测量 / Measurement |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子在已知轮廓点集上沿指定方向搜索极值点（最小值点与最大值点）。

**支持的搜索方向：**

- **horizontal / x**：沿 X 轴方向，投影值 `v = pt.X`，最小值为最左点，最大值为最右点。
- **vertical / y**：沿 Y 轴方向，投影值 `v = pt.Y`，最小值为最上点，最大值为最下点。
- **distance**：基于参考点 `(rx, ry)` 的欧几里得距离，`v = sqrt((pt.X - rx)^2 + (pt.Y - ry)^2)`，最小值为最近点，最大值为最远点。

**排序策略**：先按投影值排序，再通过辅助坐标进行稳定去重（tie-breaking）：
- horizontal 模式：主键 `v` 升序/降序，辅键 `Y` 再 `X`。
- vertical / distance 模式：主键 `v`，辅键 `X` 再 `Y`。

这种确定性去重保证了在共线或重叠极值点情况下，结果具有可重复性。

> English: The operator projects every contour point onto the selected axis or distance metric, then selects minimum and maximum points with stable secondary-key tie-breaking for deterministic, repeatable measurement output.

## 实现策略 / Implementation Strategy
- 不直接调用 OpenCV 的极值函数，而是遍历所有轮廓点做标量投影，灵活支持三种方向。
- 输入轮廓支持 `Point2f[]`、`Point[]`、`IEnumerable<Point2f>` 等多种集合类型，通过 `TryGetContour` 统一转为 `List<Point2f>`。
- distance 模式要求必须提供 `ReferencePoint` 输入，否则返回失败。
- 可视化使用偏移画布绘制轮廓线、极值点标记（红=MIN，绿=MAX）及参考点连线。
- 通过 `Stopwatch` 计时并将耗时写入输出元数据。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetContour(inputs, out contour)` -- 获取输入轮廓点集
2. `GetString(inputs, "Direction", "horizontal")` -- 读取搜索方向
3. `inputs.TryGetValue("ReferencePoint", out rp)` -- 获取可选参考点
4. `ComputeExtrema(contour, direction, refPoint)` -- 计算极值
   - `GetExtremaValue(pt, direction, refPoint)` -- 标量投影
   - `OrderExtrema(values, direction, descending)` -- 排序 + 稳定去重
5. `Cv2.BoundingRect(contour)` -- 计算包围框用于可视化偏移
6. `Cv2.Polylines(vis, shiftedContour, ...)` -- 绘制轮廓
7. `Cv2.Circle(vis, shiftedMin/Max, ...)` -- 绘制极值点
8. `CreateImageOutput(vis, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| *(无算子参数)* | | | | 该算子通过输入端口传入搜索方向与参考点，不使用 `[OperatorParam]` 参数。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Contour` | `Input Contour (Points)` | `Any` | Yes | 输入轮廓点集，支持 `Point2f[]`、`Point[]`、`List<Point2f>` 等。 |
| `Direction` | `Search Direction` | `String` | No | 搜索方向：`horizontal`/`x`、`vertical`/`y`、`distance`。默认 `horizontal`。 |
| `ReferencePoint` | `Reference Point (optional)` | `Any` | No | distance 模式下的参考点，支持 `Point2f` 或 `Point`。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `ExtremaPoints` | `Extremal Points` | `Any` | 极值点列表（`List<Point2f>`），可能含 1 个（MIN=MAX 时）或 2 个点。 |
| `MinPoint` | `Minimum Point` | `Any` | 最小值点坐标 `Point2f`。 |
| `MaxPoint` | `Maximum Point` | `Any` | 最大值点坐标 `Point2f`。 |
| `Image` | `Visualization` | `Image` | 可视化图：白色轮廓线 + 红色 MIN 点 + 绿色 MAX 点 + 紫色参考点连线。 |
| `MinValue` | `Minimum Value` | `Float` | 最小值点的投影标量值。 |
| `MaxValue` | `Maximum Value` | `Float` | 最大值点的投影标量值。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `ProcessingTimeMs` | `Long` | 算子执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(N log N)`，主要由排序决定（`N` 为轮廓点数）。 |
| 典型耗时 (Typical Latency) | 平均 0.244 ms，最大 1.171 ms（22 组合成测试用例）。 |
| 内存特征 (Memory Profile) | `O(N)` 存储投影值列表，可视化图大小与轮廓包围框相关。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：在已知轮廓上查找最左/最右、最上/最下或最近/最远点。
- **适合 (Suitable)**：需要确定性去重的下游测量，确保共线极值点可重复。
- **适合 (Suitable)**：配合 `FindContours` 算子使用，先提取轮廓再找极值。
- **不适合 (Not Suitable)**：从图像中直接提取轮廓（应先使用 FindContours）。
- **不适合 (Not Suitable)**：亚像素轮廓拟合或曲率极值估计。

## 已知限制 / Known Limitations
1. 未知方向字符串默认回退为 horizontal 模式，不会报错。
2. distance 模式仅报告欧几里得距离极值，不支持其他距离度量。
3. 输入轮廓为像素精度 `Point2f`，不提供亚像素插值。
4. 可视化画布大小有最小限制（400x300），小轮廓在大画布上可能显得很小。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写至金标准 |
| 1.0.1 | 2026-03-14 | 基于 AlgorithmInfo 补充算法细节与性能数据 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
