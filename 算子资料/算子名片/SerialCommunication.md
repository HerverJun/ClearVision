# 串口通信 / SerialCommunication

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SerialCommunicationOperator` |
| 枚举值 (Enum) | `OperatorType.SerialCommunication` |
| 分类 (Category) | 通信 / Communication |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

**中文：**
串口通信（RS-232/RS-485）是工业设备最基础的点对点或总线式通信方式。该算子通过 `System.IO.Ports.SerialPort` 封装了完整的串口数据收发流程：配置串口参数（波特率、数据位、停止位、校验位）、打开串口、发送编码后的字节数据、等待设备响应（100ms 固定延迟）、读取响应字节并解码。支持三种编码模式：UTF-8（默认）、ASCII 和 HEX（十六进制原始字节）。HEX 模式下，发送数据为十六进制字符串（如 `"01 03 00 00 00 01 84 0A"`），自动转换为字节数组；接收时将字节数组转回十六进制字符串显示。

**English:**
Serial communication (RS-232/RS-485) is the most fundamental point-to-point or bus-based communication method for industrial devices. This operator wraps the complete serial data send/receive flow via `System.IO.Ports.SerialPort`: configure port parameters (baud rate, data bits, stop bits, parity), open port, send encoded bytes, wait for device response (100ms fixed delay), read and decode response bytes. Three encoding modes are supported: UTF-8 (default), ASCII, and HEX (raw hexadecimal bytes). In HEX mode, send data is a hex string (e.g., `"01 03 00 00 00 01 84 0A"`) auto-converted to bytes; receive data converts bytes back to hex string display.

## 实现策略 / Implementation Strategy

- **无连接池**：每次执行都创建新的 `SerialPort` 实例，使用 `using` 语句确保串口在操作完成后自动关闭释放。这是因为串口资源独占性强，不适合跨算子实例共享。
- **同步执行**：`ExecuteCoreAsync` 实际上是同步操作（返回 `Task.FromResult`），串口读写使用阻塞调用。这是因为 `System.IO.Ports.SerialPort` 的 API 不支持 async/await。
- **固定响应延迟**：发送后固定等待 100ms（`Thread.Sleep(100)`）再读取响应，适用于大多数串口设备的响应时间。若设备响应较慢，可能丢失数据。
- **HEX 编码支持**：HEX 模式下自动处理空格和连字符分隔符（`"01 03"` 或 `"01-03"` 均可），验证十六进制字符串长度为偶数。
- **异常分类处理**：区分 `UnauthorizedAccessException`（串口被占用）、`IOException`（IO 错误）、`TimeoutException`（操作超时）和通用异常，提供明确的中文错误提示。

## 核心 API 调用链 / Core API Call Chain

1. `GetStringParam(@operator, "PortName", "COM1")` / `GetStringParam` / `GetIntParam` -- 读取全部 8 个参数
2. `Enum.TryParse<StopBits>(stopBitsStr)` -- 解析停止位枚举
3. `Enum.TryParse<Parity>(parityStr)` -- 解析校验位枚举
4. `new SerialPort(portName, baudRate, parity, dataBits, stopBits)` -- 创建串口实例
   - 设置 `ReadTimeout = timeoutMs`, `WriteTimeout = timeoutMs`
5. `port.Open()` -- 打开串口
6. **发送路径**（当 SendData 非空）：
   - HEX 模式：`sendData.Replace(" ", "").Replace("-", "")` -> 逐字节 `Convert.ToByte(hex, 16)`
   - 文本模式：`textEncoding.GetBytes(sendData)` -- UTF-8 或 ASCII 编码
   - `port.Write(bytes, 0, bytes.Length)` -- 发送字节
7. `Thread.Sleep(100)` -- 等待设备响应
8. **接收路径**（当 `port.BytesToRead > 0`）：
   - `port.Read(buffer, 0, buffer.Length)` -- 读取响应字节
   - HEX 模式：`BitConverter.ToString(buffer, 0, bytesRead).Replace("-", " ")`
   - 文本模式：`textEncoding.GetString(buffer, 0, bytesRead)`
9. `OperatorExecutionOutput.Success(output)` -- 构建成功输出（含 Response、BytesReceived、Port、BaudRate、Success）

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `PortName` | `string` | `"COM1"` | - | 串口名称。Windows 下为 COM1-COMn，Linux 下为 /dev/ttyUSB0 等。 |
| `BaudRate` | `enum` | `"9600"` | 9600, 19200, 38400, 57600, 115200 | 通信波特率。常见值：9600（低速设备）、115200（高速设备）。 |
| `DataBits` | `int` | `8` | [5, 8] | 每个字节的数据位数。8 位为最常用配置。 |
| `StopBits` | `enum` | `"One"` | One (1), OnePointFive (1.5), Two (2) | 停止位数量。大多数设备使用 1 位停止位。 |
| `Parity` | `enum` | `"None"` | None, Odd, Even | 校验方式。None=无校验，Odd=奇校验，Even=偶校验。 |
| `SendData` | `string` | `""` | - | 发送内容。HEX 模式下为十六进制字符串（如 `"01 03 00 00 00 01"`），文本模式下为普通字符串。 |
| `Encoding` | `enum` | `"UTF8"` | UTF8, ASCII, HEX | 编码模式。UTF-8/ASCII 为文本模式，HEX 为十六进制原始字节模式。 |
| `TimeoutMs` | `int` | `3000` | [100, 30000] | 串口读写操作的超时时间（毫秒），同时应用于 ReadTimeout 和 WriteTimeout。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Data` | 发送数据 | `Any` | No | 可选输入端口。当前实现中未从输入端口读取数据驱动发送，发送内容完全由 SendData 参数决定。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Response` | 接收数据 | `Any` | 接收到的响应数据。HEX 模式下为十六进制字符串（如 `"01 03 02 00 64"`），文本模式下为解码后的字符串。无数据时为空字符串。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) -- N 为发送/接收字节数。HEX 编码解码均为线性遍历。 |
| 典型耗时 (Typical Latency) | 串口打开 <1ms；发送 1-10ms（取决于数据量和波特率）；固定等待 100ms；接收读取 1-10ms。总耗时约 102-120ms（不含设备处理时间）。 |
| 内存特征 (Memory Profile) | 每次执行分配约 1-4KB（SerialPort 对象 + 缓冲区）。串口关闭后立即释放。无静态缓存或连接池。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：
  - 与 RS-232/RS-485 串口设备（扫码枪、称重仪表、温控器、PLC 编程口等）通信。
  - 发送 HEX 指令帧并接收响应（如 Modbus RTU 帧的手动构造）。
  - 与不支持以太网的老式工业设备通信。
  - 低频（<10Hz）串口数据收发场景。
- **不适合 (Not Suitable)**：
  - 高频轮询场景 -- 每次执行都打开/关闭串口，无连接复用。
  - 需要异步串口通信的场景 -- 当前为同步阻塞实现。
  - 多设备共享同一串口 -- 串口资源独占。
  - 需要流控（RTS/CTS、XON/XOFF）的场景 -- 未配置流控参数。

## 已知限制 / Known Limitations
1. **无连接池/连接复用**：每次执行都新建 `SerialPort` 并在执行后关闭。高频调用会导致串口频繁开关，增加延迟和串口资源争用风险。
2. **固定 100ms 响应延迟**：发送后 `Thread.Sleep(100)` 是硬编码值，对响应快的设备浪费时间，对响应慢的设备可能不够。无法通过参数调整。
3. **同步阻塞实现**：`ExecuteCoreAsync` 返回 `Task.FromResult`，实际为同步操作。在异步流程引擎中，串口操作会阻塞线程池线程。
4. **输入端口未使用**：`Data` 输入端口已声明但 `ExecuteCoreAsync` 中未读取输入数据，发送内容完全由 `SendData` 参数决定。
5. **无流控配置**：未暴露 RTS/CTS、DTR/DSR 等硬件流控和 XON/XOFF 软件流控参数。
6. **Thread.Sleep 阻塞**：使用 `Thread.Sleep(100)` 而非 `Task.Delay(100)`，在异步上下文中会阻塞当前线程。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档：精确提取串口收发流程、HEX 编码实现、同步阻塞特性、固定响应延迟、异常分类处理与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
