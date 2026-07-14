# 算子目录 / Operator Catalog

> 生成时间 / Generated At: `2026-07-15 01:10:44 +08:00`
> 算子总数 / Total Operators: **158**

## 分类统计 / Category Summary
| 分类 ID | 分类 (Category) | 数量 (Count) | 占比 (Ratio) |
|------|------|------:|------:|
| `Acquisition` | 采集 | 1 | 0.6% |
| `ImagePreprocessing` | 图像预处理 | 28 | 17.7% |
| `SegmentationAndRegion` | 分割与区域 | 17 | 10.8% |
| `FeatureExtraction` | 特征提取 | 13 | 8.2% |
| `MatchingAndLocalization` | 匹配与定位 | 17 | 10.8% |
| `DefectDetection` | 缺陷检测 | 4 | 2.5% |
| `Measurement` | 测量 | 17 | 10.8% |
| `CalibrationAndCoordinates` | 标定与坐标 | 12 | 7.6% |
| `AiInference` | AI推理 | 4 | 2.5% |
| `PointCloud3D` | 3D点云 | 6 | 3.8% |
| `DataProcessing` | 数据处理 | 18 | 11.4% |
| `FlowControl` | 流程控制 | 8 | 5.1% |
| `Communication` | 通信 | 8 | 5.1% |
| `OutputAndAuxiliary` | 输出与辅助 | 5 | 3.2% |

## 质量评分 / Quality Score
- 平均分 / Average: **95.4**
| 等级 (Level) | 数量 (Count) |
|------|------:|
| A | 153 |
| B | 5 |

## 分类索引 / Grouped Index

### 采集 / `Acquisition` (1)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ImageAcquisition` | 图像采集 | `Stable` | 2 | 1 | 6 | 100 (A) | `1.0.0` | 该算子用于从文件或相机采集图像。运行时从声明输入端口读取数据，按参数表解析配置，并把… | [ImageAcquisition](./operators/ImageAcquisition.md) |

### 图像预处理 / `ImagePreprocessing` (28)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AffineTransform` | 仿射变换 | `Stable` | 1 | 2 | 9 | 100 (A) | `1.0.0` | 该算子用于使用三点或旋转-缩放-平移模式执行二维仿射变换。运行时从声明输入端口读取数… | [AffineTransform](./operators/AffineTransform.md) |
| `OperatorType.BilateralFilter` | 双边滤波 | `Stable` | 1 | 1 | 3 | 100 (A) | `1.1.0` | 该算子用于边缘保留的平滑滤波。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [BilateralFilter](./operators/BilateralFilter.md) |
| `OperatorType.ClaheEnhancement` | CLAHE增强 | `Stable` | 1 | 1 | 5 | 94 (A) | `1.0.0` | 该算子用于使用自适应直方图均衡提升局部对比度，适合低对比或光照不均的图像。运行时从声… | [ClaheEnhancement](./operators/ClaheEnhancement.md) |
| `OperatorType.ColorConversion` | 颜色空间转换 | `Stable` | 1 | 1 | 2 | 94 (A) | `1.0.0` | 该算子用于BGR/GRAY/HSV/Lab/YUV等颜色空间转换。运行时从声明输入端… | [ColorConversion](./operators/ColorConversion.md) |
| `OperatorType.CopyMakeBorder` | 边界填充 | `Stable` | 1 | 1 | 6 | 94 (A) | `1.0.0` | 该算子用于使用 OpenCV 边界策略填充图像边缘。运行时从声明输入端口读取数据，按… | [CopyMakeBorder](./operators/CopyMakeBorder.md) |
| `OperatorType.FFT1D` | 信号/图像傅里叶变换（FFT） | `Stable` | 2 | 4 | 0 | 89 (A) | `1.0.0` | 该算子用于对一维数值信号执行 FFT；图像输入执行完整二维 DFT，并输出复数频谱、… | [FFT1D](./operators/FFT1D.md) |
| `OperatorType.Filtering` | 滤波 | `Stable` | 1 | 3 | 8 | 100 (A) | `1.2.0` | Unified spatial smoothing filters (OpenCV) | [Filtering](./operators/Filtering.md) |
| `OperatorType.FrameAveraging` | 帧平均 | `Stable` | 1 | 2 | 2 | 94 (A) | `1.0.0` | 该算子用于对多帧输入取平均以降低时域噪声。运行时从声明输入端口读取数据，按参数表解析… | [FrameAveraging](./operators/FrameAveraging.md) |
| `OperatorType.FrequencyFilter` | 频域滤波 | `Stable` | 5 | 3 | 0 | 81 (B) | `1.0.0` | 该算子用于对一维或二维复数频谱执行频域滤波，用于保留或抑制指定频率成分。运行时从声明… | [FrequencyFilter](./operators/FrequencyFilter.md) |
| `OperatorType.HistogramEqualization` | 直方图均衡化 | `Stable` | 1 | 1 | 4 | 94 (A) | `1.0.0` | 该算子用于支持全局直方图均衡与 CLAHE，用于增强图像对比度。运行时从声明输入端口… | [HistogramEqualization](./operators/HistogramEqualization.md) |
| `OperatorType.ImageAdd` | 图像加法 | `Stable` | 2 | 1 | 6 | 100 (A) | `1.0.0` | 该算子用于两幅图像叠加/合并。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [ImageAdd](./operators/ImageAdd.md) |
| `OperatorType.ImageBlend` | 图像融合 | `Stable` | 2 | 1 | 3 | 94 (A) | `1.0.0` | 该算子用于加权混合/透明叠加。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [ImageBlend](./operators/ImageBlend.md) |
| `OperatorType.ImageCompose` | 图像组合 | `Stable` | 4 | 1 | 3 | 94 (A) | `1.0.0` | 该算子用于通过拼接、网格或通道合并方式组合多张图像。运行时从声明输入端口读取数据，按… | [ImageCompose](./operators/ImageCompose.md) |
| `OperatorType.ImageCrop` | 图像裁剪 | `Stable` | 1 | 1 | 4 | 94 (A) | `1.0.0` | 该算子用于ROI区域提取。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结… | [ImageCrop](./operators/ImageCrop.md) |
| `OperatorType.ImageNormalize` | 图像归一化 | `Stable` | 1 | 6 | 4 | 100 (A) | `1.0.3` | MinMax range normalization / floating ZScore standardization / histogram equalization | [ImageNormalize](./operators/ImageNormalize.md) |
| `OperatorType.ImageResize` | 图像缩放 | `Stable` | 1 | 1 | 5 | 94 (A) | `1.0.0` | 该算子用于调整图像尺寸。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果… | [ImageResize](./operators/ImageResize.md) |
| `OperatorType.ImageRotate` | 图像旋转 | `Stable` | 1 | 1 | 5 | 94 (A) | `1.0.0` | 该算子用于任意角度旋转。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果… | [ImageRotate](./operators/ImageRotate.md) |
| `OperatorType.ImageStitching` | 图像拼接 | `Stable` | 2 | 2 | 3 | 94 (A) | `1.0.0` | 该算子用于将两张图像拼接成更大的全景式输出。运行时从声明输入端口读取数据，按参数表解… | [ImageStitching](./operators/ImageStitching.md) |
| `OperatorType.ImageSubtract` | 图像减法 | `Stable` | 2 | 4 | 1 | 89 (A) | `1.0.0` | 该算子用于计算两张图像的相减结果或绝对差值。运行时从声明输入端口读取数据，按参数表解… | [ImageSubtract](./operators/ImageSubtract.md) |
| `OperatorType.ImageTiling` | 图像切片 | `Stable` | 1 | 3 | 4 | 94 (A) | `1.0.0` | 该算子用于将图像切分为可选重叠的分块区域。运行时从声明输入端口读取数据，按参数表解析… | [ImageTiling](./operators/ImageTiling.md) |
| `OperatorType.InverseFFT1D` | 信号/图像逆傅里叶变换（IFFT） | `Stable` | 2 | 4 | 0 | 89 (A) | `1.0.0` | 该算子用于对一维复数频谱执行逆 FFT；二维复数频谱输入执行逆 DFT 并重建图像信… | [InverseFFT1D](./operators/InverseFFT1D.md) |
| `OperatorType.LaplacianSharpen` | 拉普拉斯锐化 | `Stable` | 1 | 6 | 3 | 96 (A) | `1.0.3` | Signed Laplacian sharpening | [LaplacianSharpen](./operators/LaplacianSharpen.md) |
| `OperatorType.MeanFilter` | 均值滤波 | `Stable` | 1 | 1 | 2 | 100 (A) | `1.1.0` | 该算子用于使用均值（方框）滤波平滑图像噪声。运行时从声明输入端口读取数据，按参数表解… | [MeanFilter](./operators/MeanFilter.md) |
| `OperatorType.MedianBlur` | 中值滤波 | `Stable` | 1 | 1 | 1 | 100 (A) | `1.1.0` | 该算子用于有效去除椒盐噪声同时保留边缘。运行时从声明输入端口读取数据，按参数表解析配… | [MedianBlur](./operators/MedianBlur.md) |
| `OperatorType.PerspectiveTransform` | 透视变换 | `Stable` | 3 | 1 | 20 | 100 (A) | `1.0.0` | 该算子用于四边形透视校正。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结… | [PerspectiveTransform](./operators/PerspectiveTransform.md) |
| `OperatorType.PolarUnwrap` | 极坐标展开 | `Stable` | 2 | 1 | 8 | 100 (A) | `1.0.0` | 该算子用于将环形图像区域展开为矩形视图。运行时从声明输入端口读取数据，按参数表解析配… | [PolarUnwrap](./operators/PolarUnwrap.md) |
| `OperatorType.RoiManager` | ROI裁剪与掩膜 | `Stable` | 1 | 3 | 10 | 100 (A) | `1.0.0` | 该算子用于按矩形、圆形或多边形 ROI 裁剪图像或应用掩膜，并输出空间上下文。运行时… | [RoiManager](./operators/RoiManager.md) |
| `OperatorType.ShadingCorrection` | 光照校正 | `Stable` | 2 | 1 | 3 | 96 (A) | `1.0.0` | 该算子用于通过背景法或模型法校正光照不均。运行时从声明输入端口读取数据，按参数表解析… | [ShadingCorrection](./operators/ShadingCorrection.md) |

