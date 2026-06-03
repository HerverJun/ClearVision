# ClearVision Studio-Station 后续路线 TODO

> 目标：在已完成/即将完成 Studio 与 Station 通讯链路的基础上，把 ClearVision 从“单机运行 + 手动运维”推进到“中心 Studio 统一观测、统一统计、统一运维、多 Station 自治运行”的工业现场形态。  
> 执行对象：交给 GPT-5.5 / Codex 进行快节奏工程落地。  
> 关键约束：**暂时不传图片**，不在 Station 端新开 HTTP 服务，不让中心能力影响 Station 本地检测自治。

---

## 0. 总体架构边界

### 0.1 角色定义

统一使用以下术语，后续代码、UI、配置、日志、文档保持一致。

| 名称 | 含义 |
|---|---|
| Studio | 中心桌面端，安装在产线总控办公室，负责监控、统计、发包、诊断和运维 |
| Station | 轻量运行端，安装在各工控机点位，负责加载运行包、执行检测、本地记录、上报摘要 |
| RuntimePackage | Studio 生成并交付给 Station 的运行包 |
| Telemetry | Station 上报给 Studio 的实时摘要数据，包括结果、心跳、健康状态、告警、日志摘要 |
| Command | Studio 下发给 Station 的远程控制指令 |
| Artifact | 文件型资产。当前阶段只用于运行包和日志包，**不包含图片传输** |

### 0.2 目标通信模型

```text
Station  --SignalR-->  Studio
Station  --HTTP Pull--> Studio RuntimePackage Download
Station  --SignalR/后续轻量上报--> Studio LogSummary / Health / ResultSummary

Studio UI  --REST + SSE--> Studio 后端状态
```

当前阶段不要做：

```text
Studio 主动访问 Station HTTP Server
Station 上传原图/缩略图
MQTT Broker
Studio-to-Studio 网状拓扑
复杂分布式一致性
```

### 0.3 总原则

1. **Station 本地检测自治优先**：Studio 离线时，Station 必须继续检测、继续本地写结果、继续显示本地 UI。
2. **中心只接收摘要**：首轮不传图片、不传大对象、不传完整运行上下文。
3. **所有跨网消息必须可幂等**：重连、补推、重复发送不能导致中心统计翻倍。
4. **所有远程命令必须可追踪**：不能只有 true/false，必须有命令生命周期状态机。
5. **所有 LAN 暴露必须显式开启**：Studio 默认仍保持本地 loopback 安全姿态。
6. **所有新增能力必须可独立 PR 验收**：每个 PR 都要可编译、可回滚、可测试。

---

## 1. 优先级总览

推荐按以下 5 个阶段推进。

```text
P0：通讯 MVP
  目标：Station 注册、心跳、结果摘要、断线缓存、Studio 实时看板

P1：中心化持久化 + 健康监控
  目标：结果摘要正式入库、幂等 ACK、水位、健康快照、基础告警

P2：命令通道 + 运行包远程下发
  目标：Studio 下发运行包，Station 拉取、校验、staging、切换、失败回滚

P3：日志集中与远程诊断
  目标：WARN/ERROR 摘要上报、按需收集日志包、诊断页、错误追溯

P4：站点拓扑、安全审计、仿真压测
  目标：站点业务身份管理、权限边界、操作审计、Station Simulator、压力测试
```

为了贴合 AI 编程节奏，建议不要按“月份计划”推进，而是按 **小 PR 快速闭环** 推进。每个 PR 最好控制在 1～3 天内可合并，宁可多拆，不要一个 PR 同时改通讯、数据库、UI、远程发包和日志。

---

# P0：通讯 MVP

## P0.1 新增共享同步协议 DTO

### 目标

先定义稳定的跨节点协议对象，不直接把 `RuntimeNormalizedResult` 原样跨网传输。

### 建议目录

```text
ClearVision.Product/src/ClearVision.Product.Runtime.Abstractions/
  StationSync/
    StationSyncConstants.cs
    StationRegistrationDto.cs
    StationHeartbeatDto.cs
    StationHealthSnapshotDto.cs
    StationResultSummaryDto.cs
    StationReplayCursorDto.cs
    StationCommandDto.cs
    StationCommandResultDto.cs
    StationEnums.cs
```

### DTO 约束

所有 DTO 必须包含：

```csharp
public int SchemaVersion { get; init; } = 1;
public string StationId { get; init; } = "";
public DateTimeOffset CreatedAtUtc { get; init; }
```

### `StationRegistrationDto`

建议字段：

```text
SchemaVersion
StationId
StationName
LineName
StationRole
MachineName
ProcessId
StationVersion
RuntimeVersion
IpAddressHint
MacAddressHash
CurrentPackageId
CurrentPackageName
CurrentPackageVersion
RegisteredAtUtc
```

### `StationHeartbeatDto`

建议字段：

