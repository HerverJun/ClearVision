# 算子目录 / Operator Catalog

> 生成时间 / Generated At: `2026-05-09 15:22:06 +08:00`
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
- 平均分 / Average: **82.6**
| 等级 (Level) | 数量 (Count) |
|------|------:|
| A | 52 |
| B | 54 |
| C | 12 |

## 分类索引 / Grouped Index

### AI检测 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.DeepLearning` | 深度学习 | 1 | 6 | 9 | 90 (A) | `1.0.0` | - | [DeepLearning](./operators/DeepLearning.md) |
| `OperatorType.DualModalVoting` | 双模态投票 | 2 | 3 | 6 | 84 (B) | `1.0.0` | - | [DualModalVoting](./operators/DualModalVoting.md) |
| `OperatorType.EdgePairDefect` | 边缘对缺陷 | 3 | 4 | 4 | 86 (A) | `1.0.0` | - | [EdgePairDefect](./operators/EdgePairDefect.md) |
| `OperatorType.SurfaceDefectDetection` | 表面缺陷检测 | 2 | 4 | 5 | 90 (A) | `1.0.0` | - | [SurfaceDefectDetection](./operators/SurfaceDefectDetection.md) |

### 匹配定位 (6)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AkazeFeatureMatch` | AKAZE特征匹配 | 2 | 5 | 5 | 63 (C) | `1.0.0` | - | [AkazeFeatureMatch](./operators/AkazeFeatureMatch.md) |
| `OperatorType.GradientShapeMatch` | 梯度形状匹配 | 2 | 5 | 6 | 73 (B) | `1.0.0` | - | [GradientShapeMatch](./operators/GradientShapeMatch.md) |
| `OperatorType.OrbFeatureMatch` | ORB特征匹配 | 2 | 5 | 7 | 63 (C) | `1.0.0` | - | [OrbFeatureMatch](./operators/OrbFeatureMatch.md) |
| `OperatorType.PyramidShapeMatch` | 金字塔形状匹配 | 2 | 5 | 15 | 73 (B) | `1.0.0` | - | [PyramidShapeMatch](./operators/PyramidShapeMatch.md) |
| `OperatorType.ShapeMatching` | 旋转尺度模板匹配 | 2 | 2 | 10 | 90 (A) | `1.0.0` | - | [ShapeMatching](./operators/ShapeMatching.md) |
| `OperatorType.TemplateMatching` | 模板匹配 | 2 | 6 | 3 | 86 (A) | `1.0.0` | - | [TemplateMatching](./operators/TemplateMatching.md) |

### 变量 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.CycleCounter` | 循环计数器 | 0 | 5 | 2 | 69 (C) | `1.0.0` | - | [CycleCounter](./operators/CycleCounter.md) |
| `OperatorType.VariableIncrement` | 变量递增 | 0 | 5 | 5 | 63 (C) | `1.0.0` | - | [VariableIncrement](./operators/VariableIncrement.md) |
| `OperatorType.VariableRead` | 变量读取 | 0 | 3 | 3 | 63 (C) | `1.0.0` | - | [VariableRead](./operators/VariableRead.md) |
| `OperatorType.VariableWrite` | 变量写入 | 1 | 3 | 4 | 63 (C) | `1.0.0` | - | [VariableWrite](./operators/VariableWrite.md) |

### 图像处理 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AffineTransform` | 仿射变换 | 1 | 2 | 9 | 90 (A) | `1.0.0` | - | [AffineTransform](./operators/AffineTransform.md) |
| `OperatorType.CopyMakeBorder` | 边界填充 | 1 | 1 | 6 | 84 (B) | `1.0.0` | - | [CopyMakeBorder](./operators/CopyMakeBorder.md) |
| `OperatorType.ImageStitching` | 图像拼接 | 2 | 2 | 3 | 84 (B) | `1.0.0` | - | [ImageStitching](./operators/ImageStitching.md) |
| `OperatorType.PolarUnwrap` | 极坐标展开 | 2 | 1 | 8 | 90 (A) | `1.0.0` | - | [PolarUnwrap](./operators/PolarUnwrap.md) |

