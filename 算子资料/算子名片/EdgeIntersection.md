# 边线交点 / EdgeIntersection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `EdgeIntersectionOperator` |
| 枚举值 (Enum) | `OperatorType.EdgeIntersection` |
| 分类 (Category) | 定位 |
| IconName | `intersection` |
| Keywords | `intersection`, `cross point`, `line angle` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
**中文：**
该算子计算两条直线（或线段）的交点坐标及夹角。数学原理：给定线段 L1(P1->P2) 和 L2(P3->P4)，使用 Cramer 法则求解两直线参数方程的交点。分母 `D = (P1x-P2x)*(P3y-P4y) - (P1y-P2y)*(P3x-P4x)`，当 `|D| > 1e-9` 时两直线有唯一交点，交点坐标通过行列式展开计算。夹角使用向量点积公式 `cos(theta) = (v1 . v2) / (|v1| * |v2|)`，结果钳位至 [0, 90] 度。线段相交判定使用计算几何的经典 Orientation 测试（叉积符号法），处理了共线和端点重合的边界情况。

**English:**
This operator computes the intersection point and angle between two lines (or segments). Math: given segments L1(P1->P2) and L2(P3->P4), Cramer's rule solves the parametric line equations. The denominator `D = (P1x-P2x)*(P3y-P4y) - (P1y-P2y)*(P3x-P4x)`; when `|D| > 1e-9` there is a unique intersection computed via determinant expansion. The angle uses the dot product formula `cos(theta) = (v1 . v2) / (|v1| * |v2|)`, clamped to [0, 90] degrees. Segment intersection uses the classic Orientation test (cross-product sign method), handling collinear and endpoint-coincident edge cases.

## 实现策略 / Implementation Strategy
- **双模式切换：** `IntersectionMode` 参数控制 `InfiniteLine`（无限延长线交点）和 `SegmentOnly`（仅限线段范围内相交）两种模式。`InfiniteLine` 模式下 `HasIntersection` 等价于直线有交点；`SegmentOnly` 模式下 `HasIntersection` 等价于 `SegmentsIntersect`。
- **角度钳位：** 两线夹角计算后，若 >90 度则取补角 `180 - angle`，确保结果始终在 [0, 90] 范围内。
- **退化线检测：** 线段长度 <= 1e-6 时直接返回 Failure，避免零向量除法。
- **鲁棒的线数据解析：** `TryParseLine` 支持 `LineData` 对象直接传入、`IDictionary<string,object>`（含 StartX/StartY/EndX/EndY）、以及非泛型 `IDictionary`，兼容多种上游输出格式。数值类型支持 float/double/int/long/string 自动转换。
- **Orientation 测试：** `DoSegmentsIntersect` 使用三次叉积方向判定 + `OnSegment` 共线检测，正确处理 T 形交叉、端点接触等边界情况。
- 本算子为纯数学计算，不涉及图像处理，无 OpenCV 依赖。

## 核心 API 调用链 / Core API Call Chain
```
1. inputs.TryGetValue("Line1")                   -- 获取线段 1
2. inputs.TryGetValue("Line2")                   -- 获取线段 2
3. TryParseLine(line1Obj) / TryParseLine(line2Obj)
   └─ 支持 LineData / IDictionary<string,object> / IDictionary
4. 退化检测: line.Length <= 1e-6 => Failure
5. GetStringParam("IntersectionMode", "InfiniteLine")
6. 计算方向向量 v1, v2 及其模长 norm1, norm2
7. 夹角计算: cos = dot(v1,v2)/(norm1*norm2), angle = acos(cos)*180/PI
   └─ angle > 90 => angle = 180 - angle
8. Cramer 法则求交点: denominator, pxNumerator, pyNumerator
   └─ |denominator| > 1e-9 => hasLineIntersection = true
9. DoSegmentsIntersect(line1, line2)
   ├─ Orientation(a,b,c), Orientation(a,b,d)
   ├─ Orientation(c,d,a), Orientation(c,d,b)
   └─ OnSegment 检测共线情况
10. hasIntersection = (mode=="SegmentOnly") ? segmentsIntersect : hasLineIntersection
11. 返回 {Point, Angle, HasIntersection, SegmentsIntersect}
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `IntersectionMode` | `enum` | `"InfiniteLine"` | `InfiniteLine` / `SegmentOnly` | 交点计算模式。InfiniteLine 将线段延长为无限直线求交；SegmentOnly 仅当交点在线段范围内时 HasIntersection 为 true。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Line1` | Line 1 | `LineData` | Yes | 第一条线段，需含 StartX/StartY/EndX/EndY 四个分量。 |
| `Line2` | Line 2 | `LineData` | Yes | 第二条线段，需含 StartX/StartY/EndX/EndY 四个分量。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Point` | Point | `Point` | 两线交点坐标。无交点（平行/退化）时返回 (0,0)。 |
| `Angle` | Angle | `Float` | 两线夹角，范围 [0, 90] 度。 |
| `HasIntersection` | Has Intersection | `Boolean` | 是否存在交点（受 IntersectionMode 影响）。 |
| `SegmentsIntersect` | Segments Intersect | `Boolean` | 两条线段是否在有限范围内实际相交（不受 IntersectionMode 影响）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1)，纯数学运算，与图像尺寸无关。 |
| 典型耗时 (Typical Latency) | < 0.1ms，纯 CPU 数值计算。 |
| 内存特征 (Memory Profile) | 极低，仅分配输出字典和临时数值变量。 |

## 适用场景 / Use Cases
- **适合 (Suitable)：** 两条边缘线段的交点定位（如角点、顶点计算）；线段夹角测量；与线检测算子（HoughLinesP、LineMeasurement）串联使用；判断两线段是否相交（SegmentOnly 模式）。
- **不适合 (Not Suitable)：** 多线交点的批量计算（需逐对调用）；曲线交点计算（仅支持直线段）；需要交点到线段端点距离信息的场景。

## 已知限制 / Known Limitations
1. 平行线或近平行线（`|denominator| <= 1e-9`）时返回交点 (0,0)，下游无法区分"无交点"和"交点恰好在原点"。
2. `SegmentsIntersect` 的 Orientation 测试使用 1e-9 容差，极短线段或极端坐标值可能导致误判。
3. 角度输出为 [0, 90] 度，丢失了两线的相对方向信息（锐角/钝角不可区分）。
4. 不支持射线（半无限直线）交点计算。
5. 线数据解析支持非泛型 `IDictionary`，但 key 查找不区分大小写的前提是 key 已通过 `ToString()` 转换。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充 Cramer 法则数学推导、Orientation 测试原理、双模式切换逻辑、退化线检测；完善参数语义和输出端口说明（新增 SegmentsIntersect）；增加适用场景与已知限制的源码级分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
