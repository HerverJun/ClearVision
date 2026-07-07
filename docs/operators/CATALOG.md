# 算子目录 / Operator Catalog

> 生成时间 / Generated At: `2026-07-07 08:45:40 +08:00`
> 算子总数 / Total Operators: **156**

## 分类统计 / Category Summary
| 分类 (Category) | 数量 (Count) | 占比 (Ratio) |
|------|------:|------:|
| 3D | 6 | 3.8% |
| AI Detection | 1 | 0.6% |
| AI 检测 | 1 | 0.6% |
| AI检测 | 5 | 3.2% |
| Analysis | 1 | 0.6% |
| Communication | 3 | 1.9% |
| Detection | 2 | 1.3% |
| Flow Control | 1 | 0.6% |
| Frequency | 3 | 1.9% |
| Morphology | 5 | 3.2% |
| Region | 4 | 2.6% |
| Texture | 2 | 1.3% |
| 匹配定位 | 8 | 5.1% |
| 变量 | 4 | 2.6% |
| 图像处理 | 4 | 2.6% |
| 定位 | 7 | 4.5% |
| 拆分组合 | 2 | 1.3% |
| 数据处理 | 10 | 6.4% |
| 标定 | 12 | 7.7% |
| 检测 | 18 | 11.5% |
| 流程控制 | 5 | 3.2% |
| 特征提取 | 4 | 2.6% |
| 识别 | 2 | 1.3% |
| 辅助 | 3 | 1.9% |
| 输出 | 2 | 1.3% |
| 通信 | 5 | 3.2% |
| 通用 | 4 | 2.6% |
| 逻辑工具 | 6 | 3.8% |
| 采集 | 1 | 0.6% |
| 预处理 | 23 | 14.7% |
| 颜色处理 | 2 | 1.3% |

## 质量评分 / Quality Score
- 平均分 / Average: **95.5**
| 等级 (Level) | 数量 (Count) |
|------|------:|
| A | 153 |
| B | 3 |

## 分类索引 / Grouped Index

### 3D (6)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.EuclideanClusterExtraction` | 欧氏聚类分割 | 1 | 3 | 3 | 100 (A) | `1.0.0` | 当前元数据描述为：Euclidean Cluster Extraction for … | [EuclideanClusterExtraction](./EuclideanClusterExtraction.md) |
| `OperatorType.PPFEstimation` | PPF点对特征 | 1 | 3 | 3 | 95 (A) | `1.0.0` | 当前元数据描述为：Compute Point Pair Features (PPF)… | [PPFEstimation](./PPFEstimation.md) |
| `OperatorType.PPFMatch` | PPF表面匹配 | 2 | 16 | 10 | 95 (A) | `1.0.4` | 当前元数据描述为：Simplified PPF-based 3D coarse su… | [PPFMatch](./PPFMatch.md) |
| `OperatorType.RansacPlaneSegmentation` | RANSAC平面分割 | 1 | 8 | 4 | 95 (A) | `1.0.0` | 当前元数据描述为：RANSAC plane segmentation for poi… | [RansacPlaneSegmentation](./RansacPlaneSegmentation.md) |
| `OperatorType.StatisticalOutlierRemoval` | 统计滤波 | 1 | 3 | 2 | 95 (A) | `1.0.0` | 当前元数据描述为：Statistical Outlier Removal (SOR)… | [StatisticalOutlierRemoval](./StatisticalOutlierRemoval.md) |
| `OperatorType.VoxelDownsample` | 体素下采样 | 1 | 2 | 1 | 95 (A) | `1.0.1` | Voxel grid centroid downsampling | [VoxelDownsample](./VoxelDownsample.md) |

### AI Detection (1)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.DualModalVoting` | Dual Modal Voting | 2 | 3 | 6 | 84 (B) | `1.0.0` | 当前元数据描述为：Combines deep learning and tradit… | [DualModalVoting](./DualModalVoting.md) |

### AI 检测 (1)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.DetectionSequenceJudge` | 检测顺序判定 | 4 | 13 | 13 | 100 (A) | `1.0.0` | 当前元数据描述为：Sorts detections and compares the… | [DetectionSequenceJudge](./DetectionSequenceJudge.md) |

### AI检测 (5)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AnomalyDetection` | 异常检测 | 2 | 8 | 16 | 100 (A) | `1.0.0` | Simplified PatchCore | [AnomalyDetection](./AnomalyDetection.md) |
| `OperatorType.DeepLearning` | 深度学习 | 1 | 14 | 14 | 100 (A) | `1.0.0` | 该算子用于AI 深度学习推理，支持 YOLOv5/v6/v8/v11 等模型，用于缺… | [DeepLearning](./DeepLearning.md) |
| `OperatorType.EdgePairDefect` | 边缘对缺陷 | 3 | 4 | 4 | 96 (A) | `1.0.0` | 当前元数据描述为：Checks edge-pair spacing deviatio… | [EdgePairDefect](./EdgePairDefect.md) |
| `OperatorType.SemanticSegmentation` | 语义分割 | 1 | 12 | 12 | 100 (A) | `1.0.0` | 当前元数据描述为：Runs an ONNX semantic segmentatio… | [SemanticSegmentation](./SemanticSegmentation.md) |
| `OperatorType.SurfaceDefectDetection` | 表面缺陷检测 | 2 | 8 | 24 | 100 (A) | `2.0.0` | 当前元数据描述为：Detects surface defects using gra… | [SurfaceDefectDetection](./SurfaceDefectDetection.md) |