```text
SchemaVersion
StationId
SequenceId
RuntimeState
ConnectionState
CurrentPackageId
CurrentPackageVersion
SessionOkCount
SessionNgCount
SessionErrorCount
SpoolPendingCount
LastResultAtUtc
StationLocalOffsetMinutes
CreatedAtUtc
```

### `StationResultSummaryDto`

不要传图片，不要传 `byte[]`，不要传动态对象。

建议字段：

```text
SchemaVersion
StationId
SequenceId
MessageId
RunId
PackageId
PackageName
PackageVersion
FlowHash
ImageId
Outcome
InspectionStatus
ExecutionTimeMs
DiagnosticCode
DiagnosticMessage
PrimaryOutputsPreview
StartedAtUtc
CompletedAtUtc
CreatedAtUtc
```

其中：

```text
PrimaryOutputsPreview: Dictionary<string, string?>
```

如果原始 `PrimaryOutputs` 存在复杂对象，只做字符串摘要，不做完整序列化。

### `StationHealthSnapshotDto`

第一版可简单，P1 再扩展。

```text
SchemaVersion
StationId
SequenceId
RuntimeState
ProcessUptimeSeconds
CpuUsagePercent
WorkingSetMb
PrivateMemoryMb
DiskFreeMb
DiskTotalMb
SpoolPendingCount
SpoolBytes
CameraStatusSummary
PlcStatusSummary
CurrentPackageId
CurrentPackageHealth
LastErrorCode
LastErrorMessage
CreatedAtUtc
```

### 枚举建议

```text
StationOnlineState
  Unknown
  Online
  Offline
  Warning
  Degraded
  Critical

StationRuntimeState
  Unknown
  Idle
  Running
  Paused
  Faulted
  Stopping
  LoadingPackage

StationCommandType
  Ping
  StartRuntime
  StopRuntime
  ReloadPackage
  DeployPackage
  ApplySiteProfile
  CollectLogs

StationCommandStatus
  Created
  Delivered
  Accepted
  Rejected
  Running
  Succeeded
  Failed
  TimedOut
  Cancelled
```

### 验收标准

- 全项目编译通过。
- 所有 DTO 有 XML 注释。
- 所有 DTO 有最小 JSON 序列化测试。
- DTO 不引用 Desktop、Station、Runtime 具体实现层。
- `StationResultSummaryDto` 中没有图片字段。

---

## P0.2 Studio 端新增 Station Ingress Hub

### 目标

让中心 Studio 能接收 Station 注册、心跳、结果摘要、健康摘要。

### 建议目录

```text
ClearVision.Product/src/ClearVision.Product.Desktop/
  Hubs/
    StationIngressHub.cs

  Services/StationSync/
    StationIngressOptions.cs
    StationIngressAuthService.cs
    StationRegistryService.cs
    StationTelemetryBuffer.cs
    StationEventBus.cs
    StationOfflineMonitorService.cs

  Endpoints/
    StationMonitorEndpoints.cs
    StationMonitorEventEndpoints.cs
```

### 配置项

建议新增配置：

```json
{
  "StationIngress": {
    "Enabled": false,
    "ListenMode": "Loopback",
    "Port": 5000,
    "SharedToken": "",
    "OfflineThresholdSeconds": 15,
    "ResultBufferPerStation": 200,
    "AllowMessagePack": true
  }
}
```

### 监听策略

默认保持现有行为，不破坏本地 WebView2。

```text
Enabled=false：不开放 Station 接入
ListenMode=Loopback：只本机
ListenMode=Lan：绑定指定局域网 IP 或 0.0.0.0
```

不要直接把现有所有 API 暴露成开放 LAN 服务。Station Ingress 是独立入口，要有单独 token 校验。

### Hub 方法

建议 Hub 暴露：

```csharp
Task<StationRegisterAckDto> RegisterStation(StationRegistrationDto dto);
Task<StationAckDto> Heartbeat(StationHeartbeatDto dto);
Task<StationAckDto> PushSnapshot(StationHealthSnapshotDto dto);
Task<StationAckDto> PushResult(StationResultSummaryDto dto);
Task<StationReplayCursorDto> GetReplayCursor(string stationId);
Task<StationCommandDto?> PollCommand(string stationId);
Task ReportCommandResult(StationCommandResultDto dto);
```

P0 阶段 `PollCommand` 可以先返回 null，接口先占位。

### Studio 内存状态

`StationRegistryService` 维护：

```text
StationId
StationName
LineName
StationRole
MachineName
OnlineState
RuntimeState
LastHeartbeatAtUtc
CurrentPackageId
CurrentPackageVersion
SessionOkCount
SessionNgCount
SessionErrorCount
SpoolPendingCount
RecentResults
RecentHealth
LastError
```

### Studio UI 接口

先新增 REST + SSE，不急着给前端接 SignalR JS。

```text
GET /api/stations
GET /api/stations/summary
GET /api/stations/{stationId}
GET /api/stations/{stationId}/results?take=100
GET /api/stations/events
```

### 验收标准

