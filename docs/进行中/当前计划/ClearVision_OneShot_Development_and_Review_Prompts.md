# ClearVision Studio-Station 一次性长任务开发指令与 Review Prompts

> 用途：直接交给 GPT-5.5 / Codex 执行一轮长任务，先把 Studio-Station 中央枢纽能力做出可运行初稿，再进入多轮 Review 与修复。  
> 核心策略：**一轮跑完整体闭环，后续再逐项严审**。  
> 硬约束：**暂不传图片；不新增 Station HTTP Server；Station 必须本地自治；所有远程能力默认关闭或显式开启。**

---

## 0. 任务总目标

请在 ClearVision 项目中一次性实现 Studio 与多个 Runtime Station 的工业局域网通信初稿，使系统具备以下能力：

```text
Station 自治检测
  -> 本地结果继续记录
  -> 检测摘要进入同步队列
  -> SignalR 主动连接中心 Studio
  -> 上报注册、心跳、健康状态、检测摘要、日志摘要
  -> 网络异常时本地 spool
  -> 网络恢复后补推

Studio 中央枢纽
  -> 接收多 Station 注册
  -> 维护在线/离线/异常状态
  -> 接收结果摘要并幂等持久化
  -> 展示中央监控页面
  -> 统计 OK/NG/Error/良率/耗时/告警
  -> 提供命令通道
  -> 支持运行包远程下发初稿
  -> 支持健康监控、日志摘要、站点管理、基础审计
```

本轮目标不是“最终工业级完美版本”，而是做出一版**可编译、可运行、可演示、可 Review、可继续加固**的完整初稿。

---

## 1. 总体边界

### 1.1 本轮必须做

本轮一次性覆盖以下内容：

1. 共享协议 DTO 与枚举。
2. Studio Station Ingress Hub。
3. Station SignalR 客户端。
4. Station 本地 spool 与断线补推。
5. Studio 中心化持久化。
6. ACK 水位与幂等写入。
7. Station 健康监控。
8. Studio 中央监控页面。
9. 命令状态机。
10. 运行包远程下发初稿。
11. 日志摘要上报。
12. 站点身份/拓扑管理初稿。
13. 审计记录初稿。
14. Station Simulator 初稿。
15. 基础测试、构建脚本、验证说明。

### 1.2 本轮明确不做

以下事项本轮禁止做：

```text
不传原图
不传结果图
不传缩略图
不传 base64 图片
不通过 SignalR 传 byte[]
不新增 Station HTTP Server
不做 Studio 主动拉 Station 文件
不引入 MQTT Broker
不做 Studio-to-Studio 联邦
不引入 Kafka / ElasticSearch / 时序数据库 / 大型报表平台
不破坏现有 Studio 单机使用方式
不破坏现有 Station 单机使用方式
```

### 1.3 文件传输边界

本轮允许做的文件传输只有两类：

```text
运行包：Station 主动从 Studio 下载 .cvpkg
日志包：可先只生成本地 zip 并回传路径；如已有合适 Studio API，可由 Station 主动上传日志包
```

图片传输整体延后，不要为图片预留复杂接口，不要引入图片 artifact 传输。

---

## 2. 执行方式

虽然这是一次性长任务，但执行时必须按内部里程碑推进。每完成一个里程碑就：

```text
1. 构建
2. 修复编译错误
3. 做最小自测
4. 记录完成状态
5. 再进入下一里程碑
```

不要等全部写完后才构建。

### 推荐内部里程碑

```text
Milestone A：共享协议与配置
Milestone B：Studio Hub + Registry + REST/SSE
Milestone C：Station Client + Queue + Spool
Milestone D：中心数据库 + ACK + 统计
Milestone E：健康监控 + 告警
Milestone F：中央监控 UI
Milestone G：命令状态机
Milestone H：运行包远程下发
Milestone I：日志摘要 + 诊断页
Milestone J：站点身份 + 审计
Milestone K：Station Simulator + 压测脚本
Milestone L：总体验证与文档
```

---

## 3. GPT-5.5 主开发 Prompt

下面这段可以直接复制给 GPT-5.5 / Codex 作为主任务提示词。

