# 形状匹配 / Shape Matching

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ShapeMatchingOperator` |
| 枚举值 (Enum) | `OperatorType.ShapeMatching` |
| 分类 (Category) | Matching |
| 成熟度 (Maturity) | 稳定 Stable |
| 当前版本 (Version) | `1.2.0` |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

该算子实现旋转/尺度模板匹配，使用金字塔 coarse-to-fine 搜索策略。虽然名称叫"形状匹配"，当前实现本质上是灰度模板的旋转/尺度搜索，而不是轮廓描述子匹配。

核心流程：
- 输入图和模板图统一转成灰度
- 构建图像金字塔（`Cv2.PyrDown`），层数由 `NumLevels` 控制
- 从最粗层开始，枚举角度/尺度变换组合
- 对每个变换后的模板执行 `Cv2.MatchTemplate(..., CCoeffNormed)` 或 `Cv2.MatchTemplate(..., CCorrNormed)`（有 mask 时）
- 从单个变换的响应图中持续提取多个峰值（`MinMaxLoc` + 局部抑制）
- 对变换内候选做局部抑制，再对全局候选做 IoU NMS
- 粗层匹配结果指导下一层的角度/尺度搜索范围（`BuildRefinedAngles` / `BuildRefinedScales`）
- 最终层（level 0）的匹配结果经 ReScore 验证后输出

亚像素精度：
- 对峰值位置做 5x5 二次最小二乘拟合（优先）或 3x3 Hessian 拟合（回退），输出亚像素偏移

变换几何计算：
- 对每个 (angle, scale) 变换，计算变换后的模板中心偏移和参考原点偏移
- 支持 mask 感知变换：对旋转后的模板生成有效区域 mask，mask 外区域不参与匹配评分

This operator performs rotation-scale template matching with a pyramid coarse-to-fine search strategy. Despite its name "Shape Matching", it is fundamentally a grayscale template matching approach with rotation/scale pose search, not a contour descriptor matcher. Pyramid coarse-to-fine search prunes the angle/scale search space at each level. Subpixel precision is achieved via 5x5 quadratic least-squares fitting (preferred) or 3x3 Hessian fitting (fallback).

## 实现策略 / Implementation Strategy
- 灰度转换：`Cv2.CvtColor(BGR2GRAY)` 或直接 clone 单通道图
- 金字塔构建：`Cv2.PyrDown` 逐层下采样，最小层尺寸限制（src >= 8x8, tmpl >= 4x4）
- 角度/尺度步长在粗层自动放大（`ComputeLevelAngleStep` / `ComputeLevelScaleStep`），细层恢复基础步长
- 变换模板：`RotateImageExpanded`（`Cv2.GetRotationMatrix2D` + `Cv2.WarpAffine`，自动扩展画布）+ `Cv2.Resize`
- mask 感知变换：对变换后的模板生成二值 mask，`BinarizeMask` 后传入 `Cv2.MatchTemplate`
- 响应图 NaN/Inf 清洗：`SanitizeMatchTemplateResult` 遍历替换为 -1
- ReScore 验证：粗分数 * 0.25 + 验证分数 * 0.75，验证分数通过 masked zero-mean correlation 计算
- 并行变换评估：`Parallel.ForEach(transforms, ...)` 并行处理所有 (angle, scale) 组合

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", ...)` / `TryGetInputImage(inputs, "Template", ...)`
2. `ToGray(src)` / `ToGray(templateMat)` -- 灰度转换
3. `ResolveReferenceOrigin(@operator, tmplGray.Size())` -- 解析 OriginMode
4. `HasSufficientSignal(tmplGray)` -- 检查模板纹理
5. `BuildPyramids(srcGray, tmplGray, numLevels)` -- 图像金字塔构建
6. 从最粗层到最细层：
   - `BuildAngleRange(...)` / `BuildScaleRange(...)` -- 角度/尺度候选
   - `MatchByTransforms(levelSrc, levelTmpl, currentAngles, currentScales, minScore, candidateLimit, origin)`
     - `Parallel.ForEach(transforms, ...)`
     - `TransformTemplate(tmplGray, angle, scale, ...)` -- 旋转 + 缩放
     - `Cv2.MatchTemplate(srcGray, transformedTemplate, matchResult, CCoeffNormed/CCorrNormed, transformedMask)`
     - `SanitizeMatchTemplateResult(matchResult)` -- NaN/Inf 清洗
     - `FindPeaksForTransform(...)` -- 峰值提取 + 亚像素精化
     - `ReScoreCandidates(...)` -- 粗分 + 验证分加权
   - `NonMaximumSuppression(levelMatches, 0.4f)` -- 层内 IoU NMS
   - `BuildRefinedAngles(...)` / `BuildRefinedScales(...)` -- 粗层指导下一层搜索范围