- Studio 默认启动行为不变。
- 开启 `StationIngress.Enabled=true` 后，Fake Client 可注册并推送心跳。
- Studio 日志能看到 Station 注册、断线、重连。
- `GET /api/stations` 能返回在线站点。
- 不接任何 Station 时，Studio UI 仍正常使用。

---

## P0.3 Station 端新增同步客户端 HostedService

### 目标

Station 启动后主动连接中心 Studio，订阅 `RuntimeHost` 事件，把结果摘要和状态摘要推送出去。

### 建议目录

```text
ClearVision.Product/src/ClearVision.Product.Station/
  Sync/
    StationSyncOptions.cs
    StationSyncHostedService.cs
    StationHubClient.cs
    StationSpoolStore.cs
    StationOutboundEnvelope.cs
    StationResultMapper.cs
    StationHealthCollector.cs
    StationIdentityResolver.cs
```

### Station 配置

```json
{
  "StationSync": {
    "Enabled": false,
    "StudioHubUrl": "http://127.0.0.1:5000/hubs/station-ingest",
    "SharedToken": "",
    "HeartbeatIntervalSeconds": 5,
    "HealthIntervalSeconds": 15,
    "ReconnectDelaysSeconds": [0, 2, 5, 10, 30],
    "SpoolDirectory": "%LocalAppData%\\ClearVisionStation\\spool",
    "MaxSpoolMb": 512,
    "MaxSpoolDays": 7,
    "SnapshotDebounceMilliseconds": 1000
  }
}
```

### 设计要求

`StationSyncHostedService` 必须：

1. 不阻塞 `RuntimeHost`。
2. 不阻塞 WinForms UI。
3. 使用 bounded channel。
4. 网络失败时写入本地 spool。
5. 网络恢复后按顺序补推。
6. 不传图片。
7. 不把网络异常抛回检测主流程。

### 事件桥接

```text
RuntimeHost.ResultAvailable
  -> StationResultMapper
  -> StationResultSummaryDto
  -> Channel
  -> SignalR PushResult
  -> 成功则更新 ACK
  -> 失败则落 spool

RuntimeHost.SnapshotChanged
  -> debounce
  -> StationHeartbeatDto / StationHealthSnapshotDto
  -> SignalR Heartbeat / PushSnapshot
```

### Spool 规则

```text
路径：%LocalAppData%\ClearVisionStation\spool\
格式：JSONL
文件：results-yyyyMMdd.jsonl
索引：cursor.json
```

每条 envelope 建议：

```json
{
  "messageType": "ResultSummary",
  "stationId": "...",
  "sequenceId": 123,
  "messageId": "...",
  "createdAtUtc": "...",
  "payload": {}
}
```

### 验收标准

- Studio 不在线时，Station 正常启动、检测、显示本地结果。
- Studio 不在线时，结果摘要写入 spool。
- Studio 恢复后，Station 自动重连并补推。
- 模拟 5～10 条/秒结果，Station UI 不明显卡顿。
- 断网 5 分钟后恢复，Studio 收到断网期间摘要。
- 没有图片字段进入网络消息。

---

## P0.4 Studio 实时监控页面

### 目标

复用当前“结果界面”的视觉语言，新增中央 Station 监控页。

### 页面结构

建议新增导航：

```text
结果
  - 本地结果
  - Station 监控
```

或新增一级：

```text
监控
  - 生产看板
  - 站点列表
```

### 顶部总览卡片

```text
在线站点数
离线站点数
运行中站点数
总检测量
OK 数
NG 数
Error 数
综合良率
平均耗时
当前告警数
```

### 站点矩阵

每个 Station 一张卡片：

```text
StationName / StationId
LineName
OnlineState 灯
RuntimeState
CurrentPackage
Session OK / NG / Error
最近心跳
最近检测时间
SpoolPendingCount
健康状态摘要
```

### 最近结果流

字段：

```text
时间
Station
ImageId
Outcome
DiagnosticCode
ExecutionTimeMs
PrimaryOutputsPreview
```

### 设计限制

- 不显示图片。
- 不预留图片区域。
- 不做复杂报表。
- 不接完整分析数据库。
- 首版只做实时态 + 最近 N 条。

### 验收标准

- Station 上线/离线，页面状态在阈值时间内变化。
- 多 Station 同时上报时页面不卡顿。
- 刷新页面后能通过 REST 恢复当前状态，再通过 SSE 接增量。
- 页面风格与现有 ClearVision 结果页保持一致。

---

# P1：中心化持久化 + 健康监控

P0 跑通后，必须尽快做 P1。否则 Studio 只是“实时看板”，无法承担中央枢纽。

---

## P1.1 中心数据库模型

### 目标

把 Station 结果摘要、健康快照、在线状态变更正式写入 Studio 中心数据库。

### 建议实体

```text
StationNode
StationSession
StationResultSummary
StationHealthSnapshot
StationConnectionEvent
StationAlarmEvent
StationCommandRecord
StationSyncCursor
```

### `StationNode`

