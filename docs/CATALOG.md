# 算子目录 / Operator Catalog

> 生成时间 / Generated At: `2026-06-02 13:55:09 +08:00`
> 算子总数 / Total Operators: **118**

## 分类统计 / Category Summary
| 分类 (Category) | 数量 (Count) | 占比 (Ratio) |
|------|------:|------:|
| AI检测 | 4 | 3.4% |
| 匹配定位 | 6 | 5.1% |
| 变量 | 4 | 3.4% |
| 图像处理 | 4 | 3.4% |
| 定位 | 7 | 5.9% |
| 拆分组合 | 2 | 1.7% |
| 数据处理 | 10 | 8.5% |
| 标定 | 6 | 5.1% |
| 检测 | 16 | 13.6% |
| 流程控制 | 6 | 5.1% |
| 特征提取 | 4 | 3.4% |
| 识别 | 2 | 1.7% |
| 辅助 | 2 | 1.7% |
| 输出 | 2 | 1.7% |
| 通信 | 8 | 6.8% |
| 通用 | 4 | 3.4% |
| 逻辑工具 | 5 | 4.2% |
| 采集 | 1 | 0.8% |
| 预处理 | 23 | 19.5% |
| 颜色处理 | 2 | 1.7% |

## 质量评分 / Quality Score
- 平均分 / Average: **92.2**
| 等级 (Level) | 数量 (Count) |
|------|------:|
| A | 95 |
| B | 22 |
| C | 1 |

## 分类索引 / Grouped Index

### AI检测 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.DeepLearning` | 深度学习 | 1 | 6 | 9 | 100 (A) | `1.0.0` | 该算子用于AI 深度学习推理，支持 YOLOv5/v6/v8/v11 等模型，用于缺… | [DeepLearning](./operators/DeepLearning.md) |
| `OperatorType.DualModalVoting` | 双模态投票 | 2 | 3 | 6 | 84 (B) | `1.0.0` | 当前元数据描述为：Combines deep learning and tradit… | [DualModalVoting](./operators/DualModalVoting.md) |
| `OperatorType.EdgePairDefect` | 边缘对缺陷 | 3 | 4 | 4 | 96 (A) | `1.0.0` | 当前元数据描述为：Checks edge-pair spacing deviatio… | [EdgePairDefect](./operators/EdgePairDefect.md) |
| `OperatorType.SurfaceDefectDetection` | 表面缺陷检测 | 2 | 4 | 5 | 100 (A) | `1.0.0` | 当前元数据描述为：Detects surface defects using gra… | [SurfaceDefectDetection](./operators/SurfaceDefectDetection.md) |

### 匹配定位 (6)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AkazeFeatureMatch` | AKAZE特征匹配 | 2 | 5 | 5 | 73 (B) | `1.0.0` | 当前元数据描述为：AKAZE feature matching with verif… | [AkazeFeatureMatch](./operators/AkazeFeatureMatch.md) |
| `OperatorType.GradientShapeMatch` | 梯度形状匹配 | 2 | 5 | 6 | 83 (B) | `1.0.0` | 该算子用于基于梯度方向特征的形状匹配，支持可选 ROI 搜索。运行时从声明输入端口读… | [GradientShapeMatch](./operators/GradientShapeMatch.md) |
| `OperatorType.OrbFeatureMatch` | ORB特征匹配 | 2 | 5 | 7 | 73 (B) | `1.0.0` | 当前元数据描述为：ORB feature matching with homogra… | [OrbFeatureMatch](./operators/OrbFeatureMatch.md) |
| `OperatorType.PyramidShapeMatch` | 金字塔形状匹配 | 2 | 5 | 15 | 83 (B) | `1.0.0` | 该算子用于基于 LINEMOD 的金字塔模板匹配。运行时从声明输入端口读取数据，按参… | [PyramidShapeMatch](./operators/PyramidShapeMatch.md) |
| `OperatorType.ShapeMatching` | 旋转尺度模板匹配 | 2 | 2 | 10 | 100 (A) | `1.0.0` | 当前元数据描述为：Rotation-scale template matching … | [ShapeMatching](./operators/ShapeMatching.md) |
| `OperatorType.TemplateMatching` | 模板匹配 | 2 | 6 | 3 | 96 (A) | `1.0.0` | 当前元数据描述为：Classic template matching with op… | [TemplateMatching](./operators/TemplateMatching.md) |

