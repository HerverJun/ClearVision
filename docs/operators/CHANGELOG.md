# 算子版本变更记录 / Operator Version Changelog

> 生成时间 / Generated At: `2026-05-09 15:22:06 +08:00`
> 算子总数 / Total Operators: **118**

## 当前版本快照 / Current Snapshot
| 枚举 (Enum) | 显示名 (DisplayName) | 分类 (Category) | 版本 (Version) |
|------|------|------|------|
| `OperatorType.DeepLearning` | 深度学习 | AI检测 | `1.0.0` |
| `OperatorType.DualModalVoting` | 双模态投票 | AI检测 | `1.0.0` |
| `OperatorType.EdgePairDefect` | 边缘对缺陷 | AI检测 | `1.0.0` |
| `OperatorType.SurfaceDefectDetection` | 表面缺陷检测 | AI检测 | `1.0.0` |
| `OperatorType.AkazeFeatureMatch` | AKAZE特征匹配 | 匹配定位 | `1.0.0` |
| `OperatorType.GradientShapeMatch` | 梯度形状匹配 | 匹配定位 | `1.0.0` |
| `OperatorType.OrbFeatureMatch` | ORB特征匹配 | 匹配定位 | `1.0.0` |
| `OperatorType.PyramidShapeMatch` | 金字塔形状匹配 | 匹配定位 | `1.0.0` |
| `OperatorType.ShapeMatching` | 旋转尺度模板匹配 | 匹配定位 | `1.0.0` |
| `OperatorType.TemplateMatching` | 模板匹配 | 匹配定位 | `1.0.0` |
| `OperatorType.CycleCounter` | 循环计数器 | 变量 | `1.0.0` |
| `OperatorType.VariableIncrement` | 变量递增 | 变量 | `1.0.0` |
| `OperatorType.VariableRead` | 变量读取 | 变量 | `1.0.0` |
| `OperatorType.VariableWrite` | 变量写入 | 变量 | `1.0.0` |
| `OperatorType.AffineTransform` | 仿射变换 | 图像处理 | `1.0.0` |
| `OperatorType.CopyMakeBorder` | 边界填充 | 图像处理 | `1.0.0` |
| `OperatorType.ImageStitching` | 图像拼接 | 图像处理 | `1.0.0` |
| `OperatorType.PolarUnwrap` | 极坐标展开 | 图像处理 | `1.0.0` |
| `OperatorType.BlobLabeling` | 连通域标注 | 定位 | `1.0.0` |
| `OperatorType.CornerDetection` | 角点检测 | 定位 | `1.0.0` |
| `OperatorType.EdgeIntersection` | 边线交点 | 定位 | `1.0.0` |
| `OperatorType.ParallelLineFind` | 平行线查找 | 定位 | `1.0.0` |
| `OperatorType.PositionCorrection` | 位置修正 | 定位 | `1.0.0` |
| `OperatorType.QuadrilateralFind` | 四边形查找 | 定位 | `1.0.0` |
| `OperatorType.RectangleDetection` | 矩形检测 | 定位 | `1.0.0` |
| `OperatorType.ImageCompose` | 图像组合 | 拆分组合 | `1.0.0` |
| `OperatorType.ImageTiling` | 图像切片 | 拆分组合 | `1.0.0` |
| `OperatorType.Aggregator` | 数据聚合 | 数据处理 | `1.0.0` |
| `OperatorType.ArrayIndexer` | 数组索引器 | 数据处理 | `1.0.0` |
| `OperatorType.BoxFilter` | 候选框过滤 (Bounding Box) | 数据处理 | `1.0.0` |
| `OperatorType.BoxNms` | 候选框抑制 | 数据处理 | `1.0.0` |
| `OperatorType.DatabaseWrite` | 数据库写入 | 数据处理 | `1.0.0` |
| `OperatorType.JsonExtractor` | JSON 提取器 | 数据处理 | `1.0.0` |
| `OperatorType.MathOperation` | 数值计算 | 数据处理 | `1.0.0` |
| `OperatorType.PointAlignment` | 点位对齐 | 数据处理 | `1.0.0` |
| `OperatorType.PointCorrection` | 点位修正 | 数据处理 | `1.0.0` |
| `OperatorType.UnitConvert` | 单位换算 | 数据处理 | `1.0.0` |
| `OperatorType.CalibrationLoader` | 标定加载 | 标定 | `1.0.0` |
| `OperatorType.CameraCalibration` | Camera Calibration | 标定 | `1.0.0` |
| `OperatorType.CoordinateTransform` | 坐标转换 | 标定 | `1.0.0` |
| `OperatorType.NPointCalibration` | N点标定 | 标定 | `1.0.0` |
| `OperatorType.TranslationRotationCalibration` | 平移旋转标定 | 标定 | `1.0.0` |
| `OperatorType.Undistort` | Undistort | 标定 | `1.0.0` |
| `OperatorType.AngleMeasurement` | 角度测量 | 检测 | `1.0.0` |
| `OperatorType.CaliperTool` | 卡尺工具 | 检测 | `1.0.0` |
| `OperatorType.CircleMeasurement` | 圆测量 | 检测 | `1.0.0` |
| `OperatorType.ContourMeasurement` | 轮廓测量 | 检测 | `1.0.0` |
| `OperatorType.GapMeasurement` | 间隙测量 | 检测 | `1.0.0` |
| `OperatorType.GeoMeasurement` | 几何测量 | 检测 | `1.0.0` |
| `OperatorType.GeometricFitting` | Geometric Fitting | 检测 | `1.0.0` |
| `OperatorType.GeometricTolerance` | 几何公差 | 检测 | `1.0.0` |
| `OperatorType.HistogramAnalysis` | 直方图分析 | 检测 | `1.0.0` |
| `OperatorType.LineLineDistance` | 线线距离 | 检测 | `1.0.0` |
| `OperatorType.LineMeasurement` | 直线测量 | 检测 | `1.0.0` |
| `OperatorType.Measurement` | 测量 | 检测 | `1.0.0` |
| `OperatorType.PixelStatistics` | 像素统计 | 检测 | `1.0.0` |
| `OperatorType.PointLineDistance` | 点线距离 | 检测 | `1.0.0` |
| `OperatorType.SharpnessEvaluation` | 清晰度评估 | 检测 | `1.0.0` |
| `OperatorType.WidthMeasurement` | 宽度测量 | 检测 | `1.0.0` |
| `OperatorType.Comparator` | 数值比较 | 流程控制 | `1.0.0` |
| `OperatorType.ConditionalBranch` | 条件分支 | 流程控制 | `1.0.0` |
| `OperatorType.Delay` | 延时 | 流程控制 | `1.0.0` |
| `OperatorType.ForEach` | ForEach 循环 | 流程控制 | `1.0.0` |
| `OperatorType.ResultJudgment` | 结果判定 | 流程控制 | `1.0.0` |
| `OperatorType.TryCatch` | 异常捕获 | 流程控制 | `1.0.0` |
| `OperatorType.BlobAnalysis` | Blob分析 | 特征提取 | `1.0.0` |
| `OperatorType.ContourDetection` | 轮廓检测 | 特征提取 | `1.0.0` |
| `OperatorType.EdgeDetection` | Edge Detection | 特征提取 | `1.0.0` |
| `OperatorType.SubpixelEdgeDetection` | Subpixel Edge Detection | 特征提取 | `1.0.0` |
| `OperatorType.CodeRecognition` | 条码识别 | 识别 | `1.0.0` |
| `OperatorType.OcrRecognition` | OCR 识别 | 识别 | `1.0.0` |
| `OperatorType.Comment` | 注释 | 辅助 | `1.0.0` |
| `OperatorType.RoiManager` | ROI管理器 | 辅助 | `1.0.0` |
| `OperatorType.ImageSave` | 图像保存 | 输出 | `1.0.0` |
| `OperatorType.ResultOutput` | 结果输出 | 输出 | `1.0.0` |
| `OperatorType.HttpRequest` | HTTP 请求 | 通信 | `1.0.0` |
| `OperatorType.MitsubishiMcCommunication` | 三菱MC通信 | 通信 | `1.0.0` |
| `OperatorType.ModbusCommunication` | Modbus通信 | 通信 | `1.0.0` |
| `OperatorType.MqttPublish` | MQTT 发布 | 通信 | `1.0.0` |
| `OperatorType.OmronFinsCommunication` | 欧姆龙FINS通信 | 通信 | `1.0.0` |
| `OperatorType.SerialCommunication` | 串口通信 | 通信 | `1.0.0` |
| `OperatorType.SiemensS7Communication` | 西门子S7通信 | 通信 | `1.0.0` |
| `OperatorType.TcpCommunication` | TCP通信 | 通信 | `1.0.0` |
| `OperatorType.LogicGate` | 逻辑门 | 通用 | `1.0.0` |
| `OperatorType.Statistics` | Statistics | 通用 | `1.0.0` |
| `OperatorType.StringFormat` | 字符串格式化 | 通用 | `1.0.0` |
| `OperatorType.TypeConvert` | Type Convert | 通用 | `1.0.0` |
| `OperatorType.PointSetTool` | 点集工具 | 逻辑工具 | `1.0.0` |
| `OperatorType.ScriptOperator` | 脚本算子 | 逻辑工具 | `1.0.0` |
| `OperatorType.TextSave` | Text Save | 逻辑工具 | `1.0.0` |
| `OperatorType.TimerStatistics` | 计时统计 | 逻辑工具 | `1.0.0` |
| `OperatorType.TriggerModule` | 触发模块 | 逻辑工具 | `1.0.0` |
| `OperatorType.ImageAcquisition` | 图像采集 | 采集 | `1.0.0` |
| `OperatorType.AdaptiveThreshold` | 自适应阈值 | 预处理 | `1.0.0` |
| `OperatorType.BilateralFilter` | 双边滤波 | 预处理 | `1.0.0` |
| `OperatorType.ClaheEnhancement` | CLAHE增强 | 预处理 | `1.0.0` |
| `OperatorType.ColorConversion` | 颜色空间转换 | 预处理 | `1.0.0` |
| `OperatorType.Filtering` | Gaussian Blur | 预处理 | `1.0.0` |
| `OperatorType.FrameAveraging` | 帧平均 | 预处理 | `1.0.0` |
| `OperatorType.HistogramEqualization` | 直方图均衡化 | 预处理 | `1.0.0` |
| `OperatorType.ImageAdd` | 图像加法 | 预处理 | `1.0.0` |
| `OperatorType.ImageBlend` | 图像融合 | 预处理 | `1.0.0` |
| `OperatorType.ImageCrop` | 图像裁剪 | 预处理 | `1.0.0` |
| `OperatorType.ImageDiff` | 图像对比 | 预处理 | `1.0.0` |
| `OperatorType.ImageNormalize` | 图像归一化 | 预处理 | `1.0.0` |
| `OperatorType.ImageResize` | 图像缩放 | 预处理 | `1.0.0` |
| `OperatorType.ImageRotate` | 图像旋转 | 预处理 | `1.0.0` |
| `OperatorType.ImageSubtract` | Image Subtract | 预处理 | `1.0.0` |
| `OperatorType.LaplacianSharpen` | 拉普拉斯锐化 | 预处理 | `1.0.0` |
| `OperatorType.MeanFilter` | 均值滤波 | 预处理 | `1.0.0` |
| `OperatorType.MedianBlur` | 中值滤波 | 预处理 | `1.0.0` |
| `OperatorType.MorphologicalOperation` | Morphological Operation | 预处理 | `1.0.0` |
| `OperatorType.Morphology` | Morphology (Legacy) | 预处理 | `1.0.0` |
| `OperatorType.PerspectiveTransform` | 透视变换 | 预处理 | `1.0.0` |
| `OperatorType.ShadingCorrection` | 光照校正 | 预处理 | `1.0.0` |
| `OperatorType.Thresholding` | 二值化 | 预处理 | `1.0.0` |
| `OperatorType.ColorDetection` | 颜色检测 | 颜色处理 | `1.0.0` |
| `OperatorType.ColorMeasurement` | 颜色测量 | 颜色处理 | `1.0.0` |