```text
Id
StationId
StationName
LineName
StationRole
MachineName
IpAddressHint
FirstSeenAtUtc
LastSeenAtUtc
LastHeartbeatAtUtc
OnlineState
RuntimeState
CurrentPackageId
CurrentPackageVersion
IsEnabled
Remark
```

### `StationResultSummary`

必须做唯一约束：

```text
StationId + SequenceId
```

字段：

```text
Id
StationId
SequenceId
MessageId
RunId
PackageId
PackageName
PackageVersion
FlowHash
ImageId
Outcome
InspectionStatus
ExecutionTimeMs
DiagnosticCode
DiagnosticMessage
PrimaryOutputsPreviewJson
StartedAtUtc
CompletedAtUtc
ReceivedAtUtc
```

### `StationHealthSnapshot`

```text
Id
StationId
SequenceId
RuntimeState
CpuUsagePercent
WorkingSetMb
PrivateMemoryMb
DiskFreeMb
DiskTotalMb
SpoolPendingCount
SpoolBytes
CameraStatusSummary
PlcStatusSummary
CurrentPackageId
CurrentPackageHealth
LastErrorCode
LastErrorMessage
CreatedAtUtc
ReceivedAtUtc
```

### 写入策略

- SQLite 先够用。
- 开启 WAL。
- 按 StationId + CompletedAtUtc 建索引。
- ResultSummary 写入必须幂等。
- HealthSnapshot 可降采样存储，比如 15 秒一条。
- 不存图片。
- 不存原始 `RuntimeNormalizedResult` 大对象。

### 验收标准

- 断线补推不会造成重复统计。
- Studio 重启后仍能看到历史 Station 列表和最近结果。
- 查询今日、最近 1 小时、最近 100 条结果可用。
- SQLite 文件不会因高频健康快照快速膨胀。

---

## P1.2 ACK 水位与幂等补推

### 目标

让 Station 知道哪些结果已经被 Studio 接收并持久化，避免重复上报和重复入库。

### ACK 模型

Studio 每次接收结果后返回：

```text
StationId
AcceptedSequenceId
LastPersistedSequenceId
Duplicate
Message
```

Station 只清理：

```text
sequenceId <= LastPersistedSequenceId
```

### 处理规则

| 场景 | Studio 处理 |
|---|---|
| 新 SequenceId | 写入数据库，返回 ACK |
| 重复 SequenceId | 不重复写，返回 Duplicate=true |
| SequenceId 跳号 | 先接受，记录 Gap |
| 老 SequenceId | 忽略写入，返回当前水位 |
| StationId 未注册 | 要求重新注册 |

### 验收标准

- 同一条结果重复发送 10 次，数据库只保留 1 条。
- Station 断网恢复后，不重复清理未 ACK 的 spool。
- Studio 重启后，能从数据库恢复每个 Station 的最新水位。
- 人为删除部分 spool 不会导致 Station 崩溃。

---

## P1.3 健康监控扩展

### 目标

心跳不只判断“活着”，还要判断“能不能稳定检测”。

### 采集指标

Station 端低频采集：

```text
CPU 使用率
内存占用
磁盘剩余空间
Station 进程运行时长
当前运行包状态
RuntimeHost 状态
相机连接状态
PLC 连接状态
本地结果写入状态
同步队列积压数量
spool 文件大小
最近一次检测时间
最近一次错误代码
```

### 状态分级

```text
Online
Warning
Degraded
Critical
Offline
Unknown
```

### 告警规则首版

```text
磁盘剩余 < 10%：Warning
磁盘剩余 < 5%：Critical
SpoolPendingCount > 1000：Warning
SpoolPendingCount > 10000：Critical
连续心跳丢失超过阈值：Offline
RuntimeState=Faulted：Critical
CameraStatusSummary 包含 Disconnected：Critical
连续 Error 超过阈值：Warning/Critical
```

### 验收标准

- 拔掉网络：Studio 标记 Offline。
- 模拟磁盘空间不足：Studio 产生 Warning。
- 模拟相机断开：Studio 产生 Critical。
- 告警不会每秒刷屏，同类告警需要合并或节流。

---

## P1.4 基础统计报表

### 目标

让中央 Studio 能做跨 Station、跨时段的基础统计。

### 首版统计

```text
今日总检测量
今日 OK / NG / Error
按 Station 分组良率
按小时趋势
按 DiagnosticCode 分布
平均耗时趋势
Top N 异常 Station
Top N 异常诊断码
```

### 时间规则

所有存储使用 UTC：

```text
CreatedAtUtc
StartedAtUtc
CompletedAtUtc
ReceivedAtUtc
```

展示时转本地时间。

预留班次配置：

```text
ShiftName
StartTime
EndTime
Timezone
```

首版可以先不用复杂班次，但数据库和查询层不要写死自然日。

### 验收标准

- 页面可以切换：今天 / 本周 / 本月 / 自定义。
- 多 Station 结果能合并统计。
- 统计不会因为重复补推翻倍。
- 时区转换正确。

