# F09 Final Evidence Manifest

```text
MANIFEST_STATE=ENGINEERING_DONE_WITH_ACCEPTANCE_DEBT
CONFIGURED_PROFILE=NEXT_DEFAULT
EFFECTIVE_DEFAULT_UI_ROOT=STUDIO_UI_NEXT
NEXT_UI_DEFAULT_ENTRY=ENABLED_IN_CONFIGURATION
LEGACY_ROLE=FALLBACK_ONLY
F09_STATE=ENGINEERING_DONE_WITH_ACCEPTANCE_DEBT
FINAL_SOURCE_SHA=NOT_PRODUCED
FINAL_DOC_SHA=WORKTREE_UNCOMMITTED
```

本清单只索引实际获得的证据，并把代码候选会话门禁、历史失败和未执行范围明确分开。它不是发布签收，也不把后续文档修改伪装成应用源码或最终宿主证据。

| EvidenceId | 范围 | 状态 | 来源与边界 |
| --- | --- | --- | --- |
| F09-E001 | StudioUI lint | CODE_CANDIDATE_SESSION_PASS | `npm run lint`。 |
| F09-E002 | StudioUI typecheck | CODE_CANDIDATE_SESSION_PASS | `npm run typecheck`。 |
| F09-E003 | StudioUI unit | CODE_CANDIDATE_SESSION_PASS | `npm run test:unit`，128 files / 792 tests。 |
| F09-E004 | StudioUI build | CODE_CANDIDATE_SESSION_PASS | `npm run build`。 |
| F09-E005 | Bundle gate / reproducibility | CODE_CANDIDATE_SESSION_PASS | `npm run bundle:ci`、`npm run bundle:verify`。 |
| F09-E006 | Architecture guards | CODE_CANDIDATE_SESSION_PASS | StudioUI owner guard 与 Desktop 定向架构测试。 |
| F09-E007 | F03 Workspace lifecycle | PASS_BEHAVIOR_ONLY | 外部管理静态服务下 54/54；不证明受管 Playwright launcher teardown。 |
| F09-E008 | Operator read-only projection | PASS_CODE_LEVEL | 六个受认证只读路由含 `/stations`，禁止路由与精确 `403` 断言通过；最终候选尚未重跑。 |
| F09-E009 | Historical rollback drill | HISTORICAL_FAIL | 旧 manifest 显示 Host close/flush 超过 15 秒；这是本轮修复的根因输入。 |
| F09-E010 | Final candidate Profile / Rollback / Final | NOT_RUN | runner 已增加干净工作树、fresh build、canonical EXE、isolated `.tmp` 和 shutdown diagnostics 门禁；最终候选尚未运行。 |
| F09-E011 | Product / Desktop full | NOT_RUN | 未在同一 Desktop 项目并发启动额外 `dotnet test`。 |
| F09-E012 | Browser full（最终候选） | NOT_RUN | F03 行为证据不替代完整 suite。 |
| F09-E013 | WebView2 100/125/150%、publish、no-Node、Remote CI、硬件/Station、production soak | NOT_RUN | 没有本次最终源码 SHA 的可审计结果。 |

## Provenance 规则

- 当前修改尚未形成干净提交；因此不声明 `FINAL_SOURCE_SHA`，也不把工作树状态当作最终源码证据。
- Profile、Rollback、Final runner 拒绝脏工作树、顶层 `-NoBuild` 和 caller-supplied Desktop EXE；首次运行构建 canonical fresh build。
- runner 为场景隔离仍显式注入 `Studio__StartupProfile`。因此“无显式 Profile 覆盖”的最终启动证据仍为 `NOT RUN`。
- unattended shutdown 只接受显式 runner 参数，并要求数据库、运行目录和 diagnostics 位于 `.tmp` 隔离边界；强制退出或未知结果会使 cleanup evidence 失败。
- 所有临时 evidence 必须保留在 `.tmp/studio-ui-next/`；不将临时产物加入发布或文档事实。

## Cutover 判定

```text
P0_COUNT=0
P1_OPEN=1
OPERATOR_READONLY_UI_PROJECTION=PASS
ROLLBACK_REPAIR=IMPLEMENTED_AND_STATICALLY_VERIFIED
ROLLBACK_DRILL=NOT_RUN_FINAL_CANDIDATE
NEXT_RELEASE_STARTUP=NOT_RUN
LEGACY_FALLBACK_STARTUP=NOT_RUN_FINAL_CANDIDATE
DATA_COMPATIBILITY=NOT_PROVEN_FINAL_CANDIDATE
AUTHORITY_VIOLATION=0
CUTOVER=CONFIGURED_NOT_ACCEPTED
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

源码已记录修复后的 shutdown contract，并配置了 `NEXT_DEFAULT` 入口；没有执行真实 WebView2 close/flush rollback drill、DPI matrix、publish/no-Node、Remote CI 或现场验证。`ROLLBACK_DRILL=PASS` 只能在最终候选真实演练完成且数据、owner 和 diagnostics 证据齐全后写入。

当前工作树检查为 StudioUI lint、typecheck、unit、build，Desktop build、Desktop 定向测试和 PowerShell runner parse PASS；这些结果不替代最终源码或真实 WebView2 证据。

判定依据与执行步骤见 [F09_Cutover与Rollback操作手册.md](./F09_Cutover与Rollback操作手册.md)。
