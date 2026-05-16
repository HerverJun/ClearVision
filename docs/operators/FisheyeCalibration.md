# Fisheye Calibration / FisheyeCalibration

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `FisheyeCalibrationOperator` |
| 枚举值 (Enum) | `OperatorType.FisheyeCalibration` |
| 分类 (Category) | 标定 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Calibrates fisheye camera intrinsics and distortion parameters using chessboard or circle grid patterns。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 9 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.CvtColor`
- `Cv2.CornerSubPix`
- `Cv2.DrawChessboardCorners`
- `Cv2.PutText`
- `Cv2.ImRead`
- `Cv2.FishEye`
- `Cv2.FindChessboardCorners`
- `Cv2.FindCirclesGrid`
- `Directory.Exists`
- `Path.GetFileName`
- `Path.GetDirectoryName`
- `Directory.CreateDirectory`
- `File.WriteAllText`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `PatternType` | Pattern Type | `enum` | Chessboard | Chessboard/Chessboard；CircleGrid/CircleGrid | Yes | - |
| `BoardWidth` | Board Width | `int` | 9 | [2, 30] | Yes | - |
| `BoardHeight` | Board Height | `int` | 6 | [2, 30] | Yes | - |
| `SquareSize` | Square Size(mm) | `double` | 25 | [0.1, 1000] | Yes | - |
| `Mode` | Mode | `enum` | SingleImage | SingleImage/SingleImage；FolderCalibration/FolderCalibration | Yes | - |
| `ImageFolder` | Image Folder | `string` | "" | - | Yes | - |
| `CalibrationOutputPath` | Calibration Output Path | `string` | fisheye_calibration_result.json | - | Yes | - |
| `RecomputeExtrinsic` | Recompute Extrinsic | `bool` | true | - | Yes | - |
| `CheckConditions` | Check Conditions | `bool` | true | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Input Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Result Image | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `CalibrationData` | Calibration Data | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Accepted` | `Boolean` | 源码通过输出字典索引赋值写入。 |
| `CalibrationError` | `Float` | 源码通过输出字典索引赋值写入。 |
| `CornerCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `Found` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `ImageCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `MaxReprojectionError` | `Float` | 源码通过输出字典索引赋值写入。 |
| `Message` | `String` | 源码通过输出字典索引赋值写入。 |
| `OutputPath` | `String` | 源码通过输出字典索引赋值写入。 |
| `TotalImages` | `Image` | 源码通过输出字典索引赋值写入。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 主要受外部 I/O、网络或设备响应时间影响；本地处理通常随输入规模线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；取决于文件系统、网络、PLC/串口设备或外部服务响应。 |
| 内存特征 (Memory Profile) | 通常需要输入图像、临时 Mat、结果图和输出封装内存；峰值随图像尺寸和中间副本数量增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `Acme.Product/tests/Acme.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 8 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：需要把视觉流程与文件、HTTP、数据库、PLC、MQTT 或串口等外部系统连接的场景。
- 不适合 (Not Suitable)：外部设备、路径、网络或权限不可控，且流程不能容忍 I/O 超时或失败的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
3. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
4. 外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
