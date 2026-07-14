# 模板匹配 / TemplateMatch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `TemplateMatchOperator` |
| 枚举值 (Enum) | `OperatorType.TemplateMatching` |
| 分类 ID (CategoryId) | `MatchingAndLocalization` |
| 分类 (Category) | 匹配与定位 |
| 分类顺序 (CategoryOrder) | 5 |
| 版本 (Version) | `1.2.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:MatchingAndLocalization`, `分类显示:匹配与定位`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于执行经典模板匹配，可限制旋转和尺度搜索范围；多目标结果通过基于 IoU 的 NMS 去重。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`、`Template`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Mask`。
- 参数解析覆盖 20 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.Rectangle`
- `Cv2.DrawMarker`
- `Cv2.PutText`
- `Cv2.CvtColor`
- `Cv2.GaussianBlur`
- `Cv2.Threshold`
- `Cv2.Canny`
- `Cv2.Sobel`
- `Cv2.Magnitude`
- `Cv2.Normalize`
- `Cv2.Resize`
- `Cv2.MatchTemplate`
- `Cv2.MinMaxLoc`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Method` | 匹配方法 | `enum` | CCoeffNormed | CCoeffNormed；SqDiff；SqDiffNormed；CCorr；CCorrNormed；CCoeff | Yes | - |
| `Domain` | 匹配域 | `enum` | Gray | Gray/灰度；Edge；Gradient/梯度 | Yes | - |
| `Threshold` | 匹配分数阈值 | `double` | 0.8 | [0, 1] | Yes | - |
| `MaxMatches` | 最大匹配数 | `int` | 1 | [1, 100] | Yes | - |
| `UseRoi` | 使用 ROI | `bool` | false | - | Yes | - |
| `RoiX` | ROIX | `int` | 0 | >= 0 | Yes | - |
| `RoiY` | ROIY | `int` | 0 | >= 0 | Yes | - |
| `RoiWidth` | ROI宽度 | `int` | 0 | >= 0 | Yes | - |
| `RoiHeight` | ROI高度 | `int` | 0 | >= 0 | Yes | - |
| `OriginMode` | Origin Mode | `enum` | Center | Center；TopLeft；Custom/自定义 | Yes | - |
| `OriginX` | Origin X | `double` | 0 | - | Yes | - |
| `OriginY` | Origin Y | `double` | 0 | - | Yes | - |
| `EnablePoseSearch` | 启用姿态搜索 | `bool` | false | - | Yes | - |
| `AngleStart` | 角度起点 | `double` | 0 | [-180, 180] | Yes | - |
| `AngleExtent` | 角度范围 | `double` | 0 | [0, 360] | Yes | - |
| `AngleStep` | 角度步长 | `double` | 1 | [0.1, 45] | Yes | - |
| `ScaleMin` | 最小尺度 | `double` | 1 | [0.2, 3] | Yes | - |
| `ScaleMax` | 最大尺度 | `double` | 1 | [0.2, 3] | Yes | - |
| `ScaleStep` | 尺度步长 | `double` | 0.05 | [0.01, 1] | Yes | - |
| `PyramidLevels` | 姿态搜索金字塔层数 | `int` | 1 | [1, 4] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Template` | 模板图像 | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Mask` | 搜索掩膜 | `Image` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Position` | 匹配位置 | `Point` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Score` | 匹配分数 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `NormalizedScore` | 规范化分数 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `RawResponse` | 原始响应值 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `SubpixelOffsetX` | 亚像素峰值 X 偏移 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `SubpixelOffsetY` | 亚像素峰值 Y 偏移 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `PeakCurvature` | 响应峰曲率 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Angle` | 匹配角度 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Scale` | 匹配尺度 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `IsMatch` | 是否匹配 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `Matches` | 匹配列表 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `MatchCount` | 匹配数量 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `AngleExtent` | metadata; ALL(EnablePoseSearch == true) | visible: -; hidden: ALL(EnablePoseSearch == false) | enabled: -; disabled: ALL(EnablePoseSearch == false) | ALL(EnablePoseSearch == false) | - | - | `TEMPLATE_MATCHING_POSE_SEARCH` |
| `AngleStart` | metadata; - | visible: -; hidden: ALL(EnablePoseSearch == false) | enabled: -; disabled: ALL(EnablePoseSearch == false) | ALL(EnablePoseSearch == false) | - | - | `TEMPLATE_MATCHING_POSE_SEARCH` |
| `AngleStep` | metadata; ALL(EnablePoseSearch == true) | visible: -; hidden: ALL(EnablePoseSearch == false) | enabled: -; disabled: ALL(EnablePoseSearch == false) | ALL(EnablePoseSearch == false) | - | - | `TEMPLATE_MATCHING_POSE_SEARCH` |
| `OriginX` | metadata; ALL(OriginMode == Custom) | visible: -; hidden: ALL(OriginMode != Custom) | enabled: -; disabled: ALL(OriginMode != Custom) | ALL(OriginMode != Custom) | - | - | `TEMPLATE_MATCHING_CUSTOM_ORIGIN` |
| `OriginY` | metadata; ALL(OriginMode == Custom) | visible: -; hidden: ALL(OriginMode != Custom) | enabled: -; disabled: ALL(OriginMode != Custom) | ALL(OriginMode != Custom) | - | - | `TEMPLATE_MATCHING_CUSTOM_ORIGIN` |
| `PyramidLevels` | metadata; - | visible: -; hidden: ALL(EnablePoseSearch == false) | enabled: -; disabled: ALL(EnablePoseSearch == false) | ALL(EnablePoseSearch == false) | - | - | `TEMPLATE_MATCHING_POSE_SEARCH` |
| `RoiHeight` | metadata; ALL(UseRoi == true) | visible: -; hidden: ALL(UseRoi == false) | enabled: -; disabled: ALL(UseRoi == false) | ALL(UseRoi == false) | - | - | `TEMPLATE_MATCHING_ROI_ONLY_WHEN_ENABLED` |
| `RoiWidth` | metadata; ALL(UseRoi == true) | visible: -; hidden: ALL(UseRoi == false) | enabled: -; disabled: ALL(UseRoi == false) | ALL(UseRoi == false) | - | - | `TEMPLATE_MATCHING_ROI_ONLY_WHEN_ENABLED` |
| `RoiX` | metadata; - | visible: -; hidden: ALL(UseRoi == false) | enabled: -; disabled: ALL(UseRoi == false) | ALL(UseRoi == false) | - | - | `TEMPLATE_MATCHING_ROI_ONLY_WHEN_ENABLED` |
| `RoiY` | metadata; - | visible: -; hidden: ALL(UseRoi == false) | enabled: -; disabled: ALL(UseRoi == false) | ALL(UseRoi == false) | - | - | `TEMPLATE_MATCHING_ROI_ONLY_WHEN_ENABLED` |
| `ScaleMax` | metadata; ALL(EnablePoseSearch == true) | visible: -; hidden: ALL(EnablePoseSearch == false) | enabled: -; disabled: ALL(EnablePoseSearch == false) | ALL(EnablePoseSearch == false) | - | - | `TEMPLATE_MATCHING_POSE_SEARCH` |
| `ScaleMin` | metadata; ALL(EnablePoseSearch == true) | visible: -; hidden: ALL(EnablePoseSearch == false) | enabled: -; disabled: ALL(EnablePoseSearch == false) | ALL(EnablePoseSearch == false) | - | - | `TEMPLATE_MATCHING_POSE_SEARCH` |
| `ScaleStep` | metadata; ALL(EnablePoseSearch == true) | visible: -; hidden: ALL(EnablePoseSearch == false) | enabled: -; disabled: ALL(EnablePoseSearch == false) | ALL(EnablePoseSearch == false) | - | - | `TEMPLATE_MATCHING_POSE_SEARCH` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`0FB6EFF97C2890E8ACA97E3EF08668AF293F8EAD52A2F463DFF6E5F68FDA45A5`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Center` | `Any` | 源码通过输出字典索引赋值写入。 |
| `FailureReason` | `String` | 源码通过输出字典索引赋值写入。 |
| `Found` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `MatchedTemplateHeight` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `MatchedTemplateWidth` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `Message` | `String` | 源码通过输出字典索引赋值写入。 |
| `PoseSearchEnabled` | `Boolean` | 源码通过输出字典索引赋值写入。 |
| `TemplateHeight` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `TemplateWidth` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `TopLeft` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 多数图像路径近似 `O(W*H)`；涉及轮廓、匹配或排序时会叠加候选数量相关开销。 |
| 典型耗时 (Typical Latency) | 未固定；取决于图像分辨率、ROI 范围、OpenCV 算法分支和输出可视化成本。 |
| 内存特征 (Memory Profile) | 通常需要输入图像、临时 Mat、结果图和输出封装内存；峰值随图像尺寸和中间副本数量增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 5 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：输入图像质量稳定、参数范围明确，需要在流程中完成图像处理、定位、测量或可视化输出的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
3. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.2.0 | 2026-07-14 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