### 定位 (7)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.BlobLabeling` | 连通域标注 | 2 | 3 | 3 | 90 (A) | `1.0.0` | - | [BlobLabeling](./operators/BlobLabeling.md) |
| `OperatorType.CornerDetection` | 角点检测 | 1 | 3 | 5 | 84 (B) | `1.0.0` | - | [CornerDetection](./operators/CornerDetection.md) |
| `OperatorType.EdgeIntersection` | 边线交点 | 2 | 3 | 0 | 86 (A) | `1.0.0` | - | [EdgeIntersection](./operators/EdgeIntersection.md) |
| `OperatorType.ParallelLineFind` | 平行线查找 | 1 | 6 | 4 | 84 (B) | `1.0.0` | - | [ParallelLineFind](./operators/ParallelLineFind.md) |
| `OperatorType.PositionCorrection` | 位置修正 | 4 | 5 | 3 | 84 (B) | `1.0.0` | - | [PositionCorrection](./operators/PositionCorrection.md) |
| `OperatorType.QuadrilateralFind` | 四边形查找 | 1 | 5 | 4 | 84 (B) | `1.0.0` | - | [QuadrilateralFind](./operators/QuadrilateralFind.md) |
| `OperatorType.RectangleDetection` | 矩形检测 | 1 | 7 | 4 | 84 (B) | `1.0.0` | - | [RectangleDetection](./operators/RectangleDetection.md) |

### 拆分组合 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ImageCompose` | 图像组合 | 4 | 1 | 3 | 84 (B) | `1.0.0` | - | [ImageCompose](./operators/ImageCompose.md) |
| `OperatorType.ImageTiling` | 图像切片 | 1 | 3 | 4 | 84 (B) | `1.0.0` | - | [ImageTiling](./operators/ImageTiling.md) |

### 数据处理 (10)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.Aggregator` | 数据聚合 | 3 | 5 | 1 | 84 (B) | `1.0.0` | - | [Aggregator](./operators/Aggregator.md) |
| `OperatorType.ArrayIndexer` | 数组索引器 | 1 | 3 | 2 | 69 (C) | `1.0.0` | - | [ArrayIndexer](./operators/ArrayIndexer.md) |
| `OperatorType.BoxFilter` | 候选框过滤 (Bounding Box) | 2 | 3 | 9 | 84 (B) | `1.0.0` | - | [BoxFilter](./operators/BoxFilter.md) |
| `OperatorType.BoxNms` | 候选框抑制 | 2 | 3 | 3 | 80 (B) | `1.0.0` | - | [BoxNms](./operators/BoxNms.md) |
| `OperatorType.DatabaseWrite` | 数据库写入 | 1 | 2 | 3 | 90 (A) | `1.0.0` | - | [DatabaseWrite](./operators/DatabaseWrite.md) |
| `OperatorType.JsonExtractor` | JSON 提取器 | 1 | 2 | 1 | 73 (B) | `1.0.0` | - | [JsonExtractor](./operators/JsonExtractor.md) |
| `OperatorType.MathOperation` | 数值计算 | 2 | 2 | 1 | 73 (B) | `1.0.0` | - | [MathOperation](./operators/MathOperation.md) |
| `OperatorType.PointAlignment` | 点位对齐 | 2 | 3 | 2 | 86 (A) | `1.0.0` | - | [PointAlignment](./operators/PointAlignment.md) |
| `OperatorType.PointCorrection` | 点位修正 | 4 | 4 | 3 | 86 (A) | `1.0.0` | - | [PointCorrection](./operators/PointCorrection.md) |
| `OperatorType.UnitConvert` | 单位换算 | 2 | 2 | 4 | 86 (A) | `1.0.0` | - | [UnitConvert](./operators/UnitConvert.md) |

