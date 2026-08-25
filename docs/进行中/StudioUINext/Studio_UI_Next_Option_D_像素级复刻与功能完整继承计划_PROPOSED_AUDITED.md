# Studio UI Next Option D 像素级复刻与功能完整继承 TODO

> 本文件是实施计划，不是实施结果。它不授权跳过既有合同、后端 authority、生产门禁或 Owner 签字。

```text
DOCUMENT_STATUS=PROPOSED_AUDITED
PLAN_VERSION=1.0
PLAN_DATE=2026-08-22
TARGET_BRANCH=studio-ui-next
VISUAL_BASELINE=INVALID_PENDING_OPTION_D_SCREEN_RESELECTION
VISUAL_OUTPUT_COUNT=24/24
VISUAL_REFERENCE_SIZE=3840x2160
LOGICAL_DESIGN_GRID=1920x1080
IMPLEMENTATION_STATE=G0_PASS_G1_PASS_G2_BLOCKED_BY_CONTRACT_G3_G4_BLOCKED_BY_DEPENDENCY_UI_PROHIBITED
OPTION_D_G0_STATE=PASS
OPTION_D_G0_OWNER_APPROVAL=APPROVED_HERVERJUN_2026_08_23
OPTION_D_G0_DETERMINISTIC_FIXTURE=PASS
OPTION_D_G1_STATE=PASS
OPTION_D_G1_STARTED=TRUE_2026_08_23
OPTION_D_G1_MASTER_MEASUREMENT=PASS_17_GEOMETRY_18_COLOR_3_SHA
OPTION_D_G1_VISUAL_GATE=PASS_4_OF_4_NO_MASKS
PRODUCT_TESTS_THIS_ROUND=PASS_STUDIOUI_145_FILES_963_TESTS_UI_UNIT_1046_PLAYWRIGHT_193_PASS_101_SKIPPED
OPTION_D_G2_STATE=BLOCKED_BY_CONTRACT
OPTION_D_G2_STARTED=TRUE_2026_08_24
OPTION_D_G2_HISTORICAL_MASTER_MEASUREMENT=PASS_12_GEOMETRY_8_COLOR_2_SHA
OPTION_D_G2_HISTORICAL_DETERMINISM_GATE=PASS_6_OF_6_ZERO_DIFF_NO_MASKS
OPTION_D_G2_RAW_MASTER_PIXEL_GATE=NOT_RUN_AFTER_REOPEN
OPTION_D_G2_AFFECTED_REGRESSION=NOT_RUN_AFTER_REOPEN
OPTION_D_G2_INDEPENDENT_REVIEW=NOT_PERFORMED_AFTER_REOPEN
OPTION_D_G3_STATE=BLOCKED_BY_DEPENDENCY
OPTION_D_G3_STARTED=TRUE_2026_08_24
OPTION_D_G3_DETERMINISM_GATE=PASS_REFERENCE_6_OF_6_CANDIDATE_6_OF_6_ZERO_DIFF_NO_MASKS
OPTION_D_G3_MASTER_PIXEL_GATE=FAIL_0_OF_6_CHANGED_RATIO_8_5124_TO_36_3066_PERCENT
OPTION_D_G4_STATE=BLOCKED_BY_DEPENDENCY
OPTION_D_G2_G3_REOPEN_APPROVAL=APPROVED_HERVERJUN_2026_08_24
OPTION_D_G2_G3_REOPEN_SIGNER_ROLES=PRODUCT_SECURITY_QA_RELEASE_CAPABILITY_OWNER
OPTION_D_WHOLE_PAGE_VISUAL_AUTHORITY=INVALID_BY_INTERNAL_SHELL_CONFLICT
OPTION_D_GLOBAL_PRODUCT_NAV_TOP_RULE=FROZEN_TOP_ONLY
OPTION_D_LEFT_SIDE_SURFACE_SCOPE=PAGE_INTERNAL_TOOLS_CATEGORIES_OR_CONTEXT_ONLY
OPTION_D_UI_IMPLEMENTATION=PROHIBITED_PENDING_G2_VISUAL_AUTHORITY_AND_PAGE_SCOPE_REFREEZE
OPTION_D_CAPABILITY_PRESERVATION=APPROVED_RELOCATION_OR_PROGRESSIVE_DISCLOSURE
OPTION_D_G4_START_AUTHORIZATION=NOT_GRANTED
WEBVIEW2_THIS_ROUND=NOT_PERFORMED
WINDOWS_125_PERCENT_THIS_ROUND=NOT_PERFORMED
NO_NODE_THIS_ROUND=NOT_PERFORMED
HARDWARE_THIS_ROUND=NOT_PERFORMED
REMOTE_CI_THIS_ROUND=NOT_RUN
PRODUCTION_ACCEPTANCE=NOT_GRANTED
LEGACY_RETIREMENT=NOT_APPROVED
```

## 当前止血决定（2026-08-25）

- 全局产品导航冻结为始终位于顶部；左侧只能承载页面内部工具、分类或上下文面板，不能替代或重复全局导航。
- 24 张 raw whole-page Option D 图片内部混用顶部全局导航与左侧全局 product rail，因此整套图片不再是当前唯一像素权威。图片文件与历史哈希保留，只作为待筛选资产和历史证据。
- D05-D08 的算子发现、Inspector、Preview/ROI，D13-D14 的 AI 工作区上下文，以及 D16-D21 的 Settings 分组导航属于页面内部结构；筛图时不得随左侧全局 product rail 一并删除。
- 当前代码中的 `workspace` / `product-rail` Shell 与 AI、Settings 的 `shellMode: 'product-rail'` 是实现事实，不是最终合同批准。G2 为 `BLOCKED_BY_CONTRACT`，G3/G4 为 `BLOCKED_BY_DEPENDENCY`。
- 在图片筛选、页面范围和版本化视觉权威重新冻结前，禁止继续 Option D UI 实施。本决定取代本文后续所有与“raw 24 张 whole-page Master 为唯一权威”或“左侧 rail 可作为全局导航”相冲突的实施文字；历史 evidence 数字、哈希和 invocation 结果不改写。

## 1. 目标与完成定义

本计划的目标是：在不改变 ClearVision 业务语义、authority、保存协议、执行状态机和生命周期 Owner 的前提下，先按顶部唯一全局导航规则重新筛选 Option D 资产并冻结页面范围，再让当前 StudioUI Next 对齐获批的版本化视觉基准，同时使现有真实功能、状态、权限、错误、恢复和回退路径全部继续可用。

最终完成必须同时满足：

- 24 张 Option D 页面均有可复现的真实页面/状态，不以静态图片或假数据替代产品能力；
- Option D 只决定视觉、布局和信息分组，当前代码、后端合同和 CURRENT 功能合同继续决定功能与文案；
- 当前 capability 逐项具有明确处置：原位保留、重定位、渐进披露、只读、按 role/profile/flag 隐藏、保留 Legacy fallback、合同阻塞或经批准退役；
- 未出现在 Option D 截图中的现有功能不得静默删除；
- Project、Flow、GlobalVariables 与正式 assets 的保存仍进入既有 Application Service 和 `ProjectSaveCoordinator`；
- 每个 capability 同一时刻只有一个 mounted owner、一个订阅集合和一个写入口；
- 浏览器像素证据、真实 WebView2、Windows 125%、no-Node、远端 CI 和现场硬件证据分别闭合，不能互相替代；
- F10 当前未完成的生产门禁保持真实状态，只有对应证据实际通过后才能更新。

## 2. 约束来源与优先级

发生冲突时按以下顺序裁决：

1. 当前代码、当前后端合同、根 `TODO.md`、`F10_ContractAndProductionPlan.md`；
2. 已接受 ADR，尤其是唯一 Owner、AI 身份/Handoff、Workspace 保存链与生命周期清理；
3. `_visual_master/image_prompts.json` 的 CURRENT 功能审计和 `_visual_master/functional_remapping.json`；
4. 重新筛选并版本化冻结后的 Option D 视觉基准；当前 24 张 raw whole-page PNG 只作待筛选资产；
5. `codex初稿`/Legacy 当前实现，仅作为真实行为和回退基线；
6. 旧计划、旧报告、历史截图和归档 Goal，仅作取证线索。

规则：功能权威高于视觉；当前代码事实高于旧文档；只有重新筛选并版本化冻结的 Option D 资产才可成为视觉基准；任何截图都不能授权新增能力。

## 3. 本计划使用的设计与实现规则

- `clearvision-studio-ui-design`：Quiet Precision、工业高信息密度、简体中文优先、1920×1080 与 Windows 125% 长时操作效率、单一 Owner 和既有后端 authority。
- `impeccable`：以 production-ready、全 24 页、完整交互和高 fidelity 为既定 Shape；不再探索视觉方向，不再生成 probe。
- `vue-best-practices`：Vue 3、Composition API、`<script setup lang="ts">`、最小状态、派生值使用 `computed`、props down/events up、composable/owner 明确 dispose。
- `web-design-guidelines`：实施验收时获取最新版规则；本计划不把历史规则快照宣称为最新审查结论。

## 4. 状态枚举

