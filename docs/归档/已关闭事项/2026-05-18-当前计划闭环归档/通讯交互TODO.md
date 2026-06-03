# ClearVision Station 与 Studio 通讯落地 TODO

## 核心结论

这个方案**完全可行**，而且不是“从零做一套分布式系统”，而是在你现有工程上补一层很薄但很关键的“边缘同步层”。我先按你的要求复核了 `HerverJun/ClearVision` 在 GitHub 上的当前主仓库：它已经明确分成 Desktop、Runtime、Runtime.Abstractions、Station 等子工程；`RuntimeHost` 已经对外暴露 `SnapshotChanged` 与 `ResultAvailable` 两个运行时事件；`RuntimeNormalizedResult`、`RuntimeHostSnapshot`、`StationLocalSettings` 也都已经落在共享抽象层里；与此同时，Desktop 端已经是 WinForms + WebView2 + 内嵌 ASP.NET Core/Kestrel 的形态。换句话说，你现在缺的不是“架构方向”，而是**一套贴着现有代码的同步实现**。

我对 Kimi 的判断是：**方向基本对，但需要按你这个仓库的现实改成“更保守、更快落地”的版本**。最重要的调整有三个。第一，不要把“Studio 监控在线 Studio”作为实现目标，真正应该落地的是“**中心 Studio 监控多个边缘 Station**”；第二，不要第一步就把浏览器前端也切到 SignalR，**机器到机器用 SignalR，Studio 内部 UI 仍优先复用你已有的 REST + SSE 模式**；第三，不要把 `RuntimeNormalizedResult` 原样跨网传输，因为它里面的 `PrimaryOutputs` 是 `Dictionary<string, object?>`，而图片字节又被 `[JsonIgnore]` 排除了，直接拿来做跨节点传输 DTO 会把序列化稳定性和协议演进问题提前引爆。

如果目标是“给 gpt5.5 快速稳态落地”，我建议把整个计划压缩成**四个可独立合并的 PR**：先做共享协议，再做 Studio 入站 Hub，再做 Station 客户端与离线补推，最后接监控页和硬化。这样节奏快、回滚容易，而且每个阶段都能在真实工控机场景里单独验收。

## 代码现状复核

Desktop 端已经具备“中央枢纽”的基础骨架。`ClearVision.Product.Desktop` 目标框架是 `net8.0-windows`，引用了 `Microsoft.Web.WebView2`，同时通过 `FrameworkReference Include="Microsoft.AspNetCore.App"` 内嵌了 ASP.NET Core；`Program.cs` 中直接 `WebApplication.CreateBuilder()`，配置 Kestrel、静态资源、认证中间件、健康检查、业务 API、分析接口和检查事件 SSE 端点。这说明 Studio 端**不需要另起一个旁路服务**，而是直接在现有 Desktop 后端里加一个 Station 入站 Hub 就够了。

不过，Desktop 现在的 Web 服务是**明确偏“内嵌本机 UI”场景**的，而不是面向局域网边缘节点的。`Program.cs` 现在用的是 `options.ListenLocalhost(_webPort)`，CORS 判断逻辑也只接受 loopback 主机名或 loopback IP，日志里输出的也是 `http://localhost:{port}`。这意味着你不能简单把现有服务“顺手开放到 LAN”就结束，还必须补上**配置化监听、机器鉴权、边缘节点接入隔离**。这也是 Kimi 说“把 localhost 改成 0.0.0.0”还不够完整的地方。

Studio 前端到后端的实时模式，你其实已经有一个很值得复用的实现参照：`InspectionEventEndpoints.cs` 用 SSE 暴露 `/api/inspection/realtime/{projectId}/events`，支持 `Last-Event-ID` 重放、基于 `Channel` 的流式发送、以及每 30 秒一次心跳保活。这个模式非常适合被复用到“中央监控大屏”上。也就是说，**Station 到 Studio 用 SignalR，Studio 到 WebView2 页面继续走 REST + SSE**，这样你只需要新增一层“站点事件总线”，而不是把整套前后端通信一起重写。

