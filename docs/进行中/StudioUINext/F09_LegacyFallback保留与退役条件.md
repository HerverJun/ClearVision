# F09 Legacy Fallback 保留与退役条件

```text
LEGACY_ROLE=CONFIGURABLE_FALLBACK
LEGACY_PHYSICAL_REMOVAL_IN_F09=NO
LEGACY_NEW_FEATURE_DEVELOPMENT=NOT_DECIDED
DEFAULT_ENTRY=NEXT_DEFAULT_CANDIDATE
```

F09 不删除 Legacy。Legacy 是配置级的恢复路径，服务于风险隔离、历史兼容和未迁移低频维护能力；它不是可与 Next 同时挂载的第二写入 owner。F09 不建立 Legacy 新功能冻结的产品政策；未经单独批准的功能扩张不属于本阶段范围。

## 保留范围

| 范围 | F09 决策 | 原因 |
| --- | --- | --- |
| Legacy UI root | 保留 | `LEGACY_FALLBACK` 必须可在下一次启动选择。 |
| 数据库高级 restore/repair/cleanup/global reset | 保留 | `F09-I006`，未迁移到 Next。 |
| Demo/template 创建路径 | 保留但受后端 `CanEditProject` 保护 | `F09-I003`，不是 Next lifecycle 能力。 |
| 既有项目与 Runtime/Station 链路 | 保留后端权威 | 前端 Profile 不得替代 ProjectSaveCoordinator、Runtime 或 Station。 |

## 运行规则

- 任一进程只选择一个 Profile 和一个 mounted UI root；切换必须停止旧 Host，再按 [Cutover 与 Rollback 手册](./F09_Cutover与Rollback操作手册.md) 重启。
- Legacy 不获得新的产品能力。可接受安全、数据保护、回退可靠性和兼容性修复。
- Legacy 的可用性不证明 Next 默认入口已经就绪；反之，Next 候选不能以 CSS 隐藏 Legacy 来冒充卸载。
- Project、Flow、GlobalVariables、正式 assets、结果、运行包和 Station 继续以现有服务端 authority 为准。

## 退役前置条件

Legacy 物理删除不属于 F09。只有在另行批准的维护阶段同时满足下列条件后才可以评估：

1. `NEXT_DEFAULT` 已稳定运行，且完整回退、数据兼容、WebView2、发布/no-Node、CI 和现场验收均有可审计证据。
2. `F09-I003`、`F09-I006` 与与 Legacy 相关的用户路径已迁移、替代或获得产品弃用批准。
3. 存量项目、运行包、数据库维护和客户支持路径已经有正式保留/迁移方案。
4. 独立 ADR、回退窗口、数据备份策略和发布审批已完成；删除动作经过单独变更评审。

在上述条件前，`LEGACY_FALLBACK` 继续保留，且任何未来删除任务不得借 F09 完成状态隐式执行。