### 分割与区域 / `SegmentationAndRegion` (17)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AdaptiveThreshold` | 自适应阈值 | `Stable` | 1 | 1 | 5 | 94 (A) | `1.0.0` | 该算子用于根据局部均值或高斯加权均值进行自适应阈值分割，适合光照不均场景。运行时从声… | [AdaptiveThreshold](./operators/AdaptiveThreshold.md) |
| `OperatorType.BinaryImageToRegion` | 二值图转区域 | `Stable` | 1 | 3 | 3 | 76 (B) | `1.1.0` | 该算子用于将二值图、掩膜或灰度阈值结果转换为像素区域 Region，供区域形态学和区… | [BinaryImageToRegion](./operators/BinaryImageToRegion.md) |
| `OperatorType.BlobAnalysis` | Blob分析 | `Stable` | 2 | 4 | 17 | 100 (A) | `1.2.1` | 该算子用于连通区域分析。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果… | [BlobAnalysis](./operators/BlobAnalysis.md) |
| `OperatorType.DistanceTransform` | 距离变换 | `Stable` | 1 | 4 | 7 | 100 (A) | `1.0.1` | OpenCV binary distance transform | [DistanceTransform](./operators/DistanceTransform.md) |
| `OperatorType.MorphologicalOperation` | 形态学操作 | `Stable` | 1 | 1 | 7 | 94 (A) | `1.0.0` | 该算子用于支持腐蚀、膨胀、开运算、闭运算、梯度、顶帽、黑帽等形态学操作。运行时从声明… | [MorphologicalOperation](./operators/MorphologicalOperation.md) |
| `OperatorType.Morphology` | 形态学（旧版） | `Legacy` | 1 | 1 | 6 | 94 (A) | `1.0.0` | 该算子用于旧版图像形态学节点；新建图像流程请使用“形态学操作”，区域流程请使用 Re… | [Morphology](./operators/Morphology.md) |
| `OperatorType.RectangleRegion` | 矩形框定义 | `Stable` | 0 | 1 | 4 | 84 (B) | `1.0.1` | 该算子用于根据 X、Y、宽度和高度参数生成 Rectangle 矩形框，供需要 Re… | [RectangleRegion](./operators/RectangleRegion.md) |
| `OperatorType.RegionClosing` | 区域闭运算 | `Stable` | 2 | 3 | 3 | 90 (A) | `1.0.2` | Region morphology closing | [RegionClosing](./operators/RegionClosing.md) |
| `OperatorType.RegionComplement` | 区域补集 | `Stable` | 4 | 3 | 0 | 85 (A) | `1.0.2` | Bounded run-length complement | [RegionComplement](./operators/RegionComplement.md) |
| `OperatorType.RegionDifference` | 区域差集 | `Stable` | 2 | 3 | 0 | 89 (A) | `1.0.2` | Run-length row subtraction | [RegionDifference](./operators/RegionDifference.md) |
| `OperatorType.RegionDilation` | 区域膨胀 | `Stable` | 2 | 3 | 4 | 90 (A) | `1.0.2` | Region morphology dilation | [RegionDilation](./operators/RegionDilation.md) |
| `OperatorType.RegionErosion` | 区域腐蚀 | `Stable` | 2 | 3 | 4 | 90 (A) | `1.0.2` | Region morphology erosion | [RegionErosion](./operators/RegionErosion.md) |
| `OperatorType.RegionIntersection` | 区域交集 | `Stable` | 2 | 3 | 0 | 89 (A) | `1.0.2` | Run-length row intersection | [RegionIntersection](./operators/RegionIntersection.md) |
| `OperatorType.RegionOpening` | 区域开运算 | `Stable` | 2 | 3 | 3 | 90 (A) | `1.0.2` | Region morphology opening | [RegionOpening](./operators/RegionOpening.md) |
| `OperatorType.RegionSkeleton` | 区域骨架化 | `Stable` | 2 | 5 | 2 | 90 (A) | `1.0.2` | Zhang-Suen thinning | [RegionSkeleton](./operators/RegionSkeleton.md) |
| `OperatorType.RegionUnion` | 区域并集 | `Stable` | 2 | 3 | 0 | 89 (A) | `1.0.2` | Run-length region union | [RegionUnion](./operators/RegionUnion.md) |
| `OperatorType.Thresholding` | 全局阈值处理 | `Stable` | 1 | 1 | 4 | 96 (A) | `1.1.0` | 该算子用于执行全局阈值处理，支持二值、反二值、截断、ToZero 以及 Otsu/T… | [Thresholding](./operators/Thresholding.md) |

