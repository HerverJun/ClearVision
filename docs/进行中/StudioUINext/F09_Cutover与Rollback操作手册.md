# F09 Cutover 与 Rollback 操作手册

```text
CURRENT_PROFILE=NEXT_DEFAULT
CURRENT_CUTOVER_STATE=LOCAL_EVIDENCE_READY_AWAITING_REMOTE_CI
NEXT_DEFAULT=CONFIGURED_LOCAL_EVIDENCE_READY
EFFECTIVE_DEFAULT_UI_ROOT=STUDIO_UI_NEXT
LEGACY_FALLBACK=AVAILABLE
F09_R_STATE=LOCAL_EVIDENCE_COMPLETE
F09_STATE=ENGINEERING_DONE_WITH_ACCEPTANCE_DEBT
F_PLAN_SERIES=CLOSED_LOCAL_EVIDENCE
M_SERIES_ENTRY=READY_AFTER_REMOTE_AUDIT
F09_R2_PRODUCT_SOURCE_SHA=d1c82ba88e351a2d48bcfae7f97e047483dbba98
PRODUCT_SOURCE_FOLLOW_UP_SHAS=c83dcc114290cf73e5e8d9b91e7b49732db8ec68,1545bca25
EVIDENCE_RUN_SHA=9dd69bd2bde44e8ea5b7285bfd18f47e02f95007
AUDIT_BRANCH=audit/f09-r2-d1c82ba88
AUDIT_BRANCH_HEAD=PENDING_AUDIT_PUSH
OFFICIAL_REMOTE_SHA=7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5
ROLLBACK_DRILL=PASS
REMOTE_CI=AWAITING
FINAL_GATE=AWAITING
PRODUCTION_ACCEPTANCE=NOT_GRANTED
ROLLBACK_REQUIRES_RECOMPILE=NO
ROLLBACK_REQUIRES_REINSTALL=NO
ROLLBACK_REQUIRES_SOURCE_EDIT=NO
ROLLBACK_IS_CONFIG_LEVEL=YES
```

当前权威配置已经是 `NEXT_DEFAULT`，它表示工程默认 UI root 已切换为 `STUDIO_UI_NEXT`，不表示生产签收已经完成。`LEGACY_FALLBACK` 仍可通过同一权威配置和重启恢复；任何时刻只能挂载一个 UI root 和一套 capability owner。

## 当前门禁

| 门禁 | 当前状态 | 结论 |
| --- | --- | --- |
| `P0_COUNT=0` | PASS | 当前没有 P0。 |
| `P1_OPEN=0` | PASS | Operator 权限合同和 Host rollback 均已完成当前本地门禁。 |
| `P2_OPEN=3` | DEBT | Playwright teardown、独立 no-Node/完整 DPI/现场验收和无覆盖启动仍分别记录在问题台账。 |
| `OPERATOR_READONLY_UI_PROJECTION=PASS` | PASS | 产品决策移除了 Operator 正式运行要求，后端权限没有放宽。 |
| `ROLLBACK_REPAIR` | IMPLEMENTED_AND_RUNTIME_VERIFIED | shutdown diagnostics、隔离 unattended 门禁和 runner 读取已实现，并在 `r-9dd-r1` 真实演练通过。 |
| `ROLLBACK_DRILL=PASS` | PASS | Next/缺失资源/Legacy/Next 同库回退通过；无强制退出、数据损失、双 owner 或 deadline violation。 |
| `NEXT_RELEASE_STARTUP=PASS` | PASS | Profile evidence 的 `NEXT_DEFAULT` default entry 与命名 Next profiles 通过；受控 runs 使用显式 Profile。 |
| `LEGACY_FALLBACK_STARTUP=PASS` | PASS | `LEGACY_DEFAULT` 和 `LEGACY_FALLBACK` 启动、owner ledger 与回退数据核对通过。 |
| `DATA_COMPATIBILITY=PASS` | PASS | 同一 ProjectId 在 Legacy/Next 间保持一致，最终 `PersistenceRevision=4`。 |
| `AUTHORITY_VIOLATION=0` | PASS | Operator 的 `403` 是后端有意拒绝，不是可放宽的缺陷。 |
| `REMOTE_CI=AWAITING` | PENDING | 审计分支推送后触发并核验 required jobs 与 artifacts。 |
| `FINAL_GATE=AWAITING` | PENDING | Remote CI 通过后核验 Final Gate 原始结果。 |