### 变量 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.CycleCounter` | 循环计数器 | 0 | 5 | 2 | 79 (B) | `1.0.0` | 该算子用于获取当前循环次数和统计信息。运行时从声明输入端口读取数据，按参数表解析配置… | [CycleCounter](./operators/CycleCounter.md) |
| `OperatorType.VariableIncrement` | 变量递增 | 0 | 5 | 5 | 73 (B) | `1.0.0` | 该算子用于计数器自增/自减，支持重置条件。运行时从声明输入端口读取数据，按参数表解析… | [VariableIncrement](./operators/VariableIncrement.md) |
| `OperatorType.VariableRead` | 变量读取 | 0 | 3 | 3 | 73 (B) | `1.0.0` | 该算子用于从全局变量表读取值。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [VariableRead](./operators/VariableRead.md) |
| `OperatorType.VariableWrite` | 变量写入 | 1 | 3 | 4 | 63 (C) | `1.0.0` | 该算子用于写入值到全局变量表。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [VariableWrite](./operators/VariableWrite.md) |

### 图像处理 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AffineTransform` | 仿射变换 | 1 | 2 | 9 | 100 (A) | `1.0.0` | 当前元数据描述为：Applies 2D affine transform using… | [AffineTransform](./operators/AffineTransform.md) |
| `OperatorType.CopyMakeBorder` | 边界填充 | 1 | 1 | 6 | 94 (A) | `1.0.0` | 当前元数据描述为：Pads image border using OpenCV bo… | [CopyMakeBorder](./operators/CopyMakeBorder.md) |
| `OperatorType.ImageStitching` | 图像拼接 | 2 | 2 | 3 | 94 (A) | `1.0.0` | 当前元数据描述为：Stitches two images into a larger… | [ImageStitching](./operators/ImageStitching.md) |
| `OperatorType.PolarUnwrap` | 极坐标展开 | 2 | 1 | 8 | 100 (A) | `1.0.0` | 当前元数据描述为：Unwraps annular image regions int… | [PolarUnwrap](./operators/PolarUnwrap.md) |

### 定位 (7)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.BlobLabeling` | 连通域标注 | 2 | 3 | 3 | 100 (A) | `1.0.0` | 当前元数据描述为：Classifies connected blobs by geo… | [BlobLabeling](./operators/BlobLabeling.md) |
| `OperatorType.CornerDetection` | 角点检测 | 1 | 3 | 5 | 94 (A) | `1.0.0` | 当前元数据描述为：Detects corner points using Harri… | [CornerDetection](./operators/CornerDetection.md) |
| `OperatorType.EdgeIntersection` | 边线交点 | 2 | 3 | 0 | 96 (A) | `1.0.0` | 当前元数据描述为：Computes line intersection and an… | [EdgeIntersection](./operators/EdgeIntersection.md) |
| `OperatorType.ParallelLineFind` | 平行线查找 | 1 | 6 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Finds best pair of near-parallel … | [ParallelLineFind](./operators/ParallelLineFind.md) |
| `OperatorType.PositionCorrection` | 位置修正 | 4 | 5 | 3 | 94 (A) | `1.0.0` | 当前元数据描述为：Pixel-space ROI offset tool. Use … | [PositionCorrection](./operators/PositionCorrection.md) |
| `OperatorType.QuadrilateralFind` | 四边形查找 | 1 | 5 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Finds quadrilateral contours with… | [QuadrilateralFind](./operators/QuadrilateralFind.md) |
| `OperatorType.RectangleDetection` | 矩形检测 | 1 | 7 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Detects rectangular/quadrilateral… | [RectangleDetection](./operators/RectangleDetection.md) |

