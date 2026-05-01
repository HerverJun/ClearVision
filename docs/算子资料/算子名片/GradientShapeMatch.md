# 梯度形状匹配 / GradientShapeMatch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `GradientShapeMatchOperator` |
| 枚举值 (Enum) | `OperatorType.GradientShapeMatch` |
| 分类 (Category) | 匹配定位 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子不是直接在原始灰度图上做相关性匹配，而是使用自定义 `GradientShapeMatcher` 基于**梯度方向特征**进行模板训练和匹配。

核心思想是：

1. 从模板图中提取梯度幅值足够大的边缘特征点；
2. 把每个特征点的梯度方向量化为 `8` 个方向桶；
3. 针对 `-AngleRange ~ +AngleRange` 按 `AngleStep` 预生成一组旋转模板；
4. 对场景图同样计算梯度方向图；
5. 在候选位置比较模板方向与场景方向是否一致；
6. 以“方向一致的特征点数 / 模板特征点总数”作为匹配分数，再乘 `100` 输出百分比得分。

源码中方向匹配不是严格同一方向，而是允许相邻方向桶匹配：

- `diff <= 1` 视为方向匹配成立。

因此这是一种对边缘方向有一定容差的离散梯度模板匹配。多目标场景通过 `MatchTopK` 搜集候选峰值，再用基于位置距离的 NMS 保留 TopK 结果。

> English: The operator trains a bank of rotated gradient templates, quantizes edge directions into 8 bins, scores scene positions by directional agreement ratio, and can return TopK candidates with position NMS.

## 实现策略 / Implementation Strategy
当前实现有几项很重要的源码行为：

- **训练-匹配分离**：匹配前会先训练模板，不是每次简单地对原图旋转模板直接做相关性匹配。
- **旋转模板缓存**：算子内部维护 `_matcherCache`，缓存键包含模板内容 SHA256、角度范围、角度步长和梯度阈值；缓存上限为 8 项并使用 LRU 淘汰。
- **模板来源有优先级**：优先使用输入端口 `Template`，否则读取 `TemplatePath`。
- **可选 ROI 搜索**：`UseRoi` 打开后会限制搜索区域，并在输出中回写 `SearchRegion`。
- **单目标与多目标共用匹配器**：`Match(...)` 返回最佳结果；`MatchTopK(...)` 返回候选列表，算子按 `TopK` 输出 `Matches` 与 `MatchCount`。
- **结构化失败**：模板有效特征点少于 `10` 时抛出 `GradientShapeInvalidTemplateException`，其基类 `GradientShapeMatchException` 携带 `FailureReason=InvalidTemplate`，算子捕获后返回成功包裹的结构化 NG 输出。
- **可视化尺寸**：结果框使用模板真实宽高绘制，而不是固定 `80x80` 框。

