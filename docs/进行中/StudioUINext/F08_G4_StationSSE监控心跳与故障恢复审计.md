# ClearVision Studio UI Next F08 G4：Station SSE 监控、心跳与故障恢复审计

## 1. 状态与结论

```text
F08_G4_STATE=DONE
F08_G4_AUDIT=PASS
F08_G4_STOP_CONDITION=NONE
F08_G5_ENTRY=READY_AFTER_G4_COMMIT
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_RETIREMENT=NOT_APPROVED
```

G4 在既有 `/api/stations/events`、`StationRegistryService`、共享 `ApiTransport`、read-query client 与 `stations-read` capability 上闭合 Station 实时监控。没有新增第二 Station registry、HTTP 基础设施、EventBus、设备连接 authority、命令写入口或前端持久化状态树。

## 2. 实现事实

### 2.1 单一 SSE lifecycle owner

- `stationSseAdapter` 复用共享 `ApiTransport.getTextStream`，消费 `initialState`、stored/live event 与 transport keepalive，并使用 `afterSequence` 恢复游标。
- `initialState.eventSequenceId` 由服务端在 registry lock 内生成，给客户端一个与快照一致的水位；旧 cursor 的 replay 在完成全量 authority reread 后按重复事件消解。
- `createStationMonitoringOwner` 是页面唯一流生命周期 owner，持有一个 fetch stream、一个 cursor、一个 `AbortController`、一个重连 timer 和一个断线恢复 timer。
- sequenced event 只触发既有 query owner 重新读取 authority，不把 SSE payload 写成第二份 Station 权威；列表页与详情页按事件类型刷新各自已有查询。
- 15 秒 polling 只在 SSE 不可用或断线时作为 bounded recovery 使用；流重新打开后立即清除 recovery timer。

### 2.2 顺序、重连与恢复

- duplicate sequence 不产生查询副作用；非连续 sequence、buffer `DropOldest` 造成的可见 gap、JSON/contract decode failure 都触发全量 authority reread。
- gap 后游标移动到已观察水位并中止旧流，再按 1/2/5/10/15 秒退避重连；旧连接在 abort 后残留的同批 frame 由 connection signal 拒绝。
- reconnect、visibility resume 与 stream heartbeat 都重新读取服务端投影。浏览器时间只记录 UI 的最后事件时间，不计算或改写 Station online 状态。
- authority refresh 使用串行合并队列；在一个 refresh 刚结束但 cleanup 尚未执行时到达的恢复请求会继续 drain，不会静默丢失。
- visibility hidden、route unmount、feature owner dispose 与 auth expiration 均停止 stream、timer、重连和在途 query；20 次 mount/dispose 资源计数保持归零。

### 2.3 服务端在线事实与权限边界

- `StationRegistryService` 现在统一从 `isEnabled`、connection id、last seen 与 `OfflineThresholdSeconds` 计算 `IsOnline`、`OnlineState=Offline` 和 additive `OfflineReason`。
- 离线原因区分 `NeverRegistered`、`HeartbeatExpired`、`Disabled` 与 `Disconnected`；健康告警状态只统计仍在线的 Station，避免离线节点同时显示旧 `Warning/Critical` 连接状态。
- public monitor SSE 的 `stationLogAdded` 与 `stationCommandUpdated` 对非 Admin 只投影 `stationId` 变更信号；Admin 仍通过现有受保护 endpoint 读取日志、命令、审计和身份详情。
- Operator/Engineer 页面不创建 Admin query/command owner；SSE 变更信号只会让已存在的 Admin-only query 在 Admin 页面刷新。

### 2.4 Fleet 与 focused detail 故障表达

- fleet matrix 同时显示 runtime/run、active package/version/source revision、package health、Spool pending/bytes、最近结果、最后心跳与 Camera/PLC 实际上报摘要。
- Spool pending 表述为“待回放”，不推断数据丢失；包健康摘要按 Station 原文显示，不从 package id/hash 自行判定部署成功或 mismatch。
- 当前合同没有 TCP 实时连接证据，列表与详情明确显示“TCP 未上报/不可确认”；Camera/PLC/package health 缺失时使用同样的 fail-closed 文案，不从 Settings 配置推断已连接。
- focused detail 展示 active package、source revision、execution/package flow hash、decision hash、当前 run 与服务端离线原因；Admin panel 权限边界保持不变。

## 3. 独立审计修复

本轮独立审计发现并关闭四项缺口：

1. 后端 `IsOnline=false` 时仍可能返回旧 `OnlineState=Online/Warning/Critical`，UI 会展示矛盾事实。现统一投影为 `Offline` 并返回明确 `OfflineReason`。
2. 既有 monitor SSE 会把完整日志与命令 payload 发送给非 Admin。现按当前后端 permission authority 对这两类事件脱敏，普通 monitor 只收到 station 变更标识。
3. refresh 正好在 async cleanup 窗口结束时到达的 decode/gap recovery 可能入队后不再启动。现 cleanup 会检测并继续 drain 队列，并有定向竞态测试。
4. gap 中止流后，同一网络 chunk 的残余 frame 仍可能进入 callback 并把 phase 改回 live。现每个 event callback 同时校验 connection AbortSignal，旧流不能再产生副作用。

