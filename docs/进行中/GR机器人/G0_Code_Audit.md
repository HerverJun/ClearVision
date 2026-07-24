# G0 代码与运行时审计

> 审计日期：2026-07-24
> 范围：当前 HEAD 的只读审计；未启动产品、未连接机器人、未访问 TCP/502、未写寄存器、未执行 FTP 操作。
> 结论：代码架构可进入后续设计，但设备 Gate 未通过；本文件不是实现授权。

## 1. Git 基线与工作树隔离

| 项目 | 记录 |
|---|---|
| Current Branch | `codex/gr-communication-commissioning` |
| Initial SHA | `65537ecce5533bbb335db93b0c94e068dc4d492a` |
| Upstream | `origin/codex/gr-communication-commissioning` |
| 审计开始时 Remote SHA | `612b4b60d9620d5ea9f7dbbf5fd3e4acf306d098` |
| 初始已存在工作树改动 | `MM .workbuddy/memory/2026-07-24.md`、未跟踪权威 TODO、未跟踪 `tmp/`；均不属于本任务，不会暂存或覆盖。 |

审计中再次观察到 `.workbuddy/memory/MEMORY.md` 已修改；同样不属于本任务。`tmp/` 中存在意图进行 Modbus 写入、运动或 FTP 上传的脚本，本轮没有读取为执行依据、没有运行、没有修改，也不会提交。

## 2. Modbus 正式实现：NModbus 与同端点序列化

### 2.1 现有可复用实现

证据：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/ModbusCommunicationOperator.cs`。

| 能力 | 代码证据 | 审计结论 |
|---|---|---|
| 协议库 | 第 11、62 行：`NModbus`、静态 `IModbusFactory = new ModbusFactory()` | 当前正式通用 Modbus 算子的真实协议栈是 NModbus。 |
| 操作范围 | 第 47 行列出 `ReadCoils`、`ReadHolding`、`WriteSingle`、`WriteMultiple` | 后续 GR 只能在同一栈内映射 FC03、FC06、FC16；本轮不调用写路径。 |
| 会话 key | 第 218、356 行构造并使用 `BuildConnectionKey(ipAddress, port)` | 池按 **IP:Port**，不将 Unit ID 纳入 socket 所有权 key。 |
| 创建锁 | 第 57、219 行 `ConnectionLocks` | 同端点建立连接被串行化。 |
| 操作锁 | 第 58、357 行 `OperationLocks` | 同端点所有 Modbus 操作被串行化，避免同 socket 并发请求交织。 |
| 进行中计数 | 第 61、367、423 行 `ActiveOperations` | 回收时避免主动清理仍在使用的连接。 |
| 超时 | 第 49、52、246–255、341–342、358 行 | 默认 5,000 ms；每次操作更新 send/receive timeout，连接也带可取消的 timeout token。 |
| 存活检测 | 第 283 行 `IsConnectionAlive` | 使用 `Connected` 与 `Poll/Available` 检查；仍须实机验证半开连接行为。 |
| 异常释放 | 第 380–407、537–576 行 | 取消、IO、Socket、timeout 与其他异常均强制 `PurgeConnection`，并释放 master/client。 |
| 空闲/容量回收 | 第 53–54、580–610 行 | 空闲 10 分钟清理，最多 32 个 pooled socket；`ClearConnectionPool()`（第 307 行）可统一释放。 |

这套实现已经满足“同一进程内同 IP:Port 只有一个连接创建路径、读写串行”的基础条件。它**不能**单独证明跨进程（Desktop 与 Station）或跨独立服务的端点所有权；G1 必须从此实现抽取/复用唯一会话管理与 reservation，而不能另起 Modbus client。

### 2.2 现有但不可直接作为机器人唯一会话的 ConnectionPoolManager

证据：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/ConnectionPoolManager.cs`。

- 第 8、25 行同样引用 NModbus；第 37 行提供 `GetOrCreateModbusConnectionAsync`。
- 第 44 行连接 key 是 `modbus:{ip}:{port}:{slaveId}`：同一 IP:Port 的不同 Unit ID 会形成不同池项，不能证明“一个 TCP socket”。
- 第 45 行只有按该 key 的连接建立 gate；没有与 `ModbusCommunicationOperator.OperationLocks` 对应的单 endpoint Modbus 操作串行队列。
- 第 152–248、497–536 行的 `PooledTcpConnectionLease` 是普通 `NetworkStream` lease，不能与 NModbus master 互换使用。
- 第 20–31、330、408 行显示它有 30 秒健康计时器、5 分钟空闲回收和 dispose；但 `VisionRuntimeServiceCollectionExtensions.cs` 中没有该类型的注册。

因此它可提供后续设计参考，**不能**被机器人 Gateway 单独采用，也不能与通用 Modbus 算子的池并存来访问同一端点。

### 2.3 已存在的 GR 只读诊断基线

