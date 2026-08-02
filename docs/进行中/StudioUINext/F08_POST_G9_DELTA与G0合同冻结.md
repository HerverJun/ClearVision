# ClearVision Studio UI Next F08 G0：Post-G9 Delta Reconcile 与合同冻结

## 1. 状态与实施基线

本记录是 F08 pre-G9 计划要求的独立 `F08_POST_G9_DELTA`。G0 只完成代码事实复核、合同冻结和进入治理，没有实现 G1-G7 产品能力，也没有改变默认入口或退役 Legacy。

```text
F08_G0_STATE=DONE
F07_G9_SOURCE_EVIDENCE_SHA=a5f017d0d0ae6bf3ba20ec85488bb5afa96e21ce
F07_G9_REPORT_COMMIT=3510c61fcc9a03358950b82eb7c8274b087bffd0
F08_POST_G9_DELTA_SHA=123665268cfc579e74561cc4bc2c41d582134e2b
F08_IMPLEMENTATION_BASE=123665268cfc579e74561cc4bc2c41d582134e2b
F08_IMPLEMENTATION=G1_ONLY
F08_G1_ENTRY=READY
F08_G2_G7_IMPLEMENTATION=FORBIDDEN_UNTIL_PRIOR_GOAL_COMMIT
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_RETIREMENT=NOT_APPROVED
SETTINGS_IMPORT_EXPORT=EXCLUDED
REMOTE_STATION_IMAGE_UPLOAD=EXCLUDED
```

2026-08-02 执行 `git -c http.version=HTTP/1.1 fetch origin --prune` 成功；`studio-ui-next` 与 `origin/studio-ui-next` 为 `0/0`，upstream 正确，G0 写入前工作树干净。

## 2. G9 与 post-G9 语义差异

| 区间 | 事实 | 对 F08 的影响 |
| --- | --- | --- |
| `a8854c94..a5f017d0` | F07 Station token、AI model authority、unknown outcome、owner lifecycle 与权限修补 | 保留 F07 G9 source evidence；没有改变 F08 Runtime、Result、Station fleet 或 package authority |
| `a5f017d0..3510c61f` | 仅新增/更新 F07 G9 报告、修补报告、README 和 F07 计划 | 文档闭环；没有产品合同变化 |
| `3510c61f..12366526` | Settings/Station communication/AI persistence 原子写入、rollback 结果分类、AI key UI 默认值与对应测试 | 属于实质 post-G9 delta，故把 F08 实施基线重冻在 `12366526`；不改变 F08 G1-G7 核心假设 |

逐文件审计确认 post-G9 delta 没有修改 `package.json`/lockfile、Vite、Router、App Shell、navigation、feature flags、共享 API transport、auth/401/403、HostBridge、`.csproj`、scripts 或 CI；也没有修改 Station SignalR client、heartbeat、SSE、端口、WebView2 user-data 或测试隔离入口。

当前存在 `ClearVision.Product/src/capabilities/settings/SettingsDatabasePanel.vue`，它不在正式 `StudioUI/src/capabilities/settings/` 下，也没有被当前源码或测试引用。该文件是既有 `12366526` 提交事实，本轮不删除、不移动；它不参与 F08 owner 或构建图，记录为非阻断路径漂移。

## 3. 当前缺口复核

- Formal 与 Continuous realtime state 仍投影 `SessionType/SessionId/ClientSnapshotId/PersistenceRevision/CanonicalFlowHash/DecisionConfigurationHash`。Continuous owner 已 hydrate；Workspace Formal owner 仍只保存页面内 identity，mount 时没有 authority hydrate。
- `InspectionResultBackgroundService` 仍把旧式 `InspectionResultSpoolRecord` 写入 JSONL；记录没有 schema version、canonical outcome、judgment、execution snapshot、project revision、decision hash、runtime package、execution source/run mode/shadow role，replay 仍调用 `SetResult`。G1 P0 缺口成立。
- `StationPackageManifestDto` 与 `StationPackageRecordEntity` 仍只持久化 package/version/kind/FlowHash/SHA 等子集，没有 `SourceProjectRevision` 和 `DecisionConfigurationHash`。G1 package identity 补强成立。
- `StationCentralStore.CreateCommand` 每次生成新 command/correlation id；create/deploy endpoint 没有服务端 client idempotency key。当前前端 `studioRequestId` 只嵌在部分 payload 中，unknown recovery 仍依赖时间窗。G3 P0 缺口成立。
- `stations-read` 仍由 `createVisibleStationPollingOwner` 以 15 秒轮询驱动，没有消费已有 `/api/stations/events`。G4 范围成立。
- Next 本机 Results detail 没有 `ExecutionSnapshotId`、execution source/run mode 等完整身份；Station result decoder 没有 package/execution flow hash、execution snapshot、decision hash、execution run mode 等字段。G5 范围成立。
- Station result contract 没有远程图片/evidence 上传字段；Next Station result 路径没有远程图片请求。远程数据继续定义为 summary-only/not-uploaded，不把无图像解释为加载失败。