| 状态 | 含义 |
| --- | --- |
| `TODO` | 已授权进入相应 Gate 后可实施 |
| `IN_PROGRESS` | 已有唯一 Owner，正在实施 |
| `DONE` | 当前代码与绑定证据已经完成；视觉实施只做保真与回归，不重复实现 |
| `PASS` | 有可复核证据且验收条件全部满足 |
| `PARTIAL` | 只有部分证据，不能当作通过 |
| `BLOCKED_BY_CONTRACT` | 产品/安全/后端 Owner 合同未签字，禁止实现 |
| `BLOCKED_BY_DEPENDENCY` | 前置 Gate 未通过 |
| `BLOCKED_BY_ENVIRONMENT` | 软件工作可完成，但真实环境/权限/外部资源不具备 |
| `RETAIN_LEGACY_FALLBACK` | Next 不改变现状，Legacy 入口继续保留 |
| `NOT_RUN` | 可运行但本轮未执行 |
| `NOT_PERFORMED` | 需要真实外部环境/人员但未执行 |
| `NOT_APPLICABLE` | 经审计明确不适用 |

禁止用 `DONE`、`基本完成`、`视觉通过` 覆盖 `PARTIAL`、`NOT_RUN` 或 `BLOCKED_*`。

## 5. Option D raw 视觉资产清单（当前等待重新冻结）

### 5.1 基准文件

- 24 页原图：`_visual_master/option_D/screens/`
- 24 页对照表：`_visual_master/option_D/comparison/contact_sheet.png`
- 视觉宪章：`_visual_master/option_D/visual_constitution.md`
- 功能合同：`_visual_master/image_prompts.json`
- 功能重映射：`_visual_master/functional_remapping.json`
- Master：`05_flow_editor.png`、`13_ai_workspace.png`、`16_system_settings.png`
- Flow 节点恢复审计：`_visual_master/audit/d_canonical_flowcanvas_node_restore_2026-08-22.json`

所有 PNG 均为 `3840×2160`，对应 `1920×1080` 逻辑网格的 2×交付。原文件不得重新生成、重采样、裁切或覆盖；筛选后必须新建版本化基准、页面范围和审计，不得原地漂移。当前 24 张 whole-page PNG 因内部 Shell 冲突不构成整体视觉权威。

当前对话已确认 `Option D 视觉效果 OK，可以进入实施规划`，因此本计划记录 `USER_VISUAL_DIRECTION_ACCEPTED=YES`。
`HerverJun` 已于 2026-08-23 具名批准 Option D G0，并声明有权代表 Product、Security、QA/Release 及相关 capability owner。
`image_prompts.json` 中三个 Master 的历史 metadata 仍是 `selected-for-chain-not-product-owner-approved`；链上选择、G0 方向/处置批准和最终生产签收仍分开记录，故保持 `PRODUCT_OWNER_PRODUCTION_SIGNOFF=NOT_PERFORMED`。

### 5.2 24 页 SHA-256 冻结清单

| ID | 文件 | SHA-256 |
| --- | --- | --- |
| D01 | `01_login.png` | `bf3adebb2451161ca76902d531f9953c38bd6c6f1484145f4b4818935b50a241` |
| D02 | `02_overview.png` | `a6a902196b5486817f80c094d469fa4d96e8c934fb2a36c5e7947fc3d5f24769` |
| D03 | `03_projects_data.png` | `fe6d5e6c368573de83d0a6a0ed46148a2f0e6f01d9e002565c63c6d4047c5e94` |
| D04 | `04_projects_empty.png` | `a0117dcd2b62a5cef6c499f4e0f658a4c3de255ae3da783640f6d6952f030087` |
| D05 | `05_flow_editor.png` | `247efff95e87fdd626f36dfae2dced6d94465d0c697408dca62e69d6ccacedc3` |
| D06 | `06_flow_validation_error.png` | `779422ebda60af052314108ccad147de36b621eecf4924dc9046e40e6c3c0d16` |
| D07 | `07_flow_preview_roi.png` | `51e856548b1b4cc67d2737f287ca9a0de8056a6e8c5fc77a3a5f785028dd5957` |
| D08 | `08_run_ng_modal.png` | `8793b0983eda3caa25a652f28c900ab1d04a190fc9a2c39ef80bce9a199efa8b` |
| D09 | `09_results_investigation.png` | `8d716e0ab1fdffaef82075975c34219565fc8b79b973a7b44c80150252f0201a` |
| D10 | `10_stations_list.png` | `939080aed9eaa4102702e5da3ccbe2e9f1b9f18cd8ca6184cfc6c6c228b374eb` |
| D11 | `11_station_detail.png` | `2ebb9f191210fa8ac76bb37270e630ea6f715e1ac6501b3817196b9fe097829a` |
| D12 | `12_inspection.png` | `280c7fbaf8561bd4e8bd662fbdc1eb4852180161bcb1537311a22a809eb6cc60` |
| D13 | `13_ai_workspace.png` | `0e2875749de6fc6d1971517a530f6a9daae4f935456f035482dc85bf6cf91b1d` |
| D14 | `14_ai_failure_recovery.png` | `cf4540d7e4a25c8d928462dee9186e5b4b6d569db783d0e41a20ccf1ecaef6d3` |
| D15 | `15_operator_catalog.png` | `a01ee6cfbcd1344c2340ce18ced5eb2cfce66450dd0509f5e87ccedead3a2d1f` |
| D16 | `16_system_settings.png` | `525b960075f34db309f2a4871afef54fb100474f2d6479f2447262a2ad98a35e` |
| D17 | `17_camera_settings.png` | `0768b51c32225de4804d1e2d67e65b13a0cc4c53b13a6329afc61daa245dbdf0` |
| D18 | `18_plc_settings.png` | `8f1bac13706adb645d5d03191917e1cbc47cc110ce3a6ab7c9e5600914d746c9` |
| D19 | `19_tcp_settings.png` | `d08f03523572cf2f976d5f90081903cfd46f264ea0263c213bdf095288b1df80` |
| D20 | `20_station_communication.png` | `e1a660e9e64ff17184cda8ff7341fcfaa2660934164bbf93f73bf67ad0e65cd1` |
| D21 | `21_ai_model_settings.png` | `a3fbdefa534c897daa40630308e8d2cc5672decdb4ca16fe3af1b648c744b993` |
| D22 | `22_diagnostics.png` | `0415729663e19fb6b2527956eec74d9f64284cefa8b1ba7be7f8c4d5c9e6ee97` |
| D23 | `23_about.png` | `4b085e25511b6fcffc72d6d6af33c39574baa9bfb7106729485b4035a77c8e84` |
| D24 | `24_forbidden.png` | `e6171bedda03d2c06ae5bb6c66241a8993b08f360657ff35c8245ad7eeb208ca` |

### 5.3 视觉不变量

- 全局产品导航始终位于顶部；左侧只允许页面内部工具、分类或上下文面板，不能替代或重复全局导航。所有与此冲突的 raw Master Shell 均须在筛图时排除或更换。
- D05-D08 共用 Flow 页面内部的算子发现、project context、命令层、Canvas 原点、Inspector/Preview 边界和底部状态区；raw 图片中的左侧全局 product rail 不具权威，D08 只能是同一 Workspace 上的 modal overlay。
- D05-D07 的算子节点必须保持现有 canonical FlowCanvas 样式。禁止为贴合生成图而重做节点几何、端口、连线、状态或选中语义。
- D13-D14 共用顶部全局导航下的 AI 工作区 header、三分区和上下文区域；失败恢复只是状态变化，raw 图片中的左侧全局 product rail 不具权威。
- D16-D21 共用顶部全局导航下的 Settings 页面内分组 rail、header、字段节奏和保存边界；页面内分组 rail 可保留，raw 图片中的左侧全局 product rail 不具权威。
- D19-D20 是已批准的 dark-theme fixture；只切换全局 token，不允许 route-specific dark CSS 或几何分叉。
- D03-D04 共用命令栏、搜索/排序、表格框架、列、padding 和创建动作；空态只替换行区。
- D01、D24 是最小壳例外；D23 必须留在 authenticated Product Shell。
- Cinnabar 只用于身份与导航选中；action blue 只用于命令、focus 和选中 Canvas 对象；OK、NG、warning、execution error、offline、unknown、disabled 不得混色。
- 逻辑字号 12-14px，控件 28-34px，行高 32-40px，4/8px 节奏，圆角 3-8px，边界 1px；letter spacing 固定为 0。

#### Canonical FlowCanvas 节点证据绑定

以下坐标来自节点恢复审计，坐标均为 4K reference 上的半开区间 `[left, top, right, bottom)`：

| 页面 | post-restore 全图 hash | canonical 节点修正框 | 审计条件 |
| --- | --- | --- | --- |
| D05 | `247efff95e87fdd626f36dfae2dced6d94465d0c697408dca62e69d6ccacedc3` | `[960,720,2441,971)` | `changed_pixels_outside_allowed_box=0` |
| D06 | `779422ebda60af052314108ccad147de36b621eecf4924dc9046e40e6c3c0d16` | `[960,720,2441,971)` | `changed_pixels_outside_allowed_box=0` |
| D07 | `51e856548b1b4cc67d2737f287ca9a0de8056a6e8c5fc77a3a5f785028dd5957` | `[1048,639,1560,872)` | `changed_pixels_outside_allowed_box=0` |

这些框证明 Option D 资产当时只恢复了节点区域，不是允许产品实现只比较该区域。实施时：全页布局仍对照 Option D；节点/端口/连线/选中态单独对照当前 canonical runtime fixture。不得把 post-restore 生成图反向变成第二 Canvas 样式权威。

### 5.4 当前工程成熟度快照

