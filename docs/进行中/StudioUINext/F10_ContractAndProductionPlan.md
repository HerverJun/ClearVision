# F10 Studio UI Next 合同解阻与生产化执行台账

> 本文是 F10 当前执行 source of truth。状态只记录当前代码、测试和环境取得的证据，不把历史报告或未运行的门禁记为通过。

## 基线

```text
F10_STATE=ACTIVE
F10_BASELINE_SHA=eaafef9f09c2b39542e0a675a995dd00fc9331b2
CURRENT_HEAD=HEAD
REMOTE_HEAD=HEAD
BRANCH=studio-ui-next
WORKTREE_STATE=CLEAN
IMPLEMENTATION_BASE=eaafef9f09c2b39542e0a675a995dd00fc9331b2
PROJECT_IMPORT_EXPORT=BLOCKED_BY_CONTRACT
DEMO_RECONCILE=BLOCKED_BY_CONTRACT
AI_ATTACHMENT_RESOURCE=BLOCKED_BY_CONTRACT
NPOINT_AUTHORIZATION=DONE
PLANAR_CALIBRATION=PARTIAL
RESULTS_BULK_EXPORT=DONE
LINE_SEQUENCE=BLOCKED_BY_CONTRACT
STATION_TEST_PACKAGE=PARTIAL
DATABASE_ADVANCED=BLOCKED_BY_CONTRACT
PARTIAL_EVIDENCE=PARTIAL
WEBVIEW2_125=NOT_PERFORMED
INDEPENDENT_NO_NODE=NOT_PERFORMED
REMOTE_CI=NOT_PERFORMED
FINAL_GATE=NOT_PERFORMED
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

## Gate 状态

| Gate | 状态 | 当前证据 / blocker |
| --- | --- | --- |
| G0_REMOTE_CI | NOT_PERFORMED | `.github/workflows/ci.yml` 不监听 `studio-ui-next` push；本轮未触发远程 workflow。 |
| G1_PROJECT_CONTRACT | BLOCKED_BY_CONTRACT | 未发现 Next Project JSON import/export schema、文件承载和 lifecycle `clientOperationId`/reconcile 合同；沿用 `CV-AUDIT-047`，不新增第二 repository write。 |
| G1_AI_RESOURCE_CONTRACT | BLOCKED_BY_CONTRACT | 现有 FilePicker 只解决 scalar path 参数；正式 attachment/resource reference、上传、版本和权限承载合同仍需后端 owner，不能把本地路径冒充资源绑定。 |
| G1_CALIBRATION_CONTRACT | PARTIAL | N 点 draft/solve 现在要求 Engineer/Admin、非空且存在的 Project 上下文，formal asset save 继续复用既有权限与保存链；API 定向测试 `7/7`，二维比例/偏移正式资产合同仍缺，见 `CV-AUDIT-048`。 |
| G2_RESULTS_EXPORT | DONE | 已补齐服务端 CSV/JSON export job、clientOperationId 幂等与对账、快照上界、取消、TTL、SHA-256 产物校验和权限错误映射；Results 页面仅对本机结果开放，Station 来源明确不支持。Application `5/5`、Desktop endpoint `4/4`，Studio UI 全量 `137/137` 文件、`851/851` 测试通过。 |
| G3_DEVICE_COMMANDS | PARTIAL | Station 测试包/正式包命令已有 `clientRequestId` 幂等、命令查询、过期收敛、权限与运行包身份校验；`StationEndpointsTests` `30/30`，Next 投影覆盖 pending、终态和激活身份不一致。Line sequence、Settings 高风险命令和完整 field reconcile 仍未形成 Next 合同，见 `CV-AUDIT-050`。 |
| G4_NEXT_UI_CONSUMPTION | PARTIAL | Results 页面已挂载唯一 export owner/dialog，按本机筛选范围创建、轮询、取消、对账和下载；切换来源、工程或筛选条件会卸载 owner。浏览器/WebView2 journey 尚未执行。 |
| G5_PARTIAL_EVIDENCE | PARTIAL | 已有 unit/contract 和部分 journey 证据；本轮补齐 Results export owner/page lifecycle 与 Station 边界的本地证据，但未形成完整浏览器、WebView2 或现场证据。 |
| G6_UX_HARDENING | NOT_PERFORMED | 尚未开始本轮集中 UI 收口。 |
| G7_WEBVIEW2 | NOT_PERFORMED | 当前未取得真实 WebView2 100%/125% 证据。 |
| G8_NO_NODE | NOT_PERFORMED | 当前未进行独立 no-Node 发布启动验证。 |
| G9_FIELD_HARDWARE | NOT_PERFORMED | 当前环境没有现场 Camera、PLC、Station 验证条件。 |
| G10_FINAL_CI | NOT_PERFORMED | 远程 workflow 尚未对当前 F10 baseline 运行。 |

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
- 当前 `REMOTE_CI=NOT_PERFORMED`，不修改 workflow trigger，不合并，不改 main。

### G1 Calibration authorization

- `POST /api/calibration/npoint-draft/solve` 复用 `RequireEngineerOrAdmin`，拒绝 Operator 和未认证会话；空 `ProjectId` 返回 `PROJECT_CONTEXT_REQUIRED`，不存在工程返回 `PROJECT_NOT_FOUND`。
- draft solve 仍只产生 draft/candidate/preview artifact，不写正式 Project asset；正式保存继续走既有 `CanEditProject` 和 `ProjectService` 链。
- `CalibrationDraftEndpointsTests` `7/7` 通过；`calibrationOwner` 对 solve 与 formal save 的 `403` 提示分别投影，相关 UI unit 纳入全量 `851/851`。

### G2 Results bulk export

- `ResultsExportJobService` 复用现有 `IResultAnalysisService` 生成服务端 CSV/JSON；作业状态、稳定 snapshot upper bound、取消 token、artifact TTL、SHA-256 和 `clientOperationId` fingerprint 均由 Desktop 进程内唯一 owner 管理，不创建第二 Results 持久化权威。
- `ResultsExportEndpoints` 提供创建、状态、按操作身份对账、取消和下载入口；沿用 Engineer/Admin 权限策略，Station source 在服务端拒绝，过期产物返回明确 `410`。
- Results UI 只在本机结果页创建 `resultsExportOwner`，把当前工程、时间、结果状态、缺陷类型和诊断码作为范围发送；网络未知结果只允许按操作身份对账，不自动重发创建请求，切换 capability 时 dispose 请求、timer 和 controller。
- 已通过 `ResultsExportJobServiceTests` `5/5`、`ResultsExportEndpointsTests` `4/4`、Studio UI 全量 `137/137` 文件和 `851/851` 测试；Browser / Playwright、真实 WebView2 和生产环境证据仍未执行。

### G3 Station package and command evidence

- 当前 backend 已具备 `clientRequestId` 幂等、同请求查询、过期命令收敛、Station admin 权限、正式包身份完整性与部署准入检查。
- `StationEndpointsTests` 在 `Logging__EventLog__LogLevel__Default=None` 且允许既有 AppData fixture 写入的受控进程环境下 `30/30` 通过；默认沙箱首次失败归类为 EventLog/AppData 权限，不改产品代码或目录 ACL。
- Next 只读 Station projection 保留 command-created/in-progress/terminal-failed/awaiting-active-identity/identity-mismatch/succeeded 状态；这不等同于现场 Station、PLC、Camera 或完整 unknown-outcome soak 已验收。

### Contract blockers retained

- Project JSON import/export、AI attachment/resource reference、planar calibration formal asset、line sequence auto-tune 和 Advanced Settings 仍按合同缺口记录，不新增第二套 authority。
- Remote CI、Final Gate、WebView2 125%、独立 no-Node、现场硬件与生产 soak 仍未取得证据。

## 测试与真实环境

| 证据 | 状态 |
| --- | --- |
| `npm run lint` | PASS |
| `npm run typecheck` | PASS |
| `npm run test:unit` | PASS；137 个文件、851 个测试 |
| `npm run build` | PASS；Vite 转换 503 modules |
| Results Application targeted | PASS；`ResultsExportJobServiceTests` `5/5` |
| Results Desktop targeted | PASS；`ResultsExportEndpointsTests` `4/4` |
| Browser / Playwright | NOT_PERFORMED |
| Product/Desktop build | PASS；0 warning、0 error |
| Product/Desktop targeted | PASS；Calibration `7/7`，StationEndpoints `30/30`（受控日志/AppData 环境） |
| Remote CI | NOT_PERFORMED |
| Final Gate | NOT_PERFORMED |
| WebView2 100% | NOT_PERFORMED |
| WebView2 125% | NOT_PERFORMED |
| Independent no-Node | NOT_PERFORMED |
| Camera / PLC / Station | NOT_PERFORMED |
| Production soak | NOT_PERFORMED |

## 提交

本轮变更保持为权限闭环、定向测试和台账更新的 scoped candidate；提交不会自动授予生产验收。
