# 图像采集 / ImageAcquisition

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageAcquisitionOperator` |
| 枚举值 (Enum) | `OperatorType.ImageAcquisition` |
| 分类 ID (CategoryId) | `Acquisition` |
| 分类 (Category) | 采集 |
| 分类顺序 (CategoryOrder) | 1 |
| 版本 (Version) | `1.0.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:Acquisition`, `分类显示:采集`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于从文件或相机采集图像。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Image`、`FilePath`。
- 参数解析覆盖 6 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.ImRead`
- `Cv2.ImDecode`
- `Cv2.CvtColor`
- `File.Exists`
- `ImageWrapper`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `SourceType` | 采集源 | `enum` | File | File/文件；Camera/相机 | Yes | - |
| `FilePath` | 文件路径 | `file` | "" | - | Yes | - |
| `CameraId` | 相机 | `cameraBinding` | "" | - | Yes | - |
| `ExposureTime` | 曝光时间(us) | `double` | 5000 | >= 1 | Yes | - |
| `Gain` | 增益(dB) | `double` | 1 | >= 0 | Yes | - |
| `TriggerMode` | 触发模式 | `enum` | Software | Software/软件触发；External/外部触发；Continuous/连续采集 | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Runtime supplied image | `Image` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `FilePath` | 文件路径输入 | `String` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `CameraBindingId` | optional; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `IMAGE_CAMERA_BINDING_ALIAS` |
| `CameraId` | optional; ALL(SourceType == Camera) | visible: -; hidden: ALL(SourceType == File) | enabled: -; disabled: ALL(SourceType == File) | ALL(SourceType == File) | camera_binding | Image | `IMAGE_CAMERA_REQUIRED_FOR_CAMERA_SOURCE` |
| `ExposureTime` | metadata; - | visible: -; hidden: ALL(SourceType == File) | enabled: -; disabled: ALL(SourceType == File) | ALL(SourceType == File) | - | - | `IMAGE_CAMERA_SETTING_DISABLED_FOR_FILE_SOURCE` |
| `FilePath` | optional; ALL(SourceType == File) | visible: -; hidden: ALL(SourceType == Camera) | enabled: -; disabled: ALL(SourceType == Camera) | ALL(SourceType == Camera) | image_file | Image | `IMAGE_FILE_REQUIRED_FOR_FILE_SOURCE` |
| `Gain` | metadata; - | visible: -; hidden: ALL(SourceType == File) | enabled: -; disabled: ALL(SourceType == File) | ALL(SourceType == File) | - | - | `IMAGE_CAMERA_SETTING_DISABLED_FOR_FILE_SOURCE` |
| `SourceType` | required; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `IMAGE_SOURCE_TYPE_REQUIRED` |
| `TriggerMode` | metadata; - | visible: -; hidden: ALL(SourceType == File) | enabled: -; disabled: ALL(SourceType == File) | ALL(SourceType == File) | - | - | `IMAGE_CAMERA_SETTING_DISABLED_FOR_FILE_SOURCE` |
| `cameraId` | optional; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `IMAGE_CAMERA_ID_LEGACY_ALIAS` |
| `sourceType` | optional; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `IMAGE_SOURCE_TYPE_LEGACY_ALIAS` |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 状态 | 支持位深 | 原生位深 | 支持通道 | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 失败码 | 证据 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | `Restricted` | CV_8U | CV_8U | 1, 3, 4 | Stage 2 conservative baseline: retain evidenced legacy 8U paths; reject higher depths until operator-specific evidence is added. | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit native numeric domain; no implicit MinMax conversion. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `E0_SOURCE_AUDIT` | `2.0` |

### 模式限制 / Mode Restrictions
| 输入端口 | 模式 | 状态 | 位深 | 通道 | 转换 | 输出 | 动态范围 | 条件 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - | - | - | - |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`E8BE8281A6F3F481929D5E6B06EF13914F687F5AAC00924AF12DB389F3D8F178`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Channels` | `Any` | 源码通过输出字典索引赋值写入。 |
| `CorrelationId` | `String` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `HostReceiveTimestampUtc` | `Any` | 源码输出字典初始化中可见字段。 |
| `Sequence` | `Any` | 源码输出字典初始化中可见字段。 |
| `Source` | `String` | 源码通过输出字典索引赋值写入。 |
| `TimestampSource` | `String` | 源码输出字典初始化中可见字段。 |
| `TrackId` | `String` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 主要受外部 I/O、网络或设备响应时间影响；本地处理通常随输入规模线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；取决于文件系统、网络、PLC/串口设备或外部服务响应。 |
| 内存特征 (Memory Profile) | 通常需要输入图像、临时 Mat、结果图和输出封装内存；峰值随图像尺寸和中间副本数量增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 14 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：需要把视觉流程与文件、HTTP、数据库、PLC、MQTT 或串口等外部系统连接的场景。
- 不适合 (Not Suitable)：外部设备、路径、网络或权限不可控，且流程不能容忍 I/O 超时或失败的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
2. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
3. 外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