### Analysis (1)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.DistanceTransform` | Distance Transform | 1 | 4 | 7 | 100 (A) | `1.0.1` | OpenCV binary distance transform | [DistanceTransform](./DistanceTransform.md) |

### Communication (3)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.MitsubishiMcCommunication` | Mitsubishi MC Communication | 1 | 2 | 13 | 98 (A) | `1.0.0` | 当前元数据描述为：Mitsubishi MC protocol PLC read/w… | [MitsubishiMcCommunication](./MitsubishiMcCommunication.md) |
| `OperatorType.ModbusCommunication` | Modbus Communication | 1 | 2 | 9 | 100 (A) | `1.0.0` | 当前元数据描述为：Industrial Modbus TCP communicati… | [ModbusCommunication](./ModbusCommunication.md) |
| `OperatorType.MqttPublish` | MQTT Publish | 2 | 1 | 6 | 100 (A) | `0.1.0` | 当前元数据描述为：Publishes inspection data to MQTT… | [MqttPublish](./MqttPublish.md) |

### Detection (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AngleMeasurement` | Angle Measurement | 6 | 3 | 7 | 96 (A) | `1.0.0` | 当前元数据描述为：Measures angle from three points … | [AngleMeasurement](./AngleMeasurement.md) |
| `OperatorType.ContourMeasurement` | Contour Measurement | 1 | 4 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Measures contour area, perimeter,… | [ContourMeasurement](./ContourMeasurement.md) |

### Flow Control (1)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ResultJudgment` | Result Judgment | 2 | 5 | 8 | 90 (A) | `1.0.1` | 当前元数据描述为：Generic business judgment with nu… | [ResultJudgment](./ResultJudgment.md) |

### Frequency (3)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.FFT1D` | FFT 1D | 2 | 4 | 0 | 89 (A) | `1.0.0` | 当前元数据描述为：Performs FFT on 1D signals and em… | [FFT1D](./FFT1D.md) |
| `OperatorType.FrequencyFilter` | Frequency Filter | 5 | 3 | 0 | 81 (B) | `1.0.0` | 当前元数据描述为：Applies frequency-domain filters … | [FrequencyFilter](./FrequencyFilter.md) |
| `OperatorType.InverseFFT1D` | Inverse FFT 1D | 2 | 4 | 0 | 89 (A) | `1.0.0` | 当前元数据描述为：Performs inverse FFT on 1D spectr… | [InverseFFT1D](./InverseFFT1D.md) |

### Morphology (5)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.RegionClosing` | Region Closing | 2 | 3 | 3 | 90 (A) | `1.0.1` | Region morphology closing | [RegionClosing](./RegionClosing.md) |
| `OperatorType.RegionDilation` | Region Dilation | 2 | 3 | 4 | 90 (A) | `1.0.1` | Region morphology dilation | [RegionDilation](./RegionDilation.md) |
| `OperatorType.RegionErosion` | Region Erosion | 2 | 3 | 4 | 90 (A) | `1.0.1` | Region morphology erosion | [RegionErosion](./RegionErosion.md) |
| `OperatorType.RegionOpening` | Region Opening | 2 | 3 | 3 | 90 (A) | `1.0.1` | Region morphology opening | [RegionOpening](./RegionOpening.md) |
| `OperatorType.RegionSkeleton` | Region Skeleton | 2 | 5 | 2 | 90 (A) | `1.0.1` | Zhang-Suen thinning | [RegionSkeleton](./RegionSkeleton.md) |

### Region (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.RegionComplement` | Region Complement | 4 | 3 | 0 | 85 (A) | `1.0.1` | Bounded run-length complement | [RegionComplement](./RegionComplement.md) |
| `OperatorType.RegionDifference` | Region Difference | 2 | 3 | 0 | 89 (A) | `1.0.1` | Run-length row subtraction | [RegionDifference](./RegionDifference.md) |
| `OperatorType.RegionIntersection` | Region Intersection | 2 | 3 | 0 | 89 (A) | `1.0.1` | Run-length row intersection | [RegionIntersection](./RegionIntersection.md) |
| `OperatorType.RegionUnion` | Region Union | 2 | 3 | 0 | 89 (A) | `1.0.1` | Run-length region union | [RegionUnion](./RegionUnion.md) |

### Texture (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.GlcmTexture` | GLCM Texture Features | 1 | 6 | 9 | 100 (A) | `1.0.1` | Quantized gray-level co-occurrence matrix | [GlcmTexture](./GlcmTexture.md) |
| `OperatorType.LawsTextureFilter` | Laws Texture Filter | 1 | 3 | 5 | 100 (A) | `1.0.1` | Laws 5x5 texture energy filtering | [LawsTextureFilter](./LawsTextureFilter.md) |

