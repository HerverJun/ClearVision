# 平行线查找 / ParallelLineFind

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ParallelLineFindOperator` |
| 枚举值 (Enum) | `OperatorType.ParallelLineFind` |
| 分类 (Category) | 定位 |
| IconName | `parallel` |
| Keywords | `parallel`, `dual edge`, `rails` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
**中文：**
该算子在图像中检测最佳近平行线对。核心流程：高斯模糊 -> Canny 边缘检测 -> 形态学闭运算（3x3 矩形核）-> `HoughLinesP` 概率霍夫变换提取线段候选。对每条候选线段计算方向角度（atan2, 钳位至 [0, 180) 度）和法向偏移量（signed offset = dot(lineMidpoint, normal)）。通过预剪枝（`PruneCandidates`，保留最长 48 条，去除角度/偏移/中点冗余线段）减少 O(n^2) 配对搜索量。对所有候选对评估角度差、距离、重叠率三项指标的加权得分 `score = angleDiff*6 + |dist-preferredDist|*0.15 + (1-overlap)*30 - lengthBonus`，选择得分最低的线对作为最佳平行线。

**English:**
This operator detects the best near-parallel line pair in an image. Core pipeline: Gaussian blur -> Canny edge detection -> morphological closing (3x3 rect kernel) -> `HoughLinesP` probabilistic Hough transform for line segment candidates. Each candidate's direction angle (atan2, clamped to [0, 180)) and normal offset (signed offset = dot(lineMidpoint, normal)) are computed. Pre-pruning (`PruneCandidates`, keep top 48 by length, remove angle/offset/midpoint redundancies) reduces the O(n^2) pairing search. All candidate pairs are scored by angle difference, distance, and overlap ratio with weighting `score = angleDiff*6 + |dist-preferredDist|*0.15 + (1-overlap)*30 - lengthBonus`; the lowest-scoring pair is selected.

## 实现策略 / Implementation Strategy
- **候选预剪枝策略：** `PruneCandidates` 按线段长度降序排列，去除角度差 <= max(1, angleTolerance*0.35) 且偏移差 <= 4.0 且中点距离 <= 12.0 的冗余线段，最多保留 48 条。这将 O(n^2) 配对搜索限制在可控范围内。
- **方向规范化：** `CreateCandidate` 将线段方向向量规范化，确保 unitX >= 0（或 unitX~0 时 unitY >= 0），角度映射到 [0, 180) 度范围，使反向平行线也能正确匹配。
- **重叠率计算：** `ComputeOverlapRatio` 基于线段在方向轴上的投影区间（ProjectionMin/ProjectionMax），计算两线段重叠长度与较短线段长度的比值，阈值 0.25 过滤不重叠线对。
- **综合评分：** 评分函数优先角度一致（权重 6.0），其次距离偏好（权重 0.15，偏好中点距离），再次重叠率（权重 30.0），最后给予长线段奖励（lengthBonus = min(len1,len2)*0.05）。
- **形态学闭运算：** Canny 边缘后使用 3x3 矩形核闭运算连接断裂边缘，提高线段连续性。

## 核心 API 调用链 / Core API Call Chain
```
1. TryGetInputImage(inputs)                        -- 获取输入图像
2. GetDoubleParam("AngleTolerance", 5.0, 0, 45)   -- 读取角度容差
3. GetDoubleParam("MinLength", 40.0, 1, 100000)   -- 读取最小线段长度
4. GetDoubleParam("MinDistance", 2.0, 0, 100000)   -- 读取最小平行距离
5. GetDoubleParam("MaxDistance", 200.0, 0, 100000) -- 读取最大平行距离
6. Cv2.CvtColor(src, gray, BGR2GRAY)               -- 灰度转换 (若彩色)
7. Cv2.GaussianBlur(gray, blurred, Size(5,5), 0)   -- 高斯降噪
8. Cv2.Canny(blurred, edge, 60, 180)               -- Canny 边缘检测
9. Cv2.MorphologyEx(edge, closed, Close, 3x3)      -- 形态学闭运算
10. Cv2.HoughLinesP(closed, 1, PI/180, 80, minLength, 10) -- 概率霍夫变换
11. candidates.Select(CreateCandidate)               -- 角度/偏移/投影计算
12. PruneCandidates(candidates, angleTolerance)      -- 冗余去除 (保留 top 48)
13. O(n^2) 配对搜索 + ComputePairScore 评分
14. [若找到] Cv2.Line(resultImage, line1, green), Cv2.Line(resultImage, line2, blue)
15. CreateImageOutput(result, {Line1, Line2, Distance, Angle, PairCount})
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `AngleTolerance` | `double` | `5.0` | [0.0, 45.0] | 两线角度差容差（度）。超过此值的线对不被考虑为平行线。 |
| `MinLength` | `double` | `40.0` | [1.0, 100000.0] | 线段最小长度（像素）。短于此值的 HoughLinesP 线段被过滤。 |
| `MinDistance` | `double` | `2.0` | [0.0, 100000.0] | 平行线对之间的最小距离（像素）。 |
| `MaxDistance` | `double` | `200.0` | [0.0, 100000.0] | 平行线对之间的最大距离（像素）。评分函数偏好距离中值 (MinDistance+MaxDistance)/2。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 输入待处理图像，支持灰度和彩色。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 可视化结果图，最佳线对以绿色（Line1）和蓝色（Line2）绘制。 |
| `Line1` | Line 1 | `LineData` | 最佳平行线对中第一条线段的端点坐标。 |
| `Line2` | Line 2 | `LineData` | 最佳平行线对中第二条线段的端点坐标。 |
| `Distance` | Distance | `Float` | 两平行线的法向距离（像素）。 |
| `Angle` | Angle | `Float` | 两线的角度差（度），范围 [0, AngleTolerance]。 |
| `PairCount` | Pair Count | `Integer` | 找到的平行线对数量（当前固定为 0 或 1）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) 边缘检测 + O(M log M) 候选剪枝 + O(K^2) 配对搜索，N 为像素数，M 为 HoughLinesP 候选数，K 为剪枝后候选数 (<=48)。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像约 30-80ms（取决于边缘复杂度和候选线段数量）。 |
| 内存特征 (Memory Profile) | 需分配灰度图、模糊图、边缘图、闭运算图、结果图各一份 Mat；候选线段列表最多 48 项。 |

## 适用场景 / Use Cases
- **适合 (Suitable)：** 导轨/铁轨检测；PCB 板边平行线测量；传送带边缘定位；任何需要检测一对平行结构的工业视觉场景。
- **不适合 (Not Suitable)：** 需要检测多对平行线的场景（当前仅输出最佳一对）；曲线边缘的平行性检测；线段稀疏或边缘极弱的图像。

## 已知限制 / Known Limitations
1. 仅输出得分最高的一对平行线，不支持同时返回多对结果。
2. HoughLinesP 参数（阈值 80、线段间隙 10）为硬编码，不可配置。
3. Canny 阈值 (60, 180) 和高斯核大小 (5x5) 为硬编码，对不同对比度图像可能需要不同的边缘参数。
4. 重叠率阈值 0.25 硬编码，短重叠线段对可能被错误过滤。
5. 评分函数中各项权重（6.0、0.15、30.0、0.05）为硬编码，不可通过参数调整。
6. `PruneCandidates` 的冗余判定阈值（angleTolerance*0.35、offset 4.0、midpoint 12.0）为硬编码。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充候选预剪枝策略（PruneCandidates）、方向规范化、重叠率计算、综合评分函数的详细数学描述；完善 HoughLinesP 参数和形态学闭运算说明；增加适用场景与已知限制的源码级分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
