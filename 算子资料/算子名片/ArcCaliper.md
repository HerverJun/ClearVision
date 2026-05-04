# 圆弧卡尺 / Arc Caliper

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ArcCaliperOperator` |
| 枚举值 (Enum) | `OperatorType.ArcCaliper` |
| 分类 (Category) | Measurement |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

圆弧卡尺沿指定圆弧路径等角度步进采样，在每个采样点处沿径向方向提取带状灰度轮廓（band profile），检测梯度边缘并取最强边缘作为该角度位置的亚像素边缘点。

The Arc Caliper walks along a specified arc path in uniform angular steps. At each step it samples a radial band profile centered on the arc radius, detects gradient edges on that profile, and selects the strongest edge as the subpixel edge point for that angular position.

**径向带状采样 / Radial Band Sampling:**
- 采样点位于圆弧上：`sampleX = cx + radius * cos(angle)`, `sampleY = cy + radius * sin(angle)`
- 在每个采样点处，沿径向方向（从圆心向外）提取长度为 12px（`searchHalfLength=6`）的带状轮廓
- 带状轮廓通过 `IndustrialCaliperKernel.SampleBandProfile` 生成，带宽为 5px（`averagingThickness=5`），采样 33 点
- 角度步长固定为 1 度

**边缘检测 / Edge Detection:**
- 每条径向轮廓上的边缘通过 `IndustrialCaliperKernel.DetectEdges` 检测
- 阈值取 `max(6.0, EstimateEdgeThreshold(profile, minimumThreshold: 4.0))`，确保低对比度场景下仍有基础检测能力
- 支持极性过滤：`Transition` 参数映射为 DarkToLight / LightToDark / Both
- 多个边缘中取 `Strength` 最大者作为该角度的最佳边缘

**亚像素定位 / Subpixel Localization:**
- 最佳边缘位置通过 `IndustrialCaliperKernel.InterpolatePosition` 从轮廓索引映射回图像坐标
- 输出的 (X, Y) 坐标为亚像素精度

**圆弧角度处理 / Arc Angle Handling:**
- 起止角度通过 `NormalizeAngleDegrees` 归一化到 [0, 360)
- 弧度跨度通过 `ComputePositiveArcSpanDegrees` 计算正向弧长，支持跨 0/360 度的圆弧
- 采样步数 = `ceil(arcSpan / 1.0)`，确保至少 1 步

## 实现策略 / Implementation Strategy

- **对标 Halcon `measure_pos` on arc**：功能类似 Halcon 的圆弧测量，但使用自定义的带状采样和边缘检测内核而非 Halcon 的 Measure 工具。
- **复用 IndustrialCaliperKernel**：与 CaliperToolOperator 共享同一套带状采样和边缘检测基础设施，确保算法一致性。
- **固定角度步长**：当前步长固定为 1 度，不支持自适应步长。对于非常短的圆弧（<1 度），可能需要专用算子。
- **采样区域边界检查**：每个采样点都通过 `IsWithinSamplingRegion` 检查是否在图像有效区域内（距边缘 >= 6px），无效点会被跳过。
- **失败检测**：如果所有采样点都在有效区域外（`accessibleSamples == 0`），返回失败。

## 核心 API 调用链 / Core API Call Chain

1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `GetInt / GetDouble / GetString` -- 读取 CenterX, CenterY, Radius, StartAngle, EndAngle, Transition
3. `NormalizeAngleDegrees(startAngle)` -- 角度归一化
4. `ComputePositiveArcSpanDegrees(startAngle, endAngle)` -- 计算弧度跨度
5. `image.CvtColor(BGR2GRAY)` -- 灰度转换
6. 循环 `for i = 0..steps`:
   a. 计算采样角度和坐标 `(sampleX, sampleY)`
   b. `IsWithinSamplingRegion(gray, sampleX, sampleY)` -- 边界检查
   c. `TryLocateArcEdge(gray, cx, cy, radius, rad, transition, ...)`:
      - `IndustrialCaliperKernel.SampleBandProfile(gray, start, end, 5.0, 33)` -- 径向带状采样
      - `IndustrialCaliperKernel.EstimateEdgeThreshold(profile, 4.0)` -- 自适应阈值
      - `IndustrialCaliperKernel.DetectEdges(profile, threshold, polarity, sigma: 1.2)` -- 边缘检测
      - `edges.OrderByDescending(edge => edge.Strength).First()` -- 取最强边缘
      - `IndustrialCaliperKernel.InterpolatePosition(start, end, bestEdge.Position, 33)` -- 亚像素坐标映射
7. `CreateVisualization(image, cx, cy, radius, start, end, points)` -- 绘制结果
8. `CreateImageOutput(vis, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| (无算子参数，所有配置通过输入端口传入) | | | | |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Input Image | `Image` | Yes | 输入灰度或彩色图像。彩色图像会自动转换为灰度。 |
| `CenterX` | Arc Center X | `Integer` | Yes | 圆弧中心 X 坐标（像素）。 |
| `CenterY` | Arc Center Y | `Integer` | Yes | 圆弧中心 Y 坐标（像素）。 |
| `Radius` | Arc Radius | `Integer` | Yes | 圆弧半径（像素）。必须大于 0。 |
| `StartAngle` | Start Angle (deg) | `Float` | No | 起始角度（度），默认 0。支持负值和大于 360 的值。 |
| `EndAngle` | End Angle (deg) | `Float` | No | 结束角度（度），默认 360。弧度跨度必须大于 0。 |
| `Transition` | Transition Type | `String` | No | 边缘极性类型："positive"(DarkToLight)、"negative"(LightToDark)、"all"(Both)，默认 "all"。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Points` | Detected Edge Points | `Any` | 检测到的边缘点列表 (List<ArcCaliperPoint>)，每个点包含 X, Y, Angle, Radius, Contrast。 |
| `Image` | Visualization | `Image` | 可视化结果图，绘制圆弧（黄色）、中心点（红色）和检测到的边缘点（绿色）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Count` | `int` | 检测到的边缘点总数。 |
| `AverageContrast` | `double` | 所有边缘点的平均对比度（边缘强度）。无边缘点时为 0。 |
| `ProcessingTimeMs` | `long` | 处理耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(A * S)，A 为角度步数（= arcSpan / 1 度），S 为每步的轮廓采样点数（固定 33） |
| 典型耗时 (Typical Latency) | 平均 4.169ms，最大 5.747ms（31 个合成黄金测试用例） |
| 内存特征 (Memory Profile) | O(S + P)，S 为采样缓冲区（33 个 double），P 为边缘点列表。内存开销极小。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：已知圆心和半径的圆弧边缘检测，如孔径边缘、圆形零件的圆弧段测量。
- 适合 (Suitable)：需要沿圆弧路径进行亚像素边缘定位的精密测量场景。
- 适合 (Suitable)：环形或弧形结构的边缘质量评估（通过 AverageContrast 指标）。
- 不适合 (Not Suitable)：未知圆心或半径的圆检测场景（请使用 CircleMeasurement 算子）。
- 不适合 (Not Suitable)：低纹理圆弧上无边缘响应的场景，当前不提供显式的无边缘失败状态。

## 已知限制 / Known Limitations
1. 角度步长固定为 1 度，非常短的圆弧（<1 度跨度）可能需要更紧凑的专用测量算子。
2. 输出报告检测点和数量，但不暴露单点不确定度或显式的无边缘失败状态。
3. 采样区域边界检查要求距图像边缘 >= 6px，靠近边界的圆弧部分可能无法采样。
4. 过渡类型（Transition）参数通过字符串传入，不支持算子参数面板的枚举选择。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码重写为金标准文档：补充完整的径向带状采样原理、IndustrialCaliperKernel 调用链、角度处理逻辑、性能基准数据 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
