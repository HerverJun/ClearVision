# 图像采集 / ImageAcquisition

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageAcquisitionOperator` |
| 枚举值 (Enum) | `OperatorType.ImageAcquisition` |
| 分类 (Category) | 采集 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
中文：该算子是流程的**图像入口**，支持三种采集模式：文件读取、相机采集和图像透传。执行优先级为：
1. **透传模式**：若输入端口 `Image` 已有数据（`ImageWrapper` 或 `byte[]`），直接透传并增加引用计数，跳过采集逻辑。
2. **文件模式**：若显式配置了 `FilePath` 参数（优先级高于 `SourceType` 枚举），调用 `Cv2.ImRead` 加载图像文件。
3. **相机模式**：`SourceType=Camera` 时，通过 `ICameraManager` 获取相机实例，配置曝光/增益/触发模式，调用 `AcquireSingleFrameAsync` 采集单帧。支持 Frame-Driven（帧驱动流式采集）和 Software Trigger（软件触发单帧采集）两种触发模式。

> English: This operator is the **image entry point** of a pipeline, supporting three acquisition modes with strict priority: (1) passthrough if `Image` input already has data, (2) file reading via `Cv2.ImRead` if `FilePath` is explicitly set, (3) camera acquisition via `ICameraManager` with configurable exposure/gain/trigger mode, supporting both frame-driven streaming and software-triggered single-frame capture.

## 实现策略 / Implementation Strategy
- **三级优先级**：透传 > 显式文件路径 > SourceType 枚举。显式配置 `FilePath` 时即使 `SourceType=Camera` 也会走文件路径，避免配置冲突。
- **参数双名兼容**：所有参数支持大小写两种命名（`SourceType`/`sourceType`、`FilePath`/`filePath` 等），连线输入和算子参数均按优先级尝试。
- **相机参数来源分离**：相机参数优先从"系统设置 -> 相机管理"的 `bindingConfig` 获取，算子参数仅作为向后兼容 fallback。
- **Frame-Driven 流式支持**：当 `TriggerMode` 为帧驱动模式时，通过 `ICameraFrameStreamCoordinator.AcquireFrameAsync` 获取共享帧，避免重复采集。
- **PNG 尺寸快速解析**：对 `byte[]` 输入优先尝试从 PNG 头部解析宽高，避免完整解码；非 PNG 格式回退到 `ImageWrapper.FromBytes` 延迟属性。
- **引用计数透传**：`ImageWrapper` 透传时必须调用 `AddRef()`，因为当前算子结束后会 Release 输入。

> English: The implementation uses three-level priority (passthrough > explicit file path > SourceType enum), dual-case parameter naming, separates camera parameters from binding config vs operator params, supports frame-driven streaming, optimizes PNG dimension parsing from headers, and uses reference counting for passthrough.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetStringInput(inputs, "SourceType"/"FilePath")` -- 尝试从连线输入获取参数
2. `GetStringParam(@operator, "SourceType"/"FilePath", ...)` -- 从算子参数获取（兜底）
3. **透传路径**：
   - `ImageWrapper.TryGetFromObject(imgObj, out wrapper)` + `wrapper.AddRef()`
   - `ImageWrapper.TryParsePngDimensions(rawData)` -- PNG 头部快速解析
   - `ImageWrapper.FromBytes(rawData)` -- 非 PNG 回退解码
4. **文件路径**：
   - `File.Exists(filePath)` -- 文件存在性检查
   - `Cv2.ImRead(filePath, ImreadModes.Color)` -- OpenCV 文件读取
5. **相机路径**：
   - `_cameraManager.GetOrCreateByBindingAsync(cameraId)` -- 获取相机实例
   - `_cameraManager.FindBinding(cameraId)` -- 获取绑定配置
   - `CameraTriggerModeExtensions.Normalize(triggerMode)` -- 触发模式归一化
   - `_streamCoordinator.AcquireFrameAsync(cameraId, cancellationToken)` -- 帧驱动采集
   - `camera.SetExposureTimeAsync(exposureTime)` / `camera.SetGainAsync(gain)` -- 参数配置
   - `camera.AcquireSingleFrameAsync()` -- 软件触发单帧采集
   - `Cv2.ImDecode(imageData, ImreadModes.Color)` -- 解码相机返回的原始数据