### 匹配定位 (8)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AkazeFeatureMatch` | AKAZE特征匹配 | 2 | 13 | 14 | 90 (A) | `1.0.0` | AKAZE Homography Feature Match | [AkazeFeatureMatch](./AkazeFeatureMatch.md) |
| `OperatorType.GradientShapeMatch` | 梯度形状匹配 | 2 | 6 | 12 | 100 (A) | `1.1.0` | Gradient Direction Template Match | [GradientShapeMatch](./GradientShapeMatch.md) |
| `OperatorType.LocalDeformableMatching` | Local Deformable Matching | 2 | 6 | 15 | 100 (A) | `1.1.1` | Coarse-to-fine local deformable matching | [LocalDeformableMatching](./LocalDeformableMatching.md) |
| `OperatorType.OrbFeatureMatch` | ORB特征匹配 | 2 | 13 | 17 | 90 (A) | `1.0.0` | ORB Homography Feature Match | [OrbFeatureMatch](./OrbFeatureMatch.md) |
| `OperatorType.PlanarMatching` | Planar Matching | 2 | 19 | 20 | 100 (A) | `1.1.2` | Feature homography planar matching | [PlanarMatching](./PlanarMatching.md) |
| `OperatorType.PyramidShapeMatch` | 金字塔形状匹配 | 2 | 5 | 15 | 100 (A) | `1.0.0` | LINEMOD Pyramid Shape Matching | [PyramidShapeMatch](./PyramidShapeMatch.md) |
| `OperatorType.ShapeMatching` | 旋转尺度模板匹配 | 2 | 2 | 13 | 100 (A) | `1.2.0` | 当前元数据描述为：Rotation-scale template matching … | [ShapeMatching](./ShapeMatching.md) |
| `OperatorType.TemplateMatching` | 模板匹配 | 3 | 13 | 20 | 96 (A) | `1.2.0` | 当前元数据描述为：Classic template matching with op… | [TemplateMatching](./TemplateMatching.md) |

### 变量 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.CycleCounter` | 循环计数器 | 0 | 5 | 2 | 96 (A) | `1.0.0` | 该算子用于获取当前循环次数和统计信息。运行时从声明输入端口读取数据，按参数表解析配置… | [CycleCounter](./CycleCounter.md) |
| `OperatorType.VariableIncrement` | 变量递增 | 0 | 5 | 7 | 100 (A) | `1.0.0` | 该算子用于计数器自增/自减，支持重置条件。运行时从声明输入端口读取数据，按参数表解析… | [VariableIncrement](./VariableIncrement.md) |
| `OperatorType.VariableRead` | 变量读取 | 0 | 3 | 7 | 100 (A) | `1.0.0` | 该算子用于从单次运行变量或项目全局变量读取值。运行时从声明输入端口读取数据，按参数表… | [VariableRead](./VariableRead.md) |
| `OperatorType.VariableWrite` | 变量写入 | 1 | 3 | 8 | 90 (A) | `1.0.0` | 该算子用于写入单次运行变量或项目全局变量。运行时从声明输入端口读取数据，按参数表解析… | [VariableWrite](./VariableWrite.md) |

### 图像处理 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AffineTransform` | 仿射变换 | 1 | 2 | 9 | 100 (A) | `1.0.0` | 当前元数据描述为：Applies 2D affine transform using… | [AffineTransform](./AffineTransform.md) |
| `OperatorType.CopyMakeBorder` | 边界填充 | 1 | 1 | 6 | 94 (A) | `1.0.0` | 当前元数据描述为：Pads image border using OpenCV bo… | [CopyMakeBorder](./CopyMakeBorder.md) |
| `OperatorType.ImageStitching` | 图像拼接 | 2 | 2 | 3 | 94 (A) | `1.0.0` | 当前元数据描述为：Stitches two images into a larger… | [ImageStitching](./ImageStitching.md) |
| `OperatorType.PolarUnwrap` | 极坐标展开 | 2 | 1 | 8 | 100 (A) | `1.0.0` | 当前元数据描述为：Unwraps annular image regions int… | [PolarUnwrap](./PolarUnwrap.md) |

### 定位 (7)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.BlobLabeling` | 连通域标注 | 2 | 3 | 3 | 100 (A) | `1.0.0` | 当前元数据描述为：Classifies connected blobs by geo… | [BlobLabeling](./BlobLabeling.md) |
| `OperatorType.CornerDetection` | 角点检测 | 1 | 3 | 5 | 94 (A) | `1.0.0` | 当前元数据描述为：Detects corner points using Harri… | [CornerDetection](./CornerDetection.md) |
| `OperatorType.EdgeIntersection` | 边线交点 | 2 | 4 | 1 | 96 (A) | `1.0.0` | 当前元数据描述为：Computes line intersection and an… | [EdgeIntersection](./EdgeIntersection.md) |
| `OperatorType.ParallelLineFind` | 平行线查找 | 1 | 6 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Finds best pair of near-parallel … | [ParallelLineFind](./ParallelLineFind.md) |
| `OperatorType.PositionCorrection` | 位置修正 | 4 | 10 | 3 | 94 (A) | `1.0.2` | 当前元数据描述为：Pixel-space ROI offset tool. Use … | [PositionCorrection](./PositionCorrection.md) |
| `OperatorType.QuadrilateralFind` | 四边形查找 | 1 | 6 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Finds quadrilateral contours with… | [QuadrilateralFind](./QuadrilateralFind.md) |
| `OperatorType.RectangleDetection` | 矩形检测 | 1 | 10 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Detects rectangular/quadrilateral… | [RectangleDetection](./RectangleDetection.md) |