Station 端当前则非常清晰地还是“本地自治运行器”。`ClearVision.Product.Station` 的 `Program.cs` 只注册了 `RuntimeHost`、`StationLocalSettingsStore`、`StationSiteProfileStore` 和主窗体；项目文件只引用了 Hosting 和 Logging 包，没有 SignalR Client，也没有任何 ASP.NET 或网络客户端依赖；`MainForm.cs` 里 `BindRuntimeEvents()` 也是把 `_runtimeHost.SnapshotChanged` 和 `_runtimeHost.ResultAvailable` 仅仅绑定到本地 UI 刷新。这个现状很适合做“**加一个 HostedService 订阅事件并出站同步**”，而不适合做“把 MainForm 继续缝成一个大而全控制器”。

运行时抽象层给了你非常好的起点，但也暴露了两个要主动规避的坑。好处是：`RuntimeNormalizedResult` 已经包含运行号、包信息、图像 ID、检测结果、耗时、诊断信息、时间戳和主输出；`RuntimeHostSnapshot` 也已经带了 `SessionOkCount`、`SessionNgCount`、`SessionErrorCount`；`StationLocalSettings` 还带着 `StationId` 与 `LineName`。坑在于：`RuntimeNormalizedResult.PrimaryOutputs` 是 `Dictionary<string, object?>`，而 `OutputImageBytes` / `SourceImageBytes` 被 `[JsonIgnore]` 排除了，所以它**适合做本地运行时对象，不适合不经裁剪直接作为跨节点协议**。

最后，`RuntimeHost` 的事件时机也说明这个方案非常顺手：它在内部构建好 `RuntimeNormalizedResult` 之后，会先尝试写入本地 result writer，再触发 `ResultAvailable?.Invoke(result)`，然后记录日志并重新发出 `SnapshotChanged`。这意味着你完全可以把同步层挂在现有事件之后，不去打断检测主流程；同时也说明你的核心原则应该是：**检测成功与否绝不能依赖 Studio 在线**。

## 推荐目标架构

我建议采用一个非常明确的目标架构：**中心 Studio 作为 Hub，边缘 Station 作为出站客户端，浏览器/大屏仍由 Studio 通过 REST + SSE 驱动**。这里机器到机器选 SignalR，不是因为它“更潮”，而是因为你已经站在 ASP.NET Core 上了；官方文档确认 SignalR 客户端支持自动重连，Hub 同时支持 JSON 和 MessagePack，而 MessagePack 是内建、二进制、消息更紧凑的协议；Kestrel 本身也就是 ASP.NET Core 推荐的默认服务器。这条路线对你现有仓库的改动最少。

更关键的是，这条路线**贴合你的工程边界**。当前 Studio 已经有 Kestrel、静态站点、认证中间件、SSE、分析接口；Station 则是纯 WinForms + Host + Runtime 单体。把两端都改成 gRPC、或者另起 MQTT Broker、或者让前端直接同时管 Hub/SSE 两套实时连接，都会比现在更重。这里有一个明确的推断：**对这个仓库来说，最小改动路径不是“再造系统”，而是“在现有 Desktop 后端上叠一个站点入站面，在现有 Station 宿主里叠一个站点出站面”**。这个判断来自你仓库当前的进程形态和依赖形态，而不是抽象层面的偏好。

从工业界成熟做法看，你的方向也是顺的。Cognex 的公开资料强调统一软件平台跨多设备协同，MVTec 强调机器视觉应用从开发、部署到运行阶段的稳定性与长期使用，KEYENCE 则明确把“实时机器运行监控”作为工业场景的重要能力。它们未必都公开自己的内部传输协议，但共同方向很明确：**边缘侧稳定运行，中心侧统一观测与监视**。

基于这些事实，我建议你的目标架构暂时不要写成 “Studio ↔ Studio ↔ Station”，而应该统一改成下面这句：

> **一台中心 Studio 负责汇聚与可视化，多个边缘 Station 负责检测与上报；所有边缘节点在 Studio 离线时仍可独立运行，并在网络恢复后补推摘要结果。**

## 快速落地 TODO

下面这份 TODO 我按“**四个 PR + 一个硬化尾声**”来写，目的就是让 gpt5.5 可以直接照着切。每个 PR 都要求：**独立可编译、独立可验收、默认配置下不改变现有本地使用行为**。

### PR Alpha

这一段先不要碰网络实现，先把**命名和共享协议**收干净。你现在文档语义上最容易出错的地方，就是“Studio/Station”混叫。后面的代码、配置、日志、监控页标题都必须统一成：`Studio` 指中心桌面枢纽，`Station` 指边缘低配运行点；以后不要再出现“在线 studio”这种语义，否则 AI 在后续编码时非常容易把中心端和边缘端角色写反。这个整理应该先做，因为它能显著降低后续自动编程时的误判概率。