### 拆分组合 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ImageCompose` | 图像组合 | 4 | 1 | 3 | 94 (A) | `1.0.0` | 当前元数据描述为：Composes multiple images by conca… | [ImageCompose](./operators/ImageCompose.md) |
| `OperatorType.ImageTiling` | 图像切片 | 1 | 3 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Splits an image into tiled region… | [ImageTiling](./operators/ImageTiling.md) |

### 数据处理 (10)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.Aggregator` | 数据聚合 | 3 | 5 | 1 | 94 (A) | `1.0.0` | 该算子用于将多路输入数据合并为列表，并提取极值与均值。运行时从声明输入端口读取数据，… | [Aggregator](./operators/Aggregator.md) |
| `OperatorType.ArrayIndexer` | 数组索引器 | 1 | 3 | 2 | 79 (B) | `1.0.0` | 该算子用于从列表中按索引或条件提取元素。运行时从声明输入端口读取数据，按参数表解析配… | [ArrayIndexer](./operators/ArrayIndexer.md) |
| `OperatorType.BoxFilter` | 候选框过滤 (Bounding Box) | 2 | 3 | 9 | 94 (A) | `1.0.0` | 当前元数据描述为：Filters detections by area, class… | [BoxFilter](./operators/BoxFilter.md) |
| `OperatorType.BoxNms` | 候选框抑制 | 2 | 3 | 3 | 90 (A) | `1.0.0` | 当前元数据描述为：Runs non-maximum suppression on d… | [BoxNms](./operators/BoxNms.md) |
| `OperatorType.DatabaseWrite` | 数据库写入 | 1 | 2 | 3 | 100 (A) | `1.0.0` | 该算子用于将输入数据写入 SQLite / SQL Server / MySQL 表… | [DatabaseWrite](./operators/DatabaseWrite.md) |
| `OperatorType.JsonExtractor` | JSON 提取器 | 1 | 2 | 1 | 83 (B) | `1.0.0` | 该算子用于按 JSONPath 从字符串中提取字段。运行时从声明输入端口读取数据，按… | [JsonExtractor](./operators/JsonExtractor.md) |
| `OperatorType.MathOperation` | 数值计算 | 2 | 2 | 1 | 83 (B) | `1.0.0` | 该算子用于支持加减乘除、取绝对值、开方等常用运算。运行时从声明输入端口读取数据，按参… | [MathOperation](./operators/MathOperation.md) |
| `OperatorType.PointAlignment` | 点位对齐 | 2 | 3 | 2 | 96 (A) | `1.0.0` | 当前元数据描述为：Pixel-space alignment helper for … | [PointAlignment](./operators/PointAlignment.md) |
| `OperatorType.PointCorrection` | 点位修正 | 4 | 4 | 3 | 96 (A) | `1.0.0` | 当前元数据描述为：Pixel-space rigid correction help… | [PointCorrection](./operators/PointCorrection.md) |
| `OperatorType.UnitConvert` | 单位换算 | 2 | 2 | 4 | 96 (A) | `1.0.0` | 当前元数据描述为：Converts value between pixel, mm,… | [UnitConvert](./operators/UnitConvert.md) |

### 标定 (6)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.CalibrationLoader` | 标定加载 | 0 | 4 | 2 | 100 (A) | `1.0.0` | 当前元数据描述为：Loads CalibrationBundleV2 JSON an… | [CalibrationLoader](./operators/CalibrationLoader.md) |
| `OperatorType.CameraCalibration` | Camera Calibration | 1 | 2 | 7 | 100 (A) | `1.0.0` | 当前元数据描述为：Calibrates camera intrinsics from… | [CameraCalibration](./operators/CameraCalibration.md) |
| `OperatorType.CoordinateTransform` | 坐标转换 | 3 | 3 | 4 | 90 (A) | `1.0.0` | 当前元数据描述为：Converts pixel coordinates to phy… | [CoordinateTransform](./operators/CoordinateTransform.md) |
| `OperatorType.NPointCalibration` | N点标定 | 1 | 3 | 3 | 100 (A) | `1.0.0` | 当前元数据描述为：Builds robust affine or homograph… | [NPointCalibration](./operators/NPointCalibration.md) |
| `OperatorType.TranslationRotationCalibration` | 平移旋转标定 | 1 | 3 | 3 | 100 (A) | `1.0.0` | 当前元数据描述为：Fits robust 2D rigid or similarit… | [TranslationRotationCalibration](./operators/TranslationRotationCalibration.md) |
| `OperatorType.Undistort` | Undistort | 2 | 1 | 1 | 91 (A) | `1.0.0` | 当前元数据描述为：Correct lens distortion using cal… | [Undistort](./operators/Undistort.md) |