### 拆分组合 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ImageCompose` | 图像组合 | 4 | 1 | 3 | 94 (A) | `1.0.0` | 当前元数据描述为：Composes multiple images by conca… | [ImageCompose](./ImageCompose.md) |
| `OperatorType.ImageTiling` | 图像切片 | 1 | 3 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Splits an image into tiled region… | [ImageTiling](./ImageTiling.md) |

### 数据处理 (10)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.Aggregator` | 数据聚合 | 3 | 6 | 1 | 94 (A) | `1.0.0` | 该算子用于将多路输入数据合并为列表，并提取极值与均值。运行时从声明输入端口读取数据，… | [Aggregator](./Aggregator.md) |
| `OperatorType.ArrayIndexer` | 数组索引器 | 1 | 3 | 3 | 96 (A) | `1.0.0` | 该算子用于从列表中按索引或条件提取元素。运行时从声明输入端口读取数据，按参数表解析配… | [ArrayIndexer](./ArrayIndexer.md) |
| `OperatorType.BoxFilter` | 候选框过滤 (Bounding Box) | 2 | 3 | 9 | 94 (A) | `1.0.0` | 当前元数据描述为：Filters detections by area, class… | [BoxFilter](./BoxFilter.md) |
| `OperatorType.BoxNms` | 候选框抑制 | 3 | 7 | 4 | 90 (A) | `1.0.0` | 当前元数据描述为：Runs non-maximum suppression on d… | [BoxNms](./BoxNms.md) |
| `OperatorType.DatabaseWrite` | 数据库写入 | 2 | 2 | 3 | 100 (A) | `1.0.0` | 该算子用于将输入数据写入 SQLite / SQL Server / MySQL 表… | [DatabaseWrite](./DatabaseWrite.md) |
| `OperatorType.JsonExtractor` | JSON 提取器 | 1 | 2 | 4 | 100 (A) | `1.0.0` | 该算子用于按 JSONPath 从字符串中提取字段。运行时从声明输入端口读取数据，按… | [JsonExtractor](./JsonExtractor.md) |
| `OperatorType.MathOperation` | 数值计算 | 2 | 2 | 1 | 100 (A) | `1.0.0` | 该算子用于支持加减乘除、取绝对值、开方等常用运算。运行时从声明输入端口读取数据，按参… | [MathOperation](./MathOperation.md) |
| `OperatorType.PointAlignment` | 点位对齐 | 2 | 3 | 2 | 96 (A) | `1.0.3` | 当前元数据描述为：Pixel-space alignment helper for … | [PointAlignment](./PointAlignment.md) |
| `OperatorType.PointCorrection` | 点位修正 | 4 | 5 | 4 | 96 (A) | `1.0.3` | 当前元数据描述为：Pixel-space rigid correction help… | [PointCorrection](./PointCorrection.md) |
| `OperatorType.UnitConvert` | 单位换算 | 2 | 2 | 4 | 96 (A) | `1.0.0` | 当前元数据描述为：Converts value between pixel, mm,… | [UnitConvert](./UnitConvert.md) |

### 标定 (12)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.CalibrationLoader` | Calibration Loader | 0 | 3 | 1 | 100 (A) | `1.0.0` | 当前元数据描述为：Loads CalibrationBundleV2 JSON an… | [CalibrationLoader](./CalibrationLoader.md) |
| `OperatorType.CameraCalibration` | Camera Calibration | 1 | 2 | 7 | 100 (A) | `1.0.0` | 当前元数据描述为：Calibrates camera intrinsics from… | [CameraCalibration](./CameraCalibration.md) |
| `OperatorType.CoordinateTransform` | Coordinate Transform | 4 | 3 | 2 | 90 (A) | `1.0.0` | 当前元数据描述为：Converts pixel coordinates to phy… | [CoordinateTransform](./CoordinateTransform.md) |
| `OperatorType.FisheyeCalibration` | Fisheye Calibration | 1 | 2 | 9 | 100 (A) | `1.0.0` | 当前元数据描述为：Calibrates fisheye camera intrins… | [FisheyeCalibration](./FisheyeCalibration.md) |
| `OperatorType.FisheyeUndistort` | Fisheye Undistort | 2 | 2 | 4 | 96 (A) | `1.0.0` | 当前元数据描述为：Correct fisheye lens distortion u… | [FisheyeUndistort](./FisheyeUndistort.md) |
| `OperatorType.HandEyeCalibration` | Hand-Eye Calibration | 2 | 7 | 4 | 100 (A) | `1.0.0` | OpenCV Hand-Eye Calibration | [HandEyeCalibration](./HandEyeCalibration.md) |
| `OperatorType.HandEyeCalibrationValidator` | Hand-Eye Calibration Validator | 3 | 8 | 1 | 100 (A) | `1.0.1` | Hand-Eye Consistency Validation | [HandEyeCalibrationValidator](./HandEyeCalibrationValidator.md) |
| `OperatorType.NPointCalibration` | N Point Calibration | 1 | 9 | 10 | 100 (A) | `1.0.0` | 当前元数据描述为：Builds robust affine or homograph… | [NPointCalibration](./NPointCalibration.md) |
| `OperatorType.PixelToWorldTransform` | Pixel To World Transform | 3 | 3 | 9 | 100 (A) | `1.0.1` | 当前元数据描述为：Transforms coordinates via Calibr… | [PixelToWorldTransform](./PixelToWorldTransform.md) |
| `OperatorType.StereoCalibration` | Stereo Calibration | 2 | 6 | 11 | 100 (A) | `1.0.0` | 当前元数据描述为：Calibrates stereo camera pair and… | [StereoCalibration](./StereoCalibration.md) |
| `OperatorType.TranslationRotationCalibration` | 平移旋转标定 | 1 | 3 | 3 | 100 (A) | `1.0.0` | 当前元数据描述为：Fits robust 2D rigid or similarit… | [TranslationRotationCalibration](./TranslationRotationCalibration.md) |
| `OperatorType.Undistort` | Undistort | 2 | 1 | 0 | 91 (A) | `1.0.0` | 当前元数据描述为：Correct lens distortion using cal… | [Undistort](./Undistort.md) |

