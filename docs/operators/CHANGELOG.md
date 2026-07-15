# 算子版本变更记录 / Operator Version Changelog

> 生成时间 / Generated At: `2026-07-16 01:07:27 +08:00`
> 算子总数 / Total Operators: **158**

## 当前版本快照 / Current Snapshot
| 枚举 (Enum) | 显示名 (DisplayName) | 分类 ID | 分类 (Category) | 生命周期 | 版本 (Version) |
|------|------|------|------|------|------|
| `OperatorType.ImageAcquisition` | 图像采集 | `Acquisition` | 采集 | `Stable` | `1.0.0` |
| `OperatorType.AffineTransform` | 仿射变换 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.BilateralFilter` | 双边滤波 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.1.0` |
| `OperatorType.ClaheEnhancement` | CLAHE增强 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.ColorConversion` | 颜色空间转换 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.CopyMakeBorder` | 边界填充 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.FFT1D` | 信号/图像傅里叶变换（FFT） | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.Filtering` | 滤波 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.2.0` |
| `OperatorType.FrameAveraging` | 帧平均 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.FrequencyFilter` | 频域滤波 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.HistogramEqualization` | 直方图均衡化 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.ImageAdd` | 图像加法 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.ImageBlend` | 图像融合 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.ImageCompose` | 图像组合 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.ImageCrop` | 图像裁剪 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.ImageNormalize` | 图像归一化 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.3` |
| `OperatorType.ImageResize` | 图像缩放 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.ImageRotate` | 图像旋转 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.ImageStitching` | 图像拼接 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.ImageSubtract` | 图像减法 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.ImageTiling` | 图像切片 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.InverseFFT1D` | 信号/图像逆傅里叶变换（IFFT） | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.LaplacianSharpen` | 拉普拉斯锐化 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.3` |
| `OperatorType.MeanFilter` | 均值滤波 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.1.0` |
| `OperatorType.MedianBlur` | 中值滤波 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.1.0` |
| `OperatorType.PerspectiveTransform` | 透视变换 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.PolarUnwrap` | 极坐标展开 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.RoiManager` | ROI裁剪与掩膜 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.0` |
| `OperatorType.ShadingCorrection` | 光照校正 | `ImagePreprocessing` | 图像预处理 | `Stable` | `1.0.2` |
| `OperatorType.AdaptiveThreshold` | 自适应阈值 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.0` |
| `OperatorType.BinaryImageToRegion` | 二值图转区域 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.1.0` |
| `OperatorType.BlobAnalysis` | Blob分析 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.2.1` |
| `OperatorType.DistanceTransform` | 距离变换 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.1` |
| `OperatorType.MorphologicalOperation` | 形态学操作 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.0` |
| `OperatorType.Morphology` | 形态学（旧版） | `SegmentationAndRegion` | 分割与区域 | `Legacy` | `1.0.0` |
| `OperatorType.RectangleRegion` | 矩形框定义 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.1` |
| `OperatorType.RegionClosing` | 区域闭运算 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.2` |
| `OperatorType.RegionComplement` | 区域补集 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.2` |
| `OperatorType.RegionDifference` | 区域差集 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.2` |
| `OperatorType.RegionDilation` | 区域膨胀 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.2` |
| `OperatorType.RegionErosion` | 区域腐蚀 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.2` |
| `OperatorType.RegionIntersection` | 区域交集 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.2` |
| `OperatorType.RegionOpening` | 区域开运算 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.2` |
| `OperatorType.RegionSkeleton` | 区域骨架化 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.2` |
| `OperatorType.RegionUnion` | 区域并集 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.0.2` |
| `OperatorType.Thresholding` | 全局阈值处理 | `SegmentationAndRegion` | 分割与区域 | `Stable` | `1.1.0` |
| `OperatorType.BlobLabeling` | Blob分类标注 | `FeatureExtraction` | 特征提取 | `Stable` | `1.0.1` |
| `OperatorType.CodeRecognition` | 条码识别 | `FeatureExtraction` | 特征提取 | `Stable` | `1.0.0` |
| `OperatorType.ColorDetection` | 颜色分析 | `FeatureExtraction` | 特征提取 | `Experimental` | `2.0.1` |
| `OperatorType.ContourDetection` | 轮廓检测 | `FeatureExtraction` | 特征提取 | `Stable` | `1.0.0` |
| `OperatorType.CornerDetection` | 角点检测 | `FeatureExtraction` | 特征提取 | `Stable` | `1.0.0` |
| `OperatorType.EdgeDetection` | 边缘检测 | `FeatureExtraction` | 特征提取 | `Stable` | `1.0.0` |
| `OperatorType.GlcmTexture` | GLCM纹理特征 | `FeatureExtraction` | 特征提取 | `Stable` | `1.0.2` |
| `OperatorType.HistogramAnalysis` | 直方图分析 | `FeatureExtraction` | 特征提取 | `Stable` | `1.1.0` |
| `OperatorType.ImageDiff` | 图像差异率分析 | `FeatureExtraction` | 特征提取 | `Stable` | `1.0.1` |
| `OperatorType.LawsTextureFilter` | Laws纹理滤波 | `FeatureExtraction` | 特征提取 | `Stable` | `1.0.1` |
| `OperatorType.PixelStatistics` | 像素统计 | `FeatureExtraction` | 特征提取 | `Stable` | `1.0.1` |
| `OperatorType.SharpnessEvaluation` | 清晰度评估 | `FeatureExtraction` | 特征提取 | `Stable` | `1.1.0` |
| `OperatorType.SubpixelEdgeDetection` | 亚像素边缘 | `FeatureExtraction` | 特征提取 | `Reference` | `1.0.0` |
| `OperatorType.AkazeFeatureMatch` | AKAZE特征匹配 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.0.0` |
| `OperatorType.ContourExtrema` | 轮廓极值 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.0.1` |
| `OperatorType.EdgeIntersection` | 边线交点 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.0.0` |
| `OperatorType.GradientShapeMatch` | 梯度形状匹配 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.1.0` |
| `OperatorType.LocalDeformableMatching` | 局部可变形匹配 | `MatchingAndLocalization` | 匹配与定位 | `Experimental` | `1.1.1` |
| `OperatorType.OrbFeatureMatch` | ORB特征匹配 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.0.0` |
| `OperatorType.ParallelLineFind` | 平行线查找 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.0.0` |
| `OperatorType.PlanarMatching` | 平面特征匹配 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.1.3` |
| `OperatorType.PointAlignment` | 点位偏差计算 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.0.4` |
| `OperatorType.PointCorrection` | 点位刚性补偿 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.0.4` |
| `OperatorType.PositionCorrection` | ROI位姿补偿（像素） | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.0.3` |
| `OperatorType.PyramidShapeMatch` | 金字塔形状匹配 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.0.0` |
| `OperatorType.QuadrilateralFind` | 四边形查找 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.0.0` |
| `OperatorType.RectangleDetection` | 矩形检测 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.0.0` |
| `OperatorType.RoiTransform` | ROI位姿变换 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.0.2` |
| `OperatorType.ShapeMatching` | 旋转尺度模板匹配 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.2.0` |
| `OperatorType.TemplateMatching` | 模板匹配 | `MatchingAndLocalization` | 匹配与定位 | `Stable` | `1.2.0` |
| `OperatorType.DetectionSequenceJudge` | 检测顺序判定 | `DefectDetection` | 缺陷检测 | `Experimental` | `1.0.1` |
| `OperatorType.DualModalVoting` | 双模态投票 | `DefectDetection` | 缺陷检测 | `Stable` | `1.0.0` |
| `OperatorType.EdgePairDefect` | 边缘间距缺陷检测 | `DefectDetection` | 缺陷检测 | `Stable` | `1.0.1` |
| `OperatorType.SurfaceDefectDetection` | 表面缺陷检测 | `DefectDetection` | 缺陷检测 | `Experimental` | `2.0.1` |
| `OperatorType.AngleMeasurement` | 角度测量 | `Measurement` | 测量 | `Stable` | `1.0.0` |
| `OperatorType.ArcCaliper` | 圆弧卡尺 | `Measurement` | 测量 | `Stable` | `1.0.1` |
| `OperatorType.CaliperTool` | 卡尺工具 | `Measurement` | 测量 | `Stable` | `1.2.1` |
| `OperatorType.CircleMeasurement` | 圆测量 | `Measurement` | 测量 | `Stable` | `1.2.0` |
| `OperatorType.ColorMeasurement` | 颜色测量 | `Measurement` | 测量 | `Stable` | `2.0.0` |
| `OperatorType.ContourMeasurement` | 轮廓测量 | `Measurement` | 测量 | `Stable` | `1.0.0` |
| `OperatorType.GapMeasurement` | 间隙测量 | `Measurement` | 测量 | `Stable` | `1.0.0` |
| `OperatorType.GeoMeasurement` | 几何测量 | `Measurement` | 测量 | `Stable` | `1.0.0` |
| `OperatorType.GeometricFitting` | 几何拟合 | `Measurement` | 测量 | `Stable` | `1.0.0` |
| `OperatorType.GeometricTolerance` | 二维几何公差判定 | `Measurement` | 测量 | `Stable` | `1.0.1` |
| `OperatorType.LineLineDistance` | 线线距离 | `Measurement` | 测量 | `Stable` | `1.0.0` |
| `OperatorType.LineMeasurement` | 直线测量 | `Measurement` | 测量 | `Stable` | `1.2.1` |
| `OperatorType.Measurement` | 测量 | `Measurement` | 测量 | `Stable` | `1.1.0` |
| `OperatorType.MinEnclosingGeometry` | 最小外接几何体 | `Measurement` | 测量 | `Stable` | `1.0.1` |
| `OperatorType.PhaseClosure` | 相位解缠绕 | `Measurement` | 测量 | `Stable` | `1.0.1` |
| `OperatorType.PointLineDistance` | 点线距离 | `Measurement` | 测量 | `Stable` | `1.0.0` |
| `OperatorType.WidthMeasurement` | 宽度测量 | `Measurement` | 测量 | `Stable` | `1.0.0` |
| `OperatorType.CalibrationLoader` | 标定加载 | `CalibrationAndCoordinates` | 标定与坐标 | `Stable` | `1.0.0` |
| `OperatorType.CameraCalibration` | 相机标定 | `CalibrationAndCoordinates` | 标定与坐标 | `Stable` | `1.0.0` |
| `OperatorType.CoordinateTransform` | 像素到物理坐标（单点） | `CalibrationAndCoordinates` | 标定与坐标 | `Stable` | `1.0.0` |
| `OperatorType.FisheyeCalibration` | 鱼眼标定 | `CalibrationAndCoordinates` | 标定与坐标 | `Stable` | `1.0.0` |
| `OperatorType.FisheyeUndistort` | 鱼眼去畸变 | `CalibrationAndCoordinates` | 标定与坐标 | `Stable` | `1.0.0` |
| `OperatorType.HandEyeCalibration` | 手眼标定 | `CalibrationAndCoordinates` | 标定与坐标 | `Stable` | `1.0.0` |
| `OperatorType.HandEyeCalibrationValidator` | 手眼标定验证 | `CalibrationAndCoordinates` | 标定与坐标 | `Stable` | `1.0.1` |
| `OperatorType.NPointCalibration` | N点标定 | `CalibrationAndCoordinates` | 标定与坐标 | `Stable` | `1.0.0` |
| `OperatorType.PixelToWorldTransform` | 像素世界映射 | `CalibrationAndCoordinates` | 标定与坐标 | `Stable` | `1.0.1` |
| `OperatorType.StereoCalibration` | 双目标定 | `CalibrationAndCoordinates` | 标定与坐标 | `Stable` | `1.0.0` |
| `OperatorType.TranslationRotationCalibration` | 平移旋转标定 | `CalibrationAndCoordinates` | 标定与坐标 | `Stable` | `1.1.1` |
| `OperatorType.Undistort` | 畸变校正 | `CalibrationAndCoordinates` | 标定与坐标 | `Stable` | `1.0.0` |
| `OperatorType.AnomalyDetection` | 异常检测 | `AiInference` | AI推理 | `Experimental` | `1.2.0` |
| `OperatorType.DeepLearning` | 深度学习 | `AiInference` | AI推理 | `Stable` | `1.1.0` |
| `OperatorType.OcrRecognition` | OCR 识别 | `AiInference` | AI推理 | `Stable` | `1.0.0` |
| `OperatorType.SemanticSegmentation` | 语义分割 | `AiInference` | AI推理 | `Stable` | `1.0.0` |
| `OperatorType.EuclideanClusterExtraction` | 欧氏聚类分割 | `PointCloud3D` | 3D点云 | `Stable` | `1.1.0` |
| `OperatorType.PPFEstimation` | PPF点对特征 | `PointCloud3D` | 3D点云 | `Stable` | `1.0.0` |
| `OperatorType.PPFMatch` | PPF点云粗匹配 | `PointCloud3D` | 3D点云 | `Stable` | `1.0.5` |
| `OperatorType.RansacPlaneSegmentation` | RANSAC平面分割 | `PointCloud3D` | 3D点云 | `Stable` | `1.0.0` |
| `OperatorType.StatisticalOutlierRemoval` | 点云统计离群点去除（SOR） | `PointCloud3D` | 3D点云 | `Stable` | `1.0.1` |
| `OperatorType.VoxelDownsample` | 体素下采样 | `PointCloud3D` | 3D点云 | `Stable` | `1.0.1` |
| `OperatorType.Aggregator` | 数据聚合 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.ArrayIndexer` | 数组索引器 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.BoxFilter` | 候选框筛选 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.BoxNms` | 候选框抑制 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.Comparator` | 数值比较 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.JsonExtractor` | JSON 提取器 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.LogicGate` | 逻辑门 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.MathOperation` | 数值计算 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.PointSetTool` | 点集工具 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.ScriptOperator` | 脚本算子 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.Statistics` | 统计分析 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.StringFormat` | 字符串格式化 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.TimerStatistics` | 计时统计 | `DataProcessing` | 数据处理 | `Stable` | `1.0.1` |
| `OperatorType.TypeConvert` | 类型转换 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.UnitConvert` | 单位换算 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.VariableIncrement` | 变量递增 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.VariableRead` | 变量读取 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.VariableWrite` | 变量写入 | `DataProcessing` | 数据处理 | `Stable` | `1.0.0` |
| `OperatorType.ConditionalBranch` | 条件分支 | `FlowControl` | 流程控制 | `Stable` | `1.0.0` |
| `OperatorType.CycleCounter` | 循环计数器 | `FlowControl` | 流程控制 | `Stable` | `1.0.0` |
| `OperatorType.Delay` | 延时 | `FlowControl` | 流程控制 | `Stable` | `1.0.0` |
| `OperatorType.ForEach` | ForEach 循环 | `FlowControl` | 流程控制 | `Stable` | `1.0.0` |
| `OperatorType.FrameChangeTrigger` | 帧变化触发 | `FlowControl` | 流程控制 | `Stable` | `1.0.0` |
| `OperatorType.ResultJudgment` | 结果判定 | `FlowControl` | 流程控制 | `Stable` | `1.0.1` |
| `OperatorType.TriggerModule` | 触发模块 | `FlowControl` | 流程控制 | `Stable` | `1.0.0` |
| `OperatorType.TryCatch` | Try分支透传 | `FlowControl` | 流程控制 | `Stable` | `1.0.0` |
| `OperatorType.HttpRequest` | HTTP 请求 | `Communication` | 通信 | `Stable` | `1.0.0` |
| `OperatorType.MitsubishiMcCommunication` | 三菱MC通信 | `Communication` | 通信 | `Stable` | `1.0.0` |
| `OperatorType.ModbusCommunication` | Modbus TCP通信 | `Communication` | 通信 | `Stable` | `1.0.0` |
| `OperatorType.MqttPublish` | MQTT 发布 | `Communication` | 通信 | `Reference` | `0.1.0` |
| `OperatorType.OmronFinsCommunication` | 欧姆龙FINS通信 | `Communication` | 通信 | `Stable` | `1.0.0` |
| `OperatorType.SerialCommunication` | 串口通信 | `Communication` | 通信 | `Stable` | `1.0.0` |
| `OperatorType.SiemensS7Communication` | 西门子S7通信 | `Communication` | 通信 | `Stable` | `1.0.0` |
| `OperatorType.TcpCommunication` | TCP通信 | `Communication` | 通信 | `Stable` | `1.0.0` |
| `OperatorType.Comment` | 注释 | `OutputAndAuxiliary` | 输出与辅助 | `Stable` | `1.0.1` |
| `OperatorType.DatabaseWrite` | 数据库写入 | `OutputAndAuxiliary` | 输出与辅助 | `Stable` | `1.0.0` |
| `OperatorType.ImageSave` | 图像保存 | `OutputAndAuxiliary` | 输出与辅助 | `Stable` | `1.0.0` |
| `OperatorType.ResultOutput` | 结果输出 | `OutputAndAuxiliary` | 输出与辅助 | `Stable` | `1.0.1` |
| `OperatorType.TextSave` | 文本保存 | `OutputAndAuxiliary` | 输出与辅助 | `Stable` | `1.0.0` |

## 历史变更 / Historical Changes

### OperatorType.AdaptiveThreshold / 自适应阈值
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `FF7521806537` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `A7B6230D1856` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `FD3226A7C953` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `FD3226A7C953` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `02D278C242EB` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `1CDC61ADF8A9` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `02D278C242EB` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `0B6B9A1EA1B3` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `B975CE907035` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `B455F3119904` | `legacy-source-only` |

### OperatorType.AffineTransform / 仿射变换
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `4888FB5B00DE` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `C5FEC924CD6D` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `03A3CD58A452` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `03A3CD58A452` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `022177F70CFA` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `0883863464A2` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `9C23CBB5A4BF` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T23:21:39.1176099+08:00` | `0883863464A2` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `4AD3551216EE` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `E63763CA1D29` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `4AD3551216EE` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `D15105FFF7E6` | `legacy-source-only` |

