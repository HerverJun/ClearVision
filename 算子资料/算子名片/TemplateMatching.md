# 模板匹配 / Template Matching

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `TemplateMatchOperator` |
| 枚举值 (Enum) | `OperatorType.TemplateMatching` |
| 分类 (Category) | 匹配定位 |
| 成熟度 (Maturity) | 稳定 Stable |
| 当前版本 (Version) | `1.2.0` |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

该算子在搜索图像上滑动模板并生成响应图，然后从响应图中提取多个候选并做 IoU NMS。

核心流程：
- 输入图和模板图统一转换到指定域（Gray / Edge / Gradient）
- `Cv2.MatchTemplate` 计算滑动相关响应图
- 对响应图构建阈值化分数图和归一化分数图，修正 `SqDiff` / `SqDiffNormed` 的低分语义
- 从响应图中持续提取峰值并做 FloodFill 局部抑制，再对全局候选做 IoU NMS
- 可选启用姿态搜索（`EnablePoseSearch`）：在角度/尺度空间枚举变换，对每个变换后的模板执行匹配

当前版本把输出分成两层语义：

- `RawResponse`：OpenCV `MatchTemplate` 的原始响应值
- `NormalizedScore`：canonical 的高分更好分数，面向新流程消费
- `Score`：保留的兼容字段；当前仍等于算子用于阈值判定的分数

对 `SqDiff` / `SqDiffNormed` 的处理已修正：
- `SqDiffNormed`：`RawResponse = rawSqDiffNormed`，`NormalizedScore = 1 - RawResponse`
- `SqDiff`：`RawResponse = rawSqDiff`，`NormalizedScore = 1 - rawSqDiff / (templateArea * 255^2)`

亚像素精度：
- 对峰值位置做 3x3 抛物线拟合，输出 `SubpixelOffsetX`、`SubpixelOffsetY` 和 `PeakCurvature`

This operator slides a template across the search image to produce a response map, extracts multiple peaks from the response map, and filters candidates with IoU-based NMS. The `SqDiff` / `SqDiffNormed` score semantics have been corrected so that `NormalizedScore` is always "higher is better". Subpixel precision is achieved via 3x3 parabolic interpolation at peak locations.