### 检测 (16)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AngleMeasurement` | 角度测量 | 1 | 2 | 7 | 96 (A) | `1.0.0` | 当前元数据描述为：Measures angle from three points … | [AngleMeasurement](./operators/AngleMeasurement.md) |
| `OperatorType.CaliperTool` | 卡尺工具 | 2 | 7 | 9 | 96 (A) | `1.0.0` | 当前元数据描述为：Detects edge pairs along a scan l… | [CaliperTool](./operators/CaliperTool.md) |
| `OperatorType.CircleMeasurement` | 圆测量 | 1 | 7 | 7 | 90 (A) | `1.0.0` | 该算子用于霍夫变换检测圆形并测量半径与圆心坐标，适用于孔径检测和圆形定位。运行时从声… | [CircleMeasurement](./operators/CircleMeasurement.md) |
| `OperatorType.ContourMeasurement` | 轮廓测量 | 1 | 4 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Measures contour area, perimeter,… | [ContourMeasurement](./operators/ContourMeasurement.md) |
| `OperatorType.GapMeasurement` | 间隙测量 | 2 | 6 | 4 | 96 (A) | `1.0.0` | 当前元数据描述为：Measures spacing using points or … | [GapMeasurement](./operators/GapMeasurement.md) |
| `OperatorType.GeoMeasurement` | 几何测量 | 2 | 5 | 2 | 96 (A) | `1.0.0` | 当前元数据描述为：General geometry measurement betw… | [GeoMeasurement](./operators/GeoMeasurement.md) |
| `OperatorType.GeometricFitting` | Geometric Fitting | 1 | 2 | 8 | 100 (A) | `1.0.0` | 当前元数据描述为：Fits line, circle or ellipse from… | [GeometricFitting](./operators/GeometricFitting.md) |
| `OperatorType.GeometricTolerance` | 几何公差 | 1 | 5 | 9 | 96 (A) | `1.0.0` | 当前元数据描述为：Evaluates a constrained 2D GD&T s… | [GeometricTolerance](./operators/GeometricTolerance.md) |
| `OperatorType.HistogramAnalysis` | 直方图分析 | 1 | 7 | 6 | 94 (A) | `1.0.0` | 当前元数据描述为：Computes histogram and intensity-… | [HistogramAnalysis](./operators/HistogramAnalysis.md) |
| `OperatorType.LineLineDistance` | 线线距离 | 2 | 5 | 1 | 96 (A) | `1.0.0` | 当前元数据描述为：Computes distance and angle betwe… | [LineLineDistance](./operators/LineLineDistance.md) |
| `OperatorType.LineMeasurement` | 直线测量 | 1 | 5 | 4 | 96 (A) | `1.0.0` | 当前元数据描述为：Detects line features and reports… | [LineMeasurement](./operators/LineMeasurement.md) |
| `OperatorType.Measurement` | 测量 | 3 | 2 | 5 | 96 (A) | `1.0.0` | 该算子用于两点/水平/垂直距离测量，支持参数坐标与 PointA/PointB 输入… | [Measurement](./operators/Measurement.md) |
| `OperatorType.PixelStatistics` | 像素统计 | 2 | 6 | 5 | 96 (A) | `1.0.0` | 当前元数据描述为：Computes ROI/masked pixel-level s… | [PixelStatistics](./operators/PixelStatistics.md) |
| `OperatorType.PointLineDistance` | 点线距离 | 2 | 2 | 0 | 96 (A) | `1.0.0` | 当前元数据描述为：Computes distance from a point to… | [PointLineDistance](./operators/PointLineDistance.md) |
| `OperatorType.SharpnessEvaluation` | 清晰度评估 | 1 | 3 | 6 | 96 (A) | `1.0.0` | 当前元数据描述为：Evaluates focus quality of an ima… | [SharpnessEvaluation](./operators/SharpnessEvaluation.md) |
| `OperatorType.WidthMeasurement` | 宽度测量 | 3 | 4 | 4 | 96 (A) | `1.0.0` | 当前元数据描述为：Measures width between parallel e… | [WidthMeasurement](./operators/WidthMeasurement.md) |

