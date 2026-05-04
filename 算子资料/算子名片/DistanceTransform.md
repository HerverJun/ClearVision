# 距离变换 / DistanceTransform

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `DistanceTransformOperator` |
| 枚举值 (Enum) | `OperatorType.DistanceTransform` |
| 分类 (Category) | Analysis |
| 版本 (Version) | `2.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 图标 (Icon) | `distance-transform` |
| 关键词 (Keywords) | `Distance`, `Transform`, `EDT`, `Chamfer`, `Signed`, `Euclidean` |
| 对标 | Halcon `distance_transform` |

## 算法原理 / Algorithm Principle
**中文：**
该算子计算二值图像中每个前景像素到最近零像素（背景）的距离，对标 Halcon `distance_transform`，核心流程如下：

1. **预处理**：将输入图像经 `OperatorImageDepthHelper.EnsureSingleChannelGray` 转为单通道灰度图，再通过 `OperatorImageDepthHelper.ResolveThresholdToNativeRange` 将用户阈值映射到图像原生位深范围，使用 `Cv2.Threshold` 做二值化（`ThresholdTypes.Binary`）。若 `Invert=true` 则对二值图取反。
2. **距离变换计算**：
   - **非有符号模式**：直接调用 `Cv2.DistanceTransform(binary, result, distType, distMask)` 计算距离图。
   - **有符号模式**（`Signed=true`）：分别计算前景到背景的距离（正距离）和背景到前景的距离（取反后计算），然后逐像素合并：前景区域取正值，背景区域取负值，生成有符号距离场（Signed Distance Field, SDF）。
3. **距离度量**：支持 5 种 OpenCV 距离类型映射：
   - Euclidean -> `DistanceTypes.L2`（欧氏距离）
   - Manhattan/L1 -> `DistanceTypes.L1`（曼哈顿距离）
   - Chessboard/C -> `DistanceTypes.C`（棋盘距离）
   - L12 -> `DistanceTypes.L12`
4. **掩码大小**：`MaskSize` 控制距离变换的掩码精度，3 和 5 对应标准掩码（`Mask3`/`Mask5`），7 映射到 `Precise` 精确模式。
5. **最大距离限制**：`MaxDistanceLimit > 0` 时，使用 `Cv2.Threshold(Trunc)` 将距离值截断到上限。
6. **可视化**：通过 `Cv2.MinMaxLoc` 找到最大距离位置，对距离图做归一化或缩放到 0-255，再应用 `Cv2.ApplyColorMap(Jet)` 生成伪彩色热力图，并在最大距离点绘制白色圆圈和距离标注。
7. **精度验证**：对连通区域进行形状分析，通过 `Cv2.ConnectedComponentsWithStats` 提取每个区域的边界框和面积，估算期望最大距离并与实际最大距离比较，计算误差比（容差 1%）。

**English:**
This operator computes the distance from each foreground pixel to the nearest zero pixel in a binary image, benchmarked against Halcon `distance_transform`. The pipeline binarizes the input via thresholding, computes OpenCV distance maps with the requested metric (Euclidean/Manhattan/Chessboard/L12), optionally builds a signed distance field (foreground positive, background negative), truncates to a max distance limit, and produces a Jet-colored heatmap visualization with the maximum distance point annotated. A connected-component accuracy validation compares actual vs. expected maximum distances.

## 实现策略 / Implementation Strategy
- **中文：** 算子遵循统一算子框架。预处理链为灰度转换 -> 阈值二值化 -> 可选取反，确保输入为干净的二值掩码。距离变换使用 OpenCV 原生 `Cv2.DistanceTransform`（非有符号模式）或自研逐像素合并逻辑（有符号模式）。有符号模式通过两次 `DistanceTransform` 调用（前景和背景各一次）加一次逐像素循环实现，性能开销约为非有符号模式的 3 倍。可视化链为归一化/缩放 -> Jet 色彩映射 -> 最大点标注。精度验证通过连通区域分析提供形状级别的距离准确性报告。结果通过 `CreateImageOutput` 封装，同时输出伪彩色结果图和原始浮点距离图。
- **English:** The operator follows the standard framework with a preprocessing chain of grayscale conversion, thresholding, and optional inversion. Non-signed mode uses native `Cv2.DistanceTransform`; signed mode performs two distance transforms plus a pixel-wise merge loop (approximately 3x cost). Visualization applies normalization, Jet colormap, and max-point annotation. Accuracy validation uses connected-component analysis. Results are packaged via `CreateImageOutput` with both the colored visualization and the raw float distance map.

## 核心 API 调用链 / Core API Call Chain
```
TryGetInputImage(inputs, "Image", out imageWrapper)
  -> imageWrapper.GetMat()
  -> OperatorImageDepthHelper.EnsureSingleChannelGray(src)    // 灰度转换
  -> OperatorImageDepthHelper.ResolveThresholdToNativeRange(gray, threshold)
  -> Cv2.Threshold(gray, binary, nativeThreshold, 255, Binary)  // 二值化
  -> [可选] Cv2.BitwiseNot(binary, binary)                     // Invert
  -> [dispatch by Signed]:
     Signed=false:
       ComputeDistanceTransform(binary, distanceType, maskSize)
         -> DistanceTypes 映射 (L2/L1/C/L12)
         -> DistanceTransformMasks 映射 (Mask3/Mask5/Precise)
         -> Cv2.DistanceTransform(binary, result, distType, distMask)
     Signed=true:
       ComputeSignedDistanceTransform(binary, distanceType, maskSize)
         -> ComputeDistanceTransform(binary, ...)              // 前景距离（正）
         -> Cv2.BitwiseNot(binary, inverted)                   // 反转
         -> ComputeDistanceTransform(inverted, ...)             // 背景距离（负）
         -> 逐像素合并：前景取正值，背景取负值
  -> [可选] Cv2.Threshold(distanceMap, mask, limit, limit, Trunc)  // MaxDistanceLimit
  -> Cv2.MinMaxLoc(distanceMap, minVal, maxVal, minLoc, maxLoc)
  -> [dispatch by Normalize]:
     Normalize=true:  Cv2.Normalize(distanceMap, display, 0, 255, MinMax, CV_8UC1)
     Normalize=false: distanceMap.ConvertTo(display, CV_8UC1, 255/maxVal)
  -> Cv2.ApplyColorMap(display, colorResult, Jet)              // 伪彩色
  -> Cv2.Circle(colorResult, maxLoc, 5, white, -1)             // 最大点标注
  -> Cv2.PutText(colorResult, "Max: xxx", ...)
  -> ValidateDistanceAccuracy(distanceMap, binary, distanceType)  // 精度验证
       -> Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids)
       -> 遍历连通区域，估算期望最大距离，计算误差比
  -> CreateImageOutput(colorResult, resultData)
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `DistanceType` | `enum` | `Euclidean` | Euclidean, Manhattan, Chessboard, C, L12 | 距离度量类型。Euclidean=L2 欧氏距离；Manhattan=L1 曼哈顿距离；Chessboard/C=棋盘距离；L12=L12 混合距离 |
| `MaskSize` | `int` | `5` | [3, 7] | 距离变换掩码大小。3=Mask3（快速近似）；5=Mask5（标准精度）；7=Precise（精确模式）。验证仅接受 3 和 5 |
| `Signed` | `bool` | `false` | - | 是否计算有符号距离。true 时前景为正、背景为负，生成 SDF（Signed Distance Field） |
| `Threshold` | `double` | `127.0` | [0.0, 255.0] | 二值化阈值。内部通过 `ResolveThresholdToNativeRange` 映射到图像原生位深范围 |
| `Invert` | `bool` | `false` | - | 是否反转输入。true 时对二值图取反，交换前景和背景 |
| `Normalize` | `bool` | `false` | - | 是否归一化输出。true 时使用 MinMax 归一化到 0-255；false 时按最大距离缩放 |
| `MaxDistanceLimit` | `double` | `0.0` | [0.0, 10000.0] | 最大距离限制。0 表示不限制；>0 时截断距离值到此上限 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Input Image (Binary or Grayscale) | `Image` | Yes | 输入二值或灰度图像，内部自动转灰度并二值化 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Distance Transform Result | `Image` | Jet 伪彩色距离热力图，标注最大距离点 |
| `DistanceMap` | Distance Map (Float) | `Any` | 原始浮点距离图（CV_32FC1），可用于下游精确计算 |
| `MaxDistance` | Maximum Distance | `Float` | 距离图中的最大距离值 |
| `MaxLocation` | Maximum Distance Location | `Point` | 最大距离点的坐标 {X, Y} |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `MinDistance` | `Double` | 距离图中的最小距离值 |
| `DistanceType` | `String` | 实际使用的距离度量类型 |
| `IsSigned` | `Boolean` | 是否为有符号距离 |
| `ProcessingTimeMs` | `Long` | 处理耗时（毫秒） |
| `ImageWidth` | `Integer` | 输入图像宽度 |
| `ImageHeight` | `Integer` | 输入图像高度 |
| `MeanDistance` | `Double` | 距离图均值 |
| `AccuracyReport` | `Dictionary` | 精度验证报告，含连通区域分析、期望距离、误差比等 |
| `ThresholdUsed` | `Double` | 实际使用的二值化阈值（映射到原生位深后） |
| `InputBitDepth` | `String` | 输入图像的位深（如 Byte/UInt16） |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H)；有符号模式约 3 倍于非有符号模式（两次距离变换 + 逐像素合并） |
| 典型耗时 (Typical Latency) | 无专用 golden benchmark；1080p 非有符号模式通常 <20ms；有符号模式约 50-60ms |
| 内存特征 (Memory Profile) | 距离图为 CV_32FC1（4 bytes/pixel），有符号模式额外需要反转图和两个中间距离图；峰值约 5-6 倍输入图像大小（含可视化副本） |