| 证据 | 事实与边界 |
|---|---|
| `Infrastructure/Communication/Gr/GrRegisterMapCatalog.cs`、`GrStateDecoder.cs`、`JsonCommunicationProfileStore.cs` | 已有嵌入式映射、状态解码、本地 JSON profile。 |
| `Infrastructure/ClearVision.Product.Infrastructure.csproj:91` | `Communication/Gr/Templates/gr-v3.0-register-map.json` 已作为 embedded resource。 |
| `.../Templates/gr-v3.0-register-map.json:6–9` | 模板写有 port 502、`defaultUnitId:255`、`437–459` 只读状态区、禁止写；其中 Unit ID 只是现有诊断默认值，见设备矩阵的冲突，不可当作已证实参数。 |
| `Desktop/Endpoints/CommunicationEndpoints.cs:21–178` | profile 保存被强制为 `ReadOnly=true`；只允许 `Connect` 与 `ReadOnce`；任何写操作在网络前被拒绝；ReadOnce 只准 `ReadHolding`/`ReadCoils`。 |
| 同文件第 94–117 行 | Connect 诊断使用临时裸 `TcpClient`，不是 NModbus 共享池，未来不得被当成生产会话所有者。 |
| 同文件第 68–178 行 | ReadOnce 通过注入的 `ModbusCommunicationOperator` 读取，能复用 NModbus 静态池。 |

结论：这是一个有价值的**只读诊断基线**，不是 GR Gateway、写控制或端点 lease。以后应保留其“写入拒绝”语义，且不得由它的 Connect 探测路径承担唯一会话所有权。

## 3. PLC、普通 TCP 与 Modbus 的边界

### 3.1 ClearVision.PlcComm

| 证据 | 结论 |
|---|---|
| `ClearVision.Product/Directory.Packages.props:18,47` | 版本集中管理：`HslCommunication 12.7.0`、`NModbus 3.0.81`。 |
| `src/ClearVision.PlcComm/ClearVision.PlcComm.csproj:18` | PlcComm 实际依赖 HslCommunication；没有 NModbus 包引用。 |
| `Core/HaoPlcClientBase.cs:10,323–330` | Hsl 结果被封装成 ClearVision PLC client 结果，具有它自己的连接/通信锁、重连与物理 close 策略。 |
| `Siemens/SiemensS7Client.cs:4`、`Mitsubishi/MitsubishiMcClient.cs:3`、`Omron/OmronFinsClient.cs:3` | Hsl 实际用于 Siemens S7、Mitsubishi MC、Omron FINS。 |
| `Infrastructure/Operators/PlcCommunicationOperatorBase.cs` | PLC 算子另有静态 `IPlcClient` 池、endpoint/operation lock、heartbeat/ping 策略。 |

Hsl PLC 栈不是当前 GR Modbus 正式路径。采用 `HslCommunication.ModBus.ModbusTcpNet` 会产生第二套行为、连接与异常语义，违反 G0 的单栈 ADR；本轮明确否决该路径。

### 3.2 普通 TCP 不可混用

`Infrastructure/Services/TcpDeviceManager.cs` 与 `Infrastructure/Operators/TcpCommunicationOperator.cs` 面向文本/字节流 TCP 的连接、协议帧和 lease 语义。它们没有 NModbus master 的事务、Unit ID 与 FC 编码语义；不得拿其 `NetworkStream` 或连接池替代 GR Modbus 会话，也不得让 Modbus 与普通 TCP 同时占用一个已知 GR IP:Port。

## 4. Desktop、前端、Settings 与 Capability

| 领域 | 文件级证据 | 当前事实 / 后续边界 |
|---|---|---|
| Desktop 组成与路由 | `Desktop/Program.cs:247–356,402–403` | Desktop 注册 Settings、通信 endpoint、Station hub；legacy 静态入口为 `wwwroot/index.html`。 |
| 现行 Settings | `Desktop/wwwroot/src/features/settings/settingsView.js:361–381,434–439,515–562` | 生产 Settings 是 legacy JavaScript view；已有 PLC、TCP、Station 等 tab，并由安装函数挂载。G2 若实现页面，应以届时实际启动页及此正式体系为准。 |
| feature/capability | `Desktop/wwwroot/src/features/settings/settingsView.js:499–500`、`Desktop/StudioOptions.cs` | 只有既有 feature/capability；未发现机器人 capability。它应在后续按现有权限/能力框架设计，不能假定已存在。 |
| FrontendV2 | `Desktop/StudioStartupPageResolver.cs`；`Desktop/FrontendV2/package.json`；`Desktop/ClearVision.Product.Desktop.csproj:89–131` | V2 是 Vue 3、Vite、TypeScript、Pinia 构建目标，受 `StudioOptions.WorkspaceV2Enabled` 控制；flag off 走 `/index.html`、flag on 走 `/v2/index.html`，缺 V2 资源应诊断而非回落。G0 不恢复它，也不把它断言为正式 Settings 入口。 |

## 5. Desktop/Station DI、.cvpkg 与 Station 合同

### 5.1 运行时边界