### 流程控制 (6)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.Comparator` | 数值比较 | 2 | 2 | 5 | 71 (B) | `1.0.0` | 该算子用于比较两个数值的大小关系，输出布尔判定结果与差值。运行时从声明输入端口读取数… | [Comparator](./operators/Comparator.md) |
| `OperatorType.ConditionalBranch` | 条件分支 | 1 | 2 | 3 | 90 (A) | `1.0.0` | 该算子用于根据数值/字符串/布尔条件执行 True/False 两路分支，常用于 O… | [ConditionalBranch](./operators/ConditionalBranch.md) |
| `OperatorType.Delay` | 延时 | 1 | 2 | 1 | 76 (B) | `1.0.0` | 该算子用于等待指定时间后继续执行，常用于通信前等待下位机就绪。运行时从声明输入端口读… | [Delay](./operators/Delay.md) |
| `OperatorType.ForEach` | ForEach 循环 | 1 | 1 | 4 | 83 (B) | `1.0.0` | 该算子用于对集合中的每个元素执行子图。运行时从声明输入端口读取数据，按参数表解析配置… | [ForEach](./operators/ForEach.md) |
| `OperatorType.ResultJudgment` | 结果判定 | 2 | 3 | 8 | 90 (A) | `1.0.0` | 当前元数据描述为：Generic business judgment with nu… | [ResultJudgment](./operators/ResultJudgment.md) |
| `OperatorType.TryCatch` | 异常捕获 | 1 | 4 | 3 | 93 (A) | `1.0.0` | 该算子用于Try-Catch 流程控制。运行时从声明输入端口读取数据，按参数表解析配… | [TryCatch](./operators/TryCatch.md) |

### 特征提取 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.BlobAnalysis` | Blob分析 | 2 | 4 | 17 | 100 (A) | `1.0.0` | 该算子用于连通区域分析。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果… | [BlobAnalysis](./operators/BlobAnalysis.md) |
| `OperatorType.ContourDetection` | 轮廓检测 | 1 | 3 | 8 | 94 (A) | `1.0.0` | 该算子用于查找图像轮廓，提取边缘点集和层次关系，供后续测量和拟合使用。运行时从声明输… | [ContourDetection](./operators/ContourDetection.md) |
| `OperatorType.EdgeDetection` | Edge Detection | 1 | 2 | 8 | 100 (A) | `1.0.0` | 当前元数据描述为：Detects edges with Canny and opti… | [EdgeDetection](./operators/EdgeDetection.md) |
| `OperatorType.SubpixelEdgeDetection` | Subpixel Edge Detection | 1 | 2 | 5 | 94 (A) | `1.0.0` | 当前元数据描述为：Non-industrial reference subpixel… | [SubpixelEdgeDetection](./operators/SubpixelEdgeDetection.md) |

### 识别 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.CodeRecognition` | 条码识别 | 1 | 4 | 2 | 100 (A) | `1.0.0` | 该算子用于一维码/二维码识别，支持 QR、Code128、DataMatrix 等多… | [CodeRecognition](./operators/CodeRecognition.md) |
| `OperatorType.OcrRecognition` | OCR 识别 | 1 | 2 | 0 | 100 (A) | `1.0.0` | 该算子用于识别图像中的文本内容。运行时从声明输入端口读取数据，按参数表解析配置，并把… | [OcrRecognition](./operators/OcrRecognition.md) |