### OperatorType.Aggregator / 数据聚合
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `FF854B092E0E` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `3E69089FA1ED` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `3E69089FA1ED` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `759340E0A7C1` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `16362DF3D213` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `759340E0A7C1` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T22:27:39.4611441+08:00` | `16362DF3D213` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `7687F4F003EF` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `E68F1AFAFC5F` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `FAD533F6A53E` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `9D1FADF66DA7` | `legacy-source-only` |

### OperatorType.AkazeFeatureMatch / AKAZE特征匹配
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `81A958291FEA` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `F7FC8BB252D9` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `D236A975F000` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-07-01T01:12:41.9920283+08:00` | `D236A975F000` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `52D1867EB778` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `F32415A18341` | `legacy-source-only` |
| `1.0.0` | `2026-05-01T14:53:33.8287356+08:00` | `8889F9A22DF4` | `legacy-source-only` |
| `1.0.0` | `2026-05-01T10:19:56.3600494+08:00` | `2741643CEF87` | `legacy-source-only` |
| `1.0.0` | `2026-04-30T08:08:30.0876185+08:00` | `450FF3725E39` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `76E0B0897DC7` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:54:57.1979469+08:00` | `5CF3D0185751` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `4F512FF14BE8` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `C48F52C883AF` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T19:14:52.1190277+08:00` | `0E20A63B6925` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `8E074C0E1329` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T10:35:29.6469155+08:00` | `6F966F31BFBE` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `7826C15D141A` | `legacy-source-only` |

### OperatorType.AngleMeasurement / 角度测量
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `21FF978562E8` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `B5199D10F296` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-14T06:44:39.4853382+08:00` | `778F9EFD6108` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T19:31:36.3593491+08:00` | `1957ADB39B62` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T19:08:24.4997898+08:00` | `1DFC22FA6A4B` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `2C092A81F025` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `2C092A81F025` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `B40F13EB4CE0` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `38693CE7BA0D` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `B40F13EB4CE0` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `38693CE7BA0D` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `C04EFD57FA69` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `6AD54E78C9E8` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `75B22BE2483A` | `legacy-source-only` |

### OperatorType.AnomalyDetection / 异常检测
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.2.0` | `2026-07-16T00:58:49.9337226+08:00` | `4FE4975A26FA` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-16T00:57:29.1622202+08:00` | `848C8876756F` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-15T23:17:17.3688808+08:00` | `D16582B2F872` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-15T23:08:03.2424341+08:00` | `63FA11AB5F58` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-15T17:25:56.0119276+08:00` | `68B18DB0656B` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `4690F5AC1900` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `9FA2E91555A8` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `A8E26CC9B275` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `A8E26CC9B275` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `DD55700713B3` | `legacy-source-only` |
| `1.0.0` | `2026-05-01T14:53:33.8287356+08:00` | `0DBD2BB6FA9B` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `9AE641E24834` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `D3409F7C45D4` | `legacy-source-only` |
| `1.0.0` | `2026-04-26T16:47:36.9318375+08:00` | `9AE641E24834` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `ED3374278EF7` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `FFA0A783ACCD` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `AAEA9F498173` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `C16C20BC74A9` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `0C14CFAF3486` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `C16C20BC74A9` | `legacy-source-only` |

### OperatorType.ArcCaliper / 圆弧卡尺
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `AFD3C81A2B7C` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `489A8C2635A9` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `6BC5FE7E6712` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `6BC5FE7E6712` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `F4E30D24607D` | `legacy-source-only` |
| `1.0.1` | `2026-05-01T10:19:56.3600494+08:00` | `DCDF0399AE0F` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `7B276C2CF5C1` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `8009A18194AB` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T21:50:17.7971617+08:00` | `7B276C2CF5C1` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `C78E51E6B0B4` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `540CC39DD98E` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `BD6BC33A07AE` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `A18C33786AE5` | `legacy-source-only` |

### OperatorType.ArrayIndexer / 数组索引器
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `B3BB274D288E` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `92FFAB00869F` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `92FFAB00869F` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `C0FE6410B2D8` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `85F321393926` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `C0FE6410B2D8` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T22:27:39.4611441+08:00` | `85F321393926` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `7320DDA45D24` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `14127778F4A0` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `A4ADD286E7D5` | `legacy-source-only` |

### OperatorType.BilateralFilter / 双边滤波
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.0` | `2026-07-15T17:25:56.0119276+08:00` | `2AC2FE2007E4` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-15T11:26:25.6098568+08:00` | `7659FA8D8DD6` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `9582471509EB` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T19:08:24.4997898+08:00` | `5B8CECBAF5D9` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `E28184F3B659` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `E28184F3B659` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `AD856145D522` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `41C33EAE8CC8` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `8A67432D3AB0` | `legacy-source-only` |

### OperatorType.BinaryImageToRegion / 二值图转区域
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.0` | `2026-07-15T11:26:25.6098568+08:00` | `8E9649AADED7` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-14T15:07:23.3198726+08:00` | `08214C73F8DC` | `operator-runtime-metadata-v2` |
| `1.1.0` | `2026-07-13T15:25:22.2877465+08:00` | `83D896D016A9` | `legacy-source-only` |
| `1.1.0` | `2026-07-13T11:23:19.7870903+08:00` | `664B5E76B6D2` | `legacy-source-only` |

### OperatorType.BlobAnalysis / Blob分析
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.2.1` | `2026-07-15T11:26:25.6098568+08:00` | `896918CF9B07` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.2.1` | `2026-07-14T15:07:23.3198726+08:00` | `493209900B69` | `operator-runtime-metadata-v2` |
| `1.2.1` | `2026-07-13T15:25:22.2877465+08:00` | `6D57E2462969` | `legacy-source-only` |
| `1.2.1` | `2026-07-13T11:23:19.7870903+08:00` | `4AC6DC99519F` | `legacy-source-only` |
| `1.1.0` | `2026-07-10T11:21:26.9540273+08:00` | `A0FC81AFCB84` | `legacy-source-only` |
| `1.1.0` | `2026-07-06T21:35:46.7699945+08:00` | `7B9490520F45` | `legacy-source-only` |
| `1.1.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.1.0` | `2026-06-25T11:35:53.9102407+08:00` | `7B9490520F45` | `legacy-source-only` |
| `1.1.0` | `2026-05-16T11:53:47.4328965+08:00` | `5E2729EADC33` | `legacy-source-only` |
| `1.1.0` | `2026-04-29T10:56:41.0664908+08:00` | `1A573F6388F2` | `legacy-source-only` |
| `1.1.0` | `2026-04-28T19:39:42.8097784+08:00` | `4A39589192D4` | `legacy-source-only` |
| `1.1.0` | `2026-04-28T10:51:32.3393648+08:00` | `60343868DAB7` | `legacy-source-only` |
| `1.1.0` | `2026-04-18T22:49:10.0250597+08:00` | `4A39589192D4` | `legacy-source-only` |
| `1.1.0` | `2026-04-12T20:43:23.0238145+08:00` | `6D9B00606829` | `legacy-source-only` |
| `1.1.0` | `2026-04-12T12:53:52.9929473+08:00` | `35B00D18F075` | `legacy-source-only` |
| `1.1.0` | `2026-03-21T01:38:49.8374844+08:00` | `3BC2F4374BA8` | `legacy-source-only` |
| `1.1.0` | `2026-03-17T14:30:51.0566057+08:00` | `9C4A1922B234` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T14:27:11.6128169+08:00` | `15C54747CFB6` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T12:35:04.9178309+08:00` | `066BA62991EB` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T23:16:26.6950446+08:00` | `E479498C6334` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T01:00:41.9846479+08:00` | `DD3B35AC2885` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `AA3FBABF31F0` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `399F00274DCD` | `legacy-source-only` |

### OperatorType.BlobLabeling / Blob分类标注
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `FE849935D52A` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `293E3F019348` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-13T15:25:22.2877465+08:00` | `5AEAEE7477B3` | `legacy-source-only` |
| `1.0.1` | `2026-07-13T11:23:19.7870903+08:00` | `114D44F0DFD3` | `legacy-source-only` |
| `1.0.0` | `2026-07-10T11:21:26.9540273+08:00` | `C3CB5CA0E622` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `E744E240C348` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `E744E240C348` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `B62CE4773605` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `6CA65007DBFB` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `B62CE4773605` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T19:14:52.1190277+08:00` | `6CA65007DBFB` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `10BC8AC0A2DD` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `98361D064008` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `10BC8AC0A2DD` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `CB8C33825ABA` | `legacy-source-only` |

### OperatorType.BoxFilter / 候选框筛选
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `94FB73CD440D` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `346F63E53B11` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `3B4CF30A85FE` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `3B4CF30A85FE` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `16B146F1F86B` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `2E4FC3F7DF9C` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `9686BC75BE56` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T22:27:39.4611441+08:00` | `2E4FC3F7DF9C` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `DFFA649DF37B` | `legacy-source-only` |
| `1.0.0` | `2026-03-27T15:14:57.0770992+08:00` | `809AD2B52541` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `E6CEA2515082` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `E733CF600B7F` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `2E238D831FF0` | `legacy-source-only` |

### OperatorType.BoxNms / 候选框抑制
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `79F956B20D31` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `BE4B28FF8304` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `4704AA183F94` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `4704AA183F94` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `0A6A4202A567` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `49345B76F4CE` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `40D3FE35DADA` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `49345B76F4CE` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `E4CBB398C15C` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T20:16:17.5776687+08:00` | `2B3620B159CC` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `426E8183E019` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `96FD137BF2A1` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `C0B7D3338B2D` | `legacy-source-only` |

### OperatorType.CalibrationLoader / 标定加载
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `E8896936329E` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `04537A606E24` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `04537A606E24` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `EA7DE323E4FA` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `54BFF1856F9A` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `EA7DE323E4FA` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `54BFF1856F9A` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `78A181F5EBA5` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `4DE2162FEF55` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `1680B6462051` | `legacy-source-only` |

### OperatorType.CaliperTool / 卡尺工具
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.2.1` | `2026-07-16T01:07:27.7681703+08:00` | `A85552FEF127` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.2.0` | `2026-07-16T01:06:46.0334378+08:00` | `2DDE27AAF7B3` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.2.0` | `2026-07-16T00:58:49.9337226+08:00` | `97B7649A3E5C` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-16T00:57:29.1622202+08:00` | `E25FEA79D905` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-15T23:17:17.3688808+08:00` | `B6BE6A235849` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-15T23:08:03.2424341+08:00` | `19F42DCF9E24` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `A4BC37D12A87` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `AC1D3782EBB9` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `CAC096F69FC0` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `CAC096F69FC0` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `FA337F7F3643` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T17:13:24.7548110+08:00` | `A9F758C4AF04` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `0148E489A2CF` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `77D8C08130B2` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `0148E489A2CF` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `CD8B9C826A1B` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `0E547D776123` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `6CD663B6F1E2` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `CDB7057CA44D` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `8956BB4F439B` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T12:35:04.9178309+08:00` | `78EBE4E17E09` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T23:16:26.6950446+08:00` | `79BAEECDB051` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `BE0CC297B91B` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `18247D9E6FB3` | `legacy-source-only` |

### OperatorType.CameraCalibration / 相机标定
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `C4EF240DCDB5` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `A67BB2D5E14A` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `40B10A5AADFB` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `40B10A5AADFB` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `C279EF07B9B5` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `6573CAB6B5E2` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `C279EF07B9B5` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `6573CAB6B5E2` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `2F4B09AD7F10` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `FE613E289E47` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `F16C96AD05AD` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `490769260740` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `C892CA5A86D9` | `legacy-source-only` |

### OperatorType.CircleMeasurement / 圆测量
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.2.0` | `2026-07-15T23:17:17.3688808+08:00` | `BB568917FD41` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.2` | `2026-07-15T23:08:03.2424341+08:00` | `D4674DF2C9FD` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.2` | `2026-07-15T11:26:25.6098568+08:00` | `FAE310F1643D` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.2` | `2026-07-14T15:07:23.3198726+08:00` | `19A167616B19` | `operator-runtime-metadata-v2` |
| `1.1.2` | `2026-07-13T15:25:22.2877465+08:00` | `04243D79C2EE` | `legacy-source-only` |
| `1.1.2` | `2026-07-06T21:35:46.7699945+08:00` | `29B368DADCD5` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.1.2` | `2026-07-04T02:14:41.6081842+08:00` | `29B368DADCD5` | `legacy-source-only` |
| `1.1.1` | `2026-07-03T21:48:17.3666159+08:00` | `E0A8874D6BEE` | `legacy-source-only` |
| `1.1.0` | `2026-07-03T19:53:45.9834695+08:00` | `026AAE59BD0F` | `legacy-source-only` |
| `1.1.0` | `2026-07-03T19:39:52.6116319+08:00` | `7448D2348291` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `1CB951CB0030` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `897AC10DDD90` | `legacy-source-only` |
| `1.0.0` | `2026-05-01T10:19:56.3600494+08:00` | `89896935EB97` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `B9BBF0DE7D77` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `B2A02CCF580E` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `B9BBF0DE7D77` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `F292A0C905DE` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `715E73668BBF` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T01:00:41.9846479+08:00` | `0F06DC09DFF2` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `F24C794D0D92` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `D89303A4578F` | `legacy-source-only` |

### OperatorType.ClaheEnhancement / CLAHE增强
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `8FE0EEE5EDF1` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `0A1FFC8BCD78` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `815B182CC051` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `815B182CC051` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `BE7231F64AF5` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `43109B43EBCF` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `5F3BBC85B0F4` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `43109B43EBCF` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `5F3BBC85B0F4` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T01:00:41.9846479+08:00` | `69E533C1A735` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `E6C58B4D7CFF` | `legacy-source-only` |

### OperatorType.CodeRecognition / 条码识别
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `7D711DD76987` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `AC385173DFD5` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `DE18D7DB5977` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `DE18D7DB5977` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `42B65E96D449` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T13:56:16.3361485+08:00` | `D9BD335F62EC` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `4911A54BEC5B` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `E9EA23FB99BA` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `4911A54BEC5B` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `E9EA23FB99BA` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `C49C5CAA3C71` | `legacy-source-only` |

### OperatorType.ColorConversion / 颜色空间转换
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `2FE4E29B3E3B` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `4EEF6948E412` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `049A6737FDC1` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `049A6737FDC1` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `D7F62D1A2454` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T01:00:41.9846479+08:00` | `287716BE3467` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `04C479C51518` | `legacy-source-only` |