```text
你正在 ClearVision 仓库中实现 Studio-Station 工业局域网中央枢纽能力。请先完整阅读项目结构，尤其关注 Desktop、Station、Runtime、Runtime.Abstractions、RuntimeHost、RuntimeNormalizedResult、RuntimeHostSnapshot、StationLocalSettingsStore、InspectionEventEndpoints、Program.cs、现有 WebView2/REST/SSE/EF Core/SQLite 结构。

目标：一次性做出可运行初稿，而不是只做通信 MVP。实现范围包括：
1. 共享 StationSync DTO 与枚举；
2. Studio 端 Station Ingress SignalR Hub；
3. Station 端 SignalR Client HostedService；
4. Station 注册、心跳、健康快照、检测摘要上报；
5. 本地 bounded queue、spool、断线补推；
6. Studio 端中心化持久化、ACK 水位、幂等写入；
7. Studio 站点在线状态、健康状态、告警状态；
8. Studio 中央监控页面；
9. 命令状态机；
10. 运行包远程下发初稿；
11. WARN/ERROR 日志摘要上报；
12. 站点身份管理初稿；
13. 操作审计初稿；
14. Station Simulator；
15. 最小测试与验证文档。

硬约束：
- 暂不传图片。
- 不传原图、结果图、缩略图、base64，不通过 SignalR 传 byte[]。
- 不新增 Station HTTP Server。
- Station 必须主动连接 Studio。
- Station 必须本地自治，Studio 离线不得影响检测。
- 默认配置下现有 Studio 和 Station 行为不能改变。
- LAN 监听必须显式开启。
- 所有网络异常不得抛回 RuntimeHost 检测主流程。
- 所有跨网结果必须是 StationResultSummaryDto，不要直接把 RuntimeNormalizedResult 原样跨网传输。
- 所有结果消息必须带 StationId、SequenceId、MessageId、CreatedAtUtc。
- 所有数据库写入必须幂等。
- 所有队列必须 bounded。
- 所有远程命令必须进入 Command 状态机。
- 所有高风险操作必须写审计。
- 所有新配置必须有默认值。
- 每个内部里程碑完成后都要构建并修复编译错误。

优先采用现有项目风格。不要做大而空的重构。不要把网络逻辑塞进 MainForm。Station 同步逻辑应放入独立 HostedService。Studio UI 仍优先复用现有 REST + SSE 模式，机器到机器通信使用 SignalR。

请按以下内部里程碑执行：
A. 共享协议与配置
B. Studio Hub + Registry + REST/SSE
C. Station Client + Queue + Spool
D. 中心数据库 + ACK + 统计
E. 健康监控 + 告警
F. 中央监控 UI
G. 命令状态机
H. 运行包远程下发
I. 日志摘要 + 诊断页
J. 站点身份 + 审计
K. Station Simulator + 压测脚本
L. 总体验证与文档

完成后输出：
1. 修改文件清单；
2. 新增配置说明；
3. 运行方式；
4. 验证步骤；
5. 已知风险；
6. 后续 Review 重点；
7. dotnet build/test 结果。
```

---

## 4. 详细实现 TODO

---

# Milestone A：共享协议与配置

## A1. 新增 StationSync 目录

建议位置：

```text
Acme.Product/src/Acme.Product.Runtime.Abstractions/StationSync/
```

新增文件：

```text
StationSyncConstants.cs
StationSyncEnums.cs
StationRegistrationDto.cs
StationRegisterAckDto.cs
StationHeartbeatDto.cs
StationHealthSnapshotDto.cs
StationResultSummaryDto.cs
StationAckDto.cs
StationReplayCursorDto.cs
StationCommandDto.cs
StationCommandResultDto.cs
StationLogSummaryDto.cs
StationAlarmDto.cs
StationPackageManifestDto.cs
StationAuditDto.cs
```

## A2. DTO 通用字段

所有跨网 DTO 必须包含：

```csharp
public int SchemaVersion { get; init; } = 1;
public string StationId { get; init; } = "";
public DateTimeOffset CreatedAtUtc { get; init; }
```

结果、健康、日志类 DTO 还必须包含：

```csharp
public long SequenceId { get; init; }
public string MessageId { get; init; } = "";
```

## A3. 枚举

至少定义：

```text
StationOnlineState
  Unknown
  Online
  Warning
  Degraded
  Critical
  Offline

StationRuntimeState
  Unknown
  Idle
  Running
  Paused
  LoadingPackage
  Faulted
  Stopping

StationResultOutcome
  Unknown
  Ok
  Ng
  Error

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

StationPackageState
  Unknown
  Available
  Downloading
  Staged
  Active
  Failed
  RolledBack
```

## A4. 结果摘要 DTO

`StationResultSummaryDto` 字段建议：

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

`PrimaryOutputsPreview` 使用：

```csharp
Dictionary<string, string?>
```

禁止使用：

```csharp
Dictionary<string, object?>
byte[]
Image
Bitmap
Base64
```

## A5. 配置 Options

新增共享或两端各自 options：

```text
StationIngressOptions
StationSyncOptions
StationPackageDeployOptions
StationRetentionOptions
StationSecurityOptions
```

默认值必须保守：

```text
StationIngress.Enabled = false
StationIngress.ListenMode = Loopback
StationSync.Enabled = false
图片传输相关配置不存在或显式 false
```

## A6. 验收

```text
dotnet build 成功
DTO 序列化/反序列化测试通过
Runtime.Abstractions 不引用 Desktop/Station 具体实现
DTO 中没有图片字段
```

---

# Milestone B：Studio Hub + Registry + REST/SSE

## B1. Studio 依赖

Desktop 项目增加：

```text
Microsoft.AspNetCore.SignalR.Protocols.MessagePack
```

如果项目已有 ASP.NET Core FrameworkReference，则优先沿用。

## B2. 新增目录