### 辅助 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.Comment` | 注释 | 1 | 2 | 1 | 73 (B) | `1.0.0` | 该算子用于在工作流中添加说明文本，不影响数据流，仅用于标注设计意图。运行时从声明输入… | [Comment](./operators/Comment.md) |
| `OperatorType.RoiManager` | ROI管理器 | 1 | 2 | 10 | 100 (A) | `1.0.0` | 该算子用于矩形/圆形/多边形区域选择。运行时从声明输入端口读取数据，按参数表解析配置… | [RoiManager](./operators/RoiManager.md) |

### 输出 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ImageSave` | 图像保存 | 1 | 2 | 3 | 100 (A) | `1.0.0` | 该算子用于保存检测图像到本地硬盘。运行时从声明输入端口读取数据，按参数表解析配置，并… | [ImageSave](./operators/ImageSave.md) |
| `OperatorType.ResultOutput` | 结果输出 | 4 | 6 | 2 | 98 (A) | `1.0.0` | 该算子用于汇总检测结果并输出，支持 JSON/CSV/Text 格式，可选保存到文件… | [ResultOutput](./operators/ResultOutput.md) |

### 通信 (8)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.HttpRequest` | HTTP 请求 | 2 | 3 | 6 | 100 (A) | `1.0.0` | 该算子用于调用外部 REST API。运行时从声明输入端口读取数据，按参数表解析配置… | [HttpRequest](./operators/HttpRequest.md) |
| `OperatorType.MitsubishiMcCommunication` | 三菱MC通信 | 1 | 2 | 12 | 80 (B) | `1.0.0` | 当前元数据描述为：Mitsubishi MC protocol PLC read/w… | [MitsubishiMcCommunication](./operators/MitsubishiMcCommunication.md) |
| `OperatorType.ModbusCommunication` | Modbus通信 | 1 | 2 | 8 | 100 (A) | `1.0.0` | 当前元数据描述为：Industrial Modbus TCP communicati… | [ModbusCommunication](./operators/ModbusCommunication.md) |
| `OperatorType.MqttPublish` | MQTT 发布 | 2 | 1 | 6 | 100 (A) | `1.0.0` | 当前元数据描述为：Publishes inspection data to MQTT… | [MqttPublish](./operators/MqttPublish.md) |
| `OperatorType.OmronFinsCommunication` | 欧姆龙FINS通信 | 1 | 2 | 12 | 80 (B) | `1.0.0` | 该算子用于欧姆龙FINS/TCP协议PLC读写通信（CP1H/CJ2M/NJ/NX）… | [OmronFinsCommunication](./operators/OmronFinsCommunication.md) |
| `OperatorType.SerialCommunication` | 串口通信 | 1 | 1 | 8 | 100 (A) | `1.0.0` | 该算子用于RS-232/485 串口数据收发。运行时从声明输入端口读取数据，按参数表… | [SerialCommunication](./operators/SerialCommunication.md) |
| `OperatorType.SiemensS7Communication` | 西门子S7通信 | 1 | 2 | 14 | 80 (B) | `1.0.0` | 该算子用于西门子S7系列PLC读写通信（S7-200/300/400/1200/15… | [SiemensS7Communication](./operators/SiemensS7Communication.md) |
| `OperatorType.TcpCommunication` | TCP通信 | 1 | 2 | 6 | 100 (A) | `1.0.0` | 该算子用于TCP/IP网络通信。运行时从声明输入端口读取数据，按参数表解析配置，并把… | [TcpCommunication](./operators/TcpCommunication.md) |

### 通用 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.LogicGate` | 逻辑门 | 2 | 1 | 1 | 76 (B) | `1.0.0` | 该算子用于布尔逻辑运算 (AND, OR, NOT, XOR, NAND, NOR)… | [LogicGate](./operators/LogicGate.md) |
| `OperatorType.Statistics` | Statistics | 1 | 7 | 5 | 90 (A) | `1.0.0` | 当前元数据描述为：Computes Mean/StdDev/Cpk statisti… | [Statistics](./operators/Statistics.md) |
| `OperatorType.StringFormat` | 字符串格式化 | 2 | 1 | 1 | 76 (B) | `1.0.0` | 该算子用于按模板生成字符串。运行时从声明输入端口读取数据，按参数表解析配置，并把处理… | [StringFormat](./operators/StringFormat.md) |
| `OperatorType.TypeConvert` | Type Convert | 1 | 6 | 2 | 83 (B) | `1.0.0` | 当前元数据描述为：Converts input data across String… | [TypeConvert](./operators/TypeConvert.md) |

