# Backend -> Studio UI Next 反向可达性审计

`BACKEND_ORPHANED` 要求同时满足：Legacy 用户调用者已建立、后端能力仍存在、Studio UI Next 无可达调用链。Demo 以 `ENTRY_MISSING` 作为主状态，但仍列入本页的孤儿特征清单。

| Group | Legacy caller | Backend authority | Next reachability | Classification |
| --- | --- | --- | --- | --- |
| Demo / 示例工程 | `projectManager.js:162,186` | `DemoEndpoints.cs:12,26,40` | StudioUI 无 `/demo/create*` 或 guide caller | ENTRY_MISSING + orphan characteristic |
| Operator recommendation | `propertyPanel.js:2358-2456` | `ApiEndpoints.cs:1672` | Inspector 无 recommend/accept/revert caller | BACKEND_ORPHANED |
| Database advanced | `settingsApi.js:11-15`; `systemTabs.js` | `SettingsEndpoints.cs:185,227,254` | Next 只有 status/backup，合同显式排除其余 | BACKEND_ORPHANED; ADR deferred |
| Runtime Preview Pilot | `settingsApi.js:33-58`; `runtimePreviewPilotConsole.js` | `SettingsEndpoints.cs:590-1429` | Next 只有 exclusion marker，无 panel/caller | BACKEND_ORPHANED; Legacy fallback |

## Call-chain Notes

### Demo

Legacy `ProjectView -> ProjectManager.createDemoProject -> POST /api/demo/create | create-simple -> DemoProjectService`，并读取 `/api/demo/guide`。Next Projects route、page 和 lifecycle owner 存在，但没有该入口或调用者。ADR-G2 的 `RELOCATE` 是重新进入条件，不是迁移完成。

### Operator recommendation

Legacy `PropertyPanel recommend button -> recommendParameters -> POST /api/operators/{type}/recommend-parameters -> ParameterRecommender -> accept/revert local candidate`。Next Inspector 只覆盖常规参数草稿，因此后端服务、DTO 与 endpoint 成为不可达能力。

### Database advanced

Legacy `Database tab -> repair/restore/cleanup/reset controls -> Settings API -> Admin maintenance service`。Next 有 status/backup 安全子集，但 contracts 明确排除破坏性操作。ADR-G2 要求 Admin-only、`clientOperationId + revision/backupId`、互斥、审计、timeout 与 unknown-outcome reconcile，当前延期合理，但 parity 仍不完整。

### Runtime Preview Pilot

Legacy conditional settings tab/console 消费一组 config、catalog、readiness、sessions、reports、replay、export、deploy、package、manifest、retention、station profile 和 governance endpoints。Next 没有 owner 或 UI caller。应先决定其产品/内部工具归属；在产品 owner 批准退休前不能写成 `INTENTIONALLY_RETIRED`。

## Endpoint-only Candidates Kept NOT_VERIFIED

| Endpoint | Evidence | Why not orphaned |
| --- | --- | --- |
| `POST /api/operators/{type}/preview` | `ApiEndpoints.cs:1694` | 未建立 Legacy UI caller；可能是服务级或废弃合同，不能推断迁移回归 |
| `POST /api/images/upload` | `ApiEndpoints.cs:2178` | Legacy 与 Next 均未建立 caller；需由 owner 解释预期消费者 |

## Non-orphans Cleared

Project import/export、Planar calibration、Line Sequence、Results bulk export、Runtime Package 与 Station command/package 均在当前 F10/代码中找到 owner 或调用链，不列孤儿清单。AI attachment/model/template/calibration projection 是已冻结的合同缺口，但未证明为 Legacy UI 可用能力，因此列在 open questions，而不是伪装成迁移回归。
