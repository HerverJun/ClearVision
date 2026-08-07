# F10 Studio UI Next 合同解阻与生产化执行台账

> 本文是 F10 当前执行 source of truth。状态只记录当前代码、测试和环境取得的证据，不把历史报告或未运行的门禁记为通过。

## 基线

```text
F10_BASELINE_SHA=5ec490727bc9b50c6963b1f955bc16efb594c9fd
CURRENT_HEAD=HEAD
REMOTE_HEAD=5ec490727bc9b50c6963b1f955bc16efb594c9fd
BRANCH=studio-ui-next
WORKTREE_STATE=CLEAN
IMPLEMENTATION_BASE=418406e620082fdedf46cd2a180b44a27c43d002
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

## Gate 状态

| Gate | 状态 | 当前证据 / blocker |
| --- | --- | --- |
| G0_REMOTE_CI | NOT_TRIGGERED | `.github/workflows/ci.yml` 不监听 `studio-ui-next` push；当前只取得本地基线，未触发远程 workflow。 |
| G1_PROJECT_CONTRACT | BLOCKED_BY_CONTRACT | 未发现 Next Project JSON import/export schema、文件承载和 lifecycle `clientOperationId`/reconcile 合同；沿用 `CV-AUDIT-047`，不新增第二 repository write。 |
| G1_AI_RESOURCE_CONTRACT | BLOCKED_BY_CONTRACT | 现有 FilePicker 只解决 scalar path 参数；正式 attachment/resource reference、上传、版本和权限承载合同仍需后端 owner，不能把本地路径冒充资源绑定。 |
| G1_CALIBRATION_CONTRACT | PARTIAL | N 点 draft/solve 现在要求 Engineer/Admin、非空且存在的 Project 上下文，formal asset save 继续复用既有权限与保存链；API 定向测试 `7/7`，二维比例/偏移正式资产合同仍缺，见 `CV-AUDIT-048`。 |
| G2_RESULTS_EXPORT | BLOCKED_BY_CONTRACT | 当前有趋势、分布和报告查询，但未发现整批 JSON/CSV export job、进度、取消和 unknown-outcome 合同，见 `CV-AUDIT-049`。 |
| G3_DEVICE_COMMANDS | PARTIAL | Station 测试包/正式包命令已有 `clientRequestId` 幂等、命令查询、过期收敛、权限与运行包身份校验；`StationEndpointsTests` `30/30`，Next 投影覆盖 pending、终态和激活身份不一致。Line sequence、Settings 高风险命令和完整 field reconcile 仍未形成 Next 合同，见 `CV-AUDIT-050`。 |
| G4_NEXT_UI_CONSUMPTION | PARTIAL | 已有 Project workspace、Calibration、Results analysis、Runtime package 等单一 owner；仅消费已冻结合同，未对缺口创建私有 fallback。 |
| G5_PARTIAL_EVIDENCE | PARTIAL | 已有 unit/contract 和部分 journey 证据；本轮补齐 N 点标定权限、Templates、GlobalVariables runtime、Results source lifecycle 与 Station owner/projection 的本地证据，但未形成完整浏览器、WebView2 或现场证据。 |
| G6_UX_HARDENING | NOT_PERFORMED | 尚未开始本轮集中 UI 收口。 |
| G7_WEBVIEW2 | NOT_PERFORMED | 当前未取得真实 WebView2 100%/125% 证据。 |
| G8_NO_NODE | NOT_PERFORMED | 当前未进行独立 no-Node 发布启动验证。 |
| G9_FIELD_HARDWARE | NOT_PERFORMED | 当前环境没有现场 Camera、PLC、Station 验证条件。 |
| G10_FINAL_CI | NOT_TRIGGERED | 远程 workflow 尚未对当前 F10 baseline 运行。 |

状态枚举：`DONE`、`PARTIAL`、`BLOCKED_BY_CONTRACT`、`BLOCKED_BY_ENVIRONMENT`、`NOT_PERFORMED`、`FAILED_RELATED`、`FAILED_UNRELATED`、`DEFERRED`。

## 架构权威

- Project、Flow、GlobalVariables、正式 assets、Runtime Package、Results 和 Station 状态继续由现有后端 Application Service、`ProjectSaveCoordinator`、Runtime/Station 链路负责。
- Studio UI Next 继续复用唯一 API transport、Host adapter、canonical FlowCanvas/ImageCanvas、现有保存链和 capability-local lifecycle owner。
- 本轮不新增第二 API transport、HostBridge、EventBus、Project repository、Calibration asset authority、Station command authority 或前端私有持久化链。
- Production acceptance 不由本台账自动授予；软件门禁与真实 WebView2、DPI、no-Node、现场硬件、生产 soak 和产品 owner 签收分别记录。

## 当前工作记录

### G0 Remote CI

- 已执行 `git fetch origin --prune`，远端 `studio-ui-next` 与本地基线一致。
- 已审计 workflow 触发条件；普通 `studio-ui-next` push 不触发完整 CI。
- 当前 `REMOTE_CI=NOT_TRIGGERED`，不修改 workflow trigger，不合并，不改 main。

### G1 Calibration authorization

- `POST /api/calibration/npoint-draft/solve` 复用 `RequireEngineerOrAdmin`，拒绝 Operator 和未认证会话；空 `ProjectId` 返回 `PROJECT_CONTEXT_REQUIRED`，不存在工程返回 `PROJECT_NOT_FOUND`。
- draft solve 仍只产生 draft/candidate/preview artifact，不写正式 Project asset；正式保存继续走既有 `CanEditProject` 和 `ProjectService` 链。
- `CalibrationDraftEndpointsTests` `7/7` 通过；`calibrationOwner` 对 solve 与 formal save 的 `403` 提示分别投影，相关 UI unit 纳入全量 `838/838`。

### G3 Station package and command evidence

- 当前 backend 已具备 `clientRequestId` 幂等、同请求查询、过期命令收敛、Station admin 权限、正式包身份完整性与部署准入检查。
- `StationEndpointsTests` 在 `Logging__EventLog__LogLevel__Default=None` 且允许既有 AppData fixture 写入的受控进程环境下 `30/30` 通过；默认沙箱首次失败归类为 EventLog/AppData 权限，不改产品代码或目录 ACL。
- Next 只读 Station projection 保留 command-created/in-progress/terminal-failed/awaiting-active-identity/identity-mismatch/succeeded 状态；这不等同于现场 Station、PLC、Camera 或完整 unknown-outcome soak 已验收。

### Contract blockers retained

- Project JSON import/export、AI attachment/resource reference、planar calibration formal asset、Results bulk export job、line sequence auto-tune 和 Advanced Settings 仍按合同缺口记录，不新增第二套 authority。
- Remote CI、Final Gate、WebView2 125%、独立 no-Node、现场硬件与生产 soak 仍未取得证据。

## 测试与真实环境

| 证据 | 状态 |
| --- | --- |
| `npm run lint` | PASS |
| `npm run typecheck` | PASS |
| `npm run test:unit` | PASS；136 个文件、838 个测试 |
| `npm run build` | PASS；Vite 转换 499 modules |
| Browser / Playwright | NOT PERFORMED（F10 变更前） |
| Product/Desktop build | PASS；0 warning、0 error |
| Product/Desktop targeted | PASS；Calibration `7/7`，StationEndpoints `30/30`（受控日志/AppData 环境） |
| Remote CI | NOT_TRIGGERED |
| Final Gate | NOT_TRIGGERED |
| WebView2 100% | NOT PERFORMED |
| WebView2 125% | NOT PERFORMED |
| Independent no-Node | NOT PERFORMED |
| Camera / PLC / Station | NOT PERFORMED |
| Production soak | NOT PERFORMED |

## 提交

本轮变更保持为权限闭环、定向测试和台账更新的 scoped candidate；提交不会自动授予生产验收。