结论：G9 与 post-G9 delta 没有关闭或推翻 F08 的任何 P0/P1 输入，G1-G7 顺序保持不变。

## 4. Capability Owner 冻结

| Capability | 当前唯一 mounted owner / 订阅 | 当前唯一写入口 | F08 决策 |
| --- | --- | --- | --- |
| Workspace Formal Run | `WorkspaceRunCommandOwner`，随 Workspace owner dispose | 既有 authenticated admit/execute/stop/reconcile HTTP | G2 在原 owner 上增加 authority hydrate；不新建第二 Formal owner |
| Continuous Inspection | `InspectionRunOwner`，持有唯一 inspection SSE、retry timer、AbortController | 既有 realtime start/stop HTTP | `/inspection` 继续复用；不与 Formal owner 同时 mount |
| Runtime Package export | Workspace `RuntimePackageExportOwner` | 既有 export + `StationPackageStore.ImportRuntimePackageAsync` | G1/G3 只扩现有 DTO/store projection |
| Station fleet | `stations-read` lifecycle owner；当前 visibility polling | 只读 query；Admin mutation 由 `StationAdminCommandOwner` | G4 在同一 lifecycle owner 内以现有 SSE 替换主轮询，不加 store/EventBus |
| Station command/deploy | `StationAdminCommandOwner` | authenticated Station command/deploy endpoints + `StationCentralStore` | G3 增量加入 server idempotency，不新增 queue/bridge |
| Results/evidence | `results-read` query owner + capability-local `ResultEvidenceOwner` | 既有 history/Station result/evidence endpoints | G5 扩现有 decoder/page，不新增 Results capability/store |

Run Console 采用现有 `/inspection` 与 Workspace Formal Run 的组合方案，不新增第三条 Run route。统一的是 admission、identity、phase、unknown outcome 和结果的产品表达；后端状态机与两个互斥 mode owner 不合并。route leave、feature flag off、unmount 时必须 dispose 当前 owner，禁止 CSS 隐藏保活。

## 5. 角色与入口冻结

| 能力 | Admin | Engineer | Operator | 后端事实 |
| --- | --- | --- | --- | --- |
| Formal / Continuous | 运行、停止、恢复 | 运行、停止、恢复 | 禁止 | `CanOperateHardware = Admin/Engineer` |
| Runtime Package export | 依既有工程权限 | 依既有工程权限 | 禁止写 | 复用 Project/Package 既有 policy |
| Station fleet/summary/result/health | 只读 | 只读 | 只读 | authenticated read projection；不包含敏感日志/命令 |
| Station identity/logs/commands/audit/packages/deploy | 管理 | 禁止 | 禁止 | `RequireStationAdmin = Admin` |
| Results/evidence | 只读及既有导出 | 只读及既有导出 | 只读及既有导出 | 不通过 UI 扩权 |

`Studio:StudioUiEnabled=false` 保持不变；Legacy `/index.html` 仍是默认入口。`Studio2.InspectionRun` 和 `Studio2.StationsRead` 继续使用现有 feature flag，G0 不改默认值。

## 6. Canonical identity 冻结

以下身份不可互换或由前端补造：

