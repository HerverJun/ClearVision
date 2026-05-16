# 梯度形状匹配 / GradientShapeMatch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `GradientShapeMatchOperator` |
| 枚举值 (Enum) | `OperatorType.GradientShapeMatch` |
| 分类 (Category) | 匹配定位 |
| 版本 (Version) | `1.1.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于基于梯度方向特征的形状匹配，支持可选 ROI 搜索。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Gradient Direction Template Match` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Template`。
- 参数解析覆盖 12 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `Custom GradientShapeMatcher (OpenCvSharp.Mat gradient computation, 8-bin direction quantization, coarse-to-fine peak search with per-template NMS)`
- `OperatorBase.Get*Param(...)`
- `Cv2.Rectangle`
- `Cv2.DrawMarker`
- `Cv2.PutText`
- `Cv2.ImRead`
- `File.Exists`
- `Path.GetFullPath`
- `File.OpenRead`
- `Math.Max`
- `Math.Clamp`
- `Convert.ToHexString`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `TemplatePath` | 模板路径 | `file` | "" | - | Yes | - |
| `MinScore` | 最小分数(%) | `double` | 80 | [0, 100] | Yes | - |
| `TopK` | 返回候选数 | `int` | 1 | [1, 10] | Yes | - |
| `AngleRange` | 角度范围(度) | `int` | 180 | [0, 180] | Yes | - |
| `AngleStep` | 角度步长 | `int` | 1 | [1, 10] | Yes | - |
| `MagnitudeThreshold` | 梯度阈值 | `int` | 30 | [0, 255] | Yes | - |
| `EnableCache` | 启用缓存 | `bool` | true | - | Yes | - |
| `UseRoi` | 使用 ROI | `bool` | false | - | Yes | - |
| `RoiX` | ROI X | `int` | 0 | [0, 100000] | Yes | - |
| `RoiY` | ROI Y | `int` | 0 | [0, 100000] | Yes | - |
| `RoiWidth` | ROI Width | `int` | 0 | [0, 100000] | Yes | - |
| `RoiHeight` | ROI Height | `int` | 0 | [0, 100000] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 搜索图像 | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Template` | 模板图像 | `Image` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Position` | 匹配位置 | `Point` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Angle` | 旋转角度 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `IsMatch` | 是否匹配 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `Score` | 匹配分数 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Matches` | 匹配列表 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `CacheEnabled` | `Boolean` | 源码通过输出字典索引赋值写入。 |
| `DisplayHeight` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `DisplayWidth` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `Enabled` | `Boolean` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `MatchCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `Message` | `String` | 源码通过输出字典索引赋值写入。 |
| `SearchRegion` | `Any` | 源码通过输出字典索引赋值写入。 |
| `TemplateHeight` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `TemplateWidth` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(T * R * S) where T is template feature count, R is rotated template count, and S is scene pixels under search |
| 典型耗时 (Typical Latency) | GradientShapeMatchGoldenRunner baseline: 130 cases passed, avg runtime about 92 ms on 512x384 synthetic images. |
| 内存特征 (Memory Profile) | O(R * T) for rotated template storage plus bounded LRU cache (max 8 entries) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 4 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Edge-defined object localization under moderate lighting changes.
- 适合 (Suitable)：Rotation-invariant matching when target has clear gradient structure and limited symmetry.
- 适合 (Suitable)：Multi-instance detection with TopK output and position NMS.
- 不适合 (Not Suitable)：Low-texture or blank templates that yield fewer than 10 gradient features.
- 不适合 (Not Suitable)：Scenes with heavy scale variation (fixed-scale template matching only).
- 不适合 (Not Suitable)：Sub-pixel precision measurement workflows.

## 已知限制 / Known Limitations
1. Score is a directional agreement ratio (matching features / total template features) x 100, not a correlation coefficient.
2. Template cache is bounded to 8 entries with LRU eviction.
3. Low-feature templates (< 10 valid gradient features) fail with InvalidTemplate.
4. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
5. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
6. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
7. 外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.1.0 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
