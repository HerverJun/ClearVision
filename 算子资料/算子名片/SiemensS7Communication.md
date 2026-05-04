# 西门子S7通信 / SiemensS7Communication

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SiemensS7CommunicationOperator` |
| 枚举值 (Enum) | `OperatorType.SiemensS7Communication` |
| 分类 (Category) | 通信 / Communication |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

**中文：**
西门子 S7 协议是西门子 PLC（S7-200/300/400/1200/1500 系列）的专有通信协议，运行在 TCP/IP 之上（ISO-on-TCP，端口 102）。该算子通过 `Acme.PlcComm` 封装层的 `PlcClientFactory.CreateSiemensS7` 创建 `IPlcClient` 实例，实现对 S7 PLC 数据区（DB、M、I、Q 等）的读写操作。协议层处理 COTP 连接建立、S7 协商（PDU 大小协商）和数据读写请求/响应的序列化与反序列化。数据类型支持覆盖 BOOL、BYTE、WORD、INT16、DWORD、INT32、FLOAT、DOUBLE 和 STRING，通过 `IPlcClient.ByteTransform` 进行字节序转换。

**English:**
The Siemens S7 protocol is a proprietary communication protocol for Siemens PLCs (S7-200/300/400/1200/1500 series), running over TCP/IP (ISO-on-TCP, port 102). This operator uses `PlcClientFactory.CreateSiemensS7` from the `Acme.PlcComm` library to create an `IPlcClient` instance for reading/writing S7 PLC data areas (DB, M, I, Q, etc.). The protocol layer handles COTP connection setup, S7 negotiation (PDU size negotiation), and data read/write request/response serialization. Data types cover BOOL, BYTE, WORD, INT16, DWORD, INT32, FLOAT, DOUBLE, and STRING, with byte-order conversion via `IPlcClient.ByteTransform`.

## 实现策略 / Implementation Strategy

- **继承 PLC 通信基类**：继承 `PlcCommunicationOperatorBase`，获得静态连接池（`Dictionary<string, IPlcClient>`）、心跳巡检（1s 间隔 Ping）、连接复用与自动重连、全局通信配置回退等共享能力。
- **连接键策略**：以 `S7:{IP}:{Port}:{CpuType}:{Rack}:{Slot}` 为连接池键，确保不同 PLC 配置各自独立连接。
- **全局配置回退**：当 `UseGlobalFallback=true` 且算子参数缺少 IP/Port 时，通过 `ResolveConnectionSettings` 自动从 `config.json` 的全局通信配置中回退获取，实现现场参数集中管理。
- **写入值动态解析**：`ResolveWriteValue` 方法按优先级从上游输入获取写入值：`JudgmentValue > Value > Data > 静态参数值`，支持流程中动态驱动写入。
- **轮询等待模式**：当 `PollingMode=WaitForValue` 时，读取操作进入循环轮询，按 `PollingCondition`（等于/不等于/大于/小于等）比较当前值与目标值，超时返回失败。支持数值精度比较（0.0001 容差）和字符串不区分大小写比较。

## 核心 API 调用链 / Core API Call Chain

1. `GetStringParam` / `GetIntParam` / `GetBoolParam` -- 读取全部 14 个参数
2. `ResolveWriteValue(@operator, inputs)` -- 解析写入值（上游输入优先级链）
3. `ResolveConnectionSettings(operatorIpAddress, operatorPort, "S7", useGlobalFallback)` -- 解析连接配置（含全局回退）
4. `GetOrCreateConnectionAsync(connectionKey, factory)` -- 基类连接池管理
   - `PlcClientFactory.CreateSiemensS7(ipAddress, cpuType, rack, slot)` -- 创建 S7 客户端
   - `((SiemensS7Client)s7Client).Port = port` -- 设置端口
   - `client.ConnectAsync()` -- 建立 S7 连接
5. **读取路径**：
   - `PollingMode=None` -> `ExecuteReadAsync(client, address, dataType, ct)`
     - `client.ReadAsync(address, 1, ct)` -- 读取 PLC 数据
     - `ConvertBytesToValue(client, result.Content, dataType)` -- 字节转值
   - `PollingMode=WaitForValue` -> `ExecuteReadWithPollingAsync(...)` -- 轮询等待
     - 循环 `client.ReadAsync` + `EvaluatePollingCondition` + `Task.Delay`
6. **写入路径**：
   - `ExecuteWriteAsync(client, address, dataType, writeValue, ct)`
     - `ConvertValueToBytes(client, writeValue, dataType)` -- 值转字节
     - `client.WriteAsync(address, bytes, ct)` -- 写入 PLC 数据
7. `AttachConnectionAuditInfo(output, connectionSource)` -- 附加连接审计信息

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `IpAddress` | `string` | `"192.168.0.1"` | - | PLC 设备的 IP 地址。为空且 UseGlobalFallback=true 时从全局配置回退。 |
| `Port` | `int` | `102` | [1, 65535] | S7 协议标准端口为 102（ISO-on-TCP）。 |
| `UseGlobalFallback` | `bool` | `false` | - | 启用后，缺失的 IP/Port 可回退到 config.json 中的全局通信配置。 |
| `CpuType` | `enum` | `"S71200"` | S7200, S7200Smart, S7300, S7400, S71200, S71500 | 目标 PLC 的 CPU 型号，影响 PDU 大小和连接参数协商。 |
| `Rack` | `int` | `0` | [0, 15] | PLC 机架号，S7-300/400 通常为 0。 |
| `Slot` | `int` | `1` | [0, 15] | PLC 插槽号，S7-300 通常为 2，S7-1200/1500 通常为 1。 |
| `Address` | `string` | `"DB1.DBW100"` | - | PLC 数据区地址。格式示例：DB1.DBW100（DB1 的 Word 偏移 100）、M0.0（Merker 位）、IW0（输入字）。 |
| `DataType` | `enum` | `"Word"` | Bit, Byte, Word, Int16, DWord, Int32, Float, Double, String | 读写数据的类型，决定字节转换方式。 |
| `Operation` | `enum` | `"Read"` | Read, Write | 操作类型：读取或写入。 |
| `WriteValue` | `string` | `""` | - | 写入值。支持从上游输入动态获取（优先级：JudgmentValue > Value > Data）。 |
| `PollingMode` | `enum` | `"None"` | None, WaitForValue | 轮询模式。None=直接读取，WaitForValue=循环读取直到满足条件。 |
| `PollingCondition` | `enum` | `"Equal"` | Equal, NotEqual, GreaterThan, LessThan, GreaterOrEqual, LessOrEqual | 轮询等待的比较条件。数值比较容差 0.0001，字符串不区分大小写。 |
| `PollingValue` | `string` | `"1"` | - | 轮询等待的目标值。 |
| `PollingTimeout` | `int` | `30000` | [100, 300000] | 轮询等待的最长超时时间（毫秒），最大 5 分钟。 |
| `PollingInterval` | `int` | `50` | [10, 5000] | 轮询读取间隔（毫秒）。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Data` | 数据 | `Any` | No | 可选输入端口。写入操作时，`ResolveWriteValue` 从此端口的上游输出中按优先级（JudgmentValue > Value > Data）提取写入值。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Response` | 响应 | `String` | 读取操作返回转换后的值（如 `"1234"`）；写入操作返回写入值确认。 |
| `Status` | 状态 | `Boolean` | 操作是否成功。成功时输出字典额外包含 Value、DataType、Status、Timestamp、ConnectionSource 字段。轮询模式下额外包含 PollingReadCount、PollingElapsedMs、PollingMatched。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) -- 单次 S7 读写请求；轮询模式下为 O(N) 其中 N = timeout/interval。 |
| 典型耗时 (Typical Latency) | 首次连接 20-80ms（COTP + S7 协商）；复用连接单次读写 2-15ms。轮询模式下总耗时取决于条件满足时机，最长达 PollingTimeout。 |
| 内存特征 (Memory Profile) | 每个 S7 连接约 4-8KB（含 PDU 缓冲区）。连接池与心跳均由基类静态管理，进程生命周期内常驻。单次读写分配约 256B（输出字典 + 日志）。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：
  - 与西门子 S7 系列 PLC（S7-200 Smart、S7-300/400、S7-1200/1500）进行数据读写。
  - 读取工艺参数（温度、压力、计数器值等）用于视觉检测结果关联。
  - 向 PLC 写入检测结果（OK/NG 判定、坐标数据等）。
  - 轮询等待 PLC 触发信号（如等待工件到位信号）后再执行视觉流程。
  - 多流程共享同一 PLC 连接时，连接池自动复用。
- **不适合 (Not Suitable)**：
  - 非西门子 PLC 设备（需使用对应协议算子）。
  - S7 通信加密（TLS）场景 -- 当前实现不支持。
  - 需要订阅/通知机制的实时数据推送 -- 本算子为请求/响应模式。

## 已知限制 / Known Limitations
1. **单值读取固定 Length=1**：基类 `GetReadElementCount` 始终返回 1，即每次 ReadAsync 只读取 1 个元素。若需批量读取多寄存器，需多次调用算子或扩展基类。
2. **WriteValue 静态参数仅作回退**：当上游输入存在 JudgmentValue/Value/Data 时，WriteValue 静态值被忽略。需注意流程连线逻辑。
3. **连接键含 CPU 类型**：同一 IP:Port 不同 CpuType 会创建独立连接，切换 CPU 类型不会复用已有连接。
4. **轮询模式无背压控制**：轮询间隔最短 10ms，高频轮询可能增加 PLC 通信负载。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档：精确提取 S7 协议细节、基类共享能力、轮询等待机制、动态写入值解析策略、连接键设计与性能特征 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
