# ClearVision Studio UI Next F08 G7：角色、异常矩阵与 Final Evidence 准入审计

> 历史审计说明（2026-08-03）：本报告记录 F08-R1 重开前的 G7 结论。由于本机 RunId/SessionId 语义混淆和完整 F03 Workspace suite 尚未闭环，当前 `F08_G7_STATE=BLOCKED`；当前状态与后续证据统一见 [F08-R1 RunId 语义与 Final Evidence 修复审计](./F08_R1_RunId语义与FinalEvidence修复审计.md)。以下原始结论不作静默改写。

## 1. 状态与结论

```text
F08_G7_STATE=DONE
F08_G7_AUDIT=PASS
F08_G7_STOP_CONDITION=NONE
F08_SOURCE_EVIDENCE_SHA=1ec94a647cae137a1fa6ae89bd02a9710691766d
F08_ENGINEERING_STATE=DONE
F08_PRODUCTION_ACCEPTANCE=NOT_GRANTED
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_RETIREMENT=NOT_APPROVED
```

G7 在同一 source SHA 上完成角色、异常、owner lifecycle、Browser、真实 WebView2、virtual Station、Release publish 与本机无 Node 进程链审计。G1-G6 的 P0/P1 identity、unknown-outcome、部署、Station 恢复和结果追溯门禁均保持通过，没有发现未关闭的 F08 工程阻断。

`F08_G7_AUDIT=PASS` 只表示计划定义的工程实现和分层记录完整，不表示生产现场准入。真实 Windows 125% DPI、独立无 Node 目标机、完整 CI 和真实 Station/Camera/PLC/TCP 均未执行，因此 `F08_PRODUCTION_ACCEPTANCE=NOT_GRANTED`。默认入口、默认 flags 和 Legacy 文件保持不变。

## 2. 角色矩阵冻结与验证

| 能力 | Admin | Engineer | Operator | 后端与 UI 证据 | 结论 |
| --- | --- | --- | --- | --- | --- |
| Formal Run / Continuous Inspection | 运行、停止、恢复 | 运行、停止、恢复 | 禁止 | 后端 `CanOperateHardware=Admin/Engineer`；Operator admission/execute/stop/reconcile/realtime endpoint 返回 403；Run UI 不为 Operator 创建写 owner | PASS |
| Runtime Package 生成 | 依既有工程权限 | 依既有工程权限 | 不可写 | 继续复用 Workspace package export 与既有 Project policy；未新增 F08 package 写端点 | PASS |
| Station fleet、summary、health、results、statistics、monitor SSE | 只读 | 只读 | 只读 | authenticated read projection；普通 SSE 的日志/命令事件只携带 Station 变更信号 | PASS |
| Station identity、部署、控制命令、日志、命令、审计、package 管理 | 管理 | 禁止 | 禁止 | 后端 `RequireStationAdmin=Admin`；non-Admin endpoint tests 返回 403；Engineer 页面不 mount Admin query/command owner | PASS |
| Results、NG、compare、previous-success、evidence | 只读及既有导出 | 只读及既有导出 | 只读及既有导出 | Results capability 没有写 client；权限错误投影为只读 forbidden 状态，不通过 deep link 扩权 | PASS |
| 权限策略修改 | 不在 F08 UI 实现 | 不允许 | 不允许 | 未新增 policy editor、角色写端点或前端权限 authority | PASS |

角色结论不是由隐藏按钮得出。`InspectionRunEndpointsTests` 与 `RealtimeInspectionEndpointsTests` 证明 Operator 被后端拒绝；`StationEndpointsTests` 证明 non-Admin 无法读取敏感详情或创建部署/控制写入；StudioUI page/owner tests 同时证明受限角色不会 mount 相应写 owner。

## 3. 异常矩阵