---

# P2：命令通道 + 运行包远程下发

运行包远程下发是运维刚需，但不要在 P0 第一轮硬上。必须建立在 ACK、命令状态机、包校验、回滚机制基础上。

---

## P2.1 Command 状态机

### 目标

让 Studio 可以向 Station 下发命令，并清楚知道命令生命周期。

### 命令状态

```text
Created
Delivered
Accepted
Rejected
Running
Succeeded
Failed
TimedOut
Cancelled
```

### `StationCommandDto`

```text
SchemaVersion
CommandId
StationId
CommandType
PayloadJson
CreatedAtUtc
ExpiresAtUtc
IssuedBy
CorrelationId
```

### `StationCommandResultDto`

```text
SchemaVersion
CommandId
StationId
Status
ProgressPercent
Message
ErrorCode
ErrorDetail
StartedAtUtc
CompletedAtUtc
ReportedAtUtc
```

### 命令投递方式

推荐首版用 Station 主动拉取：

```text
Station Heartbeat / PollCommand
  -> Studio 返回待执行命令
  -> Station Accepted
  -> Station Running
  -> Station Succeeded/Failed
```

不要依赖 Studio 主动实时调用 Station，因为 Station 不暴露 HTTP 服务。

### 首版命令

```text
Ping
StartRuntime
StopRuntime
ReloadPackage
DeployPackage
CollectLogs
```

`DeployPackage` 可以 P2.2 再真正实现，P2.1 先把状态机跑通。

### 验收标准

- Studio 创建 Ping 命令，Station 执行并回传 Succeeded。
- 命令超时后 Studio 标记 TimedOut。
- Station 拒绝非法命令时，Studio 显示 Rejected 和原因。
- 所有命令写入 `StationCommandRecord`。
- 不允许无记录的远程操作。

---

## P2.2 `.cvpkg` 运行包格式

### 目标

把 Studio 导出的运行包标准化为单个可传输、可校验、可回滚的包。

### 包结构

```text
xxx.cvpkg
  manifest.json
  package/
    flow.json
    site-profile.json
    operators/
    assets/
```

### `manifest.json`

```json
{
  "schemaVersion": 1,
  "packageId": "pkg_xxx",
  "packageName": "BottleLabelInspection",
  "packageVersion": "1.0.0",
  "flowHash": "sha256:...",
  "createdAtUtc": "2026-05-04T00:00:00Z",
  "createdBy": "Studio",
  "minStationVersion": "1.0.0",
  "requiredOperators": [],
  "sizeBytes": 123456,
  "sha256": "..."
}
```

### 验收标准

- Studio 可生成 `.cvpkg`。
- `.cvpkg` 可解压。
- hash 校验正确。
- manifest 缺失或损坏时 Station 拒绝加载。

---

## P2.3 运行包远程下发流程

### 推荐流程

```text
1. Studio 生成 .cvpkg
2. Studio 存入中心 package store
3. Studio 创建 DeployPackageCommand
4. Station 拉取命令
5. Station 通过 HTTP 从 Studio 下载 .cvpkg
6. Station 校验 sha256
7. Station 解压到 staging
8. Station 检查 minStationVersion / requiredOperators
9. Station 停止或等待 Runtime 空闲
10. Station 切换 active package
11. Station ReloadPackage
12. Station 回传 Succeeded
13. 失败则回滚 last-known-good
```

### Station 包目录

```text
%LocalAppData%\ClearVisionStation\packages\
  active\
  staging\
  archive\
  last-known-good\
```

### 切换策略

禁止直接覆盖 active。必须：

```text
download -> staging -> validate -> atomic switch -> active
```

### 回滚策略

```text
active 切换前复制到 last-known-good
新包加载失败则恢复 last-known-good
连续失败次数写入本地状态
```

### Studio Package Store

建议目录：

```text
%LocalAppData%\ClearVisionStudio\packages\
  package-index.db
  files\
    {packageId}\xxx.cvpkg
```

### 验收标准

- 单 Station 远程部署成功。
- 部署时网络中断，Station 不破坏当前 active package。
- hash 不匹配时拒绝加载。
- 新包加载失败时自动回滚。
- Studio 能看到部署进度和最终结果。
- Station 正在检测时，不直接粗暴覆盖运行包。

---

## P2.4 远程参数下发

### 目标

远程修改 Station 的 SiteProfile / RuntimeSiteProfile，但要比运行包更谨慎。

### 首版策略

- 只允许下发完整 profile 文件，不做局部 patch。
- 下发前生成 profile hash。
- Station 先保存到 staging。
- 校验后应用。
- 应用失败回滚。

### 验收标准

- Studio 修改参数后，Station 应用成功并回报版本。
- 参数损坏时 Station 拒绝应用。
- 参数应用失败不影响当前可用配置。

---

# P3：日志集中与远程诊断

日志集中很重要，但不要第一版直接写 Serilog Sink 实时狂推。要做异步、节流、摘要化。

