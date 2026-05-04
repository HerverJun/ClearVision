# 欧姆龙FINS通信 / OmronFinsCommunication

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `OmronFinsCommunicationOperator` |
| 枚举值 (Enum) | `OperatorType.OmronFinsCommunication` |
| 分类 (Category) | 通信 / Communication |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

**中文：**
FINS（Factory Interface Network Service）是欧姆龙 PLC 的专有通信协议，支持多种传输层（UDP/IP、TCP/IP、串口等）。该算子通过 FINS/TCP 协议（默认端口 9600）与欧姆龙 PLC 通信，使用 `PlcClientFactory.CreateOmronFins` 创建 `IPlcClient` 实例。FINS 协议使用内存区代码 + 偏移地址的方式寻址（如 DM100 表示数据存储区偏移 100），支持对 DM、WR、HR、AR、EM 等数据区的读写操作。数据类型覆盖 BOOL、WORD、INT16、DWORD、INT32、FLOAT、DOUBLE，通过 `IPlcClient.ByteTransform` 进行字节序转换（欧姆龙 PLC 使用大端序）。

**English:**
FINS (Factory Interface Network Service) is Omron PLC's proprietary communication protocol supporting multiple transports (UDP/IP, TCP/IP, serial, etc.). This operator communicates via FINS/TCP (default port 9600) using `PlcClientFactory.CreateOmronFins` to create an `IPlcClient` instance. FINS uses memory area code + offset addressing (e.g., DM100 = Data Memory area offset 100), supporting read/write on DM, WR, HR, AR, EM data areas. Data types cover BOOL, WORD, INT16, DWORD, INT32, FLOAT, DOUBLE, with byte-order conversion via `IPlcClient.ByteTransform` (Omron PLCs use big-endian).

## 实现策略 / Implementation Strategy

- **继承 PLC 通信基类**：继承 `PlcCommunicationOperatorBase`，获得静态连接池、心跳巡检（1s 间隔 Ping）、连接复用与自动重连、全局通信配置回退等共享能力。
- **连接键策略**：以 `FINS:{IP}:{Port}` 为连接池键，不区分 PLC 型号（欧姆龙 FINS 协议对 CP1H/CJ2M/NJ/NX 系列通用）。
- **全局配置回退**：`UseGlobalFallback=true` 时，缺失的 IP/Port 从 config.json 全局通信配置的 FINS Profile 中回退获取。
- **写入值动态解析**：`ResolveWriteValue` 按优先级从上游输入获取：`JudgmentValue > Value > Data > 静态参数值`。
- **批量读取支持**：通过 `Length` 参数（1-999）支持一次读取多个连续寄存器，与 S7 算子的固定单值读取不同。
- **无轮询模式**：与 S7/MC 算子不同，OmronFINS 算子未实现 PollingMode 参数的执行逻辑（参数已声明但未使用）。

## 核心 API 调用链 / Core API Call Chain

1. `GetStringParam` / `GetIntParam` / `GetBoolParam` -- 读取全部参数
2. `ResolveWriteValue(@operator, inputs)` -- 解析写入值（上游输入优先级链）
3. `ResolveConnectionSettings(operatorIpAddress, operatorPort, "FINS", useGlobalFallback)` -- 解析连接配置
4. `GetOrCreateConnectionAsync(connectionKey, factory)` -- 基类连接池管理
   - `PlcClientFactory.CreateOmronFins(ipAddress)` -- 创建 FINS 客户端
   - `((OmronFinsClient)finsClient).Port = port` -- 设置端口
   - `client.ConnectAsync()` -- 建立 FINS/TCP 连接
5. **读取路径**：
   - `ExecuteReadAsync(client, address, dataType, length, ct)`
     - `client.ReadAsync(address, length, ct)` -- 读取 PLC 数据（length 为元素个数）
     - `ConvertBytesToValue(client, result.Content, dataType)` -- 字节转值
6. **写入路径**：
   - `ExecuteWriteAsync(client, address, dataType, writeValue, ct)`
     - `ConvertValueToBytes(client, writeValue, dataType)` -- 值转字节
     - `client.WriteAsync(address, bytes, ct)` -- 写入 PLC 数据
