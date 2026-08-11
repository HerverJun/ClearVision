# ClearVision Studio UI Next 视觉精修自动推进 TODO

```text
DOCUMENT_ROLE=CODEX_AUTOMATED_VISUAL_POLISH_PLAN
DOCUMENT_STATE=V6_LOCAL_BROWSER_AND_EXTERNAL_GATES_PENDING
PLAN_DATE=2026-08-09
PLAN_BASELINE_HEAD=9c2ba21d0060ad8d70eb7e93f1228791e96ae6b8
CANDIDATE_IMPLEMENTATION_HEAD=bf662c838c4b066362169e06486f04a38be95899
CURRENT_EVIDENCE_HEAD=aba69626995ae65d38829b99ac9387eb7bc62111
PREVIOUS_VISUAL_IMPLEMENTATION_HEAD=a3e59bd552d0e7dd73be9041487843daed87caea
APPLE_REFINEMENT_BASE_HEAD=f132d999744fa6ff14a862030f0a25f888156061
BRANCH=studio-ui-next
CURRENT_STATUS_SOURCE=docs/进行中/StudioUINext/F10_ContractAndProductionPlan.md
PRODUCTION_EXECUTION_SOURCE=TODO.md
PRIMARY_UI_ROOT=ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI
LEGACY_SEMANTIC_BASELINE=ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot
QUALITY_BAR=FLAGSHIP_INDUSTRIAL_DESKTOP_TOOL
DESIGN_DIRECTION=APPLE_INSPIRED_TECH_ELEGANCE_WITH_INDUSTRIAL_QUIET_PRECISION
PRIMARY_VISUAL_REVIEW=IN_APP_BROWSER
FORMAL_BROWSER_EVIDENCE=REPOSITORY_PLAYWRIGHT_HARNESS
FORMAL_HOST_EVIDENCE=WINFORMS_WEBVIEW2
AUTO_CONTINUE=YES_AFTER_INTERNAL_GATE
AUTO_COMMIT=NO
AUTO_PUSH=NO
CURRENT_COMMIT_AUTHORIZATION=GRANTED_AND_CONSUMED
CURRENT_VISUAL_STAGE=V6_LOCAL_BROWSER_AND_EXTERNAL_GATES_PENDING
NEXT_ACTION=V6.4_COMPLETE_S00_S13_B0_B4_OR_V6.7_REAL_125_OR_V6.8_OWNER_REVIEW
THIS_PLAN_BROWSER_EVIDENCE=L0_INTERACTIVE_RECOVERED_AUTH_PROJECTS_PASS_L1_S00_S13_NOT_PERFORMED_L2_PLAYWRIGHT_PASS
THIS_PLAN_WEBVIEW2_100=PASS_DEBUG_RELEASE_1920_1536_1366_NATIVE_96_DPI
THIS_PLAN_WEBVIEW2_125=NOT_PERFORMED
PRODUCT_OWNER_VISUAL_SIGNOFF=NOT_GRANTED
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

> 本文件是视觉精修的唯一执行队列，不是新的业务、合同或生产状态权威。F10 已完成的 G3 表示既有工程门禁和方向性视觉证据成立，不等于本轮“旗舰级精修”已签收。任何当前状态、代码或合同冲突均以根 `AGENTS.md`、当前代码和 F10 为准；本文件只记录本轮实际重新取得的证据。

## 1. 目标

把 Studio UI Next 从“功能完整、工程可用”推进到“可信、精密、长时间使用不疲劳”的工业桌面产品。视觉提升必须同时改善理解速度、操作确定性和信息扫描效率，不能靠删功能、藏状态或制造装饰来换取整洁。

完成后应满足：

- 工程师在 1920x1080 Windows 工作站上能快速识别当前工程、保存状态、执行状态、判定结果和下一步动作。
- 1366x768 / 1350x704 的短屏压力场景仍能访问 Canvas、Inspector、Preview、保存和正式运行，不出现内容遮挡或多重滚动争抢。
- light 为默认工作主题，dark 具有同等层级和对比度；compact 为默认密度，comfortable 只增加适度呼吸。
- 页面不再依赖“每块内容一个完整描边卡片”表达结构；层级主要来自布局、表面、排版、对齐和少量真实 elevation。
- 主操作、次操作、危险操作、品牌强调、技术信息、OK、NG、执行错误、警告和离线均有稳定且互不混淆的视觉语义。
- 每个受影响页面都经过“实现 -> 内置浏览器截图 -> 视觉复审 -> 回改 -> 再截图”的闭环，而不是只通过 lint 或无溢出检查。
- 旧版真实能力均被等价保留、优化保留、重定位、按 profile 隐藏或明确延期；不得因美化静默丢失入口、参数、状态或错误路径。

## 2. 非目标与硬边界

- 不改变 Project、Flow、GlobalVariables、正式 assets、AgentRun、Runtime Package、Inspection、Results 或 Station 的后端权威。
- 不新增第二 API transport、EventBus、ServiceRegistry、HostBridge、Canvas 内核、Project save endpoint、保存 client 或前端私有持久化链。
- 不把 Pinia、Vue state、DOM、localStorage 或截图 fixture 变成业务权威。
- 不重写正式运行、保存、reconcile、SSE、Runtime、Station 或现场设备协议；视觉层只消费现有合同和投影。
- 不复制已废弃 `FrontendV2/`，不把 legacy 视觉直接搬到 Next；legacy 只用于核对功能和操作语义。
- 不新增在线字体、装饰图片、营销式 hero、毛玻璃、渐变发光、科技网格、卡片海洋或编排式页面入场动画。
- 不以 Chromium fixture、DPR、浏览器缩放或静态截图替代真实 WebView2、Windows 125%、独立 no-Node 目标机、现场硬件或产品签收。
- 本计划默认不自动 commit、push、切换分支、删除文件或修改 F10 状态；需要 clean SHA 的最终门禁到达时再取得明确授权。

## 3. 视觉方向冻结

### 3.1 使用场景

主要用户是长时间在 Windows 工业工作站上配置流程、调试图像并判断运行状态的工程师；环境可能明亮、任务高频且错误成本高。因此默认采用清晰、明亮、低噪声的工作表面，而不是暗色“科技感”或展示型视觉。

### 3.2 Quiet Precision

- 中性 graphite 建立 app、page、raised、floating、canvas 的表面层级；大面积背景不染品牌色。
- 朱砂只用于品牌身份、当前选中和少数关键意图；技术信息与链接使用技术蓝。
- OK、NG、执行错误、Warning、Info、Idle、Offline、Unknown、Disabled 使用独立 token，并同时提供中文标签或图标，不只靠颜色。
- 使用现有 Windows/系统字体栈、固定字号、`letter-spacing: 0` 和稳定数字宽度；不使用流体字号或展示字体。
- 圆角保持 3-8px 的现有克制尺度；普通连续工作区优先单向分隔线和 tonal surface，不使用卡片套卡片。
- 阴影只表达真实浮层；普通 Panel 不同时使用完整描边和宽软阴影。
- 动效只服务于状态变化、反馈、展开和焦点连续性，通常 140-200ms；支持 `prefers-reduced-motion` 和根 projection。
- 常用操作保持可见，低频高级操作渐进披露；“更简洁”不能增加点击、来回切页或上下文丢失。

### 3.3 明确要消除的廉价感

- 页面被同权重的完整描边矩形切碎，标题、数据和操作都像线框稿。
- 顶栏、工具条和卡片中堆叠过多文字命令，主次操作没有足够区分。
- 不同页面对相同对象使用不同字号、按钮、状态、空状态和间距。
- 紧凑控件只追求小，26px 命中区、长中文截断和 9-11px 正文影响可读性。
- 登录、初始化、错误页向普通用户暴露 `/api/auth/me`、profile、authority 等研发文案。
- “无 overflow、截图非空、detector=[]”被误写成“视觉已经精美”。这些只能证明特定缺陷未出现。

## 4. Codex 自动推进协议

Codex 每次只推进一个可验收小批次。一个小批次包含一个明确视觉假设、一个主要 surface、受控文件白名单、对应测试和一组前后截图。共享 tokens 或 Shell 修改必须独立成批，不能夹带多个页面的零散修补。

### 4.1 每批固定循环

1. **重新取证**：读取当前 `git status`、HEAD、F10、相关 route、owner、合同、旧版能力和现有 Design System；历史截图只作线索。
2. **写任务句**：用一句话记录“谁在此页面完成什么任务、最高频动作是什么、当前视觉阻力是什么”。
3. **冻结白名单**：列出允许修改的源文件和测试文件；发现跨 Owner 需求时停止该批，不顺手跨界。
4. **采集 before**：启动现有 Studio UI Next fixture，用内置浏览器打开真实 route，按本批矩阵截图并记录 DOM、console、page error、failed request、focus 和 scroll owner。
5. **实施最小改动**：优先复用现有 tokens、primitives、patterns 和图标；Vue 继续使用 Composition API、`<script setup lang="ts">`、显式 props/emits 和单一状态源。
6. **静态与行为验证**：先运行受影响 unit，再运行 lint、typecheck；共享层或阶段收口追加完整 unit、build、bundle gate。
7. **采集 after-1**：内置浏览器复走同一路径、同一尺寸、主题、密度、角色和状态，生成可比较截图。
8. **视觉复审**：逐张检查层级、密度、对齐、文案、状态、交互 affordance、焦点、滚动和长中文；必须写出具体发现，不能只写“看起来不错”。
9. **至少一次回看**：若发现 P0-P3 缺陷立即回改并生成 after-2；若 after-1 已满足门禁，也要记录“复审无新增改动”，不能为了迭代次数制造无意义 churn。
10. **正式回归**：运行受影响的 Studio UI Next Playwright；阶段出口再运行完整前端门禁。真实 WebView2 只在约定阶段执行。
11. **更新任务状态**：仅在代码、测试、截图与复审记录都存在时勾选；记录 source SHA 或 `DIRTY_WORKTREE_CANDIDATE`，随后自动进入下一项。

### 4.2 自动继续规则

满足以下全部条件时，Codex不等待用户，直接进入下一项：

- 修改位于已冻结白名单和唯一 Owner 内。
- 没有新增或改变后端合同、权限、保存、运行、SSE、Canvas、HostBridge 或持久化权威。
- 受影响测试通过，内置浏览器无新增 console/page error、请求异常、遮挡或滚动回归。
- 截图复审中 P0/P1 为 0，P2 已关闭或有不阻塞当前批次的明确归属。
- 当前改动没有触发新依赖、共享配置或 Git 历史操作。

以下情况才暂停并报告，不做推测性扩权：

- 当前代码与 F10/AGENTS 的 authority 或 Owner 边界冲突。
- 缺少后端合同、角色权限、正式状态或可复现 fixture，继续会伪造业务语义。
- 需要修改 `package.json`、lockfile、Router、App Shell、tokens、API contracts、HostBridge、`.csproj`、CI 或 evidence runner，但当前批次不属于主协调 Owner。
- 需要新增第三方依赖、外部视觉资产、改变品牌方向或删除/重命名公共 Design System API。
- 同一视觉问题连续三轮仍无法满足门禁，或修正 A 必然破坏 B，需要产品取舍。
- 需要真实 Windows 125%、独立 no-Node 目标机、Remote CI、Camera/PLC/Station/AI、产品 Owner 或生产环境。
- 需要 commit、push、分支操作、清理未归属文件或其他有外部/不可逆影响的动作。

### 4.3 不允许提前结束的理由

- detector 返回 `[]`。
- lint、typecheck、unit 或 build 通过。
- 页面没有水平 overflow。
- screenshot 非空或像素颜色数正常。
- 一个主题、一个 viewport 或 happy path 看起来正常。
- 历史 F02/F03/M06/G3 截图或报告曾经 PASS。

## 5. 内置浏览器截图闭环

### 5.1 两层证据

| 层级 | 用途 | 工具 | 证据性质 |
| --- | --- | --- | --- |
| L0 工作截图 | 每个小批次即时比较与视觉回改 | Codex 内置浏览器 | 方向性、可交互、非正式宿主证据 |
| L1 阶段截图 | 覆盖本阶段 route/state/theme/density | 内置浏览器 + 阶段 manifest | 视觉阶段证据，不等于 WebView2/DPI |
| L2 正式 Browser | 可复现 journey、错误、owner、overflow | 仓库 Playwright harness | Chromium fixture 证据 |
| L3 正式 Host | WinForms/WebView2、窗口、DPI、Canvas | 仓库 WebView2 脚本 | 真实宿主证据，仍需区分 100%/125% |

L0/L1 明确指定 Codex 内置浏览器，执行时必须使用独立的 in-app browser 会话；若该能力不可用就暂停并报告，不能静默改用 Chrome、外部 Browser 或 Computer Use。Computer Use 只可在真实 Windows/WebView2 交互无法由内置浏览器或仓库脚本完成时作为补充，并且不能冒充 L0/L1 或 DPI 证据。

工作截图统一写入：

```text
.tmp/studio-ui-next/view-polish/<stage>/<source-sha-or-dirty-candidate>/<batch>/
  before/
  after-1/
  after-2/
  review.json
  manifest.json