> English: The implementation is a cached train-once / match-many gradient-template matcher with rotation support, optional ROI, TopK output, position NMS, and structured low-feature-template failure handling.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs)`
2. `GetStringParam / GetIntParam / GetDoubleParam / GetBoolParam`
3. 获取模板：输入端口 `Template` 优先，否则读取 `TemplatePath`
4. 获取或创建缓存 `GradientShapeMatcher`
5. `matcher.Train(template, angleRange)`
   - `EnsureGray(...)`
   - `ExtractFeatures(...)`
   - `CreateRotatedTemplate(...)`
6. 根据 `TopK` 调用：
   - `matcher.Match(srcImage, minScore, searchRegion)`
   - `matcher.MatchTopK(srcImage, minScore, searchRegion, topK)`
7. 匹配内部调用：
   - `ComputeSceneGradients(...)`
   - `ComputeMatchScore(...)`
   - `RefineMatch(...)`
   - `FindAllCandidates(...)` + position NMS
8. `Cv2.Rectangle(...)` / `Cv2.DrawMarker(...)` / `Cv2.PutText(...)`
9. `CreateImageOutput(resultImage, additionalData)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `TemplatePath` | `file` | `""` | 文件路径 | 未提供模板输入端口时，从此路径加载模板。 |
| `MinScore` | `double` | `80.0` | `[0.0, 100.0]` | 最小匹配分数，单位为百分比。得分来自“方向匹配特征比例 x 100”。 |
| `TopK` | `int` | `1` | `[1, 10]` | 返回候选匹配数。`1` 时兼容最佳匹配输出；大于 `1` 时输出 `Matches` 列表。 |
| `AngleRange` | `int` | `180` | `[0, 180]` | 训练旋转模板时的角度范围，表示从 `-AngleRange` 到 `+AngleRange`。 |
| `AngleStep` | `int` | `1` | `[1, 10]` | 训练旋转模板的步长。步长越小，旋转鲁棒性更高，但训练成本和模板数量也更大。 |
| `MagnitudeThreshold` | `int` | `30` | `[0, 255]` | 梯度幅值阈值。只有强于该阈值的边缘点才会进入模板特征集。 |
| `EnableCache` | `bool` | `true` | - | 是否复用训练后的 matcher。关闭时临时 matcher 会在执行结束后释放。 |
| `UseRoi` | `bool` | `false` | - | 是否启用 ROI 搜索区域。 |
| `RoiX` | `int` | `0` | `[0, 100000]` | ROI 左上角 X。 |
| `RoiY` | `int` | `0` | `[0, 100000]` | ROI 左上角 Y。 |
| `RoiWidth` | `int` | `0` | `[0, 100000]` | ROI 宽度。 |
| `RoiHeight` | `int` | `0` | `[0, 100000]` | ROI 高度。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 搜索图像 | `Image` | Yes | 搜索图像。 |
| `Template` | 模板图像 | `Image` | No | 可选模板输入。若提供，会优先于 `TemplatePath`。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 匹配结果图。成功时绘制模板真实宽高框、中心标记和角度文字；失败时绘制 NG 信息。 |
| `Position` | 匹配位置 | `Point` | 最佳匹配位置对象。 |
| `Angle` | 旋转角度 | `Float` | 最佳匹配角度。 |
| `IsMatch` | 是否匹配 | `Boolean` | 是否满足最小分数阈值。 |
| `Score` | 匹配分数 | `Float` | 百分比得分，范围通常在 `0~100`。 |
| `Matches` | 匹配列表 | `Any` | TopK 候选列表，每项包含 `Position`、`X`、`Y`、`Angle`、`Score`。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` / `Height` | `Integer` | 输出图像尺寸。 |
| `IsMatch` | `Boolean` | 匹配是否通过。 |
| `Score` | `Double` | 最佳匹配百分比分数。 |
| `Position` | `Point` | 最佳匹配点。 |
| `X` / `Y` | `Integer` | 最佳匹配位置坐标。 |
| `Angle` | `Double` | 最佳匹配角度。 |
| `MatchCount` | `Integer` | 有效候选匹配数。 |
| `Matches` | `Array` | TopK 候选列表。 |
| `TemplateWidth` / `TemplateHeight` | `Integer` | 训练模板真实尺寸。 |
| `DisplayWidth` / `DisplayHeight` | `Integer` | 可视化框使用的宽高。 |
| `CacheEnabled` | `Boolean` | 本次是否启用 matcher 缓存。 |
| `TopK` | `Integer` | 本次请求的候选数量。 |
| `SearchRegion` | `Object` | ROI 开关与实际搜索区域。 |
| `FailureReason` | `String` | 结构化失败原因；低特征模板为 `InvalidTemplate`。 |
| `Message` | `String` | 失败或未匹配时的说明。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 训练阶段约为 `O(R * T)`；匹配阶段约为 `O(R * S * T)`，其中 `R` 为旋转模板数，`S` 为搜索区域候选位置数，`T` 为模板特征点数。 |
| 典型耗时 (Typical Latency) | `GradientShapeMatchGoldenRunner` baseline：117/117 passed，512x384 synthetic cases 平均约 98.807 ms，最大约 632.409 ms。 |
| 内存特征 (Memory Profile) | 旋转模板集会缓存在 `_matcherCache` 中，缓存体积随角度范围和模板复杂度增加；缓存上限 8 项。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：目标主要由边缘和轮廓定义、光照变化较大但边缘结构相对稳定的场景。
- **适合 (Suitable)**：目标存在旋转变化，但尺度变化不大的定位任务。
- **适合 (Suitable)**：希望避免直接使用灰度相关性、转而使用边缘方向一致性做匹配的场景。
- **适合 (Suitable)**：需要 TopK 多候选输出和位置 NMS 的多实例粗定位任务。
- **不适合 (Not Suitable)**：低纹理或空白模板；少于 10 个有效梯度特征时会返回 `FailureReason=InvalidTemplate`。
- **不适合 (Not Suitable)**：重尺度变化场景；当前实现是 fixed-scale template matching。
- **不适合 (Not Suitable)**：亚像素精密测量；建议先用该算子粗定位，再接测量类算子。

## 已知限制 / Known Limitations
1. 分数是“方向一致特征点数 / 模板特征点总数 x 100”，不是灰度相关系数，也不等同于几何重投影置信度。
2. 当前匹配是固定尺度；若目标存在明显缩放，应先做尺度归一、金字塔搜索或改用尺度感知匹配。
3. 模板有效梯度特征点少于 `10` 时会返回结构化失败 `FailureReason=InvalidTemplate`。
4. TopK 候选使用位置距离 NMS，未做严格模板框 IoU NMS；密集重复纹理场景仍需业务侧二次筛选。
5. 亚像素精度不是该算子的目标；后续几何量测应使用专门测量算子复核。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.1.0 | 2026-04-28 | 增补 MatchTopK / Matches 多候选输出、位置 NMS、结构化 `FailureReason=InvalidTemplate`，并回填 contract/golden evidence |
| 1.0.3 | 2026-04-26 | 低特征模板失败契约结构化：`<10` 特征点返回 `[InvalidTemplate]`，golden runner 已锁定该错误码 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码补充梯度方向模板训练、缓存键风险、分数语义与运行时输出说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |

## 2026-04-12 Compatibility Update / 兼容性更新
- 新增可选 ROI 搜索参数：UseRoi、RoiX、RoiY、RoiWidth、RoiHeight，默认关闭。
- 特征稀疏化已改为按响应强度优先并结合局部抑制，不再按扫描顺序抢占网格。
- EnableCache=false 时的临时 matcher 资源会在退出路径释放。
