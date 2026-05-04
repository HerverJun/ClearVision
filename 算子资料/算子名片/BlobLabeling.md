# 连通域标注 / BlobLabeling

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `BlobLabelingOperator` |
| 枚举值 (Enum) | `OperatorType.BlobLabeling` |
| 分类 (Category) | 定位 |
| IconName | `blob-label` |
| Keywords | `blob`, `label`, `classify connected component` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
**中文：**
该算子对二值图像中的连通域进行几何特征测量，然后根据选定特征（面积、圆度、宽高比或位置）对每个 Blob 进行分类标注。核心流程为：Otsu 自动阈值二值化 -> `FindContours` 提取外层轮廓 -> 对每个轮廓计算面积 `ContourArea`、周长 `ArcLength`、圆度 `4*pi*area/perimeter^2`、外接矩形 `BoundingRect` 及宽高比。若用户提供了自定义阈值区间 `Thresholds`（JSON 数组），则按区间匹配标签名；否则按内置规则自动分类（如面积 <200 为 Small，圆度 >0.8 为 Round 等）。最终在结果图上绘制轮廓和标签文本。

**English:**
This operator measures geometric features of connected components in a binary image, then classifies each blob by a selected feature (area, circularity, aspect ratio, or position). Core pipeline: Otsu auto-threshold binarization -> `FindContours` for external contours -> compute `ContourArea`, `ArcLength`, circularity `4*pi*area/perimeter^2`, `BoundingRect`, and aspect ratio per contour. If user-supplied `Thresholds` (JSON array) are provided, label names are matched by interval; otherwise built-in rules classify automatically (e.g., area <200 = Small, circularity >0.8 = Round). The result image renders contours and label text.

## 实现策略 / Implementation Strategy
- **双路 Blob 输入：** 支持外部轮廓输入（`Blobs` 端口，`PortDataType.Contour`）和自动检测两条路径。当 `Blobs` 输入未连接时，算子自动从输入图像执行 Otsu 二值化 + `FindContours` 检测连通域；当 `Blobs` 已连接时，直接使用外部轮廓数据，避免重复检测。
- **灵活的 Blob 解析：** `TryParseBlobInput` 支持 `Point[][]`、`IEnumerable<Point[]>`、`IEnumerable<object>`（字典格式含 X/Y/Width/Height/Area/Circularity/AspectRatio 字段）以及单个 Blob 对象，兼容多种上游算子输出格式。
- **阈值 JSON 解析容错：** `Thresholds` 参数接受 JSON 数组格式 `[{"Name":"xxx","Min":0,"Max":100}]`，空字符串或空白视为无自定义阈值，解析失败返回明确错误信息。
- **标签颜色确定性生成：** 基于标签名哈希值生成 BGR 颜色，保证同一标签名始终使用相同颜色。
- 与 Halcon 的 `connection` + `select_shape` 组合类似，但本算子将检测与分类合并为单步操作。

## 核心 API 调用链 / Core API Call Chain
```
1. TryGetInputImage(inputs)                     -- 获取输入图像
2. GetStringParam("LabelBy")                    -- 读取分类特征
3. GetBoolParam("DrawLabels")                   -- 读取绘制开关
4. GetStringParam("Thresholds")                 -- 读取阈值 JSON
5. TryParseThresholds(json)                     -- 解析阈值 JSON 数组
6. TryParseBlobInput(blobInput)                 -- 解析外部 Blob 输入 (若存在)
   └─ CreateBlobMeasurement(contour)            -- 单个轮廓度量
      ├─ Cv2.ContourArea(contour)
      ├─ Cv2.ArcLength(contour, closed=true)
      ├─ Cv2.BoundingRect(contour)
      └─ circularity = 4*PI*area / perimeter^2
7. [若无外部 Blobs] DetectBlobsFromImage(src)
   ├─ Cv2.CvtColor(src, gray, BGR2GRAY)
   ├─ Cv2.Threshold(gray, binary, 0, 255, Binary|Otsu)
   ├─ Cv2.FindContours(binary, External, ApproxSimple)
   └─ contours.Select(CreateBlobMeasurement)
8. ResolveLabel(labelBy, feature, center, imageSize, thresholds)
   └─ 按 thresholds 区间匹配 / 内置规则分类
9. [若 drawLabels] Cv2.DrawContours / Cv2.PutText
10. CreateImageOutput(result, {Labels, Count})
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `LabelBy` | `enum` | `"Area"` | `Area` / `Circularity` / `AspectRatio` / `Position` | 分类依据特征。Area 按面积分 Small/Medium/Large；Circularity 按圆度分 Round/Irregular；AspectRatio 按宽高比分 Wide/Tall/SquareLike；Position 按 Y 坐标分 Top/Middle/Bottom。 |
| `Thresholds` | `string` | `"[]"` | JSON 数组或空字符串 | 自定义阈值区间，格式 `[{"Name":"标签名","Min":0,"Max":100}]`。非空时覆盖内置分类规则；Min/Max 须为有限数且 Min <= Max。 |
| `DrawLabels` | `bool` | `true` | - | 是否在结果图上绘制轮廓和标签文本。关闭后仅输出数值结果，不生成可视化图层。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 输入待处理图像，支持灰度和彩色。 |
| `Blobs` | Blobs | `Contour` | No | 外部轮廓数据。未提供时算子自动从图像检测连通域；提供时跳过检测直接使用。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 可视化结果图，包含轮廓绘制和标签文本（受 DrawLabels 控制）。 |
| `Labels` | Labels | `Any` | 标注结果列表，每项含 Index、Label、Area、Circularity、AspectRatio、CenterX、CenterY。 |
| `Count` | Count | `Integer` | 检测到的有效 Blob 数量。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) 轮廓检测 + O(M) 特征计算与标注，N 为像素数，M 为轮廓数。外部 Blobs 输入时跳过检测阶段。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像约 15-40ms（含自动检测）；外部 Blobs 输入时约 2-8ms。 |
| 内存特征 (Memory Profile) | 需分配灰度图、二值图、结果图各一份 Mat；轮廓数据量随 Blob 数量线性增长。 |

## 适用场景 / Use Cases
- **适合 (Suitable)：** 工件计数与分类（如按面积区分大小零件）；圆度检测筛选异形件；基于位置的区域分区标注；与上游轮廓检测算子串联使用。
- **不适合 (Not Suitable)：** 精确的像素级分割（本算子基于阈值二值化，非语义分割）；高密度粘连 Blob 场景（需先做分水岭等分离处理）；实时性要求极高的场景（自动检测路径涉及全图扫描）。

## 已知限制 / Known Limitations
1. 自动检测路径使用 Otsu 全局阈值，对光照不均匀图像可能导致欠分割或过分割。
2. `Thresholds` JSON 解析失败时整个算子返回 Failure，不会静默回退到内置规则。
3. 圆度计算基于 `4*pi*area/perimeter^2`，对非常不规则的轮廓，周长噪声会导致圆度值不稳定。
4. 外部 Blobs 输入为字典格式时，X/Y/Width/Height 为必填字段，缺少任一则解析失败。
5. 当 `LabelBy=Position` 时，分类基于绝对 Y 坐标（图像高度三等分），不支持自定义分区比例。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充双路 Blob 输入策略、Thresholds JSON 解析容错、标签颜色确定性生成、完整 API 调用链、精确性能分析；修正算法原理描述（Otsu + FindContours + 几何特征分类）；增加适用场景与已知限制的源码级说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