```

正式 F02/F03/F04 或 WebView2 runner 必须继续使用脚本要求的 `.tmp/studio-ui-next/f02-1/`、`f03/`、`f04/`、`f09/` 等目录。`view-polish` manifest 只记录其真实路径和 SHA，不复制或移动证据来冒充新的 run。

截图命名：

```text
<scene>-<route>-<viewport>-<theme>-<density>-<role>-<state>.png
```

每份 manifest 至少记录：

- `sourceSha`、`worktreeState`、stage、batch、时间和 hostKind。
- route、role、feature flags、fixture/session 类型和业务 state。
- CSS viewport、截图像素尺寸、DPR、theme、density、reduced-motion；真实宿主另记 native DPI 与 window/client size。
- console errors、page errors、failed requests、unexpected writes 和 HTTP 状态摘要。
- document 与局部 scroll width/height、唯一滚动 Owner、可见浮层边界和焦点元素。
- workspace/canvas/preview/SSE 等适用 owner ledger；不适用时显式写 `N/A`。
- screenshot SHA-256、before/after 配对关系、视觉发现和 `NON_COMPARABLE` 原因。

### 5.2 视口、主题、密度和角色

| ID | 环境 | 必测用途 |
| --- | --- | --- |
| B0 | 1920x1080，light，compact | 默认工作环境和所有主任务 |
| B1 | 1536x864，light，compact | 中等窗口的信息压缩 |
| B2 | 1366x768 或 WebView2 client 1350x704，light，compact | 125% 等效短屏压力；不得写成真实 125% |
| B3 | 1920x1080，dark，compact | dark 同等质量 |
| B4 | 1920x1080，light/dark，comfortable | density 投影与布局稳定性 |
| B5 | 真实 WinForms/WebView2，Windows 100% | 阶段/最终宿主证据 |
| B6 | 真实 WinForms/WebView2，Windows 125% | 只能在真实环境执行，浏览器 DPR 不可替代 |

角色至少覆盖 Admin、Engineer、Operator；每个 route 只测试合同允许的角色，不为截图伪造权限。profile/flag 关闭时必须证明旧 owner 已 unmount/dispose，不接受仅 CSS 隐藏。

### 5.3 黄金场景

| Scene | Surface | 必备状态 |
| --- | --- | --- |
| S00 | 初始化 / 登录 / 改密 | idle、busy、validation、auth error、stale recovery、长用户名 |
| S01 | Product Shell / Overview | online、stale、offline、role navigation、active route、菜单边界 |
| S02 | Projects | loading、empty、data、search、long-zh-CN、readonly、error |
| S03 | Project detail / lifecycle | dirty、saving、saved、conflict、unknown outcome、import/export、delete confirm |
| S04 | Workspace shell | loaded、no selection、selected、short viewport、splitter extremes |
| S05 | Flow / Operators / Inspector | search、flyout、node selected、invalid parameter、dependency、keyboard focus |
| S06 | Image / Preview / ROI | no image、loading、fresh、stale、error、overlay、pixel probe、ROI edit |
| S07 | Formal Run / Inspection | admission blocked、running、stopping、disconnected、reconcile、OK、NG、execution error、unknown |
| S08 | Results | local/station、empty、filter、paging、detail、evidence partial/expired/not-produced、export |
| S09 | Stations | online、offline、stale、warning、version mismatch、detail、readonly/admin command feedback |
| S10 | Settings | loading、stale、403、save success/failure/unknown/reconcile、secret field、long form |
| S11 | AI | idle、intent、clarification、plan、build、validate、apply preview、failure、cancel、recovery、history |
| S12 | Operators | catalog、search、category、detail、long parameter metadata、readonly |
| S13 | Diagnostics / About | startup fault、service fault、version/license/support、copyable diagnostic detail |

每个小批次至少覆盖 B0、B2、一个受影响的第二主题/密度和所有直接修改状态；每阶段出口覆盖该阶段全部 Scene 的 B0-B4 适用组合。V6 才运行全产品最终矩阵，避免每次微调都制造无效截图洪水。

### 5.4 视觉复审顺序

每张 after 截图按以下顺序审查：

1. **缩略观察**：3 秒内是否能找到页面主体、当前状态和唯一主操作。
2. **100% 观察**：基线、列、标题、数据、单位、图标、控件是否精确对齐；是否有随机间距和不必要描边。
3. **状态观察**：执行状态与判定结果是否混淆；警告、错误、NG、离线、stale、unknown 是否同时有结构或文案线索。
4. **任务观察**：常用操作是否可见，危险操作是否隔离，是否因“简洁”增加步骤或隐藏关键参数。
5. **压力观察**：长中文、最大合理数值、空数据、短屏、菜单/modal 边缘、table/Panel 滚动是否稳定。
6. **交互观察**：hover、focus-visible、active、disabled、loading、error、success 是否齐全且不造成 layout shift。
7. **主题观察**：dark 不是简单反相；muted text、border、focus、status 与 canvas 在两主题都清晰。
8. **前后对比**：改动是否真正降低视觉噪声、增强层级；若只是“换颜色/加阴影”则不通过。

## 6. Owner 与文件边界

默认由一个 Codex 主协调 Owner 串行推进。即使未来允许并行，也只能并行无文件重叠、无共享状态权威的叶子工作包。

| Owner | 可修改范围 | 禁止越界 |
| --- | --- | --- |
| `COORD-VIEW` | `TODOView.md`、tokens、Design System public API、App Shell/layout、共享样式、Router、共享 evidence config | 不并行；不改变业务合同或 Host authority |
| `OWN-V2-GOLDEN` | `project-workspace/**`、与黄金旅程直接相关的展示组件及对应测试 | FlowCanvas + Inspector + Preview + ROI 必须是同一纵向 Owner |
| `OWN-V3-RESULTS` | `results-read/**` 及局部测试 | 不创建第二 query/export owner |
| `OWN-V3-STATIONS` | `stations-read/**` 及局部测试 | 不创建第二 SSE/command owner |
| `OWN-V3-INSPECTION` | `inspection-run/**` 及局部测试 | 不替代 Runtime/Inspection authority |
| `OWN-V4-SETTINGS` | `settings/**` 及局部测试 | `settingsWriteCoordinator` 保持唯一；设备写入合同不扩权 |
| `OWN-V4-AI` | `ai-workbench/**` 及局部测试 | 不持有 AgentRun authority，不新增 artifact 合同 |
| `OWN-V4-READONLY` | `operators-read/**`、overview、diagnostics/about 页面及局部测试 | 只读 capability 不获得写入口 |
| `COORD-EVIDENCE` | `ClearVision.Product.UI.Tests/**`、`scripts/studio-ui-next/**` | 不创建第二 server/runner；正式脚本变更必须独立批次 |

以下始终是共享文件，只能由 `COORD-VIEW` 或仓库定义的主协调 Owner 修改：`package.json`、lockfile、Vite、TypeScript、ESLint、Router、ProductLayout/App Shell、tokens、Design System exports、API contracts、HostBridge、`.csproj`、CI、Feature Flags、根 `AGENTS.md`、F10 和共享 ADR。

## 7. 阶段总览

| 阶段 | 目标 | 状态 | 解锁条件 |
| --- | --- | --- | --- |
| V0 | 当前基线、黄金场景和截图闭环冻结 | READY | 无 |
| V1 | Design System 2.0 与全局视觉语法 | LOCKED | V0 DONE |
| V2 | 登录到结果的黄金旅程纵向精修 | LOCKED | V1 DONE |
| V3 | Overview / Inspection / Results / Stations | LOCKED | V2 DONE |
| V4 | Settings / AI / Operators / Diagnostics / About | LOCKED | V3 DONE |
| V5 | 文案、图标、动效、a11y、状态与全局一致性 | LOCKED | V4 DONE |
| V6 | Browser、WebView2、DPI 与产品签收 | LOCKED | V5 DONE |

## 8. V0：重新冻结视觉基线

**Owner**：`COORD-VIEW`。**目的**：以当前 HEAD 和当前代码建立本轮真实 before，不沿用历史截图结论。

- [x] **V0.1 候选取证**：记录 branch、HEAD、upstream、`git status --short --branch`、F10 当前状态和工作树归属；若非 clean，先分类已有改动并与之协作，不清理或覆盖。
- [x] **V0.2 路由/角色/状态清单**：从 Router、feature flags、profile、页面和现有 tests 生成当前 route-role-state 矩阵。
- [x] **V0.3 Legacy/Next 任务核对**：复用 M00/F04R 矩阵并抽查当前代码，把能力标为已等价保留、已优化保留、已重定位、只读接受、按 profile 隐藏、明确延后或缺失/回归。
- [x] **V0.4 Design System 漂移盘点**：把问题分类为 missing token、one-off implementation 或 conceptual misalignment；统计 `CvPanel`、硬编码颜色/尺寸、重复控件和卡片嵌套热点。
- [ ] **V0.5 内置浏览器 before 基线**：按 S00-S13 捕获 B0/B2 代表场景，至少覆盖 light/compact、dark/compact 和一个 comfortable 场景。**BLOCKED_BY_ENVIRONMENT：阶段内多次最小探针与 V6 恢复复测均在浏览器绑定/选择阶段超时并自动重置；L2 before 证据保留但不替代本项。**
- [x] **V0.6 问题台账**：每项使用 P0-P3、类型、证据、影响、建议、验证和 Owner；先排功能/状态/效率，再排视觉细节。
- [x] **V0.7 截图设施试运行**：验证隔离端口、fixture 清理、截图目录、metadata、console/page error、scroll metrics 和 SHA 命名均可重复。
- [x] **V0.8 冻结 V1 变更面**：明确哪些问题由 tokens/primitives 系统修复，哪些留给 capability；禁止在页面重复打一遍相同补丁。

**V0 Gate**：before manifest 完整；所有 P0/P1 有 Owner；Workspace 单一纵向 Owner 无争议；没有把 Browser fixture 写成 WebView2/DPI；内置浏览器进程和端口清理完成。

## 9. V1：Design System 2.0

**Owner**：`COORD-VIEW`。共享层串行修改，每次只处理一个系统性视觉问题。

- [x] **V1.1 颜色与表面**：审计 app/page/raised/floating/sunken/canvas、text、border、focus、selection 和 scrollbar；保持 graphite + 朱砂 + 技术蓝 + 独立状态色。
- [x] **V1.2 容器语法**：为连续页面区、工具工作区、真实 card、modal/floating 建立明确用法；减少默认完整描边，消除卡片套卡片和“描边 + 宽软阴影”。
- [x] **V1.3 排版与数字**：统一 page/section/body/secondary/caption/numeric 的字号、行高、字重和中文基线；正文不使用 9-11px，标题不使用流体字号。
- [x] **V1.4 密度与命中区**：复核 compact/comfortable 的 control、row、toolbar、pane header 和 icon button；常用桌面控件保持清晰可点，26px 仅用于确有空间证据的次要控件。
- [x] **V1.5 Button/IconButton**：统一 primary、secondary、quiet、destructive、icon-only 和 loading；补齐 hover/focus/active/disabled/error，图标按钮必须有 label/tooltip。
- [x] **V1.6 Field/Search/Select**：统一 label、required、help、validation、placeholder、单位、前后缀和只读态；长中文不遮挡操作。
- [x] **V1.7 Table/List/Pagination**：统一表头、行密度、selected/hover、空态、固定关键列、局部滚动和 numeric alignment；不把每行改成大卡片。
- [x] **V1.8 Status/Alert/PageState**：覆盖 loading skeleton、empty、401、403、error、offline、stale、partial、conflict 和 unknown；提示按“发生什么 -> 影响什么 -> 下一步”组织。
- [x] **V1.9 Modal/Menu/Toast/Tooltip**：统一 elevation、viewport 边界、body scroll、操作区、Escape、outside click、focus trap/return 和 timer dispose。
- [x] **V1.10 Icons**：收敛到当前 `CvIcon` 家族，补齐常用保存、运行、停止、刷新、筛选、缩放、撤销/重做等符号；不手绘风格漂移 SVG。
- [x] **V1.11 Motion**：统一 140-200ms 状态动效和 easing；禁止 `transition: all`、布局属性动画和页面编舞；验证 reduced motion。
- [ ] **V1.12 Design Lab 回归**：L2 已完成 after-1/after-2 共 24 张 light/dark、compact/comfortable 与短屏截图，Design Foundation Playwright 6/6 PASS；必需 L0/L1 因内置浏览器最小探针连续超时，状态为 `BLOCKED_BY_ENVIRONMENT`，不得以 L2 替代。

**V1 Gate**：Design System API 和语义有测试；无新增 one-off 视觉基础设施；Design Lab L1 截图完成；lint、typecheck、full unit、build、bundle gate 通过；P0/P1/P2 系统缺陷为 0。

## 10. V2：黄金旅程纵向精修

**Owner**：`OWN-V2-GOLDEN`；共享 Shell/Design System 改动回到 `COORD-VIEW`。Workspace 的 FlowCanvas、Inspector、Preview、ROI、保存和 Formal Run 不拆分 Owner。

- [x] **V2.1 初始化与登录**：提升产品身份、表单层级和错误恢复；移除面向用户的 `/api/auth/me` 等协议文案，同时保留真实会话语义。
- [x] **V2.2 Product Shell**：精修品牌、主导航、全局状态、会话/外观菜单和 active route；降低文字命令噪声，保持键盘与角色可见性。
- [x] **V2.3 Projects**：优化搜索、最近工程、列表/空态、创建/导入/导出、打开/关闭/删除的层级与密度；危险操作不借用品牌主色。
- [x] **V2.4 Workspace chrome**：冻结 top bar、command bar、operator rail、status bar、Canvas、Inspector、Preview 的尺寸关系和单一滚动 Owner。
- [x] **V2.5 Flow 与算子**：精修算子搜索/flyout、节点、端口、连线、选中、缩放、minimap 和空画布；Canvas 继续复用 canonical 内核。
- [x] **V2.6 Inspector**：精修参数分组、字段、单位、依赖、全局变量绑定、校验和错误定位；高频参数与当前错误保持可见。
- [x] **V2.7 Image/Preview/ROI**：让图像成为视觉主体，明确输入/输出、fresh/stale/error、overlay、像素信息和 ROI 编辑工具；避免工具条挤占图像。
- [x] **V2.8 保存与离开**：精修 dirty、saving、saved、conflict、unknown outcome、reconcile 和 leave guard；本地草稿与正式 PersistenceRevision 文案严格区分。
- [x] **V2.9 Formal Run**：精修 admission、运行、停止、断线、reconcile、OK、NG、执行错误和未知终态；Preview 与 Formal Run 不共享模糊标签。
- [x] **V2.10 黄金旅程复走**：登录 -> 工程 -> 打开 Workspace -> 添加/选中节点 -> 编辑参数/ROI -> 预览 -> 保存 -> 正式运行 -> 查看结果，全程记录点击数、焦点、状态和 before/after。

**V2 Gate**：S00-S08 黄金路径在 B0-B4 适用组合通过；B2 首屏仍可访问核心工作区与状态；owner ledger 无冲突且 dispose 归零；受影响 Playwright 通过；没有因视觉简化增加关键步骤或丢失旧版能力。

## 11. V3：核心生产与调查页面

这些工作包默认串行；只有在文件、状态、端口和测试资源完全隔离且有明确授权时才并行。

- [x] **V3.1 Overview**：把它做成可靠的工作入口和异常摘要，不做营销 KPI；突出当前工程、服务健康、最近动作和可执行入口。
- [x] **V3.2 Inspection Projects**：优化工程选择、运行前置条件、权限、stale/partial/empty 和恢复路径，401/403 不得伪装成无数据。
- [x] **V3.3 Inspection Run**：精修 RunConsole、进度、结果、保护原因、连续运行和停止反馈；高频控制稳定可达。
- [x] **V3.4 Results 态势层**：优化筛选、分页、执行/判定双轴、趋势/吞吐/缺陷摘要和异常优先级；不恢复虚假空 KPI。
- [x] **V3.5 Results 调查层**：优化详情、诊断、证据、对比、partial/expired/not-produced 和导出反馈；本机/Station 来源能力清楚区分。
- [x] **V3.6 Stations 总览**：建立在线、离线、stale、warning、版本/包异常的扫描节奏和排序；状态不靠颜色猜测。
- [x] **V3.7 Station 详情/管理**：精修健康、日志、运行包、结果和命令反馈；Operator 只读与 Admin 写入入口明确，unknown/reconcile 不伪造成功。
- [x] **V3.8 跨页连续性**：从 Overview/Inspection/Stations 进入 Results 或 Workspace 时保留对象、筛选和状态上下文，返回路径可预测。

**V3 Gate**：S01、S07-S09 的 B0-B4 适用组合通过；表格无页面级水平滚动，短屏操作不被固定 chrome 遮挡；SSE/query/command owner 单一；Results/Station/Inspection 的合同和权限语义无回归。

## 12. V4：设置、AI 与低频支持面

- [x] **V4.1 Settings 信息架构**：统一设置组导航、页内标题、保存范围、dirty/保存反馈和长表单滚动；不能把所有设置做成独立卡片墙。
- [x] **V4.2 通用/存储/运行时/数据库/安全/用户**：统一字段密度、帮助、危险操作、权限、secret、validation 和 unknown/reconcile；延期能力不显示伪入口。
- [x] **V4.3 Camera**：精修发现、绑定、参数、触发、单帧/连续预览、停止和标定入口；真实设备状态与 fixture 状态明确区分。
- [x] **V4.4 PLC/TCP/Station 设置**：精修 profile、连接测试、收发调试、错误和保存反馈；协议参数保持可扫描，不为简洁深藏关键字段。
- [x] **V4.5 AI 模型设置**：统一模型目录、active 状态、敏感字段、测试和错误；LLM 配置不伪装成 CV model artifact。
- [x] **V4.6 AI Workbench**：精修 intent、clarification、plan、build、validate、apply preview、cancel、failure、recovery、history；保持 AgentRun/EventStore 权威和单一 Owner。
- [x] **V4.7 Operators**：优化目录、分类、搜索、详情、端口/参数 metadata 和只读语义；工业数据密集而不拥挤。
- [x] **V4.8 Diagnostics/About**：让诊断、版本、宿主、后端、许可证和支持信息真实、清晰、可复制；默认首屏不堆协议细节。
- [x] **V4.9 Auth 辅助页面**：统一 Setup、Change Password、Unauthorized、Forbidden、Startup failure 的产品文案和恢复动作。

**V4 Gate**：S10-S13 和 Auth 辅助状态的 B0-B4 适用组合通过；Settings 写入 Owner 唯一；AI 不新增资源 authority；所有低频页面与主产品使用同一视觉语法，没有“像另一套软件”的页面。

## 13. V5：跨产品精修与韧性

- [x] **V5.1 中文词表**：统一工程、流程、算子、属性检查器、预览、感兴趣区域（ROI）、工作站、检测结果、正式运行、未配置、未判定、离线等术语。
- [x] **V5.2 文案清理**：移除用户可见的 owner、authority、profile、safe read、stage 编号、API path 和“下一阶段”等研发语言；保留诊断价值时降为次级技术信息。
- [x] **V5.3 动作与图标一致性**：同一命令在所有页面使用同一图标、名称、按钮层级和快捷键；图标优先于重复文字工具按钮，并有 tooltip/ARIA label。
- [x] **V5.4 键盘与焦点**：验证 skip link、自然 Tab、toolbar 方向键、Escape、focus trap/return、route focus 和 Canvas/ROI 快捷键；focus-visible 在两主题清楚可见。
- [x] **V5.5 全状态韧性**：逐页覆盖 loading、empty、error、401、403、offline、stale、partial、conflict、unknown、aborted、readonly、disabled 和 long-zh-CN。
- [x] **V5.6 响应式与滚动**：每轴一个滚动 Owner；消除全局水平滚动、双层滚动、越界 popover/modal、sticky 遮挡和 splitter 极值崩坏。
- [x] **V5.7 对比度与色觉**：正文/placeholder 达到 WCAG AA；选中、焦点、状态在不依赖颜色时仍可识别；dark muted text 不发灰失真。
- [x] **V5.8 动效与性能**：验证 reduced motion、无内容闪烁、无不必要 layout shift、无长期 animation/timer；视觉特效不影响 Canvas/图像交互帧率。
- [x] **V5.9 Vue 维护性**：route page 保持组合面，超大 SFC 按展示责任拆分；不复制 owner、state tree、request、watcher 或 write entry。
- [x] **V5.10 自动规则审计**：运行 Impeccable detector，并按最新 Web Interface Guidelines 审查受影响 UI；结果只作为缺陷输入，不作为审美签收。
- [x] **V5.11 全产品一致性巡检**：将 S00-S13 按工作流连续浏览，修复局部优秀但全局不一致的问题；完成最终 screenshot-driven correction pass。

**V5 Gate**：所有可自动构造状态都有截图或交互证据；P0/P1/P2 为 0，P3 无系统性重复；键盘、对比度、reduced motion、长中文和短屏通过；完整前端 gate 通过。

**V5 Gate 结果：DONE（IMPLEMENTATION_COMMITTED_AT `a3e59bd...`）**。方向性证据根保留采集时的 `dirty` 名称；其中 291 组 JSON/PNG 同名对和 12 张 screenshot-only PNG 均可解析/解码，缺失配对、损坏图片和 JSON 解析错误为 0。最终 Impeccable detector 返回 `[]`，最新 Web Interface Guidelines 定向扫描未发现可复现的 P0/P1/P2，候选 SHA 绑定的软件门禁与 Playwright 结果见 V6 记录。内置浏览器绑定失败不以 Playwright 或 WebView2 冒充通过。

## 14. V6：正式证据与签收

V6 分为 Codex 可自主完成的本机软件门禁和必须等待真实环境/人工的外部门禁。前者通过不能自动勾选后者。

> **历史记录（2026-08-10）**：下列 V6.1-V6.10 绑定上一候选 `a3e59bd...`；当前苹果式全局精修事实以本节下方 V6R（2026-08-11）为准。旧测试数字与 Browser 阻塞状态仅作审计留痕，不代表当前投影。

- [x] **V6.1 冻结候选**：取得 commit 授权后形成 clean source SHA；任何后续产品代码变化都会使 V6 软件/截图证据失效并重新开始。**DONE：用户授权后已提交产品与测试实现，候选 implementation SHA 为 `a3e59bd552d0e7dd73be9041487843daed87caea`；提交前 `git fetch origin --prune` 成功，local 与 `origin/studio-ui-next` 基线均为 `9c2ba21d...`。**
- [x] **V6.2 完整前端门禁**：lint、typecheck、full unit、production build、bundle gate、bundle reproducibility 全部绑定候选 SHA。**PASS_SHA_BOUND：lint、typecheck、production build、`bundle:ci` 与 `bundle:verify` 均 PASS。首次 full unit 中 `appMount.spec.ts` 首项 5 秒超时并引发 3 个 cleanup 级联失败，记为 `FAILED_THEN_PASSED_NOT_COUNTED_AS_INITIAL_PASS`；定向 5/5 PASS 后，完整串行重跑 140 files / `919/919` PASS。**
- [x] **V6.3 完整 Playwright**：运行 `CV_UI_SCENARIO=studio-ui-next` 全量；evidence-only skip 必须有合理环境说明，不能用占位 SHA 强行通过。候选 `a3e59bd...` 结果为 262 total、`175 passed / 87 evidence-only skipped / 0 failed`，未使用占位 SHA。
- [ ] **V6.4 内置浏览器最终巡检**：S00-S13 在 B0-B4 适用组合完成最终截图，console/page error、overflow、focus、owner 和状态清单归零。**历史重试在端口 `42943`/`42944` 的绑定与连接诊断超时，受控 server 均已停止、端口已释放。V6R 已恢复登录到工程页的代表交互，但 S00-S13 全矩阵仍 `NOT_PERFORMED`；本项保持未勾选，不以 Playwright/WebView2 替代。**
- [x] **V6.5 前后对比报告**：每个 surface 至少一组同状态 before/final；逐项说明层级、密度、操作效率和状态表达如何改善，不以形容词代替证据。冻结前的方向性报告见下表；候选 SHA 的 1920x1080、1536x864、1366x768 代表截图已重新目视复核。
- [x] **V6.6 WebView2 100%**：使用现有仓库脚本采集 Debug 与 Release 的 1920x1080、1536x864、1366x768，覆盖 light/dark、compact/comfortable、Canvas backing/pointer、owner 和 cleanup。候选 `a3e59bd...` 的六组真实窗口均 PASS，native DPI 均为 96；正式证据根为 `.tmp/studio-ui-next/f09/view-polish-v6-a3e59bd/`。
- [ ] **V6.7 Windows 125%**：在真实 120 DPI Windows 会话运行 WebView2 matrix 与 DPI audit；浏览器 viewport/DPR/force scale 只能做预检，不能勾选本项。**NOT PERFORMED：当前 Windows 会话为 96 DPI / 100%。**
- [ ] **V6.8 产品 Owner 视觉签收**：以黄金旅程和 before/final 对照进行人工复审，记录接受项、返工项和明确结论。**NOT PERFORMED / NOT GRANTED。**
- [x] **V6.9 状态回写**：由主协调 Owner 将真实证据路径、SHA 和未执行项更新到 F10/根 TODO；不得授予超出证据的 production acceptance 或 Legacy retirement。**DONE：F10 与根 TODO 已写入 `a3e59bd...` 候选、SHA-bound 软件/Playwright/WebView2 100% 证据、内置浏览器阻塞、125%/Owner 未执行及 production/Legacy 未授权边界。**
- [x] **V6.10 交接**：记录 Design System 用法、剩余 P3、已知环境债、复测命令和回滚边界；临时服务、端口、WebView2 user-data、数据库和 publish 目录全部清理。**DONE：用户授权的五个旧 publish/runtime 临时路径已永久删除（2,194 files / `1,454,810,170` bytes）；候选复验新建的 publish/runtime 根与一个空包装目录也在审计后删除（2,002 files / `1,440,353,363` bytes）。所有相关进程和端口均释放，最终 JSON/PNG 证据保留。**

**V6 Gate**：本机软件、Browser 和 WebView2 100% 全部通过后才能形成 `VISUAL_ENGINEERING_DONE`；当前软件与 WebView2 100% 已完成，Browser 只恢复了登录到工程页的 L0 交互，S00-S13 × B0-B4 的 L1 全矩阵未执行，因此该结论仍未授予。只有真实 Windows 125% 与产品 Owner 通过后才能形成 `VISUAL_ACCEPTANCE_GRANTED`。生产接受、独立 no-Node、Remote CI、现场硬件、生产 soak 和 Legacy 退役继续服从 F10/根 TODO，绝不由本视觉计划自动授予。

### 14.0 V6R：苹果式科技优雅全局精修复验（2026-08-11）

本节追加当前候选事实，不改写上方 `a3e59bd...` 的 V5/V6 历史执行记录。视觉方向冻结为
`APPLE_INSPIRED_TECH_ELEGANCE_WITH_INDUSTRIAL_QUIET_PRECISION`：以苹果式产品的秩序、材质克制、排版精度与细节完成度为审美标尺，同时继续服从工业高信息密度、Windows 系统字体、3-8px 圆角、语义色分离和单一 Owner 边界；不引入官网 hero、macOS 仿制、毛玻璃、装饰性渐变或消费级大卡片。

- [x] **V6R.1 候选冻结**：Apple refinement 从 `f132d9997` 继续，经 `59e3f1f8e`、`57e04858b` 收敛，产品与测试实现提交为 `bf662c838c4b066362169e06486f04a38be95899`。`aba69626995ae65d38829b99ac9387eb7bc62111` 只修复 no-Node 静态审计对 Vite 图片资产的错误拒绝，未修改产品运行代码。
- [x] **V6R.2 软件门禁**：`bf662c838...` 上 lint、typecheck、production build、`bundle:ci`、`bundle:verify`、Impeccable detector `[]` 与 `git diff --check` PASS；StudioUI 串行 Vitest 为 142 files / `930/930`，legacy/UI 合同单测为 45 files / `999/999`。本轮 .NET 测试 `NOT RUN`，不外推旧 G5 结果。
- [x] **V6R.3 完整 Playwright**：`bf662c838...` 上 265 total，`178 passed / 87 evidence-only skipped / 0 failed`，单 worker、隔离端口 `43224`；skip 仍只表示未启用的正式 evidence phase。
- [x] **V6R.4 内置浏览器恢复取证**：已真实完成登录并到达工程页，复核 1920x1080 light/compact 与 1366x768 dark/comfortable；console error/warn 为 0，工程页水平/垂直 overflow 为 0，唯一 `main` 与 leave guard owner 均为 1。证据口径仅为 `L0_INTERACTIVE_RECOVERED_AUTH_PROJECTS_PASS`；S00-S13 × B0-B4 的 L1 全矩阵 `NOT_PERFORMED`，不得写成 V6.4 PASS。
- [x] **V6R.5 WebView2 1920x1080**：`bf662c838...` 首轮的 17 个宿主 run 与 cleanup 均 PASS，但总入口因旧 no-Node 审计器只接受 `.js/.css`、错误拒绝合法 PNG 而 FAIL，保留为失败诊断；`aba696269...` 修正后在 `.tmp/studio-ui-next/f09/matrix/visual-aba696269-w100-20260811-140415-919/` 重跑 17/17 PASS，publish/static/runtime、DPI、local process-tree/no-Node 与 cleanup 全部 PASS。
- [x] **V6R.6 WebView2 三尺寸补证**：`.tmp/studio-ui-next/f09/view-polish-v6-aba696269/webview2-size-matrix-r1/` 的 Debug/Release × 1536x864/1366x768 为 4/4 PASS；viewport 分别为 1520x800 / 1350x704，截图 SHA/字节、Canvas backing/pointer、theme/density、Owner、overflow、runtime error、DPI report 与 cleanup 均 PASS。21 个 run manifest 和 21 个 cleanup JSON 全部绑定 `aba696269...`；全部真实窗口仅为 native 96 DPI / scale 1.0。
- [x] **V6R.7 清理与交接**：本轮尺寸 runtime、Release publish 与两个空 matrix publish wrapper 已按验证后的精确路径永久删除；临时 browser fixture 中 64 files / `2,350,765` bytes 的内容已删除，空 wrapper 保留。HTTP/CDP 端口释放，正式 JSON/PNG 证据保留。用户自有 `.impeccable/` 与其他未归属产物未修改、未提交。
- [ ] **V6R.8 外部门禁**：真实 Windows 125%、独立无 Node 目标机、Remote CI、现场 Camera/PLC/Station/AI、生产 soak、产品 Owner 签收、production acceptance 与 Legacy retirement 均未由本轮授予。

### 14.1 V6 当前证据结论

| 证据域 | 当前结果 | 证据 / 边界 |
| --- | --- | --- |
| 前端软件门禁 | `PASS_VISUAL_SHA_BOUND` | `bf662c838...`：lint、typecheck、production build、`bundle:ci`、`bundle:verify` PASS；StudioUI 串行 142 files / `930/930`，legacy/UI 合同单测 45 files / `999/999`；本轮 .NET tests `NOT RUN` |
| 完整 Playwright | `PASS_VISUAL_SHA_BOUND` | `bf662c838...`：265 total，`178 passed / 87 evidence-only skipped / 0 failed`；skip 只来自未启用的正式 evidence phase |
| 自动 UI 规则 | `PASS` | Impeccable detector `[]`；三路独立只读终审无 P0-P2；focus replacement、图片尺寸/alt、reduced motion、skip link、语义交互与架构 Owner 均有代码/测试锚点 |
| 内置浏览器代表交互 | `PARTIAL_INTERACTIVE_RECOVERED` | `.tmp/studio-ui-next/final-visual-review/current-worktree-{login,overview,projects}-*.png` 共 6 张；登录到工程页、light/compact 与 dark/comfortable、console 0、overflow 0、唯一 `main`/leave guard owner 通过；不是 S00-S13 全矩阵 |
| 历史 V5 截图完整性 | `PASS_HISTORICAL` | `a3e59bd...` 的 291 个配对 JSON/PNG 与 12 张 screenshot-only PNG 保留为历史方向证据，不冒充当前 V6R Browser 全矩阵 |
| WebView2 1920x1080 | `PASS_REAL_100_EVIDENCE_SHA_BOUND` | `.tmp/studio-ui-next/f09/matrix/visual-aba696269-w100-20260811-140415-919/`；17/17 子运行、17/17 cleanup、DPI、local process-tree/no-Node、publish/static/runtime 与外层清理 PASS；独立无 Node 目标机仍 `NOT_PERFORMED` |
| WebView2 1536x864 / 1366x768 | `PASS_REAL_100_EVIDENCE_SHA_BOUND` | `.tmp/studio-ui-next/f09/view-polish-v6-aba696269/webview2-size-matrix-r1/`；Debug/Release 4/4，viewport 1520x800 / 1350x704，Canvas backing/pointer、Owner、theme/density、overflow、runtime error 与 cleanup PASS |
| V6R JSON / PNG 完整性 | `PASS` | 21 个 run manifest 与 21 个 cleanup JSON 绑定 `aba696269...`；16 张正式 PNG 的实际 SHA-256/byte length/像素与引用一致；matrix、no-Node、两份 DPI report 和 `studio-ui-webview2-size-audit.json` 均 PASS |
| 内置浏览器最终矩阵 | `NOT_PERFORMED` | L0 代表交互已恢复，但 S00-S13 × B0-B4 的 L1 最终截图、focus 和状态清单没有完整执行；Playwright/WebView2 不替代本项 |
| Windows 125% | `NOT_PERFORMED` | 所有真实窗口只观测到 native 96 DPI / scale 1；DPR/force scale 不外推 |
| 产品 Owner / production acceptance | `NOT_PERFORMED / NOT_GRANTED` | 无人工签收；`VISUAL_ACCEPTANCE_GRANTED`、production acceptance 与 Legacy retirement 均未授予 |

### 14.2 S00-S13 前后对比报告

V6R 在既有 S00-S13 对照基础上追加了当前候选的登录、总览、工程页六张代表截图，位于
`.tmp/studio-ui-next/final-visual-review/`。它们证明 Apple refinement 已落到真实登录与工程路径、两档尺寸和两组主题/密度，但不构成新的 S00-S13 × B0-B4 全量 before/final 矩阵；下表仍保留 V5 的完整历史对照来源。

| Surface | 同状态 before / final 证据根 | 可核对的改善 |
| --- | --- | --- |
| S00 初始化 / 登录 / 改密 | `f04/view-polish-v0-9c2ba21d/before/` → `f04/view-polish-v5-dirty/v5-gate/` | 去除 API/协议研发文案；统一验证、忙碌、失败与恢复动作；外观偏好改为单一应用生命周期 owner |
| S01-S03 Shell / Overview / Projects / lifecycle | `f02-1/view-polish-v0-9c2ba21d/before/` → `f02-1/view-polish-v5-dirty/v5-gate/` | 导航收纳为可关闭菜单；内容层级由整圈卡片转为页面带与工作区；搜索、长工程名、保存/冲突/未知结果保持可扫描 |
| S04-S06 Workspace / Flow / Inspector / Preview / ROI | `f03/view-polish-v2-dirty/workspace-before/` → `f04/view-polish-v5-dirty/v5-gate/`、`v5-workspace-review/` | Canvas 获得稳定主工作面；Inspector/Preview 各自单滚动 owner；短屏保留保存、正式运行、ROI 与预览状态；业务失败、取消、无输出不再折叠为同一错误 |
| S07 Formal Run / Inspection | `f05/view-polish-v0-9c2ba21d/before/` → `f05/view-polish-v5-dirty/v5-gate/` | 执行状态与判定结果分轴；admission、occupied、running、reconcile 与终态具有中文原因/影响/下一步；长中文在短屏不断裂 |
| S08-S09 Results / Stations | `f02-1/view-polish-v0-9c2ba21d/before/`、`view-polish-v3-dirty/stations-before/` → `f02-1/view-polish-v5-dirty/v5-gate/` | Results 分为态势总览/调查详情并把低频诊断码移入高级筛选；Stations 按异常优先且明确包版本、离线/stale、只读与 Admin 命令反馈 |
| S10 Settings | `f07/view-polish-v4-dirty/settings-before-1/` → `f07/view-polish-v5-dirty/v5-gate/` | 分组导航、长表单和保存反馈共享同一层级；secret、restart-required、unknown/reconcile 与权限边界保持可见且不复制写 owner |
| S11 AI | `f06-g5/view-polish-v4-dirty/ai-before/` → `f06-g5/view-polish-v5-dirty/v5-gate/` | intent/clarification/plan/build/apply 的阶段职责可扫读；资源决策、阻断、恢复和历史抽屉不暴露内部 authority，也不改变 AgentRun 权威 |
| S12-S13 Operators / Diagnostics / About | `f02-1/view-polish-v4-dirty/operators-before/`、`support-before/` → `f02-1/view-polish-v5-dirty/v5-gate/` | catalog/detail 与长参数保持 GET-only；诊断详情可复制且剔除 token；版本、许可证、支持与服务故障由产品语言承载，协议细节降级折叠 |

### 14.3 交接边界

- Design System 继续以 `tokens -> primitives -> patterns -> capability composition` 使用；业务 owner、请求、写入口、Canvas 与 HostBridge 不进入视觉 primitive。
- P0/P1/P2 为 0。剩余 P3：dark Auth 仍通过滤镜复用亮色工作区截图；`<=920px` Auth 单栏没有正式截图矩阵；forced-colors 只有 Playwright 模拟、没有真实 Windows 高对比截图。后续局部审美调整不得改变保存、执行、AgentRun、Runtime/Station 或正式结果权威。
- 已知证据债：内置浏览器已恢复代表交互，但 S00-S13 × B0-B4 全矩阵未执行；真实 Windows 125%、产品 Owner 签收、独立 no-Node 目标机、Remote CI、现场 Camera/PLC/Station/AI 与生产 soak 仍未完成。
- 复测入口保持第 15 节命令；任何产品代码变化必须从 V6.1 重新冻结候选并重跑软件、截图和 WebView2 证据。
- 回滚继续使用既有 Startup Profile 与 `LEGACY_FALLBACK`；Legacy 源码未删除，`LEGACY_RETIREMENT=NOT_APPROVED`，Project/Flow/Result authority 无迁移或双写。

## 15. 验证命令模板

命令必须从当前代码重新确认后执行；下面是现有入口，不锁定历史测试数量。

### 15.1 StudioUI 快速门禁

工作目录：`ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/`

```powershell
npm run lint
npm run typecheck
npm run test:unit
npm run build
```

共享层或阶段出口追加：

```powershell
npm run bundle:ci
npm run bundle:verify
```

Impeccable detector 从仓库根运行：

```powershell
node .agents/skills/impeccable/scripts/detect.mjs --json ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src
```

若只改一个 capability，先运行其定向 Vitest，再在阶段 Gate 运行全量。

### 15.2 内置浏览器工作服务器

工作目录：`ClearVision.Product/tests/ClearVision.Product.UI.Tests/`

```powershell
$env:CV_UI_HOST = '127.0.0.1'
$env:CV_UI_PORT = '<本批次隔离端口>'
$env:CV_STUDIO_UI_EVIDENCE_PHASE = 'f09'
node .\tests\support\studio-ui-next-server.cjs
```

Codex 必须持有该进程的生命周期，等待 `/studio/index.html` ready 后用**内置浏览器**打开 `http://127.0.0.1:<端口>/studio/index.html`。截图完成后通过受控进程句柄停止服务并验证端口释放；不能遗留后台 server。

### 15.3 Studio UI Next Playwright

工作目录：`ClearVision.Product/tests/ClearVision.Product.UI.Tests/`

```powershell
$env:CV_UI_SCENARIO = 'studio-ui-next'
$env:CV_UI_PORT = '<隔离端口>'
npx playwright test tests/e2e/studio-ui-next/<affected>.spec.ts --reporter=list
```

阶段/最终全量：

```powershell
$env:CV_UI_SCENARIO = 'studio-ui-next'
$env:CV_UI_PORT = '<隔离端口>'
npx playwright test --reporter=list
```

正式 F02/F03/F04 截图环境变量和输出根必须复用当前 fixture 的校验规则；不要新建第二 runner，也不要把 legacy lane 的结果记为 Next PASS。

### 15.4 WebView2 / DPI

优先复用：

- `scripts/studio-ui-next/Invoke-StudioUiWebView2Evidence.ps1`
- `scripts/studio-ui-next/Invoke-StudioUiWebView2Matrix.ps1`
- `scripts/studio-ui-next/Test-StudioUiDpiEvidence.ps1`
- `scripts/studio-ui-next/Invoke-StudioUiFinalEvidence.ps1`

调用前重新读取参数和 F10 的最新证据边界，使用唯一 `RunName`、隔离 HTTP/CDP 端口、WebView2 user-data、数据库、runtime 和 publish 目录。临时 publish 只允许进入 `./.tmp/publish-check/`，完成后按脚本合同清理。

## 16. 全局 Definition of Done

只有以下全部满足，视觉工作才可宣称工程完成：

- 功能：旧版核心任务没有无处置丢失；保存、运行、结果、权限和错误路径仍使用现有 authority。
- 层级：每个页面有明确主体、当前状态和唯一主操作；没有卡片海洋、同权重边框或无效大留白。
- 密度：1920x1080 高效，1366x768/1350x704 可用；Canvas、Inspector、Preview 和高频动作不被挤成装饰区。
- 排版：中文、数字、单位、长名称和错误原因可读；用户界面不暴露无必要研发语言。
- 状态：Preview/Formal Run/History、执行状态/判定结果、NG/执行错误、stale/empty/offline/403/unknown 均清楚区分。
- 控件：default、hover、focus、active、disabled、loading、error、success 状态完整；命中区、tooltip 和键盘路径专业可靠。
- 主题：light/dark 和 compact/comfortable 使用同一层级语法，无主题特有遮挡、低对比或状态混淆。
- 响应式：无非预期全局水平滚动、双层滚动、超屏浮层、文本遮挡、布局跳动或动态内容引起的尺寸漂移。
- 可访问性：WCAG AA 对比度、语义结构、label、focus-visible、reduced motion 和非颜色状态线索通过。
- 架构：唯一 API、Host、Canvas、save、query、SSE 和 capability owner 边界未改变；dispose 后资源归零。
- 证据：每批有 before/after、metadata 和复审；每阶段有 Playwright；最终有真实 WebView2，并诚实区分 100%/125%。
- 审美：至少完成一次基于 after 截图的回改；clean detector、非空截图和无 overflow 从未被单独当作“精美”证明。

## 17. 执行记录模板

每完成一个批次，在本文件末尾追加一行；不要改写历史证据。

| Batch | 状态 | Source SHA / worktree | Owner 与文件 | 行为验证 | 内置浏览器证据 | 正式证据 | 复审与剩余项 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| V0.1 | READY | `9c2ba21d...` / plan baseline | `COORD-VIEW` / read-only | NOT RUN | NOT RUN | N/A | 下一唯一动作 |
| V0.1-V0.4 | DONE | `9c2ba21d...` / source clean; `TODOView.md` user-owned | `COORD-VIEW` / read-only | branch/upstream/F10/Router/M00/F04R/current source audited | BLOCKED_BY_ENVIRONMENT | L2 supplemental: 64 Playwright tests PASS; 146 PNG | route, capability and Design System inventories recorded under `.tmp/studio-ui-next/view-polish/v0/9c2ba21d.../v0-baseline/` |
| V0.5 | BLOCKED_BY_ENVIRONMENT | `9c2ba21d...` | `COORD-EVIDENCE` / no source edits | minimal browser probes timed out and reset kernel | NOT RUN | L0/L1 NOT RUN; L2 is not substituted | retry at each stage gate; no Browser/WebView2/DPI claim |
| V0.6-V0.8 | DONE | `9c2ba21d...` | `COORD-VIEW` / read-only | P0/P1 UI findings 0; P2/P3 and environment issue assigned; V1 whitelist frozen | BLOCKED_BY_ENVIRONMENT | review/manifest/component map recorded | V1 preparation may proceed; V0 Gate remains evidence-partial until L0/L1 is available |
| V1.1-V1.11 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / Design System whitelist | design unit 71/71; full unit 917/917; lint/typecheck/build/bundle gates PASS | BLOCKED_BY_ENVIRONMENT | L2 Design Foundation 6/6 PASS; after-1/after-2 各 12 PNG | after-1 的 P2 图标一致性问题已关闭；P0/P1/P2 系统缺陷为 0；manifest/review 位于 `.tmp/studio-ui-next/view-polish/v1/DIRTY_WORKTREE_CANDIDATE/design-system/` |
| V1.12 | BLOCKED_BY_ENVIRONMENT | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-EVIDENCE` / no further source edits | stage-gate minimal probe timed out and reset kernel; total minimal probe attempts 6 | L0/L1 NOT RUN | L2 is supplemental and is not substituted | retry at every later stage gate; V2 software work may proceed without claiming the V1 L1 gate |
| V2.1 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `OWN-V2-GOLDEN` / Auth whitelist | Auth/App unit 23/23; lint/typecheck; F04 Auth 4/4 PASS | BLOCKED_BY_ENVIRONMENT | L2 after-1/after-2 各 16 PNG | protocol copy removed; short-height P2 closed |
| V2.2 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / Product Shell whitelist | unit 18/18; lint/typecheck; affected F02/F04/M07 PASS | BLOCKED_BY_ENVIRONMENT | L2 shell-after-1 16 PNG | menu lifecycle/focus/containment reviewed; no correction churn required |
| V2.3 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `OWN-V2-GOLDEN` / Projects whitelist | unit/query/contracts 30/30; lint/typecheck; F02 Projects and F04 lifecycle PASS | BLOCKED_BY_ENVIRONMENT | L2 after-1/after-2 各 12 PNG | 1366 action-wrap P2 closed; review/manifest under `.tmp/studio-ui-next/view-polish/v2/DIRTY_WORKTREE_CANDIDATE/` |
| V2.4-V2.9 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `OWN-V2-GOLDEN` / Workspace vertical whitelist | Workspace/canvas/architecture unit 272/272; focused unit 36/36; lint/typecheck; F03 71/71 PASS | BLOCKED_BY_ENVIRONMENT | L2 before 12 PNG; after-1 52 PNG; final ROI correction 4 PNG | 1350x704 ROI action visibility P2 closed; Canvas/save/SSE/execution/HostBridge authority unchanged |
| V2.10 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `OWN-V2-GOLDEN` / continuous golden journey | targeted journey 1/1; 12 visible clicks; focus/state at 8 checkpoints; F03 71/71 PASS | BLOCKED_BY_ENVIRONMENT | L2 8 PNG and 8 JSON | Workspace owner/resource ledger zero after Results navigation; no additional P0/P1/P2 correction required |
| V3.1 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / Overview whitelist | Overview unit 2/2; lint/typecheck; F02 Overview 14/14 PASS | BLOCKED_BY_ENVIRONMENT | L2 after-1/after-2 and supporting state PNG | hierarchy/technical-copy P2 closed; no synthetic KPI added |
| V3.2 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / Inspection Projects whitelist | Inspection focused unit 5/5; lint/typecheck; F05 5/5 PASS | BLOCKED_BY_ENVIRONMENT | L2 before 3 PNG; after-1 project/stale/forbidden PNG | partial-state content-loss P1 closed; successful stale reads remain visible |
| V3.3 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / Inspection Run whitelist | Inspection focused unit 5/5; lint/typecheck; F05 5/5 PASS with one worker | BLOCKED_BY_ENVIRONMENT | L2 continuous/occupied/long-zh blocked PNG | technical-copy and recovery-hierarchy P2 closed; execution authority unchanged |
| V3.4-V3.5 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / Results presentation and F02 fixture whitelist | Results unit 48/48; lint/typecheck; corrected F02 16/16 PASS with one worker | BLOCKED_BY_ENVIRONMENT | L2 before; after-1/after-2 each 48 PNG and 48 JSON | initial expanded matrix had 12 failures from three missing existing-contract analysis mocks; fixtures added and full rerun passed; after-2 has no new P0/P1/P2 |
| V3.6-V3.7 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / Stations presentation and existing F02 fixture whitelist | Stations unit 72/72; lint/typecheck; F02 visual 22/22 PASS with one worker | BLOCKED_BY_ENVIRONMENT | L2 before; after-1/after-2 each 41 PNG and 41 JSON | priority, package badge, missing-package and deployment-label findings closed; query/SSE/command/package authority unchanged |
| V3.8 / V3 Gate | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / Overview, shared deep links and existing cross-surface fixtures | continuity unit 23/23; full unit 918/918; lint/typecheck; applicable Overview/Results/Stations/Inspection L2 interactions PASS | BLOCKED_BY_ENVIRONMENT | Overview continuity 8 PNG and 8 JSON; prior Results/Stations/Inspection stage captures retained | filtered Station list → detail → Results → detail → filtered list verified; unsafe return targets rejected; L0/L1 remain unavailable |
| V4.1-V4.5 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / Settings and device presentation whitelist | full unit 918/918; lint/typecheck/build; F07 Settings/device 32/32 PASS | BLOCKED_BY_ENVIRONMENT | L2 V4 gate 20 PNG and 20 JSON under `.tmp/studio-ui-next/f07/view-polish-v4-dirty/v4-gate/` | B0-B4 overview plus affected group/device states; save/runtime/device owners and secret semantics unchanged |
| V4.6 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / AI Workbench presentation and existing fixture whitelist | AI Workbench/history 25/25 PASS with one worker; full unit/lint/typecheck/build PASS | BLOCKED_BY_ENVIRONMENT | L2 V4 gate 37 PNG and 37 JSON under `.tmp/studio-ui-next/f06-g5/view-polish-v4-dirty/v4-gate/` | B0-B4 entry matrix and failure/recovery/history states; AgentRun/EventStore/resource authority unchanged |
| V4.7 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / Operators presentation and existing F02 fixture whitelist | Operators behavior and B0-B4 matrix 17/17 PASS with one worker | BLOCKED_BY_ENVIRONMENT | L2 12 PNG and 12 JSON in the shared F02 V4 gate directory | search/query URL ownership, GET-only behavior, long metadata and read-only semantics verified |
| V4.8 | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / Diagnostics and About presentation whitelist | support behavior and B0-B4 matrix 14/14 PASS with one worker | BLOCKED_BY_ENVIRONMENT | L2 12 PNG and 12 JSON under `.tmp/studio-ui-next/f02-1/view-polish-v4-dirty/v4-gate/` | copied diagnostics excludes token; protocol details collapsed; service fault remains actionable |
| V4.9 / V4 Gate | DONE | `9c2ba21d...` / `DIRTY_WORKTREE_CANDIDATE` | `COORD-VIEW` / Auth presentation, application preference lifecycle and F04 fixture whitelist | Auth 6/6; targeted lifecycle/architecture 22/22; full unit 918/918; lint/typecheck/build PASS | BLOCKED_BY_ENVIRONMENT after two 30-second browser binding retries; controlled server PID stopped and port `42851` released | L2 24 PNG and 24 JSON under `.tmp/studio-ui-next/f04/view-polish-v4-dirty/v4-gate/`; V4 affected suites all PASS | Auth now consumes the single app-lifetime preference owner across B0-B4; final screenshot review found P0/P1/P2 = 0; WebView2/DPI NOT PERFORMED |
| V5.1-V5.11 / V5 Gate | DONE | `a3e59bd...` / `IMPLEMENTATION_COMMITTED` | `COORD-VIEW` / S00-S13 presentation, tests and evidence fixtures | lint/typecheck; complete serial rerun 140 files / 919/919 unit; production build; bundle gates; full Playwright 175 passed / 87 evidence-only skipped / 0 failed | BLOCKED_BY_ENVIRONMENT after final 30-second binding retry; controlled server stopped and port `42943` released | 291 paired JSON/PNG + 12 screenshot-only PNG; Impeccable `[]`; Web Guidelines P0/P1/P2 = 0 | screenshot-driven correction complete; no systematic P3; historical direction evidence retains its capture-time `dirty` path names; Browser evidence is not substituted |
| V6.1-V6.3 / V6.5-V6.6 / V6.9 | DONE | `a3e59bd...` / `SHA_BOUND_IMPLEMENTATION` | `COORD-VIEW` / evidence, audit and status docs | lint/typecheck/build/bundle PASS；首次 full unit 超时与 3 个 cleanup 级联失败不计初次通过，定向 5/5 后完整串行重跑 919/919 PASS；Playwright 175 passed / 87 evidence-only skipped / 0 failed | N/A；V6.4 单独记录 | real WebView2 Debug/Release 1920x1080 + 1536x864 + 1366x768 PASS at native 96 DPI；21 run manifests、21 cleanup JSON、16 referenced PNG audit PASS | F10/root TODO updated；no 125%、Owner、production acceptance or Legacy retirement claim |
| V6.4 recovery retry | BLOCKED_BY_ENVIRONMENT | `a3e59bd...` / `SHA_BOUND_IMPLEMENTATION` | `COORD-EVIDENCE` / no product source edits | controlled static server returned HTTP 200; Browser selection and prescribed connection diagnostic both timed out and reset | L0/L1 NOT RUN | server terminated; port `42944` unreachable and listener count 0 | environment block independently reproduced; no fallback evidence substituted |
| V6.7-V6.8 | IN_PROGRESS | `a3e59bd...` / `SHA_BOUND_IMPLEMENTATION` | external 120 DPI environment / Product Owner | Windows 125% and Owner signoff NOT PERFORMED | N/A | 100% evidence retained; 125%/human acceptance NOT PERFORMED | waiting for the required environment and accountable reviewer; no local substitute is valid |
| V6.10 cleanup / handoff | DONE | `a3e59bd...` / `SHA_BOUND_IMPLEMENTATION` | `COORD-VIEW` / approved temporary roots and handoff docs | five old roots removed: 2,194 files / `1,454,810,170` bytes; candidate publish/runtime roots and empty wrapper removed: 2,002 files / `1,440,353,363` bytes | N/A | all targets rechecked absent; final JSON/PNG evidence retained | permanent deletion completed under explicit authorization; processes and ports released |
| V6R.1-V6R.4 Apple refinement | DONE | `bf662c838...` / `VISUAL_IMPLEMENTATION_COMMITTED` | `COORD-VIEW` / global StudioUI presentation, focused legacy canvas presentation and tests | lint/typecheck/build/bundle PASS；StudioUI 930/930；legacy/UI contracts 999/999；Playwright 178 passed / 87 skipped / 0 failed；.NET tests NOT RUN | `PARTIAL_INTERACTIVE_RECOVERED`：登录到工程页、6 张 light/compact 与 dark/comfortable 截图、console/overflow/owner PASS；L1 full matrix NOT PERFORMED | L2 full Playwright PASS | Apple-inspired tech elegance landed without authority/Owner/save/Canvas/HostBridge drift；three P3 evidence gaps retained |
| V6R.5 first WebView2 matrix | FAILED_RELATED | `bf662c838...` / `VISUAL_IMPLEMENTATION_COMMITTED` | `COORD-EVIDENCE` / no product code edits | 17/17 individual WebView2 manifests and 17/17 cleanup PASS | N/A | matrix total FAIL because the existing no-Node static audit rejected a valid Vite PNG manifest asset | failure retained under `.tmp/studio-ui-next/f09/matrix/visual-bf662c838-w100-20260811-135321-830/`; not reused as final PASS |
| V6R.5-V6R.7 evidence and cleanup | DONE | `aba696269...` / `EVIDENCE_FIX_COMMITTED` | `COORD-EVIDENCE` / one-line manifest asset path audit fix, WebView2 evidence and temporary cleanup | PowerShell parse and diff check PASS；final matrix local no-Node/process-tree PASS | N/A | 1920 full matrix 17/17 + size matrix 4/4；21 manifests、21 cleanup、16 PNG、native 96/1.0、DPI/audit PASS | runtime/publish roots 与 two empty wrappers removed；browser fixture 的 64 个文件已删除、空 wrapper 保留；formal evidence retained；clean machine no-Node/125% remain NOT PERFORMED |
| V6R.8 external acceptance | IN_PROGRESS | `aba696269...` / `EVIDENCE_HEAD` | external Browser full matrix / 120 DPI environment / Release, Field and Product Owners | S00-S13 Browser L1、Windows 125%、Remote CI、field hardware、production soak、Owner signoff NOT PERFORMED | L0 representative interaction only | local 100% evidence retained | no production acceptance or Legacy retirement claim |

状态只使用：`LOCKED`、`READY`、`IN_PROGRESS`、`BLOCKED_BY_CONTRACT`、`BLOCKED_BY_ENVIRONMENT`、`DONE`、`DEFERRED`。任何未实际执行的 Browser、WebView2、Windows 125%、现场硬件、Remote CI 或人工签收必须写 `NOT RUN`、`NOT PERFORMED` 或 `BLOCKED`。

## 18. 下一唯一动作

本机可自主执行的实现、候选冻结、软件/Playwright/WebView2 100% 证据、审计与清理已经完成。下一动作是补齐内置浏览器 S00-S13 × B0-B4 的 L1 最终矩阵，或取得任一外部条件并关闭对应门禁：进入真实 120 DPI Windows 会话完成 V6.7，或由产品 Owner 执行 V6.8 人工签收。三者互不替代；任何后续产品代码变化都必须从 V6.1 重新冻结并重跑候选证据。
