# F09 Cutover 与 Rollback 操作手册

```text
CURRENT_PROFILE=NEXT_DEFAULT
CURRENT_CUTOVER_STATE=CONFIGURED_NOT_ACCEPTED
NEXT_DEFAULT=CONFIGURED_NOT_ACCEPTED
EFFECTIVE_DEFAULT_UI_ROOT=STUDIO_UI_NEXT
LEGACY_FALLBACK=AVAILABLE
ROLLBACK_REQUIRES_RECOMPILE=NO
ROLLBACK_REQUIRES_REINSTALL=NO
ROLLBACK_REQUIRES_SOURCE_EDIT=NO
ROLLBACK_IS_CONFIG_LEVEL=YES
```

当前权威配置已经是 `NEXT_DEFAULT`，它表示工程默认 UI root 已切换为 `STUDIO_UI_NEXT`，不表示生产签收已经完成。`LEGACY_FALLBACK` 仍可通过同一权威配置和重启恢复；任何时刻只能挂载一个 UI root 和一套 capability owner。

## 12.6 当前门禁

| 门禁 | 当前状态 | 结论 |
| --- | --- | --- |
| `P0_COUNT=0` | PASS | 当前没有 P0。 |
| `P1_OPEN=1` | FAIL | 仅 `F09-I002` 仍待最终候选 rollback drill。 |
| `OPERATOR_READONLY_UI_PROJECTION=PASS` | PASS | 产品决策移除了 Operator 正式运行要求，后端权限没有放宽。 |
| `ROLLBACK_REPAIR` | IMPLEMENTED | shutdown diagnostics、隔离 unattended 门禁和 runner 读取已实现。 |
| `ROLLBACK_DRILL=PASS` | NOT RUN | 真实最终候选 WebView2 drill 尚未执行，不能写成 PASS。 |
| `NEXT_RELEASE_STARTUP=PASS` | NOT RUN | runner 为隔离场景显式注入 Profile，未验证无覆盖启动。 |
| `LEGACY_FALLBACK_STARTUP=PASS` | NOT RUN | 缺最终候选的完整回退演练。 |
| `DATA_COMPATIBILITY=PASS` | NOT PROVEN | 缺最终候选端到端重启/回退数据证据。 |
| `AUTHORITY_VIOLATION=0` | PASS | Operator 的 `403` 是后端有意拒绝，不是可放宽的缺陷。 |

当前配置已为 `NEXT_DEFAULT`，但只要表中的 `FAIL` 或 `NOT RUN` 尚未补齐，就不得声明生产接受、最终 gate 或 `ROLLBACK_DRILL=PASS`。

## 默认入口验收步骤

1. 在干净、已提交的候选上串行完成 Profile、Rollback、Final、Desktop、Browser 和发布证据，并更新 [Final Evidence Manifest](./F09_FinalEvidenceManifest.md)。
2. 复核 `F09_OPEN_ISSUES.md`，确认 F09-I001 已关闭、F09-I002 已通过真实演练，P0/P1 计数和 evidence provenance 一致。
3. 确认权威配置保持 `Studio:StartupProfile=NEXT_DEFAULT`；不要额外手工覆盖由 Profile 投影的 root/workspace flags。
4. 重启一个 Desktop Host，确认启动日志和注入 profile 都是 `NEXT_DEFAULT`，且只挂载一个 UI root/写 owner。
5. 使用 Admin、Engineer 和 Operator 的产品合同场景进行最小 smoke；所有执行与写入仍由 authenticated HTTP/SSE 和后端 policy 裁决。
6. 记录配置、候选 SHA、时间、操作者、结果和 evidence 路径，然后才可以声明 `NEXT_DEFAULT=ACCEPTED`。

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