---

## P3.1 WARN/ERROR 日志摘要上报

### 目标

让 Studio 能看到各 Station 的关键错误，而不用远程桌面登录工控机。

### 不推荐

```text
Serilog Sink 直接 SignalR 推全量日志
```

### 推荐

```text
Serilog 本地文件照旧完整记录
LogRelayService 只采集 WARN/ERROR/FATAL 摘要
Bounded Channel
限流、去重、合并
异步上报 Studio
```

### `StationLogSummaryDto`

```text
SchemaVersion
StationId
SequenceId
TimestampUtc
Level
Source
EventId
MessageTemplate
RenderedMessage
ExceptionType
ExceptionMessage
CorrelationId
RunId
PackageId
```

### 限流规则

```text
同类错误 60 秒内合并
每分钟最多上报 N 条
异常堆栈默认截断
超长消息截断
```

### 验收标准

- Station 产生 ERROR 后，Studio 能看到日志摘要。
- 网络断开时不影响本地日志。
- 高频错误不会刷爆 Studio。
- 日志上报失败不影响检测。

---

## P3.2 按需收集日志包

### 目标

需要诊断时，Studio 下发命令，让 Station 打包最近日志并回传索引或上传到中心。

### 命令

```text
CollectLogs
```

### 参数

```json
{
  "fromUtc": "...",
  "toUtc": "...",
  "maxBytes": 104857600,
  "includeRuntimeLogs": true,
  "includeStationLogs": true,
  "includeWindowsEventHint": false
}
```

### 当前阶段传输建议

由于你不希望 Station 新开 HTTP 服务，采用：

```text
Studio 下发 CollectLogsCommand
Station 打包 zip
Station 主动上传到 Studio 的日志接收 API
Studio 存储日志包索引
```

注意：这不是图片传输，不走实时链路，仅用于诊断。若仍想完全避免 HTTP 上传，也可以先只实现“Station 本地生成日志包并返回路径”，由现场人员手动取。

### 验收标准

- Studio 能下发日志收集命令。
- Station 能生成 zip。
- 生成失败能回报原因。
- 日志包大小受限。
- 不影响检测线程。

---

## P3.3 诊断详情页

### 页面内容

```text
Station 基本信息
当前运行包
最近健康快照
最近 100 条日志摘要
最近 100 条结果摘要
最近命令记录
当前告警
spool 积压情况
```

### 验收标准

- 点击某个 Station 可进入详情。
- 能按时间、级别、关键字筛日志摘要。
- 能看到该 Station 最近命令执行历史。
- 页面不依赖图片。

---

# P4：站点拓扑、安全审计、仿真压测

P4 是让系统从“能跑”变成“能运维”的关键。

---

## P4.1 站点身份与拓扑管理

### 目标

避免中心界面里只显示 GUID，让现场人员能按产线和工位理解系统。

### Station 技术身份

```text
StationId
MachineName
ProcessId
MacAddressHash
StationVersion
```

### Station 业务身份

```text
StationName
LineName
AreaName
WorkcellName
InspectionNodeName
CameraAlias
StationRole
Owner
Remark
```

### 首次接入流程

```text
1. Station 首次注册
2. Studio 显示为“未命名站点”
3. 管理员在 Studio 中绑定产线/工位/角色
4. 保存到 StationNode
5. 后续 UI 优先显示业务名称
```

### 验收标准

- 新 Station 首次接入可识别。
- 可在 Studio 修改 StationName / LineName / Role。
- 修改后看板显示业务名称。
- StationId 不变，业务名称可变。

---

## P4.2 权限与审计

### 目标

区分“查看”和“远程操作”，避免现场误操作。

### 权限建议

```text
ViewStationStatus
ViewStationResults
ViewStationLogs
ManageStationProfile
DeployRuntimePackage
StartStopRuntime
CollectStationLogs
ManageStationIdentity
```

### 审计字段

```text
AuditId
UserId
UserName
Action
TargetStationId
CommandId
PayloadSummary
CreatedAtUtc
Result
ClientIp
```

### 必须审计的操作

```text
DeployPackage
StartRuntime
StopRuntime
ReloadPackage
ApplySiteProfile
CollectLogs
修改 StationName / LineName
修改 token / 安全配置
```

### 验收标准

- 所有远程命令都有审计记录。
- 审计记录可查询。
- 没权限的用户不能下发高风险命令。
- 审计中不保存明文 token。

---

## P4.3 Station Simulator

### 目标

给 GPT-5.5 和后续测试提供可重复压测工具，不依赖真实工控机。

### 建议项目

```text
ClearVision.Product/src/ClearVision.Product.Station.Simulator/
```

### 功能

```text
模拟 1 / 10 / 50 个 Station
随机注册
固定频率心跳
固定频率结果上报
随机 OK/NG/Error
随机断线重连
随机 spool 补推
随机健康异常
随机命令执行成功/失败
```

### CLI 参数