7. `AttachConnectionAuditInfo(output, connectionSource)` -- 附加连接审计信息

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `IpAddress` | `string` | `"192.168.250.1"` | - | 欧姆龙 PLC 的 IP 地址（默认值为欧姆龙 NJ/NX 系列常见地址）。 |
| `Port` | `int` | `9600` | [1, 65535] | FINS/TCP 标准端口为 9600。 |
| `UseGlobalFallback` | `bool` | `false` | - | 启用后，缺失的 IP/Port 可回退到 config.json 中的全局通信配置。 |
| `Address` | `string` | `"DM100"` | - | PLC 数据区地址。格式示例：DM100（数据存储区偏移 100）、WR0（内部继电器区）、HR0（保持继电器区）。 |
| `Length` | `int` | `1` | [1, 999] | 读取的连续寄存器数量。写入时忽略此参数。 |
| `DataType` | `enum` | `"Word"` | Bit, Word, Int16, DWord, Int32, Float, Double | 读写数据的类型。注意：不支持 Byte 和 String 类型。 |
| `Operation` | `enum` | `"Read"` | Read, Write | 操作类型：读取或写入。 |
| `WriteValue` | `string` | `""` | - | 写入值。支持从上游输入动态获取（优先级：JudgmentValue > Value > Data）。 |
| `PollingMode` | `enum` | `"None"` | None, WaitForValue | 轮询模式参数已声明但当前执行逻辑未使用。 |
| `PollingCondition` | `enum` | `"Equal"` | Equal, NotEqual, GreaterThan, LessThan, GreaterOrEqual, LessOrEqual | 轮询条件参数已声明但当前执行逻辑未使用。 |
| `PollingValue` | `string` | `"1"` | - | 轮询目标值参数已声明但当前执行逻辑未使用。 |
| `PollingTimeout` | `int` | `30000` | [100, 300000] | 轮询超时参数已声明但当前执行逻辑未使用。 |
| `PollingInterval` | `int` | `50` | [10, 5000] | 轮询间隔参数已声明但当前执行逻辑未使用。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Data` | 数据 | `Any` | No | 可选输入端口。写入操作时，`ResolveWriteValue` 从此端口的上游输出中按优先级提取写入值。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Response` | 响应 | `String` | 读取操作返回转换后的值；写入操作返回写入值确认。 |
| `Status` | 状态 | `Boolean` | 操作是否成功。成功时输出字典额外包含 Value、DataType、Status、Timestamp、ConnectionSource。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) -- 单次 FINS 读写请求，批量读取由协议层一次完成。 |
| 典型耗时 (Typical Latency) | 首次连接 10-40ms（FINS/TCP 握手 + 节点地址分配）；复用连接单次读写 1-10ms。批量读取 999 个寄存器约 5-20ms。 |
| 内存特征 (Memory Profile) | 每个 FINS 连接约 2-4KB。连接池与心跳由基类静态管理。批量读取时 Content 缓冲区随 Length 线性增长（999 Word ≈ 2KB）。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：
  - 与欧姆龙 CP1H、CJ2M、CJ2H、NJ、NX 系列 PLC 进行数据读写。
  - 批量读取 DM 区工艺数据（如一次读取 100 个寄存器）。
  - 向 PLC 写入视觉检测结果。
  - 多流程共享同一 FINS 连接时，连接池自动复用。
- **不适合 (Not Suitable)**：
  - 需要轮询等待功能的场景 -- PollingMode 参数已声明但未实现。
  - 需要读取 STRING 类型数据 -- 数据类型不支持 String。
  - FINS/UDP 通信场景 -- 本算子仅支持 FINS/TCP。
  - 非欧姆龙 PLC 设备。

## 已知限制 / Known Limitations
1. **PollingMode 未实现**：虽然元数据中声明了 PollingMode、PollingCondition、PollingValue、PollingTimeout、PollingInterval 五个参数，但 `ExecuteCoreAsync` 中未使用这些参数。读取操作始终为单次读取，不支持轮询等待。
2. **DataType 缺少 Byte 和 String**：相比 S7 算子支持 9 种数据类型，FINS 算子仅支持 7 种（无 Byte、String），与 PLC 数据类型定义相关。
3. **连接键不含 PLC 型号**：以 `FINS:{IP}:{Port}` 为键，不区分 PLC 型号。FINS 协议通用性使得此设计足够，但无法为不同型号配置不同参数。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档：精确提取 FINS/TCP 协议细节、批量读取能力、PollingMode 未实现的明确定性、连接键设计与数据类型覆盖 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