| 范围 | 必须保留的 authority identity |
| --- | --- |
| 已保存工程 | `ProjectId + PersistenceRevision` |
| 正式执行快照 | `ClientSnapshotId/ExecutionSnapshotId + FlowHash + DecisionConfigurationHash` |
| Runtime Package | `RuntimePackageId + version + SHA-256 + SourceProjectRevision + FlowHash + DecisionConfigurationHash` |
| Station 实际执行 | `StationId + active package identity + PackageFlowHash + ExecutionFlowHash + ExecutionSnapshotId + ProjectRevision + DecisionConfigurationHash` |
| 结果 | `RunId + SessionId + ExecutionSource + ExecutionRunMode + ShadowRole + canonical outcome/judgment` |
| 命令 | `CommandId + CorrelationId + ClientRequestId + PayloadFingerprint` |

字段缺失时 UI 必须显示 legacy/unknown/incomplete，不把空值、零值、FlowHash 或“最新结果”冒充完整身份。

## 7. 命令幂等合同冻结

- G3 在现有 command entity/store/endpoint 上 additive 增加 nullable `ClientRequestId` 与 `PayloadFingerprint`；旧命令保持 null，不批量伪造。
- 唯一语义为 `(StationId, CommandType, ClientRequestId)`。同 key、同规范化 payload 返回同一 command；同 key、不同 payload 返回 `409 StationCommandIdempotencyConflict`。
- payload fingerprint 使用服务端结构化 JSON 规范化后的 SHA-256，不能依赖原始属性顺序或前端字符串。
- 并发创建以数据库唯一约束兜底；冲突后 reread 并执行同 payload/异 payload 判定。
- POST 成功只表示“命令已创建”。部署完成必须同时满足 command terminal `Succeeded` 与 Station active package authority identity 匹配。
- abort/network/decode/5xx 后复用同一个 client request id 查询或重试创建；UI 不生成新 key 自动重发副作用。

## 8. G1 增量合同草案

- 本机 result spool 引入明确 schema version；新写完整保存 canonical outcome、judgment signal/source/reason、diagnostic、execution snapshot、project revision、flow/decision hash、runtime package、execution source/run mode/shadow role、run/session 和 defects；读取器兼容旧行并保留 legacy/unknown。
- replay 必须经完整 persistence snapshot 恢复实体，禁止继续以 `SetResult` 丢弃双轴 outcome/identity；Formal exact reconciliation 不使用时间窗或最新结果猜测。
- history detail/traceability additive 投影 `ExecutionSnapshotId`、execution source/run mode/shadow role 等实体已有字段；列表保持兼容。
- Station package DTO/entity additive nullable 保存 runtime manifest 的 source project id/revision、flow hash 和 decision hash；只从可信 manifest 回填，无法验证的旧行保持 null。
- site profile 的有效快照必须在 runtime side effect 前由现有 Runtime validator/admission 校验。UI 只展示稳定 violation code/message，不复制 validator。
- 不新增第二 result endpoint、package store、identity table、HTTP client、EventBus、RuntimeHost 或 Station authority。

## 9. G0 验证证据

```text
STATIC_CODE_AUDIT=PERFORMED
POST_G9_DELTA_RECONCILE=PASS
REMOTE_FETCH=PASS
WORKTREE_CLEAN_BEFORE_G0_WRITE=YES
STUDIOUI_SETTINGS_UNIT=19/19 PASS
STUDIOUI_TYPECHECK=PASS
DESKTOP_SETTINGS_STATION_AI_FOCUSED=90/90 PASS
SERVICES_AI_CONFIG_STORE_FOCUSED=24/24 PASS
FULL_BUILD=NOT RUN
FULL_UNIT_REGRESSION=NOT RUN
BROWSER_PLAYWRIGHT=NOT RUN
REAL_WEBVIEW2=NOT RUN
VIRTUAL_STATION=NOT RUN
RELEASE_PUBLISH=NOT RUN
NO_NODE_TARGET=NOT RUN
CI=NOT PERFORMED
REAL_STATION_CAMERA_PLC_TCP=NOT PERFORMED
```

Desktop 与 services 定向测试通过时有一次既有 `System.Collections.Immutable` 8.0/9.0 解析 warning；未导致测试失败，本报告不把它改写为 clean full build。

## 10. G0 审计结论

G0 的门禁通过：F07 G9 有可复现 source evidence 和报告提交，post-G9 delta 已逐项审计并通过定向验证，当前共享 owner/authority 假设仍成立，角色、identity、幂等、图像边界、入口与 G1 additive contract 已冻结。只解除 G1 实施门禁；G2-G7 仍须等待前一 Goal 完成、审计、回填和提交。