```text
Acme.Product/src/Acme.Product.Desktop/Hubs/
  StationIngressHub.cs

Acme.Product/src/Acme.Product.Desktop/Services/StationSync/
  StationIngressAuthService.cs
  StationRegistryService.cs
  StationTelemetryBuffer.cs
  StationEventBus.cs
  StationOfflineMonitorService.cs
  StationCommandQueue.cs

Acme.Product/src/Acme.Product.Desktop/Endpoints/
  StationMonitorEndpoints.cs
  StationMonitorEventEndpoints.cs
```

## B3. Hub 方法

`StationIngressHub` 至少提供：

```csharp
Task<StationRegisterAckDto> RegisterStation(StationRegistrationDto dto);
Task<StationAckDto> Heartbeat(StationHeartbeatDto dto);
Task<StationAckDto> PushHealth(StationHealthSnapshotDto dto);
Task<StationAckDto> PushResult(StationResultSummaryDto dto);
Task<StationAckDto> PushLog(StationLogSummaryDto dto);
Task<StationReplayCursorDto> GetReplayCursor(string stationId);
Task<StationCommandDto?> PollCommand(string stationId);
Task ReportCommandResult(StationCommandResultDto dto);
```

## B4. 鉴权

首版采用共享 token：

```text
Station 请求 Header：X-ClearVision-Station-Token
Studio 校验 StationIngress:SharedToken
```

要求：

```text
SharedToken 为空时，LAN 模式不允许启动，除非配置 AllowInsecureDevelopment=true
token 不写入普通日志
鉴权失败记录安全日志
```

## B5. Kestrel 监听

保留现有 loopback 默认行为。

新增配置：

```json
{
  "StationIngress": {
    "Enabled": false,
    "ListenMode": "Loopback",
    "Port": 5000,
    "SharedToken": "",
    "OfflineThresholdSeconds": 15,
    "ResultBufferPerStation": 200,
    "AllowInsecureDevelopment": false
  }
}
```

`ListenMode`：

```text
Loopback：ListenLocalhost
Lan：ListenAnyIP 或指定 IP
```

## B6. Registry

`StationRegistryService` 维护中心内存状态：

```text
StationId
StationName
LineName
AreaName
WorkcellName
StationRole
MachineName
IpAddressHint
OnlineState
RuntimeState
CurrentPackageId
CurrentPackageVersion
LastHeartbeatAtUtc
LastResultAtUtc
SessionOkCount
SessionNgCount
SessionErrorCount
SpoolPendingCount
RecentResults
RecentHealth
RecentLogs
LastError
```

## B7. REST 接口

新增：

```text
GET /api/stations
GET /api/stations/summary
GET /api/stations/{stationId}
GET /api/stations/{stationId}/results?take=100
GET /api/stations/{stationId}/health?take=100
GET /api/stations/{stationId}/logs?take=100
GET /api/stations/{stationId}/commands?take=100
GET /api/stations/events
```

`/api/stations/events` 使用 SSE，复用现有 Inspection SSE 的写法和心跳策略。

## B8. 验收

```text
默认启动行为不变
开启 StationIngress 后 Hub 可用
Fake client 可注册、心跳、推结果
REST 能查到 Station
SSE 能收到 Station 事件
鉴权失败不能注册
```

---

# Milestone C：Station Client + Queue + Spool

## C1. Station 依赖

Station 项目增加：

```text
Microsoft.AspNetCore.SignalR.Client
Microsoft.AspNetCore.SignalR.Protocols.MessagePack
```

## C2. 新增目录

```text
Acme.Product/src/Acme.Product.Station/Sync/
  StationSyncHostedService.cs
  StationHubClient.cs
  StationSyncOptions.cs
  StationIdentityResolver.cs
  StationOutboundEnvelope.cs
  StationSpoolStore.cs
  StationResultMapper.cs
  StationHealthCollector.cs
  StationLogRelayService.cs
  StationSequenceStore.cs
```