### 标定 (6)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.CalibrationLoader` | 标定加载 | 0 | 4 | 2 | 90 (A) | `1.0.0` | - | [CalibrationLoader](./operators/CalibrationLoader.md) |
| `OperatorType.CameraCalibration` | Camera Calibration | 1 | 2 | 7 | 90 (A) | `1.0.0` | - | [CameraCalibration](./operators/CameraCalibration.md) |
| `OperatorType.CoordinateTransform` | 坐标转换 | 3 | 3 | 4 | 90 (A) | `1.0.0` | - | [CoordinateTransform](./operators/CoordinateTransform.md) |
| `OperatorType.NPointCalibration` | N点标定 | 1 | 3 | 3 | 90 (A) | `1.0.0` | - | [NPointCalibration](./operators/NPointCalibration.md) |
| `OperatorType.TranslationRotationCalibration` | 平移旋转标定 | 1 | 3 | 3 | 90 (A) | `1.0.0` | - | [TranslationRotationCalibration](./operators/TranslationRotationCalibration.md) |
| `OperatorType.Undistort` | Undistort | 2 | 1 | 1 | 81 (B) | `1.0.0` | - | [Undistort](./operators/Undistort.md) |

### 检测 (16)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AngleMeasurement` | 角度测量 | 1 | 2 | 7 | 86 (A) | `1.0.0` | - | [AngleMeasurement](./operators/AngleMeasurement.md) |
| `OperatorType.CaliperTool` | 卡尺工具 | 2 | 7 | 9 | 86 (A) | `1.0.0` | - | [CaliperTool](./operators/CaliperTool.md) |
| `OperatorType.CircleMeasurement` | 圆测量 | 1 | 7 | 7 | 90 (A) | `1.0.0` | - | [CircleMeasurement](./operators/CircleMeasurement.md) |
| `OperatorType.ContourMeasurement` | 轮廓测量 | 1 | 4 | 4 | 84 (B) | `1.0.0` | - | [ContourMeasurement](./operators/ContourMeasurement.md) |
| `OperatorType.GapMeasurement` | 间隙测量 | 2 | 6 | 4 | 86 (A) | `1.0.0` | - | [GapMeasurement](./operators/GapMeasurement.md) |
| `OperatorType.GeoMeasurement` | 几何测量 | 2 | 5 | 2 | 86 (A) | `1.0.0` | - | [GeoMeasurement](./operators/GeoMeasurement.md) |
| `OperatorType.GeometricFitting` | Geometric Fitting | 1 | 2 | 8 | 90 (A) | `1.0.0` | - | [GeometricFitting](./operators/GeometricFitting.md) |
| `OperatorType.GeometricTolerance` | 几何公差 | 1 | 5 | 9 | 86 (A) | `1.0.0` | - | [GeometricTolerance](./operators/GeometricTolerance.md) |
| `OperatorType.HistogramAnalysis` | 直方图分析 | 1 | 7 | 6 | 84 (B) | `1.0.0` | - | [HistogramAnalysis](./operators/HistogramAnalysis.md) |
| `OperatorType.LineLineDistance` | 线线距离 | 2 | 5 | 1 | 86 (A) | `1.0.0` | - | [LineLineDistance](./operators/LineLineDistance.md) |
| `OperatorType.LineMeasurement` | 直线测量 | 1 | 5 | 4 | 86 (A) | `1.0.0` | - | [LineMeasurement](./operators/LineMeasurement.md) |
| `OperatorType.Measurement` | 测量 | 3 | 2 | 5 | 86 (A) | `1.0.0` | - | [Measurement](./operators/Measurement.md) |
| `OperatorType.PixelStatistics` | 像素统计 | 2 | 6 | 5 | 86 (A) | `1.0.0` | - | [PixelStatistics](./operators/PixelStatistics.md) |
| `OperatorType.PointLineDistance` | 点线距离 | 2 | 2 | 0 | 86 (A) | `1.0.0` | - | [PointLineDistance](./operators/PointLineDistance.md) |
| `OperatorType.SharpnessEvaluation` | 清晰度评估 | 1 | 3 | 6 | 86 (A) | `1.0.0` | - | [SharpnessEvaluation](./operators/SharpnessEvaluation.md) |
| `OperatorType.WidthMeasurement` | 宽度测量 | 3 | 4 | 4 | 86 (A) | `1.0.0` | - | [WidthMeasurement](./operators/WidthMeasurement.md) |