### 逻辑工具 (5)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.PointSetTool` | 点集工具 | 2 | 4 | 6 | 90 (A) | `1.0.0` | 当前元数据描述为：Merges/sorts/filters point lists … | [PointSetTool](./operators/PointSetTool.md) |
| `OperatorType.ScriptOperator` | 脚本算子 | 4 | 2 | 3 | 90 (A) | `1.0.0` | 当前元数据描述为：Runs user-defined expression or s… | [ScriptOperator](./operators/ScriptOperator.md) |
| `OperatorType.TextSave` | Text Save | 2 | 2 | 5 | 100 (A) | `1.0.0` | 当前元数据描述为：Saves text or structured data to … | [TextSave](./operators/TextSave.md) |
| `OperatorType.TimerStatistics` | 计时统计 | 1 | 4 | 2 | 94 (A) | `1.0.0` | 当前元数据描述为：Measures elapsed and cycle time s… | [TimerStatistics](./operators/TimerStatistics.md) |
| `OperatorType.TriggerModule` | 触发模块 | 1 | 3 | 3 | 90 (A) | `1.0.0` | 当前元数据描述为：Generates software, timer, or ext… | [TriggerModule](./operators/TriggerModule.md) |

### 采集 (1)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ImageAcquisition` | 图像采集 | 2 | 1 | 6 | 83 (B) | `1.0.0` | 该算子用于从文件或相机采集图像。运行时从声明输入端口读取数据，按参数表解析配置，并把… | [ImageAcquisition](./operators/ImageAcquisition.md) |

