# F09 Final Evidence Manifest

## Current M-series reconciliation

```text
AUDIT_SOURCE_SHA=9800d6045a9f5fdfc62a166242e83529b833dc7d
M00_BASELINE_SHA=f8f581932469f7c52fe547b7bcabe8ad45d89532
CURRENT_BRANCH=studio-ui-next
CURRENT_WORKTREE=CLEAN_AFTER_DOCUMENTATION_COMMIT
HISTORICAL_REMOTE_CI_RUN=30966530885
HISTORICAL_REMOTE_CI=FAIL
CURRENT_PLAN_REMOTE_CI_RUN=31026167704
CURRENT_PLAN_REMOTE_CI=PASS_PLAN_PROVENANCE
CURRENT_BROWSER_BASELINE=PASS_146_SKIPPED_26_FAILED_0_CONTENT_EQUIVALENT_PRECOMMIT_CANDIDATE
CURRENT_WEBVIEW2_100=PASS_AUDIT_DIRTY_CANDIDATE_NOT_BASELINE_SHA
CURRENT_WEBVIEW2_125=BLOCKED
CURRENT_RELEASE_PUBLISH_RUNTIME=PASS_LOCAL_MACHINE
CURRENT_INDEPENDENT_NO_NODE=NOT_PERFORMED
CURRENT_FIELD_HARDWARE=NOT_PERFORMED
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

`CURRENT_PLAN_REMOTE_CI=PASS_PLAN_PROVENANCE` means the final TODO plan records the run as passing; this worktree has not refreshed remote state in the current session. It is retained as an input fact, not substituted for current local gates or real WebView2 evidence.

### Current M08 evidence addendum (2026-08-07)

| EvidenceId | 范围 | 状态 | 来源与边界 |
| --- | --- | --- | --- |
| M08-E001 | Debug WebView2 Windows 100% DPI | PASS_DIRTY_CANDIDATE | `.tmp/studio-ui-next/m-series/m08/9800d604/webview2-100-dpi-f09/evidence/`；native DPI 96、PerMonitorV2、Canvas/pointer/overflow PASS |
| M08-E002 | Debug WebView2 Golden Journey | PASS_DIRTY_CANDIDATE | `.tmp/studio-ui-next/m-series/m08/9800d604/webview2-100-golden-f09-v3/evidence/`；20-cycle owner/resource ledger 归零，formal run/reconcile/stop PASS |
| M08-E003 | Release publish/runtime matrix | PASS_DIRTY_CANDIDATE | `.tmp/studio-ui-next/m-series/m08/9800d604/publish-matrix-f09-v5/studio-ui-webview2-matrix.json`；7/7 runs、static/runtime/local no-Node PASS |
| M08-E004 | Windows native 125% | BLOCKED_NOT_PERFORMED | 当前 session 为 100%；没有修改系统缩放，也没有用 DPR/forced scale 替代 |
| M08-E005 | Independent no-Node target | NOT_PERFORMED | 当前机器有 Node，外部 CDP driver 使用 Node；local audit 不等于独立目标机 |
| M08-E006 | Current SHA Browser full | PASS | `146 passed / 26 explicit skipped / 0 failed`；Chromium only |
| M08-E007 | Current SHA Remote CI / Final Gate | NOT_RUN | 未 push、未触发、未代签 |

以上 evidence 的 JSON `sourceSha` 为当前 Git HEAD，但采集时 worktree dirty，因此不是 scope-clean final candidate。
下方 F09-E013 的 Release 125% 记录属于历史 `9dd69bd2b` 证据，不能迁移为当前
`f8f581932469f7c52fe547b7bcabe8ad45d89532` 产品基线的 125% PASS。

```text
MANIFEST_STATE=LOCAL_EVIDENCE_COMPLETE_REMOTE_CI_FAILED
CONFIGURED_PROFILE=NEXT_DEFAULT
EFFECTIVE_DEFAULT_UI_ROOT=STUDIO_UI_NEXT
NEXT_UI_DEFAULT_ENTRY=ENABLED
LEGACY_ROLE=FALLBACK_ONLY
F09_R_STATE=PARTIAL
F09_STATE=PARTIAL
FRONTEND_MIGRATION_MAINLINE=COMPLETE
F09_R2_PRODUCT_SOURCE_SHA=d1c82ba88e351a2d48bcfae7f97e047483dbba98
PRODUCT_SOURCE_FOLLOW_UP_SHAS=c83dcc114290cf73e5e8d9b91e7b49732db8ec68,1545bca25
EVIDENCE_RUN_SHA=9dd69bd2bde44e8ea5b7285bfd18f47e02f95007
BRANCH=studio-ui-next
HEAD=9dd69bd2bde44e8ea5b7285bfd18f47e02f95007
TRACKING_SHA=7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5
REMOTE_STUDIO_UI_NEXT_SHA=7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5
REMOTE_AUDIT_SHA_BEFORE_PUSH=c83dcc114290cf73e5e8d9b91e7b49732db8ec68
AHEAD_BEHIND=24/0
AUDIT_BRANCH=audit/f09-r2-d1c82ba88
AUDIT_BRANCH_HEAD=06eddf63c488266f818bc36e1d14d6aa0f798333
OFFICIAL_REMOTE_SHA=7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5
FINAL_SOURCE_SHA=9dd69bd2bde44e8ea5b7285bfd18f47e02f95007
FINAL_EVIDENCE_SHA=9dd69bd2bde44e8ea5b7285bfd18f47e02f95007
FINAL_DOC_SHA=7ccbe915dcee42978bb50caea53e51c7539ad18d
REMOTE_CI_RUN=30966530885
REMOTE_CI=FAIL
FINAL_GATE=FAIL
ROLLBACK_DRILL=PASS
WORKTREE_STATE=DIRTY_UNRELATED_TRACKED_CHANGES
```

本清单只索引实际获得的证据，并把代码候选会话门禁、历史失败和未执行范围明确分开。它不是发布签收，也不把后续文档修改伪装成应用源码或最终宿主证据。

| EvidenceId | 范围 | 状态 | 来源与边界 |
| --- | --- | --- | --- |
| F09-E001 | StudioUI lint | PASS | `9dd69bd2b` 当前工作树。 |
| F09-E002 | StudioUI typecheck | PASS | `9dd69bd2b` 当前工作树。 |
| F09-E003 | StudioUI unit | PASS | `npm run test:unit`，128 files / 792 tests。 |
| F09-E004 | StudioUI build | PASS | `npm run build`。 |
| F09-E005 | Bundle gate / reproducibility | PASS | `npm run bundle:ci`、`npm run bundle:verify`。 |
| F09-E006 | Architecture guards | PASS_32_OF_32 | `.tmp/studio-ui-next/f09/dotnet/desktop-architecture-9dd-r2/desktop-architecture.trx`。 |
| F09-E007 | F03 Workspace lifecycle | PASS_BEHAVIOR_ONLY | 外部管理静态服务下 54/54；受管 launcher teardown 仍单独记录。 |
| F09-E008 | Operator read-only projection | PASS_REAL_AUTHORITY | `9dd-profile-r3`：6 个只读路由、5 个禁止路由、4 个精确 `403`。 |
| F09-E009 | Rollback drill | PASS | `r-9dd-r1`：Next/Legacy/Next 同库回退、PersistenceRevision 4、无数据损失、无双 owner。 |
| F09-E010 | Final candidate Profile / Rollback / Final | PASS | `profiles/9dd-profile-r3`、`rollback/r-9dd-r1`、`final/9dd-final-r1`。 |
| F09-E011 | Product / Desktop full | PASS | Product `3872/3872` executed、2 existing skips；Desktop `812/812`；endpoints `427/427`。 |
| F09-E012 | Browser full | PASS | `.tmp/studio-ui-next/f09/browser/full-9dd-r3/browser-full.log`：141 passed / 26 skipped / 0 failed。 |
| F09-E013 | Release WebView2 125% / publish | PASS | `.tmp/studio-ui-next/f09/webview2/r-9dd-release-125-empty/studio-ui-no-node-evidence-9dd.json`。 |
| F09-E014 | Final user journey / soak | PASS | 2 restarts、20/20 cycles、GC/WeakRef/owner cleanup gates passed。 |
| F09-E015 | Independent no-Node target | NOT_PERFORMED | 当前证据使用外部 Node/CDP driver；Desktop 子进程树 Node descendant count 为 0，但未执行独立无 Node 目标机。 |
| F09-E016 | Full DPI matrix / field hardware / production soak | NOT_PERFORMED | 保留为 acceptance debt。 |
| F09-E017 | Remote CI required jobs | FAIL | [run 30966530885](https://github.com/HerverJun/ClearVision/actions/runs/30966530885)：`detection-measurement-data` 在 `Validate Measurement Performance Report` 因 `ColorMeasurement=FAIL` 失败；其余 required jobs 成功。 |
| F09-E018 | Final Gate | FAIL | [job 92188299730](https://github.com/HerverJun/ClearVision/actions/runs/30966530885/job/92188299730)：required `detection-measurement-data` 为 failure，故 Final Gate 原始结果为 failure。 |

## Provenance 规则

- F09-R2 产品修复锚点为 `d1c82ba88e351a2d48bcfae7f97e047483dbba98`，后续产品源修复为 `c83dcc114290cf73e5e8d9b91e7b49732db8ec68` 与 `1545bca25`；证据运行 HEAD 为 `9dd69bd2bde44e8ea5b7285bfd18f47e02f95007`。
- Profile、Rollback、Final runner 使用 canonical build、独立 `.tmp` 隔离、绝对路径校验和 shutdown diagnostics；当前 SHA 的 Profile/Rollback/Final evidence 均通过。
- runner 为场景隔离显式注入 `Studio__StartupProfile`，所以证据证明的是每个受控 Profile 合同与 `NEXT_DEFAULT` 配置投影，不把“无覆盖启动”扩大解释为独立 no-Node 证据。
- unattended shutdown 只接受显式 runner 参数，并要求数据库、运行目录和 diagnostics 位于 `.tmp` 隔离边界；强制退出或未知结果会使 cleanup evidence 失败。
- 所有临时 evidence 必须保留在 `.tmp/studio-ui-next/`；不将临时产物加入发布或文档事实。Remote CI run `30966530885` 的 clean checkout SHA 为 `06eddf63c488266f818bc36e1d14d6aa0f798333`；`detection-measurement-data` 的 validate-only gate 发现已提交 measurement report 中 `ColorMeasurement` 为 `FAIL`，不能通过本地未提交的生成报告替代。当前正式远端仍为 `7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5`，审计分支为 `audit/f09-r2-d1c82ba88`。

## Cutover 判定

```text
P0_COUNT=0
P1_OPEN=1
OPERATOR_READONLY_UI_PROJECTION=PASS
ROLLBACK_REPAIR=IMPLEMENTED_AND_RUNTIME_VERIFIED
ROLLBACK_DRILL=PASS
NEXT_RELEASE_STARTUP=PASS
LEGACY_FALLBACK_STARTUP=PASS
DATA_COMPATIBILITY=PASS
AUTHORITY_VIOLATION=0
CUTOVER=REMOTE_CI_FAILED
REMOTE_CI=FAIL
FINAL_GATE=FAIL
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

