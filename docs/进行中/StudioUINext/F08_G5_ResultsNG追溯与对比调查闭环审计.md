# ClearVision Studio UI Next F08 G5：Results、NG 追溯与对比调查闭环审计

## 1. 状态与结论

```text
F08_G5_STATE=DONE
F08_G5_AUDIT=PASS
F08_G5_STOP_CONDITION=NONE
F08_G6_ENTRY=READY_AFTER_G5_COMMIT
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_RETIREMENT=NOT_APPROVED
```

G5 在既有 inspection history/statistics、compare、previous-success、evidence manifest/export、image 与 Station results/statistics endpoint 上闭合 Results 调查链。没有修改后端业务权威、结果持久化、角色策略、共享 API、Router、App Shell 或默认入口，也没有新增第二 Results store、HTTP client、EventBus、evidence store、图片上传或保存链。

## 2. 实现事实

### 2.1 Canonical outcome 与统计分母

- 本机与 Station 列表、详情均从后端 `ExecutionOutcome` + `DecisionOutcome` 解码 canonical 双轴，分别展示执行状态、判定结果、`HasJudgmentSignal`、`DecisionSource`、`ReasonCode` 与 diagnostic code/message。
- `Failed`、`Cancelled`、`TimedOut`、`Skipped` 与 `Invalid`、`Undetermined`、`NotApplicable` 保持独立语义；旧 Station 结果只使用后端冻结的 legacy outcome 映射并明确标记兼容投影，不从诊断文案或算子类型推断 NG。
- 统计同时展示执行成功率、判定覆盖率与有效判定良率；`validDecisionCount = okCount + ngCount`，有效判定数不大于成功执行数，成功执行数不大于总尝试数。

### 2.2 本机与 Station 完整追溯

- 本机详情显示 FlowHash、CalibrationBundle、ExecutionSnapshotId、Project PersistenceRevision、DecisionHash、package/runtime package、run/session、execution source/run mode、shadow role 与 stationId。
- Station decoder/detail 保留 PackageFlowHash、ExecutionFlowHash、FlowHash、ExecutionSnapshotId、ProjectRevision、DecisionHash、ExecutionRunMode、RunId、MessageId 与主要标量输出摘要。
- list/detail/previous-success/compare/evidence 响应均绑定请求 project/result identity；动态 route identity 改变时销毁旧 list/detail/statistics/evidence/investigation owner，不能把上一工程的 stale data 投影到新路由。

### 2.3 NG 调查、证据与图像边界

- “查找前次成功并对比”先调用既有 previous-success endpoint，再将服务端返回的同工程 reference 交给既有 compare endpoint；UI 仅显示服务端 compatibility、warning、field diff、traceability diff 与 replay availability，不在前端拼算生产结论。
- 本机 evidence owner 独占 manifest、export、image 的请求与 `AbortController`/blob URL 生命周期；export 与 image 使用分离取消域，导出不会把仍在读取的图片永久留在 loading。
- evidence 区分 available、partial、retained-summary-only、expired、not-produced、load-failed 与 export-error；图片区分 available、retained-summary-only、not-produced 与 load-failed。
- 本机图片只接受受控 `/api/images/{uuid}` 引用并经共享 authenticated `ApiTransport.getBlob` 读取。Station 合同始终投影 `not-uploaded`，页面只显示远程摘要边界，Browser request audit 证明不会请求远程图片。

## 3. 独立审计修复

独立审计发现并关闭三项缺口：

1. compare、previous-success、detail/list 与 evidence manifest 原先只验证 payload 内部字段，未全部绑定请求 identity。现 list/detail/compare/previous-success decoder 和 manifest owner 都拒绝跨 project/result 响应。
2. 共享 read-query owner 会在动态 key 请求失败时保留 previous data；工程或结果 identity 改变若继续复用 owner，可能短暂或失败后展示上一 identity。现 identity 切换先 dispose 对应 owner，再从新路由 authority reread。
3. evidence export 原先与 image load 共用 generation/controller；清单先返回时立即导出可能中止图片且保留 loading。现 load/export generation 与 controller 分离，并新增并发回归测试。

修复后重新执行 Results unit、StudioUI 全量门禁、history/spool/Station round-trip、服务与 Desktop endpoint 回归及 Browser 场景，未发现新的 P0/P1 缺口。

## 4. 门禁证据

