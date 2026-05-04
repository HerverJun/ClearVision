# HTTP请求 / HttpRequest

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `HttpRequestOperator` |
| 枚举值 (Enum) | `OperatorType.HttpRequest` |
| 分类 (Category) | 通信 / Communication |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

**中文：**
HTTP（HyperText Transfer Protocol）是 Web 服务和工业互联网中最常用的应用层协议。该算子实现了一个通用的 HTTP 客户端，支持调用外部 REST API 触发 MES 系统、AGV 调度、数据上报等业务流程。核心流程为：构建 HTTP 请求（方法、URL、Headers、Body）-> 发送请求 -> 解析响应（状态码、响应体）-> 输出结果。使用静态 `HttpClient` 实例复用底层 TCP 连接（遵循 .NET 最佳实践）。支持 GET/POST/PUT/DELETE 四种 HTTP 方法，内置重试机制（仅对幂等方法 GET/HEAD/OPTIONS 启用自动重试）。

**English:**
HTTP (HyperText Transfer Protocol) is the most commonly used application-layer protocol in web services and Industrial IoT. This operator implements a generic HTTP client for calling external REST APIs to trigger MES systems, AGV scheduling, data reporting, and other business flows. The core flow is: build HTTP request (method, URL, headers, body) -> send request -> parse response (status code, body) -> output result. Uses a static `HttpClient` instance to reuse underlying TCP connections (.NET best practice). Supports GET/POST/PUT/DELETE methods with built-in retry (automatic retry only for idempotent methods GET/HEAD/OPTIONS).

## 实现策略 / Implementation Strategy

- **静态 HttpClient**：使用 `private static readonly HttpClient _httpClient = new()` 全局共享一个 HttpClient 实例，避免端口耗尽问题（.NET HttpClient 最佳实践）。无自定义超时配置，使用 HttpClient 默认的 100 秒超时。
- **超时控制**：通过 `CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs))` 实现请求级超时，与 CancellationToken 链接确保可取消。
- **重试策略**：`maxAttempts = IsAutomaticRetryAllowed(method) ? retryCount : 0`。仅 GET/HEAD/OPTIONS 允许自动重试（幂等方法）。POST/PUT/DELETE 即使配置了 RetryCount 也不会自动重试，防止非幂等操作重复执行。
- **重试延迟**：重试间隔由 `RetryDelayMs` 参数控制，使用 `Task.Delay` 实现。所有异常（超时、HTTP 错误、网络异常）均触发重试。
- **请求体构建**：优先从 `Body` 输入端口获取；若无 Body 输入但有其他输入，将所有输入序列化为 JSON；无输入时 Body 为 null。
- **请求头合并**：从 `Headers` 输入端口获取自定义 Headers（需为 `Dictionary<string, object>` 类型），自动添加 `Content-Type` 默认值。
- **Content-Type 处理**：Content-Type 作为 Header 的一部分传递，同时用作 `StringContent` 的编码类型。支持覆盖默认 Content-Type。

## 核心 API 调用链 / Core API Call Chain

1. `GetStringParam(@operator, "Url", "")` / `GetStringParam` / `GetIntParam` -- 读取全部 6 个参数
2. `IsAutomaticRetryAllowed(normalizedMethod)` -- 判断是否允许自动重试（GET/HEAD/OPTIONS=true）
3. **请求体构建**：
   - `inputs.TryGetValue("Body", out bodyObj)` -- 优先从 Body 输入端口获取
   - `JsonSerializer.Serialize(inputs)` -- 回退：将所有输入序列化为 JSON
4. **请求头构建**：
   - `inputs.TryGetValue("Headers", out headersObj)` -- 从 Headers 输入端口获取自定义 Headers
   - 添加默认 `Content-Type: application/json`
5. **重试循环** (`for attempt = 0; attempt <= maxAttempts`):
   - `new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs))` -- 创建超时 Token
   - `CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken)` -- 链接取消 Token
   - `ExecuteRequestAsync(url, method, body, headers, linkedCts.Token)` -- 执行请求
     - `new HttpRequestMessage(new HttpMethod(method), url)` -- 构建请求消息
     - `request.Content = new StringContent(body, Encoding.UTF8, contentType)` -- 设置请求体
     - `request.Headers.TryAddWithoutValidation(key, value)` -- 添加自定义 Headers
     - `_httpClient.SendAsync(request, cancellationToken)` -- 发送请求
     - `response.Content.ReadAsStringAsync(cancellationToken)` -- 读取响应
   - 失败时 `Task.Delay(retryDelayMs, cancellationToken)` -- 等待后重试