## C3. Station 配置

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
    "SnapshotDebounceMilliseconds": 1000,
    "OutboundQueueCapacity": 1000,
    "LogQueueCapacity": 500
  }
}
```

## C4. HostedService 责任

`StationSyncHostedService`：

```text
读取 StationLocalSettings
确保 StationId 存在
连接 Studio Hub
注册 Station
定时 Heartbeat
定时 Health
订阅 RuntimeHost.ResultAvailable
订阅 RuntimeHost.SnapshotChanged
将结果映射为 StationResultSummaryDto
写入 bounded queue
网络可用时推送
网络不可用时写 spool
重连后从 spool 补推
收到 ACK 后推进水位
```

禁止：

```text
阻塞 RuntimeHost
阻塞 UI
在事件回调中做网络同步等待
让网络异常向外冒泡
```

## C5. Spool

路径：

```text
%LocalAppData%\ClearVisionStation\spool\
```

文件：

```text
results-yyyyMMdd.jsonl
logs-yyyyMMdd.jsonl
cursor.json
```

Envelope：

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

规则：

```text
结果不可直接丢
快照可压缩/覆盖
日志可限流
spool 有容量上限
已 ACK 的结果可清理
未 ACK 的结果不可清理
```

## C6. 验收

```text
Studio 不在线时 Station 正常运行
检测结果写入 spool
Studio 恢复后补推
重复补推不崩溃
Station UI 不明显卡顿
无图片字段上报
```

---

# Milestone D：中心数据库 + ACK + 统计

## D1. 数据实体

在 Studio 端数据库中新增或接入现有 EF Core 模型。实体建议：

```text
StationNode
StationResultSummaryEntity
StationHealthSnapshotEntity
StationConnectionEventEntity
StationAlarmEventEntity
StationCommandRecordEntity
StationSyncCursorEntity
StationLogSummaryEntity
StationAuditRecordEntity
StationPackageRecordEntity
```

如果现有数据上下文不适合直接扩展，可先建立独立轻量 DbContext，但不要引入额外数据库服务。

## D2. 幂等约束

`StationResultSummaryEntity` 必须有唯一约束：

```text
StationId + SequenceId
```

或：

```text
StationId + MessageId
```

优先：

```text
StationId + SequenceId
```

## D3. ACK

Studio 返回：

```text
StationId
AcceptedSequenceId
LastPersistedSequenceId
Duplicate
Message
```

Station 清理规则：

```text
只清理 sequenceId <= LastPersistedSequenceId 的本地 spool
```

## D4. 统计接口

新增或扩展：

```text
GET /api/stations/statistics?range=today
GET /api/stations/statistics?range=week
GET /api/stations/statistics?range=month
GET /api/stations/statistics?from=...&to=...
```

返回：

```text
总检测量
OK
NG
Error
良率
平均耗时
按 Station 分组
按小时趋势
按 DiagnosticCode 分布
Top 异常 Station
```

## D5. 时间规则

```text
存储统一 UTC
展示转本地
Station 上报 LocalOffset
Studio 检测时钟漂移
```

## D6. 验收

```text
重复发送同一条结果 10 次，数据库只写 1 条
Studio 重启后可恢复最新水位
今日统计不因重复补推翻倍
查询性能可接受
SQLite WAL 正常
```

---

# Milestone E：健康监控 + 告警

## E1. 健康采集

Station 端 `StationHealthCollector` 采集：

```text
CPU 使用率
内存占用
磁盘剩余
进程运行时长
RuntimeState
CurrentPackage
spool pending count
spool bytes
最近一次检测时间
最近错误代码
CameraStatusSummary
PlcStatusSummary
```

相机/PLC 如果没有统一状态接口，先做可空摘要或 best-effort，不要强行重构底层设备模块。

## E2. 告警规则

Studio 端首版：

```text
心跳超时 -> Offline
磁盘剩余 < 10% -> Warning
磁盘剩余 < 5% -> Critical
SpoolPendingCount > 1000 -> Warning
SpoolPendingCount > 10000 -> Critical
RuntimeState=Faulted -> Critical
CameraStatusSummary 包含 Disconnected -> Critical
连续 Error 超阈值 -> Warning/Critical
```

## E3. 告警节流

```text
同类告警合并
状态恢复生成恢复事件
不要每秒刷同一条告警
```

## E4. 验收

```text
断开 Station 后 Studio 标记 Offline
模拟磁盘不足产生 Warning/Critical
模拟 Runtime Faulted 产生 Critical
告警不会刷屏
恢复后状态回落
```

---

# Milestone F：中央监控 UI

## F1. 页面入口

新增页面：

```text
Station 监控
```

或在现有“结果”页面增加：

```text
本地结果 / 中央监控
```

## F2. 页面内容

顶部总览：

```text
在线站点数
离线站点数
运行中站点数
总检测量
OK
NG
Error
综合良率
平均耗时
当前告警数
```

站点矩阵：

```text
StationName
LineName
StationRole
OnlineState
RuntimeState
CurrentPackage
Session OK/NG/Error
LastHeartbeat
LastResult
SpoolPendingCount
HealthStatus
```

最近结果流：

```text
时间
Station
ImageId
Outcome
DiagnosticCode
ExecutionTimeMs
PrimaryOutputsPreview
```

健康/告警区：

```text
当前告警
磁盘
CPU
内存
spool
相机/PLC 摘要
```

命令区初稿：

```text
Ping
ReloadPackage
DeployPackage 入口
CollectLogs 入口
```

## F3. 数据方式

```text
首次加载：REST
增量刷新：SSE
```

不要求前端直接接 SignalR。

## F4. 视觉要求

参考现有结果界面：

```text
卡片化
浅色科技风
状态灯
趋势图
筛选器
导出按钮可占位
不预留图片大区域
```

## F5. 验收

```text
多个 Station 同时在线可显示
状态变化可实时刷新
页面刷新后能恢复状态
无 Station 时显示空状态
不因高频上报明显卡顿
```

---

# Milestone G：命令状态机

## G1. 命令实体

`StationCommandRecordEntity`：

```text
CommandId
StationId
CommandType
PayloadJson
Status
CreatedAtUtc
ExpiresAtUtc
DeliveredAtUtc
AcceptedAtUtc
StartedAtUtc
CompletedAtUtc
IssuedBy
CorrelationId
ResultMessage
ErrorCode
ErrorDetail
```

## G2. 命令状态

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

## G3. 投递方式

Station 主动拉取：

```text
Heartbeat 或 PollCommand
  -> Studio 返回一条或多条待执行命令
  -> Station 回报 Accepted/Rejected
  -> Station 执行
  -> Station 回报 Running/Succeeded/Failed