### 预处理 (23)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AdaptiveThreshold` | 自适应阈值 | 1 | 1 | 5 | 94 (A) | `1.0.0` | 当前元数据描述为：Local mean or Gaussian adaptive t… | [AdaptiveThreshold](./operators/AdaptiveThreshold.md) |
| `OperatorType.BilateralFilter` | 双边滤波 | 1 | 1 | 3 | 100 (A) | `1.0.0` | 该算子用于边缘保留的平滑滤波。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [BilateralFilter](./operators/BilateralFilter.md) |
| `OperatorType.ClaheEnhancement` | CLAHE增强 | 1 | 1 | 5 | 94 (A) | `1.0.0` | 当前元数据描述为：Adaptive histogram equalization f… | [ClaheEnhancement](./operators/ClaheEnhancement.md) |
| `OperatorType.ColorConversion` | 颜色空间转换 | 1 | 1 | 2 | 94 (A) | `1.0.0` | 该算子用于BGR/GRAY/HSV/Lab/YUV等颜色空间转换。运行时从声明输入端… | [ColorConversion](./operators/ColorConversion.md) |
| `OperatorType.Filtering` | Gaussian Blur | 1 | 1 | 4 | 94 (A) | `1.0.0` | Gaussian Blur (OpenCV) | [Filtering](./operators/Filtering.md) |
| `OperatorType.FrameAveraging` | 帧平均 | 1 | 2 | 2 | 94 (A) | `1.0.0` | 当前元数据描述为：Averages multi-frame input to red… | [FrameAveraging](./operators/FrameAveraging.md) |
| `OperatorType.HistogramEqualization` | 直方图均衡化 | 1 | 1 | 3 | 94 (A) | `1.0.0` | 当前元数据描述为：Supports global histogram equaliz… | [HistogramEqualization](./operators/HistogramEqualization.md) |
| `OperatorType.ImageAdd` | 图像加法 | 2 | 1 | 6 | 100 (A) | `1.0.0` | 该算子用于两幅图像叠加/合并。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [ImageAdd](./operators/ImageAdd.md) |
| `OperatorType.ImageBlend` | 图像融合 | 2 | 1 | 3 | 94 (A) | `1.0.0` | 该算子用于加权混合/透明叠加。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [ImageBlend](./operators/ImageBlend.md) |
| `OperatorType.ImageCrop` | 图像裁剪 | 1 | 1 | 4 | 94 (A) | `1.0.0` | 该算子用于ROI区域提取。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结… | [ImageCrop](./operators/ImageCrop.md) |
| `OperatorType.ImageDiff` | 图像对比 | 2 | 2 | 0 | 89 (A) | `1.0.0` | 该算子用于分析两幅图像的差异。运行时从声明输入端口读取数据，按参数表解析配置，并把处… | [ImageDiff](./operators/ImageDiff.md) |
| `OperatorType.ImageNormalize` | 图像归一化 | 1 | 1 | 3 | 94 (A) | `1.0.0` | 当前元数据描述为：Normalizes pixel distribution for… | [ImageNormalize](./operators/ImageNormalize.md) |
| `OperatorType.ImageResize` | 图像缩放 | 1 | 1 | 5 | 94 (A) | `1.0.0` | 该算子用于调整图像尺寸。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果… | [ImageResize](./operators/ImageResize.md) |
| `OperatorType.ImageRotate` | 图像旋转 | 1 | 1 | 5 | 94 (A) | `1.0.0` | 该算子用于任意角度旋转。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果… | [ImageRotate](./operators/ImageRotate.md) |
| `OperatorType.ImageSubtract` | Image Subtract | 2 | 4 | 1 | 89 (A) | `1.0.0` | 当前元数据描述为：Computes subtraction or absolute … | [ImageSubtract](./operators/ImageSubtract.md) |
| `OperatorType.LaplacianSharpen` | 拉普拉斯锐化 | 1 | 1 | 3 | 94 (A) | `1.0.0` | 该算子用于基于拉普拉斯算子的边缘增强。运行时从声明输入端口读取数据，按参数表解析配置… | [LaplacianSharpen](./operators/LaplacianSharpen.md) |
| `OperatorType.MeanFilter` | 均值滤波 | 1 | 1 | 2 | 94 (A) | `1.0.0` | 当前元数据描述为：Applies mean (box blur) filtering… | [MeanFilter](./operators/MeanFilter.md) |
| `OperatorType.MedianBlur` | 中值滤波 | 1 | 1 | 1 | 94 (A) | `1.0.0` | 该算子用于有效去除椒盐噪声同时保留边缘。运行时从声明输入端口读取数据，按参数表解析配… | [MedianBlur](./operators/MedianBlur.md) |
| `OperatorType.MorphologicalOperation` | Morphological Operation | 1 | 1 | 7 | 94 (A) | `1.0.0` | 当前元数据描述为：Erode, Dilate, Open, Close, Gradi… | [MorphologicalOperation](./operators/MorphologicalOperation.md) |
| `OperatorType.Morphology` | Morphology (Legacy) | 1 | 1 | 6 | 94 (A) | `1.0.0` | 当前元数据描述为：Legacy image morphology node. Use… | [Morphology](./operators/Morphology.md) |
| `OperatorType.PerspectiveTransform` | 透视变换 | 3 | 1 | 20 | 100 (A) | `1.0.0` | 该算子用于四边形透视校正。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结… | [PerspectiveTransform](./operators/PerspectiveTransform.md) |
| `OperatorType.ShadingCorrection` | 光照校正 | 2 | 1 | 2 | 96 (A) | `1.0.0` | 当前元数据描述为：Corrects uneven illumination by b… | [ShadingCorrection](./operators/ShadingCorrection.md) |
| `OperatorType.Thresholding` | 二值化 | 1 | 1 | 4 | 94 (A) | `1.0.0` | 当前元数据描述为：Global thresholding with optional… | [Thresholding](./operators/Thresholding.md) |

### 颜色处理 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ColorDetection` | 颜色检测 | 1 | 4 | 9 | 96 (A) | `1.0.0` | 当前元数据描述为：Supports compatibility color anal… | [ColorDetection](./operators/ColorDetection.md) |
| `OperatorType.ColorMeasurement` | 颜色测量 | 2 | 8 | 8 | 96 (A) | `1.0.0` | 当前元数据描述为：Measures Lab delta-E or HSV stati… | [ColorMeasurement](./operators/ColorMeasurement.md) |
