# 局部形变匹配 / Local Deformable Matching

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `LocalDeformableMatchingOperator` |
| 枚举值 (Enum) | `OperatorType.LocalDeformableMatching` |
| 分类 (Category) | Matching |
| 成熟度 (Maturity) | 实验性 Experimental |
| 当前版本 (Version) | `1.1.1` |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

该算子实现局部形变匹配，对标 Halcon `find_local_deformable_model`。核心思路是将模板匹配问题分解为"候选窗口生成 + 粗到细形变精化"两阶段。

第一阶段：候选窗口生成
- 使用 `Cv2.MatchTemplate(CCoeNormed)` 在搜索图上滑动模板，生成初始响应图
- 从响应图中持续提取峰值，以峰值为中心扩展候选窗口（模板尺寸 + padding + maxDeformation）
- 对已选窗口区域做局部抑制，避免重复候选

第二阶段：粗到细形变精化（对每个候选窗口）
- 构建搜索图和模板的图像金字塔
- 在每层用 ORB 特征提取和 BFMatcher KNN 匹配（Lowe ratio 0.75），得到特征对应点
- 用 `HomographyVerificationHelper` 估计初始刚性变换（单应性矩阵）
- 初始化均匀网格控制点（TPSGridSize x TPSGridSize）
- 迭代精化：计算特征对应 -> MLS 变形场估计 -> 应用变形 warp -> 计算匹配分数和遮挡掩膜 -> 收敛判断
- 最终验证：在全图尺度计算匹配分数、遮挡率、变形幅度

遮挡检测：
- 对变形后的模板图和搜索图做 `Absdiff`，阈值化得到遮挡掩膜
- 遮挡率 = 遮挡像素数 / 支撑区域像素数

刚性回退（`EnableFallback`）：
- 当形变匹配失败时，可选回退到纯刚性单应性匹配结果

多目标与 NMS：
- 支持同时输出多个匹配实例（`MaxMatches`）
- 通过 IoU NMS 去除重叠候选

This operator implements local deformable matching, benchmarked against Halcon `find_local_deformable_model`. It decomposes the problem into candidate window generation and coarse-to-fine deformable refinement. Each candidate undergoes pyramid ORB feature alignment, MLS deformation field estimation, occlusion verification, and convergence iteration. Multi-target output with IoU NMS is supported.

## 实现策略 / Implementation Strategy
- 候选窗口生成使用归一化互相关模板匹配，响应图局部 FloodFill 抑制
- 粗到细形变精化在图像金字塔上逐层执行，控制点逐层上采样
- 变形场估计使用 `LocalDeformableMovingLeastSquaresHelper`（MLS 相似性变形），参数名沿用 TPS 命名以保持兼容
- 特征匹配使用 ORB + BFMatcher KNN + Lowe ratio test，低于 4 匹配时回退到 crossCheck 模式
- 刚性变换估计使用 `HomographyVerificationHelper.TryEstimateAndVerify`
- 遮挡检测基于像素差绝对值阈值化（阈值 32.0）
- 模板缓存使用 LRU 策略，容量 10，基于文件 SHA256 指纹

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", ...)` / `TryGetInputImage(inputs, "Template", ...)`
2. `BuildTemplatePyramid(templateMat, pyramidLevels)` -- 模板金字塔 + ORB 特征提取
3. `GetOrLoadTemplate(templatePath, pyramidLevels)` -- LRU 模板缓存（SHA256 指纹）
4. `GenerateCandidateWindows(searchImage, template.BaseImage, candidateBudget, candidateThreshold, maxDeformation)` -- CCoeffNormed 滑动匹配 + 峰值提取 + 局部抑制
5. `EvaluateCandidates(searchImage, template, candidates, parallelCandidates, ...)` -- 并行/串行评估每个候选
6. 对每个候选：`PerformCoarseToFineMatching(localSearch, template, ...)`
   - `BuildImagePyramid(searchImage, pyramidLevels)`
   - 逐层：`MatchFeaturesAtLevel(...)` -> `ORB.DetectAndCompute` + `BFMatcher.KnnMatch`
   - `EstimateRigidTransform(...)` -> `HomographyVerificationHelper.TryEstimateAndVerify`
   - 迭代：`ComputeCorrespondences(...)` -> `EstimateTPSDeformation(...)` -> `ApplyTPSWarp(...)` -> `ComputeMatchScoreAndOcclusion(...)`
   - `ValidateFinalMatch(...)` -- 全图最终验证