### 检测 (18)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ArcCaliper` | Arc Caliper | 7 | 2 | 0 | 81 (B) | `1.0.1` | Radial band-profile arc edge scan | [ArcCaliper](./ArcCaliper.md) |
| `OperatorType.CaliperTool` | 卡尺工具 | 2 | 7 | 9 | 96 (A) | `1.0.0` | 当前元数据描述为：Detects edge pairs along a scan l… | [CaliperTool](./CaliperTool.md) |
| `OperatorType.CircleMeasurement` | 圆测量 | 1 | 13 | 25 | 100 (A) | `1.1.2` | 该算子用于霍夫变换检测圆形并测量半径与圆心坐标，适用于孔径检测和圆形定位。运行时从声… | [CircleMeasurement](./CircleMeasurement.md) |
| `OperatorType.ContourExtrema` | Contour Extrema | 3 | 6 | 0 | 89 (A) | `1.0.1` | Directional contour extrema scan | [ContourExtrema](./ContourExtrema.md) |
| `OperatorType.GapMeasurement` | 间隙测量 | 2 | 9 | 8 | 96 (A) | `1.0.0` | 当前元数据描述为：Measures spacing using points or … | [GapMeasurement](./GapMeasurement.md) |
| `OperatorType.GeoMeasurement` | 几何测量 | 2 | 5 | 3 | 96 (A) | `1.0.0` | 当前元数据描述为：General geometry measurement betw… | [GeoMeasurement](./GeoMeasurement.md) |
| `OperatorType.GeometricFitting` | Geometric Fitting | 1 | 2 | 8 | 100 (A) | `1.0.0` | 当前元数据描述为：Fits line, circle or ellipse from… | [GeometricFitting](./GeometricFitting.md) |
| `OperatorType.GeometricTolerance` | 几何公差 | 5 | 7 | 5 | 96 (A) | `1.0.0` | 当前元数据描述为：Evaluates a constrained 2D GD&T s… | [GeometricTolerance](./GeometricTolerance.md) |
| `OperatorType.HistogramAnalysis` | 直方图分析 | 1 | 11 | 6 | 94 (A) | `1.0.0` | 当前元数据描述为：Computes histogram and intensity-… | [HistogramAnalysis](./HistogramAnalysis.md) |
| `OperatorType.LineLineDistance` | 线线距离 | 2 | 5 | 3 | 96 (A) | `1.0.0` | 当前元数据描述为：Computes distance and angle betwe… | [LineLineDistance](./LineLineDistance.md) |
| `OperatorType.LineMeasurement` | 直线测量 | 1 | 5 | 4 | 96 (A) | `1.0.0` | 当前元数据描述为：Detects line features and reports… | [LineMeasurement](./LineMeasurement.md) |
| `OperatorType.Measurement` | 测量 | 3 | 2 | 5 | 96 (A) | `1.0.0` | 该算子用于两点/水平/垂直距离测量，支持参数坐标与 PointA/PointB 输入… | [Measurement](./Measurement.md) |
| `OperatorType.MinEnclosingGeometry` | Min Enclosing Geometry | 1 | 2 | 10 | 100 (A) | `1.0.1` | Contour-derived enclosing geometry and robust fitting | [MinEnclosingGeometry](./MinEnclosingGeometry.md) |
| `OperatorType.PhaseClosure` | Phase Closure | 4 | 4 | 0 | 89 (A) | `1.0.1` | Itoh/quality-guided phase unwrapping | [PhaseClosure](./PhaseClosure.md) |
| `OperatorType.PixelStatistics` | 像素统计 | 2 | 6 | 5 | 96 (A) | `1.0.0` | 当前元数据描述为：Computes ROI/masked pixel-level s… | [PixelStatistics](./PixelStatistics.md) |
| `OperatorType.PointLineDistance` | 点线距离 | 2 | 2 | 2 | 96 (A) | `1.0.0` | 当前元数据描述为：Computes distance from a point to… | [PointLineDistance](./PointLineDistance.md) |
| `OperatorType.SharpnessEvaluation` | 清晰度评估 | 1 | 3 | 8 | 96 (A) | `1.0.0` | 当前元数据描述为：Evaluates focus quality of an ima… | [SharpnessEvaluation](./SharpnessEvaluation.md) |
| `OperatorType.WidthMeasurement` | 宽度测量 | 3 | 8 | 8 | 96 (A) | `1.0.0` | 当前元数据描述为：Measures width between parallel e… | [WidthMeasurement](./WidthMeasurement.md) |