### OperatorType.ColorDetection / 颜色分析
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `2.0.1` | `2026-07-15T17:25:56.0119276+08:00` | `7FC15AEA6072` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `2.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `0E04626E9216` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `2.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `282E0EEEE835` | `operator-runtime-metadata-v2` |
| `2.0.1` | `2026-07-13T15:25:22.2877465+08:00` | `B2E5AF92FD59` | `legacy-source-only` |
| `2.0.1` | `2026-07-13T11:23:19.7870903+08:00` | `55D847A7E4B3` | `legacy-source-only` |
| `2.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `15F36841CDF3` | `legacy-source-only` |
| `2.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `2.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `15F36841CDF3` | `legacy-source-only` |
| `2.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `97A3F84941B4` | `legacy-source-only` |
| `2.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `DE402A570244` | `legacy-source-only` |
| `2.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `97A3F84941B4` | `legacy-source-only` |
| `2.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `B084EF7C405A` | `legacy-source-only` |
| `2.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `D9F18714A4C1` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `424C14C47E4B` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T12:35:04.9178309+08:00` | `F78B0BF0F340` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T01:00:41.9846479+08:00` | `423D33D479AE` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `BBD0B5A93508` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T10:35:29.6469155+08:00` | `46BDCCBEBC34` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `47E27D2BD6CF` | `legacy-source-only` |

### OperatorType.ColorMeasurement / 颜色测量
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `2.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `C2E255FDE864` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `2.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `126CB81CBAFB` | `operator-runtime-metadata-v2` |
| `2.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `001910DD20D2` | `legacy-source-only` |
| `2.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `2.0.0` | `2026-07-01T00:00:21.2732782+08:00` | `001910DD20D2` | `legacy-source-only` |
| `2.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `4357163C9800` | `legacy-source-only` |
| `2.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `63D11BF53D74` | `legacy-source-only` |
| `2.0.0` | `2026-04-29T17:13:24.7548110+08:00` | `09A22761CDC2` | `legacy-source-only` |
| `2.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `63D15F76BCB1` | `legacy-source-only` |
| `2.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `B71CB44ECE08` | `legacy-source-only` |
| `2.0.0` | `2026-04-19T13:57:48.4747228+08:00` | `63D15F76BCB1` | `legacy-source-only` |
| `2.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `3BE4DD1A8F27` | `legacy-source-only` |
| `2.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `20BC9FC908FB` | `legacy-source-only` |
| `1.0.2` | `2026-03-21T01:38:49.8374844+08:00` | `63EE99CDC008` | `legacy-source-only` |
| `1.0.2` | `2026-03-18T19:00:25.2910689+08:00` | `B6BCC41568EB` | `legacy-source-only` |
| `1.0.1` | `2026-03-17T17:33:01.9139128+08:00` | `13FF4D7C376D` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T17:30:55.6121854+08:00` | `AB581D9F2445` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `31FC57E80C3C` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `970C6588B0A4` | `legacy-source-only` |

### OperatorType.Comment / 注释
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `AB1512D96759` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `3DA69144335C` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `3DA69144335C` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `AB24C01595BF` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T23:15:19.4916106+08:00` | `0551D0079C76` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `3D88605226ED` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `D088230EC428` | `legacy-source-only` |

### OperatorType.Comparator / 数值比较
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `30EF6DB53084` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `35E3716B920F` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `35E3716B920F` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `97A7464100D9` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `20A58BFA7B00` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `22FCE0FF7CB5` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `B6B5D8E5A137` | `legacy-source-only` |

### OperatorType.ConditionalBranch / 条件分支
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `ACE45EF2B768` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `D122ACB53A28` | `legacy-source-only` |
| `1.0.0` | `2026-07-07T09:12:23.0895237+08:00` | `881F0099B537` | `legacy-source-only` |
| `1.0.0` | `2026-07-07T08:45:40.9653060+08:00` | `548635B675A6` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `4CFA01D0E7A0` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `4CFA01D0E7A0` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `0EBE96F5F22F` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `29F6BE3DEEB2` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `E8EBC9095D13` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `29F6BE3DEEB2` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `E8EBC9095D13` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `29F6BE3DEEB2` | `legacy-source-only` |

### OperatorType.ContourDetection / 轮廓检测
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `8415835802F2` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `6DE3F087082E` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `D468426E549D` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `D468426E549D` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `B72BC27F515B` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T13:56:16.3361485+08:00` | `5E019893822A` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `B5B5CF6903DA` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `D73488938EDD` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `62F866A1DD98` | `legacy-source-only` |

### OperatorType.ContourExtrema / 轮廓极值
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `13CE457707AC` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `FDC905FE4A86` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `FDC905FE4A86` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `96667B5EB5FF` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `E1CE0C0D5287` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `BBFAEBF7CB82` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T23:15:19.4916106+08:00` | `E1CE0C0D5287` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `C36BDB58E279` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `89D3C64A1F06` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `CF1814DC72D3` | `legacy-source-only` |

### OperatorType.ContourMeasurement / 轮廓测量
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `4936B3B59F92` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `FA5A6B27C5B5` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `F5B23EF05F50` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `F5B23EF05F50` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `130D0FA47911` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `287892D24DDD` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `130D0FA47911` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T13:57:48.4747228+08:00` | `287892D24DDD` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `9E59A9BBDB9F` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `54B6127392FC` | `legacy-source-only` |

### OperatorType.CoordinateTransform / 像素到物理坐标（单点）
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `A42A7A6B0B58` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `D869EA49B591` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T19:08:24.4997898+08:00` | `7118851688D0` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `B2A08CA721CA` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `536A2FBA1471` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `536A2FBA1471` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `7FD48BE71F4F` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `0ACBA65146BA` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `7FD48BE71F4F` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `0ACBA65146BA` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `9A444692FBD4` | `legacy-source-only` |

### OperatorType.CopyMakeBorder / 边界填充
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `9E8042AFC582` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `76D2548C0436` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `C34732E1650B` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `C34732E1650B` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `7FAE5011770B` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `EA1319B3DDD0` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `D98F801C200D` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `EA1319B3DDD0` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `D98F801C200D` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `EA1319B3DDD0` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `D52A1162A333` | `legacy-source-only` |

### OperatorType.CornerDetection / 角点检测
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `186EBA4B1F5E` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `1C94A76CA848` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `034015C02612` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `034015C02612` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `5A37D8EC2E9C` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `8FC52A647500` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `A309BED01F1A` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `8FC52A647500` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `A309BED01F1A` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `8FC52A647500` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `8CB17BD12666` | `legacy-source-only` |

### OperatorType.CycleCounter / 循环计数器
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `A2754CE732F5` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `B94BB234DD64` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `B94BB234DD64` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `057DF8632923` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `FB3FBB21C312` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `057DF8632923` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `FB3FBB21C312` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `8938799BB943` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `59FED1DF2CFC` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `8938799BB943` | `legacy-source-only` |

### OperatorType.DatabaseWrite / 数据库写入
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `399D8D35CF0F` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `F0A505E7354E` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `1A87A17DFA67` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `34F8D879F026` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `38728B31DD7E` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `34F8D879F026` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T22:27:39.4611441+08:00` | `38728B31DD7E` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `C6053AF760EF` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `DB7098D9E78E` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `71703C60A331` | `legacy-source-only` |

### OperatorType.DeepLearning / 深度学习
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.0` | `2026-07-15T11:26:25.6098568+08:00` | `C43819258811` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-14T15:07:23.3198726+08:00` | `6629D61042D1` | `operator-runtime-metadata-v2` |
| `1.1.0` | `2026-07-14T06:44:39.4853382+08:00` | `07A951845D29` | `legacy-source-only` |
| `1.1.0` | `2026-07-13T19:31:36.3593491+08:00` | `EE2D68C2F977` | `legacy-source-only` |
| `1.1.0` | `2026-07-13T19:08:24.4997898+08:00` | `6E22F78869DD` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `21E09294F69B` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-07-01T00:00:21.2732782+08:00` | `21E09294F69B` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `3C6DE313D0C0` | `legacy-source-only` |
| `1.0.0` | `2026-05-26T00:24:19.8012708+08:00` | `087C6F211875` | `legacy-source-only` |
| `1.0.0` | `2026-05-20T23:16:04.5632818+08:00` | `7DFC4B0B489D` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `B43B8B8EE6F5` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:11:27.4197640+08:00` | `957E3CC0659A` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `FE7155D6576E` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T00:59:13.5713917+08:00` | `47DADF605F06` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `AC51CFE64D6E` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `BA01D2E30BBF` | `legacy-source-only` |
| `1.0.0` | `2026-04-26T16:47:36.9318375+08:00` | `AC51CFE64D6E` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `6271FC302C63` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T23:09:00.7096859+08:00` | `C0CCA317E748` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `919300EFCAB2` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `3B4FA6C253FC` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `2E48EADEADA1` | `legacy-source-only` |
| `1.0.0` | `2026-03-27T21:24:05.6159117+08:00` | `4D9170172AFC` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T20:16:17.5776687+08:00` | `D88A39CAAF47` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `94CF298C8838` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `7A28ABBC8B98` | `legacy-source-only` |
| `1.0.0` | `2026-03-19T21:05:20.9090050+08:00` | `BA294CCA79B2` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T19:59:19.7031372+08:00` | `1080D0ECD2E1` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `5EC82567EBEA` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `B029BB23CF34` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T10:35:29.6469155+08:00` | `BED694E4F32C` | `legacy-source-only` |
| `1.0.0` | `2026-02-28T20:04:47.7041096+08:00` | `EE655353AE8F` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `CB4801A37985` | `legacy-source-only` |

### OperatorType.Delay / 延时
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `58DF057665DD` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `A2774782A3BD` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `A2774782A3BD` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `A2DBCB95F690` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `90A13B625F06` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `D68E60C72457` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `AE3DCF902E63` | `legacy-source-only` |

### OperatorType.DetectionSequenceJudge / 检测顺序判定
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `93D6CC25A3CD` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-13T15:25:22.2877465+08:00` | `22395D52F5B4` | `legacy-source-only` |
| `1.0.1` | `2026-07-13T11:23:19.7870903+08:00` | `2D5746D6EFB9` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `52A3E1A57B9E` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-07-01T00:00:21.2732782+08:00` | `52A3E1A57B9E` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `0DD49E56D2D0` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `3CCC1642A95F` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `288FDBA0A0F1` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `3CCC1642A95F` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `ED19D09189CA` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `727A2E906BAF` | `legacy-source-only` |
| `1.0.0` | `2026-03-27T15:14:57.0770992+08:00` | `304650035DC9` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `BBDC8D920528` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `8A2065048793` | `legacy-source-only` |
| `1.0.0` | `2026-03-19T21:05:20.9090050+08:00` | `FBDBEFB1AF7F` | `legacy-source-only` |

### OperatorType.DistanceTransform / 距离变换
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `3F1534F8BA3D` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `AC722495D92E` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `F3BC52589AE4` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `F3BC52589AE4` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `69161E9EFB14` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `0D125088BA31` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `4A781ED0A16A` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T23:33:19.3146997+08:00` | `0D125088BA31` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `4CCCB1271AB4` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `F7E7D8FDA5AC` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `067E9764FAD3` | `legacy-source-only` |

### OperatorType.DualModalVoting / 双模态投票
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `59EE5795D682` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `5BD094789A35` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `5BD094789A35` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `7E383C0AD12E` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `0273FCAB0A85` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `7E383C0AD12E` | `legacy-source-only` |
| `1.0.0` | `2026-04-26T21:31:56.5042156+08:00` | `0273FCAB0A85` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `6FDD25C9226A` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T23:21:39.1176099+08:00` | `6220BDF108EF` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `F7DA5BDB66C7` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `B50F970BACFE` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `F7DA5BDB66C7` | `legacy-source-only` |

### OperatorType.EdgeDetection / 边缘检测
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T17:25:56.0119276+08:00` | `7E0C2BA77CA2` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `6B83C5AE650D` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `B08931DAA048` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `A0F0D4878898` | `legacy-source-only` |
| `1.0.0` | `2026-07-09T09:44:47.2036663+08:00` | `F4C0EA5E8130` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `5DFF91703AC5` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `5DFF91703AC5` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `D794E0E1D6FF` | `legacy-source-only` |
| `1.0.0` | `2026-05-01T10:19:56.3600494+08:00` | `8A3EEFD0E4DA` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T13:56:16.3361485+08:00` | `283558FA75DC` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `B68676340745` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `2FE248A237C5` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `B68676340745` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T01:00:41.9846479+08:00` | `2DFA437FCD6B` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `3635CC3533DC` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `D8DB44452BC2` | `legacy-source-only` |

### OperatorType.EdgeIntersection / 边线交点
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `9CCBC23B4EF4` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `8CB0955B4C95` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `8CB0955B4C95` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `CF4B05792020` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `85FCA83E139C` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `CF4B05792020` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T19:14:52.1190277+08:00` | `85FCA83E139C` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `25C7003F1EBD` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `F9B031E6BA5A` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `25C7003F1EBD` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `49D076E880F2` | `legacy-source-only` |

### OperatorType.EdgePairDefect / 边缘间距缺陷检测
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `F562034673B4` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `396A9FF1EA5B` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-13T15:25:22.2877465+08:00` | `C29CC66E9EDE` | `legacy-source-only` |
| `1.0.1` | `2026-07-13T11:23:19.7870903+08:00` | `4625E13FC56F` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `ADBD594BBED7` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `ADBD594BBED7` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `27338ED21CCC` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `15C5F1D203C5` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `B87662A40B4C` | `legacy-source-only` |
| `1.0.0` | `2026-04-26T16:47:36.9318375+08:00` | `15C5F1D203C5` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `4A36B0BDAC3F` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `4BF8AD287B51` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `612FC012809E` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `40A7891AAD29` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `7FC4BA140142` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `81CCA70F0BDE` | `legacy-source-only` |

### OperatorType.EuclideanClusterExtraction / 欧氏聚类分割
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.0` | `2026-07-15T20:30:44.7937318+08:00` | `75C259C93648` | `operator-runtime-metadata-v2` |
| `1.1.0` | `2026-07-15T20:16:21.3617917+08:00` | `C14FD1BA20AA` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `91C9F015F7A9` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `98D5F5461D09` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `98D5F5461D09` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `24432AA1D437` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T15:49:49.5980240+08:00` | `80F7E85D31B4` | `legacy-source-only` |

### OperatorType.FFT1D / 信号/图像傅里叶变换（FFT）
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `72F7D0AAA92F` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T19:08:24.4997898+08:00` | `0B57F07C871E` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `59EB7F16DFD5` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `117EB00200ED` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `117EB00200ED` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `276DD43F9752` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `9C0C2BD10FA0` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `E4AFC15E467E` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `9C0C2BD10FA0` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `0A819319AA59` | `legacy-source-only` |

### OperatorType.Filtering / 滤波
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.2.0` | `2026-07-15T17:25:56.0119276+08:00` | `D956D7CDABEF` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.2.0` | `2026-07-15T11:26:25.6098568+08:00` | `DC2DDAD5E612` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-14T15:07:23.3198726+08:00` | `FB53FB9D9164` | `operator-runtime-metadata-v2` |
| `1.1.0` | `2026-07-14T06:44:39.4853382+08:00` | `5A179D86EA18` | `legacy-source-only` |
| `1.1.0` | `2026-07-13T19:36:09.4305891+08:00` | `47E1A288F2BA` | `legacy-source-only` |
| `1.1.0` | `2026-07-13T19:31:36.3593491+08:00` | `01385017D3A6` | `legacy-source-only` |
| `1.1.0` | `2026-07-13T19:08:24.4997898+08:00` | `E0A97FB54B33` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `9BA39849E674` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `9BA39849E674` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `216466C811A1` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `10556AD6D71E` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `D9C1A51FA92C` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `10556AD6D71E` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `D9C1A51FA92C` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `10556AD6D71E` | `legacy-source-only` |

### OperatorType.FisheyeCalibration / 鱼眼标定
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `147BA40A3D44` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `01534C65905C` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `41EA08084786` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `41EA08084786` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `CC2BD45C6A21` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `429A6C95885C` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `CC2BD45C6A21` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `429A6C95885C` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `990EBD083DE8` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `72B2E9BED29F` | `legacy-source-only` |

### OperatorType.FisheyeUndistort / 鱼眼去畸变
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `C1CD23B2ABC0` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `ADCAA919220A` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `4FDB9AC0354A` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `4FDB9AC0354A` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `40025FF3EE20` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T23:36:31.6119368+08:00` | `B6E1D4913619` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `FF4B5FD2ABF2` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `3C4BC955A4B9` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `FF4B5FD2ABF2` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `30A48680273C` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `A9155938C127` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `B0B56491C645` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `7B86C470ECE8` | `legacy-source-only` |

### OperatorType.ForEach / ForEach 循环
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `92685762FCE9` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T11:23:19.7870903+08:00` | `EDCBE412B184` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `1F5CC38646CF` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `1F5CC38646CF` | `legacy-source-only` |
| `1.0.0` | `2026-05-20T23:16:04.5632818+08:00` | `454A81DB790E` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `D4D0581CB465` | `legacy-source-only` |
| `1.0.0` | `2026-05-04T22:48:13.3257374+08:00` | `E199FAFD4B15` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `8F6A40F1272A` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `AC46932FE32B` | `legacy-source-only` |

