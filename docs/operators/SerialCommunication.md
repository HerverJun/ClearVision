# 串口通信 / SerialCommunication

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SerialCommunicationOperator` |
| 枚举值 (Enum) | `OperatorType.SerialCommunication` |
| 分类 ID (CategoryId) | `Communication` |
| 分类 (Category) | 通信 |
| 分类顺序 (CategoryOrder) | 13 |
| 版本 (Version) | `1.0.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| Execution | `Implemented` |
| AlgorithmQuality | `Unknown` |
| ProductionReadiness | `Unknown` |
| FieldValidation | `NotValidated` |
| Quality Evidence Refs |  |
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:Communication`, `分类显示:通信`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于RS-232/485 串口数据收发。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含外部资源访问逻辑，执行结果会受文件系统、网络、PLC、串口或外部服务状态影响。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Data`。
- 参数解析覆盖 9 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Math.Clamp`
- `Convert.ToByte`
- `Math.Max`
- `Math.Min`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `PortName` | 串口号 | `string` | COM1 | - | Yes | - |
| `BaudRate` | 波特率 | `enum` | 9600 | 9600；19200；38400；57600；115200 | Yes | - |
| `DataBits` | 数据位 | `int` | 8 | [5, 8] | Yes | - |
| `StopBits` | 停止位 | `enum` | One | One/1；OnePointFive/1.5；Two/2 | Yes | - |
| `Parity` | 校验位 | `enum` | None | None/无；Odd/奇校验；Even/偶校验 | Yes | - |
| `SendData` | 发送内容 | `string` | "" | - | Yes | - |
| `Encoding` | 编码 | `enum` | UTF8 | UTF8/UTF-8；ASCII；HEX | Yes | - |
| `TimeoutMs` | 超时(毫秒) | `int` | 3000 | [100, 30000] | Yes | - |
| `ResponseWaitMs` | 响应等待(毫秒) | `int` | 100 | [0, 30000] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Data` | 发送数据 | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Response` | 接收数据 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`53B241AD2E2435FAB993500C5BF935ECB16C7E7AF6469CC7432A5B681126478E`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `BytesReceived` | `Any` | 源码输出字典初始化中可见字段。 |
| `Port` | `Any` | 源码输出字典初始化中可见字段。 |
| `Success` | `Any` | 源码输出字典初始化中可见字段。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 主要受外部 I/O、网络或设备响应时间影响；本地处理通常随输入规模线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；取决于文件系统、网络、PLC/串口设备或外部服务响应。 |
| 内存特征 (Memory Profile) | 主要由请求/响应缓冲、序列化数据和外部资源句柄占用决定。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 6 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：需要把视觉流程与文件、HTTP、数据库、PLC、MQTT 或串口等外部系统连接的场景。
- 不适合 (Not Suitable)：外部设备、路径、网络或权限不可控，且流程不能容忍 I/O 超时或失败的场景。

## 已知限制 / Known Limitations
1. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
2. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
3. 外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-07-14 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