### 流程控制 (5)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.Comparator` | 数值比较 | 2 | 2 | 5 | 89 (A) | `1.0.0` | 该算子用于比较两个数值的大小关系，输出布尔判定结果与差值。运行时从声明输入端口读取数… | [Comparator](./Comparator.md) |
| `OperatorType.ConditionalBranch` | 条件分支 | 1 | 2 | 3 | 100 (A) | `1.0.0` | 该算子用于根据数值/字符串/布尔条件执行 True/False 两路分支，常用于 O… | [ConditionalBranch](./ConditionalBranch.md) |
| `OperatorType.Delay` | 延时 | 1 | 2 | 1 | 94 (A) | `1.0.0` | 该算子用于等待指定时间后继续执行，常用于通信前等待下位机就绪。运行时从声明输入端口读… | [Delay](./Delay.md) |
| `OperatorType.ForEach` | ForEach 循环 | 1 | 1 | 4 | 100 (A) | `1.0.0` | 该算子用于对集合中的每个元素执行子图。运行时从声明输入端口读取数据，按参数表解析配置… | [ForEach](./ForEach.md) |
| `OperatorType.TryCatch` | 异常捕获 | 1 | 4 | 3 | 93 (A) | `1.0.0` | 该算子用于Try-Catch 流程控制。运行时从声明输入端口读取数据，按参数表解析配… | [TryCatch](./TryCatch.md) |

### 特征提取 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.BlobAnalysis` | Blob分析 | 2 | 4 | 17 | 100 (A) | `1.1.0` | 该算子用于连通区域分析。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果… | [BlobAnalysis](./BlobAnalysis.md) |
| `OperatorType.ContourDetection` | 轮廓检测 | 1 | 3 | 11 | 94 (A) | `1.0.0` | 该算子用于查找图像轮廓，提取边缘点集和层次关系，供后续测量和拟合使用。运行时从声明输… | [ContourDetection](./ContourDetection.md) |
| `OperatorType.EdgeDetection` | Edge Detection | 1 | 2 | 14 | 100 (A) | `1.0.0` | 当前元数据描述为：Detects edges with Canny and opti… | [EdgeDetection](./EdgeDetection.md) |
| `OperatorType.SubpixelEdgeDetection` | Subpixel Edge Detection | 1 | 2 | 5 | 94 (A) | `1.0.0` | 当前元数据描述为：Non-industrial reference subpixel… | [SubpixelEdgeDetection](./SubpixelEdgeDetection.md) |

### 识别 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.CodeRecognition` | 条码识别 | 1 | 4 | 2 | 100 (A) | `1.0.0` | 该算子用于一维码/二维码识别，支持 QR、Code128、DataMatrix 等多… | [CodeRecognition](./CodeRecognition.md) |
| `OperatorType.OcrRecognition` | OCR 识别 | 1 | 2 | 0 | 100 (A) | `1.0.0` | 该算子用于识别图像中的文本内容。运行时从声明输入端口读取数据，按参数表解析配置，并把… | [OcrRecognition](./OcrRecognition.md) |

### 辅助 (3)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.Comment` | 注释 | 1 | 2 | 1 | 90 (A) | `1.0.1` | Workflow annotation passthrough | [Comment](./Comment.md) |
| `OperatorType.RoiManager` | ROI管理器 | 1 | 3 | 10 | 100 (A) | `1.0.0` | 该算子用于矩形/圆形/多边形区域选择。运行时从声明输入端口读取数据，按参数表解析配置… | [RoiManager](./RoiManager.md) |
| `OperatorType.RoiTransform` | ROI跟踪 | 2 | 1 | 1 | 96 (A) | `1.0.1` | Pose-driven ROI rectangle transform | [RoiTransform](./RoiTransform.md) |

### 输出 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ImageSave` | 图像保存 | 1 | 2 | 3 | 100 (A) | `1.0.0` | 该算子用于保存检测图像到本地硬盘。运行时从声明输入端口读取数据，按参数表解析配置，并… | [ImageSave](./ImageSave.md) |
| `OperatorType.ResultOutput` | 结果输出 | 4 | 6 | 3 | 98 (A) | `1.0.1` | 该算子用于汇总检测结果并输出，支持 JSON/CSV/Text 格式，可选保存到文件… | [ResultOutput](./ResultOutput.md) |

