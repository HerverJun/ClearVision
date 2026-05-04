# 三菱MC通信 / MitsubishiMcCommunication

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MitsubishiMcCommunicationOperator` |
| 枚举值 (Enum) | `OperatorType.MitsubishiMcCommunication` |
| 分类 (Category) | 通信 / Communication |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

**中文：**
三菱 MC（Melsec Communication）协议是三菱 PLC（FX 系列、Q 系列、L 系列、iQ-R 系列等）的二进制通信协议，运行在 TCP/IP 之上（默认端口 5002）。该算子通过 `PlcClientFactory.CreateMitsubishiMc` 创建 `IPlcClient` 实例，使用 MC 协议的 3E/4E 帧格式进行数据读写。MC 协议使用软元件代码 + 偏移地址寻址（如 D100 表示数据寄存器偏移 100、M0 表示内部继电器位 0），支持对 D、M、X、Y、W、R 等软元件的读写。数据类型覆盖 BOOL、WORD、INT16、DWORD、INT32、FLOAT、DOUBLE，通过 `IPlcClient.ByteTransform` 进行字节序转换。

**English:**
Mitsubishi MC (Melsec Communication) is a binary communication protocol for Mitsubishi PLCs (FX, Q, L, iQ-R series, etc.), running over TCP/IP (default port 5002). This operator creates an `IPlcClient` via `PlcClientFactory.CreateMitsubishiMc`, using MC protocol 3E/4E frame format for data read/write. MC protocol uses device code + offset addressing (e.g., D100 = Data Register offset 100, M0 = Internal Relay bit 0), supporting D, M, X, Y, W, R device types. Data types cover BOOL, WORD, INT16, DWORD, INT32, FLOAT, DOUBLE, with byte-order conversion via `IPlcClient.ByteTransform`.

## 实现策略 / Implementation Strategy

- **继承 PLC 通信基类**：继承 `PlcCommunicationOperatorBase`，获得静态连接池、心跳巡检、连接复用与自动重连、全局通信配置回退等共享能力。
- **连接键策略**：以 `MC:{IP}:{Port}` 为连接池键，不区分 PLC 系列（MC 协议对三菱各系列通用）。
- **全局配置回退**：`UseGlobalFallback=true` 时，缺失的 IP/Port 从 config.json 全局通信配置的 MC Profile 中回退获取。
- **写入值动态解析**：`ResolveWriteValue` 按优先级从上游输入获取：`JudgmentValue > Value > Data > 静态参数值`。
- **批量读取支持**：通过 `Length` 参数（1-999）支持一次读取多个连续软元件。
- **轮询等待实现**：与 FINS 算子不同，MC 算子完整实现了 PollingMode=WaitForValue 的轮询逻辑，支持六种比较条件（Equal/NotEqual/GreaterThan/LessThan/GreaterOrEqual/LessOrEqual），数值比较容差 0.0001。
- **类声明为 sealed**：`MitsubishiMcCommunicationOperator` 使用 `sealed` 修饰，禁止进一步继承。

## 核心 API 调用链 / Core API Call Chain

1. `GetStringParam` / `GetIntParam` / `GetBoolParam` -- 读取全部 14 个参数
2. `ResolveWriteValue(@operator, inputs)` -- 解析写入值（上游输入优先级链）
3. `ResolveConnectionSettings(operatorIpAddress, operatorPort, "MC", useGlobalFallback)` -- 解析连接配置
4. `GetOrCreateConnectionAsync(connectionKey, factory)` -- 基类连接池管理
   - `PlcClientFactory.CreateMitsubishiMc(ipAddress)` -- 创建 MC 客户端
   - `((MitsubishiMcClient)mcClient).Port = port` -- 设置端口（类型检查后赋值）
   - `client.ConnectAsync()` -- 建立 MC 连接
5. **读取路径**：
   - `PollingMode=None` -> `ExecuteReadAsync(client, address, dataType, length, ct)`
     - `client.ReadAsync(address, length, ct)` -- 读取 PLC 数据
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
| `IpAddress` | `string` | `"192.168.3.39"` | - | 三菱 PLC 的 IP 地址。 |
| `Port` | `int` | `5002` | [1, 65535] | MC 协议标准端口为 5002。 |
| `UseGlobalFallback` | `bool` | `false` | - | 启用后，缺失的 IP/Port 可回退到 config.json 中的全局通信配置。 |
| `Address` | `string` | `"D100"` | - | PLC 软元件地址。格式示例：D100（数据寄存器）、M0（内部继电器）、X0（输入）、Y0（输出）、W0（链接寄存器）、R0（文件寄存器）。 |
| `Length` | `int` | `1` | [1, 999] | 读取的连续软元件数量。写入时忽略此参数。 |
| `DataType` | `enum` | `"Word"` | Bit, Word, Int16, DWord, Int32, Float, Double | 读写数据的类型。注意：不支持 Byte 和 String 类型。 |
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
| `Data` | Data | `Any` | No | 可选输入端口。写入操作时，`ResolveWriteValue` 从此端口的上游输出中按优先级提取写入值。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Response` | Response | `String` | 读取操作返回转换后的值；写入操作返回写入值确认。 |
| `Status` | Status | `Boolean` | 操作是否成功。成功时输出字典额外包含 Value、DataType、Status、Timestamp、ConnectionSource。轮询模式下额外包含 PollingReadCount、PollingElapsedMs、PollingMatched。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) -- 单次 MC 读写请求；轮询模式下为 O(N) 其中 N = timeout/interval。 |
| 典型耗时 (Typical Latency) | 首次连接 10-30ms（MC 协议握手）；复用连接单次读写 1-8ms。批量读取 999 个软元件约 3-15ms。 |
| 内存特征 (Memory Profile) | 每个 MC 连接约 2-4KB。连接池与心跳由基类静态管理。轮询模式下每次读取分配约 256B，读取失败时延迟上限 1000ms。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：
  - 与三菱 FX3U、FX5U、Q 系列、L 系列、iQ-R/iQ-F 系列 PLC 进行数据读写。
  - 读取 D 区工艺数据、M 区状态标志。
  - 轮询等待 PLC 触发信号（如工件到位 M0=ON）后再执行视觉流程。
  - 向 PLC 写入检测结果到指定 D 寄存器。
  - 多流程共享同一 MC 连接时，连接池自动复用。
- **不适合 (Not Suitable)**：
  - 需要读取 STRING 类型数据 -- 数据类型不支持 String。
  - CC-Link IE 通信场景 -- 本算子使用 MC 协议（以太网），非 CC-Link。
  - 非三菱 PLC 设备。

## 已知限制 / Known Limitations
1. **DataType 缺少 Byte 和 String**：与 FINS 算子一致，仅支持 7 种数据类型，不支持 Byte 和 String。
2. **连接键不含 PLC 系列**：以 `MC:{IP}:{Port}` 为键，不区分 PLC 系列。MC 协议通用性使得此设计足够。
3. **ResolveWriteValue 使用 Parameters 直接查找**：与其他 PLC 算子使用 `GetStringParam` 不同，MC 算子的 `ResolveWriteValue` 直接从 `@operator.Parameters` 集合中查找 WriteValue，行为一致但实现路径不同。
4. **MitsubishiMcClient 类型检查**：工厂方法返回 `IPlcClient`，设置端口时需类型转换为 `MitsubishiMcClient`。若工厂实现变更导致返回类型不同，端口设置将被静默跳过。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档：精确提取 MC 协议细节、轮询等待完整实现、批量读取、连接键设计、ResolveWriteValue 实现差异与性能特征 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
