# 三菱MC通信 / MitsubishiMcCommunication

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MitsubishiMcCommunicationOperator` |
| 枚举值 (Enum) | `OperatorType.MitsubishiMcCommunication` |
| 分类 ID (CategoryId) | `Communication` |
| 分类 (Category) | 通信 |
| 分类顺序 (CategoryOrder) | 13 |
| 版本 (Version) | `1.1.0` |
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
该算子用于三菱 MC 协议 PLC 读写通信。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含外部资源访问逻辑，执行结果会受文件系统、网络、PLC、串口或外部服务状态影响。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Data`。
- 参数解析覆盖 10 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `PlcClientFactory.CreateMitsubishiMc`
- `Math.Min`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `ProfileId` | PLC Profile | `string` | "" | - | Yes | - |
| `Address` | PLC Address | `string` | D100 | - | Yes | - |
| `Length` | Read Length | `int` | 1 | [1, 999] | Yes | - |
| `Operation` | 操作 | `enum` | Read | Read；Write | Yes | - |
| `WriteValue` | Write Value | `string` | "" | - | Yes | - |
| `PollingMode` | Polling Mode | `enum` | None | None/无；WaitForValue/Wait For Value | Yes | 读取时是否启用轮询等待。 |
| `PollingCondition` | Polling Condition | `enum` | Equal | Equal；NotEqual/Not Equal；GreaterThan/Greater Than；LessThan/Less Than；GreaterOrEqual/Greater Or Equal；LessOrEqual/Less Or Equal | Yes | 轮询等待时用于判断目标值是否满足的条件。 |
| `PollingValue` | Polling Value | `string` | 1 | - | Yes | 轮询等待时要匹配的目标值。 |
| `PollingTimeout` | Polling Timeout (ms) | `int` | 30000 | [100, 300000] | Yes | 轮询等待的最大持续时间（毫秒）。 |
| `PollingInterval` | Polling Interval (ms) | `int` | 50 | [10, 5000] | Yes | 轮询读取之间的时间间隔（毫秒）。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Data` | Data | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Response` | Response | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `Status` | Status | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `Address` | required; - | visible: -; hidden: - | enabled: -; disabled: - | - | plc_address | - | `MITSUBISHI_PLC_ADDRESS_REQUIRED` |
| `Length` | metadata; - | visible: -; hidden: ALL(Operation != Read) | enabled: ALL(Operation == Read); disabled: - | ALL(Operation != Read) | - | - | `MITSUBISHI_READ_LENGTH_ONLY_FOR_READ` |
| `Operation` | required; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `MITSUBISHI_PLC_OPERATION_REQUIRED` |
| `PollingCondition` | metadata; - | visible: -; hidden: ANY(Operation != Read \|\| PollingMode != WaitForValue) | enabled: ALL(Operation == Read && PollingMode == WaitForValue); disabled: - | ANY(Operation != Read \|\| PollingMode != WaitForValue) | - | - | `MITSUBISHI_POLLING_CONDITION_ONLY_WHEN_WAITING` |
| `PollingInterval` | metadata; - | visible: -; hidden: ANY(Operation != Read \|\| PollingMode != WaitForValue) | enabled: ALL(Operation == Read && PollingMode == WaitForValue); disabled: - | ANY(Operation != Read \|\| PollingMode != WaitForValue) | - | - | `MITSUBISHI_POLLING_INTERVAL_ONLY_WHEN_WAITING` |
| `PollingMode` | metadata; - | visible: -; hidden: ALL(Operation != Read) | enabled: ALL(Operation == Read); disabled: - | ALL(Operation != Read) | - | - | `MITSUBISHI_POLLING_ONLY_FOR_READ` |
| `PollingTimeout` | metadata; - | visible: -; hidden: ANY(Operation != Read \|\| PollingMode != WaitForValue) | enabled: ALL(Operation == Read && PollingMode == WaitForValue); disabled: - | ANY(Operation != Read \|\| PollingMode != WaitForValue) | - | - | `MITSUBISHI_POLLING_TIMEOUT_ONLY_WHEN_WAITING` |
| `PollingValue` | metadata; - | visible: -; hidden: ANY(Operation != Read \|\| PollingMode != WaitForValue) | enabled: ALL(Operation == Read && PollingMode == WaitForValue); disabled: - | ANY(Operation != Read \|\| PollingMode != WaitForValue) | - | - | `MITSUBISHI_POLLING_VALUE_ONLY_WHEN_WAITING` |
| `ProfileId` | required; - | visible: -; hidden: - | enabled: -; disabled: - | - | plc_profile | - | `MITSUBISHI_PLC_PROFILE_REQUIRED` |
| `WriteValue` | optional; - | visible: -; hidden: ALL(Operation != Write) | enabled: ALL(Operation == Write); disabled: - | ALL(Operation != Write) | - | - | `MITSUBISHI_WRITE_VALUE_ONLY_FOR_WRITE` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`D9DAA52E3334A95828B46E9A466902AB7E19759BD18B4DA1A5396FC83CD484CC`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `JudgmentValue` | `Any` | 源码输出字典初始化中可见字段。 |
| `PollingElapsedMs` | `Any` | 源码通过输出字典索引赋值写入。 |
| `PollingMatched` | `Any` | 源码通过输出字典索引赋值写入。 |
| `PollingReadCount` | `Integer` | 源码通过输出字典索引赋值写入。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 主要受外部 I/O、网络或设备响应时间影响；本地处理通常随输入规模线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；取决于文件系统、网络、PLC/串口设备或外部服务响应。 |
| 内存特征 (Memory Profile) | 主要由请求/响应缓冲、序列化数据和外部资源句柄占用决定。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。

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
| 1.1.0 | 2026-08-31 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