### OperatorType.FrameAveraging / 帧平均
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `E3A544B34FB8` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `BFF62047DA26` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `DB8CC81E2F2B` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `BDCCB072E6DC` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `D74D77BEC045` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `F412F9209141` | `legacy-source-only` |
| `1.0.0` | `2026-05-05T10:58:42.1531784+08:00` | `85EB532B8835` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `D28C8497E738` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `867181842D44` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T23:09:00.7096859+08:00` | `D28C8497E738` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `D5E243D1D832` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `5D3A3372B991` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `5D3973154CFA` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `986722B04F67` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `2DF9846480B8` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `66F873796264` | `legacy-source-only` |

### OperatorType.FrameChangeTrigger / 帧变化触发
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `8B9104464950` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `D3195C906FDB` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `6DF0EAA42C89` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `6DF0EAA42C89` | `legacy-source-only` |
| `1.0.0` | `2026-05-20T23:16:04.5632818+08:00` | `38926D226ABC` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `4A6E36C8EA66` | `legacy-source-only` |

### OperatorType.FrequencyFilter / 频域滤波
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `52A0030491EE` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `82374D249D1B` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `82374D249D1B` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `DB6374932824` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T13:56:16.3361485+08:00` | `0F034FC04450` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `36FDA7F3C776` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `2625B00C0AA9` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `36FDA7F3C776` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `400CEF87F60B` | `legacy-source-only` |

### OperatorType.GapMeasurement / 间隙测量
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `DF0DA993EE66` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `ADAAF51B4237` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `EA606E49F9DF` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `EA606E49F9DF` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `B919FA2DB07F` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `5B71FA7A78D9` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `C9569CFF8449` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `5B71FA7A78D9` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `F8143FA7FDB2` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `CE50242C670F` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `B4480FC082D5` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `81FC48BD52D5` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `EC1A78180B57` | `legacy-source-only` |

### OperatorType.GeoMeasurement / 几何测量
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `1C5CC7256DC5` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `40911190653E` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `40911190653E` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `9DF7C740A24E` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `946DA685AAD7` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `9DF7C740A24E` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T23:09:00.7096859+08:00` | `946DA685AAD7` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T13:57:48.4747228+08:00` | `D59CA00865CD` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `E0AD164B2935` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `B21D3ED23346` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `D3985346D77B` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `8E8DBCE518EA` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `20AC7D63887E` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `8F4ECDF2968D` | `legacy-source-only` |

### OperatorType.GeometricFitting / 几何拟合
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `B460CD780BA2` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `11CDBDDB6504` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `A7AD00CF2CAE` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `A7AD00CF2CAE` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `CD187AD20399` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `89651052C566` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `4C97CFE688D4` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T23:09:00.7096859+08:00` | `89651052C566` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T13:57:48.4747228+08:00` | `2F9F03D7CF54` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `561C6CE8FB9B` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `B5568DC07CE4` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `8DF53D931DAF` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `8C22B86DA1AA` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T19:59:19.7031372+08:00` | `8DF53D931DAF` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `8C22B86DA1AA` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `5F9717089B34` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `911B9BC4CEF9` | `legacy-source-only` |

### OperatorType.GeometricTolerance / 二维几何公差判定
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `818495EA9C4B` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `C0DF9111DD3E` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-13T15:25:22.2877465+08:00` | `9D7BF35D957C` | `legacy-source-only` |
| `1.0.1` | `2026-07-13T11:23:19.7870903+08:00` | `1416D24B3406` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `2B5BD17A77C3` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `2B5BD17A77C3` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `37E631ED6C45` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T10:56:41.0664908+08:00` | `297C7866EFA1` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `7363042656F9` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `620ED06FB5FA` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T23:09:00.7096859+08:00` | `7363042656F9` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T13:57:48.4747228+08:00` | `AA2D1B8DE5C8` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T23:21:39.1176099+08:00` | `AB2F9D1AB907` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `A016FCAF629A` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `1B73C69B7203` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `29267D6F030B` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `D68B1A1FA0EC` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `9BDF9252DECC` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `CC1399C0B8AD` | `legacy-source-only` |

### OperatorType.GlcmTexture / GLCM纹理特征
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.2` | `2026-07-15T20:30:44.7937318+08:00` | `BB1DD1681A4A` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.2` | `2026-07-15T20:16:21.3617917+08:00` | `ED8ADF795903` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `1A1A4D0DA3C2` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `2AE85451CABD` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `2F99D67BB6CA` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `2F99D67BB6CA` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `4C1D4B81C51A` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `876C8124C99F` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `57AF89E8349E` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T23:25:12.6473691+08:00` | `876C8124C99F` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `0AD9F465A363` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T17:07:32.7286304+08:00` | `1D82B3EEEF43` | `legacy-source-only` |

### OperatorType.GradientShapeMatch / 梯度形状匹配
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.0` | `2026-07-15T11:26:25.6098568+08:00` | `1733B12CC72F` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-14T15:07:23.3198726+08:00` | `B4DEA1FBF77C` | `operator-runtime-metadata-v2` |
| `1.1.0` | `2026-07-06T21:35:46.7699945+08:00` | `1DC3499DBEDE` | `legacy-source-only` |
| `1.1.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.1.0` | `2026-06-25T11:35:53.9102407+08:00` | `1DC3499DBEDE` | `legacy-source-only` |
| `1.1.0` | `2026-05-16T11:53:47.4328965+08:00` | `59FB9A89A6DC` | `legacy-source-only` |
| `1.1.0` | `2026-04-29T13:56:16.3361485+08:00` | `79CA6AB94374` | `legacy-source-only` |
| `1.1.0` | `2026-04-29T10:56:41.0664908+08:00` | `7084E0C8A032` | `legacy-source-only` |
| `1.1.0` | `2026-04-28T12:42:25.6626086+08:00` | `5D188ECFFC24` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `1D5483E6F3D3` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T19:14:52.1190277+08:00` | `4B10EFE2FDDF` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `BE1DD761D410` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `6E00E64E04AA` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `BE1DD761D410` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T19:59:19.7031372+08:00` | `6E00E64E04AA` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `BE1DD761D410` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `C4917CE7BE0A` | `legacy-source-only` |

### OperatorType.HandEyeCalibration / 手眼标定
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `CB4D33407E47` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `79F249CD8BD1` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `79F249CD8BD1` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `71337861D1D2` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `37FB38F7A53F` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `71337861D1D2` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `37FB38F7A53F` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `0E8F8C1E43EC` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `2958261D0E81` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `0E8F8C1E43EC` | `legacy-source-only` |

### OperatorType.HandEyeCalibrationValidator / 手眼标定验证
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `5C8B734D7A25` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `919AAF17EDA7` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `919AAF17EDA7` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `6566D5F08092` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:54:57.1979469+08:00` | `D539D536B845` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `E17BBC343ACF` | `legacy-source-only` |
| `1.0.1` | `2026-04-12T20:43:23.0238145+08:00` | `BB1D4F408829` | `legacy-source-only` |
| `1.0.1` | `2026-04-12T13:38:35.2141516+08:00` | `E17BBC343ACF` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T13:37:21.3936141+08:00` | `16D11BE65447` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `0C424A5EA7B2` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `67868CDD56AD` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `5B2B745ACB3E` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `67868CDD56AD` | `legacy-source-only` |

### OperatorType.HistogramAnalysis / 直方图分析
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.0` | `2026-07-15T17:25:56.0119276+08:00` | `CBB6DAABA04D` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-15T11:26:25.6098568+08:00` | `E30614EC00BB` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `758C84C0893C` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `759457FB33A8` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `759457FB33A8` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `B4BFE43B4160` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `CEEF418CF4C8` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `B4BFE43B4160` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T13:57:48.4747228+08:00` | `CEEF418CF4C8` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `A1A8C84771EA` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `C6D017A36ED7` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `01897F1C223A` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `8515841FC225` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `8B682154A78D` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `A33C328B07D8` | `legacy-source-only` |

### OperatorType.HistogramEqualization / 直方图均衡化
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `58C571870EA4` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `15E73A766744` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `FAB7AB792CC0` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `FAB7AB792CC0` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `D041298739A0` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `94D272B6626B` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `D041298739A0` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `DEF0F4F6E9C5` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `279F9C6A031C` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `4F533BE2BFD2` | `legacy-source-only` |

### OperatorType.HttpRequest / HTTP 请求
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `5D37FA5972A9` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `997E208EAA17` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `997E208EAA17` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `054CB8A6C997` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `933DF4F86055` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `F4B99380F765` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `933DF4F86055` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T01:00:41.9846479+08:00` | `FEE9B353B7DD` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `5329BD29F5B3` | `legacy-source-only` |

### OperatorType.ImageAcquisition / 图像采集
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `A71333E37772` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `95FAF1A2F92B` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `331645B2B66D` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T11:23:19.7870903+08:00` | `7B4F89070B01` | `legacy-source-only` |
| `1.0.0` | `2026-07-09T09:44:47.2036663+08:00` | `D8424FD5D035` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `3875EB215795` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `085840897431` | `legacy-source-only` |
| `1.0.0` | `2026-05-20T23:16:04.5632818+08:00` | `FD748A5E641F` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `930D57E0B61B` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:11:27.4197640+08:00` | `052E255BB7BA` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `CF45EDA52ADC` | `legacy-source-only` |
| `1.0.0` | `2026-05-06T20:55:32.4532274+08:00` | `529DA9900D8E` | `legacy-source-only` |
| `1.0.0` | `2026-05-06T20:33:19.1208969+08:00` | `A50DC61A05BE` | `legacy-source-only` |
| `1.0.0` | `2026-05-04T22:48:13.3257374+08:00` | `75F9DB2F8F43` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `7583C279AD30` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `94FF4EB8EBE5` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `ADB472FDFADC` | `legacy-source-only` |
| `1.0.0` | `2026-02-27T13:55:09.8962327+08:00` | `E826D45C253D` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `0B7EA0C62AEC` | `legacy-source-only` |

### OperatorType.ImageAdd / 图像加法
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `F5F9DF63BB57` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `4C728D8A03D9` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `0532219B88AF` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `0532219B88AF` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `C928FAED5F36` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T23:36:31.6119368+08:00` | `97EEFC780996` | `legacy-source-only` |
| `1.0.0` | `2026-02-27T09:08:37.3873065+08:00` | `0CDCBE571A32` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `68648F5303AB` | `legacy-source-only` |

### OperatorType.ImageBlend / 图像融合
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `64DC1EE1FA52` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `E3B8BCA864F2` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `4607AD098E70` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `4607AD098E70` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `01C2BFA9B87D` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `EFB0D9B90191` | `legacy-source-only` |

### OperatorType.ImageCompose / 图像组合
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `1FDE0B669EC3` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `B0898E521BBE` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `94B83FD2A4FE` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `94B83FD2A4FE` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `A19A3F9DE35B` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `023726942CCB` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `739E80EBA720` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `023726942CCB` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `739E80EBA720` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `023726942CCB` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `D087BD94D51F` | `legacy-source-only` |

### OperatorType.ImageCrop / 图像裁剪
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `47402FEBCC97` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `3990A89C757A` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `8152A88E3E69` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `8152A88E3E69` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `C5A85DF6AB4B` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `A83435868402` | `legacy-source-only` |

### OperatorType.ImageDiff / 图像差异率分析
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `33FBFBD54D70` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `3C3DC2A9FC03` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-13T15:25:22.2877465+08:00` | `127B9F14AEA5` | `legacy-source-only` |
| `1.0.1` | `2026-07-13T11:32:36.5245133+08:00` | `42A6BE17BF23` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `067A0DC13218` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `067A0DC13218` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `3DB7F5D36000` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `C891A8BB5072` | `legacy-source-only` |

### OperatorType.ImageNormalize / 图像归一化
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.3` | `2026-07-15T17:25:56.0119276+08:00` | `D635DBB61BF6` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.3` | `2026-07-15T11:26:25.6098568+08:00` | `F76410BF2620` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.2` | `2026-07-14T20:53:26.4350488+08:00` | `D369FB9E53E7` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-14T19:30:44.4677421+08:00` | `48E61F15938A` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `0D788CB2E0E5` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `7FEB76F136A6` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `7FEB76F136A6` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `E7CD36D371B1` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `D3E68AE79672` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `ABFCF0BAF884` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `D3E68AE79672` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `EAC81104D863` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `C13B4C8B12BF` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `EAC81104D863` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `4510C4122028` | `legacy-source-only` |

### OperatorType.ImageResize / 图像缩放
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `226A0498FC6B` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `2561198C2D09` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `EA9C6E9AAE83` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `EA9C6E9AAE83` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `289037968A26` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `DD2E746E9FED` | `legacy-source-only` |

### OperatorType.ImageRotate / 图像旋转
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `C4AF5D397F39` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `4A0E9E7169D2` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `C78828365011` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `C78828365011` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `523ED6397AE2` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `477E05F0F405` | `legacy-source-only` |

### OperatorType.ImageSave / 图像保存
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `1AC3405B713E` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `03D5A3B8B0D3` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `0F0E24725D6F` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T11:23:19.7870903+08:00` | `A63CBA2B88B0` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `3FAD5F248923` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `5932E99DB37A` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `535EC369B3FC` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T13:56:16.3361485+08:00` | `598ECA4F3AD6` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `8D9536A10076` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `D35AD95D2BD6` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `8D9536A10076` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `0AF22B8BF03D` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `5F4D6954858A` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `0AF22B8BF03D` | `legacy-source-only` |

### OperatorType.ImageStitching / 图像拼接
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `A51B1D1AD26F` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `1F897E4A01BF` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `0DADBEF6C721` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `0DADBEF6C721` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `583B6706C597` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T00:59:13.5713917+08:00` | `E746C0A503FC` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `DB2912FE4AC5` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `1871F48CC569` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `DB2912FE4AC5` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `6E49A6E1A982` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `6BEF54EF7AED` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `6E49A6E1A982` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `648EE37D0E05` | `legacy-source-only` |

### OperatorType.ImageSubtract / 图像减法
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `9794085B1D72` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `C641F678AEBE` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `8AA4CF71333E` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `8AA4CF71333E` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `BB6FA7671D3D` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `C55253F2EBBF` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `BB6FA7671D3D` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `C55253F2EBBF` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `BB6FA7671D3D` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `C55253F2EBBF` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `01B4C5663412` | `legacy-source-only` |

### OperatorType.ImageTiling / 图像切片
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `D96F87D16F84` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `92FCAFF96AFB` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `646CF44FB1F1` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `646CF44FB1F1` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `3F3C831A03DE` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `98FA118539D1` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `2B7CC3AE5B97` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `98FA118539D1` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `2B7CC3AE5B97` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `98FA118539D1` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `E5252D9D290F` | `legacy-source-only` |

### OperatorType.InverseFFT1D / 信号/图像逆傅里叶变换（IFFT）
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `D51743C9C49B` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T19:08:24.4997898+08:00` | `BDB934A52583` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `E37F539412D4` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `F0ECC1050CF0` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `F0ECC1050CF0` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `511D14EA2920` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `A5F09358E01F` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `CF819FE431AF` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `A5F09358E01F` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `6820133964C4` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `C1A839CCFE84` | `legacy-source-only` |

### OperatorType.JsonExtractor / JSON 提取器
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `88265F6416E4` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `944884BF6726` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `944884BF6726` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `B7A866AC5CD8` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `22D51A35DACD` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `B7A866AC5CD8` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T22:27:39.4611441+08:00` | `F49876D4D5C3` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `FA594CCE59D4` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `170544CA4FA5` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `F3C38D7AD62D` | `legacy-source-only` |

### OperatorType.LaplacianSharpen / 拉普拉斯锐化
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.3` | `2026-07-15T17:25:56.0119276+08:00` | `5FEA5C648C6B` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.3` | `2026-07-15T11:26:25.6098568+08:00` | `3B9A71B0CB21` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.2` | `2026-07-14T19:47:48.6428823+08:00` | `013E6A13462C` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `8FE92DA15405` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `34AD327F6589` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `34AD327F6589` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `8BE0560EBB60` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `7F718355AFC8` | `legacy-source-only` |

### OperatorType.LawsTextureFilter / Laws纹理滤波
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `F78650EEAF36` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `9FA8B532141D` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `C10D983101BC` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `C10D983101BC` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `24EB08A69B3B` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `2603C53F2983` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `CDC59096C72F` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T23:25:12.6473691+08:00` | `2603C53F2983` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `40D5DCDD709C` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T16:52:44.2376192+08:00` | `261438B3A43B` | `legacy-source-only` |