### 特征提取 / `FeatureExtraction` (13)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.BlobLabeling` | Blob分类标注 | `Stable` | 2 | 3 | 3 | 100 (A) | `1.0.1` | 该算子用于按面积、圆度、宽高比或位置对 Blob/连通区域分类，输出标签列表并可绘制… | [BlobLabeling](./operators/BlobLabeling.md) |
| `OperatorType.CodeRecognition` | 条码识别 | `Stable` | 1 | 4 | 2 | 100 (A) | `1.0.0` | 该算子用于一维码/二维码识别，支持 QR、Code128、DataMatrix 等多… | [CodeRecognition](./operators/CodeRecognition.md) |
| `OperatorType.ColorDetection` | 颜色分析 | `Experimental` | 2 | 10 | 18 | 96 (A) | `2.0.1` | 该算子用于对图像执行平均色、主色和范围分析，并支持 HSV 区间检查与 Lab De… | [ColorDetection](./operators/ColorDetection.md) |
| `OperatorType.ContourDetection` | 轮廓检测 | `Stable` | 1 | 3 | 11 | 94 (A) | `1.0.0` | 该算子用于查找图像轮廓，提取边缘点集和层次关系，供后续测量和拟合使用。运行时从声明输… | [ContourDetection](./operators/ContourDetection.md) |
| `OperatorType.CornerDetection` | 角点检测 | `Stable` | 1 | 3 | 5 | 94 (A) | `1.0.0` | 该算子用于使用 Harris 或 Shi-Tomasi 检测角点。运行时从声明输入端… | [CornerDetection](./operators/CornerDetection.md) |
| `OperatorType.EdgeDetection` | 边缘检测 | `Stable` | 1 | 2 | 14 | 100 (A) | `1.0.0` | 该算子用于使用 Canny 进行边缘检测，并可选自动阈值。运行时从声明输入端口读取数… | [EdgeDetection](./operators/EdgeDetection.md) |
| `OperatorType.GlcmTexture` | GLCM纹理特征 | `Stable` | 1 | 6 | 9 | 100 (A) | `1.0.1` | Quantized gray-level co-occurrence matrix | [GlcmTexture](./operators/GlcmTexture.md) |
| `OperatorType.HistogramAnalysis` | 直方图分析 | `Stable` | 1 | 11 | 6 | 96 (A) | `1.1.0` | 该算子用于统计指定通道的直方图及灰度/强度分布指标。运行时从声明输入端口读取数据，按… | [HistogramAnalysis](./operators/HistogramAnalysis.md) |
| `OperatorType.ImageDiff` | 图像差异率分析 | `Stable` | 2 | 2 | 0 | 89 (A) | `1.0.1` | 该算子用于计算两幅同尺寸图像的绝对差异图，并输出非零差异像素占比。运行时从声明输入端… | [ImageDiff](./operators/ImageDiff.md) |
| `OperatorType.LawsTextureFilter` | Laws纹理滤波 | `Stable` | 1 | 3 | 5 | 100 (A) | `1.0.1` | Laws 5x5 texture energy filtering | [LawsTextureFilter](./operators/LawsTextureFilter.md) |
| `OperatorType.PixelStatistics` | 像素统计 | `Stable` | 2 | 6 | 5 | 96 (A) | `1.0.0` | 该算子用于计算 ROI 或掩码区域内的像素级统计信息。运行时从声明输入端口读取数据，… | [PixelStatistics](./operators/PixelStatistics.md) |
| `OperatorType.SharpnessEvaluation` | 清晰度评估 | `Stable` | 1 | 3 | 8 | 96 (A) | `1.1.0` | 该算子用于评估图像的对焦清晰度。运行时从声明输入端口读取数据，按参数表解析配置，并把… | [SharpnessEvaluation](./operators/SharpnessEvaluation.md) |
| `OperatorType.SubpixelEdgeDetection` | 亚像素边缘 | `Reference` | 1 | 2 | 5 | 94 (A) | `1.0.0` | 该算子用于非工业定型的亚像素边缘参考实现；用于计量前必须完成应用级验证。运行时从声明… | [SubpixelEdgeDetection](./operators/SubpixelEdgeDetection.md) |