## 适用场景 / Use Cases
- **适合 (Suitable)：**
  - 二值掩码分析：寻找最大内切圆半径、中心候选点或距离图可视化
  - 稳定阈值分割后的前景/背景有符号距离测量（SDF）
  - 形态学分析中的骨架提取辅助（距离图峰值对应中轴线）
  - 缺陷检测中需要量化缺陷区域到边缘的距离
  - 对标 Halcon `distance_transform` 的迁移场景
- **不适合 (Not Suitable)：**
  - 灰度距离分析（必须先二值化，结果质量取决于阈值参数）
  - 高吞吐量有符号距离计算（额外的前景/背景变换和逐像素循环显著增加延迟）
  - 需要亚像素级距离精度的场景（距离变换基于像素网格）

## 已知限制 / Known Limitations
1. 输入在距离计算前会被二值化，结果质量高度依赖 `Threshold` 和 `Invert` 参数的选择。
2. `ValidateParameters` 仅接受标准掩码大小 3 和 5；源码中 `MaskSize=7` 映射到 `Precise` 模式但验证会拒绝，存在声明与验证的不一致。
3. 有符号模式的逐像素循环（`binary.At<byte>` / `distForeground.At<float>`）使用了 OpenCvSharp 的索引器访问，对大图像性能较差，可通过 `GetGenericIndexer` 或 `Marshal.Copy` 优化。
4. 精度验证中的期望最大距离估算（`EstimateExpectedMaxDistance`）使用简化的几何近似（矩形取 `min(w,h)/2`），对不规则形状的估算偏差较大。
5. `DistanceMap` 输出为 OpenCV `Mat` 对象（CV_32FC1），下游算子需要正确处理浮点 Mat 类型。
6. 可视化结果图中的最大距离点标注文本（`"Max: {maxVal:F1}"`）可能超出图像边界，未做边界保护。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写算子名片：基于源码提取完整算法流程（二值化 -> 距离变换 -> 有符号 SDF -> 可视化 -> 精度验证）、7 个参数的详细说明、4 个输出端口 + 10 个运行时附加输出的语义、5 种距离度量与 Halcon 对标说明 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