```text
STUDIOUI_RESULTS_TARGETED=29/29 PASS
STUDIOUI_UNIT=771/771 PASS (123 files)
STUDIOUI_TYPECHECK=PASS
STUDIOUI_LINT=PASS (0 warnings)
STUDIOUI_PRODUCTION_BUILD=PASS
DOTNET_HISTORY_SPOOL_SERIALIZATION_FOCUSED=34/34 PASS
DOTNET_RESULTS_STATION_DESKTOP_FOCUSED=68/68 PASS
SERVICES_REGRESSION=515/515 PASS (existing System.Collections.Immutable 8/9 resolution warnings)
DESKTOP_ENDPOINTS_REGRESSION=423/423 PASS
BROWSER_PLAYWRIGHT_F02_RESULTS=3/3 PASS (3 screenshot-only cases skipped when no output target was requested)
BROWSER_RESULTS_VISUAL_PROVISIONAL=3/3 PASS (1920x1080; 1366x768; 1366x600; compact/light)
GLOBAL_HORIZONTAL_OVERFLOW=0/0/0 px
BROWSER_RUNTIME_ERRORS=0
IMPECCABLE_STATIC_DETECTOR=PASS (no findings)
STATIC_AUTHORITY_AUDIT=PASS
GIT_DIFF_CHECK=PASS
```

Browser 证据使用 `BROWSER_FIXTURE` 与 `HARNESS_SEEDED_SESSION`，覆盖 NG、execution failure、decision invalid、undetermined、cancelled、expired evidence、legacy Station 与 remote summary-only。它证明页面投影、请求 shape、路由与布局，不证明真实 endpoint、WebView2、Windows DPI 或现场设备。

提交前视觉截图以当前工作树构建并通过门禁，仅作 provisional 审查；最终提交 SHA 的截图 metadata 在 G5 commit 后生成到 `.tmp/studio-ui-next/f02-1/g5`，不把提交前 SHA 冒充 Final Candidate。

### 4.1 UI 技术审计

| 维度 | 评分 | 结论 |
| --- | ---: | --- |
| 可访问性 | 4/4 | outcome、evidence、legacy 与 remote boundary 均有文字；技术追溯使用原生 details/summary 与可复制字段 |
| 性能 | 4/4 | 单一 capability owner 集合；500 条 Station fixture 分页；图片/manifest/export 可取消且 blob URL 回收 |
| 响应式 | 3/4 | 1920x1080、1366x768、1366x600 零全局横向溢出且双栏独立滚动；真实 WebView2/DPI 留待 G7 |
| 主题 | 4/4 | 复用现有 surface/text/border/status tokens，无新主题或单色装饰体系 |
| 反模式 | 4/4 | detector 无 findings；无卡片套卡片、装饰渐变、伪图片 loading、算子名推断或新视觉词汇 |
| **总计** | **19/20** | **Excellent；剩余 1 分是未执行真实 WebView2/DPI 证据，不是已知代码缺陷** |

## 5. 门禁逐条结论

| 门禁 | 结论 | 证据 |
| --- | --- | --- |
| history/list/detail/compare/previous-success/evidence identity 一致 | PASS | Core 34/34、Desktop 68/68、endpoint 423/423、请求 identity decoder tests |
| 完整/legacy/缺字段/非法双轴/远程无图像 | PASS | Results contracts + page unit；Station canonical/offline replay/serialization tests |
| NG 与非 NG 异常实际可见且无文本推断 | PASS | Browser canonical scenario；shared inspection outcome formatter |
| evidence/image 状态和 export 生命周期 | PASS | evidence owner unit；manifest/export endpoint 与 redaction tests |
| Station mapper/offline replay/central store/API round-trip | PASS | Desktop focused 68/68；services regression 515/515 |
| Operator 只读边界与 Admin command/log 隔离 | PASS | 既有 authenticated Results GET policy；Desktop endpoint role regression；Results 无写 client |

## 6. 停止条件审计

- 本机与 Station 详情均显示 execution snapshot 与 decision hash；旧字段缺失时保留未知，不伪造零值。
- background spool、serialization、Station mapper/central store/offline replay 测试证明 canonical outcome 与 identity 不退化。
- UI 不读取 operator type，也不扫描输出名或 diagnostic 文本来决定 NG。
- Station remote summary 不创建 image/evidence owner，不请求 `/api/images/*`，也不显示图片加载失败。

因此 G5 停止条件均未触发，独立审计结论为 `PASS`。

## 7. 未执行证据边界

```text
REAL_WEBVIEW2=NOT RUN
WINDOWS_DPI_MATRIX=NOT RUN
VIRTUAL_STATION_END_TO_END=NOT RUN IN G5 (existing focused spool/replay tests passed)
RELEASE_PUBLISH=NOT RUN
NO_NODE_TARGET=NOT RUN
REMOTE_CI=NOT PERFORMED
REAL_STATION_CAMERA_PLC_TCP=NOT PERFORMED
```

上述未执行项不能由静态 Chromium、TestServer 或单元测试替代，继续留待 G7 的同一 source SHA 分层 evidence ledger。