7. `ApplyNms(acceptedMatches, nmsThreshold, maxMatches)` -- IoU NMS 去重
8. 结果输出：变形网格绘制、多目标标注

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `TemplatePath` | `file` | `""` | - | 模板图像文件路径。未提供 Template 输入时从文件加载 |
| `PyramidLevels` | `int` | `3` | [1, 6] | 图像金字塔层数，层数越多粗层搜索越快但可能漏检 |
| `TPSGridSize` | `int` | `4` | [2, 8] | 控制网格尺寸（N x N），网格越细变形精度越高但计算量越大 |
| `TPSLambda` | `double` | `0.01` | [0.001, 1.0] | MLS 平滑/正则化权重（沿用 TPS 参数名），值越大变形越平滑 |
| `MaxDeformation` | `double` | `20.0` | [5.0, 100.0] | 最大允许变形量（像素），超出的变形会被截断 |
| `OcclusionThreshold` | `double` | `0.3` | [0.1, 0.9] | 遮挡率阈值，超过此值的匹配会被拒绝 |
| `MinMatchScore` | `double` | `0.6` | [0.0, 1.0] | 最小匹配分数阈值 |
| `EnableFallback` | `bool` | `false` | - | 形变匹配失败时是否回退到刚性单应性匹配 |
| `MaxIterations` | `int` | `5` | [1, 20] | 每层最大迭代精化次数 |
| `ConvergenceThreshold` | `double` | `0.5` | [0.1, 5.0] | 收敛阈值（像素），相邻迭代误差变化低于此值时停止 |
| `MaxMatches` | `int` | `5` | [1, 20] | 最终最多输出的匹配数量 |
| `CandidateThreshold` | `double` | `0.65` | [0.1, 1.0] | 候选窗口种子阈值，CCoeffNormed 响应低于此值的窗口不进入精化 |
| `EnableNms` | `bool` | `true` | - | 是否启用 IoU NMS 去重 |
| `NmsThreshold` | `double` | `0.35` | [0.0, 1.0] | NMS IoU 阈值，IoU 超过此值的重叠候选被抑制 |
| `ParallelCandidates` | `bool` | `true` | - | 是否并行评估候选窗口 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Search Image | `Image` | Yes | 搜索图像 |
| `Template` | Template Image | `Image` | No | 模板图像；未提供时可改用 TemplatePath |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Result Image | `Image` | 结果图，绘制变形网格和多目标标注 |
| `MatchResult` | Match Result | `Any` | 最佳匹配详细结果，包含 Score, OcclusionRate, DeformationMagnitude, Corners, ControlPoints 等 |
| `Matches` | Match List | `Any` | 匹配列表，每项包含 Method, Score, OcclusionRate, DeformationMagnitude, BoundingBox, Corners |
| `MatchCount` | Match Count | `Integer` | 最终匹配数量 |
| `DeformationField` | Deformation Field | `Any` | 变形场，包含 ControlPoints 和 DeformedPoints 两组点集 |
| `OcclusionMask` | Occlusion Mask | `Image` | 遮挡掩膜，白色区域为检测到的遮挡 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(C*L*(F+M) + C*G*I*P)，C 为候选数，L 为金字塔层数，F 为特征提取，M 为匹配，G 为网格点数，I 为迭代次数，P 为像素数 |
| 典型耗时 (Typical Latency) | 无专用基准测试；受候选数、金字塔层数、网格尺寸和迭代次数影响显著 |
| 内存特征 (Memory Profile) | O(W*H + C*G + F)，W*H 为图像，C*G 为候选控制点，F 为特征描述子 |

## 适用场景 / Use Cases
- 适合 (Suitable)：有纹理的模板在局部形变、轻度遮挡或多实例场景下的匹配
- 适合 (Suitable)：需要变形场、遮挡掩膜和刚性回退诊断信息的工作流
- 不适合 (Not Suitable)：空白或低纹理模板，ORB 特征支持不足
- 不适合 (Not Suitable)：实时高吞吐匹配，除非严格约束候选数、金字塔层数和网格尺寸

## 已知限制 / Known Limitations
1. 变形场估计使用 MLS 风格变形，但参数名沿用 TPS 命名（TPSLambda、TPSGridSize）以保持向后兼容
2. 候选窗口生成仍从归一化模板匹配开始，强重复背景场景下可能需要 ROI 约束或更高阈值
3. ORB 特征对低纹理或模糊模板可能提取不足，导致匹配失败
4. 模板缓存容量为 10，大量不同模板时会频繁淘汰

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档，补充候选窗口生成、MLS 变形精化、遮挡检测、刚性回退和多目标 NMS 完整说明 |
| 1.1.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