修复后重新执行 capability unit、Desktop endpoint/registry、virtual Station、services regression、Browser 与生产构建，未发现新的 P0/P1 缺口。

## 4. 门禁证据

```text
DESKTOP_STATION_FOCUSED=48/48 PASS
DESKTOP_ENDPOINTS_REGRESSION=423/423 PASS
SERVICES_REGRESSION=515/515 PASS (existing System.Collections.Immutable 8/9 resolution warnings)
VIRTUAL_STATION_RECOVERY=14/14 PASS
STUDIOUI_TYPECHECK=PASS
STUDIOUI_LINT=PASS (0 warnings)
STUDIOUI_UNIT=761/761 PASS (123 files)
STUDIOUI_STATION_TARGETED=35/35 PASS
STUDIOUI_PRODUCTION_BUILD=PASS
BROWSER_PLAYWRIGHT_F02_STATIONS=4/4 PASS (STATIC CHROMIUM FIXTURE)
IMPECCABLE_STATIC_DETECTOR=PASS (no findings)
STATIC_AUTHORITY_AUDIT=PASS
GIT_DIFF_CHECK=PASS
```

virtual Station 证据覆盖 registration、heartbeat、health、disconnect/offline、reconnect、重复 result、result gap 与 SSE snapshot；spool restart/replay、overflow trim 与 command execution/result journal restart/replay 由同一串行调用中的既有 Station tests 覆盖。该证据不代表真实 Station 或现场设备。

提交前截图证据因 `CV_F02_SOURCE_SHA` 必须是最终 40 位提交 SHA 而按 fixture 规则留待 G4 提交后捕获，不使用占位 SHA 冒充证据。

### 4.1 UI 技术审计

| 维度 | 评分 | 结论 |
| --- | ---: | --- |
| 可访问性 | 4/4 | 状态有文字与 badge；离线原因、设备未知和恢复状态不只依赖颜色 |
| 性能 | 4/4 | 一个流 owner；事件按 query 范围刷新；断线 polling 有界且 live 后停止 |
| 响应式 | 3/4 | 矩阵使用固定列宽与截断、详情在 1080px 收为单列；真实 WebView2/DPI 留待 G7 |
| 主题 | 4/4 | 复用现有 surface/text/border/status tokens，无新主题分支 |
| 反模式 | 4/4 | detector 无 findings；无设备状态猜测、卡片套卡片、装饰渐变或新视觉词汇 |
| **总计** | **19/20** | **Excellent；剩余 1 分是未执行真实 WebView2/DPI 证据，不是已知代码缺陷** |

## 5. 门禁逐条结论

| 门禁 | 结论 | 证据 |
| --- | --- | --- |
| initial/replay/live 顺序与重复副作用 | PASS | adapter + lifecycle unit；服务端 snapshot watermark contract |
| reconnect、gap、overflow/decode fallback | PASS | gap/duplicate/decode unit；bounded `DropOldest` gap detection |
| visibility、route、flag、auth 资源归零 | PASS | hide/resume/auth 与 20-cycle lifecycle unit |
| Station heartbeat/offline/reconnect/gap/spool/journal | PASS | virtual Station + offline spool + command journal tests |
| Browser 状态、布局与 decoder | PASS | F02 Station 4/4；静态 Chromium 只作 fixture 证据 |
| Admin-only 日志、命令、审计、身份 | PASS | endpoint SSE redaction test + page role-bound owner tests |

## 6. 停止条件审计

- SSE 是唯一实时 owner；polling 仅在断线/无 stream 时恢复，不与 SSE 并列覆盖页面权威。
- 前端不判定 package、Camera、PLC 或 TCP 已连接/部署成功；缺证据一律显示未上报或不可确认。
- Operator/Engineer 不创建 Admin query owner，非 Admin SSE 不再暴露日志/命令 payload。
- unmount、hidden、auth expiration 与 gap abort 后 stream/timer/reconnect 资源均归零，旧连接 frame 无副作用。

因此 G4 停止条件均未触发，独立审计结论为 `PASS`。

## 7. 未执行证据边界

```text
REAL_WEBVIEW2=NOT RUN
WINDOWS_DPI_MATRIX=NOT RUN
RELEASE_PUBLISH=NOT RUN
NO_NODE_TARGET=NOT RUN
REMOTE_CI=NOT PERFORMED
REAL_STATION_CAMERA_PLC_TCP=NOT PERFORMED
```

静态 Chromium、TestServer 与 virtual Station 不能证明真实 WebView2、Windows DPI、现场网络、相机、PLC 或 TCP。Release publish、无 Node 启动、完整 CI 与真实现场继续留待 G7 分层 evidence ledger。