| 异常 | 权威处理与用户投影 | 证据 | 结论 |
| --- | --- | --- | --- |
| admission reject | validator/endpoint 在副作用前返回稳定 violation code；Run Console 显示原因、影响对象和参数，禁止 execute | G1 Runtime effective admission；G2 owner/unit；blocked admission Browser | PASS |
| occupied | coordinator/realtime state 是唯一占用事实；另一 mode 只读显示，不创建第二 session/SSE/stop owner | G2 Formal/Continuous 双向占用 unit、endpoint 与 Browser | PASS |
| start unknown | 保留 exact execution identity，先 authority hydrate/reconcile；禁止自动重复 start | G2 execute response loss、五字段 identity 与 exact result lookup tests | PASS |
| stop unknown | 保持 locked unknown，先 authority state/result reconcile；route leave/dispose 不伪装为 stop | G2 stop response loss、late response、dispose 与 Browser lifecycle tests | PASS |
| SSE gap / duplicate / decode failure | sequence gap 触发全量 authority reread并中止旧流；duplicate 不产生查询副作用；bounded reconnect 不触发 start | G2/G4 sequence、generation、gap、decode、retry tests | PASS |
| Station offline | 后端按 enabled/connection/lastSeen/threshold 投影 Offline 与原因；UI 不用浏览器时钟改写在线事实 | G3 offline command expiry；G4 registry/endpoint/virtual Station | PASS |
| command timeout | Created/Delivered 到期由 central store 在 lookup/list/poll 结算为 `TimedOut`；POST 200 不算终态成功 | G3 central store、endpoint、UI projection | PASS |
| package SHA mismatch | Station 在 staging/activation 前校验下载 SHA，失败不更新 active identity | G3 package deployment tests、virtual Station | PASS |
| package version mismatch | 部署 admission 和 Station manifest 校验版本/最低版本，失败保持未部署 | G3 endpoint/deployment tests | PASS |
| package/active/result identity mismatch | packageId/version/SHA/source revision/flow/decision 及 execution identity 逐字段比较；缺失或不一致显示不可确认 | G3 deployment projection；G5/G6 traceability/production chain tests | PASS |
| activation failure / rollback | 继续使用 Station staging、active、last-known-good；失败恢复并重新加载旧 active package，UI 不覆盖该事实 | G3 deployment service 与 virtual Station | PASS_VIRTUAL |
| Camera failure / unavailable | 只显示 Station/设备 authority 实际上报；缺失显示未上报/不可确认，不从 Settings 推断已连接 | G4 contracts/page、Station health tests | PASS_LOCAL_CONTRACT |
| PLC failure / unavailable | 与 Camera 相同；runtime offline/pending 保持明确状态，不猜测连接成功 | G4 contracts/page、StationSyncHostedService tests | PASS_LOCAL_CONTRACT |
| TCP failure / unavailable | 当前 Station 合同没有实时 TCP 证据时明确显示“未上报/不可确认” | G4 fleet/detail unit 与 Browser | PASS_LOCAL_CONTRACT |
| spool backpressure / gap | pending/bytes 表达为待回放，不直接宣称数据丢失；gap、trim、restart/replay 由既有 store 处理 | G4 virtual Station、offline replay、command/result spool tests | PASS_VIRTUAL |
| result persistence outage | schema v2 spool 完整保存 canonical outcome 与 execution identity，数据库恢复后 exact replay/reconcile | G1 primary 46/46、Services 515/515、Product full | PASS |
| evidence unavailable | available/partial/summary-only/expired/not-produced/not-uploaded/load-failed/export-error 分离；Station remote 不请求图片 | G5 evidence owner/unit、Results Browser、G6 remote boundary audit | PASS |

`PASS_LOCAL_CONTRACT` 和 `PASS_VIRTUAL` 不能替代真实设备或现场网络。真实 Camera、PLC、TCP、Station 断线与恢复仍为 `NOT PERFORMED`。

## 4. G7 审计中发现并修复的问题

1. 累积 F07/F08 capability 使原冻结 bundle closure 超限。G7 按当前正式 route closure 重冻预算并保留 fail-closed gate；当前 AI/Inspection/Results/Shell/Stations/Workspace 分别为 1,001,078/797,523/848,575/873,038/861,540/946,045 bytes，均低于新预算，reproducibility 两次产物一致。没有合并 lazy route 或把全部页面改成 eager。
2. Workspace 的五个直接子项此前只有四行 grid 定义，Run Console 会挤占 Canvas；现使用五个显式行。FlowWorkspace 也固定单一 constrained row，并把 canonical splitter 恢复为 8px。
3. 短宿主下 Formal Run Console 原先会让主 Canvas 几乎不可用；现 console body 成为唯一滚动 owner，并给正常 1350x704 client 保留 330px work area 和 308px 可见 Canvas。
4. WebView2 harness 原先把 harness-seeded dirty draft 当失败、使用未变换 pointer 坐标、不能稳定确认 Node 完成。现只通过一次 canonical Project PUT 归一化并验证 `PersistenceRevision` 前进，按 Canvas scale/offset 转换 pointer，同时写入显式完成信号。
5. 初次 simulated DPR 1.25 矩阵把“所有逻辑 viewport 均至少 300px 可见 Canvas”错误当成统一准入。现把正常短宿主可用性与极端 force-scale probe 分层，记录真实可见 slice，不把 675x352 logical viewport 当正常 200% 布局验收。