### 匹配与定位 / `MatchingAndLocalization` (17)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AkazeFeatureMatch` | AKAZE特征匹配 | `Stable` | 2 | 13 | 14 | 90 (A) | `1.0.0` | AKAZE Homography Feature Match | [AkazeFeatureMatch](./operators/AkazeFeatureMatch.md) |
| `OperatorType.ContourExtrema` | 轮廓极值 | `Stable` | 3 | 6 | 0 | 89 (A) | `1.0.1` | Directional contour extrema scan | [ContourExtrema](./operators/ContourExtrema.md) |
| `OperatorType.EdgeIntersection` | 边线交点 | `Stable` | 2 | 4 | 1 | 96 (A) | `1.0.0` | 该算子用于计算两条直线的交点和夹角。运行时从声明输入端口读取数据，按参数表解析配置，… | [EdgeIntersection](./operators/EdgeIntersection.md) |
| `OperatorType.GradientShapeMatch` | 梯度形状匹配 | `Stable` | 2 | 6 | 12 | 100 (A) | `1.1.0` | Gradient Direction Template Match | [GradientShapeMatch](./operators/GradientShapeMatch.md) |
| `OperatorType.LocalDeformableMatching` | 局部可变形匹配 | `Experimental` | 2 | 6 | 15 | 100 (A) | `1.1.1` | Coarse-to-fine local deformable matching | [LocalDeformableMatching](./operators/LocalDeformableMatching.md) |
| `OperatorType.OrbFeatureMatch` | ORB特征匹配 | `Stable` | 2 | 13 | 17 | 90 (A) | `1.0.0` | ORB Homography Feature Match | [OrbFeatureMatch](./operators/OrbFeatureMatch.md) |
| `OperatorType.ParallelLineFind` | 平行线查找 | `Stable` | 1 | 6 | 4 | 94 (A) | `1.0.0` | 该算子用于在图像中查找最佳近似平行线对。运行时从声明输入端口读取数据，按参数表解析配… | [ParallelLineFind](./operators/ParallelLineFind.md) |
| `OperatorType.PlanarMatching` | 平面特征匹配 | `Stable` | 2 | 19 | 20 | 100 (A) | `1.1.3` | Feature homography planar matching | [PlanarMatching](./operators/PlanarMatching.md) |
| `OperatorType.PointAlignment` | 点位偏差计算 | `Stable` | 2 | 3 | 2 | 96 (A) | `1.0.4` | 该算子用于计算当前点相对参考点的 X/Y 偏差与距离；属于像素空间偏差工具，按 Pi… | [PointAlignment](./operators/PointAlignment.md) |
| `OperatorType.PointCorrection` | 点位刚性补偿 | `Stable` | 4 | 5 | 4 | 96 (A) | `1.0.4` | 该算子用于根据检测点/角度与参考点/角度计算像素空间二维刚性补偿量和变换矩阵；按 P… | [PointCorrection](./operators/PointCorrection.md) |
| `OperatorType.PositionCorrection` | ROI位姿补偿（像素） | `Stable` | 4 | 10 | 3 | 94 (A) | `1.0.3` | 该算子用于根据参考点与基准点的像素偏差，对 ROI 坐标执行平移或平移旋转补偿并输出… | [PositionCorrection](./operators/PositionCorrection.md) |
| `OperatorType.PyramidShapeMatch` | 金字塔形状匹配 | `Stable` | 2 | 5 | 15 | 100 (A) | `1.0.0` | LINEMOD Pyramid Shape Matching | [PyramidShapeMatch](./operators/PyramidShapeMatch.md) |
| `OperatorType.QuadrilateralFind` | 四边形查找 | `Stable` | 1 | 6 | 4 | 94 (A) | `1.0.0` | 该算子用于查找不受直角约束的四边形轮廓。运行时从声明输入端口读取数据，按参数表解析配… | [QuadrilateralFind](./operators/QuadrilateralFind.md) |
| `OperatorType.RectangleDetection` | 矩形检测 | `Stable` | 1 | 10 | 4 | 94 (A) | `1.0.0` | 该算子用于根据轮廓检测矩形/四边形目标。运行时从声明输入端口读取数据，按参数表解析配… | [RectangleDetection](./operators/RectangleDetection.md) |
| `OperatorType.RoiTransform` | ROI位姿变换 | `Stable` | 2 | 1 | 1 | 96 (A) | `1.0.2` | Pose-driven ROI rectangle transform | [RoiTransform](./operators/RoiTransform.md) |
| `OperatorType.ShapeMatching` | 旋转尺度模板匹配 | `Stable` | 2 | 2 | 13 | 100 (A) | `1.2.0` | 该算子用于基于金字塔粗到细搜索的旋转/尺度模板匹配；不是通用轮廓描述子匹配。运行时从… | [ShapeMatching](./operators/ShapeMatching.md) |
| `OperatorType.TemplateMatching` | 模板匹配 | `Stable` | 3 | 13 | 20 | 96 (A) | `1.2.0` | 该算子用于执行经典模板匹配，可限制旋转和尺度搜索范围；多目标结果通过基于 IoU 的… | [TemplateMatching](./operators/TemplateMatching.md) |

### 缺陷检测 / `DefectDetection` (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.DetectionSequenceJudge` | 检测顺序判定 | `Experimental` | 4 | 13 | 13 | 100 (A) | `1.0.1` | 该算子用于对检测结果排序，并与期望标签序列进行比对。运行时从声明输入端口读取数据，按… | [DetectionSequenceJudge](./operators/DetectionSequenceJudge.md) |
| `OperatorType.DualModalVoting` | 双模态投票 | `Stable` | 2 | 3 | 6 | 84 (B) | `1.0.0` | 该算子用于融合深度学习与传统视觉检测结果，输出最终判定。运行时从声明输入端口读取数据… | [DualModalVoting](./operators/DualModalVoting.md) |
| `OperatorType.EdgePairDefect` | 边缘间距缺陷检测 | `Stable` | 3 | 4 | 4 | 96 (A) | `1.0.1` | 该算子用于沿边缘对采样间距，按期望宽度与容差判定偏差点并输出缺陷数量和最大偏差。运行… | [EdgePairDefect](./operators/EdgePairDefect.md) |
| `OperatorType.SurfaceDefectDetection` | 表面缺陷检测 | `Experimental` | 2 | 8 | 24 | 100 (A) | `2.0.1` | 该算子用于使用梯度、配准后的参考差分或局部对比度检测表面缺陷。运行时从声明输入端口读… | [SurfaceDefectDetection](./operators/SurfaceDefectDetection.md) |