当前配置已为 `NEXT_DEFAULT`，本地工程证据已完成，但在 Remote CI/Final Gate 通过前不得声明生产接受或推送正式 `studio-ui-next`。独立 no-Node、完整 DPI、现场硬件和生产 soak 即使后续仍为 acceptance debt，也不得被本地证据替代。

## 默认入口验收步骤

1. 保留当前已完成的 `9dd69bd2b` Profile、Rollback、Final、Desktop、Browser 和 Release 125% evidence；确认产品源代码 SHA 与证据运行 SHA 分开记录。
2. 仅提交 F09 文档和根 README；保留已有 `ClearVision.Product/test_results/*` tracked 修改，不把它们纳入本轮提交。
3. `git fetch origin --prune` 后，将当前 HEAD fast-forward 推送到 `audit/f09-r2-d1c82ba88`，并记录审计分支 SHA。
4. 触发并核验 Remote CI、required jobs、关键 artifacts 和 Final Gate；未全部通过前保持正式远端不变。
5. 通过后才可将 `REMOTE_CI`/`FINAL_GATE` 标为 `PASS`，再将同一 HEAD fast-forward 推送到 `studio-ui-next`。
3. 确认权威配置保持 `Studio:StartupProfile=NEXT_DEFAULT`；不要额外手工覆盖由 Profile 投影的 root/workspace flags。
4. 重启一个 Desktop Host，确认启动日志和注入 profile 都是 `NEXT_DEFAULT`，且只挂载一个 UI root/写 owner。
5. 使用 Admin、Engineer 和 Operator 的产品合同场景进行最小 smoke；所有执行与写入仍由 authenticated HTTP/SSE 和后端 policy 裁决。
6. 记录配置、候选 SHA、时间、操作者、结果和 evidence 路径；`NEXT_DEFAULT=ACCEPTED` 仍需明确区分工程门禁与生产接受。

## 配置级回退步骤

1. 停止受影响的 Desktop Host，保留 diagnostics 和 evidence；不要删除数据库、项目文件、运行包或结果。
2. 在同一权威启动配置中设置 `Studio:StartupProfile=LEGACY_FALLBACK`，或使用等价的部署环境配置 `Studio__StartupProfile=LEGACY_FALLBACK`。
3. 重启一个 Host，确认启动日志显示 `LEGACY_FALLBACK`，没有 StudioUI root，且 Legacy root 是唯一 mounted owner。
4. 打开同一项目并验证只读与既有保存/数据访问链未被回退破坏；不得把前端缓存当作项目真相。
5. 记录回退时间、原因、配置、项目 identity、PersistenceRevision、证据路径和 owner 状态。问题解决后切回 `NEXT_DEFAULT`；`NEXT_DEFAULT_CANDIDATE` 仅保留给兼容/历史 evidence runner，不作为当前默认语义。

## 异常处理

- Host close/flush 超时、双 owner、数据不一致、权限绕过或回退启动失败均是停止接受信号。
- 回退失败、数据损坏风险或同时挂载 Legacy/Next 写 owner 按 P0 处理；普通报告器/受管 launcher 清理缺口记入 P2，不以假阳性 PASS 收口。
- 生产行为保留人工确认；unattended shutdown 只能由明确 runner 参数启用，并且所有隔离数据库、运行目录、日志和 diagnostics 必须位于 `.tmp` 边界。
- `LEGACY_DEFAULT` 与 `LEGACY_FALLBACK` 由 Profile catalog 投影。源代码、重新编译和重新安装不是正常回退步骤。