## 5. 同一 SHA 证据台账

所有以下最终证据均在 `1ec94a647cae137a1fa6ae89bd02a9710691766d` 为 HEAD 时执行或由该 SHA 的 WebView2 matrix 显式记录。

| 证据层 | 结果 | 产物/说明 |
| --- | --- | --- |
| StudioUI lint | PASS | `npm.cmd run lint` |
| StudioUI strict typecheck | PASS | `npm.cmd run typecheck` |
| StudioUI unit | 126 files / 782/782 PASS | `npm.cmd run test:unit` |
| production build | PASS | `npm.cmd run build:production` |
| bundle gate | PASS | `.tmp/bundle/report.json` |
| bundle reproducibility | PASS | 两次 production output 规范化一致 |
| WebView2 harness syntax | PASS | `node --check studio-ui-webview2-smoke.cjs` |
| Services regression | 515/515 PASS | `.tmp/test_results/services-regression/services-regression.trx` |
| Product non-performance full | 3861 PASS / 2 existing SKIP / 0 FAIL | `.tmp/studio-ui-next/f08/g7-final-1ec94a64/product-full-r2/product-full-r2.trx` |
| Desktop full | 772/772 PASS | `.tmp/studio-ui-next/f08/g7-final-1ec94a64/desktop-full/desktop-full.trx` |
| Desktop endpoints | 423/423 PASS | `.tmp/test_results/desktop-endpoints/desktop-endpoints.trx` |
| StudioUI architecture guard | 9/9 PASS | `.tmp/test_results/f08-g7-final-1ec94a64-architecture/f08-g7-final-architecture.trx` |
| Virtual Station | 39/39 PASS | `.tmp/studio-ui-next/f08/g7-final-1ec94a64/virtual-station/virtual-station.trx` |
| Browser, all Next specs except legacy F03 Workspace file | 87 PASS / 26 screenshot-only SKIP / 0 FAIL | static Chromium fixture；1 worker |
| Browser, F03 owner/layout acceptance subset | 6/6 PASS | one owner/dispose + 1920x1080、1366x768、1350x704 compact/comfortable |
| git diff check | PASS | 最终 source tree 无 whitespace error |

Product 第一次最终调用使用了 `MinimumTotalTests=3863`，而 wrapper 把两个 SKIP 视为未执行，因此在产品测试 `3861 PASS / 2 SKIP / 0 FAIL` 后由阈值校验返回非零。R2 使用正确的 minimum executed/passed 3861，结果保持相同并通过 wrapper；两个 SKIP 未改写为 PASS。

完整旧 `f03-workspace.spec.ts` 在 G7 早期尝试中为 17 PASS / 37 FAIL，主要是过时 Camera 节点坐标和旧 golden journey fixture 合同漂移。该结果未被记录为全量 Browser PASS，也未用来否定当前 6/6 owner/layout acceptance；后续应单独治理旧 F03 fixture。

## 6. 真实 WebView2、DPI 与 Release

最终 matrix：`.tmp/studio-ui-next/f04/matrix/f08-g7-final-1ec94a64/studio-ui-webview2-matrix.json`。

```text
REAL_WEBVIEW2_MATRIX=12/12 PASS
SOURCE_SHA=1ec94a647cae137a1fa6ae89bd02a9710691766d
DEBUG_LEGACY_DIAGNOSTICS_OVERVIEW_PROJECTS=PASS
DEBUG_WORKSPACE_DPR_1_1.25_1.5_2=PASS (FORCE_SCALE PROBES)
RELEASE_DIAGNOSTICS_OVERVIEW_WORKSPACE=PASS
RELEASE_MISSING_ASSETS_DIAGNOSTIC=PASS
RELEASE_PUBLISH=PASS
PUBLISH_STATIC_AUDIT=PASS
LOCAL_DESKTOP_PROCESS_TREE_WITHOUT_NODE=PASS
INDEPENDENT_CLEAN_MACHINE_WITHOUT_NODE=NOT_PERFORMED
REAL_WINDOWS_125_PERCENT_DPI=NOT RUN
```