6. `OperatorExecutionOutput.Success(outputData)` -- 构建成功输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Url` | `string` | `"http://localhost:5000/api"` | - | 目标 API 的完整 URL 地址。必须为非空有效 URL。 |
| `Method` | `enum` | `"POST"` | GET, POST, PUT, DELETE | HTTP 请求方法。支持 PATCH（验证层允许但 UI 未列出）。 |
| `TimeoutMs` | `int` | `10000` | [1000, 60000] | 单次 HTTP 请求的超时时间（毫秒）。注意：这与 HttpClient 自身的默认 100s 超时独立，通过 CancellationToken 实现。 |
| `RetryCount` | `int` | `0` | [0, 5] | 最大重试次数。仅对幂等方法（GET/HEAD/OPTIONS）生效，POST/PUT/DELETE 不会自动重试。 |
| `ContentType` | `string` | `"application/json"` | - | 请求体的 Content-Type 头。可通过 Headers 输入端口覆盖。 |
| `RetryDelayMs` | `int` | `1000` | [0, 10000] | 每次重试之间的等待时间（毫秒）。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Body` | 请求体 | `String` | No | HTTP 请求体内容。优先级最高，存在时直接作为请求体发送。 |
| `Headers` | 请求头 | `Any` | No | 自定义请求头，需为 `Dictionary<string, object>` 类型。每个键值对添加为 HTTP Header。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Response` | 响应内容 | `String` | HTTP 响应体的原始字符串内容。 |
| `StatusCode` | 状态码 | `Integer` | HTTP 状态码（如 200、404、500）。 |
| `IsSuccess` | 是否成功 | `Boolean` | 是否为 2xx 成功状态码。输出字典额外包含 IsSuccessStatusCode 和 ResponseBody（与 Response 相同）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) -- 单次 HTTP 请求/响应，不涉及循环或大数据处理。序列化输入为 JSON 的开销可忽略。 |
| 典型耗时 (Typical Latency) | 取决于目标服务响应速度。局域网内 API 调用 5-50ms；公网 API 50-500ms。重试场景下总耗时 = 单次耗时 * (1 + retryCount) + retryDelayMs * retryCount。 |
| 内存特征 (Memory Profile) | 静态 HttpClient 常驻约 1KB。每次请求分配约 2-8KB（HttpRequestMessage + StringContent + 响应缓冲区）。大响应体可能导致 Gen2 GC 压力。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：
  - 调用 MES API 上报检测结果（OK/NG 判定、缺陷类型、坐标数据）。
  - 触发 AGV 搬运指令（发送 HTTP POST 到 AGV 调度系统）。
  - 查询外部系统数据（GET 请求获取工艺参数、配方数据）。
  - 调用 Webhook 通知（POST JSON 到钉钉/企业微信机器人）。
  - 与 RESTful 微服务架构集成。
- **不适合 (Not Suitable)**：
  - 高频（>100Hz）HTTP 调用 -- 每次构建 HttpRequestMessage 有开销，且无连接池优化。
  - 大文件上传/下载 -- 无流式传输支持，响应体全部加载到内存。
  - HTTPS 客户端证书认证 -- 未配置自定义 HttpClientHandler。
  - WebSocket 通信 -- 仅支持 HTTP 请求/响应模式。
  - 需要 Cookie/Session 管理的场景 -- 无 Cookie 容器。

## 已知限制 / Known Limitations
1. **POST/PUT/DELETE 不自动重试**：`IsAutomaticRetryAllowed` 仅对 GET/HEAD/OPTIONS 返回 true。即使配置了 RetryCount>0，POST 等方法也不会重试。这是安全设计但可能不符合某些幂等 POST 场景的需求。
2. **静态 HttpClient 无自定义配置**：使用默认 HttpClient，无法配置代理、自定义证书验证、连接池大小等。如需定制需修改源码。
3. **Headers 输入端口类型要求**：Headers 输入必须为 `Dictionary<string, object>` 类型。若上游输出为其他类型（如 JSON 字符串），Headers 解析会静默跳过。
4. **响应体全部加载内存**：`ReadAsStringAsync` 将整个响应体加载到字符串。大响应（>10MB）可能导致内存压力。
5. **无重试退避策略**：重试间隔固定为 RetryDelayMs，无指数退避。高并发重试可能对目标服务造成压力。
6. **ContentType 双重设置**：ContentType 既作为 StringContent 的编码参数，又可能被 Headers 中的 Content-Type 覆盖。两者同时存在时以 Headers 为准（因为 Headers 后设置到 request.Content）。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 重写为金标准文档：精确提取静态 HttpClient 策略、幂等重试限制、请求体/请求头构建逻辑、超时控制机制与性能特征 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