```text
--studio http://127.0.0.1:5000
--stations 20
--rate 5
--ng-rate 0.05
--error-rate 0.01
--disconnect-rate 0.02
--duration 01:00:00
```

### 验收标准

- 可模拟 10 个 Station，每站 2～10 条/秒，运行 1 小时。
- Studio 内存不持续上涨。
- SQLite 不重复写。
- UI 不明显卡顿。
- 断线重连后结果补推正常。

---

## P4.4 保留策略与清理任务

### 目标

防止中心数据库、Station spool、日志包无限增长。

### 清理策略

Station 端：

```text
spool 已 ACK 结果定期清理
spool 超过 MaxSpoolMb 后进入保护模式
旧运行包 archive 保留最近 N 个
日志保留最近 N 天
```

Studio 端：

```text
ResultSummary 保留 90 天
HealthSnapshot 保留 30 天
LogSummary 保留 30 天
CommandRecord 保留 180 天
AuditRecord 保留 1 年
LogBundle 保留 30 天
RuntimePackage 可手动归档
```

### 验收标准

- 长时间运行不会无限占用磁盘。
- 清理任务有日志。
- 清理不会删除未 ACK 的 spool。
- 清理策略可配置。

---

# 2. 明确暂不做事项

为了让第一轮落地足够快、足够稳，以下事项明确暂不做。

## 2.1 暂不传图片

包括：

```text
不传原图
不传结果图
不传缩略图
不传 base64
不通过 SignalR 传 byte[]
不在 Station 端新增 HTTP Server 供 Studio 拉图
```

原因：

```text
图片大，会拖累低端 IPC
图片传输会放大网络抖动风险
Station 新开 HTTP 服务会增加安全面和维护成本
首轮中央枢纽先解决“看状态、看结果摘要、看统计、能运维”
```

后续若要做图片，必须另起专题，不并入当前主线。

## 2.2 暂不引入 MQTT

原因：

```text
当前规模不需要额外 Broker
SignalR 与现有 ASP.NET Core/Kestrel 更贴合
Broker 增加部署、运维和故障点
```

## 2.3 暂不做 Studio-to-Studio

当前真正的边缘节点是 Station，不是第二批 Studio。先跑稳：

```text
中心 Studio -> 多 Station
```

再考虑：

```text
多中心 Studio 联邦
```

## 2.4 暂不做复杂数据仓库

首版用 SQLite + 轻量实体足够。不要一上来引入：

```text
时序数据库
OLAP
Kafka
ElasticSearch
大型报表平台
```

---

# 3. 推荐 PR 拆分

## PR-01：共享协议与命名整理

### 内容

- 新增 `StationSync` DTO。
- 新增枚举。
- 新增序列化测试。
- 文档统一 Studio/Station 命名。

### 验收

- 编译通过。
- DTO 测试通过。
- 无运行行为变化。

---

## PR-02：Studio StationIngressHub

### 内容

- Desktop 新增 SignalR Hub。
- 新增 StationRegistryService。
- 新增 StationMonitor REST/SSE。
- LAN 模式显式开关。
- token 校验。

### 验收

- Fake Client 可注册。
- REST 可查询在线站点。
- 默认 loopback 行为不变。

---

## PR-03：Station SyncHostedService

### 内容

- Station 新增 SignalR Client。
- HostedService 订阅 RuntimeHost 事件。
- 心跳上报。
- 结果摘要上报。
- 本地 spool。
- 自动重连。

### 验收

- Studio 离线不影响 Station。
- 断线期间结果落 spool。
- 恢复后补推。
- 不传图片。

---

## PR-04：Studio 监控页面

### 内容

- 新增 Station 监控页。
- 顶部总览卡。
- 站点矩阵。
- 最近结果流。
- REST 初始加载 + SSE 增量刷新。

### 验收

- 多站点状态实时变化。
- 页面不卡顿。
- 与现有结果页风格统一。

---

## PR-05：中心数据库持久化

### 内容

- 新增 StationNode / StationResultSummary / StationHealthSnapshot 等实体。
- 写入幂等。
- ACK 水位。
- 查询接口。
- 基础统计。

### 验收

- 重复补推不重复入库。
- Studio 重启后恢复历史状态。
- 今日统计正确。

---

## PR-06：健康监控与基础告警

### 内容

- StationHealthCollector。
- CPU/内存/磁盘/spool/Runtime/Camera/PLC 摘要。
- Studio 告警规则。
- 告警列表和状态灯。

### 验收

- 模拟故障能产生告警。
- 告警节流。
- 告警恢复可识别。

---

## PR-07：Command 状态机

### 内容

- StationCommandRecord。
- 命令创建、拉取、执行、回报。
- Ping / Start / Stop / Reload 占位实现。
- 命令审计初版。

### 验收

- Ping 闭环成功。
- 超时、失败、拒绝均可追踪。
- 命令状态在 UI 可见。

---

## PR-08：运行包远程下发

### 内容

