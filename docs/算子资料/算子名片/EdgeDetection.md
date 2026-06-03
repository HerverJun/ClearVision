# Edge Detection / CannyEdge

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `CannyEdgeOperator` |
| 枚举值 (Enum) | `OperatorType.EdgeDetection` |
| 分类 (Category) | 特征提取 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Detects edges with Canny and optional auto-thresholding。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含模型或推理资源解析逻辑，核心结果取决于模型文件、标签配置、阈值和运行时推理环境。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 14 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.GaussianBlur`
- `Cv2.Canny`
- `Cv2.CountNonZero`
- `Cv2.Sobel`
- `Cv2.Magnitude`
- `Cv2.Normalize`
- `Cv2.Threshold`
- `Cv2.Resize`
- `Cv2.CvtColor`
- `Cv2.CalcHist`
- `File.Exists`
- `Math.Clamp`
- `Math.Min`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Method` | Method | `enum` | Canny | Canny/Canny；OnnxEdge/ONNX Edge | Yes | - |
| `Threshold1` | Low Threshold | `double` | 50 | [0, 255] | Yes | - |
| `Threshold2` | High Threshold | `double` | 150 | [0, 255] | Yes | - |
| `AutoThreshold` | Auto Threshold | `bool` | false | - | Yes | - |
| `AutoThresholdSigma` | Auto Threshold Sigma | `double` | 0.33 | [0.01, 1] | Yes | - |
| `AutoThresholdStrategy` | Auto Threshold Strategy | `enum` | MedianIntensity | MedianIntensity/Median Intensity；GradientPercentile/Gradient Percentile；RecallGuardPercentile/Recall Guard Percentile；OtsuGradient/Otsu Gradient | Yes | - |
| `EnableGaussianBlur` | Enable Gaussian Blur | `bool` | true | - | Yes | - |
| `GaussianKernelSize` | Gaussian Kernel Size | `int` | 5 | [3, 15] | Yes | - |
| `ApertureSize` | Sobel Aperture Size | `enum` | 3 | 3/3；5/5；7/7 | Yes | - |
| `L2Gradient` | L2 梯度 | `bool` | false | - | Yes | 使用 L2 范数计算梯度幅值，更精确但稍慢 |
| `EdgeModelPath` | Edge Model Path | `file` | "" | - | Yes | - |
| `EdgeModelId` | Edge Model Id | `string` | "" | - | Yes | - |
| `ModelCatalogPath` | Model Catalog Path | `file` | "" | - | Yes | - |
| `EdgeBinarizationThreshold` | Edge Binarization Threshold | `double` | 0.5 | [0, 1] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Edges` | Edges | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `EdgePixelRatio` | `Float` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `InputBitDepth` | `Any` | 源码输出字典初始化中可见字段。 |
| `ModelSource` | `String` | 源码输出字典初始化中可见字段。 |
| `ResolvedModelCatalogPath` | `String` | 源码输出字典初始化中可见字段。 |
| `Threshold1Used` | `Any` | 源码输出字典初始化中可见字段。 |
| `Threshold2Used` | `Any` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 推理路径主要受模型规模、输入尺寸和硬件后端影响；后处理通常随候选数量线性或近似线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；取决于模型大小、输入尺寸、CPU/GPU/ONNX Runtime 后端和候选数量。 |
| 内存特征 (Memory Profile) | 需要模型会话、输入张量、输出张量和后处理集合内存；峰值随模型与输入尺寸增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 5 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：模型、标签和阈值已完成现场校准，需要把推理结果接入视觉流程的场景。
- 不适合 (Not Suitable)：外部设备、路径、网络或权限不可控，且流程不能容忍 I/O 超时或失败的场景。
- 不适合 (Not Suitable)：模型未完成验证、标签映射不稳定或现场数据分布明显偏离训练数据的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
3. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
4. 外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。
5. 模型推理类路径依赖模型文件、标签、运行时库和硬件后端，算法准确率不由算子元数据单独保证。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