### 测量 / `Measurement` (17)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AngleMeasurement` | 角度测量 | `Stable` | 6 | 3 | 7 | 96 (A) | `1.0.0` | 该算子用于通过三点或两线计算角度，兼容亚像素级输入。运行时从声明输入端口读取数据，按… | [AngleMeasurement](./operators/AngleMeasurement.md) |
| `OperatorType.ArcCaliper` | 圆弧卡尺 | `Stable` | 7 | 2 | 0 | 81 (B) | `1.0.1` | Radial band-profile arc edge scan | [ArcCaliper](./operators/ArcCaliper.md) |
| `OperatorType.CaliperTool` | 卡尺工具 | `Stable` | 2 | 7 | 9 | 96 (A) | `1.0.0` | 该算子用于沿扫描线检测边缘对并输出宽度。运行时从声明输入端口读取数据，按参数表解析配… | [CaliperTool](./operators/CaliperTool.md) |
| `OperatorType.CircleMeasurement` | 圆测量 | `Stable` | 1 | 13 | 25 | 100 (A) | `1.1.2` | 该算子用于霍夫变换检测圆形并测量半径与圆心坐标，适用于孔径检测和圆形定位。运行时从声… | [CircleMeasurement](./operators/CircleMeasurement.md) |
| `OperatorType.ColorMeasurement` | 颜色测量 | `Stable` | 2 | 8 | 9 | 96 (A) | `2.0.0` | 该算子用于在选定 ROI 内统计 Lab 色差或 HSV 颜色特征。运行时从声明输入… | [ColorMeasurement](./operators/ColorMeasurement.md) |
| `OperatorType.ContourMeasurement` | 轮廓测量 | `Stable` | 1 | 4 | 4 | 94 (A) | `1.0.0` | 该算子用于计算轮廓面积、周长和质心，并支持按灰度权重估算面积。运行时从声明输入端口读… | [ContourMeasurement](./operators/ContourMeasurement.md) |
| `OperatorType.GapMeasurement` | 间隙测量 | `Stable` | 2 | 9 | 8 | 96 (A) | `1.0.0` | 该算子用于通过点或图像投影方式测量间距。运行时从声明输入端口读取数据，按参数表解析配… | [GapMeasurement](./operators/GapMeasurement.md) |
| `OperatorType.GeoMeasurement` | 几何测量 | `Stable` | 2 | 5 | 3 | 96 (A) | `1.0.0` | 该算子用于对点、线、圆等几何元素进行通用几何测量。运行时从声明输入端口读取数据，按参… | [GeoMeasurement](./operators/GeoMeasurement.md) |
| `OperatorType.GeometricFitting` | 几何拟合 | `Stable` | 1 | 2 | 8 | 100 (A) | `1.0.0` | 该算子用于根据轮廓点拟合直线、圆或椭圆。运行时从声明输入端口读取数据，按参数表解析配… | [GeometricFitting](./operators/GeometricFitting.md) |
| `OperatorType.GeometricTolerance` | 二维几何公差判定 | `Stable` | 5 | 7 | 5 | 96 (A) | `1.0.1` | 该算子用于基于特征与基准评估平行度、垂直度、位置度、同心度等受限二维公差带并输出判定… | [GeometricTolerance](./operators/GeometricTolerance.md) |
| `OperatorType.LineLineDistance` | 线线距离 | `Stable` | 2 | 5 | 3 | 96 (A) | `1.0.0` | 该算子用于计算两条直线或线段之间的距离与夹角。运行时从声明输入端口读取数据，按参数表… | [LineLineDistance](./operators/LineLineDistance.md) |
| `OperatorType.LineMeasurement` | 直线测量 | `Stable` | 1 | 5 | 4 | 96 (A) | `1.0.0` | 该算子用于检测直线特征，输出方向、跨度和拟合质量诊断。运行时从声明输入端口读取数据，… | [LineMeasurement](./operators/LineMeasurement.md) |
| `OperatorType.Measurement` | 测量 | `Stable` | 6 | 17 | 8 | 96 (A) | `1.1.0` | 该算子用于统一基础二维几何测量入口，支持点点距离、点线距离、线线距离/夹角和三点角度… | [Measurement](./operators/Measurement.md) |
| `OperatorType.MinEnclosingGeometry` | 最小外接几何体 | `Stable` | 1 | 2 | 10 | 100 (A) | `1.0.1` | Contour-derived enclosing geometry and robust fitting | [MinEnclosingGeometry](./operators/MinEnclosingGeometry.md) |
| `OperatorType.PhaseClosure` | 相位解缠绕 | `Stable` | 4 | 4 | 0 | 89 (A) | `1.0.1` | Itoh/quality-guided phase unwrapping | [PhaseClosure](./operators/PhaseClosure.md) |
| `OperatorType.PointLineDistance` | 点线距离 | `Stable` | 2 | 2 | 2 | 96 (A) | `1.0.0` | 该算子用于计算点到直线或线段的最短距离。运行时从声明输入端口读取数据，按参数表解析配… | [PointLineDistance](./operators/PointLineDistance.md) |
| `OperatorType.WidthMeasurement` | 宽度测量 | `Stable` | 3 | 8 | 8 | 96 (A) | `1.0.0` | 该算子用于测量平行边缘或直线之间的宽度。运行时从声明输入端口读取数据，按参数表解析配… | [WidthMeasurement](./operators/WidthMeasurement.md) |