**改动建议**

- 在 `ClearVision.Product/src/ClearVision.Product.Runtime.Abstractions/` 下新增一个 `Remote/` 或 `StationSync/` 目录。
- **不要新建单独的 `ClearVision.Product.Network` 项目作为第一步**；对你这个仓库，最快路径是先把同步 DTO 也放在已有的 `Runtime.Abstractions` 里，因为它本来就是 Runtime / Station 共享抽象层。
- 新增以下 DTO：
  - `StationRegistrationDto`
  - `StationHeartbeatDto`
  - `StationSnapshotDto`
  - `StationResultSummaryDto`
  - `StationReplayCursorDto`
  - `StationCommandDto`
  - `StationCommandResultDto`
- 所有 DTO 都带 `SchemaVersion`，默认 `1`。
- 结果类**不要**直接沿用 `RuntimeNormalizedResult` 原型跨网传输，而是裁成稳定摘要：
  - 保留：`StationId`、`LineName`、`RunId`、`PackageId`、`PackageName`、`FlowHash`、`ImageId`、`Outcome`、`InspectionStatus`、`ExecutionTimeMs`、`DiagnosticCode`、`DiagnosticMessage`、`StartedAtUtc`、`CompletedAtUtc`
  - **新增 `SequenceId`（`long` 类型）**：每站点自增，用于后续 ACK 去重和断线补推排序。启动时从 spool 文件恢复最大值，避免重启后 ID 重叠。
  - **Phase 1 不传 `PrimaryOutputs`。** `PrimaryOutputs` 是运行时内部的算子输出字典，内容完全取决于用户自定义流程，跨网传输意味着要处理任意类型的序列化兼容性。监控面板真正需要的 `Outcome`、`ExecutionTimeMs`、`DiagnosticCode`、`DiagnosticMessage` 已经够用。如果后续需要查看完整算出输出，Studio 通过 REST 按需从 Station 拉取，不走实时同步通道。
  - **同步层不包含任何图片数据。** `OutputImageBytes`、`SourceImageBytes` 在所有阶段的同步 DTO 中均不出现。图片传输如果未来需要，作为独立的”图片服务”子系统规划，不走 SignalR Hub 主通道。

**验收标准**

- Desktop、Runtime、Station 全部编译通过。
- 旧功能不变。
- 共享 DTO 有基本序列化测试，至少覆盖 camelCase、枚举字符串、UTC 时间。
- 任何新 DTO 都有 XML 注释，方便后续 AI 补全。

### PR Beta

这一段把 Studio 正式变成**站点入站枢纽**。这里的核心不是“把现有 localhost 直接改成公网风格服务”，而是**在保持默认 loopback 模式不变的前提下，新增一个显式的 LAN 接入模式**。因为你现有 Desktop 后端就是围绕 loopback UI 设计的，所以必须保留默认安全姿态。

**改动建议**

- 在 `ClearVision.Product.Desktop.csproj` 里新增：
  - `Microsoft.AspNetCore.SignalR.Protocols.MessagePack`
- 在 `ClearVision.Product/src/ClearVision.Product.Desktop/` 下新增：
  - `Hubs/StationHub.cs`
  - `Services/StationRegistryService.cs`
  - `Services/StationTelemetryBuffer.cs`
  - `Services/StationIngressAuthService.cs`
  - `Models/StationStatusViewModel.cs`
- 在 `Program.cs` 里做三件事：
  - `services.AddSignalR().AddMessagePackProtocol();`
  - `app.MapHub<StationHub>("/hubs/station-ingest");`
  - 新增配置驱动监听：
    - `Loopback` 模式继续 `ListenLocalhost`
    - `Lan` 模式才 `ListenAnyIP` 或指定网卡 IP
- 在 `appsettings.json` 或新的 `station-ingress.json` 里新增：
  - `StationIngress:Enabled`
  - `StationIngress:ListenMode`
  - `StationIngress:Port`
  - `StationIngress:SharedToken`
  - `StationIngress:OfflineThresholdSeconds`
  - `StationIngress:ResultBufferPerStation`
- 给 Hub 接入做**单独机器鉴权**：
  - 最简版：预共享 token
  - 验证位置：Hub Filter 或专用 auth service
  - 先不要把现有 UI 登录体系硬塞进 Station 接入链路