### OperatorType.LineLineDistance / 线线距离
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `249BEE4DFDAC` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `1767E28794E2` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `1767E28794E2` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `75E91C67BC93` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `ABBB20C38403` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `75E91C67BC93` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T13:57:48.4747228+08:00` | `ABBB20C38403` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `5843980A97C5` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `FFC6ED93DD57` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `50EFBA259A3A` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `DB4FE7BE4F4D` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `6218C76C8208` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `C4197D651346` | `legacy-source-only` |

### OperatorType.LineMeasurement / 直线测量
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.2.1` | `2026-07-16T01:07:27.7681703+08:00` | `49DA9D751D73` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.2.0` | `2026-07-16T01:06:46.0334378+08:00` | `C248D07D58A3` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.2.0` | `2026-07-16T00:58:49.9337226+08:00` | `1CCEC479C739` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-16T00:57:29.1622202+08:00` | `CDEA017EED13` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-15T23:17:17.3688808+08:00` | `2864B65050C1` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-15T23:08:03.2424341+08:00` | `8854E9BC6026` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `482B52D14198` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `9AE084250AAE` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `1AF5ECC7EB11` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `1AF5ECC7EB11` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `BE28B9E832AC` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `D11009553B08` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `BE28B9E832AC` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `D11009553B08` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `AB0B4EEF67D7` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `482500224EB3` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `2FF0E868B0A4` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `FB7CB0898D13` | `legacy-source-only` |

### OperatorType.LocalDeformableMatching / 局部可变形匹配
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.1` | `2026-07-15T17:25:56.0119276+08:00` | `70B3157E202F` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.1` | `2026-07-15T11:26:25.6098568+08:00` | `520C64505DB2` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.1` | `2026-07-14T15:07:23.3198726+08:00` | `0106A4D36DD3` | `operator-runtime-metadata-v2` |
| `1.1.1` | `2026-07-06T21:35:46.7699945+08:00` | `2B2EB6371AA4` | `legacy-source-only` |
| `1.1.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.1.1` | `2026-07-01T01:12:41.9920283+08:00` | `2B2EB6371AA4` | `legacy-source-only` |
| `1.1.1` | `2026-06-25T11:35:53.9102407+08:00` | `6C1D07107960` | `legacy-source-only` |
| `1.1.1` | `2026-05-16T11:53:47.4328965+08:00` | `C347C2437732` | `legacy-source-only` |
| `1.1.1` | `2026-05-10T12:48:32.0866998+08:00` | `2B7077FF59F2` | `legacy-source-only` |
| `1.1.1` | `2026-04-28T19:39:42.8097784+08:00` | `74C13D77D48A` | `legacy-source-only` |
| `1.1.1` | `2026-04-28T10:51:32.3393648+08:00` | `66C68667ECF8` | `legacy-source-only` |
| `1.1.1` | `2026-04-24T23:25:12.6473691+08:00` | `74C13D77D48A` | `legacy-source-only` |
| `1.1.0` | `2026-04-18T22:49:10.0250597+08:00` | `A6291C9EBAE5` | `legacy-source-only` |
| `1.0.4` | `2026-04-13T19:14:52.1190277+08:00` | `D0691119B5D3` | `legacy-source-only` |
| `1.0.4` | `2026-04-12T20:43:23.0238145+08:00` | `8333DAA86430` | `legacy-source-only` |
| `1.0.4` | `2026-04-12T12:53:52.9929473+08:00` | `9AD0E95A8C88` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `95A32A24B967` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `D31FBB0020ED` | `legacy-source-only` |

### OperatorType.LogicGate / 逻辑门
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `EBC0DC57AB54` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `155D870E2E00` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `155D870E2E00` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `88A6495351DE` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `A41D7D091E3D` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `88A6495351DE` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `EC2FE73781FE` | `legacy-source-only` |

### OperatorType.MathOperation / 数值计算
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `7199E66A395C` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `B93BDAFECECF` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `B93BDAFECECF` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `E416C3C0E2CE` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `9E58EDC189B9` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `E416C3C0E2CE` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `9E58EDC189B9` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `EC1803C8E9A9` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `985B1E155628` | `legacy-source-only` |

### OperatorType.MeanFilter / 均值滤波
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.0` | `2026-07-15T17:25:56.0119276+08:00` | `AD6FC35D40C9` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-15T11:26:25.6098568+08:00` | `C28DDAA64F11` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `80898427BC64` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T19:08:24.4997898+08:00` | `7AEFDE1FD3AC` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `A1F41A8612BB` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `A1F41A8612BB` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `23168BEB9E7F` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `DD0BC13363D3` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `23168BEB9E7F` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `DD0BC13363D3` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `23168BEB9E7F` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `DD0BC13363D3` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `ED740E081606` | `legacy-source-only` |

### OperatorType.Measurement / 测量
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.0` | `2026-07-15T11:26:25.6098568+08:00` | `636C65F41928` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-14T15:07:23.3198726+08:00` | `ABE05241039C` | `operator-runtime-metadata-v2` |
| `1.1.0` | `2026-07-14T06:44:39.4853382+08:00` | `188FA5AA3C2C` | `legacy-source-only` |
| `1.1.0` | `2026-07-13T19:31:36.3593491+08:00` | `5E984D5C3F8A` | `legacy-source-only` |
| `1.1.0` | `2026-07-13T19:08:24.4997898+08:00` | `D7E2B739EC5E` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `F6EE8AFE310E` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `F6EE8AFE310E` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `40394990EE68` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `0191042C7424` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `40394990EE68` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T13:57:48.4747228+08:00` | `0191042C7424` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `7B17102BB6A1` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `1A22FD221339` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `91AB87431111` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `4016CC586C1F` | `legacy-source-only` |

### OperatorType.MedianBlur / 中值滤波
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.0` | `2026-07-15T17:25:56.0119276+08:00` | `466C4AA2FC57` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-15T11:26:25.6098568+08:00` | `A6FD5F7191FE` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `7BA2CF90B1DF` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T19:08:24.4997898+08:00` | `0584E7F41C4F` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `142E9C987CAF` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `142E9C987CAF` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `A9FDA79A012E` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `2809B653B975` | `legacy-source-only` |

### OperatorType.MinEnclosingGeometry / 最小外接几何体
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `5B1D43A43790` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `20BAD126AE7F` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `498F95F4E6D6` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `498F95F4E6D6` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `5790A79A8080` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `E5137B091A95` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `5730B690B9F2` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T23:25:12.6473691+08:00` | `E5137B091A95` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `1575F5D91455` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `9A00C29AC41A` | `legacy-source-only` |

### OperatorType.MitsubishiMcCommunication / 三菱MC通信
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `A392A906BB00` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `BCC4F47D87A7` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T11:23:19.7870903+08:00` | `92743F4BBC28` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `7CCA6AB8815E` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `13418D49A5FC` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `D8CFD8EC6196` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `30A1C11E92FA` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `D8CFD8EC6196` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T23:21:39.1176099+08:00` | `30A1C11E92FA` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `6DA73E6E8D2B` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `64EF0C1DC0E3` | `legacy-source-only` |
| `1.0.0` | `2026-02-27T21:39:45.2435118+08:00` | `6DA73E6E8D2B` | `legacy-source-only` |
| `1.0.0` | `2026-02-27T21:13:09.9008744+08:00` | `6FCA4034036C` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `5CBB32DC2CED` | `legacy-source-only` |

### OperatorType.ModbusCommunication / Modbus TCP通信
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `F44B9E02DAF4` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T19:08:24.4997898+08:00` | `0E80FF1315C9` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T15:30:35.6120213+08:00` | `861C9C537804` | `legacy-source-only` |
| `1.0.0` | `2026-07-08T13:53:08.5202166+08:00` | `8CA74BF88B49` | `legacy-source-only` |
| `1.0.0` | `2026-07-08T13:51:33.9970116+08:00` | `A02E1D91FE24` | `legacy-source-only` |
| `1.0.0` | `2026-07-08T13:44:48.7501564+08:00` | `8CA74BF88B49` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `993A2A46E827` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `993A2A46E827` | `legacy-source-only` |
| `1.0.0` | `2026-06-01T01:11:17.9618416+08:00` | `8703BB6001B1` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `D0DE04C84A90` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T17:13:24.7548110+08:00` | `A44E72F03DB4` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T10:56:41.0664908+08:00` | `F61D0A205F88` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `30D6D8AF1CA6` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `A97F92EE13CF` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `30D6D8AF1CA6` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `EAC52E7749F1` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `2FB888EEAEA1` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `EAC52E7749F1` | `legacy-source-only` |

### OperatorType.MorphologicalOperation / 形态学操作
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `749575BD09E1` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `5CC135A20B82` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `AC68E55BF5BB` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `AC68E55BF5BB` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `9F96B5773EA8` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `F4895E5C1243` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `9F96B5773EA8` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `F4895E5C1243` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `9F96B5773EA8` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `F4895E5C1243` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `C4E6D9300B08` | `legacy-source-only` |

### OperatorType.Morphology / 形态学（旧版）
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `8BCC72D1E70D` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `972FD419A523` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `6AA3D0EC541D` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `C63487D59ECC` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `32A98594911C` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `D2E82EF12966` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `D50D116508C7` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `D2E82EF12966` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `D68EB476BBEF` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `DD34DE7B5726` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `D68EB476BBEF` | `legacy-source-only` |
| `1.0.0` | `2026-02-27T09:08:37.3873065+08:00` | `3161073CD194` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `7D8DED829483` | `legacy-source-only` |

### OperatorType.MqttPublish / MQTT 发布
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `0.1.0` | `2026-07-14T15:07:23.3198726+08:00` | `4F56C778A447` | `operator-runtime-metadata-v2` |
| `0.1.0` | `2026-07-06T21:35:46.7699945+08:00` | `6CC83E130E30` | `legacy-source-only` |
| `0.1.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `0.1.0` | `2026-06-25T11:35:53.9102407+08:00` | `CF6DDC77C0C8` | `legacy-source-only` |
| `0.1.0` | `2026-05-16T11:53:47.4328965+08:00` | `0D070FAB720F` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `0D070FAB720F` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `0875F6C2B395` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T19:59:19.7031372+08:00` | `EDF3937B18BD` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T01:00:41.9846479+08:00` | `FFCB190578C7` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `7F475495E079` | `legacy-source-only` |

### OperatorType.NPointCalibration / N点标定
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `5AA2FA9BB23C` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `1BA75A3673AC` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `0D5E991D8429` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `8146925C3F26` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-07-04T03:58:57.0633505+08:00` | `8146925C3F26` | `legacy-source-only` |
| `1.0.0` | `2026-07-03T01:55:37.8923615+08:00` | `764B5FEE6A23` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `2E440912E7B6` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `E3B3332A63D6` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T17:13:24.7548110+08:00` | `8CCBC19C9C43` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `CB0466E99CD8` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `5E8C999B02AC` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `CB0466E99CD8` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `E2EEB069BA7B` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `7FBC8F939D41` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `170692A628FB` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `3DF5A204BEE4` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `0B9DBE2431AE` | `legacy-source-only` |

### OperatorType.OcrRecognition / OCR 识别
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `53D8EEF9DD43` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `11654593C6E3` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `BB769FC1767D` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `BB769FC1767D` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `C7CBB4487B32` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `7FC79F721D84` | `legacy-source-only` |

### OperatorType.OmronFinsCommunication / 欧姆龙FINS通信
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `636ED45E6067` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `D661A9481284` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `CF217C03E297` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `A7E735CDBE98` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `7281D7A2BCA1` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `6B35BA2B0309` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T23:21:39.1176099+08:00` | `7281D7A2BCA1` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `6041B1ADB2F0` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `F11484F49198` | `legacy-source-only` |
| `1.0.0` | `2026-02-27T21:39:45.2435118+08:00` | `6041B1ADB2F0` | `legacy-source-only` |
| `1.0.0` | `2026-02-27T21:13:09.9008744+08:00` | `63542A557B6C` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `DC5EBDDA2BD2` | `legacy-source-only` |

### OperatorType.OrbFeatureMatch / ORB特征匹配
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `7DE1535831EA` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `EF069F000E61` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `D1E1BCB0035F` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-07-01T01:12:41.9920283+08:00` | `D1E1BCB0035F` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `7DB3D92F4325` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `695AE1FCC331` | `legacy-source-only` |
| `1.0.0` | `2026-05-01T14:53:33.8287356+08:00` | `1F4F6EA3B5AC` | `legacy-source-only` |
| `1.0.0` | `2026-05-01T10:19:56.3600494+08:00` | `EC10A8BC50CB` | `legacy-source-only` |
| `1.0.0` | `2026-04-30T08:08:30.0876185+08:00` | `356DB9EEA276` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `CB74B6EE1EFA` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:54:57.1979469+08:00` | `4987E75F171F` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `7C4CFDA1AF08` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `DA425F814A14` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T19:14:52.1190277+08:00` | `04BE1B17CC9C` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `AE7C4204010A` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T10:35:29.6469155+08:00` | `024F1AD3150C` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `260BD860C2E1` | `legacy-source-only` |

### OperatorType.PPFEstimation / PPF点对特征
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `8C2698E9B424` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `F24ACBDDAC43` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `F24ACBDDAC43` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `DA072308815D` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T16:06:03.0736962+08:00` | `A311D862B07B` | `legacy-source-only` |

### OperatorType.PPFMatch / PPF点云粗匹配
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.5` | `2026-07-14T15:07:23.3198726+08:00` | `4E2D8AF6A1CC` | `operator-runtime-metadata-v2` |
| `1.0.5` | `2026-07-13T15:25:22.2877465+08:00` | `F75E15AF75FA` | `legacy-source-only` |
| `1.0.5` | `2026-07-13T11:23:19.7870903+08:00` | `43D0E5363D89` | `legacy-source-only` |
| `1.0.4` | `2026-07-06T21:35:46.7699945+08:00` | `3A7C32AAED74` | `legacy-source-only` |
| `1.0.4` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.4` | `2026-06-25T11:35:53.9102407+08:00` | `3A7C32AAED74` | `legacy-source-only` |
| `1.0.4` | `2026-04-28T19:39:42.8097784+08:00` | `7345BE969AA8` | `legacy-source-only` |
| `1.0.4` | `2026-04-28T10:51:32.3393648+08:00` | `618C04BD1448` | `legacy-source-only` |
| `1.0.4` | `2026-04-18T22:49:10.0250597+08:00` | `7345BE969AA8` | `legacy-source-only` |
| `1.0.4` | `2026-04-12T20:43:23.0238145+08:00` | `A849550406CB` | `legacy-source-only` |
| `1.0.4` | `2026-04-12T12:53:52.9929473+08:00` | `0C6B3AA3DE69` | `legacy-source-only` |
| `1.0.1` | `2026-03-21T01:38:49.8374844+08:00` | `0D0FDDC9C625` | `legacy-source-only` |
| `1.0.1` | `2026-03-18T19:00:25.2910689+08:00` | `E64DEFAE4B95` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T16:34:20.9153387+08:00` | `3F62675CEB39` | `legacy-source-only` |

### OperatorType.ParallelLineFind / 平行线查找
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `A1AD663C8E00` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `262E18F97689` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `21E61A7B1375` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `21E61A7B1375` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `8FC4187A1E45` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `0A6FE3B5601B` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `8FC4187A1E45` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T19:14:52.1190277+08:00` | `0A6FE3B5601B` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `D67465BA6D4D` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `F11A9F3E6855` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `D67465BA6D4D` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `26F9114FF5A8` | `legacy-source-only` |

### OperatorType.PerspectiveTransform / 透视变换
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `2B877F2C6FDE` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `F1EC2768ED2C` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `BF0DD8CDF660` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `BF0DD8CDF660` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `B8FCC05F617A` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `967413F4AD69` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `B8FCC05F617A` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T23:21:39.1176099+08:00` | `967413F4AD69` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `683816ED05A1` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `C56528ABD9AF` | `legacy-source-only` |
| `1.0.0` | `2026-02-27T09:08:37.3873065+08:00` | `683816ED05A1` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `386962DF7E0B` | `legacy-source-only` |