本计划建立在 F10 已完成的软件/架构收口之上，不重启旧 F01 阶段，也不重复实现已经存在的 Router、Platform、Design System、Canvas adapter、Workspace、AI Handoff 或保存链。

| F10 范围 | 当前状态 | 本计划如何处理 |
| --- | --- | --- |
| F10 G1-G5 软件/架构收口 | `DONE` | 作为地基做视觉校准和功能回归，不复制或替换 owner/authority |
| F10 G6 UX hardening 子门禁 | `DONE` | 保留当前 UI hardening；不得外推为真实环境通过 |
| 根 TODO G6 真实环境总门禁 | `BLOCKED_BY_ENVIRONMENT` | Windows 125%、独立目标机、Remote CI、现场硬件和生产签收仍未闭合 |
| G7 真实 WebView2 | `PARTIAL` | G9 补齐 Debug/Release、theme/density、owner cleanup 和代表性 24 页证据 |
| G8 no-Node | `PARTIAL` | G9 在独立无 Node 环境完成启动、资产、导航与功能证据 |
| G9 现场硬件 | `NOT_PERFORMED` | G10 由 Camera/PLC/TCP/Station/Inspection/AI 对应 Owner 执行 |
| G10 Final CI | `PARTIAL` | G10 运行 Remote CI、soak、签收；普通 branch push 不等于完整 CI |
| Parity Alignment Wave 0 | `DONE` | 2026-08-23 具名批准已冻结本轮处置；run-to-node/active-node 与 Inspector recommendation 为 `DEFERRED`，subgraph 为 `NOT_APPLICABLE`，Station 保持现状 |
| Production acceptance | `NOT_GRANTED` | 只能由 F10 在全证据闭合后决定 |
| Legacy retirement | `NOT_APPROVED` | 在批准前保留回退，不以 Option D 视觉通过替代退役决定 |

`F01_五轮执行卡.md` 中较早的阶段状态只作历史记录；出现冲突时以当前代码、根 TODO 和 F10 为准。

## 6. 明确非目标

- 不生成 Option E 或新的 Option D 图片；不再向外部主机上传参考板。
- 不复制 Roboflow 导航、品牌、block、Builder Assist、Deploy/Publish/Test 或其他业务能力。
- 不重做 Runtime、Station、Inspection、AgentRun、Project save、运行包、正式结果或设备 authority。
- 不新增第二 API transport、HostBridge、EventBus、ServiceRegistry、Canvas/ImageCanvas 内核、保存 client、SSE 基础设施或前端持久化 authority。
- 不从已废弃的 `FrontendV2/` 复制代码、目录、状态树或视觉实现。
- 不因像素工作修改后端 DTO/endpoint；如发现合同缺口，先停止并提交合同审计。
- 不在本计划阶段修改根 `TODO.md`、F10、代码、配置、测试或图片。

## 7. Authority 与唯一 Owner 地图

| 范围 | 当前唯一入口/Owner | 实施约束 |
| --- | --- | --- |
| Composition root | `StudioUI/src/app/createStudioApp.ts` | `mountStudioApp` 创建 Router/Pinia/platform/auth；统一 unmount/dispose |
| Product runtime | `app/productRuntimeFactory.ts` | 只组装既有 query、project lifecycle、workspace、leave guard；不建第二 runtime |
| Router/Shell | `app/router.ts`、`app/layouts/ProductLayout.vue` | route/view 只作 composition surface；权限和 flag 在 mount 前裁决 |
| HTTP | `platform/api/apiTransport.ts` | 唯一 authenticated HTTP transport；capability 不直接建 fetch client |
| Host | `platform/host/webView2HostAdapter.ts` | 唯一 WebView2 adapter；listener 必须 detach |
| Auth/session | `app/auth/**`、`app/session/**` | 继续使用现有 lifecycle owner 和 route guard |
| Project read/lifecycle | `capabilities/projects-read/**`、`project-lifecycle/**` | 查询与命令边界不因页面布局合并 |
| Workspace | `project-workspace/workspaceRuntime.ts`、`workspaceOwner.ts` | Workspace 是 Flow draft、Canvas 和 AI candidate 的唯一接收 owner |
| FlowCanvas | `project-workspace/flow/flowCanvasOwner.ts` -> `platform/canvas/canonicalFlowCanvas.ts` | 只保留 canonical adapter；Vue 不长期持有内核对象 |
| ImageCanvas | `project-workspace/image/imageCanvasOwner.ts` -> `platform/canvas/canonicalImageCanvas.ts` | Preview/ROI 通过窄接口使用；dispose 后零 listener |
| Inspector | `project-workspace/inspector/inspectorOwner.ts` | 参数草稿/校验投影，不拥有 Project 保存 |
| Preview/ROI | `preview/previewOwner.ts`、`previewWorkbenchOwner.ts`、`roi/roiInteractionOwner.ts` | stale/cancel/revision/selection/dispose 必须清理；Preview 不等于 Formal Run |
| Persistence | `persistence/workspacePersistenceOwner.ts`、`projectPersistencePort.ts` | 正式保存沿既有 Project POST/PUT -> Application Service -> `ProjectSaveCoordinator` |
| Results | `results-read/**` | read/evidence/analysis/export owner 保持分离，不生成第二结果 authority |
| Stations | `stations-read/**` | read、SSE lifecycle、admin command owner 保持既有身份与 reconcile |
| Inspection | `inspection-run/**` | HTTP/SSE 和 run owner 不迁入组件，不伪造运行状态 |
| AI | `ai-workbench/aiSessionOwner.ts`、既有 adapter/ledger | `/ai` 与 `/projects/:id/ai` 共用一个 lazy capability；Handoff 不持有 Canvas |
| Settings | `settings/settingsOwner.ts`、`settingsWriteCoordinator.ts` | 视觉分区不能生成第二保存协调器或 secret authority |
| Design System | `design-system/**` | 只负责呈现/交互语义；不读 API、不持有业务状态、不决定权限 |

`v-show` 仅允许无资源、无副作用的纯展示切换。任何持有 Canvas、ImageCanvas、SSE、timer、request、AbortController 或 Host listener 的视图必须真实 unmount/dispose；CSS 隐藏不算卸载。

## 8. 功能完整继承协议

### 8.1 每项能力必须建立的台账字段

实施前从当前代码、CURRENT 截图和测试生成 capability ledger，每一行至少包含：

```text
capability_id
entry_route_or_trigger
current_owner
backend_contract_or_host_port
read_or_write_authority
roles_profiles_flags
authorization_policy_and_401_403_404_semantics
operation_identity_and_expected_revision
response_loss_lookup_reconcile
happy_path
loading_empty_error_offline_stale_partial_conflict_unknown_states
cancel_reconcile_response_loss_behavior
cleanup_obligation
option_d_location
disposition
confirmed_regions_controls_tabs_navigation
forbidden_additions
renamed_reinterpreted_or_implied_capability_check
omission_check
test_and_evidence_ids
owner_signoff
```

允许的 `disposition` 只有：

- `RETAIN_SAME_LOCATION`
- `RETAIN_RELOCATED`
- `RETAIN_PROGRESSIVE_DISCLOSURE`
- `RETAIN_READ_ONLY`
- `RETAIN_ROLE_PROFILE_FLAG_GATED`
- `RETAIN_LEGACY_FALLBACK`
- `BLOCKED_BY_CONTRACT`
- `RETIRE_WITH_EXPLICIT_APPROVAL`

任何能力不得留空，不得用 `NOT_IN_OPTION_D` 当作删除理由。只有 Product/安全/对应 capability Owner 明确批准后才能使用 `RETIRE_WITH_EXPLICIT_APPROVAL`。每个 Gate 同时统计 `UNAUTHORIZED_ADDITION`、`RENAMED_CAPABILITY`、`REINTERPRETED_CAPABILITY`、`IMPLIED_CAPABILITY`、`SILENTLY_REMOVED`；任一计数非零即失败。

### 8.2 已完成合同的保真清单

以下能力已由 F10 标记为 `DONE`，本计划只允许复刻呈现和补回归，不得重新设计合同：

| 能力 | 必须保持的合同 |
| --- | --- |
| Project import/export | formal export 使用 `ProjectExportDocumentV1`；import 区分 `CREATE_NEW`/`OVERWRITE_EXISTING`；后端校验 schema/version、权限、operator/parameter compatibility、revision、`clientOperationId`，支持 replay/reconcile |
| N Point / Planar calibration | Engineer/Admin；必须有存在的 Project context；`draft -> solve -> candidate -> Project asset save`；不新增 calibration authority |
| GlobalVariables | 只映射当前 Flow draft 的四种标量兼容端口/参数；Apply 前重验变量/算子/端口/参数 identity 与类型；正式保存仍走 Project save chain |
| Line Sequence | 输入优先使用同一 Preview owner 的当前输入图，其次当前输出图；stale/未完成不发送旧图；返回 input/output/`FinalPreview`；Apply 只 patch canonical Flow draft，不写设备、不保存 Project |
| Results bulk export | 服务端 CSV/JSON job；稳定 snapshot upper bound、取消、artifact TTL、SHA-256、`clientOperationId` fingerprint 和 operation lookup；只支持本机 Results，Station source 服务端明确拒绝 |
| AI Handoff | owner-bound、短期 artifact；Plan/Build/baseline/fingerprint 复核；`available -> consuming -> consumed`，可 reject/expire；两阶段 reserve/acknowledge、crash recovery；consume receipt 固定 `projectSaved=false` |
| Station package/command | package/command identity、target Station、`clientRequestId` 幂等、准入、过期、查询、取消/核对和终态 reconcile；现场与完整 soak 仍未通过 |