### 标定与坐标 / `CalibrationAndCoordinates` (12)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.CalibrationLoader` | 标定加载 | `Stable` | 0 | 3 | 1 | 100 (A) | `1.0.0` | 该算子用于从 CalibrationBundleV2 JSON 文件加载标定数据并输… | [CalibrationLoader](./operators/CalibrationLoader.md) |
| `OperatorType.CameraCalibration` | 相机标定 | `Stable` | 1 | 2 | 7 | 100 (A) | `1.0.0` | 该算子用于根据棋盘格或圆点阵图像标定相机内参。运行时从声明输入端口读取数据，按参数表… | [CameraCalibration](./operators/CameraCalibration.md) |
| `OperatorType.CoordinateTransform` | 像素到物理坐标（单点） | `Stable` | 4 | 3 | 2 | 90 (A) | `1.0.0` | 该算子用于使用 CalibrationBundleV2 的二维标定变换，将单个像素坐… | [CoordinateTransform](./operators/CoordinateTransform.md) |
| `OperatorType.FisheyeCalibration` | 鱼眼标定 | `Stable` | 1 | 2 | 9 | 100 (A) | `1.0.0` | 该算子用于使用棋盘格或圆点阵图案标定鱼眼相机内参和畸变参数。运行时从声明输入端口读取… | [FisheyeCalibration](./operators/FisheyeCalibration.md) |
| `OperatorType.FisheyeUndistort` | 鱼眼去畸变 | `Stable` | 2 | 2 | 4 | 96 (A) | `1.0.0` | 该算子用于使用标定数据校正鱼眼镜头畸变，并支持 LUT 加速。运行时从声明输入端口读… | [FisheyeUndistort](./operators/FisheyeUndistort.md) |
| `OperatorType.HandEyeCalibration` | 手眼标定 | `Stable` | 2 | 7 | 4 | 100 (A) | `1.0.0` | OpenCV Hand-Eye Calibration | [HandEyeCalibration](./operators/HandEyeCalibration.md) |
| `OperatorType.HandEyeCalibrationValidator` | 手眼标定验证 | `Stable` | 3 | 8 | 1 | 100 (A) | `1.0.1` | Hand-Eye Consistency Validation | [HandEyeCalibrationValidator](./operators/HandEyeCalibrationValidator.md) |
| `OperatorType.NPointCalibration` | N点标定 | `Stable` | 1 | 9 | 10 | 100 (A) | `1.0.0` | 该算子用于基于全部点对鲁棒估计仿射或单应性标定模型。运行时从声明输入端口读取数据，按… | [NPointCalibration](./operators/NPointCalibration.md) |
| `OperatorType.PixelToWorldTransform` | 像素世界映射 | `Stable` | 3 | 3 | 11 | 100 (A) | `1.0.1` | 该算子用于通过 CalibrationBundleV2 执行坐标转换，可使用 Tra… | [PixelToWorldTransform](./operators/PixelToWorldTransform.md) |
| `OperatorType.StereoCalibration` | 双目标定 | `Stable` | 2 | 6 | 11 | 100 (A) | `1.0.0` | 该算子用于标定双目相机并生成极线校正映射。运行时从声明输入端口读取数据，按参数表解析… | [StereoCalibration](./operators/StereoCalibration.md) |
| `OperatorType.TranslationRotationCalibration` | 平移旋转标定 | `Stable` | 1 | 3 | 3 | 100 (A) | `1.0.0` | 该算子用于从图像-机器人点对鲁棒拟合二维刚性或相似变换。运行时从声明输入端口读取数据… | [TranslationRotationCalibration](./operators/TranslationRotationCalibration.md) |
| `OperatorType.Undistort` | 畸变校正 | `Stable` | 2 | 1 | 0 | 91 (A) | `1.0.0` | 该算子用于使用标定数据校正镜头畸变。运行时从声明输入端口读取数据，按参数表解析配置，… | [Undistort](./operators/Undistort.md) |

### AI推理 / `AiInference` (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AnomalyDetection` | 异常检测 | `Experimental` | 2 | 8 | 16 | 100 (A) | `1.0.0` | Simplified PatchCore | [AnomalyDetection](./operators/AnomalyDetection.md) |
| `OperatorType.DeepLearning` | 深度学习 | `Stable` | 1 | 31 | 27 | 100 (A) | `1.1.0` | 该算子用于统一 ONNX 深度学习推理入口，支持目标检测、图像分类和语义分割；默认保… | [DeepLearning](./operators/DeepLearning.md) |
| `OperatorType.OcrRecognition` | OCR 识别 | `Stable` | 1 | 2 | 0 | 100 (A) | `1.0.0` | 该算子用于识别图像中的文本内容。运行时从声明输入端口读取数据，按参数表解析配置，并把… | [OcrRecognition](./operators/OcrRecognition.md) |
| `OperatorType.SemanticSegmentation` | 语义分割 | `Stable` | 1 | 12 | 12 | 100 (A) | `1.0.0` | 该算子用于运行 ONNX 语义分割模型，输出类别图、着色可视化结果和各类别掩码。运行… | [SemanticSegmentation](./operators/SemanticSegmentation.md) |

### 3D点云 / `PointCloud3D` (6)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.EuclideanClusterExtraction` | 欧氏聚类分割 | `Stable` | 1 | 3 | 3 | 100 (A) | `1.0.0` | 该算子用于对点云执行欧式聚类提取（按距离划分三维连通分量）。运行时从声明输入端口读取… | [EuclideanClusterExtraction](./operators/EuclideanClusterExtraction.md) |
| `OperatorType.PPFEstimation` | PPF点对特征 | `Stable` | 1 | 3 | 3 | 95 (A) | `1.0.0` | 该算子用于为点云计算点对特征（PPF）并构建逐点特征图。运行时从声明输入端口读取数据… | [PPFEstimation](./operators/PPFEstimation.md) |
| `OperatorType.PPFMatch` | PPF点云粗匹配 | `Stable` | 2 | 16 | 10 | 95 (A) | `1.0.5` | 该算子用于基于 PPF 对模型点云与场景点云执行三维粗匹配，输出候选位姿、内点与稳定… | [PPFMatch](./operators/PPFMatch.md) |
| `OperatorType.RansacPlaneSegmentation` | RANSAC平面分割 | `Stable` | 1 | 8 | 4 | 95 (A) | `1.0.0` | 该算子用于对点云执行 RANSAC 平面分割，输出平面系数和内点。运行时从声明输入端… | [RansacPlaneSegmentation](./operators/RansacPlaneSegmentation.md) |
| `OperatorType.StatisticalOutlierRemoval` | 点云统计离群点去除（SOR） | `Stable` | 1 | 3 | 2 | 95 (A) | `1.0.1` | 该算子用于对点云执行统计离群点去除（SOR），输出过滤点云、保留点数和移除点数。运行… | [StatisticalOutlierRemoval](./operators/StatisticalOutlierRemoval.md) |
| `OperatorType.VoxelDownsample` | 体素下采样 | `Stable` | 1 | 2 | 1 | 95 (A) | `1.0.1` | Voxel grid centroid downsampling | [VoxelDownsample](./operators/VoxelDownsample.md) |

