# F09 Final Evidence Manifest

```text
MANIFEST_STATE=PARTIAL
CODE_CANDIDATE_SHA=5513f36c87508f6ae343ad6d77727ea3b4f056ee
FINAL_SOURCE_SHA=NOT_PRODUCED
DEFAULT_ENTRY=NEXT_DEFAULT_CANDIDATE
NEXT_UI_DEFAULT_ENTRY=NOT_ENABLED
F09_STATE=PARTIAL
```

本清单只索引实际获得的证据，并把代码候选会话门禁、历史失败和未执行明确分开。它不是发布签收，也不把后续文档提交伪装成应用源码证据。

| EvidenceId | 范围 | 状态 | 来源与边界 |
| --- | --- | --- | --- |
| F09-E001 | StudioUI lint | CODE_CANDIDATE_SESSION_PASS | `npm run lint`，代码候选 `5513f36c8750`。 |
| F09-E002 | StudioUI typecheck | CODE_CANDIDATE_SESSION_PASS | `npm run typecheck`。 |
| F09-E003 | StudioUI unit | CODE_CANDIDATE_SESSION_PASS | `npm run test:unit`，128 files / 790 tests。 |
| F09-E004 | StudioUI build | CODE_CANDIDATE_SESSION_PASS | `npm run build`。 |
| F09-E005 | Bundle gate / reproducibility | CODE_CANDIDATE_SESSION_PASS | `npm run bundle:ci`、`npm run bundle:verify`。 |
| F09-E006 | G15X architecture guard | CODE_CANDIDATE_SESSION_PASS | `tests/unit/g15x-capability-owners.test.mjs`，12/12。 |
| F09-E007 | F03 Workspace lifecycle | PASS | 外部管理静态服务下 54/54；证明行为，不证明受管 Playwright launcher teardown。 |
| F09-E008 | Operator read-only pilot | HISTORICAL_OR_CODE_LEVEL_PASS | 六个受认证只读路由含 `/stations`，五个禁止路由，四个精确 `403` 断言；最终候选尚未重跑，不等同于 Operator 可执行正式运行。 |
| F09-E009 | 历史 rollback drill | HISTORICAL_FAIL | `.tmp/studio-ui-next/f09/rollback/f09-rollback-ba2389d/studio-ui-rollback-evidence.json`：Host close/flush 超过 15 秒。 |
| F09-E010 | 最终候选 Profile / Rollback / Final | NOT RUN | runner 已加干净工作树、fresh build、canonical EXE 门禁；本候选尚未完成运行。 |
| F09-E011 | Product / Desktop full | NOT RUN | 不与已有 Desktop `dotnet` 进程并发执行。 |
| F09-E012 | Browser full（最终候选） | NOT RUN | F03 行为证据不替代完整 suite。 |
| F09-E013 | WebView2 100/125/150%、publish、no-Node、Remote CI、硬件/Station、production soak | NOT RUN | 没有本候选的可审计结果。 |

## Provenance 规则

- F09-E001 至 F09-E006 是本次任务在干净工作树、`HEAD=5513f36c8750` 时获得的控制台门禁结果；它们不是最终候选 manifest，也不覆盖下方的 `NOT RUN` 项。
- Profile、Rollback、Final runner 现在拒绝脏工作树、顶层 `-NoBuild` 和 caller-supplied Desktop EXE；首次运行构建 canonical candidate。
- 这些 runner 为场景隔离仍显式注入 `Studio__StartupProfile`。因此不存在未覆盖 `appsettings.json` 启动的最终证据，`NEXT_RELEASE_STARTUP=PASS` 不可声明。
- 受管 Playwright 的 Windows teardown 在测试通过后可能遭遇 `taskkill` access denied；外部管理静态服务的 F03 54/54 是有效行为证据，clean exit 仍是 `F09-I004` 的 P2。
- 所有临时 evidence 必须保留在 `.tmp/studio-ui-next/`；不将临时产物加入发布或文档事实。

## Cutover 判定

```text
P0_COUNT=0
CRITICAL_P1_COUNT=2
ROLLBACK_DRILL=HISTORICAL_FAIL
NEXT_RELEASE_STARTUP=NOT_PROVEN
LEGACY_FALLBACK_STARTUP=NOT_PROVEN_FOR_FINAL_CANDIDATE
DATA_COMPATIBILITY=NOT_PROVEN_FOR_FINAL_CANDIDATE
AUTHORITY_VIOLATION=0
CUTOVER=FORBIDDEN
```

判定依据与执行步骤见 [F09_Cutover与Rollback操作手册.md](./F09_Cutover与Rollback操作手册.md)。