视觉工作包必须为这些合同建立现有测试映射；不得把 `DONE` 合同重新拆成新的前端状态机。

### 8.3 Option D 未画出的现有正式路由

下列路由仍须保留并继承同一 Design System；24 页视觉通过不代表它们可以删除：

| 路由/状态 | 当前组件 | 计划处置 |
| --- | --- | --- |
| `/setup` | `SetupPage` | 保留首次管理员初始化与安全边界，使用最小 AuthShell 派生样式 |
| `/change-password` | `ChangePasswordPage` | 保留会话内改密流程，不并入 Settings 普通表单 |
| `/not-found` / catch-all | `NotFoundPage` | 保留 404，与 403、网络错误分开 |
| `/projects/:id` | `ProjectDetailPage` | 保留详情、进入 Workspace/AI/Inspection 的真实入口 |
| `/operators/:operatorType` | `OperatorDetailPage` | 保留 read-only 详情，不添加编辑/安装 |
| `/inspection` | `InspectionProjectsPage` | 保留连续检测工程选择入口 |
| `/labs/design`、`/labs/canvas` | Internal Labs | 仅内部 fixture/evidence，不进入正式导航和产品 authority |

### 8.4 G0 已签字处置

| 能力 | 已批准处置 | 禁止越界 |
| --- | --- | --- |
| 运行到节点 / active node | `DEFERRED` | 保持 canonical FlowCanvas 全流程执行语义；不新增入口、快捷键或状态模型 |
| subgraph | `NOT_APPLICABLE` | 不新增 host/child flow/breadcrumb/嵌套/保存语义；deterministic fixture 不包含 subgraph |
| Inspector 参数推荐 | `DEFERRED` | 保持当前参数编辑/校验；不调 recommendation endpoint，不创建 candidate/accept/revert UI |
| Station 高风险命令确认 | `APPROVED_RETAIN_CURRENT` | 保持现有 owner、后端准入与 reconcile；不新增命令、确认 modal 或入口 |
| Demo/示例工程 | `RETAIN_LEGACY_FALLBACK` | 不复制 demo Flow JSON，不新增 Option D Demo UI |
| 独立本地图像加载 | `RETAIN_LEGACY_FALLBACK` | 不绕过 FilePicker/ImageCanvas owner |
| Runtime Preview Pilot | `RETAIN_LEGACY_FALLBACK` | 继续 default-off/internal-only，不把 pilot 伪装为正式功能 |
| Station token | `RETAIN_CURRENT_REGENERATE_ONLY` | 不显示明文，不实现 preserve/replace |
| Storage cleanup | `RETIRE_WITH_APPROVAL` | 本轮不提供破坏性入口；不由此删除后端 authority |
| 工程/版本/FPS 持续状态 | `RETAIN_LEGACY_FALLBACK` | 等待 DPI budget，不挤压 Canvas/Inspector/Preview |

## 9. 24 页实施与验收矩阵

`功能合同` 列只列主干；`image_prompts.json -> functional_audit` 中的 regions、controls、tabs、navigation 和 forbidden additions 全部为绑定清单，实施时必须逐项转入 capability ledger。

| ID | 路由/状态 | Option D 视觉锚点 | 必须继承的功能主干 | 页面验收重点 |
| --- | --- | --- | --- | --- |
| D01 Login | `/login` | 最小 AuthShell、紧凑对齐表单 | 用户名、密码、记住账号、显隐密码、登录、验证/session 恢复 | 无产品导航、无营销/注册/SSO/MFA；键盘、自动填充、错误焦点正确 |
| D02 Overview | `/overview` | 连续工作面，最近工程主列表 + 窄运行环境带 + 功能启动器 | 刷新、查看全部工程、详情、继续配置、现有功能入口 | 禁止 KPI/图表/card mosaic；loading/stale/partial/offline 均可恢复 |
| D03 Projects Data | `/projects` populated | 文件浏览器式命令栏 + 密集表格 | 刷新、导入、新建、搜索、排序、详情、打开、导出、删除、分页；保留 `ProjectExportDocumentV1`、`CREATE_NEW/OVERWRITE_EXISTING`、revision/operation identity/replay | 与 D04 几何完全一致；删除/导入/响应未知按现有 command owner 协调，不重写 Project 合同 |
| D04 Projects Empty | `/projects` empty | D03 表格框架内空态 | 刷新、导入、新建、搜索、排序、创建 | 不加 sample/onboarding/illustration；空态不改变 Shell 或列框架 |
| D05 Flow Editor | `/projects/:id/workspace` default | Canvas 65-75%，窄 rail、按需 picker、右 Inspector | 工程/详情/判定/保存/结果/检查/正式运行/详情/变量/运行包/模板；算子发现；完整 canonical Canvas 命令；Inspector 全真实区 | 节点样式完全冻结；禁止 Fit/Auto Layout/第二 Canvas/第二保存；owner cleanup 归零 |
| D06 Flow Validation | 同 Workspace，selected invalid node | D05 几何，仅 Inspector 扩展错误字段 | D05 全入口、无效字段、校验消息、保存/运行守卫 | 错误附着字段，说明原因与下一步；不发明 auto-fix/校验规则 |
| D07 Preview + ROI | 同 Workspace，Preview/ROI state | 左 Canvas + 可调整大图工作区，ROI 控件贴图 | 手动/取消预览、折叠；ROI X/Y/W/H、编辑/撤销/重做/放弃/应用；图像缩放/适应/实际像素/大图/探针；结果摘要/结构化输出；保留 Line Sequence 当前输入/输出图、`FinalPreview` 与 draft-only Apply | Preview/ROI/Inspector 各自 owner；stale/abort/dispose 不显示旧图；不自动保存、不写设备 |
| D08 Run NG Modal | 同 Workspace，formal run NG overlay | D05 不变背景上的宽受限 modal | 正式运行、重新检查、关闭、六项真实指标、admission、最近结果、技术信息、诊断、查看本次结果 | Preview 与 Formal Run 严格区分；focus trap/restore；无新 tab/重跑变体/导出 |
| D09 Results Investigation | `/results` investigation | 时间线/主证据/诊断三分区 | 返回 Workspace、导出、刷新、筛选、分页、详情、基线/当前/失败前成功比较、evidence export；保留 server-side export job、snapshot/cancel/TTL/SHA/operation lookup | `态势总览/调查详情` 均保留；partial/stale/证据缺失/导出失败可辨识；Station source 明确不支持 bulk export |
| D10 Stations List | `/stations` | 异常筛选 + 站点密集表，概览为同能力入口 | 刷新、搜索、连接/运行状态筛选、详情；全站概览/异常调查 | read/SSE recovery 清晰；不在列表发明 CRUD、设置或详情 rail |
| D11 Station Detail | `/stations/:stationId` | 身份上下文条 + 概览/最近结果/生产追踪/健康证据带 | 返回、刷新、查看结果、明细数量、追溯、realtime recovery；保留 test package 的 package/target/identity/expiry/query/cancel/terminal reconcile | admin command/test-package owner 不得删除；未签风险合同前保持 role/profile/fallback，不塞入此视觉状态或绕过准入 |
| D12 Inspection | `/projects/:id/inspection` | 紧凑运行命令条 + 六指标证据带 + 设备/6-of-7 检查/最近结果 | 启动、停止、核对状态、查看结果、相机摘要、技术信息、诊断 | 真实 HTTP/SSE identity/recovery；不新增 mode/trigger/上传/设备设置 |
| D13 AI Workspace | `/ai`、`/projects/:id/ai` normal | AI Master 三分区，不伪造 Canvas/chat | candidate readiness、计划/构建、diff、校验/演练/Handoff、历史、诊断、技术身份；owner-scoped Session/Run/operation 与 Engineer/Admin 权限 | 单一 `AiSessionOwner`；`ownerHash + operationKind + clientOperationId`；Handoff 复核 Plan/Build/baseline/fingerprint，先 dispose AI，再由 Workspace staged 接收；不自动保存/运行 |
| D14 AI Recovery | 同 AI route，blocked/failed | D13 同几何，failure/replay 放上下文 rail | 已有恢复、历史、诊断、删除结果核对（存在时）；operation lookup、replay、artifact 两阶段 consume/ack/reject 与 crash recovery | 不盲重试、不猜终态、不新增 recovery 模式；401/403 与非 owner 404 不泄漏；receipt/TTL/status/redaction 权威不变 |
| D15 Operator Catalog | `/operators` | 搜索优先的 read-only 工程索引 | 刷新、搜索、分类、生命周期、范围、端口、参数、清筛选、详情、分页 | 不加安装/编辑/删除/运行/市场/KPI；detail route 可达 |
| D16 System Settings | `/settings` General | Settings Master rail + 单一宽配置页 + save footer | 刷新、软件标题、产品主题、readonly 自动启动、放弃、保存 | 唯一 `settingsWriteCoordinator`；无重复 tabs/第二保存/密码字段 |
| D17 Camera Settings | `/settings` Camera | 发现/绑定列 + 采集/触发主区 + Preview/诊断 rail | 厂商筛选、绑定、采集参数、触发输入、设备识别、串口测试、单帧/连续预览、资源诊断 | 不声明真实硬件在线；Preview 生命周期清理；不发明 vendor/firmware/wizard |
| D18 PLC Settings | `/settings` PLC | 连接/心跳/测试条 + 主地址表 + 两个保存边界 | S7/MC/FINS、连接字段、测试、保存协议、映射 CRUD、保存映射 | 协议设置与映射保存不合并；现场 PLC 证据单独执行 |
| D19 TCP Settings | `/settings` TCP, dark fixture | 三栏 profile/editor/traffic，Settings 几何不变 | Client/Server profile CRUD、连接/断开或启停、文本/HEX、等待响应、发送、六列表、清空 | dark 仅为全局 theme；不加 transport/analyzer/telemetry/列；runtime 状态不伪造 |
| D20 Station Communication | `/settings` Station, dark fixture | mode 主列 + effective/restart/diagnostic rail + 单一保存 | 刷新、Disabled/LocalLoopback/LanController、port/host/local sync、当前已核 token regenerate、放弃/保存；preserve/replace 只有合同签字后才可启用 | secret 不明文；Option D 不授权新增 token 操作；dark 不分叉布局；不加 remote control/live claim/topology |
| D21 AI Model Settings | `/settings` AI Model | 模型目录源列 + provider 配置 + contextual rail | 刷新/新建/选择/激活/planner/shadow/删除、endpoint、密钥操作、测试、推理支持、放弃/保存 | secret redaction；不加 cost/token analytics/marketplace/prompt preset/provider 能力 |
| D22 Diagnostics | `/diagnostics` | read-only 技术控制台 | 刷新、复制、服务/session/host/version/environment/technical projection | 只读；不加执行/重启/secret/telemetry；复制反馈可访问 |
| D23 About | `/about` | Product Shell 内的紧凑身份/组成页 | 当前真实产品、版本、license、support、组成说明 | 不发明版本/license/update/support service；无 marketing hero |
| D24 Forbidden | `/forbidden` | 最小权限边界面板 | 原因、影响、返回工程库 | 403 与 401/404/network 分开；无 retry/request access/role editor/login control |