源码已记录修复后的 shutdown contract，并在当前 SHA 上完成真实 rollback close/flush 演练、Profile、Final journey 和 Release publish 125% 运行。独立 no-Node、完整 DPI 矩阵、现场硬件、Remote CI 和 Final Gate 仍分别按证据边界处理，不能互相替代。

产品源提交检查为 StudioUI lint、typecheck、unit、build、bundle reproducibility，Product/Desktop full、Desktop endpoints、architecture guards、Browser full 和当前 SHA 宿主证据均通过；Playwright 受管 webServer 在用例结束后的 teardown 仍需单独治理，不改变已落盘的 141/26/0 用例结果。

判定依据与执行步骤见 [F09_Cutover与Rollback操作手册.md](./F09_Cutover与Rollback操作手册.md)。

## Current TODO execution addendum (2026-08-07)

本节是当前 TODO 实现候选的新增证据，不覆盖上面的历史 F09/M-series 证据。当前 source anchor 是 `68e6e4286d008433f804ef90de00c8017184c177` 加 scoped working-tree diff；提交后会在 provenance follow-up 中记录实际 commit。

```text
CURRENT_TODO_AUDIT_BASELINE_HEAD=68e6e4286d008433f804ef90de00c8017184c177
CURRENT_TODO_BRANCH=studio-ui-next
CURRENT_TODO_REMOTE_HEAD=7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5
CURRENT_TODO_REMOTE_RELATION=REMOTE_ANCESTOR_AHEAD_37_NO_DIVERGENCE
CURRENT_TODO_WORKTREE=DIRTY_SCOPED_CANDIDATE_BEFORE_COMMIT
CURRENT_TODO_PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

| EvidenceId | 范围 | 状态 | 命令/来源 | 边界 |
| --- | --- | --- | --- | --- |
| TODO-E001 | StudioUI lint | `PASS` | `npm run lint` | 当前工作树；不是 release gate |
| TODO-E002 | StudioUI typecheck | `PASS` | `npm run typecheck` | Vue/Vitest/Node tsconfig |
| TODO-E003 | StudioUI unit | `PASS` | `npm run test:unit`，136 files / 837 tests | jsdom/Vitest |
| TODO-E004 | StudioUI build | `PASS` | `npm run build` | Vite build；`build:production` 未运行 |
| TODO-E005 | F03 workspace | `PASS` | Playwright Chromium，`f03-workspace.spec.ts` | 59/59；静态 fixture，不是 WebView2 |
| TODO-E006 | F04-R project lifecycle | `PASS` | Playwright Chromium，`f04-project-lifecycle.spec.ts` | 2/2；不证明 Template/Import/Demo 已全迁移 |
| TODO-E007 | Calibration endpoint authority | `PASS` | 串行 Desktop test，CalibrationDraftEndpointsTests | 4/4；formal save Operator 403，draft solve 权限仍 open |
| TODO-E008 | Contract/owner audit | `PASS_WITH_BLOCKED_SCOPE` | current source/config and route search | 无第二 transport/bridge/save chain；缺合同项停止 |
| TODO-E009 | `git diff --check` | `PASS` | repository worktree | 无 whitespace error |
| TODO-E010 | WebView2 125% / no-Node / field / Remote CI / Final Gate / soak | `NOT_PERFORMED` or `BLOCKED` | no command run in this session | 不以 Chromium 或历史证据代替 |

### Current implementation ledger

`FilePickerPort`、Inspector file/path/color、AI Pending file parameter、Template owner、N 点 calibration draft/solve/formal-save、GlobalVariables runtime values，以及 Results trend/distribution/report query owner属于当前候选实现范围。Template 完整 E2E、Project JSON import/export、AI attachments/resource binding、二维 calibration、results bulk export、line-sequence analysis、Station test package 和 advanced settings maintenance 仍分别受 `CV-AUDIT-045` 至 `CV-AUDIT-050` 或既有 F09 issue 阻断/部分证据限制。

正式 Project/Flow/GlobalVariables/assets authority、ProjectSaveCoordinator、AgentRun、authenticated HTTP/SSE、HostBridge 和 Canvas ownership 均保持既有边界；本轮没有新增第二 API transport、EventBus、ServiceRegistry、Canvas kernel、HostBridge 或 Project save client。

`PRODUCTION_ACCEPTANCE=NOT_GRANTED` 必须保持，直到产品负责人和真实宿主/目标机/现场证据完成签收。