## 实现策略 / Implementation Strategy
- 输入图和模板图统一走 `TryGetInputImage(...)` 解码
- 可选做 ROI 裁剪与搜索掩膜限制（`UseRoi` / `Mask` 输入）
- `Gray / Edge / Gradient` 三种域都会先生成可匹配图，再调用 `Cv2.MatchTemplate(...)`
- 候选提取不再只依赖单次 `MinMaxLoc`；会从响应图中持续取峰值并做 FloodFill 局部抑制，然后再做 IoU NMS
- `MaxMatches` 已实际生效，可返回多个离散匹配
- 姿态搜索模式下：枚举角度/尺度变换，对每个变换用 `Cv2.WarpAffine` + `Cv2.Resize` 变换模板，支持多层金字塔粗筛加速

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", ...)` / `TryGetInputImage(inputs, "Template", ...)`
2. `PrepareMatchImage(searchRegion, domain)` -- 灰度转换 + 可选 Canny 边缘 / Sobel 梯度
3. `PrepareSearchMask(inputs, roi, searchSize)` -- 掩膜裁剪与二值化
4. `HasSufficientSignal(preparedTemplate)` -- 检查模板是否有足够纹理
5. `ResolveReferenceOrigin(@operator, templateSize)` -- 解析 OriginMode（Center / TopLeft / Custom）
6. 固定姿态路径：`FindFixedPoseMatches(...)` -> `Cv2.MatchTemplate(...)` -> `FindMatches(...)`
7. 姿态搜索路径：`FindPoseMatches(...)` -> `BuildPoseCandidates(...)` -> `TransformTemplate(...)` -> `MatchTemplate(...)` -> `FindMatches(...)`
8. `BuildThresholdScoreMap(result, method, templateSize)` / `BuildNormalizedScoreMap(...)` -- 分数图构建
9. `FindMatches(...)` -- 循环 `MinMaxLoc` + `SuppressCandidateRegion(FloodFill)` + `CreateCandidate(EstimateSubpixelPeak)`
10. `ApplyNms(candidates, 0.35)` -- IoU NMS
11. ROI 偏移修正：`match.Offset(roi.X, roi.Y)`
12. 结果绘制：矩形 + 标记 + 分数文本

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Method` | `enum` | `CCoeffNormed` | CCoeffNormed / SqDiff / SqDiffNormed / CCorr / CCorrNormed / CCoeff | 匹配方法。CCoeffNormed 为零均值归一化互相关，SqDiff 为平方差，SqDiffNormed 为归一化平方差 |
| `Domain` | `enum` | `Gray` | Gray / Edge / Gradient | 匹配域。Gray 直接灰度匹配，Edge 用 Canny 边缘，Gradient 用 Sobel 梯度幅值 |
| `Threshold` | `double` | `0.8` | [0.0, 1.0] | 候选阈值。对 SqDiff / SqDiffNormed，阈值比较的是修正后的高分更好分数 |
| `MaxMatches` | `int` | `1` | [1, 100] | 最多保留的匹配数量 |
| `UseRoi` | `bool` | `false` | - | 是否启用 ROI 搜索 |
| `RoiX` | `int` | `0` | [0, +inf) | ROI 左上角 X 坐标 |
| `RoiY` | `int` | `0` | [0, +inf) | ROI 左上角 Y 坐标 |
| `RoiWidth` | `int` | `0` | [0, +inf) | ROI 宽度 |
| `RoiHeight` | `int` | `0` | [0, +inf) | ROI 高度 |
| `OriginMode` | `enum` | `Center` | Center / TopLeft / Custom | 参考原点模式。Center 取模板中心，TopLeft 取左上角，Custom 使用 OriginX/OriginY |
| `OriginX` | `double` | `0.0` | - | Custom 模式下的参考原点 X |
| `OriginY` | `double` | `0.0` | - | Custom 模式下的参考原点 Y |
| `EnablePoseSearch` | `bool` | `false` | - | 启用姿态搜索（旋转/尺度） |
| `AngleStart` | `double` | `0.0` | [-180.0, 180.0] | 角度搜索起点（度） |
| `AngleExtent` | `double` | `0.0` | [0.0, 360.0] | 角度搜索范围（度） |
| `AngleStep` | `double` | `1.0` | [0.1, 45.0] | 角度步长（度） |
| `ScaleMin` | `double` | `1.0` | [0.2, 3.0] | 最小缩放系数 |
| `ScaleMax` | `double` | `1.0` | [0.2, 3.0] | 最大缩放系数 |
| `ScaleStep` | `double` | `0.05` | [0.01, 1.0] | 缩放步长 |
| `PyramidLevels` | `int` | `1` | [1, 4] | 姿态搜索金字塔层数。大于 1 时先在粗层筛除低分姿态候选 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 搜索图像 |
| `Template` | 模板图像 | `Image` | Yes | 模板图像 |
| `Mask` | 搜索掩膜 | `Image` | No | 搜索掩膜；非零区域允许搜索 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 结果图，绘制每个最终候选的矩形、标记和分数 |
| `Position` | 匹配位置 | `Point` | 最佳匹配参考原点位置（含亚像素偏移） |
| `Score` | 匹配分数 | `Float` | legacy 兼容分数字段 |
| `NormalizedScore` | 规范化分数 | `Float` | canonical 分数，新流程优先读这个字段 |
| `RawResponse` | 原始响应值 | `Float` | 原始 OpenCV 响应值 |
| `SubpixelOffsetX` | 亚像素峰值 X 偏移 | `Float` | 峰值亚像素 X 偏移量（[-0.5, 0.5]） |
| `SubpixelOffsetY` | 亚像素峰值 Y 偏移 | `Float` | 峰值亚像素 Y 偏移量（[-0.5, 0.5]） |
| `PeakCurvature` | 响应峰曲率 | `Float` | 峰值处的抛物线曲率，越大越尖锐 |
| `Angle` | 匹配角度 | `Float` | 匹配角度（姿态搜索模式） |
| `Scale` | 匹配尺度 | `Float` | 匹配尺度（姿态搜索模式） |
| `IsMatch` | 是否匹配 | `Boolean` | 是否存在满足阈值的候选 |
| `Matches` | 匹配列表 | `Any` | 匹配列表，每项包含 Position, Center, Score, NormalizedScore, RawResponse, SubpixelOffsetX, SubpixelOffsetY, PeakCurvature, Angle, Scale, Width, Height |
| `MatchCount` | 匹配数量 | `Integer` | 最终匹配数量 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 固定姿态：O((W-w)*(H-h)*w*h)；姿态搜索：O(A*S*(W-w)*(H-h)*w*h)，A 为角度数，S 为尺度数；金字塔模式下粗层过滤可大幅减少细层候选 |
| 典型耗时 (Typical Latency) | 固定姿态 1024x768 模板 64x64 约 5-20ms；姿态搜索视角度/尺度范围可达 100ms+ |
| 内存特征 (Memory Profile) | 主要为响应图 O((W-w+1)*(H-h+1)) 和中间 Mat；姿态搜索模式下额外产生变换模板副本 |

## 适用场景 / Use Cases
- 适合 (Suitable)：固定尺度、固定角度的刚性模板定位
- 适合 (Suitable)：有界旋转/尺度范围的姿态搜索
- 适合 (Suitable)：多实例匹配（MaxMatches > 1）
- 不适合 (Not Suitable)：强遮挡、强非刚体形变或明显透视变化的场景
- 不适合 (Not Suitable)：无纹理或纹理极弱的模板

## 已知限制 / Known Limitations
1. 固定姿态模式不负责旋转/尺度搜索，需手动启用 `EnablePoseSearch`
2. `CCorr` / `CCoeff` 的 `RawResponse` 仍保留 OpenCV 原始量纲，新流程应优先依赖 `NormalizedScore`
3. 重复纹理或强周期背景下，仍需结合 ROI、Mask 或更高阈值使用
4. 姿态搜索的金字塔粗筛可能在极端角度/尺度组合下漏检

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档，补充所有端口、参数范围、姿态搜索机制和亚像素精度说明 |
| 1.2.0 | 2026-04-12 | 修正 SqDiff / SqDiffNormed 评分语义，新增 NormalizedScore 与 RawResponse，同步澄清 legacy / canonical 关系 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码补充实际匹配模式、得分归一化、模板输入形态与参数限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