### 通信 (5)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.HttpRequest` | HTTP 请求 | 2 | 3 | 6 | 100 (A) | `1.0.0` | 该算子用于调用外部 REST API。运行时从声明输入端口读取数据，按参数表解析配置… | [HttpRequest](./HttpRequest.md) |
| `OperatorType.OmronFinsCommunication` | 欧姆龙FINS通信 | 1 | 2 | 13 | 98 (A) | `1.0.0` | 该算子用于欧姆龙FINS/TCP协议PLC读写通信（CP1H/CJ2M/NJ/NX）… | [OmronFinsCommunication](./OmronFinsCommunication.md) |
| `OperatorType.SerialCommunication` | 串口通信 | 1 | 1 | 8 | 100 (A) | `1.0.0` | 该算子用于RS-232/485 串口数据收发。运行时从声明输入端口读取数据，按参数表… | [SerialCommunication](./SerialCommunication.md) |
| `OperatorType.SiemensS7Communication` | 西门子S7通信 | 1 | 2 | 15 | 98 (A) | `1.0.0` | 该算子用于西门子S7系列PLC读写通信（S7-200/300/400/1200/15… | [SiemensS7Communication](./SiemensS7Communication.md) |
| `OperatorType.TcpCommunication` | TCP通信 | 1 | 2 | 6 | 100 (A) | `1.0.0` | 该算子用于TCP/IP网络通信。运行时从声明输入端口读取数据，按参数表解析配置，并把… | [TcpCommunication](./TcpCommunication.md) |

### 通用 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.LogicGate` | 逻辑门 | 2 | 1 | 1 | 94 (A) | `1.0.0` | 该算子用于布尔逻辑运算 (AND, OR, NOT, XOR, NAND, NOR)… | [LogicGate](./LogicGate.md) |
| `OperatorType.Statistics` | Statistics | 1 | 7 | 5 | 90 (A) | `1.0.0` | 当前元数据描述为：Computes Mean/StdDev/Cpk statisti… | [Statistics](./Statistics.md) |
| `OperatorType.StringFormat` | 字符串格式化 | 2 | 1 | 1 | 94 (A) | `1.0.0` | 该算子用于按模板生成字符串。运行时从声明输入端口读取数据，按参数表解析配置，并把处理… | [StringFormat](./StringFormat.md) |
| `OperatorType.TypeConvert` | Type Convert | 1 | 6 | 2 | 100 (A) | `1.0.0` | 当前元数据描述为：Converts input data across String… | [TypeConvert](./TypeConvert.md) |

### 逻辑工具 (6)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.FrameChangeTrigger` | 帧变化触发 | 1 | 10 | 20 | 100 (A) | `1.0.0` | 该算子用于通过连续帧 ROI 变化判断端子是否到达；未到料时短路当前检测周期，避免空… | [FrameChangeTrigger](./FrameChangeTrigger.md) |
| `OperatorType.PointSetTool` | 点集工具 | 2 | 4 | 6 | 90 (A) | `1.0.0` | 当前元数据描述为：Merges/sorts/filters point lists … | [PointSetTool](./PointSetTool.md) |
| `OperatorType.ScriptOperator` | 脚本算子 | 4 | 2 | 3 | 90 (A) | `1.0.0` | 当前元数据描述为：Runs user-defined expression or s… | [ScriptOperator](./ScriptOperator.md) |
| `OperatorType.TextSave` | Text Save | 2 | 2 | 5 | 100 (A) | `1.0.0` | 当前元数据描述为：Saves text or structured data to … | [TextSave](./TextSave.md) |
| `OperatorType.TimerStatistics` | 计时统计 | 1 | 4 | 4 | 94 (A) | `1.0.1` | 当前元数据描述为：Measures elapsed and cycle time s… | [TimerStatistics](./TimerStatistics.md) |
| `OperatorType.TriggerModule` | 触发模块 | 1 | 3 | 3 | 90 (A) | `1.0.0` | 当前元数据描述为：Generates software, timer, or ext… | [TriggerModule](./TriggerModule.md) |

### 采集 (1)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ImageAcquisition` | 图像采集 | 2 | 1 | 6 | 100 (A) | `1.0.0` | 该算子用于从文件或相机采集图像。运行时从声明输入端口读取数据，按参数表解析配置，并把… | [ImageAcquisition](./ImageAcquisition.md) |

