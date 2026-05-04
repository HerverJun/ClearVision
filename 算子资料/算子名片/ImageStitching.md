# 图像拼接 / ImageStitching

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageStitchingOperator` |
| 枚举值 (Enum) | `OperatorType.ImageStitching` |
| 分类 (Category) | 图像处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | stitch, panorama, merge image |
| 图标 (Icon) | stitch |

## 算法原理 / Algorithm Principle
图像拼接将两幅有重叠区域的图像合成为一张更大的全景图。核心流程为：

**FeatureBased 模式（特征匹配拼接）**：
1. ORB 特征检测与描述：对两幅灰度图各提取最多 1200 个 ORB 关键点及 256 位描述子。
2. BF 暴力匹配 + Lowe's 比率检验：对描述子做 KNN-2 匹配（Hamming 距离），保留 `distance[0] < 0.75 * distance[1]` 的优质匹配对（至少 8 对）。
3. 单应性估计：用 `Cv2.FindHomography()` RANSAC 方法（阈值 3.0）从匹配点对估计 3x3 单应矩阵 H。
4. 透视变换：将两幅图分别通过 `Cv2.WarpPerspective()` 投影到统一坐标系。
5. 重叠区域混合：计算重叠区域的掩码后，用选定的混合策略生成最终结果。

**Manual 模式（手动水平拼接）**：
- 按 `OverlapPercent` 参数指定的重叠比例，将两幅图在水平方向上拼接，重叠区域使用选定混合策略。

**混合策略**：
- **Linear（线性渐变/羽化混合）**：基于距离变换计算每个像素到非零区域边界的距离作为权重，做加权平均。实现在重叠区域实现平滑过渡。
- **MultiBand（多频段/拉普拉斯金字塔混合）**：构建高斯金字塔和拉普拉斯金字塔，在每个频率层级上做加权混合后重建，避免低频鬼影和高频断裂。

> English: The operator stitches two overlapping images into a panorama. FeatureBased mode uses ORB descriptors + BFMatcher + RANSAC homography. Manual mode uses a fixed overlap percentage. Blending is either distance-based feather (Linear) or Laplacian pyramid multi-band.

## 实现策略 / Implementation Strategy
- 优先尝试 FeatureBased 特征匹配拼接；若特征点不足或匹配失败，自动降级为 Manual 水平拼接。
- ORB 特征点上限 1200，对工业场景中的低纹理图像可能不足，此时会触发降级。
- 单应矩阵通过 RANSAC 鲁棒估计，内点阈值 3 像素，可过滤外点匹配。
- 输出画布大小由两幅图经变换后的所有角点包围框自动确定。
- 重叠比率 `OverlapRatio` 通过 `BitwiseAnd(mask1, mask2)` 非零像素 / `BitwiseOr(mask1, mask2)` 非零像素计算。
- MultiBand 混合的金字塔层数由 `min(width, height)` 自动决定（最多 5 层，最小边长 >= 32 像素时增加一层）。

> English: Feature matching is attempted first; on failure, manual horizontal stitching is used. The output canvas is auto-sized from transformed corner bounds. Multi-band pyramid depth is auto-determined from image dimensions.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image1" / "Image2", out image)` -- 获取两幅输入图
2. `GetStringParam(@operator, "Method" / "BlendMode")` / `GetDoubleParam(@operator, "OverlapPercent")` -- 读取参数
3. **FeatureBased 路径**：
   - `Cv2.CvtColor(src, gray, BGR2GRAY)` -- 灰度转换
   - `ORB.Create(1200)` + `orb.DetectAndCompute()` -- ORB 特征提取
   - `BFMatcher(NormTypes.Hamming)` + `matcher.KnnMatch(desc2, desc1, 2)` -- KNN 匹配
   - Lowe's 比率检验（0.75 阈值）筛选优质匹配
   - `Cv2.FindHomography(srcPts, dstPts, Ransac, 3, mask)` -- RANSAC 单应性估计
   - `Cv2.PerspectiveTransform(corners, homography)` -- 角点投影确定画布范围
   - `Cv2.WarpPerspective(src, warped, homography, size)` -- 透视变换
   - `BlendWarpedImages(warped1, warped2, mask1, mask2, blendMode)` -- 混合