- `.cvpkg` 打包。
- package store。
- DeployPackageCommand。
- Station 主动下载。
- hash 校验。
- staging / active / last-known-good。
- 回滚。

### 验收

- 成功部署。
- hash 错误拒绝。
- 加载失败回滚。
- 断网不中断当前运行包。

---

## PR-09：日志摘要与诊断页

### 内容

- LogRelayService。
- WARN/ERROR 摘要上报。
- 日志限流。
- Station 诊断页。
- CollectLogs 命令初版。

### 验收

- 错误日志可在 Studio 查看。
- 高频错误不刷爆。
- 可生成日志包或返回本地日志包路径。

---

## PR-10：Station Simulator 与压测

### 内容

- 新增 Simulator 项目。
- 模拟多 Station。
- 模拟断线、补推、异常。
- 压测脚本。

### 验收

- 10 个 Station，每站 2～10 条/秒，连续 1 小时。
- Studio UI 不明显卡顿。
- 内存不持续上涨。
- 数据不重复。

---

# 4. GPT-5.5 执行约束

把下面这段直接作为开发提示词的硬约束。

```text
你正在为 ClearVision 实现 Studio-Station 工业局域网通讯与中央运维能力。

硬约束：
1. 每次只做一个 PR 粒度，不要跨阶段大改。
2. 默认配置下不得改变现有 Studio 和 Station 的本地使用行为。
3. Station 必须本地自治，Studio 离线不得影响检测。
4. 首轮禁止传图片，禁止新增 Station HTTP Server。
5. 跨网结果只传 StationResultSummaryDto，不直接传 RuntimeNormalizedResult。
6. 所有消息必须带 StationId、SequenceId、MessageId、CreatedAtUtc。
7. 所有结果写入必须幂等。
8. 所有远程命令必须进入 Command 状态机和审计记录。
9. 所有队列必须 bounded，不能无限内存增长。
10. 所有网络异常必须被捕获并降级为日志/状态，不得抛回检测主流程。
11. LAN 监听必须显式开启，不能默认开放。
12. 每个 PR 必须提供最小测试或最小模拟客户端。
```

---

# 5. 第一轮最小闭环

最小闭环只做这一条链：

```text
Station 检测完成
  -> RuntimeHost.ResultAvailable
  -> StationResultSummaryDto
  -> 本地 bounded queue
  -> SignalR PushResult
  -> Studio StationIngressHub
  -> StationRegistryService
  -> StationTelemetryBuffer / DB
  -> Studio REST + SSE
  -> 监控页面刷新
```

只要这条链跑稳，整个方案就成立。

第一轮不要碰：

```text
图片
MQTT
多中心
复杂报表
远程发包
全量日志
```

---

# 6. 第二轮核心闭环

第二轮再做：

```text
Studio 选择运行包
  -> 生成 .cvpkg
  -> 创建 DeployPackageCommand
  -> Station 拉取命令
  -> Station 下载 .cvpkg
  -> 校验 hash
  -> staging
  -> 切换 active
  -> ReloadPackage
  -> 成功/失败/回滚
  -> Studio 显示部署结果
```

这条链打通后，现场手动拷包的痛点基本消除。

---

# 7. 最终验收清单

## 工业现场验收

- [ ] 一台中心 Studio 能同时监控多台 Station。
- [ ] Station 离线、上线、重连状态准确。
- [ ] Station 检测摘要秒级出现在 Studio 看板。
- [ ] Studio 关闭时 Station 继续检测。
- [ ] Studio 重启后 Station 自动恢复连接。
- [ ] 断网期间结果不会丢，恢复后补推。
- [ ] 重复补推不会导致 OK/NG 统计翻倍。
- [ ] 中心能查看今日/本周/本月基础统计。
- [ ] 中心能看到磁盘、CPU、内存、相机/PLC 摘要健康状态。
- [ ] 中心能远程下发运行包并看到部署结果。
- [ ] 运行包部署失败可回滚。
- [ ] 中心能查看 Station WARN/ERROR 摘要。
- [ ] 长时间运行不会导致内存、磁盘无限增长。

## 工程质量验收

- [ ] 所有新增配置都有默认值。
- [ ] 所有网络模块都有开关。
- [ ] 所有 DTO 都有 SchemaVersion。
- [ ] 所有结果都有 SequenceId。
- [ ] 所有命令都有 CommandId。
- [ ] 所有远程高风险操作都有审计。
- [ ] 所有队列 bounded。
- [ ] 所有异常可降级。
- [ ] 所有 PR 可独立编译。
- [ ] 所有 PR 有最小测试或模拟器验证。

---

# 8. 一句话总结

先把 ClearVision 做成：

```text
Station 自治检测，Studio 中央观测；
摘要实时上报，结果集中入库；
命令可追踪，运行包可远程下发；
异常可诊断，失败可回滚；
图片暂不传，低端工控机不被拖垮。
```

这就是当前阶段最贴合你项目实际、最适合交给 GPT-5.5 快速执行的后续路线。