### 预处理 (23)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AdaptiveThreshold` | Adaptive Threshold | 1 | 1 | 5 | 94 (A) | `1.0.0` | 当前元数据描述为：Local mean or Gaussian adaptive t… | [AdaptiveThreshold](./AdaptiveThreshold.md) |
| `OperatorType.BilateralFilter` | 双边滤波 | 1 | 1 | 3 | 100 (A) | `1.0.0` | 该算子用于边缘保留的平滑滤波。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [BilateralFilter](./BilateralFilter.md) |
| `OperatorType.ClaheEnhancement` | CLAHE增强 | 1 | 1 | 5 | 94 (A) | `1.0.0` | 当前元数据描述为：Adaptive histogram equalization f… | [ClaheEnhancement](./ClaheEnhancement.md) |
| `OperatorType.ColorConversion` | 颜色空间转换 | 1 | 1 | 2 | 94 (A) | `1.0.0` | 该算子用于BGR/GRAY/HSV/Lab/YUV等颜色空间转换。运行时从声明输入端… | [ColorConversion](./ColorConversion.md) |
| `OperatorType.Filtering` | Gaussian Blur | 1 | 1 | 4 | 94 (A) | `1.0.0` | Gaussian Blur (OpenCV) | [Filtering](./Filtering.md) |
| `OperatorType.FrameAveraging` | 帧平均 | 1 | 2 | 2 | 94 (A) | `1.0.0` | 当前元数据描述为：Averages multi-frame input to red… | [FrameAveraging](./FrameAveraging.md) |
| `OperatorType.HistogramEqualization` | 直方图均衡化 | 1 | 1 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Supports global histogram equaliz… | [HistogramEqualization](./HistogramEqualization.md) |
| `OperatorType.ImageAdd` | 图像加法 | 2 | 1 | 6 | 100 (A) | `1.0.0` | 该算子用于两幅图像叠加/合并。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [ImageAdd](./ImageAdd.md) |
| `OperatorType.ImageBlend` | 图像融合 | 2 | 1 | 3 | 94 (A) | `1.0.0` | 该算子用于加权混合/透明叠加。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [ImageBlend](./ImageBlend.md) |
| `OperatorType.ImageCrop` | 图像裁剪 | 1 | 1 | 4 | 94 (A) | `1.0.0` | 该算子用于ROI区域提取。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结… | [ImageCrop](./ImageCrop.md) |
| `OperatorType.ImageDiff` | 图像对比 | 2 | 2 | 0 | 89 (A) | `1.0.0` | 该算子用于分析两幅图像的差异。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [ImageDiff](./ImageDiff.md) |
| `OperatorType.ImageNormalize` | 图像归一化 | 1 | 1 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Normalizes pixel distribution for… | [ImageNormalize](./ImageNormalize.md) |
| `OperatorType.ImageResize` | 图像缩放 | 1 | 1 | 5 | 94 (A) | `1.0.0` | 该算子用于调整图像尺寸。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果… | [ImageResize](./ImageResize.md) |
| `OperatorType.ImageRotate` | 图像旋转 | 1 | 1 | 5 | 94 (A) | `1.0.0` | 该算子用于任意角度旋转。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果… | [ImageRotate](./ImageRotate.md) |
| `OperatorType.ImageSubtract` | Image Subtract | 2 | 4 | 1 | 89 (A) | `1.0.0` | 当前元数据描述为：Computes subtraction or absolute … | [ImageSubtract](./ImageSubtract.md) |
| `OperatorType.LaplacianSharpen` | 拉普拉斯锐化 | 1 | 1 | 3 | 94 (A) | `1.0.0` | 该算子用于基于拉普拉斯算子的边缘增强。运行时从声明输入端口读取数据，按参数表解析配置… | [LaplacianSharpen](./LaplacianSharpen.md) |
| `OperatorType.MeanFilter` | 均值滤波 | 1 | 1 | 2 | 94 (A) | `1.0.0` | 当前元数据描述为：Applies mean (box blur) filtering… | [MeanFilter](./MeanFilter.md) |
| `OperatorType.MedianBlur` | 中值滤波 | 1 | 1 | 1 | 94 (A) | `1.0.0` | 该算子用于有效去除椒盐噪声同时保留边缘。运行时从声明输入端口读取数据，按参数表解析配… | [MedianBlur](./MedianBlur.md) |
| `OperatorType.MorphologicalOperation` | Morphological Operation | 1 | 1 | 7 | 94 (A) | `1.0.0` | 当前元数据描述为：Erode, Dilate, Open, Close, Gradi… | [MorphologicalOperation](./MorphologicalOperation.md) |
| `OperatorType.Morphology` | Morphology (Legacy) | 1 | 1 | 6 | 94 (A) | `1.0.0` | 当前元数据描述为：Legacy image morphology node. Use… | [Morphology](./Morphology.md) |
| `OperatorType.PerspectiveTransform` | 透视变换 | 3 | 1 | 20 | 100 (A) | `1.0.0` | 该算子用于四边形透视校正。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结… | [PerspectiveTransform](./PerspectiveTransform.md) |
| `OperatorType.ShadingCorrection` | 光照校正 | 2 | 1 | 3 | 96 (A) | `1.0.0` | 当前元数据描述为：Corrects uneven illumination by b… | [ShadingCorrection](./ShadingCorrection.md) |
| `OperatorType.Thresholding` | Threshold | 1 | 1 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Global thresholding with optional… | [Thresholding](./Thresholding.md) |

### 颜色处理 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ColorDetection` | 颜色检测 | 2 | 10 | 18 | 96 (A) | `2.0.0` | 当前元数据描述为：Supports compatibility color anal… | [ColorDetection](./ColorDetection.md) |
| `OperatorType.ColorMeasurement` | 颜色测量 | 2 | 8 | 9 | 96 (A) | `2.0.0` | 当前元数据描述为：Measures Lab delta-E or HSV stati… | [ColorMeasurement](./ColorMeasurement.md) |