## 10. Design System 像素复刻 TODO

现有 `design-system/tokens/tokens.css`、primitives、patterns 和 `ProductLayout` 是实施地基，不得另起第二套 Design System。

### ODD-DS-01｜测量与 token 差异表

- [ ] 以 1920×1080 逻辑坐标测量 Product Shell、Flow、AI、Settings 三个 Master 的所有稳定锚点。
- [ ] 记录 topbar/rail/toolbar/status/pane 宽高、page padding、grid、gap、border、radius、shadow、font、line-height、icon 与 hit target。
- [ ] 对照现有 `--cv-*` 与 `--flow-canvas-*`，标记 `KEEP`、`CALIBRATE`、`ADD_ALIAS`；禁止 capability-local magic number 先行。
- [ ] 为 light/dark、compact/comfortable 建立同构 token 表；D19/D20 不得使用 route selector 改主题。
- [ ] 证明所有字体均为 Windows/系统字体，不下载在线字体。

验收：重复锚点只由一个 token/primitive 决定；无同义 token 重复；Canvas node token 保持 canonical 值或有明确“不改”证据。

### ODD-DS-02｜共享 primitive/pattern 校准

- [ ] 校准 `CvButton`、`CvIconButton`、`CvField`、`CvSelect`、`CvSearchField`、`CvDataTable`、`CvPagination`。
- [ ] 校准 `CvMenu`、`CvTooltip`、`CvModal`、`CvToastRegion`、`CvSplitter`、`CvViewTabs`、`CvStatusBadge`。
- [ ] 校准 `CvPageHeader`、`CvToolbar`、`CvPageState`、`CvBreadcrumbs` 和 workbench patterns。
- [ ] 统一 hover/active/focus-visible/disabled/loading/error/readonly/selected/destructive 状态。
- [ ] icon-only command 使用既有 `CvIcon`/图标库并有可读 label/tooltip；不得手画重复 SVG。
- [ ] cards 只用于真实独立对象、modal 或工具；页面 section 不套 card，禁止 card-in-card。

验收：Design Lab 覆盖所有状态、light/dark、compact/comfortable、键盘和 reduced motion；组件切换状态不造成布局位移。

### ODD-DS-03｜布局 primitive

- [ ] 固化 Product Shell、命令条、密集表格、split workbench、context rail、settings rail、overlay 的共享几何。
- [ ] 固定可调整 pane 的 min/max、初始尺寸、持久化边界和 1366×768 collapse 策略。
- [ ] 保证 1366×768 无全局横向滚动；表格/Canvas/console 只在各自 owner 内滚动。
- [ ] 所有固定格式控件定义稳定尺寸，动态 label/status 不得推动相邻结构。

## 11. 串行实施 DAG

```text
G0 合同/功能/基准冻结
  -> G1 Design System 校准与 Labs 证据
    -> G2 Product Shell + Auth/Page State
      -> G3 Read-only/List 页面族
        -> G4 Workspace D05-D08（单一 owner，禁止并拆）
          -> G5 Results + Stations + Inspection
            -> G6 AI + Settings
              -> G7 未入 24 页路由 + 全局 parity/a11y/i18n
                -> G8 Browser 像素/功能证据
                  -> G9 WebView2/DPI/no-Node/回滚/性能
                    -> G10 Remote CI/现场硬件/Owner 签收/生产决定
```

任一 Gate 未通过，不自动进入下一 Gate。视觉页面可以按无共享文件的叶子工作包并行，但共享 tokens、Router、Product Shell、Workspace cluster、Project save/GlobalVariables 和 Host/API 只能由主协调 Owner 串行修改。

## 12. Gate 与工作包

### G0｜合同、基准与功能盘点

唯一 Owner：主协调 Owner。状态：`PASS`（实现、独立复核与最终 manifest 已冻结）。

- [x] `ODD-G0-01` 复核 24 个 hash、尺寸、Master 链和 visual constitution；写 evidence manifest，不改 `_visual_master`。
- [x] `ODD-G0-01A` 记录三个 Master 的 `selected-for-chain` metadata、本次用户视觉确认和 Product/UX Owner 身份；链上选择、视觉方向确认、生产签收三种状态分别记录。当前 requester 是本次视觉方向决策 authority；未提供个人姓名且未完成 production signoff，故不得补写姓名或把方向确认升级为生产签收。
- [x] `ODD-G0-02` 生成完整 capability ledger，逐项导入 24 页 functional audit 的所有 regions/controls/tabs/navigation/prohibitions。
- [x] `ODD-G0-03` 对照当前 Router、Legacy/CURRENT、Next capability、测试和 endpoint 建立 zero-omission/zero-addition 映射。
- [x] `ODD-G0-04` 对 setup/change-password/project detail/operator detail/inspection selector/not-found/Labs 建立非 24 页继承清单。
- [x] `ODD-G0-05` 冻结单一 Owner/resource ledger：request、SSE、timer、subscription、AbortController、Canvas、ImageCanvas、Host listener。
- [x] `ODD-G0-06` 冻结 `option-d-g0-deterministic.v1`：单一 Project seed 同时包含普通节点、Preview、ROI、双向 global binding、formal decision 与 Formal Run/Results evidence；subgraph 按批准处置为 `NOT_APPLICABLE`。
- [x] `ODD-G0-07` `HerverJun` 于 2026-08-23 代表 Product / Security / QA-Release / 相关 capability owner 完成具名批准；处置不被测试外推为功能实施。

G0 evidence：[OptionD_G0_CapabilityLedger](./OptionD_G0_CapabilityLedger.md)；[OptionD_G0_EvidenceManifest](./OptionD_G0_EvidenceManifest.md)。
当前 StudioUI 串行 Vitest 为 144 files / 946 tests PASS；受影响 Playwright 串行回归为 6/6 PASS，包含 1920×1080 / 1366×768 golden journey 与 20-cycle cleanup。
G0 没有 UI 候选实现，因此 candidate/diff/overlay 为 `NOT_APPLICABLE`，reference/hash/Master/canonical-restore 轨为 PASS，未使用 mask。
三路独立 fixture/contract、documentation、visual/hash 复核及补充 disposition 复核均为 PASS，P0-P3 finding 为 0；最终 manifest、hash、whitespace/status 已冻结。以上是 G0 关闭时的历史结论；当前 G1 为 `PASS`，G2 为 `BLOCKED_BY_CONTRACT`，G3/G4 为 `BLOCKED_BY_DEPENDENCY`，UI 实施已暂停。

退出条件：24/24 页面、所有当前 route/capability 和所有写入口均有 ledger 行；不存在 `UNKNOWN_OWNER`、`UNKNOWN_AUTHORITY`、`UNMAPPED_FUNCTION`、`UNAUTHORIZED_ADDITION`、`RENAMED_CAPABILITY`、`REINTERPRETED_CAPABILITY` 或 `IMPLIED_CAPABILITY`。

### G1｜Design System 与视觉标尺

唯一 Owner：Design System 主协调 Owner。状态：`PASS`（实现、本地门禁、owner cleanup、双轨视觉证据和三路独立终审已完成）。