## 历史变更 / Historical Changes

### OperatorType.AffineTransform / 仿射变换
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `022177F70CFA` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `9C23CBB5A4BF` |

### OperatorType.BilateralFilter / 双边滤波
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `AD856145D522` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `41C33EAE8CC8` |

### OperatorType.BlobAnalysis / Blob分析
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `5E2729EADC33` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `3625B6B90CA4` |

### OperatorType.BoxFilter / 候选框过滤 (Bounding Box)
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `16B146F1F86B` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `9686BC75BE56` |

### OperatorType.BoxNms / 候选框抑制
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `0A6A4202A567` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `40D3FE35DADA` |

### OperatorType.CaliperTool / 卡尺工具
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `FA337F7F3643` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `81CAB20D19BA` |

### OperatorType.CircleMeasurement / 圆测量
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `897AC10DDD90` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `DB361BC29EEA` |

### OperatorType.CodeRecognition / 条码识别
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `42B65E96D449` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `C308EABF11B5` |

### OperatorType.ColorConversion / 颜色空间转换
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `D7F62D1A2454` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `287716BE3467` |

### OperatorType.Comment / 注释
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `AB24C01595BF` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `0551D0079C76` |

### OperatorType.Comparator / 数值比较
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `97A7464100D9` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `20A58BFA7B00` |