### OperatorType.PhaseClosure / 相位解缠绕
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-15T17:25:56.0119276+08:00` | `6B0C1E1A6E8F` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `800C7AA92FF0` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `957A43F50F4A` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-13T19:08:24.4997898+08:00` | `8E9923CE7B6B` | `legacy-source-only` |
| `1.0.1` | `2026-07-13T18:54:34.0417583+08:00` | `744DA119B81F` | `legacy-source-only` |
| `1.0.1` | `2026-07-13T15:25:22.2877465+08:00` | `3BF667E4992E` | `legacy-source-only` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `CDACB29FBAA6` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `CDACB29FBAA6` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `A87DA7243082` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `F89CB39F71A3` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `A87DA7243082` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T23:15:19.4916106+08:00` | `F89CB39F71A3` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `10DB4D181363` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `598E1EA010DF` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `0AA74A983A97` | `legacy-source-only` |

### OperatorType.PixelStatistics / 像素统计
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-15T20:30:44.7937318+08:00` | `7D84BF747A71` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-15T20:16:21.3617917+08:00` | `5672DBF54B24` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `A0DE399710B0` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `B50999A3C27D` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `71F99B0EF139` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `71F99B0EF139` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `9AEF516248F8` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:11:27.4197640+08:00` | `C77E9BCEAE3B` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `CC87573DD903` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T17:13:24.7548110+08:00` | `0D59C9731466` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T13:56:16.3361485+08:00` | `3CE1380B567A` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `EAC2DE279DF5` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `57161403304D` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T13:57:48.4747228+08:00` | `EAC2DE279DF5` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `997FBE6B445A` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `9AABA0434C8A` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `86B857B8747F` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `77ED8F58B715` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `3E845B989BC1` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `82DC78427BAF` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `4EB06D674A3D` | `legacy-source-only` |

### OperatorType.PixelToWorldTransform / 像素世界映射
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `33D735CF78CA` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `FD7939D3C3AA` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `32633EFA4832` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-07-03T17:48:32.1194342+08:00` | `8A3436BB59CA` | `legacy-source-only` |
| `1.0.0` | `2026-07-03T15:57:48.3660532+08:00` | `B24753A04A85` | `legacy-source-only` |
| `1.0.0` | `2026-07-03T14:28:34.0693635+08:00` | `65AE6421AE2F` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `14DE66586187` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `A9239E09422B` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T17:13:24.7548110+08:00` | `18729C511F46` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `0E40C0FDDC61` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `820C23FE98C5` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `0E40C0FDDC61` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T23:21:39.1176099+08:00` | `CFFB6BAB9A61` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `C19F560B8590` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `6B3BFC7109C5` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `863B5827D277` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `DD71E7F3F1AA` | `legacy-source-only` |

### OperatorType.PlanarMatching / 平面特征匹配
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.3` | `2026-07-15T11:26:25.6098568+08:00` | `3C07D1FA4807` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.3` | `2026-07-14T15:07:23.3198726+08:00` | `7FDE7A98936E` | `operator-runtime-metadata-v2` |
| `1.1.3` | `2026-07-13T15:25:22.2877465+08:00` | `E9BD4D2708D7` | `legacy-source-only` |
| `1.1.3` | `2026-07-13T11:23:19.7870903+08:00` | `FA565C7540E3` | `legacy-source-only` |
| `1.1.2` | `2026-07-06T21:35:46.7699945+08:00` | `7232A36C968D` | `legacy-source-only` |
| `1.1.2` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.1.2` | `2026-06-25T11:35:53.9102407+08:00` | `7232A36C968D` | `legacy-source-only` |
| `1.1.2` | `2026-05-16T11:53:47.4328965+08:00` | `1DB792B29411` | `legacy-source-only` |
| `1.1.2` | `2026-05-01T10:19:56.3600494+08:00` | `AAE4B69BEAB5` | `legacy-source-only` |
| `1.1.2` | `2026-04-30T08:08:30.0876185+08:00` | `BBD7547D0ADC` | `legacy-source-only` |
| `1.1.2` | `2026-04-28T19:39:42.8097784+08:00` | `11D54C8907EE` | `legacy-source-only` |
| `1.1.2` | `2026-04-28T10:51:32.3393648+08:00` | `FF61ABF9729D` | `legacy-source-only` |
| `1.1.2` | `2026-04-24T23:25:12.6473691+08:00` | `11D54C8907EE` | `legacy-source-only` |
| `1.1.1` | `2026-04-18T22:49:10.0250597+08:00` | `547733BF24E3` | `legacy-source-only` |
| `1.1.1` | `2026-04-13T19:14:52.1190277+08:00` | `804A46C17AD8` | `legacy-source-only` |
| `1.1.1` | `2026-04-12T20:43:23.0238145+08:00` | `CD1D6DEF0F5F` | `legacy-source-only` |
| `1.1.1` | `2026-04-12T12:53:52.9929473+08:00` | `F97D9B2286D6` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `7DF103AFF77A` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `2706B43F0B0E` | `legacy-source-only` |

### OperatorType.PointAlignment / 点位偏差计算
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.4` | `2026-07-14T15:07:23.3198726+08:00` | `82EAB47A9674` | `operator-runtime-metadata-v2` |
| `1.0.4` | `2026-07-13T15:25:22.2877465+08:00` | `67128FA2C6BE` | `legacy-source-only` |
| `1.0.4` | `2026-07-13T11:28:20.9457107+08:00` | `24C2F6B7400D` | `legacy-source-only` |
| `1.0.4` | `2026-07-13T11:23:19.7870903+08:00` | `81BAFDF458DE` | `legacy-source-only` |
| `1.0.3` | `2026-07-06T21:35:46.7699945+08:00` | `41A018E7D742` | `legacy-source-only` |
| `1.0.3` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.3` | `2026-06-25T11:35:53.9102407+08:00` | `41A018E7D742` | `legacy-source-only` |
| `1.0.3` | `2026-05-16T11:53:47.4328965+08:00` | `38433D21EAD8` | `legacy-source-only` |
| `1.0.3` | `2026-04-28T19:39:42.8097784+08:00` | `E8839DA96494` | `legacy-source-only` |
| `1.0.3` | `2026-04-28T10:51:32.3393648+08:00` | `5769ADE0033C` | `legacy-source-only` |
| `1.0.3` | `2026-04-12T22:27:39.4611441+08:00` | `E8839DA96494` | `legacy-source-only` |
| `1.0.3` | `2026-04-12T20:43:23.0238145+08:00` | `8B1E30AFA789` | `legacy-source-only` |
| `1.0.1` | `2026-04-12T12:53:52.9929473+08:00` | `87ED7F7672ED` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `7D3ED24D3273` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `6732C97572C2` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `E4A8E6F775A9` | `legacy-source-only` |

### OperatorType.PointCorrection / 点位刚性补偿
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.4` | `2026-07-14T15:07:23.3198726+08:00` | `6D2D29704E5A` | `operator-runtime-metadata-v2` |
| `1.0.4` | `2026-07-13T15:25:22.2877465+08:00` | `F38FA5729C51` | `legacy-source-only` |
| `1.0.4` | `2026-07-13T11:28:20.9457107+08:00` | `019C59C6C8EB` | `legacy-source-only` |
| `1.0.4` | `2026-07-13T11:23:19.7870903+08:00` | `A9FD7539C3E3` | `legacy-source-only` |
| `1.0.3` | `2026-07-06T21:35:46.7699945+08:00` | `C8B056880339` | `legacy-source-only` |
| `1.0.3` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.3` | `2026-06-25T11:35:53.9102407+08:00` | `C8B056880339` | `legacy-source-only` |
| `1.0.3` | `2026-05-16T11:53:47.4328965+08:00` | `AD716CFD767C` | `legacy-source-only` |
| `1.0.3` | `2026-04-28T19:39:42.8097784+08:00` | `C6C5996F4285` | `legacy-source-only` |
| `1.0.3` | `2026-04-28T10:51:32.3393648+08:00` | `0AAD72DEA70B` | `legacy-source-only` |
| `1.0.3` | `2026-04-12T22:27:39.4611441+08:00` | `C6C5996F4285` | `legacy-source-only` |
| `1.0.3` | `2026-04-12T20:43:23.0238145+08:00` | `765775619C59` | `legacy-source-only` |
| `1.0.1` | `2026-04-12T12:53:52.9929473+08:00` | `6AA553FE68DD` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `C1DFFAC1D1D8` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `324DE476C870` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `815617758954` | `legacy-source-only` |

### OperatorType.PointLineDistance / 点线距离
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `76C29D87909B` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `1FD9A968C223` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `1FD9A968C223` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `B52DF5F2C90E` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `263D1CBA61AA` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `B52DF5F2C90E` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T13:57:48.4747228+08:00` | `263D1CBA61AA` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `A010E18F79D6` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `AB9761C58FAA` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `84F85377B4C7` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `7C11CE8D231C` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `FCE5924A5358` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `EB16F20EF164` | `legacy-source-only` |

### OperatorType.PointSetTool / 点集工具
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `FB08128EEF7D` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `EC6480D40F81` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `EC6480D40F81` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `DBAD0C90624F` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `C86961A6A036` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `50908877001E` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `C86961A6A036` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `50908877001E` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `C86961A6A036` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `32673C56E0B9` | `legacy-source-only` |

### OperatorType.PolarUnwrap / 极坐标展开
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `9E7C1802E2AB` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `B00804C1C0AE` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `11255525EFA8` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `11255525EFA8` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `8BEAF8DE5095` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `886EAF34FEA7` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `8BEAF8DE5095` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `886EAF34FEA7` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `8BEAF8DE5095` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `886EAF34FEA7` | `legacy-source-only` |
| `1.0.0` | `2026-02-27T09:08:37.3873065+08:00` | `AC99929E34C4` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `688C288F636E` | `legacy-source-only` |

### OperatorType.PositionCorrection / ROI位姿补偿（像素）
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.3` | `2026-07-14T15:07:23.3198726+08:00` | `0EE01A4E9F45` | `operator-runtime-metadata-v2` |
| `1.0.3` | `2026-07-13T15:25:22.2877465+08:00` | `A51ECAE265C4` | `legacy-source-only` |
| `1.0.3` | `2026-07-13T11:28:20.9457107+08:00` | `597BDF853BB7` | `legacy-source-only` |
| `1.0.3` | `2026-07-13T11:23:19.7870903+08:00` | `FF598DEA7928` | `legacy-source-only` |
| `1.0.2` | `2026-07-06T21:35:46.7699945+08:00` | `DB62ECAE9831` | `legacy-source-only` |
| `1.0.2` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.2` | `2026-06-25T11:35:53.9102407+08:00` | `DB62ECAE9831` | `legacy-source-only` |
| `1.0.2` | `2026-05-16T11:53:47.4328965+08:00` | `AAADB14950C2` | `legacy-source-only` |
| `1.0.2` | `2026-04-28T19:39:42.8097784+08:00` | `ABAE93DF25FF` | `legacy-source-only` |
| `1.0.2` | `2026-04-28T10:51:32.3393648+08:00` | `AAADB14950C2` | `legacy-source-only` |
| `1.0.2` | `2026-04-13T19:14:52.1190277+08:00` | `ABAE93DF25FF` | `legacy-source-only` |
| `1.0.1` | `2026-04-12T20:43:23.0238145+08:00` | `00E035FDDE0C` | `legacy-source-only` |
| `1.0.1` | `2026-04-12T12:53:52.9929473+08:00` | `53B133FF0C30` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `07D84C961991` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `8B093712CDBE` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `A33AE687F32A` | `legacy-source-only` |

### OperatorType.PyramidShapeMatch / 金字塔形状匹配
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `E45766A3B443` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `5520B97DA67B` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `08B542D5546D` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T11:23:19.7870903+08:00` | `1808451BBA2E` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `37E39BC95DB2` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `37E39BC95DB2` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `23640B83C9E8` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:54:57.1979469+08:00` | `93171154DEA9` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `5FA4266DBA12` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T19:14:52.1190277+08:00` | `E8DAE68DA1B6` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T01:00:41.9846479+08:00` | `396248DA73D3` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `9A881861B316` | `legacy-source-only` |

### OperatorType.QuadrilateralFind / 四边形查找
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `086E5FB139C6` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `244C7E7D777B` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `3C6C083AE6CA` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `3C6C083AE6CA` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `F7371968FC1B` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `341C4A7B9542` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `F7371968FC1B` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T19:14:52.1190277+08:00` | `16A9D5D73866` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `B4D7EBCD05FB` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `11857A8CD15C` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `B4D7EBCD05FB` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `1CCDD43E1B39` | `legacy-source-only` |

### OperatorType.RansacPlaneSegmentation / RANSAC平面分割
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `A6A453B4C35A` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `00D4EBC1D2D3` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `00D4EBC1D2D3` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `052EBCF0B556` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `EC616E266B32` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `052EBCF0B556` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T23:09:00.7096859+08:00` | `EC616E266B32` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `E69D35252071` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T15:37:22.1136239+08:00` | `D692426CF813` | `legacy-source-only` |

### OperatorType.RectangleDetection / 矩形检测
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `5D312E5A2967` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `359E868BF0E1` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `5D0C2C2260E8` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `5D0C2C2260E8` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `CC4C9AC70ED0` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `AB9ED7D2B284` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `CC4C9AC70ED0` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T19:14:52.1190277+08:00` | `1A97B4E10A94` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `D1603F6CA019` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `9A52976090F9` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `D1603F6CA019` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `FBC9E5DEC5C4` | `legacy-source-only` |

### OperatorType.RectangleRegion / 矩形框定义
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `B0C81EEEAFB8` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-13T15:25:22.2877465+08:00` | `97810B9DBD67` | `legacy-source-only` |
| `1.0.1` | `2026-07-13T11:24:34.6274067+08:00` | `18565944D1CE` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T11:23:19.7870903+08:00` | `6121B78706A3` | `legacy-source-only` |

### OperatorType.RegionClosing / 区域闭运算
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.2` | `2026-07-15T11:26:25.6098568+08:00` | `E66B115CC646` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.2` | `2026-07-14T15:07:23.3198726+08:00` | `7588707944E6` | `operator-runtime-metadata-v2` |
| `1.0.2` | `2026-07-13T15:25:22.2877465+08:00` | `270AA8CFE07A` | `legacy-source-only` |
| `1.0.2` | `2026-07-13T11:23:19.7870903+08:00` | `736F75941933` | `legacy-source-only` |
| `1.0.1` | `2026-07-10T11:21:26.9540273+08:00` | `736F75941933` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:45:14.7543995+08:00` | `9F8C899FE255` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:41:52.9221275+08:00` | `600249D3D451` | `legacy-source-only` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `5C97F862041E` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `5C97F862041E` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `B7120C9B84DB` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `1CD4872250EC` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `F92AEDA0142C` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T21:36:45.8180800+08:00` | `1CD4872250EC` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `98C3BA2E149D` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `51C06A810144` | `legacy-source-only` |

### OperatorType.RegionComplement / 区域补集
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.2` | `2026-07-15T11:26:25.6098568+08:00` | `ED7BA8434610` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.2` | `2026-07-14T15:07:23.3198726+08:00` | `A42CAFEDF34C` | `operator-runtime-metadata-v2` |
| `1.0.2` | `2026-07-13T15:25:22.2877465+08:00` | `7D0418CD0C30` | `legacy-source-only` |
| `1.0.2` | `2026-07-13T11:23:19.7870903+08:00` | `3C64CC70D3EE` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:45:14.7543995+08:00` | `3C64CC70D3EE` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:41:52.9221275+08:00` | `960F7FE58053` | `legacy-source-only` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `63B7BFB6617D` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `63B7BFB6617D` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `4AB0BB88DB72` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `2A590B4CC7B3` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `7F8CA6BE2AE4` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T21:36:45.8180800+08:00` | `2A590B4CC7B3` | `legacy-source-only` |
| `1.0.0` | `2026-04-24T20:31:31.4396535+08:00` | `2A900E9345C6` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `2A46F259A55C` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `AE6829D33D5A` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `8BD7259C4E0C` | `legacy-source-only` |

### OperatorType.RegionDifference / 区域差集
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.2` | `2026-07-14T15:07:23.3198726+08:00` | `718BCA617984` | `operator-runtime-metadata-v2` |
| `1.0.2` | `2026-07-13T15:25:22.2877465+08:00` | `6B81FD08A1B6` | `legacy-source-only` |
| `1.0.2` | `2026-07-13T11:23:19.7870903+08:00` | `DC1A4ABA1F01` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:45:14.7543995+08:00` | `DC1A4ABA1F01` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:41:52.9221275+08:00` | `48802B1B0854` | `legacy-source-only` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `5DA4F0F92DC3` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `5DA4F0F92DC3` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `2F835797BBC7` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `352DD8966BC9` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `48C66EDD2D82` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T21:36:45.8180800+08:00` | `352DD8966BC9` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `4035E96533FB` | `legacy-source-only` |

