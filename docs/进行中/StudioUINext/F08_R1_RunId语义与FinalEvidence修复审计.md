# ClearVision Studio UI Next F08-R1：RunId 语义与 Final Evidence 修复审计

## 1. 当前唯一状态

```text
F08_PLAN_STATE=REOPENED_FOR_R1
F08_ENGINEERING_STATE=PARTIAL
F08_G1_STATE=PASS
F08_G2_STATE=AWAITING_WORKSPACE_REGRESSION_RECONCILE
F08_G3_STATE=PASS
F08_G4_STATE=PASS
F08_G5_STATE=BLOCKED_BY_RUN_IDENTITY
F08_G6_STATE=BLOCKED_BY_RUN_IDENTITY
F08_G7_STATE=BLOCKED
F08_NEXT_GOAL=F08_R1
F08_PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

本文件是 F08 当前唯一状态入口。`F08_完成报告.md` 与 `F08_G7_角色异常矩阵与FinalEvidence准入审计.md` 保留 2026-08-03 R1 重开前的历史审计结论，不再代表当前状态。

## 2. 重开原因与审计对象

旧审计发现本机 history API 将 `runId` 投影为 `result.SessionId`，混淆了业务运行身份与执行会话身份；旧 G7 同时只运行了 F03 Workspace 的 6 项子集，完整 suite 仍有 37 项失败且未逐项保存真实失败证据。上述两项使 G5/G6 身份链与 G7 Final Evidence 不能继续维持完成结论。

```text
F08_REPORTED_FINAL_SHA=9b0525cdc6904d2e6ccc0da125b80cec15c7a061
F08_SOURCE_EVIDENCE_SHA=1ec94a647cae137a1fa6ae89bd02a9710691766d
F08_R1_INITIAL_SHA=9b0525cdc6904d2e6ccc0da125b80cec15c7a061
PREVIOUS_REMOTE_AUDIT_BRANCH=audit/f08-9b0525cdc
OFFICIAL_REMOTE_SHA=123665268cfc579e74561cc4bc2c41d582134e2b
```

## 3. R1 决策与门禁

- 先从 Runtime、Inspection、结果实体、持久化、spool、history 与 Station 链路追踪真实 RunId authority；若无法证明已有独立 authority，则采用 `ABSENT_RETURN_NULL`，不得生成或借用其他身份。
- SessionId、RunId、ExecutionSnapshotId 与 ResultId 必须保持独立语义；legacy 本机结果不得补造 RunId。
- 完整 `f03-workspace.spec.ts` 必须以 Chromium、单 worker、失败 trace 与结构化报告重跑，并达到 0 unexpected failures。
- 本地工程证据、Playwright、WebView2、publish 与 bundle 证据必须绑定同一 source SHA；无法内嵌 source SHA 的产物在本报告记录 SHA-256。
- 本地门禁全部通过后，只创建并推送新的 `audit/f08-r1-<short-sha>`；不得移动 `origin/studio-ui-next`。

## 4. 当前证据边界

```text
RUN_ID_AUTHORITY_DECISION=PENDING
SESSION_ID_RUN_ID_CONFLATION=OPEN
F03_WORKSPACE_REGRESSION_RECONCILE=NOT_RUN
REMOTE_CI=NOT_RUN
RELEASE_PUBLISH=NOT_RUN
LOCAL_NO_NODE_PROCESS_TREE=NOT_RUN
INDEPENDENT_CLEAN_MACHINE_WITHOUT_NODE=NOT_RUN
REAL_WINDOWS_125_PERCENT_DPI=NOT_RUN
REAL_STATION_CAMERA_PLC_TCP=NOT_RUN
FIELD_NETWORK_RECOVERY=NOT_RUN
LONG_RUNNING_SOAK=NOT_RUN
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

后续取证、实现决策、37 项原失败分类、验证命令、source SHA、artifact 路径与 SHA-256、Remote CI 链接和最终状态均在本文件追加。未实际执行的层保持 `NOT_RUN` 或 `NOT_PERFORMED`。