- 新增供 WebView2 页面使用的接口：
  - `GET /api/stations`
  - `GET /api/stations/{stationId}`
  - `GET /api/stations/summary`
  - `GET /api/stations/{stationId}/results?take=100`
  - `GET /api/stations/events`（SSE）
- `StationRegistryService` 要维护：
  - 注册信息
  - 最后心跳时间
  - 当前包信息
  - 当前状态
  - **最近 200 条结果摘要/站点（仅用于实时面板，更多历史通过 REST 按需从 spool 或数据库查询，避免多站点长时间运行后内存膨胀）**
  - 在线/离线判定
- `StationTelemetryBuffer` 先用**内存 + 可选 JSONL append**，先不要第一步就改 `VisionDbContext` 和迁移。你现有 Desktop 后端已经在启动时处理 EF Core 和 SQLite WAL，这部分先保持稳定，监控面板单独走一套轻缓冲最安全。

**为什么 UI 先继续走 SSE**

你现有 Desktop 已经有一套成熟的 SSE 事件流实现，支持重放、心跳和基于 `Channel` 的事件推送；这意味着站点监控页最短路径不是“浏览器再接一个 SignalR JS 客户端”，而是**Hub 收边缘消息，Registry 形成中心态，SSE 再把中心态推给前端**。这比前后端双改 SignalR 更贴你当前代码。

**验收标准**

- Desktop 默认启动方式不变，仍能本地打开现有 UI。
- 显式开启 `Lan` 模式后，局域网其他机器可连到 `StationHub`。
- 用一个最简 Console/Fake Client 能成功注册、发心跳、发快照、发结果。
- Studio 进程重启后，站点列表能在边缘侧自动重连后恢复。SignalR .NET 客户端官方支持 `WithAutomaticReconnect()`，这一点后面要给 Station 用起来。

### PR Gamma

这一段把 Station 变成**自动出站上报客户端**。注意不要把网络逻辑继续塞进 `MainForm.cs`。当前 `MainForm` 已经很大，而且它只负责本地视图；最适合的落点是一个 Hosted Service，订阅 `RuntimeHost` 事件然后独立处理出站队列。

**改动建议**

- 在 `ClearVision.Product.Station.csproj` 增加：
  - `Microsoft.AspNetCore.SignalR.Client`
  - `Microsoft.AspNetCore.SignalR.Protocols.MessagePack`
- 在 `ClearVision.Product/src/ClearVision.Product.Station/` 下新增：
  - `Sync/StationSyncOptions.cs`
  - `Sync/StationSyncHostedService.cs`
  - `Sync/StationHubClient.cs`
  - `Sync/StationSpoolStore.cs`
  - `Sync/StationOutboundEnvelope.cs`
  - `Sync/StationIdentityResolver.cs`
- 在 `Program.cs` 里将同步服务注册为 Hosted Service。
- `StationSyncHostedService` 做这些事：
  - 启动时读取 `StationLocalSettingsStore.Current`
  - 若 `StationId` 为空则生成一次并持久化
  - 创建 `HubConnection`
  - 启用 `WithAutomaticReconnect()`
  - 连接成功后先 `RegisterStation`
  - 定时 `Heartbeat`
  - 订阅 `RuntimeHost.SnapshotChanged`
  - 订阅 `RuntimeHost.ResultAvailable`
- 事件发送策略：
  - `SnapshotChanged`：做 **500ms~1000ms 去抖**
  - `ResultAvailable`：即时入队
  - `LogMessage`：Phase 1 不上送，避免噪声
- 队列策略：
  - 用 `Channel<T>`，**必须 bounded**
  - 推荐分两条队列：`snapshotQueue` 与 `resultQueue`
  - 队列满时：快照可覆盖旧值，结果不可直接丢