12 次真实 WinForms + WebView2 启动均无 console error、page error 或 request failure；每次 process/port/runtime cleanup 均通过，publish、missing-assets 和 build 临时目录已移除。Debug Legacy profile 通过，证明 G7 没有偷偷切换默认入口；Release publish 从允许的 `.tmp/publish-check/` 生成并在验证后清理。

本机 native window 观测 DPI 始终是 96/100%。`devicePixelRatio=1.25/1.5/2` 来自 WebView2 `force-device-scale-factor`，只证明 backing store、pointer transform 和逻辑 viewport 压力行为，不是 Windows 125% 系统 DPI 证据。

| probe | logical viewport | Run Console | work area | Canvas logical/backing | 可见 Canvas | ROI pointer |
| --- | --- | ---: | ---: | --- | ---: | --- |
| native 100%, DPR 1 | 1350x704 | 262px | 330px | 680x308 / 680x308 | 308px | PASS |
| simulated DPR 1.25 | 1080x564 | 152px | 300px | 716x300 / 895x375 | 300px | PASS |
| simulated DPR 1.5 | 900x470 | 152px | 206px | 676x300 / 1014x450 | 206px | PASS |
| simulated DPR 2 | 675x352 | 152px | 88px | 1040x300 / 2080x600 | 88px | PASS |

simulated DPR 2 的 88px 只是极端逻辑 viewport 下的可见 slice 与 pointer/backing-store probe，不能表述为正常 200% Workspace 可用性通过。

本机 no-Node 结论也需分层：发布后的 Desktop 进程树没有 Node 子进程，且静态资产从 publish 目录加载；同一机器仍安装并使用外部 Node CDP driver。因此独立无 Node 目标机启动仍为 `NOT_PERFORMED`。

## 7. Owner、权限与回滚演练

- StudioUI unit、Browser 20-cycle、F03 owner/layout subset、G2/G4 lifecycle tests 与 WebView2 diagnostics 均未发现 duplicate owner；dispose 后 stream、timer、AbortController、请求和写计数归零。
- auth expiry、401/403、Operator/Engineer/Admin、feature flag off、route leave、dirty/pending/unknown protection 均有 endpoint/unit/Browser 覆盖。
- Debug Legacy profile 真实 WebView2 PASS；默认 `StudioUiEnabled=false` 与默认 flags 未改，UI 可继续使用既有 flag/route 回到稳定 owner。
- additive DB 字段不做破坏性 down migration；代码回滚必须继续容忍 nullable identity。Station activation failure 仍由 last-known-good 恢复，virtual tests PASS。
- 真实生产部署回滚、真实 Station last-known-good 和现场网络中断演练未执行，不由本机 matrix 替代。

## 8. 未执行证据与准入限制

```text
REAL_WINDOWS_125_PERCENT_DPI=NOT RUN
INDEPENDENT_CLEAN_MACHINE_WITHOUT_NODE=NOT PERFORMED
REMOTE_CI=NOT PERFORMED
REAL_STATION=NOT PERFORMED
REAL_CAMERA=NOT PERFORMED
REAL_PLC=NOT PERFORMED
REAL_TCP=NOT PERFORMED
SITE_NETWORK_INTERRUPTION_AND_RECOVERY=NOT PERFORMED
LONG_RUNNING_PRODUCTION_SOAK=NOT PERFORMED
```

CI 没有真实 run URL，普通本地提交不构成 CI。真实硬件和现场证据未执行，virtual Station、loopback TCP、静态 Chromium、真实 WebView2 或 Release publish 均不能替代这些层。因此 F08 工程实现可以完成，production acceptance、默认 cutover 和 Legacy retirement 仍不批准。

## 9. 停止条件审计

- 没有未关闭的 P0 identity 或 unknown-outcome 缺口。
- 没有发现 owner dispose 后仍保留订阅、timer、SSE、请求或写操作。
- 角色矩阵由后端 policy 与 endpoint 403 共同验证，不是只靠 UI 隐藏。
- WebView2、Release 与 Browser 结果分别记录；未用 Browser 覆盖真实 WebView2 或未执行层。
- F08 完成没有修改默认入口、默认 flags、Legacy owner/files，也没有形成 Runtime/Station/Result 第二 authority。

因此 G7 停止条件均未触发，独立审计结论为 `PASS`；生产准入限制继续生效。