7. `NonMaximumSuppression(finalMatches, 0.5f).Take(maxMatches)` -- 最终 IoU NMS
8. `DrawMatchResult(resultImage, match)` -- 结果绘制

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `TemplatePath` | `file` | `""` | - | 模板图像文件路径。未提供 Template 输入时从文件加载 |
| `MinScore` | `double` | `0.7` | [0.1, 1.0] | 最小匹配分数阈值 |
| `MaxMatches` | `int` | `1` | [1, 50] | 最终最多输出的匹配数量 |
| `AngleStart` | `double` | `-30.0` | [-180.0, 180.0] | 搜索起始角度（度） |
| `AngleExtent` | `double` | `60.0` | [0.0, 360.0] | 搜索角度跨度（度），实际搜索范围 = [AngleStart, AngleStart + AngleExtent] |
| `AngleStep` | `double` | `1.0` | [0.1, 10.0] | 基础角度步长（度），粗层自动放大 |
| `ScaleMin` | `double` | `1.0` | [0.2, 3.0] | 最小缩放系数 |
| `ScaleMax` | `double` | `1.0` | [0.2, 3.0] | 最大缩放系数 |
| `ScaleStep` | `double` | `0.1` | [0.01, 1.0] | 基础缩放步长，粗层自动放大 |
| `NumLevels` | `int` | `3` | [1, 6] | 金字塔层数 |
| `OriginMode` | `enum` | `Center` | Center / TopLeft / Custom | 参考原点模式。Center 取模板中心，TopLeft 取左上角，Custom 使用 OriginX/OriginY |
| `OriginX` | `double` | `0.0` | - | Custom 模式下的参考原点 X |
| `OriginY` | `double` | `0.0` | - | Custom 模式下的参考原点 Y |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Search Image | `Image` | Yes | 搜索图像 |
| `Template` | Template Image | `Image` | No | 模板图像；未提供时可改用 TemplatePath |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Result Image | `Image` | 结果图，绘制每个匹配的矩形、分数和角度/尺度标注 |
| `Matches` | Matches | `Any` | 匹配结果列表；每项包含 X, Y, XSubpixel, YSubpixel, Angle, Scale, Score, CenterX, CenterY, ReferenceX, ReferenceY, Width, Height |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `IsMatch` | `Boolean` | 是否找到候选 |
| `Score` | `Double` | 最佳候选分数 |
| `MatchCount` | `Integer` | 最终输出数量 |
| `NumLevelsUsed` | `Integer` | 实际参与搜索的金字塔层数 |
| `OriginMode` | `String` | 当前使用的原点模式 |
| `Method` | `String` | 固定为 "RotationScaleTemplateSearch" |
| `FailureReason` | `String` | 匹配失败时的原因描述 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(L * A * S * (W-w)*(H-h)*w*h)，L 为金字塔层数，A 为角度数，S 为尺度数；粗层通过 BuildRefinedAngles/BuildRefinedScales 剪枝可大幅减少细层候选 |
| 典型耗时 (Typical Latency) | 视角度/尺度范围和金字塔层数而定；默认配置（AngleExtent=60, NumLevels=3）通常在 100ms 以内 |
| 内存特征 (Memory Profile) | 主要为金字塔 Mat 列表和变换模板副本；`Parallel.ForEach` 并行评估时峰值内存较高 |

## 适用场景 / Use Cases
- 适合 (Suitable)：需要旋转/尺度搜索的刚性模板匹配
- 适合 (Suitable)：同姿态多实例场景（MaxMatches > 1）
- 适合 (Suitable)：模板有明显纹理且目标无严重遮挡
- 不适合 (Not Suitable)：强遮挡、强非刚体形变或明显透视变化
- 不适合 (Not Suitable)：无纹理或纹理极弱的模板

## 已知限制 / Known Limitations
1. 这仍然是模板匹配路线，不适用于强遮挡、强非刚体形变或明显透视变化
2. 匹配核心固定使用 CCoeffNormed（无 mask）或 CCorrNormed（有 mask），没有暴露多种模板匹配方法
3. 同姿态多实例已支持，但在强重复纹理场景仍应结合 ROI、先验角度范围或更高阈值使用
4. 角度/尺度步长在粗层自动放大，极端参数组合下可能漏检

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档，补充金字塔 coarse-to-fine 机制、亚像素精化、ReScore 验证、mask 感知变换和并行评估说明 |
| 1.2.0 | 2026-04-12 | 支持同姿态多实例提峰，补充 MaxMatches 的实际行为与稳定排序说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码补充粗到细角度搜索、金字塔规则、NMS 与实际输出结构说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