- [x] 完成 ODD-DS-01/02/03。
- [x] 在 `/labs/design` 建立 deterministic visual fixture，不读取业务 API；未声明 `/api/**` fail closed。
- [x] 在 `/labs/canvas` 只验证 canonical adapter 和冻结节点，不建立第二 Canvas。
- [x] 建立 1920×1080@2x light/compact、dark/compact 的 reference/candidate/diff/overlay；4/4 PASS、`NO_MASKS`。
- [x] 在 Chromium 中实测 Segoe UI/Microsoft YaHei UI fallback 和数字等宽；真实 WebView2 字体证据为 `NOT_PERFORMED`，留在 G9，未以 Chromium 替代。
- [x] 获取最新版 Web Interface Guidelines 并记录版本/日期；来源 commit `e3d624baaf29dc1fc645aff3e38f03e564d2d6b1`，G1 范围审计无未处置阻断。

本 Gate 的 Master measurement、Design/Canvas Labs、owner cleanup、lint/typecheck/unit/build/bundle 与专用视觉门禁均已通过；
证据和仍未执行的 WebView2、Windows 125%、独立 no-Node、Remote CI、现场硬件类别见
`OptionD_G1_EvidenceManifest.md`。独立 code/Owner、visual/gate、documentation/status 终审已闭合且无未处置 P0-P3 finding；G2 仅转为 `READY`，本 Gate 未实施 G2 页面。

退出条件：共享组件状态矩阵通过；Master 锚点测量完成；后续页面不需要私建按钮、表格、modal、splitter、status 或 page-state 组件。

### G2｜Product Shell、Auth 与全局状态

唯一 Owner：Shell 主协调 Owner。状态：`BLOCKED_BY_CONTRACT`。2026-08-24 的重开批准与历史证据继续保留，但 raw whole-page Master 集合因内部 Shell 冲突失去当前视觉权威。全局产品导航顶部唯一规则已经冻结；须先筛图并重新冻结页面范围和版本化视觉基准，完成前不得继续 G2 UI。

- [x] 记录 HerverJun 代表 Product / Security / QA-Release / 相关 capability owner 的重开批准。
- [ ] 按顶部唯一全局导航规则筛选 raw 图片，区分全局 Shell 与页面内部工具、分类、上下文面板。
- [ ] 新建版本化视觉基准并冻结页面范围、图片清单、Shell 边界、阈值、Owner 与签字；不得原地覆盖历史 raw 资产。
- [ ] 冻结并验证 capability relocation / progressive-disclosure map；全部 route、role/profile/flag admission、键盘入口与单一 mounted owner 保持可达。
- [ ] 新视觉权威冻结后再建立全页比较、定向/受影响回归、owner cleanup 和独立复核 invocation；不使用 mask、私有组合 reference 或降低阈值。

- [x] 校准 `ProductLayout` 的 lockup、稳定 topbar、导航顺序、cinnabar 选中、service/appearance/more/account cluster。
- [x] 校准 workspace product rail 与普通页面 top navigation 的边界，禁止 route 自建 Shell。
- [x] 实现 D01、D24；同步派生 setup/change-password/not-found，但保留各自语义。
- [x] 统一 loading/empty/error/offline/stale/partial/conflict/unknown/unauthorized/forbidden/not-found。
- [x] route 切换恢复主内容焦点，保留 skip link、唯一 `main`、heading 层级和 reduced motion。
- [x] 验证 flag/role/profile 拒绝发生在 capability mount 前，零 owner DOM/订阅。

历史关闭证据见 [OptionD_G2_EvidenceManifest](./OptionD_G2_EvidenceManifest.md)。D01/D24 三个 CSS viewport 的 implementation-reference/candidate 零差异、Master anchors 与旧回归统计仍可用于漂移诊断，但不再构成当前 G2 PASS。新视觉权威冻结后的全页 gate、定向/受影响回归、owner cleanup、独立复核均须生成新 invocation 与 manifest；真实 WebView2、Windows 125%、独立 no-Node、Remote CI 与现场硬件继续如实保持 `NOT_PERFORMED` / `NOT_RUN`。

退出条件：Shell 在所有标准页面像素锚点一致；D19/D20 切暗色不改几何；权限/错误状态不混淆。

### G3｜Read-only 与列表页面族

可在文件无重叠时并行；每个 capability 仍只有一个 Owner。状态：`BLOCKED_BY_DEPENDENCY`。G3 依赖 G2 的合同、页面范围和版本化视觉权威重新冻结；不得恢复 G3 实施，G4 仍禁止进入。

- [x] `ODD-P02` Overview：D02 与所有 query 状态。
- [x] `ODD-P03-04` Projects：同一组件/框架覆盖 populated/empty/loading/error/stale/partial；保留完整命令 owner。
- [x] `ODD-P15` Operators：catalog + detail route，保持 read-only。
- [x] `ODD-P22` Diagnostics：只读技术投影与复制/刷新。
- [x] `ODD-P23` About：真实版本/license/support/组成投影。
- [x] 每页补齐键盘、焦点、aria、长中文、空数据、极端行数、慢请求和取消测试。
- [ ] raw Master-to-candidate 像素门禁：D02/D03/D04/D15/D22/D23 changed-pixel ratio 分别为 `8.5124% / 16.4402% / 16.7529% / 20.0981% / 14.3678% / 36.3066%`，全部超过冻结的 `1%` 上限。

现有 reference/candidate 6/6 零差异只证明 deterministic capture，不是有效视觉基准比较。2026-08-24 重开批准及旧 diff/overlay/hash、功能回归继续作为历史证据；2026-08-25 的止血决定已使 raw whole-page Master 集合失效，须等待新视觉权威冻结。全部真实能力仍必须通过批准的重定位或渐进披露继续可达。历史证据见 [OptionD_G3_EvidenceManifest](./OptionD_G3_EvidenceManifest.md)。

退出条件：D02-D04、D15、D22-D23 pixel/functional gate 通过；未画出的 detail route 功能不回归。

### G4｜Workspace D05-D08

唯一 Owner：Workspace/FlowCanvas/Inspector/Preview 集成 Owner。禁止并拆。状态：`BLOCKED_BY_DEPENDENCY`。

- [ ] `ODD-WS-00` 逐个完整阅读即将修改的 Workspace、FlowCanvas、Inspector、Preview、ROI、Run、Persistence owner 代码并确认 resource ledger。
- [ ] `ODD-WS-01` 复刻 D05 Master 几何，但 canonical node/port/connection/selection 代码与 token 保持不变。
- [ ] `ODD-WS-02` Operator picker 改为按需浮动/停靠，保留搜索、分类、兼容、最近、收藏、单击/拖动添加。
- [ ] `ODD-WS-03` Inspector 改为 contextual resizable dock，保留 identity、lifecycle、ports、resource、common/advanced/special workbench 和 validation。
- [ ] `ODD-WS-04` 实现 D06 error state；错误贴字段，保存/运行守卫沿当前 authority。
- [ ] `ODD-WS-05` 实现 D07 Preview/ImageCanvas/ROI split；新请求、revision/selection/project/permission/dispose 取消旧结果。
- [ ] `ODD-WS-06` 实现 D08 Formal Run detail modal；与 Preview result 明确分离，保留 admission、metrics、diagnostics 和 result link。
- [ ] `ODD-WS-07` 保留 GlobalVariables、final decision、line sequence、camera binding、calibration、runtime package、templates、AI Handoff 等未完全展示的 Workspace 入口。
- [ ] `ODD-WS-08` 证明 AI Handoff 只 staged draft，用户显式保存仍走原 POST/PUT -> Application Service -> `ProjectSaveCoordinator`。
- [ ] `ODD-WS-09` 对每次 mount/unmount、project switch、flag off、route leave 断言 Canvas/ImageCanvas/request/timer/subscription 全部归零。
- [ ] `ODD-WS-10` 遵守已签处置：本轮不实现 run-to-node/active-node/subgraph/recommendation；保留现有行为与 fallback。

退出条件：D05-D08 所有功能、错误、取消、响应未知、保存冲突和 cleanup 测试通过；Canvas 性能不低于现有基线；节点像素审计无未授权变化。

### G5｜Results、Stations、Inspection

状态：`BLOCKED_BY_DEPENDENCY`。

- [ ] `ODD-P09` Results 三分区调查工作台；保留 summary/detail、filters、pagination、comparisons、export、evidence 和 traceability。
- [ ] `ODD-P10-11` Stations 列表/详情；保留 read/SSE recovery、production trace、health 和跨结果链接。
- [ ] `ODD-ST-PKG` 保留 Station test package 的 package identity、target Station、`clientRequestId`、准入、过期、查询、取消/核对与终态 reconcile；其 Owner/入口可按 role/profile 渐进披露，但不得静默删除。
- [ ] `ODD-ST-CMD` Station admin commands 保持当前唯一 owner 与 identity/reconcile；风险合同未签前不得迁入新 modal 或静默删除。
- [ ] `ODD-P12` Inspection 命令条/六指标/ready checks/最近结果；保留正式 run owner、SSE、start/stop/reconcile。
- [ ] 覆盖 offline/stale/partial/unknown outcome、权限变化、Station/Inspection owner dispose 和跨页返回。

退出条件：D09-D12 visual/functional gate 通过；Preview、Formal Run、历史 Result、Station 上报四类状态没有混写。

### G6｜AI 与 Settings

状态：`BLOCKED_BY_DEPENDENCY`。