### 数据处理 / `DataProcessing` (18)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.Aggregator` | 数据聚合 | `Stable` | 3 | 6 | 1 | 94 (A) | `1.0.0` | 该算子用于将多路输入数据合并为列表，并提取极值与均值。运行时从声明输入端口读取数据，… | [Aggregator](./operators/Aggregator.md) |
| `OperatorType.ArrayIndexer` | 数组索引器 | `Stable` | 1 | 3 | 3 | 96 (A) | `1.0.0` | 该算子用于从列表中按索引或条件提取元素。运行时从声明输入端口读取数据，按参数表解析配… | [ArrayIndexer](./operators/ArrayIndexer.md) |
| `OperatorType.BoxFilter` | 候选框筛选 | `Stable` | 2 | 3 | 9 | 94 (A) | `1.0.0` | 该算子用于按面积、类别、区域或分数过滤检测结果。运行时从声明输入端口读取数据，按参数… | [BoxFilter](./operators/BoxFilter.md) |
| `OperatorType.BoxNms` | 候选框抑制 | `Stable` | 3 | 7 | 4 | 90 (A) | `1.0.0` | 该算子用于对检测框执行非极大值抑制。运行时从声明输入端口读取数据，按参数表解析配置，… | [BoxNms](./operators/BoxNms.md) |
| `OperatorType.Comparator` | 数值比较 | `Stable` | 2 | 2 | 5 | 89 (A) | `1.0.0` | 该算子用于比较两个数值的大小关系，输出布尔判定结果与差值。运行时从声明输入端口读取数… | [Comparator](./operators/Comparator.md) |
| `OperatorType.JsonExtractor` | JSON 提取器 | `Stable` | 1 | 2 | 4 | 100 (A) | `1.0.0` | 该算子用于按 JSONPath 从字符串中提取字段。运行时从声明输入端口读取数据，按… | [JsonExtractor](./operators/JsonExtractor.md) |
| `OperatorType.LogicGate` | 逻辑门 | `Stable` | 2 | 1 | 1 | 94 (A) | `1.0.0` | 该算子用于布尔逻辑运算 (AND, OR, NOT, XOR, NAND, NOR)… | [LogicGate](./operators/LogicGate.md) |
| `OperatorType.MathOperation` | 数值计算 | `Stable` | 2 | 2 | 1 | 100 (A) | `1.0.0` | 该算子用于支持加减乘除、取绝对值、开方等常用运算。运行时从声明输入端口读取数据，按参… | [MathOperation](./operators/MathOperation.md) |
| `OperatorType.PointSetTool` | 点集工具 | `Stable` | 2 | 4 | 6 | 90 (A) | `1.0.0` | 该算子用于合并、排序、过滤点列表并计算集合属性。运行时从声明输入端口读取数据，按参数… | [PointSetTool](./operators/PointSetTool.md) |
| `OperatorType.ScriptOperator` | 脚本算子 | `Stable` | 4 | 2 | 3 | 90 (A) | `1.0.0` | 该算子用于运行用户自定义表达式或脚本片段。运行时从声明输入端口读取数据，按参数表解析… | [ScriptOperator](./operators/ScriptOperator.md) |
| `OperatorType.Statistics` | 统计分析 | `Stable` | 1 | 7 | 5 | 90 (A) | `1.0.0` | 该算子用于基于滚动历史计算均值、标准差和 Cpk 统计结果。运行时从声明输入端口读取… | [Statistics](./operators/Statistics.md) |
| `OperatorType.StringFormat` | 字符串格式化 | `Stable` | 2 | 1 | 1 | 94 (A) | `1.0.0` | 该算子用于按模板生成字符串。运行时从声明输入端口读取数据，按参数表解析配置，并把处理… | [StringFormat](./operators/StringFormat.md) |
| `OperatorType.TimerStatistics` | 计时统计 | `Stable` | 1 | 4 | 4 | 94 (A) | `1.0.1` | 该算子用于统计耗时和周期时间。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [TimerStatistics](./operators/TimerStatistics.md) |
| `OperatorType.TypeConvert` | 类型转换 | `Stable` | 1 | 6 | 2 | 100 (A) | `1.0.0` | 该算子用于在字符串、浮点、整数、布尔等类型之间转换输入数据。运行时从声明输入端口读取… | [TypeConvert](./operators/TypeConvert.md) |
| `OperatorType.UnitConvert` | 单位换算 | `Stable` | 2 | 2 | 4 | 96 (A) | `1.0.0` | 该算子用于在像素、mm、um 和英寸之间进行数值换算。运行时从声明输入端口读取数据，… | [UnitConvert](./operators/UnitConvert.md) |
| `OperatorType.VariableIncrement` | 变量递增 | `Stable` | 0 | 5 | 7 | 100 (A) | `1.0.0` | 该算子用于递增单次运行变量或项目全局 Int64 变量。运行时从声明输入端口读取数据… | [VariableIncrement](./operators/VariableIncrement.md) |
| `OperatorType.VariableRead` | 变量读取 | `Stable` | 0 | 12 | 10 | 100 (A) | `1.0.0` | 该算子用于从单次运行变量或项目全局变量读取值。运行时从声明输入端口读取数据，按参数表… | [VariableRead](./operators/VariableRead.md) |
| `OperatorType.VariableWrite` | 变量写入 | `Stable` | 1 | 11 | 12 | 90 (A) | `1.0.0` | 该算子用于写入单次运行变量或项目全局变量。运行时从声明输入端口读取数据，按参数表解析… | [VariableWrite](./operators/VariableWrite.md) |