```

不要让 Studio 主动调用 Station。

## G4. 首版命令

```text
Ping：真实实现
ReloadPackage：接本地 reload
StartRuntime：如已有能力则接入，否则占位返回 Rejected/NotSupported
StopRuntime：如已有能力则接入，否则占位返回 Rejected/NotSupported
DeployPackage：Milestone H 实现
CollectLogs：Milestone I 实现
```

## G5. 验收

```text
Ping 命令闭环成功
超时命令标记 TimedOut
非法命令标记 Rejected
所有命令可在 UI 查看状态
所有命令进入审计
```

---

# Milestone H：运行包远程下发

## H1. 原则

运行包远程下发是刚需，但必须安全：

```text
Studio 提供包
Station 主动下载
hash 校验
staging 解压
atomic switch
失败回滚
不覆盖 last-known-good
```

## H2. `.cvpkg` 格式

```text
xxx.cvpkg
  manifest.json
  package/
    flow.json
    site-profile.json
    operators/
    assets/
```

`manifest.json`：

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

## H3. Studio Package Store

```text
%LocalAppData%\ClearVisionStudio\packages\
  package-index.db 或 package-index.json
  files\
    {packageId}\xxx.cvpkg
```

Studio 提供下载接口：

```text
GET /api/station-packages/{packageId}/download
```

注意这是 Station 主动拉 Studio，不是 Studio 推大文件。

## H4. Station 包目录

```text
%LocalAppData%\ClearVisionStation\packages\
  active\
  staging\
  archive\
  last-known-good\