- [ ] `ODD-P13-14` 在不改 AI DTO/receipt/replay/Handoff authority 的前提下复刻 AI Master 和 failure state。
- [ ] 验证 `/ai` 与 `/projects/:id/ai` 仍共用 lazy capability，每条 route 只有一个 `AiSessionOwner`。
- [ ] AI ledger 固定 ownerHash、owner-scoped Session/Run/operation、Engineer/Admin、401/403、非 owner 统一 404、`ownerHash + operationKind + clientOperationId`、payload fingerprint 和 response-loss lookup。
- [ ] Handoff ledger 固定 eligible terminal Build、Plan/Build/project baseline/candidate fingerprint、30 分钟 TTL、available/consuming/consumed/rejected/expired、reserve/acknowledge/reject、crash recovery 与 `projectSaved=false`。
- [ ] `ODD-P16-21` 以 D16 为唯一 Settings Master，六个页面共享 rail/header/field/save geometry。
- [ ] Camera/PLC/TCP/Station/AI Model 的 read/write/runtime owner 不并入通用 form 状态。
- [ ] D19/D20 通过全局 dark-theme fixture 复刻；切回 light 后其他页面不残留 route CSS。
- [ ] secret/token/API key 全程 masked/redacted；保留/替换/再生身份、保存与 unknown outcome 按现有合同。
- [ ] 任何设备连接/模型可用/现场状态只展示真实投影，不根据视觉 fixture 伪造成功。

退出条件：D13-D14、D16-D21 visual/functional gate 通过；AI/Settings 所有 request/timer/subscription dispose；保存入口数量不增加。

### G7｜非 24 页、全局 parity、a11y 与 i18n

状态：`BLOCKED_BY_DEPENDENCY`。

- [ ] 完成 setup、change-password、not-found、project detail、operator detail、inspection project selector 的功能与视觉一致性。
- [ ] 对 capability ledger 中每行执行 disposition 审计；`UNMAPPED`、`SILENTLY_REMOVED`、`DUPLICATE_OWNER`、`UNAUTHORIZED_ADDITION`、`RENAMED_CAPABILITY`、`REINTERPRETED_CAPABILITY`、`IMPLIED_CAPABILITY` 必须为 0。
- [ ] 简体中文优先；术语、字段、状态、按钮在 sibling state 间不得漂移。
- [ ] 验证 200% text zoom、Windows High Contrast/forced colors（适用时）、reduced motion、键盘-only、screen reader 名称。
- [ ] 验证长工程名、长 Station/模型/算子名、最大数字、空值、未知状态不重叠/截断。
- [ ] 对所有 modal/menu/tooltip/toast/splitter 做焦点、Escape、outside click 和 cleanup 测试。

退出条件：全部 route 可达且无功能回归；WCAG 2.2 AA 适用项与最新版 Web Interface Guidelines 有证据化审计。

### G8｜Browser 像素与功能证据

状态：`BLOCKED_BY_DEPENDENCY`。

- [ ] 建立 deterministic fixture：固定 locale、theme、density、数据、时间、权限、feature flags、viewport 和动画。
- [ ] 24 页逐页 capture 1920×1080 CSS viewport、device scale factor 2，输出 3840×2160 PNG。
- [ ] D19/D20 使用 dark/compact，其余按合同使用 light/compact；额外验证全局主题切换。
- [ ] 同时 capture 1536×864、1366×768；这些用于自适应/溢出验收，不替代 1920×1080 像素基准。
- [ ] 逐页生成 reference/candidate/diff/overlay、DOM geometry、computed token、console/network/owner cleanup manifest。
- [ ] D05-D07 先验证 Option D 全图 hash 与上述修正框/零越界审计，再将候选节点 crop 对照 canonical runtime fixture；两类证据分开记录。
- [ ] 运行 unit/component/Playwright/legacy regression；不以 screenshot PASS 覆盖行为 FAIL。
- [ ] 所有 mask 写入版本化清单并说明原因；不得 mask shell 边界、容器、控件、状态、错误、重叠或缺失功能。

退出条件：24/24 visual gate + functional gate + cleanup gate 同时 PASS，且 Legacy fallback regression PASS。

### G9｜真实宿主、DPI、发布与性能

状态：`BLOCKED_BY_DEPENDENCY`。

- [ ] 真实 Debug WebView2：light/dark、compact/comfortable、导航、权限、所有 owner mount/unmount。
- [ ] 真实 Release/publish WebView2：同样矩阵，独立 user-data/port/result/publish 目录。
- [ ] Windows 100%/125%/150% 分别记录 native DPI、devicePixelRatio、CSS viewport、client/physical size；125% 必须真实执行。
- [ ] no-Node 目标机/环境验证启动、资产、导航、功能和 24 页代表性视觉，不把本机 publish 当作目标机证据。
- [ ] Canvas pan/zoom/drag/connect、ROI、ImageCanvas、长表格、AI/Station SSE 做性能与长时 soak。
- [ ] flag off/rollback 证明旧 owner unmount/dispose；Legacy 回退仍能完成核心工作流。

退出条件：F10 对应 G7/G8 证据从 `PARTIAL` 转为真实 PASS 所需条件全部满足；未运行项继续写 `NOT_RUN`。

### G10｜CI、现场与生产决定

状态：`BLOCKED_BY_DEPENDENCY`。

- [ ] Remote CI 执行 StudioUI quality、contracts、browser、legacy、.NET regression 和 architecture guards。
- [ ] 真实 Camera/PLC/Station/AI provider/Inspection 现场矩阵由对应 Owner 执行并签字。
- [ ] 执行生产 soak、故障注入、response-loss/reconcile、restart/recovery、资源清理和审计。
- [ ] Product/UX、Frontend、Backend capability、Security、QA/Release、现场 Owner 完成签收。
- [ ] 只有全部证据通过后，才由 F10 Owner 决定 `PRODUCTION_ACCEPTANCE` 和 `LEGACY_RETIREMENT`；本计划不能自行批准。

## 13. 文件 Owner 与白名单

### 13.1 共享文件，仅主协调 Owner

- `StudioUI/package.json`、lockfile、Vite、TypeScript、ESLint、Vitest 配置
- `StudioUI/src/main.ts`、`app/createStudioApp.ts`、`app/router.ts`、`app/navigation.ts`
- `app/layouts/**`、`app/base.css`
- `design-system/tokens/**`、根 public exports、Design System README
- `platform/api/**`、`platform/host/**`、`platform/canvas/**`
- Desktop `.csproj`、Feature Flags、CI、contracts、共享 ADR/F10/根 TODO

### 13.2 capability-local 白名单

| 工作包 | 默认文件范围 |
| --- | --- |
| Overview | `capabilities/overview/**` |
| Projects | `capabilities/projects-read/**`、`project-lifecycle/**` |
| Workspace D05-D08 | `capabilities/project-workspace/**`，由一个集成 Owner 独占 |
| Results | `capabilities/results-read/**` |
| Stations | `capabilities/stations-read/**` |
| Inspection | `capabilities/inspection-run/**` |
| AI | `capabilities/ai-workbench/**` |
| Operators | `capabilities/operators-read/**` |
| Settings | `capabilities/settings/**` |
| About | `capabilities/about/**` |
| Diagnostics | `platform/diagnostics/**` |
| Auth pages | 当前 auth capability/page 范围；不得跨入 session/platform owner |

共享 primitive 缺口只能向 Design System Owner 提交需求，capability 不私建同名组件。任何后端改动默认不在视觉工作包白名单内；只有合同审计确认缺口并由主协调 Owner 单独授权后才能建立新的后端工作包。

## 14. 像素级验收方法

### 14.1 归一化环境

```text
locale=zh-CN
timezone=Asia/Shanghai（fixture 时间仍需冻结）
font=Segoe UI Variable Text / Microsoft YaHei UI / system fallback
viewport=1920x1080 CSS px
deviceScaleFactor=2
output=3840x2160 PNG
density=compact
motion=disabled for capture
theme=light；D19/D20 reference fixture 为 dark
data/role/profile/flags=versioned deterministic fixture
```

该浏览器环境只用于像素对比，不是 Windows 125% 的替代证据。

### 14.2 双轨比较

Option D raw 图片是生成式视觉候选；只有重新筛选并版本化冻结的子集才可作为视觉参考，真实中文、业务数据和状态仍由当前产品决定。新视觉权威冻结后必须同时运行：

1. 视觉轨：比较 Shell、布局、边界、surface、spacing、type scale、颜色、图标、状态组件和浮层几何；
2. 功能轨：比较 DOM/accessible name、真实 controls、route、owner、request、authority、error/recovery/cleanup，不复制图片中的不可靠业务值。

允许 mask：动态时间、随机 id、真实图像像素、业务事实文本的 glyph interior。mask 后仍必须比较其容器、baseline、行数、wrap、padding、背景和相邻几何。禁止 mask 缺失控件、错位、重叠、错误状态、Shell、modal、panel 或整个页面区。

### 14.3 历史建议阈值（须随新视觉权威重新批准）

以下阈值不能从失效的 24 张 whole-page Master 自动继承；新视觉合同必须逐项复核并重新签字，未批准前不得启动像素 Gate。

- 全页未 mask 区域 `SSIM >= 0.990`，changed-pixel ratio `<= 1.0%`；
- Master/受保护锚点 crop `SSIM >= 0.995`，changed-pixel ratio `<= 0.35%`；
- Shell/family anchor 边界偏差 `<= 1 CSS px`；独立动态 pane 可接受 `<= 2 CSS px`，须有原因；
- 重复控件宽高、row height、baseline、gap 偏差 `<= 1 CSS px`；
- token 颜色 `Delta E 2000` median `<= 1.5`、P95 `<= 3.0`；
- 文本 wrap 行数必须一致；0 个 incoherent overlap、0 个全局 overflow、0 个不可达 action；
- D05-D07 canonical node protected crop 不允许未经批准的样式变化。