### OperatorType.ConditionalBranch / 条件分支
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `0EBE96F5F22F` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `29F6BE3DEEB2` |

### OperatorType.ContourDetection / 轮廓检测
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `B72BC27F515B` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `5E019893822A` |

### OperatorType.CopyMakeBorder / 边界填充
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `7FAE5011770B` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `EA1319B3DDD0` |

### OperatorType.CornerDetection / 角点检测
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `5A37D8EC2E9C` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `8FC52A647500` |

### OperatorType.DeepLearning / 深度学习
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `389D77E24800` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `8E05D8B0E4A4` |

### OperatorType.Delay / 延时
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `A2DBCB95F690` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `90A13B625F06` |

### OperatorType.EdgePairDefect / 边缘对缺陷
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `27338ED21CCC` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `B87662A40B4C` |

### OperatorType.Filtering / Gaussian Blur
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `216466C811A1` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `10556AD6D71E` |

### OperatorType.ForEach / ForEach 循环
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `454A81DB790E` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `E199FAFD4B15` |

### OperatorType.FrameAveraging / 帧平均
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `D74D77BEC045` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `30231B664557` |

### OperatorType.GapMeasurement / 间隙测量
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `B919FA2DB07F` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `5B71FA7A78D9` |

### OperatorType.GeometricFitting / Geometric Fitting
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `CD187AD20399` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `4C97CFE688D4` |

