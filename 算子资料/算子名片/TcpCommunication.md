# TCP通信 / TcpCommunication

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `TcpCommunicationOperator` |
| 枚举值 (Enum) | `OperatorType.TcpCommunication` |
| 分类 (Category) | 通信 / Communication |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

**中文：**
TCP/IP 是互联网和工业网络中最基础的传输层协议。该算子实现了一个通用的 TCP 客户端，支持与任意 TCP 服务器进行数据收发。核心流程为：从连接池获取或建立 TCP 连接 -> 编码发送数据 -> 接收响应 -> 解码输出。当前仅实现了客户端模式（Client），服务器模式（Server）已声明但未实现。连接池以 `IP:Port` 为键，使用 `ConcurrentDictionary` + `RefCountedSemaphore` 实现线程安全的连接复用和请求/响应串行化。支持三种编码：UTF-8、ASCII 和 GBK（中文编码）。

**English:**
TCP/IP is the most fundamental transport-layer protocol in industrial and internet networks. This operator implements a generic TCP client for data exchange with any TCP server. The core flow is: acquire or create TCP connection from pool -> encode and send data -> receive response -> decode and output. Only Client mode is implemented; Server mode is declared but not functional. The connection pool uses `IP:Port` as key with `ConcurrentDictionary` + `RefCountedSemaphore` for thread-safe connection reuse and request/response serialization. Three encodings are supported: UTF-8, ASCII, and GBK (Chinese encoding).

## 实现策略 / Implementation Strategy

- **连接池复用**：使用静态 `ConcurrentDictionary<string, TcpClient>` 和 `ConcurrentDictionary<string, NetworkStream>` 维护连接池，以 `IP:Port` 为键复用 TCP 连接和网络流。
- **两级锁机制**：
  - `ConnectionLocks`：保护连接建立过程，确保同一 `IP:Port` 不会并发创建多个连接。
  - `RequestResponseLocks`：串行化同一连接上的请求/响应，避免多算子并发读写导致数据交错。
- **RefCountedSemaphore**：自研引用计数信号量，支持并发获取和安全移除。当引用计数归零时自动从字典中移除并释放 Semaphore。
- **存活检测**：通过 `TcpClient.Client.Poll(1, SelectMode.SelectRead) && Available == 0` 检测连接是否被远端关闭，失效连接自动清理并重建。
- **服务器模式占位**：`Mode=Server` 时直接返回错误提示，需单独启动监听服务。
- **编码支持**：通过 `System.Text.Encoding` 支持 UTF-8（默认）、ASCII 和 GBK（`Encoding.GetEncoding("GBK")`），适配中文设备协议。

## 核心 API 调用链 / Core API Call Chain

1. `GetStringParam` / `GetIntParam` -- 读取全部 6 个参数
2. `inputs.TryGetValue("Data", out data)` -- 获取可选输入数据
3. `ExecuteClientModeAsync(ipAddress, port, sendData, timeout, encoding, cancellationToken)` -- 客户端模式主流程
4. `AcquireRefCountedSemaphore(RequestResponseLocks, key)` -- 获取请求级信号量（串行化请求/响应）
5. `GetOrCreateConnectionAsync(ipAddress, port, timeout, ct)` -- 获取或创建连接
   - `AcquireRefCountedSemaphore(ConnectionLocks, key)` -- 获取连接级信号量
   - `IsConnectionAlive(existingClient)` -- 检测现有连接存活
   - `InvalidateConnection(key)` -- 清理失效连接（关闭 stream 和 client）
   - `new TcpClient()` + `client.ConnectAsync(ipAddress, port)` -- 建立新连接
   - `client.GetStream()` -- 获取网络流
6. `encoding.GetBytes(sendData)` -- 编码发送数据
7. `stream.WriteAsync(sendBytes)` + `stream.FlushAsync()` -- 发送数据
8. `stream.ReadAsync(buffer)` -- 接收响应（4096 字节缓冲区）
9. `encoding.GetString(buffer, 0, bytesRead)` -- 解码响应
10. `OperatorExecutionOutput.Success(output)` -- 构建成功输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Mode` | `enum` | `"Client"` | Client, Server | 工作模式。当前仅 Client 模式可用，Server 模式返回错误提示。 |
| `IpAddress` | `string` | `"127.0.0.1"` | - | 目标服务器的 IP 地址。 |
| `Port` | `int` | `8080` | [1, 65535] | 目标服务器的端口号。 |
| `SendData` | `string` | `""` | - | 发送的数据内容，按 Encoding 参数编码后发送。 |
| `Timeout` | `int` | `5000` | [100, 30000] | 连接建立和数据收发的超时时间（毫秒）。 |
| `Encoding` | `enum` | `"UTF8"` | UTF8, ASCII, GBK | 数据编码方式。GBK 支持中文字符编码。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Data` | 数据 | `Any` | No | 可选输入端口。当前实现中从输入读取 Data 字段但未用于发送，发送内容完全由 SendData 参数决定。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Response` | 响应 | `String` | 接收到的服务器响应数据，按 Encoding 参数解码后的字符串。 |
| `Status` | 状态 | `Boolean` | 操作是否成功。成功时输出字典额外包含 Mode、IpAddress、Port。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) -- N 为发送/接收字节数。编码解码为线性遍历。 |
| 典型耗时 (Typical Latency) | 首次连接 5-30ms（TCP 三次握手）；复用连接发送+接收 1-10ms（取决于数据量和网络延迟）。4096 字节缓冲区限制单次接收量。 |
| 内存特征 (Memory Profile) | 每个连接约 1-2KB（TcpClient + NetworkStream）。连接池静态常驻。每次请求分配 4096B 接收缓冲区 + 编码缓冲区。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：
  - 与自定义 TCP 服务器进行数据收发（如 MES、SCADA、自定义上位机协议）。
  - 与支持 TCP 通信的工业设备（扫码枪、读卡器、称重终端等）交互。
  - GBK 编码的中文协议设备通信。
  - 多算子共享同一 TCP 连接时，请求/响应自动串行化。
- **不适合 (Not Suitable)**：
  - 需要 TCP 服务器监听的场景 -- Server 模式未实现。
  - 二进制协议通信 -- 当前以字符串编码/解码为主，非原始字节流。
  - 超长响应（>4096 字节）-- 接收缓冲区固定 4096 字节，超出部分会丢失。
  - 需要自定义协议帧头/帧尾解析的场景 -- 无帧解析逻辑。

## 已知限制 / Known Limitations
1. **服务器模式未实现**：`Mode=Server` 时直接返回错误消息 "服务器模式需要单独启动监听，当前版本仅支持客户端模式"。
2. **固定 4096 字节接收缓冲区**：单次 `ReadAsync` 最多读取 4096 字节。若服务器响应超过此长度，只会读取前 4096 字节，剩余数据留在流中等待下次读取。
3. **输入端口 Data 未用于发送**：虽然从输入读取了 `Data` 字段，但 `sendData` 变量仅从 `SendData` 参数获取，输入端口数据未实际参与发送。
4. **无连接超时清理**：与 Modbus 算子不同，TCP 算子没有空闲连接超时清理机制。连接一旦建立就保持到进程退出或连接失效。
5. **请求/响应锁粒度**：`RequestResponseLocks` 以 `IP:Port` 为键，同一目标的所有请求串行化。高并发场景下可能成为瓶颈。
6. **无心跳检测**：与 PLC 基类不同，TCP 算子无后台心跳巡检，连接断开只能在下次请求时发现。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档：精确提取连接池策略、两级锁机制、RefCountedSemaphore 实现、接收缓冲区限制、服务器模式占位与性能特征 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