### 流程控制 (6)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.Comparator` | 数值比较 | 2 | 2 | 5 | 61 (C) | `1.0.0` | - | [Comparator](./operators/Comparator.md) |
| `OperatorType.ConditionalBranch` | 条件分支 | 1 | 2 | 3 | 80 (B) | `1.0.0` | - | [ConditionalBranch](./operators/ConditionalBranch.md) |
| `OperatorType.Delay` | 延时 | 1 | 2 | 1 | 66 (C) | `1.0.0` | - | [Delay](./operators/Delay.md) |
| `OperatorType.ForEach` | ForEach 循环 | 1 | 1 | 4 | 73 (B) | `1.0.0` | - | [ForEach](./operators/ForEach.md) |
| `OperatorType.ResultJudgment` | 结果判定 | 2 | 3 | 8 | 80 (B) | `1.0.0` | - | [ResultJudgment](./operators/ResultJudgment.md) |
| `OperatorType.TryCatch` | 异常捕获 | 1 | 4 | 3 | 83 (B) | `1.0.0` | - | [TryCatch](./operators/TryCatch.md) |

### 特征提取 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.BlobAnalysis` | Blob分析 | 2 | 4 | 17 | 90 (A) | `1.0.0` | - | [BlobAnalysis](./operators/BlobAnalysis.md) |
| `OperatorType.ContourDetection` | 轮廓检测 | 1 | 3 | 8 | 84 (B) | `1.0.0` | - | [ContourDetection](./operators/ContourDetection.md) |
| `OperatorType.EdgeDetection` | Edge Detection | 1 | 2 | 8 | 90 (A) | `1.0.0` | - | [EdgeDetection](./operators/EdgeDetection.md) |
| `OperatorType.SubpixelEdgeDetection` | Subpixel Edge Detection | 1 | 2 | 5 | 84 (B) | `1.0.0` | - | [SubpixelEdgeDetection](./operators/SubpixelEdgeDetection.md) |

### 识别 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.CodeRecognition` | 条码识别 | 1 | 4 | 2 | 90 (A) | `1.0.0` | - | [CodeRecognition](./operators/CodeRecognition.md) |
| `OperatorType.OcrRecognition` | OCR 识别 | 1 | 2 | 0 | 90 (A) | `1.0.0` | - | [OcrRecognition](./operators/OcrRecognition.md) |

### 辅助 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.Comment` | 注释 | 1 | 2 | 1 | 63 (C) | `1.0.0` | - | [Comment](./operators/Comment.md) |
| `OperatorType.RoiManager` | ROI管理器 | 1 | 2 | 10 | 90 (A) | `1.0.0` | - | [RoiManager](./operators/RoiManager.md) |

### 输出 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ImageSave` | 图像保存 | 1 | 2 | 3 | 90 (A) | `1.0.0` | - | [ImageSave](./operators/ImageSave.md) |
| `OperatorType.ResultOutput` | 结果输出 | 4 | 6 | 2 | 88 (A) | `1.0.0` | - | [ResultOutput](./operators/ResultOutput.md) |