### 流程控制 / `FlowControl` (8)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ConditionalBranch` | 条件分支 | `Stable` | 2 | 5 | 12 | 100 (A) | `1.0.0` | 该算子用于根据数值/字符串/布尔条件执行 True/False 两路分支，常用于 O… | [ConditionalBranch](./operators/ConditionalBranch.md) |
| `OperatorType.CycleCounter` | 循环计数器 | `Stable` | 0 | 5 | 2 | 96 (A) | `1.0.0` | 该算子用于获取当前循环次数和统计信息。运行时从声明输入端口读取数据，按参数表解析配置… | [CycleCounter](./operators/CycleCounter.md) |
| `OperatorType.Delay` | 延时 | `Stable` | 1 | 2 | 1 | 94 (A) | `1.0.0` | 该算子用于等待指定时间后继续执行，常用于通信前等待下位机就绪。运行时从声明输入端口读… | [Delay](./operators/Delay.md) |
| `OperatorType.ForEach` | ForEach 循环 | `Stable` | 1 | 1 | 4 | 100 (A) | `1.0.0` | 该算子用于对集合中的每个元素执行子图。运行时从声明输入端口读取数据，按参数表解析配置… | [ForEach](./operators/ForEach.md) |
| `OperatorType.FrameChangeTrigger` | 帧变化触发 | `Stable` | 1 | 10 | 20 | 90 (A) | `1.0.0` | 该算子用于通过连续帧 ROI 变化判断端子是否到达；未到料时短路当前检测周期，避免空… | [FrameChangeTrigger](./operators/FrameChangeTrigger.md) |
| `OperatorType.ResultJudgment` | 结果判定 | `Stable` | 2 | 5 | 8 | 90 (A) | `1.0.1` | 该算子用于对数值、字符串等结果执行业务判定，输出条件检查结果。运行时从声明输入端口读… | [ResultJudgment](./operators/ResultJudgment.md) |
| `OperatorType.TriggerModule` | 触发模块 | `Stable` | 1 | 3 | 3 | 90 (A) | `1.0.0` | 该算子用于生成软件、定时或外部触发信号。运行时从声明输入端口读取数据，按参数表解析配… | [TriggerModule](./operators/TriggerModule.md) |
| `OperatorType.TryCatch` | Try分支透传 | `Stable` | 1 | 4 | 3 | 93 (A) | `1.0.0` | 该算子用于将输入透传到 Try 分支并输出空 Catch/无错误状态；本算子不捕获下… | [TryCatch](./operators/TryCatch.md) |

### 通信 / `Communication` (8)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.HttpRequest` | HTTP 请求 | `Stable` | 2 | 3 | 6 | 100 (A) | `1.0.0` | 该算子用于调用外部 REST API。运行时从声明输入端口读取数据，按参数表解析配置… | [HttpRequest](./operators/HttpRequest.md) |
| `OperatorType.MitsubishiMcCommunication` | 三菱MC通信 | `Stable` | 1 | 2 | 13 | 98 (A) | `1.0.0` | 该算子用于三菱 MC 协议 PLC 读写通信。运行时从声明输入端口读取数据，按参数表… | [MitsubishiMcCommunication](./operators/MitsubishiMcCommunication.md) |
| `OperatorType.ModbusCommunication` | Modbus TCP通信 | `Stable` | 1 | 2 | 9 | 100 (A) | `1.0.0` | 该算子用于通过 Modbus TCP 读写线圈和保持寄存器；当前算子不执行 Modb… | [ModbusCommunication](./operators/ModbusCommunication.md) |
| `OperatorType.MqttPublish` | MQTT 发布 | `Reference` | 2 | 1 | 6 | 100 (A) | `0.1.0` | 该算子用于在启用可选 MQTT 集成时发布检测数据。运行时从声明输入端口读取数据，按… | [MqttPublish](./operators/MqttPublish.md) |
| `OperatorType.OmronFinsCommunication` | 欧姆龙FINS通信 | `Stable` | 1 | 2 | 13 | 98 (A) | `1.0.0` | 该算子用于欧姆龙FINS/TCP协议PLC读写通信（CP1H/CJ2M/NJ/NX）… | [OmronFinsCommunication](./operators/OmronFinsCommunication.md) |
| `OperatorType.SerialCommunication` | 串口通信 | `Stable` | 1 | 1 | 9 | 100 (A) | `1.0.0` | 该算子用于RS-232/485 串口数据收发。运行时从声明输入端口读取数据，按参数表… | [SerialCommunication](./operators/SerialCommunication.md) |
| `OperatorType.SiemensS7Communication` | 西门子S7通信 | `Stable` | 1 | 2 | 15 | 98 (A) | `1.0.0` | 该算子用于西门子S7系列PLC读写通信（S7-200/300/400/1200/15… | [SiemensS7Communication](./operators/SiemensS7Communication.md) |
| `OperatorType.TcpCommunication` | TCP通信 | `Stable` | 1 | 12 | 39 | 90 (A) | `1.0.0` | 该算子用于TCP/IP网络通信。运行时从声明输入端口读取数据，按参数表解析配置，并把… | [TcpCommunication](./operators/TcpCommunication.md) |

### 输出与辅助 / `OutputAndAuxiliary` (5)
| 枚举 (Enum) | 显示名 (DisplayName) | 生命周期 | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.Comment` | 注释 | `Stable` | 1 | 2 | 1 | 90 (A) | `1.0.1` | Workflow annotation passthrough | [Comment](./operators/Comment.md) |
| `OperatorType.DatabaseWrite` | 数据库写入 | `Stable` | 2 | 2 | 3 | 100 (A) | `1.0.0` | 该算子用于将输入数据写入 SQLite / SQL Server / MySQL 表… | [DatabaseWrite](./operators/DatabaseWrite.md) |
| `OperatorType.ImageSave` | 图像保存 | `Stable` | 1 | 2 | 3 | 100 (A) | `1.0.0` | 该算子用于保存检测图像到本地硬盘。运行时从声明输入端口读取数据，按参数表解析配置，并… | [ImageSave](./operators/ImageSave.md) |
| `OperatorType.ResultOutput` | 结果输出 | `Stable` | 4 | 6 | 3 | 98 (A) | `1.0.1` | 该算子用于汇总检测结果并输出，支持 JSON/CSV/Text 格式，可选保存到文件… | [ResultOutput](./operators/ResultOutput.md) |
| `OperatorType.TextSave` | 文本保存 | `Stable` | 2 | 2 | 5 | 100 (A) | `1.0.0` | 该算子用于将文本或结构化数据保存为 text/csv/json 文件。运行时从声明输… | [TextSave](./operators/TextSave.md) |