### OperatorType.GradientShapeMatch / 梯度形状匹配
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `59FB9A89A6DC` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `ABFE4629A17F` |

### OperatorType.HttpRequest / HTTP 请求
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `054CB8A6C997` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `933DF4F86055` |

### OperatorType.ImageAcquisition / 图像采集
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `FD748A5E641F` |
| `1.0.0` | `2026-05-12T20:03:52.7193054+08:00` | `872DBEFFDC39` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `CF45EDA52ADC` |

### OperatorType.ImageAdd / 图像加法
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `C928FAED5F36` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `0CDCBE571A32` |

### OperatorType.ImageBlend / 图像融合
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `01C2BFA9B87D` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `EFB0D9B90191` |

### OperatorType.ImageCompose / 图像组合
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `A19A3F9DE35B` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `023726942CCB` |

### OperatorType.ImageCrop / 图像裁剪
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `C5A85DF6AB4B` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `A83435868402` |

### OperatorType.ImageDiff / 图像对比
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `3DB7F5D36000` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `C891A8BB5072` |

### OperatorType.ImageNormalize / 图像归一化
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `E7CD36D371B1` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `D3E68AE79672` |

### OperatorType.ImageResize / 图像缩放
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `289037968A26` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `DD2E746E9FED` |

### OperatorType.ImageRotate / 图像旋转
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `523ED6397AE2` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `477E05F0F405` |

