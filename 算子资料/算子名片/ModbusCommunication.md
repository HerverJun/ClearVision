# Modbus通信 / ModbusCommunication

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ModbusCommunicationOperator` |
| 枚举值 (Enum) | `OperatorType.ModbusCommunication` |
| 分类 (Category) | 通信 / Communication |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

**中文：**
Modbus TCP 是工业自动化领域最广泛使用的应用层协议之一，基于客户端/服务器模型运行在 TCP/IP 之上。该算子实现了一个完整的 Modbus TCP 客户端，通过 NModbus 库与 Modbus 从站设备进行通信。核心流程为：根据功能码（Function Code）构建 Modbus PDU，经由已建立的 TCP 连接发送至目标从站（由 SlaveId 标识），解析响应寄存器或线圈数据并转换为字符串输出。支持四种标准功能码：读线圈（FC01）、读保持寄存器（FC03）、写单寄存器（FC06）和写多寄存器（FC16）。

**English:**
Modbus TCP is one of the most widely used application-layer protocols in industrial automation, operating on a client/server model over TCP/IP. This operator implements a full Modbus TCP client using the NModbus library. The core flow is: build a Modbus PDU based on the function code, transmit it over an established TCP connection to the target slave (identified by SlaveId), parse the response register or coil data, and convert it to string output. Four standard function codes are supported: Read Coils (FC01), Read Holding Registers (FC03), Write Single Register (FC06), and Write Multiple Registers (FC16).

## 实现策略 / Implementation Strategy

- **连接池复用**：使用静态 `ConcurrentDictionary<string, TcpClient>` 和 `ConcurrentDictionary<string, IModbusMaster>` 维护连接池，以 `IP:Port` 为键复用 TCP 连接，避免频繁握手开销。最大池容量为 32，空闲连接超过 10 分钟自动清理。
- **引用计数信号量**：采用自研 `RefCountedSemaphore` 实现连接锁（`ConnectionLocks`）和操作锁（`OperationLocks`）两级锁机制，确保并发场景下连接建立与 Modbus 操作的线程安全。
- **存活检测**：通过 `TcpClient.Client.Poll` + `Available` 检测连接存活状态，失效连接在下次操作时自动重建。
- **超时分层**：连接建立超时和操作超时均受 `TimeoutMs` 参数控制，范围 100-60000ms。
- **错误分类处理**：区分 `OperationCanceledException`（取消/超时）、`IOException`（IO 异常）、`SocketException`（网络异常）和 `TimeoutException`，失败时主动清理连接池中的失效连接。

## 核心 API 调用链 / Core API Call Chain

1. `GetStringParam(@operator, "Protocol", "TCP")` / `GetIntParam` / `GetStringParam` -- 读取全部参数
2. `ExecuteTcpModbusAsync(ipAddress, port, slaveId, functionCode, registerAddress, registerCount, writeValue, timeoutMs, cancellationToken)` -- TCP Modbus 主流程
3. `AcquireRefCountedSemaphore(OperationLocks, key)` -- 获取操作级信号量
4. `GetOrCreateConnectionAsync(ipAddress, port, timeoutMs, cancellationToken)` -- 获取或创建连接
   - `CleanupIdleConnections(now)` -- 清理过期空闲连接
   - `IsConnectionAlive(existingClient)` -- 检测连接存活
   - `TcpClient.ConnectAsync(ipAddress, port)` -- 建立 TCP 连接
   - `ModbusFactory.CreateMaster(client)` -- 创建 Modbus 主站实例
   - `TrimConnectionPoolIfNeeded(key)` -- 池满时淘汰最旧连接
5. `ExecuteModbusFunction(master, slaveId, functionCode, registerAddress, registerCount, writeValue)` -- 执行具体功能码
   - `master.ReadCoils` / `master.ReadHoldingRegisters` / `master.WriteSingleRegister` / `master.WriteMultipleRegisters`
6. `OperatorExecutionOutput.Success(Dictionary)` -- 构建成功输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Protocol` | `enum` | `"TCP"` | TCP, RTU | 通信协议类型。当前仅 TCP 模式可用，RTU 模式需额外串口生命周期配置，本算子未打包支持。 |
| `IpAddress` | `string` | `"192.168.1.1"` | - | Modbus 从站设备的 IP 地址。 |
| `Port` | `int` | `502` | [1, 65535] | Modbus TCP 标准端口为 502。 |
| `SlaveId` | `int` | `1` | [1, 247] | Modbus 从站地址，协议规范范围 1-247。 |
| `RegisterAddress` | `int` | `0` | [0, 65535] | 起始寄存器/线圈地址。 |
| `RegisterCount` | `int` | `1` | [1, 125] | 读取或写入的寄存器数量，Modbus PDU 单帧上限 125 个寄存器。 |
| `FunctionCode` | `enum` | `"ReadHolding"` | ReadCoils, ReadHolding, WriteSingle, WriteMultiple | 功能码选择。ReadCoils=FC01读线圈，ReadHolding=FC03读保持寄存器，WriteSingle=FC06写单寄存器，WriteMultiple=FC16写多寄存器。 |
| `WriteValue` | `string` | `""` | - | 写入值。单寄存器写入时为单个 ushort 值；多寄存器写入时为逗号分隔的 ushort 列表（如 `"100,200,300"`）。 |
| `TimeoutMs` | `int` | `5000` | [100, 60000] | 连接建立和操作执行的超时时间（毫秒）。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Data` | Data | `Any` | No | 可选输入端口，用于接收上游算子的输出数据。当前实现中未直接使用输入端口数据驱动 Modbus 操作，保留为扩展接口。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Response` | Response | `String` | 读取操作返回逗号分隔的寄存器/线圈值（如 `"100, 200, 300"`）；写入操作返回写入确认信息。 |
| `Status` | Status | `Boolean` | 操作是否成功。成功时附加 Protocol、FunctionCode、SlaveId 到输出字典。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) -- 单次 Modbus 请求/响应，不涉及循环或大数据处理。 |
| 典型耗时 (Typical Latency) | 首次连接 10-50ms（TCP 三次握手）；后续复用连接 1-10ms（取决于从站响应速度和网络延迟）。写入操作通常比读取稍快。 |
| 内存特征 (Memory Profile) | 静态连接池固定占用：每个连接约 2KB（TcpClient + IModbusMaster），最大 32 个连接共约 64KB。RefCountedSemaphore 每键约 100B。运行时无额外大对象分配。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：
  - 与 Modbus TCP 从站设备（PLC、HMI、变频器、智能仪表等）进行寄存器/线圈读写。
  - 读取传感器数据（温度、压力、流量等工艺参数）。
  - 向 PLC 写入控制指令（启动/停止、设定值下发）。
  - 多算子并发访问同一设备时，连接池自动复用。
- **不适合 (Not Suitable)**：
  - Modbus RTU（串口）通信 -- 本算子已声明 RTU 选项但实际会返回失败。
  - 需要广播或多播 Modbus 帧的场景。
  - 超高频轮询（<10ms 周期） -- 连接池锁竞争可能成为瓶颈。

## 已知限制 / Known Limitations
1. **RTU 模式未实现**：Protocol 参数虽然支持选择 RTU，但实际执行时会直接返回失败，提示需要额外的串口生命周期配置。
2. **单连接键**：连接池以 `IP:Port` 为键，不区分 SlaveId。若同一 IP:Port 下有多个从站，会共享同一 TCP 连接，Modbus 协议层通过 SlaveId 区分，但这可能导致操作锁竞争。
3. **静态连接池生命周期**：连接池和相关锁均为静态字段，生命周期与进程一致。进程退出时无显式连接清理逻辑（依赖 GC/Finalizer）。
4. **WriteMultiple 值解析**：WriteValue 参数要求逗号分隔的 ushort 列表，不支持单次写入超过 123 个寄存器（Modbus 协议限制）。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档：基于源码精确提取连接池策略、RefCountedSemaphore 双级锁机制、四种功能码实现细节、性能特征与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