- 离线补推：
  - `StationSpoolStore` 写到 `%LocalAppData%\ClearVisionStation\spool\`
  - 格式先用 JSONL
  - 断线时结果落盘
  - 重连后按 `SequenceId` 顺序补推
  - **容量上限：每站点默认 10,000 条摘要（约 5-10MB JSONL）**
  - **超出后策略：最旧未确认的记录被丢弃，并在 Station 本地日志中标记丢失区间**
  - 理由：低端工控机磁盘有限，补推的意义是"不丢最近的检测结果"，不是"全量回溯"
- 图片策略：
  - **Phase 1 完全不推图**
  - 结果流只推摘要
  - 若要显示缩略图，放到后续 PR；你当前运行时对象里的图片字节被 `[JsonIgnore]` 排除，而且原图跨网传输最容易把低配 IPC 拖死。

**这里有一个比 Kimi 更重要的补充**

Kimi 说“`RuntimeNormalizedResult` 已标准化，所以可直接序列化传输”，这句话**只对‘本地对象层面’成立，不对‘跨节点协议层面’成立**。原因不是字段不够，而是协议稳定性不够：`PrimaryOutputs` 的动态对象图和图片字节策略都不适合直接跨机。你应该把它视作**运行时内部对象**，再派生出一个**站点同步摘要对象**。这一步是整个方案稳定性的分水岭。

**验收标准**

- Station 在断网时仍可继续本地检测。
- Station 不因 Studio 离线而阻塞 UI 或停止 Runtime。
- 恢复网络后，已落盘的摘要结果能回补到 Studio。
- 在 5～10 条/秒的模拟结果速率下，Station UI 无明显卡顿。
- 不改现有 `MainForm` 的本地显示逻辑，只额外挂同步服务。

### PR Delta

这一段接你的中心监控可视化，而且要**刻意复用你当前结果页的设计语言**。你发的结果界面已经有卡片、趋势、分布、告警、高频区域、筛选器这些视觉母板；中央 Studio 的站点监控页最好的做法不是重新发明一个“大屏框架”，而是在同一套前端组件风格里增加“站点层级”。这会让用户感知非常统一。用户侧这一点不需要外部资料支撑，因为它来自你给我的界面目标。  

**改动建议**

- 在 `wwwroot` 里新增一个 “站点监控” 页面，而不是侵入现有结果页主体逻辑。
- 页面分三块：
  - 顶部总览卡片：在线站点数、离线站点数、总 OK/NG/Error、平均耗时、告警数
  - 中间站点矩阵：每个 Station 一张小卡
  - 右侧或底部详情：最近结果流、缺陷 Top、当前包、最后心跳、最后错误
- 数据流：
  - 首次进入：REST 拉全量
  - 后续更新：SSE 增量推送
- 每个站点卡至少包含：
  - `StationId`
  - `LineName`
  - 在线状态灯
  - 当前状态 `Idle/Running/Faulted`
  - 当前运行包
  - Session OK/NG/Error
  - 最后心跳时间
  - 平均耗时
- 最近结果区域只保留摘要流：
  - 时间
  - 图像 ID
  - OK/NG/Error
  - 诊断码
  - 诊断信息（`DiagnosticMessage`）
  - 耗时
- 先不要做中央“报表中心改造”，也不要先接现有分析数据库模型。你已经有 `/api/analysis/*` 能力，但那更像单项目/单上下文分析接口；站点监控第一版应该是**在线态看板**，不是“跨产线数据仓库”。

**验收标准**

- 局域网内 Station 上线/掉线，中心页在阈值时间内变灯。
- 结果摘要到达中心 UI 的端到端延迟，在典型 LAN 下维持在秒级以内。
- 关闭某个 Station 后，不会拖垮其他站点卡刷新。
- 中心页不因为单个站点持续高频上传而整体卡顿。

### PR Epsilon

这一段是**硬化尾声**，不要和前面混在一个 PR 里。它包括 ACK、重放、命令下发、图片、报警与安全收口。如果前四个 PR 还没在真实工位上跑稳，这一段先不要开工。

**改动建议**

- 生命周期与补推
  - 基于 PR Alpha 已定义的 `SequenceId`，Studio 回 ACK 水位
  - Station 仅清理已确认补推的本地 spool
- 指令通道
  - `StartRuntime`
  - `StopRuntime`
  - `ReloadPackage`
  - `ApplySiteProfile`
  - `Ping`
- 图片
  - **当前阶段的同步层不传输任何图片数据。** 图片传输如未来需要，作为独立的"图片服务"子系统单独规划，通道与 Hub 主数据流完全分离。当前各 Station 本地已通过 `RuntimeImageWriter` 持久化图片，Studio 可按需通过 REST 拉取。
- 告警
  - 设备离线超阈值
  - 连续 NG 超阈值
  - Station 重复崩溃
- 安全
  - token 轮换
  - Hub 接入审计日志
  - Studio LAN 模式下保留 UI 端 loopback 限制，不因 Station 接入就扩大浏览器来源范围。你现在的 UI CORS 明确围绕 loopback 设计，这个边界不能随手打穿。

**验收标准**

- Studio 异常重启后，Station 全部自动重连。
- 同一摘要重复发送不会造成中心计数翻倍。
- 单个 Station 短时网络抖动不会造成大面积误告警。
- UI 仍可在不连接任何 Station 时正常作为本地 Studio 使用。

## 验收口径

这个项目不要用“功能都写完了”来验收，而要用**四条闭环**来验收。

第一条是**检测闭环**：Studio 离线时，Station 依然能正常加载运行包、跑检测、更新本地 UI、记录最后运行号和最后可用包路径；这条闭环必须在任何联机功能之前成立，因为 `RuntimeHost` 本身就是本地运行时核心，而 `StationLocalSettingsStore` 还承担着崩溃痕迹与最后状态落盘。

第二条是**通讯闭环**：一台 Station 启动后能注册到中心 Studio，发送心跳、快照和结果摘要；Studio 能在自己的 API 层查到该站点在线态；UI 能看到该站点卡片。这个闭环打通以后，你的中央监控方案就已经成立了。

第三条是**断线补推闭环**：在检测过程中断网 5 分钟，Station 本地继续跑；恢复网络后，中心 Studio 能收到断线期间的摘要结果，而且顺序正确、不重复。这一条是你方案能不能在现场稳定跑的关键，不是可选项。

第四条是**安全闭环**：默认仍是 loopback 本地模式，只有明确打开 LAN 模式并配置接入令牌后，边缘 Station 才能接入；UI 浏览器来源规则保持现有策略，不因为边缘接入而把整个 Desktop 服务暴露成“任何局域网页面都能打”的开放式服务。

## 暂不做的事项

当前阶段我建议**明确不做**下面这些事，不是因为它们永远没价值，而是因为它们会显著拉长你第一次上线的路径。

- **不先做 MQTT。** 你的仓库现状和 Desktop/Station 进程形态，决定了 SignalR 直连是更短路径；加 Broker 会引入额外部署对象、额外运维面和额外故障点，而你当前真正缺的是“边缘同步层”，不是“总线基础设施”。这是基于你现有代码结构和官方 SignalR 能力做出的工程判断。
- **不先做 Studio-to-Studio 网状拓扑。** 你真正的边缘节点是 Station，不是第二批 Studio。先把中心 Studio 与多个 Station 跑稳，再谈多中心联邦。
- **不先把原图通过 Hub 实时推。** 原图和大输出对象最容易把低端工控机、局域网交换机和中心 UI 一起拖垮。摘要先行、图片按需，是你当前最稳的节奏。
- **不先动现有分析数据库主模型。** 你已经有 EF Core / SQLite / 分析接口，但中央监控第一步是“在线态枢纽”，不是“全厂报表仓库”。等入站和补推跑稳了，再考虑正式入库。
- **不把远程下发和实时上报放在同一个首发 PR。** 单向遥测先稳，再加双向控制，能少掉很多事故面。

## 给 gpt5.5 的执行约束

这部分不是技术愿景，而是我建议你直接交给 gpt5.5 的执行规则。

- 只接受**可独立编译**的 PR。
- 每个 PR 都要补一个最小可运行 Demo 或最小集成测试。
- 默认配置下，现有本地 Studio 和本地 Station 行为**完全不变**。
- 任何新配置项都必须有默认值，并写入示例配置。
- 任何网络异常都**不得阻塞 RuntimeHost 检测流程**。
- `RuntimeHost.ResultAvailable` 事件处理中，同步层推送必须是 **fire-and-forget**，不能 await 网络发送结果后再继续下一次检测。检测主循环的吞吐量绝不能受网络状态影响。高速产线节拍可能很短，如果同步层阻塞了检测循环，后果比丢几条摘要严重得多。
- 任何跨网结果 DTO 都不要直接复用 `RuntimeNormalizedResult` 原型。
- 任何图片都不要在首发阶段走 Hub 主数据流。
- 任何 LAN 暴露都必须是**显式开关**，不能偷偷替换当前 loopback 行为。当前 Desktop 明确是本机嵌入式 Web UI 形态，这个默认边界必须保留。

如果让我把这份 TODO 再压缩成一句最适合落地执行的话，就是：

> **先把“Station 检测完成 → 摘要入本地队列 → SignalR 推到 Studio → Studio Registry 落中心态 → 监控页通过 REST+SSE 更新”这个闭环做出来；在这个闭环跑稳之前，不要分心去做图片、远程发包、MQTT 和跨中心联邦。**