4. **Manual 路径**：
   - 计算重叠像素数 `overlap = min(W1,W2) * overlapPercent / 100`
   - 直接拷贝 + 重叠区域混合
5. **Linear 混合**：`Cv2.DistanceTransform()` 计算权重 -> 逐像素加权
6. **MultiBand 混合**：`BuildGaussianPyramid()` -> `BuildLaplacianPyramid()` -> 层级加权混合 -> 金字塔重建
7. `CreateImageOutput(stitched, output)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Method` | `enum` | `"FeatureBased"` | `FeatureBased` / `Manual` | 拼接方法。FeatureBased 自动特征匹配；Manual 按重叠比例水平拼接。 |
| `OverlapPercent` | `double` | `20.0` | [0.0, 90.0] | 手动模式下的重叠百分比。仅 Manual 方法生效。 |
| `BlendMode` | `enum` | `"Linear"` | `Linear` / `MultiBand` | 混合策略。Linear 为距离变换羽化混合（实现：FeatherDistanceBlend）；MultiBand 为拉普拉斯金字塔多频段混合（实现：LaplacianPyramidMultiBand）。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image1` | Image 1 | `Image` | Yes | 第一幅输入图像（左图/基准图）。 |
| `Image2` | Image 2 | `Image` | Yes | 第二幅输入图像（右图/待拼接图）。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 拼接后的全景图像。 |
| `OverlapRatio` | Overlap Ratio | `Float` | 实际重叠区域占比 [0.0, 1.0]。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | FeatureBased：ORB 提取 O(N) + 匹配 O(K1*K2) + 透视变换 O(W*H)。Manual：O(W*H) 拷贝 + 重叠区域混合。MultiBand 混合额外 O(W*H*log(levels))。 |
| 典型耗时 (Typical Latency) | 两幅 1920x1080 图像，FeatureBased + Linear 混合约 100-300ms；Manual 模式约 10-30ms。 |
| 内存特征 (Memory Profile) | FeatureBased 路径需分配两张变换后的大画布图 + 混合中间缓冲区。MultiBand 混合额外需要金字塔各层图像。峰值内存约为输入图像总面积的 3-5 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：工业视觉中宽幅面检测（如 PCB、面板、卷材），需将多视野图像拼接为全景。
- **适合 (Suitable)**：文档扫描、地图拼接等有明确重叠区域的场景。
- **适合 (Suitable)**：Manual 模式适合传送带匀速运动、重叠比例已知的场景。
- **不适合 (Not Suitable)**：两幅图无重叠区域或重叠区域极小（<8 个有效匹配点）。
- **不适合 (Not Suitable)**：低纹理、重复纹理场景（ORB 特征点不足，会降级为 Manual）。
- **不适合 (Not Suitable)**：大视角差异或非平面场景（单应性模型不适用，需要柱面/球面投影）。

## 已知限制 / Known Limitations
1. 仅支持两幅图拼接，不支持多图自动拼接流程。
2. ORB 特征点上限固定 1200，对大面积低纹理图像可能导致匹配不足。
3. 单应性假设场景为平面，对于深度差异大的三维场景会产生拼接伪影。
4. Manual 模式假设两幅图仅在水平方向有重叠，不支持任意方向偏移。
5. Linear 混合的羽化权重基于距离变换，对亮度差异大的图像可能产生明显过渡带。
6. MultiBand 混合的金字塔层数自动计算，不支持用户手动指定。
7. 不支持自动曝光补偿或色彩均衡，两幅图亮度差异大时接缝明显。
8. 输出画布大小由角点包围框确定，可能导致输出图像过大。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 金牌质量重写：补充完整算法原理（ORB+RANSAC 流程、两种混合策略数学描述）、实现策略、详细参数语义、性能分析与限制说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