### OperatorType.RegionDilation / 区域膨胀
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.2` | `2026-07-15T11:26:25.6098568+08:00` | `F31025EB47D0` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.2` | `2026-07-14T15:07:23.3198726+08:00` | `A9470C80F68D` | `operator-runtime-metadata-v2` |
| `1.0.2` | `2026-07-13T15:25:22.2877465+08:00` | `EFFB6FF7983A` | `legacy-source-only` |
| `1.0.2` | `2026-07-13T11:23:19.7870903+08:00` | `DF066CCB35E7` | `legacy-source-only` |
| `1.0.1` | `2026-07-10T11:21:26.9540273+08:00` | `DF066CCB35E7` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:45:14.7543995+08:00` | `03CA205B0C01` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:41:52.9221275+08:00` | `4FEF59C65134` | `legacy-source-only` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `2939429F99D3` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `2939429F99D3` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `97DA486E2025` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `4498ABDC40C9` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `50FFBAE87AF7` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T21:36:45.8180800+08:00` | `4498ABDC40C9` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `665F8400F9BB` | `legacy-source-only` |

### OperatorType.RegionErosion / 区域腐蚀
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.2` | `2026-07-15T11:26:25.6098568+08:00` | `22190DF1942B` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.2` | `2026-07-14T15:07:23.3198726+08:00` | `9ED081045FB6` | `operator-runtime-metadata-v2` |
| `1.0.2` | `2026-07-13T15:25:22.2877465+08:00` | `02E180537CB4` | `legacy-source-only` |
| `1.0.2` | `2026-07-13T11:23:19.7870903+08:00` | `284BF94D14E0` | `legacy-source-only` |
| `1.0.1` | `2026-07-10T11:21:26.9540273+08:00` | `284BF94D14E0` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:45:14.7543995+08:00` | `F723EF05C2BE` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:41:52.9221275+08:00` | `529BF25EF8E8` | `legacy-source-only` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `9D8D40A85710` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `9D8D40A85710` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `503F1ADFA732` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `30B22EB3B176` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `7AB55B7F0FCF` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T21:36:45.8180800+08:00` | `30B22EB3B176` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `08885F6CC301` | `legacy-source-only` |

### OperatorType.RegionIntersection / 区域交集
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.2` | `2026-07-14T15:07:23.3198726+08:00` | `C63D9895C11A` | `operator-runtime-metadata-v2` |
| `1.0.2` | `2026-07-13T15:25:22.2877465+08:00` | `4455D425E408` | `legacy-source-only` |
| `1.0.2` | `2026-07-13T11:23:19.7870903+08:00` | `306A3D0DB82E` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:45:14.7543995+08:00` | `306A3D0DB82E` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:41:52.9221275+08:00` | `D76BD641E77C` | `legacy-source-only` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `44850CDF4F72` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `44850CDF4F72` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `268B88E63C1F` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `52CB74CC5F93` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `C0681E83E799` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T21:36:45.8180800+08:00` | `52CB74CC5F93` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `B29DEF5EB888` | `legacy-source-only` |

### OperatorType.RegionOpening / 区域开运算
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.2` | `2026-07-15T11:26:25.6098568+08:00` | `64768EB2EC89` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.2` | `2026-07-14T15:07:23.3198726+08:00` | `49EAED11F315` | `operator-runtime-metadata-v2` |
| `1.0.2` | `2026-07-13T15:25:22.2877465+08:00` | `59846A0C2CBA` | `legacy-source-only` |
| `1.0.2` | `2026-07-13T11:23:19.7870903+08:00` | `ACC9110A131F` | `legacy-source-only` |
| `1.0.1` | `2026-07-10T11:21:26.9540273+08:00` | `ACC9110A131F` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:45:14.7543995+08:00` | `28F381E1B328` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:41:52.9221275+08:00` | `68C17675653B` | `legacy-source-only` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `402C2DFF4814` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `402C2DFF4814` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `96C65D59BEA1` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `5F7760676F85` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `89347424A68D` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T21:36:45.8180800+08:00` | `5F7760676F85` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `8AE1BD3C0157` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `BFA8EED40F07` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `B1DA89F6441D` | `legacy-source-only` |

### OperatorType.RegionSkeleton / 区域骨架化
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.2` | `2026-07-15T11:26:25.6098568+08:00` | `DBAE8FC2DA3D` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.2` | `2026-07-14T15:07:23.3198726+08:00` | `1F4F417A8845` | `operator-runtime-metadata-v2` |
| `1.0.2` | `2026-07-13T15:25:22.2877465+08:00` | `48377D8497F8` | `legacy-source-only` |
| `1.0.2` | `2026-07-13T11:23:19.7870903+08:00` | `7F7AD10483BA` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:45:14.7543995+08:00` | `7F7AD10483BA` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:41:52.9221275+08:00` | `AB2E158382B7` | `legacy-source-only` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `D9ED9065FC29` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `D9ED9065FC29` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `547CEF7E6C75` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `268718F7CC19` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `7F77105543A9` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T21:36:45.8180800+08:00` | `268718F7CC19` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `E0E288BA3665` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `D9F748831F97` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `618A97A0B08E` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `0B331757C5AA` | `legacy-source-only` |

### OperatorType.RegionUnion / 区域并集
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.2` | `2026-07-14T15:07:23.3198726+08:00` | `CB26C72974A4` | `operator-runtime-metadata-v2` |
| `1.0.2` | `2026-07-13T15:25:22.2877465+08:00` | `C547C9114C9E` | `legacy-source-only` |
| `1.0.2` | `2026-07-13T11:23:19.7870903+08:00` | `F73E4EE3B6FE` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:45:14.7543995+08:00` | `F73E4EE3B6FE` | `legacy-source-only` |
| `1.0.1` | `2026-07-07T14:41:52.9221275+08:00` | `D8524250A21A` | `legacy-source-only` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `19D0588C515A` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `19D0588C515A` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `ABFAE1AE2BCD` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `346E066817F1` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `834BB68F4CFC` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T21:36:45.8180800+08:00` | `346E066817F1` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `148881C196DD` | `legacy-source-only` |

### OperatorType.ResultJudgment / 结果判定
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `7333F7792534` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `DE9842D6D240` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `DE9842D6D240` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `A338715C7F4E` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `1BA85E5F1871` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `F17AA0FBCF0C` | `legacy-source-only` |
| `1.0.1` | `2026-04-26T23:05:23.4040741+08:00` | `1BA85E5F1871` | `legacy-source-only` |
| `1.0.0` | `2026-04-26T23:04:22.3546450+08:00` | `CF2A076702DE` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T23:21:39.1176099+08:00` | `FFDC0A297FC6` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `53985764ED7F` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `D6D6EF86DA89` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `53985764ED7F` | `legacy-source-only` |

### OperatorType.ResultOutput / 结果输出
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `0B158C24D2FF` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `6655F87B33D9` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-08T13:57:46.2325088+08:00` | `F238F5781AC8` | `legacy-source-only` |
| `1.0.1` | `2026-07-08T13:51:33.9970116+08:00` | `29C919BC9CA2` | `legacy-source-only` |
| `1.0.1` | `2026-07-08T13:44:48.7501564+08:00` | `F238F5781AC8` | `legacy-source-only` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `29C919BC9CA2` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `29C919BC9CA2` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `DF85C9F7EC3F` | `legacy-source-only` |
| `1.0.1` | `2026-04-29T10:56:41.0664908+08:00` | `2E2EA9583DC6` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `1C23D62F4D9A` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `A737396D3609` | `legacy-source-only` |
| `1.0.1` | `2026-04-22T00:42:55.8987044+08:00` | `1C23D62F4D9A` | `legacy-source-only` |
| `1.0.1` | `2026-03-26T18:46:50.6676488+08:00` | `296E7F76B69C` | `legacy-source-only` |
| `1.0.1` | `2026-03-21T01:38:49.8374844+08:00` | `8547990CCEBC` | `legacy-source-only` |
| `1.0.1` | `2026-03-17T14:30:51.0566057+08:00` | `8165811DA08A` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T14:27:11.6128169+08:00` | `9272BB760587` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T19:59:19.7031372+08:00` | `BECDD0398F2A` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `ED19595D838D` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T11:07:12.6855371+08:00` | `F230E925DC3A` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `CD53E822B204` | `legacy-source-only` |

### OperatorType.RoiManager / ROI裁剪与掩膜
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `1E5FEA43F5BB` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `83688BB94E03` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T19:08:24.4997898+08:00` | `9A970238C67E` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T18:54:34.0417583+08:00` | `13321800C5D5` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `2251160214D2` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T22:52:02.0510182+08:00` | `195EAF5065FC` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `54F1E527103D` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-07-03T10:28:07.0087621+08:00` | `54F1E527103D` | `legacy-source-only` |
| `1.0.0` | `2026-07-03T02:22:42.0309360+08:00` | `486B7C5E26B6` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `9D83AF8CF774` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `73A32D1320D6` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `FBE3ADDF8A60` | `legacy-source-only` |

### OperatorType.RoiTransform / ROI位姿变换
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.2` | `2026-07-14T15:07:23.3198726+08:00` | `23C60C75C7FB` | `operator-runtime-metadata-v2` |
| `1.0.2` | `2026-07-13T15:25:22.2877465+08:00` | `984EEEB1C462` | `legacy-source-only` |
| `1.0.2` | `2026-07-13T11:23:19.7870903+08:00` | `444BF4737BE1` | `legacy-source-only` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `0A555EBBBF0C` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `0A555EBBBF0C` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `0F2F95B8B0AD` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `F8DD7BB5F11C` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `0F2F95B8B0AD` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T23:33:19.3146997+08:00` | `F8DD7BB5F11C` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `259F54709B54` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `2BA2591DC83F` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T14:27:11.6128169+08:00` | `72CC34B3C57B` | `legacy-source-only` |

### OperatorType.ScriptOperator / 脚本算子
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `50289B6A4670` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `59D99403F4CC` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `59D99403F4CC` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `C94789B517DD` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `5FFF3B6EEC51` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `3BF73F11F392` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `5FFF3B6EEC51` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `3BF73F11F392` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `5FFF3B6EEC51` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `C36E4CDE7016` | `legacy-source-only` |

### OperatorType.SemanticSegmentation / 语义分割
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `55C1A04F54B1` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `EAEF2C189254` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T19:08:24.4997898+08:00` | `D5C001B2B2A9` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `E342AFC81C7E` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `E342AFC81C7E` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `3A76A8414573` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `34758F6F7D56` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `DFC799DB2FBE` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `34758F6F7D56` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `097A3205214C` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `F78A9BC39DE0` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `097A3205214C` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `F78A9BC39DE0` | `legacy-source-only` |

### OperatorType.SerialCommunication / 串口通信
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `53B241AD2E24` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-08T13:53:08.5202166+08:00` | `E8D5452533CE` | `legacy-source-only` |
| `1.0.0` | `2026-07-08T13:51:33.9970116+08:00` | `077AD8F6531C` | `legacy-source-only` |
| `1.0.0` | `2026-07-08T13:44:48.7501564+08:00` | `E8D5452533CE` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `471682B39F52` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `352948239420` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `BCA28CF81511` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `0546620C9813` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `661DA5689903` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `0546620C9813` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `661DA5689903` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `0546620C9813` | `legacy-source-only` |

### OperatorType.ShadingCorrection / 光照校正
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.2` | `2026-07-15T21:48:42.7530282+08:00` | `5011419F487D` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-15T20:30:44.7937318+08:00` | `74B8461693AE` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.1` | `2026-07-15T20:16:21.3617917+08:00` | `1946F4045201` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `BF63DFD419DF` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `F039A652D2BD` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `9006925E6778` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `9006925E6778` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `2274CD6243D8` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `1E8B4A2B5D6D` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `63D10B75B5F8` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `9BEF1CFAE717` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `63D10B75B5F8` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `D979701EF91A` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `4B654D0F93E4` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `D979701EF91A` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `4A014AD67BA3` | `legacy-source-only` |

### OperatorType.ShapeMatching / 旋转尺度模板匹配
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.2.0` | `2026-07-15T11:26:25.6098568+08:00` | `2CA30374003A` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.2.0` | `2026-07-14T15:07:23.3198726+08:00` | `56FB56C94372` | `operator-runtime-metadata-v2` |
| `1.2.0` | `2026-07-06T21:35:46.7699945+08:00` | `715F77A0E110` | `legacy-source-only` |
| `1.2.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.2.0` | `2026-06-25T11:35:53.9102407+08:00` | `CA86F7BF9F07` | `legacy-source-only` |
| `1.2.0` | `2026-05-16T11:53:47.4328965+08:00` | `FA7D4FA749F2` | `legacy-source-only` |
| `1.2.0` | `2026-04-28T19:39:42.8097784+08:00` | `E878A5D2742F` | `legacy-source-only` |
| `1.2.0` | `2026-04-28T10:51:32.3393648+08:00` | `03832208109B` | `legacy-source-only` |
| `1.2.0` | `2026-04-18T22:49:10.0250597+08:00` | `E878A5D2742F` | `legacy-source-only` |
| `1.2.0` | `2026-04-13T19:14:52.1190277+08:00` | `015FDB202B2F` | `legacy-source-only` |
| `1.1.2` | `2026-04-12T20:43:23.0238145+08:00` | `8D0AEBDCF2AB` | `legacy-source-only` |
| `1.1.2` | `2026-04-12T12:53:52.9929473+08:00` | `51059543B1E4` | `legacy-source-only` |
| `1.1.0` | `2026-03-21T01:38:49.8374844+08:00` | `1A5E244A6349` | `legacy-source-only` |
| `1.1.0` | `2026-03-17T14:30:51.0566057+08:00` | `141086A053D4` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T14:27:11.6128169+08:00` | `D405977E4DDC` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T19:59:19.7031372+08:00` | `35D41D58EBB1` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `ADE360463FD2` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `C20A8A850891` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `16DA32089F01` | `legacy-source-only` |

### OperatorType.SharpnessEvaluation / 清晰度评估
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.0` | `2026-07-15T17:25:56.0119276+08:00` | `0FE236CADF8A` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-15T11:26:25.6098568+08:00` | `94B51F326C98` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `335D1F6100C9` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `08B6A34318F2` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `08B6A34318F2` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `10A91490017F` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `B9B31746D5BA` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `4282F1D88FA8` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T13:57:48.4747228+08:00` | `B9B31746D5BA` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `3E591B8FF848` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `52D038E87604` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `85F599843C91` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `88E5BE97F908` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `1FF3E760D01C` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `3362EBCD01BB` | `legacy-source-only` |

### OperatorType.SiemensS7Communication / 西门子S7通信
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `CB6E01785B91` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `59928B2C1308` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `03FC76A0F2BC` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `01ED892E7F24` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `9CF5D5801305` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `80B4E9F9D31B` | `legacy-source-only` |
| `1.0.0` | `2026-04-13T23:21:39.1176099+08:00` | `9CF5D5801305` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `42E5C6F8C21C` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `B6237819F8BE` | `legacy-source-only` |
| `1.0.0` | `2026-02-27T21:39:45.2435118+08:00` | `42E5C6F8C21C` | `legacy-source-only` |
| `1.0.0` | `2026-02-27T21:13:09.9008744+08:00` | `DD54339521A8` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `8AB237E58691` | `legacy-source-only` |

### OperatorType.StatisticalOutlierRemoval / 点云统计离群点去除（SOR）
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `B24ACC6EB806` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-13T15:25:22.2877465+08:00` | `7D6C226F3FD8` | `legacy-source-only` |
| `1.0.1` | `2026-07-13T11:23:19.7870903+08:00` | `07BA1933A21C` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `2CF51132C127` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `2CF51132C127` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `FEE9BBE53DF5` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `6824980A49D6` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T15:25:05.6682201+08:00` | `39F96B06A2E6` | `legacy-source-only` |

### OperatorType.Statistics / 统计分析
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `D003B6EEB0D0` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `C12A2259E2DA` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `FFFB5889E735` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `05FC9DC25A81` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `0878AB3E2290` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `05FC9DC25A81` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `4E2FDAE0D791` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `4C4452402E9A` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `4E2FDAE0D791` | `legacy-source-only` |
| `1.0.0` | `2026-02-27T09:08:37.3873065+08:00` | `284782E31077` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `332ECE5D2E91` | `legacy-source-only` |

### OperatorType.StereoCalibration / 双目标定
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `4B95BFC3A92A` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `9D92ECE568BD` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `7BEE758548D9` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `7BEE758548D9` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `8390DF6DF511` | `legacy-source-only` |
| `1.0.0` | `2026-05-01T14:53:33.8287356+08:00` | `ADDD71247CD6` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `FA10AB8F2AE8` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `5B8B6CD60E7B` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `FA10AB8F2AE8` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `08EAD5F43029` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `810DBE56FF6D` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `8CC920E1EE3D` | `legacy-source-only` |
| `1.0.0` | `2026-03-18T19:00:25.2910689+08:00` | `EB7C28467E72` | `legacy-source-only` |

### OperatorType.StringFormat / 字符串格式化
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `149B3B790FF6` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `DE032F7E5B60` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `DE032F7E5B60` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `63B5875F3F31` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `2A4462CE9D5A` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `4CCDDE88F48F` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `2A4462CE9D5A` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `4CCDDE88F48F` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `2A4462CE9D5A` | `legacy-source-only` |

### OperatorType.SubpixelEdgeDetection / 亚像素边缘
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T17:25:56.0119276+08:00` | `D5995266624D` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `C6115E1C6466` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `23070B2BE296` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `5398A2F71BE3` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `5398A2F71BE3` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `FC973B2494F2` | `legacy-source-only` |
| `1.0.0` | `2026-04-29T00:59:13.5713917+08:00` | `4C91DDE48B33` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `9D7273B5AC72` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `91A5C0FF9D80` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `9D7273B5AC72` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `F1D2A18E813C` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `652B8AC1E38A` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `482F884A9448` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `3AB12925E1E0` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `A3FB6B396DF1` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `8A3328983212` | `legacy-source-only` |

