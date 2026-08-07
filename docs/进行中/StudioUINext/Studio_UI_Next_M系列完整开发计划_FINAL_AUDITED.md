# Studio UI Next M 系列完整开发计划审计索引

本文件是仓库内的执行索引。权威任务清单为工作区外的 `C:\Users\HerverJun\Desktop\Studio_UI_Next_M系列完整开发TODO计划_FINAL.md`；本索引不复制另一份可漂移的 TODO，而记录当前实现、证据和门禁状态。

```text
SOURCE_PLAN_STATE=FINAL_EXECUTABLE_TODO
SOURCE_PLAN_DATE=2026-08-06
AUDIT_HEAD=9800d6045a9f5fdfc62a166242e83529b833dc7d
M00_BASELINE_SHA=f8f581932469f7c52fe547b7bcabe8ad45d89532
BRANCH=studio-ui-next
WORKTREE_STATE=CLEAN_AFTER_DOCUMENTATION_COMMIT
M00_STATE=PARTIAL_ACCEPTANCE_OPEN
M01_TO_M05_STATE=IMPLEMENTED_LOCAL_EVIDENCE
M06_STATE=PASS_BROWSER_PARTIAL_ACCEPTANCE
M07_STATE=PASS_AUTOMATED_PARTIAL_ACCEPTANCE
M08_STATE=BLOCKED_REAL_HOST_ENVIRONMENT
M09_STATE=BLOCKED_FINAL_ACCEPTANCE
M01_PLUS_ENTRY=IMPLEMENTATION_EVIDENCE_PRESENT_ACCEPTANCE_OPEN
REMOTE_CI_PLAN_RUN=31026167704
REMOTE_CI_PLAN_STATE=PASS_AS_RECORDED_NOT_RERUN
REMOTE_CI_CURRENT_SHA=NOT_RUN
REAL_WEBVIEW2_125=BLOCKED
WINDOWS_DPI_MATRIX=BLOCKED
INDEPENDENT_NO_NODE=NOT_PERFORMED
FIELD_HARDWARE_ACCEPTANCE=NOT_PERFORMED
PRODUCTION_SOAK=NOT_PERFORMED
PRODUCT_OWNER_VISUAL_CONFIRMATION=NOT_GRANTED
AUTHORITY_CHANGED=NO
OWNER_TOPOLOGY_CHANGED=NO
PRODUCTION_ACCEPTANCE=NOT_GRANTED
LEGACY_RETIREMENT=NOT_APPROVED
M_SERIES_ENGINEERING_STATE=PASS_LOCAL_GATES
M_SERIES_VISUAL_STATE=REWORK_PENDING_PRODUCT_SIGNOFF
M_SERIES_STATE=PARTIAL
```

## 执行顺序

`M00 -> M01 -> M02 -> (M03-A/B/C, M04-A/B/C) -> M05 -> M06 -> M07 -> M08 -> M09`

M02 是单一 Workspace owner 的不可拆分纵向工作包。M03/M04 只有在文件、测试资源、端口和共享合同隔离时才允许并行；共享 Shell、tokens、router、contracts、Canvas 和 Host 文件始终由 `COORD-M` 串行修改。

## 当前证据入口

- M00 接管表：[M00_文件Owner与验证矩阵.md](./M00_文件Owner与验证矩阵.md)
- Legacy/Next 差异：[M00_LegacyNext任务与视觉差异矩阵.md](./M00_LegacyNext任务与视觉差异矩阵.md)
- 场景与截图：[M00_视觉场景与截图索引.md](./M00_视觉场景与截图索引.md)
- 当前基线：[M00_视觉精修进入基线.md](./M00_视觉精修进入基线.md)
- 历史 F09 及当前校正：[F09_FinalEvidenceManifest.md](./F09_FinalEvidenceManifest.md)

未执行的验证必须继续写 `NOT RUN`、`NOT PERFORMED` 或 `BLOCKED`，不能用历史 CI、Chromium fixture 或静态截图替代真实 WebView2、Windows DPI、现场设备和生产验收。

## 阶段审计状态

| 阶段 | 当前状态 | 当前证据 | 仍未闭合 |
| --- | --- | --- | --- |
| M00 | `PARTIAL_ACCEPTANCE_OPEN` | 接管表、Legacy/Next 差异矩阵、场景索引、dirty source gates | 产品签收、真实 WebView2/125%、完整 DPI、远端当前 SHA |
| M01-M05 | `IMPLEMENTED_LOCAL_EVIDENCE` | StudioUI 本地 lint/typecheck/unit/build、bundle、architecture guards 与阶段报告 | 依赖 M00 冻结的正式签收语义 |
| M06 | `PASS_BROWSER_PARTIAL_ACCEPTANCE` | 26/5/91/32 份当前 SHA JSON，截图/错误/溢出审计，Browser 回归 `145/26/0` | WebView2、Windows DPI、现场和产品签收 |
| M07 | `PASS_AUTOMATED_PARTIAL_ACCEPTANCE` | M07 spec `4/4 PASS`，M06/M07 回归 `171 total`；InspectionRun/Station/query owner unit 覆盖重连、abort、stale、unknown 与资源归零 | 全量数据态 pointer、真实网络/宿主复核 |
| M08 | `BLOCKED_REAL_HOST_ENVIRONMENT` | 现有 WebView2/no-Node/DPI 脚本和边界说明 | 当前会话未提供真实 WebView2 125%、独立 no-Node、现场硬件 |
| M09 | `BLOCKED_FINAL_ACCEPTANCE` | 当前 SHA 审计索引、source scope、工程门禁待重跑 | Remote CI current SHA、最终视觉签收、生产准入 |

## 当前证据入口

- M06 报告：[M06_Browser视觉验收报告.md](./M06_Browser视觉验收报告.md)
- M07 报告：[M07_可访问性响应式状态韧性审计报告.md](./M07_可访问性响应式状态韧性审计报告.md)
- M08 报告：[M08_WebView2_DPI性能收口报告.md](./M08_WebView2_DPI性能收口报告.md)
- M09 报告：[M09_最终签收与交接审计报告.md](./M09_最终签收与交接审计报告.md)
- M06 manifest：`.tmp/studio-ui-next/m-series/m06/<source-sha>/manifest.json`

## 当前门禁摘要

```text
BROWSER_GATE=PASS_WITH_SKIPS
M07_AUTOMATED_AUDIT=PASS_4_OF_4
STUDIO_UI_FAST_GATES=PASS
BUNDLE_GATE=PASS
BUNDLE_REPRODUCIBILITY=PASS
RELEASE_PUBLISH_STATIC=PASS
LOCAL_NO_NODE=BLOCKED_RUNTIME_EVIDENCE_MISSING
DPI_CODE_AUTHORITY=PASS
DPI_RUNTIME_MATRIX=BLOCKED
REAL_WEBVIEW2_125=BLOCKED
REMOTE_CI_CURRENT_SHA=NOT_RUN
FINAL_GATE=BLOCKED
PRODUCT_OWNER_VISUAL_CONFIRMATION=NOT_GRANTED
AUTHORITY_CHANGED=NO
OWNER_TOPOLOGY_CHANGED=NO
```