```

## H5. 部署流程

```text
1. Studio 生成 .cvpkg
2. Studio 计算 sha256
3. Studio 写 package store
4. Studio 创建 DeployPackageCommand
5. Station PollCommand 获取命令
6. Station 下载 .cvpkg
7. Station 校验 sha256
8. Station 解压 staging
9. Station 校验 manifest / minStationVersion / requiredOperators
10. Station 等待 Runtime 空闲或按配置停止 Runtime
11. Station 备份当前 active 到 last-known-good
12. Station atomic switch staging -> active
13. Station ReloadPackage
14. 成功则回报 Succeeded
15. 失败则回滚 last-known-good 并回报 Failed
```

## H6. 验收

```text
远程部署成功
hash 错误拒绝
manifest 损坏拒绝
网络中断不破坏 active
新包加载失败自动回滚
Studio 能看到部署状态
```

---

# Milestone I：日志摘要 + 诊断页

## I1. 日志摘要

不要直接写“Serilog Sink 实时狂推全量日志”。

推荐：

```text
本地 Serilog 文件照旧
LogRelayService 采集 WARN/ERROR/FATAL 摘要
bounded channel
限流
去重
异步上报
```

`StationLogSummaryDto`：

```text
StationId
SequenceId
MessageId
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
CreatedAtUtc
```

## I2. 限流

```text
同类错误 60 秒内合并
每分钟最多上报 N 条
异常堆栈截断
超长消息截断
```

## I3. 诊断页

Station 详情页展示：

```text
基础信息
当前运行包
最近健康快照
最近结果摘要
最近日志摘要
最近命令
当前告警
spool 积压
```

## I4. CollectLogs

首版可实现：

```text
Studio 下发 CollectLogsCommand
Station 打包最近日志 zip
如果未实现上传，则返回本地 zip 路径
如果已有中心接收 API，则 Station 主动上传到 Studio
```

## I5. 验收

```text
ERROR 日志能在 Studio 看到
高频错误不会刷爆
日志上报失败不影响检测
CollectLogs 至少能生成本地 zip 并回传路径
```

---

# Milestone J：站点身份 + 审计

## J1. 站点身份

技术身份：

```text
StationId
MachineName
ProcessId
MacAddressHash
StationVersion
RuntimeVersion
```

业务身份：

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

## J2. 首次接入

```text
新 Station 注册
Studio 标记为“未命名站点”
管理员在 UI 修改业务身份
保存到 StationNode
后续 UI 优先显示业务名称
```

## J3. 审计

必须审计：

```text
DeployPackage
StartRuntime
StopRuntime
ReloadPackage
ApplySiteProfile
CollectLogs
修改 StationName / LineName
修改 token / LAN 配置
```

审计字段：

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

## J4. 验收

```text
可编辑 StationName / LineName / Role
命令操作产生审计
审计可查询
token 不进入审计明文
```

---

# Milestone K：Station Simulator + 压测脚本

## K1. 新项目

建议：

```text
Acme.Product/src/Acme.Product.Station.Simulator/
```

如果新增项目成本过高，也可先放在 tools：

```text
tools/ClearVision.StationSimulator/
```

## K2. 功能

```text
模拟 1 / 10 / 50 个 Station
注册
心跳
健康上报
结果上报
日志摘要
随机 OK/NG/Error
随机断线重连
随机 spool 补推
随机健康异常
随机命令成功/失败
```

## K3. CLI 参数

```text
--studio http://127.0.0.1:5000
--stations 10
--rate 5
--ng-rate 0.05
--error-rate 0.01
--disconnect-rate 0.02
--duration 01:00:00
--token xxx
```

## K4. 验收

```text
10 个 Station
每站 2～10 条/秒
连续运行 30～60 分钟
Studio UI 不明显卡顿
SQLite 不重复写
内存不持续上涨
```

---

# Milestone L：总体验证与文档

## L1. 必须运行

```text
dotnet restore
dotnet build
dotnet test
```

如果测试项目不存在或部分失败，必须说明原因，不要假装通过。

## L2. 文档

新增：

```text
docs/station-studio-sync.md
docs/station-deployment.md
docs/station-monitoring.md
```

文档至少包含：

```text
架构说明
配置说明
如何开启 LAN 模式
如何配置 token
如何启动 Station 同步
如何运行 Simulator
如何验证断线补推
如何远程下发运行包
已知限制：暂不传图片
```

## L3. 最终输出

长任务完成后，GPT-5.5 必须输出：

```text
修改文件清单
新增配置项
新增接口
新增数据库实体
运行方式
验证步骤
build/test 结果
已知风险
下一轮 review 建议
```

---

## 5. 最终验收清单

### 5.1 功能验收

```text
[ ] 中心 Studio 可启动，默认仍为本地模式
[ ] 显式开启 LAN 后 Station 可连接
[ ] Station 可注册
[ ] Station 可心跳
[ ] Station 可上报健康状态
[ ] Station 可上报检测摘要
[ ] Studio 可显示在线/离线状态
[ ] Studio 可显示结果摘要流
[ ] Studio 可做基础统计
[ ] Studio 可持久化结果摘要
[ ] 重复结果不重复入库
[ ] Station 断网时继续检测
[ ] Station 断网时写 spool
[ ] Station 恢复网络后补推
[ ] 命令状态机可跑通 Ping
[ ] 运行包下发可跑通成功路径
[ ] 运行包下发失败可回滚
[ ] WARN/ERROR 日志摘要可上报
[ ] Station 详情页可查看健康/日志/命令
[ ] Station Simulator 可模拟多站点
```

### 5.2 稳定性验收

```text
[ ] 网络异常不影响 RuntimeHost
[ ] 网络异常不阻塞 UI
[ ] 所有 queue bounded
[ ] spool 有容量上限
[ ] 告警有节流
[ ] 日志有节流
[ ] Studio 重启后 Station 自动重连
[ ] Station 重启后恢复 StationId
[ ] Studio 重启后恢复水位
[ ] 长时间运行内存不持续上涨
```

### 5.3 安全验收

```text
[ ] LAN 模式显式开启
[ ] SharedToken 必填或开发模式显式开启
[ ] token 不写普通日志
[ ] 高风险命令有审计
[ ] 默认不开放 Station HTTP Server
[ ] 默认不传图片
[ ] UI CORS 不被随意放宽
```

---

# 6. 后续 Review Prompts

下面这些 prompt 用于初稿完成后的多轮 Review。建议每次只让 GPT-5.5 做一个维度的审查，不要一轮让它“全面看看”，否则容易泛泛而谈。

---

## Review Prompt 1：总体架构审查

```text
请对当前 ClearVision Studio-Station 初稿做总体架构审查。

重点检查：
1. 是否仍保持 Station 本地自治；
2. Studio 离线是否不会影响 RuntimeHost 检测；
3. SignalR、REST、SSE、数据库、spool 的边界是否清晰；
4. 是否错误地引入了 Station HTTP Server；
5. 是否错误地实现了图片传输；
6. 是否存在大范围重构导致原有 Studio/Station 单机功能风险；
7. 新增模块命名是否与现有项目风格一致；
8. 依赖方向是否正确，Runtime.Abstractions 是否没有反向依赖 Desktop/Station；
9. 是否存在“为了通信而污染 MainForm / RuntimeHost 核心流程”的问题；
10. 整体方案是否可继续演进到工业现场。

请输出：
- 架构结论；
- 高风险问题；
- 中风险问题；
- 可接受的技术债；
- 必须立即修复项；
- 建议保留项。
```

---

## Review Prompt 2：Station 自治与故障隔离审查

```text
请专门审查 Station 端实现，重点判断它是否真正保持本地自治。

检查点：
1. Studio 未开启时 Station 能否正常启动；
2. Studio 离线时检测是否继续；
3. Hub 连接失败是否会阻塞 UI 或 RuntimeHost；
4. ResultAvailable 事件回调里是否做了同步网络等待；
5. bounded channel 是否正确；
6. queue 满时处理策略是否合理；
7. spool 写入失败时是否有降级策略；
8. StationId 是否稳定持久化；
9. 重启后是否能恢复 sequence / cursor；
10. 是否有网络异常向检测主流程冒泡。

