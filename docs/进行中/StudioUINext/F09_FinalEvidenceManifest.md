# F09 Final Evidence Manifest

```text
MANIFEST_STATE=LOCAL_EVIDENCE_COMPLETE_AWAITING_REMOTE_CI
CONFIGURED_PROFILE=NEXT_DEFAULT
EFFECTIVE_DEFAULT_UI_ROOT=STUDIO_UI_NEXT
NEXT_UI_DEFAULT_ENTRY=ENABLED
LEGACY_ROLE=FALLBACK_ONLY
F09_R_STATE=LOCAL_EVIDENCE_COMPLETE
F09_STATE=ENGINEERING_DONE_WITH_ACCEPTANCE_DEBT
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
AUDIT_BRANCH_HEAD=PENDING_AUDIT_PUSH
OFFICIAL_REMOTE_SHA=7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5
FINAL_SOURCE_SHA=9dd69bd2bde44e8ea5b7285bfd18f47e02f95007
FINAL_EVIDENCE_SHA=9dd69bd2bde44e8ea5b7285bfd18f47e02f95007
FINAL_DOC_SHA=PENDING_DOCUMENTATION_COMMIT
REMOTE_CI=AWAITING
FINAL_GATE=AWAITING
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

## Provenance 规则

- F09-R2 产品修复锚点为 `d1c82ba88e351a2d48bcfae7f97e047483dbba98`，后续产品源修复为 `c83dcc114290cf73e5e8d9b91e7b49732db8ec68` 与 `1545bca25`；证据运行 HEAD 为 `9dd69bd2bde44e8ea5b7285bfd18f47e02f95007`。
- Profile、Rollback、Final runner 使用 canonical build、独立 `.tmp` 隔离、绝对路径校验和 shutdown diagnostics；当前 SHA 的 Profile/Rollback/Final evidence 均通过。
- runner 为场景隔离显式注入 `Studio__StartupProfile`，所以证据证明的是每个受控 Profile 合同与 `NEXT_DEFAULT` 配置投影，不把“无覆盖启动”扩大解释为独立 no-Node 证据。
- unattended shutdown 只接受显式 runner 参数，并要求数据库、运行目录和 diagnostics 位于 `.tmp` 隔离边界；强制退出或未知结果会使 cleanup evidence 失败。
- 所有临时 evidence 必须保留在 `.tmp/studio-ui-next/`；不将临时产物加入发布或文档事实。当前正式远端仍为 `7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5`，审计分支为 `audit/f09-r2-d1c82ba88`，当前尚未推送本轮最终 HEAD。

## Cutover 判定

```text
P0_COUNT=0
P1_OPEN=0
OPERATOR_READONLY_UI_PROJECTION=PASS
ROLLBACK_REPAIR=IMPLEMENTED_AND_RUNTIME_VERIFIED
ROLLBACK_DRILL=PASS
NEXT_RELEASE_STARTUP=PASS
LEGACY_FALLBACK_STARTUP=PASS
DATA_COMPATIBILITY=PASS
AUTHORITY_VIOLATION=0
CUTOVER=LOCAL_EVIDENCE_READY_AWAITING_REMOTE_CI
REMOTE_CI=AWAITING
FINAL_GATE=AWAITING
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

源码已记录修复后的 shutdown contract，并在当前 SHA 上完成真实 rollback close/flush 演练、Profile、Final journey 和 Release publish 125% 运行。独立 no-Node、完整 DPI 矩阵、现场硬件、Remote CI 和 Final Gate 仍分别按证据边界处理，不能互相替代。

产品源提交检查为 StudioUI lint、typecheck、unit、build、bundle reproducibility，Product/Desktop full、Desktop endpoints、architecture guards、Browser full 和当前 SHA 宿主证据均通过；Playwright 受管 webServer 在用例结束后的 teardown 仍需单独治理，不改变已落盘的 141/26/0 用例结果。

判定依据与执行步骤见 [F09_Cutover与Rollback操作手册.md](./F09_Cutover与Rollback操作手册.md)。