### 通信 (8)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.HttpRequest` | HTTP 请求 | 2 | 3 | 6 | 90 (A) | `1.0.0` | - | [HttpRequest](./operators/HttpRequest.md) |
| `OperatorType.MitsubishiMcCommunication` | 三菱MC通信 | 1 | 2 | 12 | 70 (B) | `1.0.0` | - | [MitsubishiMcCommunication](./operators/MitsubishiMcCommunication.md) |
| `OperatorType.ModbusCommunication` | Modbus通信 | 1 | 2 | 8 | 90 (A) | `1.0.0` | - | [ModbusCommunication](./operators/ModbusCommunication.md) |
| `OperatorType.MqttPublish` | MQTT 发布 | 2 | 1 | 6 | 90 (A) | `1.0.0` | - | [MqttPublish](./operators/MqttPublish.md) |
| `OperatorType.OmronFinsCommunication` | 欧姆龙FINS通信 | 1 | 2 | 12 | 70 (B) | `1.0.0` | - | [OmronFinsCommunication](./operators/OmronFinsCommunication.md) |
| `OperatorType.SerialCommunication` | 串口通信 | 1 | 1 | 8 | 90 (A) | `1.0.0` | - | [SerialCommunication](./operators/SerialCommunication.md) |
| `OperatorType.SiemensS7Communication` | 西门子S7通信 | 1 | 2 | 14 | 70 (B) | `1.0.0` | - | [SiemensS7Communication](./operators/SiemensS7Communication.md) |
| `OperatorType.TcpCommunication` | TCP通信 | 1 | 2 | 6 | 90 (A) | `1.0.0` | - | [TcpCommunication](./operators/TcpCommunication.md) |

### 通用 (4)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.LogicGate` | 逻辑门 | 2 | 1 | 1 | 66 (C) | `1.0.0` | - | [LogicGate](./operators/LogicGate.md) |
| `OperatorType.Statistics` | Statistics | 1 | 7 | 5 | 80 (B) | `1.0.0` | - | [Statistics](./operators/Statistics.md) |
| `OperatorType.StringFormat` | 字符串格式化 | 2 | 1 | 1 | 66 (C) | `1.0.0` | - | [StringFormat](./operators/StringFormat.md) |
| `OperatorType.TypeConvert` | Type Convert | 1 | 6 | 2 | 73 (B) | `1.0.0` | - | [TypeConvert](./operators/TypeConvert.md) |

### 逻辑工具 (5)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.PointSetTool` | 点集工具 | 2 | 4 | 6 | 80 (B) | `1.0.0` | - | [PointSetTool](./operators/PointSetTool.md) |
| `OperatorType.ScriptOperator` | 脚本算子 | 4 | 2 | 3 | 90 (A) | `1.0.0` | - | [ScriptOperator](./operators/ScriptOperator.md) |
| `OperatorType.TextSave` | Text Save | 2 | 2 | 5 | 90 (A) | `1.0.0` | - | [TextSave](./operators/TextSave.md) |
| `OperatorType.TimerStatistics` | 计时统计 | 1 | 4 | 2 | 84 (B) | `1.0.0` | - | [TimerStatistics](./operators/TimerStatistics.md) |
| `OperatorType.TriggerModule` | 触发模块 | 1 | 3 | 3 | 80 (B) | `1.0.0` | - | [TriggerModule](./operators/TriggerModule.md) |

### 采集 (1)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ImageAcquisition` | 图像采集 | 2 | 1 | 6 | 73 (B) | `1.0.0` | - | [ImageAcquisition](./operators/ImageAcquisition.md) |