请给出具体文件级审查意见，并标出需要修改的代码位置。
```

---

## Review Prompt 3：协议 DTO 与序列化审查

```text
请审查 StationSync DTO 和跨网协议设计。

重点检查：
1. 是否所有 DTO 都有 SchemaVersion；
2. 是否所有结果/健康/日志消息都有 StationId、SequenceId、MessageId、CreatedAtUtc；
3. 是否误用了 RuntimeNormalizedResult 原对象跨网传输；
4. PrimaryOutputs 是否被安全地压缩为字符串摘要；
5. 是否存在 Dictionary<string, object?>、byte[]、Image、Bitmap、Base64 图片字段；
6. 枚举序列化是否稳定；
7. DateTime/DateTimeOffset 是否统一；
8. DTO 是否适合后续版本演进；
9. MessagePack/JSON 双协议下是否都可用；
10. 是否存在循环引用或不可序列化对象。

请输出问题清单与修复建议。
```

---

## Review Prompt 4：数据一致性与 ACK 审查

```text
请审查 Studio-Station 结果摘要持久化、ACK、水位和断线补推逻辑。

重点检查：
1. StationId + SequenceId 是否唯一；
2. 重复补推是否不会重复入库；
3. ACK 是否只在 Studio 成功持久化后返回；
4. Station 是否只清理已 ACK 的 spool；
5. Studio 重启后是否能恢复每个 Station 的 LastPersistedSequenceId；
6. Sequence 跳号如何处理；
7. 老 Sequence 如何处理；
8. 并发上报是否可能造成重复写；
9. 数据库唯一约束是否真实存在；
10. 统计接口是否基于幂等后的数据。

请设计 5 个可执行测试用例，并指出当前实现是否通过。
```

---

## Review Prompt 5：性能与资源占用审查

```text
请审查当前实现对低端工控机和中心 Studio 的性能影响。

重点检查：
1. Station 是否存在高频分配；
2. Station 是否在检测热路径中做 JSON/MessagePack 大量同步序列化；
3. Station queue 是否 bounded；
4. spool 是否会无限增长；
5. Studio Registry 是否会无限保留 RecentResults；
6. SSE 是否会因慢客户端造成积压；
7. 健康采集频率是否合理；
8. 日志上报是否限流；
9. SQLite 写入是否批量/节流/索引合理；
10. Simulator 压测是否覆盖 10 个 Station、每站 2～10 条/秒。

请输出：
- 可能的性能瓶颈；
- 最小修复方案；
- 后续优化方案；
- 需要加的压测脚本。
```

---

## Review Prompt 6：安全与局域网暴露审查

```text
请审查当前 Studio-Station 实现的安全边界。

重点检查：
1. Studio 默认是否仍为 loopback；
2. LAN 模式是否必须显式开启；
3. SharedToken 是否为空时拒绝 LAN 生产模式；
4. token 是否进入普通日志；
5. Station Hub 是否有鉴权；
6. UI CORS 是否被错误放宽；
7. Station 是否新增了 HTTP Server；
8. 命令接口是否有鉴权和审计；
9. 运行包下载是否校验 packageId、hash、权限；
10. 是否存在任意文件读取/写入风险。

请输出高危问题和必须修复的安全补丁。
```

---

## Review Prompt 7：运行包远程下发审查

```text
请专门审查 RuntimePackage 远程下发实现。

重点检查：
1. 是否采用 Studio 存包、Station 主动下载；
2. 是否没有通过 SignalR 传大文件；
3. .cvpkg 是否包含 manifest；
4. 是否校验 sha256；
5. 是否校验 minStationVersion；
6. 是否先 staging 再切 active；
7. 是否保留 last-known-good；
8. 加载失败是否回滚；
9. 网络中断是否不会破坏 active；
10. DeployPackageCommand 的状态是否完整可追踪。

请输出：
- 成功路径审查；
- 失败路径审查；
- 回滚路径审查；
- 需要补充的测试。
```

---

## Review Prompt 8：健康监控与告警审查

```text
请审查健康监控和告警规则。

重点检查：
1. Heartbeat 和 HealthSnapshot 是否区分；
2. 心跳频率是否合理；
3. 健康采集是否过重；
4. 磁盘/CPU/内存/spool 是否采集正确；
5. Camera/PLC 状态是否 best-effort，不强行破坏底层模块；
6. Offline/Warning/Degraded/Critical 状态是否清晰；
7. 告警是否节流；
8. 告警恢复是否可识别；
9. UI 是否能区分离线、异常、降级；
10. 数据库是否不会被健康快照刷爆。

请给出规则调整建议和测试方案。
```

---

## Review Prompt 9：日志摘要与诊断审查

```text
请审查日志集中与诊断实现。

重点检查：
1. 是否仍保留 Station 本地完整日志；
2. 是否只上报 WARN/ERROR/FATAL 摘要；
3. 是否没有实时推全量日志；
4. 日志上报是否 bounded；
5. 日志是否限流、去重、截断；
6. 网络异常是否不会造成日志递归风暴；
7. CollectLogs 是否受大小和时间范围限制；
8. 日志包是否不会影响检测线程；
9. Studio 诊断页是否能按站点查看；
10. 是否避免泄露 token、路径敏感信息。