- `Infrastructure/DependencyInjection/VisionRuntimeServiceCollectionExtensions.cs:37–82` 的 `AddVisionRuntimeCoreServices()` 是 Desktop 与 Station 共用的 runtime core，注册 `ITcpDeviceManager`、GR catalog/profile store、`ModbusCommunicationOperator` 等。
- 同文件第 221–290 行的 `AddVisionRuntimeServices()` 是 Desktop 附加层，注册 DB、检查 worker、托管服务等 Desktop-only 构成。
- `Station/Program.cs` 使用 core services，并额外注册 `StationSyncHostedService`、station storage/sync/runtime host。故后续生产会话所有权应落到 Station，而 Desktop 只读调试必须经显式 lease/release 规则让位；当前代码尚未实现该 reservation。

### 5.2 .cvpkg 扩展点

`Runtime/RuntimePackageExporter.cs`、`RuntimePackageLoader.cs`、`RuntimePackage.cs` 现有 package 输出/加载 `package.json`、`flow.json`、`runtime-profile.json`、`field/*`，并支持 `StationProfile`、`TriggerProfile`、`ResultMappingProfile`、`ModelAssets`、`RuntimeParameters`、`DefaultSiteProfile`、`GlobalVariables`、`ProjectAssets` 等 field extension。未发现 `GrRobotProfile`、机器人 endpoint reservation 或 robot runtime profile。

结论：G5/G6 如需持久化机器人配置，应扩展现有 field-extension/package validation 边界；不得声称已有 GR profile，也不得把临时本机 JSON 诊断 profile 直接升级为 `.cvpkg` 合同。

### 5.3 Station 同步合同

| 合同 | 证据 | 结论 |
|---|---|---|
| Hub 入口 | `Runtime.Abstractions/StationSyncContracts.cs:31+`；`Desktop/Program.cs:352` | Station ingress 通过 `StationHub` 挂载在 `StationSyncContractDefaults.HubPath`（`/hubs/station-ingest`）。 |
| 注册、心跳、快照 | `Desktop/Hubs/StationHub.cs`、`Desktop/Station/StationRegistryService.cs` | 已有认证、register、heartbeat、snapshot/health/log/result 上报以及确认。 |
| 周期 | `Station/Sync/StationSyncOptions.cs` | 默认 heartbeat 5 秒、health 15 秒。 |
| 命令 | `StationSyncContracts.cs`、`StationCentralStore.cs`、`StationRegistryService.cs:392–413` | 现有类型为 Ping、StartRuntime、StopRuntime、ReloadPackage、DeployPackage、ApplySiteProfile、CollectLogs；状态 Created → Delivered → Accepted → Running → 终态。没有 robot command。 |
| 持久化/恢复 | `Station/Sync/StationSyncHostedService.cs`、station spool/journal stores | result 与 command result 以 JSONL spool/journal 记录、可重放终态；后续机器人未知终态不能被自动宣告成功。 |

`StationHub` 只能复用作 Station→Studio 的正式 ingress 合同参考。Desktop 本机机器人状态、调试读写或高频快照不能塞入该 Hub，也不能改变其现有命令语义。

## 6. 可复用性判定

| 资产 | 判定 | 原因 |
|---|---|---|
| `ModbusCommunicationOperator` 的 NModbus pool/lock/timeout/purge | **必须复用/抽取** | 已是正式 Modbus 实现；同 IP:Port key 与串行锁符合唯一会话起点。 |
| GR catalog、state decoder、只读 profile | **可复用为诊断输入** | 只读、映射和 profile 壳已存在；地址/Unit ID 要经过设备 Gate。 |
| `ConnectionPoolManager` | **仅参考，不可独立接管** | key 含 slaveId、无 endpoint operation queue、未注册。 |
| Hsl PlcComm | **不可混用** | 用于 S7/MC/FINS，不是 GR NModbus 栈。 |
| TCP manager/operator | **不可混用** | 字节流 TCP，不提供 Modbus 事务和 socket 所有权。 |
| FrontendV2 | **本轮不可恢复，后续按实际 flag 决定** | 非既定 Settings 主入口。 |
| StationHub | **不可用于 Desktop 本机机器人页** | 仅 Station ingress；命令集无 robot command。 |
| Station health/log/result/spool | **后续可按合同扩展摘要** | 可报告生产机器人健康摘要，但需版本化合同而不是重载现有本机语义。 |

## 7. G0 代码侧结论

1. 代码侧已明确：GR 后续连接层以 NModbus 的现有实现为唯一基础，所有通用算子与机器人 gateway 必须收敛为同一 IP:Port 会话所有者和串行队列。
2. `IModbusTcpSessionManager`、endpoint reservation、`IGrRobotGateway`、`GrRobotProfile` 尚不存在；它们只是未来候选，不可被本计划当成当前结构。
3. 代码清晰不等于设备协议已证明。实际 Unit ID、寻址、控制写、半开连接和参数化程序链路都仍由设备 Gate 阻塞。

关联：[`G0_ADR.md`](G0_ADR.md)、[`G0_Device_Protocol_Verification_Matrix.md`](G0_Device_Protocol_Verification_Matrix.md)。
