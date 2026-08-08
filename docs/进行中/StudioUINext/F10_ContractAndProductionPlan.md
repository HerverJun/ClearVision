# F10 Studio UI Next 合同解阻与生产化执行台账

> 本文是 F10 当前执行 source of truth。状态只记录当前代码、测试和环境取得的证据，不把历史报告或未运行的门禁记为通过。

## 基线

```text
F10_STATE=ACTIVE
F10_START_HEAD=38b80b0dfcb66db67a9eab5ff84f80b994104606
F10_START_REMOTE_HEAD=38b80b0dfcb66db67a9eab5ff84f80b994104606
IMPLEMENTATION_HEAD=026768cf41552f8b7da11cfad496820901edfe22
DOCUMENTATION_HEAD=SELF
BRANCH_HEAD_AT_REVIEW=SELF
REMOTE_IMPLEMENTATION_HEAD=026768cf41552f8b7da11cfad496820901edfe22
BRANCH=studio-ui-next
PROJECT_IMPORT_EXPORT=DONE
AI_ATTACHMENT_RESOURCE=BLOCKED_BY_CONTRACT
AI_MODEL_RESOURCE=BLOCKED_BY_CONTRACT
AI_TEMPLATE_ARTIFACT=BLOCKED_BY_CONTRACT
AI_CALIBRATION_PROJECTION=BLOCKED_BY_CONTRACT
NPOINT_AUTHORIZATION=DONE
PLANAR_CALIBRATION=DONE
RESULTS_BULK_EXPORT=DONE
LINE_SEQUENCE=DONE
F10_BROWSER_JOURNEYS=DONE
STATION_TEST_PACKAGE=PARTIAL
DATABASE_ADVANCED=BLOCKED_BY_CONTRACT
PARTIAL_EVIDENCE=PARTIAL
WEBVIEW2_125=NOT_PERFORMED
INDEPENDENT_NO_NODE=NOT_PERFORMED
REMOTE_CI=BLOCKED_BY_ENVIRONMENT
FINAL_GATE=PARTIAL
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

## Gate 状态

| Gate | 状态 | 当前证据 / blocker |
| --- | --- | --- |
| G0_REMOTE_CI | BLOCKED_BY_ENVIRONMENT | `026768cf4` 已推送至 `origin/studio-ui-next`。`ci.yml` 支持 `workflow_dispatch`，但本机 `gh` token 失效、内置浏览器未登录 GitHub、Chrome 会话不可用；未创建 run，未改 trigger。 |
| G1_PROJECT_CONTRACT | DONE | 复用 `ProjectLifecycleCoordinator`、`ProjectSaveCoordinator` 和现有 Project Service；JSON schema/version、CREATE/OVERWRITE、权限、revision、clientOperationId、validation、partial-save 防护和 replay/reconcile 已由现有 endpoint/lifecycle tests 覆盖。 |
| G1_AI_RESOURCE_CONTRACT | BLOCKED_BY_CONTRACT | Camera resource identity/revision/decision 已有 authority；attachment、CV model artifact、TemplateMatching artifact 与 calibration asset-to-scale 投影仍缺正式后端合同，详见本轮 AI 结论。 |
| G1_CALIBRATION_CONTRACT | DONE | N 点 draft/solve 继续要求 Engineer/Admin、非空且存在的 Project 上下文；`ScaleOffset` 复用同一 solver、candidate bundle 和 Project asset save 链。本轮新增 Chromium/fixture solve + formal asset save/reconcile journey，未新增 calibration authority。 |
| G2_RESULTS_EXPORT | DONE | 已补齐服务端 CSV/JSON export job、clientOperationId 幂等与对账、快照上界、取消、TTL、SHA-256 产物校验和权限错误映射；Results 页面仅对本机结果开放，Station 来源明确不支持。Studio UI 全量 `138/138` 文件、`869/869` 测试通过。 |
| G3_DEVICE_COMMANDS | PARTIAL | Station unknown/reconcile 已有 Chromium/fixture journey。Line Sequence 本轮不执行设备写入，Apply 仅修改 canonical flow draft；未引入新 command authority。现场 Station/PLC 仍未验证。 |
| G4_NEXT_UI_CONSUMPTION | DONE | Line Sequence 唯一 owner 挂载于 FlowWorkspace，只使用 shared `ApiTransport` 和 `FlowCanvasOwner.commands.patchNodeParameters`；Project/Results/Station/Template/Calibration 用户路径已有本轮 Chromium/fixture 证据。 |
| G5_PARTIAL_EVIDENCE | PARTIAL | F10 Chromium/fixture 定向 journey `7/7`；仍不等同于真实 WebView2、DPI、no-Node 或现场硬件证据。 |
| G6_UX_HARDENING | NOT_PERFORMED | 尚未开始本轮集中 UI 收口。 |
| G7_WEBVIEW2 | NOT_PERFORMED | 当前未取得真实 WebView2 100%/125% 证据。 |
| G8_NO_NODE | NOT_PERFORMED | 当前未进行独立 no-Node 发布启动验证。 |
| G9_FIELD_HARDWARE | NOT_PERFORMED | 当前环境没有现场 Camera、PLC、Station 验证条件。 |
| G10_FINAL_CI | PARTIAL | 当前 implementation SHA 本地 gates 通过；clean-checkout Remote CI 未运行，不授予 final/production acceptance。 |

状态枚举：`DONE`、`PARTIAL`、`BLOCKED_BY_CONTRACT`、`BLOCKED_BY_ENVIRONMENT`、`NOT_PERFORMED`、`FAILED_RELATED`、`FAILED_UNRELATED`、`DEFERRED`。

## 架构权威

- Project、Flow、GlobalVariables、正式 assets、Runtime Package、Results 和 Station 状态继续由现有后端 Application Service、`ProjectSaveCoordinator`、Runtime/Station 链路负责。
- Studio UI Next 继续复用唯一 API transport、Host adapter、canonical FlowCanvas/ImageCanvas、现有保存链和 capability-local lifecycle owner。
- 本轮不新增第二 API transport、HostBridge、EventBus、Project repository、Calibration asset authority、Station command authority 或前端私有持久化链。
- Production acceptance 不由本台账自动授予；软件门禁与真实 WebView2、DPI、no-Node、现场硬件、生产 soak 和产品 owner 签收分别记录。

## 本轮工作记录（2026-08-08）

### AI Resource Contract

- 可复用：Camera resource 已有 identity、revision、binding decision 与工程关联合同，不需要新建前端资源库。
- `model_resource=BLOCKED_BY_CONTRACT`：CV 算子消费 `ModelPath/ModelId`；`/api/ai/models` 是 LLM provider/model 配置，不是视觉模型 asset authority。
- `template_artifact=BLOCKED_BY_CONTRACT`：`/api/templates` 拥有 flow template，不拥有 TemplateMatching 图像模板产物。
- `calibration_resource=BLOCKED_BY_CONTRACT`：Project assets 拥有正式 calibration bundle，但 AI/UnitConvert 当前消费 numeric `Scale`，尚无权威 asset-to-scale projection。
- `attachment_resource=BLOCKED_BY_CONTRACT`：Legacy 本地路径只是主机路径；AgentRun 会剥离路径，当前没有上传、版本、权限和 resource reference store。
- 因此本轮不把本地路径冒充正式资源，不为 model/template/calibration 创建前端私有绑定。

### Line Sequence authority 与闭环

- Analyze authority：`POST /api/autotune/flow-node/preview`，要求 Engineer/Admin，使用 `ExecutionAdmissionSurface.AutoTunePreview` 拒绝真实外部副作用。
- Recommendation authority：`POST /api/autotune/scenario`，仅接受 `wire-sequence-terminal`，迭代限制为 1..5 轮；允许 flow-owned File 输入，非收敛但已有迭代结果时返回可审查 recommendation。
- Apply authority：Next 只通过 `FlowCanvasOwner.commands.patchNodeParameters` 修改最近上游 `BoxNms`/`DeepLearning` 草稿；白名单仅含 `ScoreThreshold`、`IouThreshold`、`Confidence`，值必须是 `[0,1]` 内有限数。
- Formal save authority：仍为现有 Project save chain；Analyze/Recommendation/Apply 不写设备、不保存 Project、不实现生产算法。如未来需要设备 Apply，必须进入现有 command authority 并补身份、冲突、unknown-outcome/reconcile，不在 Vue 内扩展。
- Lifecycle：owner 在节点选择、flow revision 变化或 dispose 时 abort/丢弃旧响应；保留 `stale` 以防止过期 recommendation Apply。持久化数值算子 identity（`61/140/150`）已纳入 owner 与工作台识别。

### Browser evidence

- Chromium/fixture 定向组 `7/7` 通过：Project canonical JSON import/export、Results server-side full-batch export/download、Station lost-response + duplicate lock + request identity reconcile、Template apply-to-draft、N Point solve + Project asset save/reconcile、Line Sequence Analyze/Recommendation/Apply，以及 draft stale/backend safety rejection。
- 该证据只代表 Playwright Chromium + fixture contract，不代表真实 WebView2、DPI、Desktop endpoint 联调或现场设备。

### Remote CI / Final Gate

- `026768cf4` 已安全推送，push 前 fetch 确认远程未前进且 merge-base 与起始 SHA 一致。
- `ci.yml` 存在 `workflow_dispatch`，但 `gh auth status` 显示 token 失效，内置浏览器 GitHub 未登录，Chrome 会话不可用。本轮未创建 remote run，未修改 trigger，未跳过 required job。
- `FINAL_GATE=PARTIAL`：当前 implementation SHA 本地 gates 通过；clean-checkout CI、WebView2、no-Node 与现场硬件仍缺失。

## 前序工作记录（历史 checkpoint）

### G0 Remote CI

- 已执行 `git fetch origin --prune`，远端 `studio-ui-next` 与本地基线一致。
- 已审计 workflow 触发条件；普通 `studio-ui-next` push 不触发完整 CI。
- 当前 `REMOTE_CI=NOT_PERFORMED`，不修改 workflow trigger，不合并，不改 main。

### G1 Calibration authorization

- `POST /api/calibration/npoint-draft/solve` 复用 `RequireEngineerOrAdmin`，拒绝 Operator 和未认证会话；空 `ProjectId` 返回 `PROJECT_CONTEXT_REQUIRED`，不存在工程返回 `PROJECT_NOT_FOUND`。
- draft solve 仍只产生 draft/candidate/preview artifact，不写正式 Project asset；正式保存继续走既有 `CanEditProject` 和 `ProjectService` 链。
- `CalibrationDraftEndpointsTests` `8/8`、`NPointCalibrationSolverTests` `10/10`、`NPointCalibrationOperatorTests` `12/12`、Planar service 纯计算 `2/2` 通过；`calibrationOwner` 对 solve 与 formal save 的 `403` 提示分别投影，当前 calibration contract/owner UI tests `10/10`。
- `ScaleOffset` 走 `draft -> solve -> candidate -> Project asset save`；没有新增第二 calibration authority 或第二保存协议。直接 `PlanarScaleOffsetCalibrationService.SaveCalibrationAsync` 测试在当前沙箱无法写真实 `%APPDATA%\\ClearVision\\calibration`，状态保留为环境限制。

### G1 Project lifecycle import/export

- formal persisted export 使用 dedicated `ProjectExportDocumentV1` projection；import 明确区分 `CREATE_NEW` 与 `OVERWRITE_EXISTING`，由后端验证 schema/version、权限、operator/parameter compatibility、revision 和 operation identity。
- create/overwrite、invalid document no-mutation、unknown operator/parameter、Operator denial、export permission、replay/reconcile 由现有 Desktop endpoint 与 lifecycle service tests 覆盖；没有新增第二 Project repository、第二 save chain 或前端私有持久化。

### G2 Results bulk export

- `ResultsExportJobService` 复用现有 `IResultAnalysisService` 生成服务端 CSV/JSON；作业状态、稳定 snapshot upper bound、取消 token、artifact TTL、SHA-256 和 `clientOperationId` fingerprint 均由 Desktop 进程内唯一 owner 管理，不创建第二 Results 持久化权威。
- `ResultsExportEndpoints` 提供创建、状态、按操作身份对账、取消和下载入口；沿用 Engineer/Admin 权限策略，Station source 在服务端拒绝，过期产物返回明确 `410`。
- Results UI 只在本机结果页创建 `resultsExportOwner`，把当前工程、时间、结果状态、缺陷类型和诊断码作为范围发送；网络未知结果只允许按操作身份对账，不自动重发创建请求，切换 capability 时 dispose 请求、timer 和 controller。
- 已通过 `ResultsExportJobServiceTests` `5/5`、`ResultsExportEndpointsTests` `4/4`、Studio UI 全量 `137/137` 文件和 `859/859` 测试；本轮另有 Results 分析工程切换与 Global Variables runtime-read 生命周期竞态测试通过。Browser / Playwright、真实 WebView2 和生产环境证据仍未执行。

### G3 Station package and command evidence

- 当前 backend 已具备 `clientRequestId` 幂等、同请求查询、过期命令收敛、Station admin 权限、正式包身份完整性与部署准入检查。
- `StationEndpointsTests` 在 `Logging__EventLog__LogLevel__Default=None` 且允许既有 AppData fixture 写入的受控进程环境下 `30/30` 通过；默认沙箱首次失败归类为 EventLog/AppData 权限，不改产品代码或目录 ACL。
- Next 只读 Station projection 保留 submitting/unknown/reconciling 与 command-created/in-progress/terminal-failed/awaiting-active-identity/identity-mismatch/succeeded 状态；owner dispose 会把未决提交或核对转为 unknown-outcome，核对期间 reset/重复提交被拒绝。这不等同于现场 Station、PLC、Camera 或完整 unknown-outcome soak 已验收。

### Contract blockers retained

- AI attachment、CV model artifact、TemplateMatching artifact、calibration asset-to-scale projection 与 Advanced Settings 仍按合同缺口记录，不新增第二套 authority。
- Line Sequence 软件闭环已完成，但未包含设备写入；Remote CI、WebView2 100%/125%、独立 no-Node、现场硬件与生产 soak 仍未取得证据。

## 测试与真实环境

| 证据 | 状态 |
| --- | --- |
| `npm run lint` | PASS |
| `npm run typecheck` | PASS |
| `npm run test:unit` | PASS；全量 `138` 个文件、`869` 个测试 |
| `npm run build` | PASS；Vite 转换 `509` modules |
| Results Application targeted | PASS；`ResultsExportJobServiceTests` `5/5` |
| Results Desktop targeted | PASS；`ResultsExportEndpointsTests` `4/4` |
| Project lifecycle targeted | PASS；create/overwrite/reconcile/validation/permission tests 已随 `eaafef9f0` checkpoint 通过 |
| Calibration solver/operator targeted | PASS；`10/10` + `12/12` |
| Calibration Desktop endpoint targeted | PASS；`8/8` |
| Planar service solve targeted | PASS；`2/2`；legacy file-save test `BLOCKED_BY_ENVIRONMENT`（真实 AppData 无写权限） |
| Line Sequence owner targeted | PASS；`9/9`，连同 Calibration owner 定向组 `16/16` |
| Browser / Playwright | PASS；F10 Chromium/fixture 定向 journey `7/7` |
| Product/Desktop build | PASS；Desktop targeted test invocation 完成 build，0 error；观察到环境 `NU1900` vulnerability-feed unavailable warning |
| Product/Desktop targeted | PASS；`AutoTuneEndpointsTests` `10/10`；`AutoTuneServiceTests` + `ExecutionAdmissionServiceTests` 合并 `101/101` |
| `git diff --check` | PASS |
| Remote CI | BLOCKED_BY_ENVIRONMENT；workflow 可 dispatch，但当前无有效 GitHub 认证入口，未创建 run |
| Final Gate | PARTIAL；本地 gates 通过，remote clean-checkout 与真实环境未通过 |
| WebView2 100% | NOT_PERFORMED |
| WebView2 125% | NOT_PERFORMED |
| Independent no-Node | NOT_PERFORMED |
| Camera / PLC / Station | NOT_PERFORMED |
| Production soak | NOT_PERFORMED |

## 提交

本轮 implementation checkpoint：`026768cf4` （Line Sequence authority/Next 闭环、AutoTune 权限与 preview admission、核心 Browser journeys），已安全推送至 `origin/studio-ui-next`。前序 checkpoint：`1af7b2ec6`、`8846c52e4`、`d469a4740`。提交和软件测试不会自动授予生产验收。