阈值不能按页面临时放宽。若字体栅格差异导致噪声，先修复环境或缩小到 glyph mask，不得扩大结构 mask。

## 15. 验证矩阵与现有入口

### 15.1 StudioUI 静态质量

工作目录：`ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/`

- [ ] `npm ci`
- [ ] `npm run lint`
- [ ] `npm run typecheck`
- [ ] `npm run test:unit`
- [ ] `npm run build:production`
- [ ] `npm run bundle:gate`
- [ ] `npm run bundle:verify`

### 15.2 Browser/UI

工作目录：`ClearVision.Product/tests/ClearVision.Product.UI.Tests/`

- [ ] `npm ci`
- [ ] `npm run test:unit`
- [ ] `npm run test:agent-ui-contract`
- [ ] `npm run test:preview-smoke`
- [ ] `npm test`，使用现有 `CV_UI_SCENARIO=studio-ui-next` 选择器和当前 Playwright 配置
- [ ] Legacy static Chromium regression
- [ ] 新增 24 页 visual contract、geometry、functional parity、owner cleanup、theme/density/viewport 测试

静态 Chromium 不能宣称真实 WebView2、真实 DPI、真实 endpoint 或现场硬件通过。

### 15.3 .NET 与架构回归

同一 `.csproj` 严格串行，优先使用：

- [ ] `./scripts/run-tests-services-regression.ps1`
- [ ] `./scripts/run-tests-phase42-regression.ps1`
- [ ] `./scripts/run-tests-plc-regression.ps1`
- [ ] `./scripts/run-tests-desktop-endpoints.ps1`
- [ ] 定向项使用 `./scripts/run-dotnet-test-serial.ps1`，同项目多个类合并为一次调用
- [ ] architecture guards：第二 transport/Host/Canvas/save/EventBus/ServiceRegistry、FrontendV2 回流、Owner dispose、publish asset

### 15.4 WebView2/DPI/no-Node/最终证据

复用现有脚本，不创建平行 runner：

- [ ] `scripts/studio-ui-next/Invoke-StudioUiWebView2Evidence.ps1`
- [ ] `scripts/studio-ui-next/Invoke-StudioUiWebView2Matrix.ps1`
- [ ] `scripts/studio-ui-next/Test-StudioUiDpiEvidence.ps1`
- [ ] `scripts/studio-ui-next/Test-StudioUiNoNodeEvidence.ps1`
- [ ] `scripts/studio-ui-next/Invoke-StudioUiCanvasPerformanceEvidence.ps1`
- [ ] `scripts/studio-ui-next/Invoke-StudioUiRollbackEvidence.ps1`
- [ ] `scripts/studio-ui-next/Invoke-StudioUiFinalEvidence.ps1`

### 15.5 现场矩阵

| 环境 | 必须验证 | 当前计划状态 |
| --- | --- | --- |
| Camera | 发现/绑定/单帧/连续预览/trigger/resource cleanup | `NOT_PERFORMED` |
| PLC | S7/MC/FINS 合同、测试、映射保存、错误/断线 | `NOT_PERFORMED` |
| TCP | Client/Server、connect/start、send/receive/log、unknown outcome | `NOT_PERFORMED` |
| Station | read/SSE/recovery/trace；批准后的风险命令矩阵 | `NOT_PERFORMED` |
| Inspection | readiness/start/stop/SSE/result/diagnostics/recovery | `NOT_PERFORMED` |
| AI provider | model test、Plan/Build/replay/Handoff/redaction | `NOT_PERFORMED` |

## 16. 证据目录与真实性

每个 Gate 的 evidence 写入：

```text
.tmp/studio-ui-next/option-d/<gate>/<sourceSha>/<runId>/
```

至少包含：

```text
sourceSha
workingTreeDiffIdentity
optionDManifestHash
fixtureIdentity
routeAndState
role/profile/featureFlags
theme/density/locale
viewport/deviceScaleFactor
WindowsScale/nativeDpi/clientAndPhysicalSize（真实宿主时）
reference/candidate/diff/overlay
maskManifest
pixelAndGeometryMetrics
functionalLedgerRows
network/operationIdentity
ownerResourceBeforeAfter
consoleErrors
commandsAndExitCodes
artifactHashes
cleanupResult
reviewerAndSignoff
```

临时 publish 只能写 `./.tmp/publish-check/`。截图、日志、测试结果不得散落仓库根目录。未运行的验证必须写 `NOT_RUN` 或 `NOT_PERFORMED`，不能引用过去 PASS 冒充本次证据。

## 17. 停止条件

出现任一情况立即停止当前工作包：

- Option D 与当前功能/合同冲突且无法通过重定位或渐进披露同时满足；
- 需要新增第二 authority、transport、HostBridge、EventBus、ServiceRegistry、Canvas/ImageCanvas 或保存链；
- 需要让 route/component 直接持有长期命令式对象才能完成布局；
- 无法证明 mounted owner、订阅、timer、request、controller 和写入口唯一；
- AI Handoff 需要自动保存/运行/部署或 AI owner 持有 Canvas；
- dirty Workspace、PersistenceRevision、flow hash、artifact fingerprint 或 response-loss 需要猜测成功；
- Station/Storage/secret 等高风险行为缺少合同、安全身份或 reconcile；
- 为通过视觉 diff 需要删除真实功能、复制不可靠业务数据或大面积 mask；
- D05-D07 canonical node style 出现非授权变化；
- 真实 Windows 125% 发生 overlap、不可达 action、全局滚动或 Canvas 被持续状态挤压；
- 远端分支历史不兼容、当前 worktree 变更使唯一 Owner 无法隔离。

停止后只提交证据与 blocker，不自行扩展后端或降低验收阈值。

## 18. 回滚与发布策略

- 保留当前 Feature Flag/profile/route guard；视觉实现按 capability 开关，不用 CSS 隐藏旧 owner。
- flag 切换必须真实 unmount/dispose 旧 owner，再 mount 新 owner；不得双写或双订阅。
- 每个 Gate 保持可回滚提交边界；共享 tokens/Shell 与 capability 迁移分开提交。
- 回滚后验证 legacy/当前 Next owner 恢复且新 request/SSE/timer/listener 为 0。
- `LEGACY_RETIREMENT` 只有在 24 页、非 24 页功能、WebView2/DPI/no-Node/CI/现场/soak 和 Owner 签字全部闭合后由 F10 决定。

## 19. 最终 Definition of Done

只有以下所有 checkbox 均可由证据证明时，本计划才可标记完成：

- [ ] Option D 24 个 hash 与 Master/invariant 未漂移。
- [ ] 24/24 页面均在真实 StudioUI Next route/state 中实现，不是静态图片。
- [ ] 24/24 visual gate、functional gate、cleanup gate 全部 PASS。
- [ ] 当前全部 route/capability ledger 无 `UNMAPPED`、`SILENTLY_REMOVED`、`DUPLICATE_OWNER`、`UNAUTHORIZED_ADDITION`、`RENAMED_CAPABILITY`、`REINTERPRETED_CAPABILITY`、`IMPLIED_CAPABILITY`。
- [ ] 未画出能力均有经签字的 disposition；无未经批准退役。
- [ ] Project/Flow/GlobalVariables/assets 保存链与 `ProjectSaveCoordinator` authority 有 trace 证据。
- [ ] AI Handoff、Inspection、Results、Station、Runtime Package authority 未被前端替代。
- [ ] canonical FlowCanvas/ImageCanvas 内核唯一；D05-D07 节点样式未改变。
- [ ] 1920×1080 像素阈值通过；1536×864、1366×768 无全局 overflow/overlap。
- [ ] light/dark、compact/comfortable、reduced motion、键盘、焦点、长中文和错误状态通过。
- [ ] StudioUI lint/typecheck/unit/build/bundle、Playwright、legacy regression、.NET/architecture guards 通过。
- [ ] Debug/Release WebView2 和 Windows 100%/125%/150% 证据通过。
- [ ] no-Node 目标环境、rollback、性能、soak 通过。
- [ ] Remote CI 通过。
- [ ] Camera/PLC/TCP/Station/Inspection/AI 现场矩阵完成或由 F10 明确判定不适用。
- [ ] Product/UX、Frontend、Backend、Security、QA/Release、现场 Owner 签收。
- [ ] F10 明确更新生产接受与 Legacy 退役决定；本计划不自行宣称。

## 20. 本轮交付边界

G0 交付包含权威基线/route/capability/Owner/authority/fallback 台账、ADR 具名批准、单一 deterministic fixture、
定向与受影响回归、owner cleanup 证据及 F10/TODO/计划同步。G1 交付仅包含共享 Design System、内部 Design/Canvas Labs、
定向测试与双轨视觉证据；没有修改 Legacy、后端合同、权限、保存链、CI、Option D 资产或 canonical FlowCanvas 节点语义，
也没有开始 G2 页面实施。真实 WebView2、Windows 125%、独立 no-Node、现场硬件为 `NOT_PERFORMED`，Remote CI 为 `NOT_RUN`；
这些类别不从 Chromium、本地 build 或历史证据外推。