### 预处理 (23)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.AdaptiveThreshold` | 自适应阈值 | 1 | 1 | 5 | 84 (B) | `1.0.0` | - | [AdaptiveThreshold](./operators/AdaptiveThreshold.md) |
| `OperatorType.BilateralFilter` | 双边滤波 | 1 | 1 | 3 | 90 (A) | `1.0.0` | - | [BilateralFilter](./operators/BilateralFilter.md) |
| `OperatorType.ClaheEnhancement` | CLAHE增强 | 1 | 1 | 5 | 84 (B) | `1.0.0` | - | [ClaheEnhancement](./operators/ClaheEnhancement.md) |
| `OperatorType.ColorConversion` | 颜色空间转换 | 1 | 1 | 2 | 84 (B) | `1.0.0` | - | [ColorConversion](./operators/ColorConversion.md) |
| `OperatorType.Filtering` | Gaussian Blur | 1 | 1 | 4 | 84 (B) | `1.0.0` | Gaussian Blur (OpenCV) | [Filtering](./operators/Filtering.md) |
| `OperatorType.FrameAveraging` | 帧平均 | 1 | 2 | 2 | 84 (B) | `1.0.0` | - | [FrameAveraging](./operators/FrameAveraging.md) |
| `OperatorType.HistogramEqualization` | 直方图均衡化 | 1 | 1 | 3 | 84 (B) | `1.0.0` | - | [HistogramEqualization](./operators/HistogramEqualization.md) |
| `OperatorType.ImageAdd` | 图像加法 | 2 | 1 | 6 | 90 (A) | `1.0.0` | - | [ImageAdd](./operators/ImageAdd.md) |
| `OperatorType.ImageBlend` | 图像融合 | 2 | 1 | 3 | 84 (B) | `1.0.0` | - | [ImageBlend](./operators/ImageBlend.md) |
| `OperatorType.ImageCrop` | 图像裁剪 | 1 | 1 | 4 | 84 (B) | `1.0.0` | - | [ImageCrop](./operators/ImageCrop.md) |
| `OperatorType.ImageDiff` | 图像对比 | 2 | 2 | 0 | 79 (B) | `1.0.0` | - | [ImageDiff](./operators/ImageDiff.md) |
| `OperatorType.ImageNormalize` | 图像归一化 | 1 | 1 | 3 | 84 (B) | `1.0.0` | - | [ImageNormalize](./operators/ImageNormalize.md) |
| `OperatorType.ImageResize` | 图像缩放 | 1 | 1 | 5 | 84 (B) | `1.0.0` | - | [ImageResize](./operators/ImageResize.md) |
| `OperatorType.ImageRotate` | 图像旋转 | 1 | 1 | 5 | 84 (B) | `1.0.0` | - | [ImageRotate](./operators/ImageRotate.md) |
| `OperatorType.ImageSubtract` | Image Subtract | 2 | 4 | 1 | 79 (B) | `1.0.0` | - | [ImageSubtract](./operators/ImageSubtract.md) |
| `OperatorType.LaplacianSharpen` | 拉普拉斯锐化 | 1 | 1 | 3 | 84 (B) | `1.0.0` | - | [LaplacianSharpen](./operators/LaplacianSharpen.md) |
| `OperatorType.MeanFilter` | 均值滤波 | 1 | 1 | 2 | 84 (B) | `1.0.0` | - | [MeanFilter](./operators/MeanFilter.md) |
| `OperatorType.MedianBlur` | 中值滤波 | 1 | 1 | 1 | 84 (B) | `1.0.0` | - | [MedianBlur](./operators/MedianBlur.md) |
| `OperatorType.MorphologicalOperation` | Morphological Operation | 1 | 1 | 7 | 84 (B) | `1.0.0` | - | [MorphologicalOperation](./operators/MorphologicalOperation.md) |
| `OperatorType.Morphology` | Morphology (Legacy) | 1 | 1 | 6 | 84 (B) | `1.0.0` | - | [Morphology](./operators/Morphology.md) |
| `OperatorType.PerspectiveTransform` | 透视变换 | 3 | 1 | 20 | 90 (A) | `1.0.0` | - | [PerspectiveTransform](./operators/PerspectiveTransform.md) |
| `OperatorType.ShadingCorrection` | 光照校正 | 2 | 1 | 2 | 86 (A) | `1.0.0` | - | [ShadingCorrection](./operators/ShadingCorrection.md) |
| `OperatorType.Thresholding` | 二值化 | 1 | 1 | 4 | 84 (B) | `1.0.0` | - | [Thresholding](./operators/Thresholding.md) |

### 颜色处理 (2)
| 枚举 (Enum) | 显示名 (DisplayName) | 输入 | 输出 | 参数 | 质量 (Q) | 版本 (Version) | 算法 (Algorithm) | 文档 |
|------|------|------:|------:|------:|------|------|------|------|
| `OperatorType.ColorDetection` | 颜色检测 | 1 | 4 | 9 | 86 (A) | `1.0.0` | - | [ColorDetection](./operators/ColorDetection.md) |
| `OperatorType.ColorMeasurement` | 颜色测量 | 2 | 8 | 8 | 86 (A) | `1.0.0` | - | [ColorMeasurement](./operators/ColorMeasurement.md) |