请输出问题清单和修复 patch 建议。
```

---

## Review Prompt 10：前端 UI 与可用性审查

```text
请审查中央 Station 监控页面。

重点检查：
1. 是否符合 ClearVision 现有结果页的视觉语言；
2. 是否没有预留图片大区域；
3. 多 Station 下是否易读；
4. 在线/离线/异常状态是否一眼可见；
5. 总览卡片是否有实际价值；
6. 最近结果流是否过载；
7. 筛选器是否够用；
8. 刷新页面后是否能恢复状态；
9. SSE 断线是否能重连；
10. 空状态、错误状态、无权限状态是否清晰。

请给出 UI 层面的修改建议，按“必须改 / 建议改 / 可后续改”分类。
```

---

## Review Prompt 11：代码质量与可维护性审查

```text
请对本次新增代码做代码质量审查。

重点检查：
1. 是否存在超大类、超大方法；
2. 是否把网络逻辑塞进 UI；
3. 是否把持久化逻辑塞进 Hub；
4. 是否抽象过度；
5. 是否命名混乱；
6. 是否异常吞掉不记录；
7. 是否日志过多或过少；
8. 是否存在重复实现；
9. 是否有未使用代码；
10. 是否符合现有项目风格。

请输出：
- 应立即重构的文件；
- 可接受的初稿技术债；
- 建议拆分的类；
- 建议删除的代码。
```

---

## Review Prompt 12：测试覆盖审查

```text
请审查当前测试覆盖是否足以支撑 Studio-Station 初稿。

必须覆盖：
1. DTO 序列化；
2. StationId 持久化；
3. Result -> Summary 映射；
4. Spool 写入/读取/清理；
5. SignalR Fake Client 注册；
6. ACK 幂等；
7. 数据库唯一约束；
8. 命令状态机；
9. 运行包 hash 校验；
10. Simulator 多站点压测。

请输出当前缺失测试，并直接给出测试文件和测试用例的实现建议。
```

---

## Review Prompt 13：工业现场部署审查

```text
请从工业现场部署角度审查本次实现。

场景：
- 中心 Studio 在总控办公室；
- 多个 Station 在产线低端工控机；
- 内部局域网；
- 可能断网、重启、IP 变化、磁盘不足、相机异常；
- 现场人员不希望频繁远程桌面。

重点检查：
1. 配置是否容易部署；
2. token 如何分发；
3. Station 如何找到 Studio；
4. IP 变化如何处理；
5. 日志如何定位；
6. 运行包如何回滚；
7. 工控机性能是否能接受；
8. 现场误操作是否有保护；
9. 数据保留策略是否合理；
10. 故障时是否能恢复。

请给出现场部署清单和需要补的工具。
```

---

## Review Prompt 14：修复执行 Prompt

当某一轮 Review 输出问题后，用下面这个 prompt 让 GPT-5.5 修复。

```text
请根据上一轮 Review 的问题清单修复 ClearVision Studio-Station 初稿。

修复原则：
1. 只修 Review 指出的问题，不做无关重构；
2. 保持默认行为不变；
3. 不引入图片传输；
4. 不新增 Station HTTP Server；
5. 不破坏 Station 自治；
6. 每个修复点说明修改文件和理由；
7. 修复后运行 dotnet build/test；
8. 如果某个问题暂不修，必须说明原因和风险。

请输出：
- 修复摘要；
- 修改文件清单；
- 每个 Review 问题的处理状态；
- 构建/测试结果；
- 剩余风险。
```

---

# 7. 一轮长任务的风险控制

你选择“一轮直接跑完初稿”是可以的，但要接受这版初稿一定会有粗糙处。因此必须把风险控制在以下边界内：

```text
可以粗糙：UI 细节、报表美观、日志查询体验、Station Simulator 功能丰富度
不能粗糙：Station 自治、幂等、ACK、spool、默认配置安全、运行包回滚、禁止图片传输
```

最值得反复 Review 的顺序是：

```text
1. Station 自治
2. 数据一致性 / ACK / 幂等
3. 安全边界
4. 运行包下发回滚
5. 性能与资源占用
6. UI 和体验
```

---

# 8. 最终交付标准

初稿完成后，至少要能演示下面这条完整链路：

```text
启动 Studio
  -> 开启 StationIngress LAN/开发模式
  -> 启动 Station 或 Simulator
  -> Station 注册
  -> Station 心跳
  -> Station 上报健康
  -> Station 上报结果摘要
  -> Studio 页面显示在线站点
  -> Studio 页面显示统计
  -> 断开网络
  -> Station 继续运行并写 spool
  -> 恢复网络
  -> Station 补推
  -> Studio 不重复计数
  -> Studio 下发 Ping 命令
  -> Station 回报成功
  -> Studio 下发 DeployPackage 测试包
  -> Station 下载、校验、切换或回滚
  -> Studio 查看命令记录和审计记录
```

只要这条链路成立，就可以进入后续精修。
