# 图像保存 / ImageSave

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageSaveOperator` |
| 枚举值 (Enum) | `OperatorType.ImageSave` |
| 分类 ID (CategoryId) | `OutputAndAuxiliary` |
| 分类 (Category) | 输出与辅助 |
| 分类顺序 (CategoryOrder) | 14 |
| 版本 (Version) | `1.0.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:OutputAndAuxiliary`, `分类显示:输出与辅助`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于保存检测图像到本地硬盘。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 3 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.ImEncode`
- `Directory.Exists`
- `Directory.CreateDirectory`
- `Path.ChangeExtension`
- `Path.Combine`
- `File.Exists`
- `Path.GetDirectoryName`
- `Path.GetFileNameWithoutExtension`
- `Path.GetExtension`
- `File.WriteAllBytesAsync`
- `Path.IsPathRooted`
- `Path.DirectorySeparatorChar`
- `Path.AltDirectorySeparatorChar`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Directory` | 目录 | `string` | C:\ClearVision\NG_Images | - | Yes | - |
| `FileNameTemplate` | 命名规则 | `string` | NG_{yyyyMMdd_HHmmss}_{Guid}.jpg | - | Yes | - |
| `Quality` | 质量 | `int` | 90 | [1, 100] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `FilePath` | 保存路径 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `IsSuccess` | 是否成功 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `Directory` | required; - | visible: -; hidden: - | enabled: -; disabled: - | - | output_file | - | `IMAGE_SAVE_DIRECTORY_REQUIRED` |
| `FileName` | optional; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `IMAGE_SAVE_FILE_NAME_LEGACY_ALIAS` |
| `FileNameTemplate` | required; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `IMAGE_SAVE_FILE_NAME_REQUIRED` |
| `FolderPath` | optional; - | visible: -; hidden: - | enabled: -; disabled: - | - | output_file | - | `IMAGE_SAVE_DIRECTORY_LEGACY_ALIAS` |
| `JpegQuality` | optional; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `IMAGE_SAVE_QUALITY_LEGACY_ALIAS` |
| `Quality` | metadata; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `IMAGE_SAVE_QUALITY` |

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
- 组合指纹 (Generation Fingerprint)：`C932EBEE14A7490ECC46A15429AC43ADA696F4CB335CC9640374F5AD90C0BA7F`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `FileName` | `Any` | 源码输出字典初始化中可见字段。 |
| `FileSize` | `Any` | 源码输出字典初始化中可见字段。 |
| `Format` | `Any` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 源码输出字典初始化中可见字段。 |
| `Success` | `Any` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 源码输出字典初始化中可见字段。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 主要受外部 I/O、网络或设备响应时间影响；本地处理通常随输入规模线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；取决于文件系统、网络、PLC/串口设备或外部服务响应。 |
| 内存特征 (Memory Profile) | 通常需要输入图像、临时 Mat、结果图和输出封装内存；峰值随图像尺寸和中间副本数量增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 4 条 `OperatorExecutionOutput.Failure(...)` 路径。

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
| 1.0.0 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