### OperatorType.ImageSave / 图像保存
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `535EC369B3FC` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `54EC57174052` |

### OperatorType.ImageStitching / 图像拼接
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `583B6706C597` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `85AFAF0F6E54` |

### OperatorType.ImageTiling / 图像切片
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `3F3C831A03DE` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `98FA118539D1` |

### OperatorType.LaplacianSharpen / 拉普拉斯锐化
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `8BE0560EBB60` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `7F718355AFC8` |

### OperatorType.MedianBlur / 中值滤波
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `A9FDA79A012E` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `2809B653B975` |

### OperatorType.ModbusCommunication / Modbus通信
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `D0DE04C84A90` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `8D788B97BF6D` |

### OperatorType.Morphology / Morphology (Legacy)
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `32A98594911C` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `D50D116508C7` |

### OperatorType.OcrRecognition / OCR 识别
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `C7CBB4487B32` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `7FC79F721D84` |

### OperatorType.OmronFinsCommunication / 欧姆龙FINS通信
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `A7E735CDBE98` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `6B35BA2B0309` |

### OperatorType.PixelStatistics / 像素统计
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `9AEF516248F8` |
| `1.0.0` | `2026-05-12T20:03:52.7193054+08:00` | `60AFE6AC3B29` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `46BEAB9EF27C` |

### OperatorType.PointAlignment / 点位对齐
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `38433D21EAD8` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `5769ADE0033C` |

### OperatorType.PointCorrection / 点位修正
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `AD716CFD767C` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `0AAD72DEA70B` |

### OperatorType.PointSetTool / 点集工具
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `DBAD0C90624F` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `C86961A6A036` |

### OperatorType.RoiManager / ROI管理器
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `73A32D1320D6` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `FBE3ADDF8A60` |

### OperatorType.ScriptOperator / 脚本算子
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `C94789B517DD` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `5FFF3B6EEC51` |

### OperatorType.SerialCommunication / 串口通信
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `BCA28CF81511` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `0546620C9813` |

### OperatorType.ShadingCorrection / 光照校正
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `2274CD6243D8` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `5BB5CD7A8327` |

### OperatorType.ShapeMatching / 旋转尺度模板匹配
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `FA7D4FA749F2` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `E878A5D2742F` |

### OperatorType.SiemensS7Communication / 西门子S7通信
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `01ED892E7F24` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `80B4E9F9D31B` |

### OperatorType.StringFormat / 字符串格式化
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `63B5875F3F31` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `2A4462CE9D5A` |

### OperatorType.TcpCommunication / TCP通信
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `4A0AE7EB1287` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `4CF06F947271` |

### OperatorType.TimerStatistics / 计时统计
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `040C798232A8` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `1F126804C239` |

### OperatorType.TriggerModule / 触发模块
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `CC21B806EF2D` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `25F3C51EF33A` |

### OperatorType.UnitConvert / 单位换算
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `87BA1B588E97` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `F1B3A7EC80A7` |

### OperatorType.VariableIncrement / 变量递增
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `BE5303E8977C` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `DF059C73C970` |

### OperatorType.VariableRead / 变量读取
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `313CAB03DC58` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `BBAB0B899754` |

### OperatorType.VariableWrite / 变量写入
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `B05B5F4EEA95` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `34636F7341BC` |

### OperatorType.WidthMeasurement / 宽度测量
| 版本 (Version) | 记录时间 (Recorded At) | 源码摘要 (Source Hash) |
|------|------|------|
| `1.0.0` | `2026-05-28T18:39:04.9379600+08:00` | `0FCE8D581D9D` |
| `1.0.0` | `2026-05-09T15:22:06.0243662+08:00` | `BC8DFD93662D` |
