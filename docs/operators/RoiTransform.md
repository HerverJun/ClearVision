# ROI跟踪 / RoiTransform

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RoiTransformOperator` |
| 枚举值 (Enum) | `OperatorType.RoiTransform` |
| 分类 (Category) | 辅助 |
| 版本 (Version) | `1.0.1` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Transforms a base ROI using match pose (CenterX/CenterY/Angle/Scale) and outputs SearchRegion。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Pose-driven ROI rectangle transform` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`BaseRoi`、`Matches`；缺失时通常返回失败结果。
- 参数解析覆盖 1 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `BaseRoi + selected match -> TryReadPose/TryNormalizeDictionary -> RoiTracker.TransformRoi`
- `OperatorBase.Get*Param(...)`
- `Math.Round`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `MatchIndex` | Match Index | `int` | 0 | [0, 100] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `BaseRoi` | Base ROI | `Rectangle` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Matches` | Matches | `Any` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `SearchRegion` | Search Region | `Rectangle` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Height` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `Width` | `Integer` | 源码通过输出字典索引赋值写入。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(I+C) |
| 典型耗时 (Typical Latency) | No dedicated golden benchmark yet; covered by ROI tracker and caliper bridge tests |
| 内存特征 (Memory Profile) | O(C) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `Acme.Product/tests/Acme.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 5 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Passing shape-matching or planar-matching poses into downstream measurement operators as a tracked search ROI.
- 适合 (Suitable)：Translation, rotation, and scale adjustment of a known reference ROI between frames.
- 不适合 (Not Suitable)：Full multi-object tracking or selecting the best match by score inside this operator.
- 不适合 (Not Suitable)：Perspective or non-rigid ROI deformation where a rectangle bounding box is insufficient.

## 已知限制 / Known Limitations
1. Output is an integer bounding rectangle around the transformed ROI corners.
2. The operator does not clip the SearchRegion to image bounds and clamps non-positive scale values back to 1.0.
3. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
4. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
5. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