### OperatorType.SurfaceDefectDetection / 表面缺陷检测
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `2.0.1` | `2026-07-15T17:25:56.0119276+08:00` | `35AA01E44E55` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `2.0.1` | `2026-07-15T11:26:25.6098568+08:00` | `D1DE15020EF3` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `2.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `9719B597A678` | `operator-runtime-metadata-v2` |
| `2.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `B956D3DC061A` | `legacy-source-only` |
| `2.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `2.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `B956D3DC061A` | `legacy-source-only` |
| `2.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `65FB9BC5ED39` | `legacy-source-only` |
| `2.0.0` | `2026-05-01T14:53:33.8287356+08:00` | `5F0B2565B877` | `legacy-source-only` |
| `2.0.0` | `2026-05-01T10:19:56.3600494+08:00` | `26D38931774A` | `legacy-source-only` |
| `2.0.0` | `2026-04-29T13:56:16.3361485+08:00` | `AE415A10FF18` | `legacy-source-only` |
| `2.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `CF515A77005D` | `legacy-source-only` |
| `2.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `857488425EF3` | `legacy-source-only` |
| `2.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `CF515A77005D` | `legacy-source-only` |
| `2.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `478933331F47` | `legacy-source-only` |
| `2.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `3A7A4EC68BD2` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `90D11B72CA9C` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `77EFD328EF95` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `02BD406438C7` | `legacy-source-only` |

### OperatorType.TcpCommunication / TCP通信
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `EE7310E9731A` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `BA8EE56A23C6` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T11:23:19.7870903+08:00` | `934E173F1B10` | `legacy-source-only` |
| `1.0.0` | `2026-07-09T09:44:47.2036663+08:00` | `6D650742ACDE` | `legacy-source-only` |
| `1.0.0` | `2026-07-07T09:12:23.0895237+08:00` | `A2FF608F15DC` | `legacy-source-only` |
| `1.0.0` | `2026-07-07T08:45:40.9653060+08:00` | `C37DCAAE4C1B` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `EB6CC0380C15` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `69AE35B8F99B` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `4A0AE7EB1287` | `legacy-source-only` |
| `1.0.0` | `2026-04-19T23:09:00.7096859+08:00` | `4CF06F947271` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `7137E5C4EFEB` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `D0B361FA486B` | `legacy-source-only` |

### OperatorType.TemplateMatching / 模板匹配
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.2.0` | `2026-07-15T11:26:25.6098568+08:00` | `0D64DE486999` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.2.0` | `2026-07-14T15:07:23.3198726+08:00` | `0FB6EFF97C28` | `operator-runtime-metadata-v2` |
| `1.2.0` | `2026-07-06T21:35:46.7699945+08:00` | `04ADD85AE874` | `legacy-source-only` |
| `1.2.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.2.0` | `2026-06-25T11:35:53.9102407+08:00` | `04ADD85AE874` | `legacy-source-only` |
| `1.2.0` | `2026-05-16T11:53:47.4328965+08:00` | `EB155BFFC610` | `legacy-source-only` |
| `1.2.0` | `2026-05-01T10:19:56.3600494+08:00` | `0E5951B8A391` | `legacy-source-only` |
| `1.2.0` | `2026-04-28T19:39:42.8097784+08:00` | `2C50074D0CF2` | `legacy-source-only` |
| `1.2.0` | `2026-04-28T10:51:32.3393648+08:00` | `9D2F4C22D52D` | `legacy-source-only` |
| `1.2.0` | `2026-04-18T22:49:10.0250597+08:00` | `2C50074D0CF2` | `legacy-source-only` |
| `1.2.0` | `2026-04-13T19:14:52.1190277+08:00` | `EF42E7B65449` | `legacy-source-only` |
| `1.1.1` | `2026-04-12T20:43:23.0238145+08:00` | `B5B076B4DFAF` | `legacy-source-only` |
| `1.1.1` | `2026-04-12T12:53:52.9929473+08:00` | `A856B21665F3` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `2FD9DB94E474` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `30BE4FBE1B26` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T19:59:19.7031372+08:00` | `2FD9DB94E474` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `30BE4FBE1B26` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T10:35:29.6469155+08:00` | `9D6ABB27BF04` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `19F60BB66DE8` | `legacy-source-only` |

### OperatorType.TextSave / 文本保存
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `BE9E2F907C85` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `332CDDEE12A5` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T11:23:19.7870903+08:00` | `48C35074C26B` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `3A971AE2DA21` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `7EB2639C1BF3` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `D27F4CA5947C` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `BD1CB4079308` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `D27F4CA5947C` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `BD1CB4079308` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `D27F4CA5947C` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `BD1CB4079308` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `23E51E9AEF58` | `legacy-source-only` |

### OperatorType.Thresholding / 全局阈值处理
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.0` | `2026-07-15T17:25:56.0119276+08:00` | `467D29990384` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-15T11:26:25.6098568+08:00` | `E0109AFA718C` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `104596319967` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T19:08:24.4997898+08:00` | `5CB05C0001C9` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `CC76C90CCB8B` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `EE4C8A0CBB5E` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `EE4C8A0CBB5E` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `20281AD75880` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `6EF90355BA3A` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `20281AD75880` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `6EF90355BA3A` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `F59A1E561D20` | `legacy-source-only` |

### OperatorType.TimerStatistics / 计时统计
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `133D64456744` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `64ADBCC4B42A` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `64ADBCC4B42A` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `040C798232A8` | `legacy-source-only` |
| `1.0.1` | `2026-04-29T00:59:13.5713917+08:00` | `B380C1A0AEB1` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `1E4C3D63C2AD` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `66C49F4AB9C0` | `legacy-source-only` |
| `1.0.1` | `2026-04-26T23:05:23.4040741+08:00` | `1E4C3D63C2AD` | `legacy-source-only` |
| `1.0.0` | `2026-04-26T23:04:22.3546450+08:00` | `C606DD7708E3` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `E7DB3FB15227` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `5AE68CD325A0` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `A2DAA30341B0` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `5AE68CD325A0` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `606B1D386D2B` | `legacy-source-only` |

### OperatorType.TranslationRotationCalibration / 平移旋转标定
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.1.1` | `2026-07-15T21:48:42.7530282+08:00` | `8EBD8DB80A8D` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.1.0` | `2026-07-15T20:16:21.3617917+08:00` | `95E997FA412B` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `E854175C98D2` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `86F4BE760343` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `F6672401E42E` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `F6672401E42E` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `A88694A2640A` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `6C5C3A22E74C` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `A88694A2640A` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `669B881200BD` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `288090A14B98` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `108D0A0FCC40` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `994AF95A3442` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `62D1609B1CCB` | `legacy-source-only` |

### OperatorType.TriggerModule / 触发模块
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `D13BAD545E69` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `C03B71152B59` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `C03B71152B59` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `CC21B806EF2D` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `1641FC436031` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `3977E8D475C9` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `9B64858FDC2A` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `3977E8D475C9` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `757078C7423F` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `A2BBEE56F7C4` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `757078C7423F` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T19:59:19.7031372+08:00` | `A2BBEE56F7C4` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `757078C7423F` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `4325D6D6D8A0` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `8EBA52E49F78` | `legacy-source-only` |

### OperatorType.TryCatch / Try分支透传
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `DFF8F58801CC` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T19:08:24.4997898+08:00` | `3004B982B8F0` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T18:54:34.0417583+08:00` | `7D94B210096E` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T15:30:35.6120213+08:00` | `000C173440FA` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `4398A9A300BD` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `4398A9A300BD` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `1C3ACCA39267` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `4063A03C1DD0` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `1C3ACCA39267` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `4063A03C1DD0` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `1C3ACCA39267` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `4063A03C1DD0` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T19:59:19.7031372+08:00` | `1C3ACCA39267` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `4063A03C1DD0` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `92086F4126B2` | `legacy-source-only` |

### OperatorType.TypeConvert / 类型转换
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `DD7EB17C6F31` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `CC710D7123EC` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `CC710D7123EC` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `102ADEA5B2B2` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `7B2399167D77` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `102ADEA5B2B2` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `7B2399167D77` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `102ADEA5B2B2` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `7B2399167D77` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T19:59:19.7031372+08:00` | `102ADEA5B2B2` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `7B2399167D77` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `8BCC83E53180` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `A92AC259F970` | `legacy-source-only` |

### OperatorType.Undistort / 畸变校正
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `ACE830793818` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `D439F93A5C8A` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `AB3EA1234883` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `04D7BDDABF8E` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `CAB00FCCDB56` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `06710FBB3038` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `CAB00FCCDB56` | `legacy-source-only` |
| `1.0.0` | `2026-04-22T00:42:55.8987044+08:00` | `06710FBB3038` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `51F8061FFF76` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `DC5C039E75FD` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `AA6855AFF929` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `B1CB22CD65A0` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `397F69EB0E6E` | `legacy-source-only` |

### OperatorType.UnitConvert / 单位换算
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `DEAF0E82CACF` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `8C3C63F1521B` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `8C3C63F1521B` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `87BA1B588E97` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `69C7B040046B` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `F1B3A7EC80A7` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `69C7B040046B` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `0283FE80FDDC` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `F2D5A61A11C9` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `FE7B780A4358` | `legacy-source-only` |

### OperatorType.VariableIncrement / 变量递增
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `E926ED16A6C9` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `2BC1D0FDBC7C` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T11:23:19.7870903+08:00` | `78656F9A0D58` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `A810B34342E8` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-07-01T00:00:21.2732782+08:00` | `CAA086AF7C0E` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `884D37326973` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `BE5303E8977C` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `DF059C73C970` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `D6112BF58752` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `DF059C73C970` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `7F3E98C46BB3` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `BF2BB11DC5D1` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `7F3E98C46BB3` | `legacy-source-only` |

### OperatorType.VariableRead / 变量读取
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `32855968C1EF` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `67DF4D6231BA` | `legacy-source-only` |
| `1.0.0` | `2026-07-07T09:12:23.0895237+08:00` | `8C4CA50FD559` | `legacy-source-only` |
| `1.0.0` | `2026-07-07T08:45:40.9653060+08:00` | `DA393D9E19FC` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `64E7650DF78D` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-07-01T00:00:21.2732782+08:00` | `E44B90637ED5` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `C7176374BA8B` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `313CAB03DC58` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `BBAB0B899754` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `4FE6D6F64722` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `BBAB0B899754` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `4FE6D6F64722` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `BBAB0B899754` | `legacy-source-only` |

### OperatorType.VariableWrite / 变量写入
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `803372859ACF` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-13T15:25:22.2877465+08:00` | `43364CCFBD10` | `legacy-source-only` |
| `1.0.0` | `2026-07-13T11:23:19.7870903+08:00` | `807F2ED61F17` | `legacy-source-only` |
| `1.0.0` | `2026-07-07T08:45:40.9653060+08:00` | `D9219744E8E2` | `legacy-source-only` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `D18D176C3377` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-07-01T00:00:21.2732782+08:00` | `E736FF3FC7EA` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `0825732FA3D6` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `B05B5F4EEA95` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `34636F7341BC` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `42DA20C2270A` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `34636F7341BC` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `42DA20C2270A` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `34636F7341BC` | `legacy-source-only` |

### OperatorType.VoxelDownsample / 体素下采样
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.1` | `2026-07-14T15:07:23.3198726+08:00` | `43713255CDAB` | `operator-runtime-metadata-v2` |
| `1.0.1` | `2026-07-06T21:35:46.7699945+08:00` | `99AF3A3F4E79` | `legacy-source-only` |
| `1.0.1` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.1` | `2026-06-25T11:35:53.9102407+08:00` | `99AF3A3F4E79` | `legacy-source-only` |
| `1.0.1` | `2026-05-16T11:53:47.4328965+08:00` | `1B1926E70C4B` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T19:39:42.8097784+08:00` | `36B1AD6878B2` | `legacy-source-only` |
| `1.0.1` | `2026-04-28T10:51:32.3393648+08:00` | `1A837284A8CA` | `legacy-source-only` |
| `1.0.1` | `2026-04-24T23:25:12.6473691+08:00` | `36B1AD6878B2` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `37C219635A89` | `legacy-source-only` |
| `1.0.0` | `2026-03-17T15:02:44.2227737+08:00` | `1FDE2F2206BC` | `legacy-source-only` |

### OperatorType.WidthMeasurement / 宽度测量
| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |
|------|------|------|------|
| `1.0.0` | `2026-07-15T11:26:25.6098568+08:00` | `0A08E53EE191` | `operator-runtime-metadata-v2:image-contract-v2.1` |
| `1.0.0` | `2026-07-14T15:07:23.3198726+08:00` | `2CFAC4D8CEE8` | `operator-runtime-metadata-v2` |
| `1.0.0` | `2026-07-06T21:35:46.7699945+08:00` | `39C53C07C578` | `legacy-source-only` |
| `1.0.0` | `2026-07-05T18:24:06.3177828+08:00` | `E3B0C44298FC` | `legacy-source-only` |
| `1.0.0` | `2026-07-01T00:00:21.2732782+08:00` | `39C53C07C578` | `legacy-source-only` |
| `1.0.0` | `2026-06-25T11:35:53.9102407+08:00` | `3937774A224A` | `legacy-source-only` |
| `1.0.0` | `2026-05-16T11:53:47.4328965+08:00` | `0FCE8D581D9D` | `legacy-source-only` |
| `1.0.0` | `2026-05-10T12:48:32.0866998+08:00` | `0D27447D6EF4` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T19:39:42.8097784+08:00` | `373345EB8A15` | `legacy-source-only` |
| `1.0.0` | `2026-04-28T10:51:32.3393648+08:00` | `2DE41DE29DDB` | `legacy-source-only` |
| `1.0.0` | `2026-04-18T22:49:10.0250597+08:00` | `373345EB8A15` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T20:43:23.0238145+08:00` | `A181A21D50B9` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T18:31:04.9508036+08:00` | `1BD1D7AEA8FE` | `legacy-source-only` |
| `1.0.0` | `2026-04-12T12:53:52.9929473+08:00` | `0A455BD495CA` | `legacy-source-only` |
| `1.0.0` | `2026-03-26T18:46:50.6676488+08:00` | `7D994B459340` | `legacy-source-only` |
| `1.0.0` | `2026-03-21T01:38:49.8374844+08:00` | `175A335805B8` | `legacy-source-only` |
| `1.0.0` | `2026-03-16T19:59:19.7031372+08:00` | `7D994B459340` | `legacy-source-only` |
| `1.0.0` | `2026-03-15T14:24:43.1972535+08:00` | `175A335805B8` | `legacy-source-only` |
| `1.0.0` | `2026-03-04T19:17:03.2031512+08:00` | `BBDEE390601A` | `legacy-source-only` |
| `1.0.0` | `2026-02-26T21:18:02.8071504+08:00` | `801058F2953A` | `legacy-source-only` |