6. `CreateImageOutput(mat, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `SourceType` | `enum` | `"File"` | File / Camera | 采集源类型。当显式配置了 `FilePath` 时，文件路径优先于此枚举。 |
| `FilePath` | `file` | `""` | 有效文件路径 | 图像文件路径。显式配置时自动切换为文件模式，忽略 `SourceType`。 |
| `CameraId` | `cameraBinding` | `""` | 相机绑定 ID | 相机绑定标识。`SourceType=Camera` 时必填。 |
| `ExposureTime` | `double` | `5000.0` | [1.0, +inf] | 曝光时间（微秒）。优先从相机绑定配置获取，此参数为 fallback。 |
| `Gain` | `double` | `1.0` | [0.0, +inf] | 增益（dB）。优先从相机绑定配置获取，此参数为 fallback。 |
| `TriggerMode` | `enum` | `"Software"` | Software / External | 触发模式。`Software` 为软件触发单帧；`External` 为外部触发（Frame-Driven）。优先从相机绑定配置获取。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | No | 可选图像输入。若提供则直接透传，跳过采集逻辑。 |
| `FilePath` | 文件路径输入 | `String` | No | 可选文件路径输入。若提供则覆盖算子参数中的 `FilePath`。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 图像 | `Image` | 采集或透传的图像。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 图像宽度。 |
| `Height` | `Integer` | 图像高度。 |
| `Channels` | `Integer` | 图像通道数。 |
| `Source` | `String` | 采集来源标识（`camera`、帧驱动模式配置值等）。 |
| `CameraId` | `String` | 使用的相机绑定 ID（仅相机模式）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 透传模式：`O(1)`（仅引用计数）。文件模式：`O(W*H*C)`（解码）。相机模式：取决于相机硬件延迟，通常 10-100ms。 |
| 典型耗时 (Typical Latency) | 透传：< 0.1ms。文件读取（1920x1080 JPEG）：5-20ms。相机采集：10-100ms（取决于曝光时间和接口速度）。 |
| 内存特征 (Memory Profile) | 透传模式无额外内存分配。文件/相机模式会创建一个新的 `Mat`，峰值内存约为图像大小。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：流程起点，从文件夹批量读取检测图像。
- **适合 (Suitable)**：在线检测，从工业相机实时采集图像。
- **适合 (Suitable)**：透传模式，上游已有图像数据时直接透传避免重复采集。
- **适合 (Suitable)**：混合场景，部分流程从文件测试、部分从相机上线，通过 `SourceType` 切换。
- **不适合 (Not Suitable)**：视频流连续采集（当前仅支持单帧）。
- **不适合 (Not Suitable)**：网络摄像头或 RTSP 流（需要专用视频采集算子）。

## 已知限制 / Known Limitations
1. 相机模式仅支持单帧采集，不支持连续流式采集（grab loop）。
2. `ExposureTime` 和 `Gain` 参数优先从相机绑定配置获取，算子参数仅在绑定配置缺失时生效，可能造成配置困惑。
3. 文件模式不支持通配符或目录批量扫描，每次只能读取一个文件。
4. `byte[]` 输入的 PNG 头部解析仅支持标准 PNG 格式，非 PNG 数据会回退到完整解码。
5. 帧驱动模式下如果 `ICameraFrameStreamCoordinator` 为默认的 `NoOp` 实例，帧驱动采集会静默失败。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充三级采集优先级、透传引用计数、PNG 快速解析、Frame-Driven 流式支持、相机参数来源分离、参数双名兼容等核心实现细节；重写算法原理、实现策略、API 调用链、参数语义、适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
