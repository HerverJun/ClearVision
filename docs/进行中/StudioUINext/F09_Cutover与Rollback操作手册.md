# F09 Cutover 与 Rollback 操作手册

```text
CURRENT_PROFILE=NEXT_DEFAULT_CANDIDATE
CURRENT_CUTOVER_STATE=FORBIDDEN
NEXT_DEFAULT=NOT_ACTIVE
LEGACY_FALLBACK=AVAILABLE
ROLLBACK_REQUIRES_RECOMPILE=NO
ROLLBACK_REQUIRES_REINSTALL=NO
ROLLBACK_REQUIRES_SOURCE_EDIT=NO
ROLLBACK_IS_CONFIG_LEVEL=YES
```

本手册的当前操作结论是“不切换”。它保留在满足门禁后可复核的配置级流程，并不授权绕过后端权限、修改项目数据或并行启动两个 UI owner。

## 12.6 默认入口门禁

| 门禁 | 当前状态 | 结论 |
| --- | --- | --- |
| `P0_COUNT=0` | PASS | 当前 P0 为 0。 |
| `CRITICAL_P1_COUNT=0` | FAIL | `F09-I001` 与 `F09-I002` 未关闭。 |
| `ROLLBACK_DRILL=PASS` | FAIL | 现有真实 manifest 为历史 FAIL；没有最终候选 PASS。 |
| `NEXT_RELEASE_STARTUP=PASS` | NOT PROVEN | runner 仍显式注入 Profile，未验证无覆盖默认启动。 |
| `LEGACY_FALLBACK_STARTUP=PASS` | NOT PROVEN | 缺最终候选的完整回退演练。 |
| `DATA_COMPATIBILITY=PASS` | NOT PROVEN | 缺最终候选端到端重启/回退数据证据。 |
| `AUTHORITY_VIOLATION=0` | PASS | Operator 的 `403` 是后端有意拒绝，不是可放宽的缺陷。 |

只要表中存在 `FAIL` 或 `NOT PROVEN`，不得把 `Studio:StartupProfile` 改为 `NEXT_DEFAULT`。

## 满足门禁后的切换步骤

1. 在干净、已提交的候选上串行完成 Profile、Rollback、Final、Desktop、Browser 和发布证据，并更新 [Final Evidence Manifest](./F09_FinalEvidenceManifest.md)。
2. 复核 `F09_OPEN_ISSUES.md`，确认 P0 为 0 且两个 P1 均关闭。
3. 在部署的权威启动配置中把 `Studio:StartupProfile` 设置为 `NEXT_DEFAULT`；不要同时手工改写 Profile 已投影的 feature flags。
4. 重启一个 Desktop Host 实例，确认启动日志和注入的 profile 都是 `NEXT_DEFAULT`，且只挂载一个 UI root/写 owner。
5. 使用 Admin、Engineer 和允许的 Operator 场景进行最小 smoke；所有执行与写入仍由 authenticated HTTP/SSE 和后端策略裁决。
6. 记录配置、候选 SHA、时间、操作者、结果和 evidence 路径，然后才可以声明 `NEXT_DEFAULT=ACTIVE`。

## 配置级回退步骤

1. 停止受影响的 Desktop Host，保留 diagnostics 和 evidence；不要删除数据库、项目文件、运行包或结果。
2. 在同一权威启动配置中设置 `Studio:StartupProfile=LEGACY_FALLBACK`，或使用等价的部署环境配置 `Studio__StartupProfile=LEGACY_FALLBACK`。
3. 重启一个 Host，确认启动日志显示 `LEGACY_FALLBACK`，没有 StudioUI root，且 Legacy root 是唯一 mounted owner。
4. 打开同一项目并验证只读与既有保存/数据访问链未被回退破坏；不得把前端缓存当作项目真相。
5. 记录回退时间、原因、配置、项目 identity、PersistenceRevision、证据路径和 owner 状态。问题解决后可按本手册重新启用 `NEXT_DEFAULT_CANDIDATE`，但不得跳过证据门禁直接转为 `NEXT_DEFAULT`。

## 异常处理

- Host close/flush 超时、双 owner、数据不一致、权限绕过或回退启动失败均是停止切换信号。
- 回退失败、数据损坏风险或同时挂载 Legacy/Next 写 owner 按 P0 处理；普通报告器/受管 launcher 清理缺口记入 P2，不以假阳性 PASS 收口。
- `LEGACY_DEFAULT` 与 `LEGACY_FALLBACK` 由 Profile catalog 投影。源代码、重新编译和重新安装不是正常回退步骤。
