# MQTT发布 / MqttPublish

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MqttPublishOperator` |
| 枚举值 (Enum) | `OperatorType.MqttPublish` |
| 分类 (Category) | 通信 / Communication |
| 成熟度 (Maturity) | 预览 Preview（MQTT 客户端未集成） |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

**中文：**
MQTT（Message Queuing Telemetry Transport）是物联网领域最广泛使用的轻量级发布/订阅消息协议。该算子设计用于向 MQTT Broker 发布消息，实现视觉检测结果向数字孪生平台、IoT 网关或其他订阅者的推送。消息发布遵循 MQTT 3.1.1/5.0 规范，支持三个 QoS 级别：QoS 0（最多一次）、QoS 1（至少一次）、QoS 2（恰好一次），以及保留消息（Retain）标志。当前实现为框架占位版本，消息体构建逻辑已完整实现（支持 Payload 输入端口优先、Message 输入端口回退、全输入 JSON 序列化兜底），但实际 MQTT 发布功能依赖外部 MQTT 客户端库（如 MQTTnet）集成。

**English:**
MQTT (Message Queuing Telemetry Transport) is the most widely used lightweight publish/subscribe messaging protocol in IoT. This operator is designed to publish messages to an MQTT Broker, pushing visual inspection results to digital twin platforms, IoT gateways, or other subscribers. Message publishing follows MQTT 3.1.1/5.0 specification, supporting three QoS levels: QoS 0 (at most once), QoS 1 (at least once), QoS 2 (exactly once), and the Retain flag. The current implementation is a framework placeholder -- message body construction is fully implemented (Payload port priority -> Message port fallback -> all-input JSON serialization fallback), but actual MQTT publishing depends on external MQTT client library integration (e.g., MQTTnet).

## 实现策略 / Implementation Strategy

- **框架占位设计**：当前实现仅完成参数验证和消息体构建，实际 MQTT 发布未执行。`ExecuteCoreAsync` 在构建消息体后直接返回失败，提示 "MQTT 发布功能在当前构建中未启用"。
- **消息体优先级链**：消息内容按以下优先级确定：
  1. `Payload` 输入端口 -- 优先级最高，支持 string 直接使用或 object 自动 JSON 序列化。
  2. `Message` 输入端口 -- 回退选项，使用 `ToString()` 转换。
  3. 全输入 JSON 序列化 -- 当上述端口均无数据时，将整个 `inputs` 字典序列化为 JSON。
  4. 空对象 `"{}"` -- 无任何输入时的兜底。
- **QoS 参数处理**：通过自定义 `GetQosParam` 方法（非基类标准方法）处理 QoS 参数，使用 `Math.Clamp(0, 2)` 确保值在合法范围内。支持大小写不敏感匹配（Qos/QoS）。
- **输入端口大小写不敏感查找**：`TryGetInputValue` 方法先精确匹配键名，再通过 `StringComparison.OrdinalIgnoreCase` 模糊匹配，增强输入端口的容错性。

## 核心 API 调用链 / Core API Call Chain

1. `GetStringParam(@operator, "Broker", "localhost")` / `GetIntParam` / `GetBoolParam` / `GetQosParam` -- 读取全部 6 个参数
2. `string.IsNullOrWhiteSpace(topic)` -- 验证 Topic 非空
3. **消息体构建**：
   - `TryGetInputValue(inputs, "Payload", out payloadObj)` -- 尝试从 Payload 端口获取
     - `payloadObj is string ? payloadText : JsonSerializer.Serialize(payloadObj)` -- string 直接用，object 序列化
   - `TryGetInputValue(inputs, "Message", out msgObj)` -- 回退到 Message 端口
     - `msgObj.ToString()` -- 转换为字符串
   - `JsonSerializer.Serialize(inputs)` -- 兜底：序列化全部输入
4. `Logger.LogWarning(...)` -- 记录 MQTT 发布请求日志（当前构建未启用）
5. `OperatorExecutionOutput.Failure("MQTT 发布功能在当前构建中未启用...")` -- 返回失败

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Broker` | `string` | `"localhost"` | - | MQTT Broker 的地址（IP 或域名）。 |
| `Port` | `int` | `1883` | [1, 65535] | MQTT Broker 端口。标准端口：1883（非加密）、8883（TLS）。 |
| `Topic` | `string` | `"cv/results"` | - | MQTT 消息主题。支持层级分隔符（如 `factory/line1/vision/results`）。必须非空。 |
| `Qos` | `int` | `1` | [0, 2] | 服务质量级别。0=最多一次（最快），1=至少一次（推荐），2=恰好一次（最慢但最可靠）。 |
| `Retain` | `bool` | `false` | - | 是否保留消息。设为 true 时 Broker 会保存最后一条消息，新订阅者连接时立即收到。 |
| `TimeoutMs` | `int` | `5000` | [1000, 30000] | MQTT 发布操作的超时时间（毫秒）。当前未实际使用（框架占位）。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Payload` | 消息负载 | `Any` | Yes | 主要消息输入端口。string 类型直接作为消息体，object 类型自动 JSON 序列化。 |
| `Message` | 消息内容 | `String` | No | 备用消息输入端口。当 Payload 端口无数据时回退使用。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `IsSuccess` | 是否成功 | `Boolean` | 发布是否成功。当前始终返回 false（MQTT 客户端未集成）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) -- N 为消息体长度。JSON 序列化开销与输入数据量成正比。 |
| 典型耗时 (Typical Latency) | 当前框架占位版本 <1ms（仅参数验证和消息构建）。集成 MQTT 客户端后预计：QoS 0 约 1-5ms，QoS 1 约 5-20ms（含 ACK），QoS 2 约 10-50ms（含四次握手）。 |
| 内存特征 (Memory Profile) | 每次执行分配约 1-4KB（消息体字符串 + JSON 序列化缓冲区）。无连接池或静态缓存。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：
  - 向数字孪生平台推送视觉检测状态（OK/NG、缺陷类型、坐标）。
  - 向 IoT 网关上报设备运行数据（节拍时间、良品率）。
  - 触发 MQTT 订阅者的下游流程（如报警、统计）。
  - 与 MQTT Broker（Mosquitto、EMQX、AWS IoT Core 等）集成。
- **不适合 (Not Suitable)**：
  - 当前构建版本 -- MQTT 客户端未集成，所有发布请求均返回失败。
  - 需要 MQTT 订阅（Subscribe）功能的场景 -- 本算子仅支持发布。
  - 需要 MQTT 5.0 特性（用户属性、共享订阅等）的场景 -- 未实现。
  - 超大消息（>256MB）-- MQTT 协议限制。

## 已知限制 / Known Limitations
1. **MQTT 客户端未集成**：当前实现为框架占位，`ExecuteCoreAsync` 在消息体构建完成后直接返回失败。需接入 MQTTnet 等库后才能实际发布消息。
2. **QoS 和 Retain 参数未使用**：虽然参数已声明且通过验证，但在当前实现中未传递给任何 MQTT 客户端。
3. **TimeoutMs 参数未使用**：超时参数已声明但当前无 MQTT 操作可超时。
4. **无连接管理**：无 MQTT 连接池、自动重连或会话持久化机制。集成时需设计连接生命周期管理。
5. **单向发布**：仅支持发布（Publish），不支持订阅（Subscribe）、请求/响应模式或共享订阅。
6. **Message 输入端口 DataType 为 String**：与 Payload（Any）不同，Message 端口限定为 String 类型。非 string 输入需通过 Payload 端口传递。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档：精确提取消息体构建优先级链、QoS 参数处理、框架占位状态、输入端口大小写不敏感查找与集成依赖说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
