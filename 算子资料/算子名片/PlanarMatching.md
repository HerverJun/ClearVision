# 平面匹配 / Planar Matching

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PlanarMatchingOperator` |
| 枚举值 (Enum) | `OperatorType.PlanarMatching` |
| 分类 (Category) | Matching |
| 成熟度 (Maturity) | 稳定 Stable |
| 当前版本 (Version) | `1.1.2` |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

该算子实现基于特征的平面匹配，对标 Halcon `find_planar_uncalib_deformable_model`。核心思路是利用局部特征描述子匹配和单应性矩阵估计来检测平面目标。

特征匹配阶段：
- 从模板图像和搜索图像（或 ROI 区域）中提取二值局部特征（ORB / AKAZE / BRISK）
- 使用 BFMatcher 进行 KNN 匹配（k=2），应用 Lowe ratio test 过滤误匹配
- 双向匹配验证：前向匹配后检查反向匹配一致性（互匹配检验）

单应性验证阶段：
- 用 `HomographyVerificationHelper.TryEstimateAndVerify` 估计单应性矩阵
- RANSAC 内点筛选，几何一致性检查（面积比、角点内投影、中心投影等）
- 综合评分：`finalScore = candidateScore * 0.35 + verificationScore * 0.65`

多尺度搜索：
- 构建多个尺度候选（1.0, 1 +/- scaleRange/2, 1 +/- scaleRange）
- 对每个尺度缩放搜索图后重新匹配
- 选择最佳尺度的匹配结果

模板特征缓存：
- 基于文件 SHA256 指纹的 LRU 缓存（容量 20）
- 缓存键 = 路径 + 指纹 + 检测器类型 + MaxFeatures + ScaleFactor + NLevels

This operator implements feature-based planar matching, benchmarked against Halcon `find_planar_uncalib_deformable_model`. It extracts binary local features from template and search ROI, applies descriptor matching with Lowe ratio filtering, estimates and verifies homography with geometric checks, and performs multi-scale search for scale robustness. Template features are cached via LRU with SHA256 fingerprinting.

## 实现策略 / Implementation Strategy
- 特征检测器支持 ORB、AKAZE、BRISK 三种，ORB 为默认（最高效）
- ORB 使用 `ORB.Create(maxFeatures, scaleFactor, nLevels)` 构建，支持金字塔参数
- AKAZE 和 BRISK 使用默认参数，MaxFeatures 通过 response 排序后截断实现
- 描述子距离类型：ORB/AKAZE/BRISK 统一使用 `NormTypes.Hamming`
- 双向 BFMatcher KNN 匹配 + Lowe ratio test（前向 + 反向一致性）
- 候选评分 = 覆盖率 * 0.65 + 距离分数 * 0.35
- 单应性验证评分通过 `HomographyVerificationHelper.ComputeVerificationScore` 计算
- 多尺度搜索通过缩放搜索图（而非模板图）实现，角点坐标和单应性矩阵反向补偿

## 核心 API 调用链 / Core API Call Chain
1. `TryNormalizeDetectorType(detectorType, ...)` -- 校验检测器类型
2. `TryGetInputImage(inputs, "Image", ...)` / `TryGetInputImage(inputs, "Template", ...)`
3. `ExtractFeatures(templateMat, detectorType, maxFeatures, scaleFactor, nLevels)` -- 模板特征提取
4. `GetOrLoadTemplateFeatures(templatePath, ...)` -- LRU 模板特征缓存
5. `BuildScaleCandidates(enableMultiScale, scaleRange)` -- 多尺度候选构建
6. 对每个尺度：
   - 缩放搜索图（`Cv2.Resize`，1/scale）
   - `ExtractFeatures(searchImage, detectorType, ...)` -- 搜索图特征提取
   - `MatchFeatures(templateDescriptors, searchDescriptors, detectorType, matchRatio)` -- 双向 KNN 匹配
   - `CalculateCandidateScore(matches, ...)` -- 候选评分
   - `HomographyVerificationHelper.TryEstimateAndVerify(...)` -- RANSAC 单应性估计 + 几何验证
   - `HomographyVerificationHelper.ComputeVerificationScore(...)` -- 验证评分
7. 角点和中心坐标补偿（缩放 + ROI 偏移）
8. 结果绘制：四边形框 + 中心点 + 信息文本

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `TemplatePath` | `file` | `""` | - | 模板图像文件路径。未提供 Template 输入时从文件加载 |
| `DetectorType` | `enum` | `ORB` | ORB / AKAZE / BRISK | 特征检测器类型。ORB 最高效，AKAZE 对模糊更鲁棒，BRISK 为折中方案 |
| `MaxFeatures` | `int` | `1000` | [100, 5000] | 最大特征点数。ORB 直接传入构造器，AKAZE/BRISK 通过 response 排序截断 |
| `ScaleFactor` | `double` | `1.2` | [1.01, 2.0] | ORB 金字塔缩放因子（仅 ORB 生效） |
| `NLevels` | `int` | `8` | [1, 16] | ORB 金字塔层数（仅 ORB 生效） |
| `MatchRatio` | `double` | `0.75` | [0.5, 0.95] | Lowe ratio test 阈值，越小越严格 |
| `RansacThreshold` | `double` | `3.0` | [0.5, 10.0] | RANSAC 重投影误差阈值（像素） |
| `MinMatchCount` | `int` | `10` | [4, 100] | 最小特征匹配数，低于此值直接判定失败 |
| `MinInliers` | `int` | `8` | [4, 100] | 最小内点数 |
| `MinInlierRatio` | `double` | `0.25` | [0.1, 1.0] | 最小内点率（内点数/匹配数） |
| `ScoreThreshold` | `double` | `0.5` | [0.0, 1.0] | 最终匹配分数阈值 |
| `AllowCenterOnlyProjection` | `bool` | `false` | - | 允许仅中心点投影验证（放宽角点投影要求） |
| `UseRoi` | `bool` | `false` | - | 是否启用 ROI 搜索 |
| `RoiX` | `int` | `0` | - | ROI 左上角 X 坐标 |
| `RoiY` | `int` | `0` | - | ROI 左上角 Y 坐标 |
| `RoiWidth` | `int` | `0` | - | ROI 宽度 |
| `RoiHeight` | `int` | `0` | - | ROI 高度 |
| `EnableMultiScale` | `bool` | `true` | - | 是否启用多尺度搜索 |
| `ScaleRange` | `double` | `0.2` | [0.0, 1.0] | 多尺度搜索范围（+/-），生成 5 个尺度候选 |
| `EnableEarlyExit` | `bool` | `false` | - | 多尺度搜索时，找到满足阈值的匹配后提前退出 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Search Image | `Image` | Yes | 搜索图像 |
| `Template` | Template Image | `Image` | No | 模板图像；未提供时可改用 TemplatePath |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Result Image | `Image` | 结果图，绘制四边形检测框、中心点和匹配信息 |
| `IsMatch` | Is Match | `Boolean` | 是否存在满足阈值的匹配 |
| `Score` | Score | `Float` | 最终综合分数（candidateScore * 0.35 + verificationScore * 0.65） |
| `MatchCount` | Match Count | `Integer` | 匹配数量（匹配成功时为 1） |
| `Method` | Method | `String` | 匹配方法描述，如 FeatureHomography:ORB |
| `FailureReason` | Failure Reason | `String` | 失败原因描述 |
| `CandidateScore` | Candidate Score | `Float` | 特征匹配候选分数（覆盖率 + 距离分数加权） |
| `InlierCount` | Inlier Count | `Integer` | RANSAC 内点数 |
| `InlierRatio` | Inlier Ratio | `Float` | 内点率（内点数/匹配数） |
| `MeanReprojectionError` | Mean Reprojection Error | `Float` | 内点平均重投影误差（像素） |
| `MaxReprojectionError` | Max Reprojection Error | `Float` | 内点最大重投影误差（像素） |
| `AreaRatio` | Area Ratio | `Float` | 检测四边形面积与模板面积之比 |
| `CornersInsideCount` | Corners Inside Count | `Integer` | 落在搜索图边界内的角点数 |
| `ProjectedCenterInside` | Projected Center Inside | `Boolean` | 投影中心是否在搜索图内 |
| `HomographyFailureReason` | Homography Failure Reason | `String` | 单应性验证失败原因 |
| `VerificationPassed` | Verification Passed | `Boolean` | 单应性几何验证是否通过 |
| `MatchResult` | Match Result | `Any` | 匹配结果详情字典 |
| `Homography` | Homography Matrix | `Any` | 3x3 单应性矩阵 |
| `Corners` | Detected Corners | `PointList` | 检测到的四边形角点（4 个点） |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(S*(F log F + M + R*I))，S 为尺度数，F 为特征点数，M 为匹配数，R 为 RANSAC 迭代，I 为内点数 |
| 典型耗时 (Typical Latency) | 无专用基准测试；受检测器类型、特征数量和多尺度搜索影响 |
| 内存特征 (Memory Profile) | O(F + M + W*H)，F 为特征描述子，M 为匹配结果，W*H 为图像 |

## 适用场景 / Use Cases
- 适合 (Suitable)：有纹理的平面目标在透视变化下的检测
- 适合 (Suitable)：需要匹配分数、投影角点、内点指标和失败诊断信息的检测流程
- 不适合 (Not Suitable)：非平面、强形变或无纹理的目标
- 不适合 (Not Suitable)：局部特征高度重复的场景，除非严格约束 ROI、检测器和分数阈值

## 已知限制 / Known Limitations
1. 检测器仅支持 ORB、AKAZE 和 BRISK，不支持 SIFT 等浮点描述子
2. 多尺度搜索使用固定的 5 个尺度候选而非连续尺度空间优化
3. AKAZE 和 BRISK 的 ScaleFactor 和 NLevels 参数仅对 ORB 生效，其他检测器使用默认配置
4. 模板特征缓存容量为 20，大量不同模板/检测器组合时会频繁淘汰

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档，补充双向匹配验证、多尺度搜索、候选评分公式和所有输出端口说明 |
| 1.1.2 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
