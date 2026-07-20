# ClearVision Studio UI Next F04.2 旧版与新版严肃对标审计（PROPOSED）

> 本文是只读审计产物，不改变 F04、F04.1、F05 或任何正式阶段状态。

## 0. 审计基线与证据边界

### 0.1 实际 Git 基线

| 对象 | 实际分支 | 实际 HEAD | 上游/远端 | 工作树 | 审计处理 |
|---|---|---|---|---|---|
| 旧版 `C:\Users\HerverJun\Desktop\ClearVision` | `codex初稿` | `4386d8f3537e80084802567b41d96414b0ddacd0` | `origin/codex初稿` 本地远端引用为 `bea404394ac8cf403cca719c1990c426414a06c2`；当前工作树落后 1 个提交 | 1 个用户未跟踪文件 `ClearVision_视觉算子科学性与稳定性分析报告.md` | 未 fetch、未切换、未清理；审计的是实际工作树，不冒充远端 tip |
| 新版 `C:\Users\HerverJun\Desktop\ClearVision-UI-Next` | `studio-ui-next` | `56fbf18fcb59f91e9d63666c08e302db92ff692c` | `origin/studio-ui-next` 同 SHA，ahead/behind 为 `0/0` | 用户已把 `appsettings.json` 的 `StudioUiEnabled` 从 `false` 改为 `true` | 保留原样；截图和报告以该实际工作树为准 |

远端均为 `https://github.com/HerverJun/ClearVision.git`。

### 0.2 实际启动配置

- 旧版：`WorkspaceV2Enabled=false`，其余 legacy capability flags 由现有 owner/fallback 解释；正式入口是 `wwwroot/index.html`。
- 新版：`StudioUiEnabled=true`、`WorkspaceCapabilityEnabled=false`。按 `StudioStartupProfileCatalog.Resolve`，这不是 `NEXT_FULL_CANDIDATE`，而是 `ISOLATED_TRUTH_TABLE`；`WebView2Host` 将后者映射为 `Studio2.Workspace=false`。因此当前真实启动配置会加载新版产品壳层，但不会开放新版工程工作区。
- 新版浏览器工作区截图来自仓库 F03/F04 fixture，并显式把 `Studio2.Workspace=true`，用于审计已实现 capability；它不能证明当前实际启动配置已经把工作区交给新版 owner。

关键代码：

- 新版 `Configuration/StudioOptions.cs:37-57`：启动 profile 真值表。
- 新版 `WebView2Host.cs:330`：`Studio2.Workspace` 来自 `WorkspaceCapabilityEnabled`。
- 新版 `appsettings.json:30-31`：当前实际为 UI on、Workspace off。

### 0.3 实测与限制

- 旧版 Playwright：3/3 PASS，覆盖 light 主题正式页面、1920 桌面布局、1600/1366/1024 视口矩阵。
- 新版 Playwright：22/22 PASS，覆盖工程、结果、工作站、工程生命周期、Workspace、Inspector、Preview、ROI、1920/1366/1350 与浏览器 DPR 矩阵。
- 新版 `npm run typecheck`：PASS；`npm run lint`：PASS。
- 新版构建：PASS，但 Vite 报告主 chunk 超过 500 KB；本次产物为 `888,589 B` JS + `160,325 B` CSS。
- 新版诊断页现有截图循环因默认 `Operator` 访问 Engineer/Admin 路由而失败 1 条；随后用与 F02 等价的工程师 browser fixture 只读补拍，页面水平溢出为 0。该失败属于测试权限 fixture 漂移，不是诊断页本体失败。
- 数据源：页面截图均为 `BROWSER_FIXTURE` / `HARNESS_SEEDED_SESSION`，不是生产数据库、现场相机、PLC 或 Station。
- 真实 Windows 125% DPI：**NOT PERFORMED**。浏览器 DPR 1.25 仅作分层证据，不冒充系统 DPI。
- 真实 WebView2、Release publish、真实端点、相机、PLC、Station、正式运行包：**NOT PERFORMED**。

## 1. 执行摘要

**一句话结论：REWORK：保留 Vue、严格 TypeScript、单一 owner、合同解码、生命周期诊断和 canonical Canvas 适配层，但必须重做产品层的信息架构与能力迁移，把旧版已成熟的检测、设置、AI、全局变量、最终判定、特殊参数编辑、Station 操作与导出闭环吸收回来。**

新版不是失败的技术实验：Workspace、结果追溯、只读监控、认证/路由守卫、保存冲突和资源释放都有可验证的工程与体验收益。但它目前也不是可替代旧版的完整产品：7 个旧版主域中，“检测”“AI”被明确禁用，“设置”实际指向诊断，工作站仅只读；工作区的最终判定和全局变量只是禁用占位，文件选择、相机绑定等特殊编辑器未接入。当前启动配置甚至未开放已实现的 Workspace。

因此不满足 KEEP 的“核心任务总体不低于旧版、无系统性回归”条件；同时 Vue 架构收益、已迁移的高质量工作区/结果/监控和自动化资产足以否定 ABANDON。

## 2. 功能完整性矩阵

| 领域 | 旧版能力 | 新版状态 | 分类 | 证据 | 影响 |
|---|---|---|---|---|---|
| 首次初始化 | 初始化管理员、进入会话 | 独立 `/setup`、自动登录、恢复与失败态 | 已优化保留 | `router.ts:37-46`；F04 auth 用例 | 边界更清楚 |
| 登录/会话 | 登录、当前用户、401 处理 | 登录、改密、登出、returnTo 白名单、并发 401 收敛 | 已优化保留 | `router.ts:140-207`；F04 auth 通过 | 新版更安全、可测 |
| 角色/权限 | 前端按角色禁用编辑入口 | 路由 meta + capability 内独立 403/只读态 | 已优化保留 | `router.ts:91-128,190-203` | 减少越权误导 |
| 工程列表/搜索 | 列表、搜索、排序、最近打开 | 表格、搜索、排序、最近工程 | 已优化保留 | 新旧 1920/1366 工程截图 | 新版扫描更快 |
| 新建工程 | 标准/示例语义、保存当前画布为新工程 | 仅“新建空白工程” | 明确延后 | `ProjectsPage.vue:509-510` | 模板和已有画布复用路径消失 |
| 打开/详情/重命名 | 打开、删除；主画布内编辑工程 | 详情页、更新、open authority、删除 reconcile | 已优化保留 | `ProjectDetailPage.vue:86-139` | 新版闭环更可诊断 |
| 删除工程 | 确认后删除 | operation id、重放、tombstone、失败重试 | 已优化保留 | F04 G3C 用例通过 | 新版可靠性明显更高 |
| 工程导入/导出 | 工程 JSON 导入/导出 | 无页面入口、无 capability | 未迁移 | 旧 `index.html:369-384`、`app.js:2868,3188`；新版无匹配 | 迁移/备份任务不可完成 |
| 运行包导出 | 从工程库或当前工程导出正式运行包 | 无入口 | 未迁移 | 旧 `app.js:3012-3138` | Station 投产闭环中断 |
| 算子库 | 工作区分组、搜索、收藏、添加 | 独立只读算子库 + 工作区 Rail/Flyout 搜索/收藏/添加 | 已重定位/优化保留 | `OperatorsPage.vue:144-160`、`OperatorRail.vue:193-275` | 浏览与添加职责更清晰 |
| 节点选择/移动/复制/删除 | canonical Canvas 支持 | 继续复用 canonical adapter；复制、粘贴、副本、禁用、删除、撤销/重做 | 已等价保留 | F03 interaction 用例；`canonicalFlowCanvas.ts` | 核心编辑未重造 |
| 连线/类型/环路 | 端口兼容、连线和防环 | 合同与 pointer wiring 已测 | 已等价保留 | F03 pointer wiring 用例 | 核心语义保留 |
| 画布缩放/平移/小地图 | 支持 | 支持，且布局 owner 管理 splitters | 已优化保留 | Workspace 1920/1366 截图 | 新版空间利用更稳定 |
| 子图/Lint/DryRun | 旧版存在子图入口与校验反馈 | 当前产品截图/页面未发现完整可达闭环 | 尚未迁移 | 旧 `index.html:244-247`；新版 route/UI 无对应入口 | 复杂流程调试能力下降 |
| 基础参数编辑 | 文本、数值、滑块、枚举、布尔、依赖、只读、校验 | 文本、数值、滑块、枚举、布尔、nullable、依赖、只读、校验 | 已优化保留 | `parameterEditorRegistry.ts:46-79`、`parameterValidation.ts` | 新版类型与错误定位更稳 |
| 文件选择器 | 已接入路径/文件语义 | extension slot 存在，但显示“尚未接入” | 明确延后 | `parameterEditorRegistry.ts:48` | 部分采集/模型算子不可完整配置 |
| 相机绑定 | 动态读取 `/cameras/bindings` 并绑定 | extension slot 存在，但“尚未接入” | 明确延后 | 新 `parameterEditorRegistry.ts:50-51`；旧 `propertyPanel.js:1538-1600` | 采集算子关键路径不可闭环 |
| 全局变量绑定 | 管理器、source/target binding、参数联动 | Project 合同和保存 payload 保留；入口禁用 | 未迁移 | `WorkspaceShell.vue:263-276`；旧 `propertyPanel.js:1303-1342` | 高频工程复用能力回归 |
| 标定/测量特殊编辑器 | calibration workbench、测量只读语义 | 通用 Inspector 未吸收完整工作台 | 未迁移 | 旧 `propertyPanel.js:2254-2289` | 专业算子配置退化 |
| 节点预览 | 手动/自动、结构化结果、错误、取消 | 手动预览、结构化/空/业务失败/安全阻断/网络失败/取消 | 已优化保留 | Preview 7 状态用例与截图 | 新版状态层级更可信 |
| 图像工具/像素探针 | 缩放、平移、适应、实际大小、像素信息 | 等价工具 + pixel probe 锁定状态 | 已优化保留 | F03 G4 用例 | 新版反馈更集中 |
| ROI | 图上编辑、约束、撤销/重做、同步 | ROI session、应用/放弃、undo/redo、参数同步 | 已优化保留 | `next-roi-1920x1080.png` | 新版是当前最成熟区域之一 |
| 保存/未保存 | 正式保存、本机草稿提醒、冲突提示 | PersistenceRevision、dirty、重试、409、unknown outcome、GET reconcile、Leave Guard | 已优化保留 | F03 G5、F04 G3C | 新版工程收益显著 |
| 最终判定 | 顶栏入口与完整编辑对话框 | 仅禁用按钮，未实现编辑 owner | 未迁移 | `WorkspaceShell.vue:205-222`；旧 1920 final-decision 证据 | 正式判定配置不可完成 |
| 正式运行/停止 | 顶栏运行 + 检测工作台 | Workspace 内 admission/execute/stop/reconcile | 已优化保留 | F03 G6 | 单次正式运行 authority 更清晰 |
| 连续检测/生产保护 | 独立检测页、连续运行、缺料/连续 NG 等保护 | 顶栏“检测”禁用，未见等价连续检测工作台 | 未迁移 | `ProductLayout.vue:58`；旧 inspection 模块 | 现场连续任务不可替代 |
| 检测结果列表/筛选 | 实时看板、筛选、分页、详情 | 本机/Station 双源、URL 筛选、分页、双轴状态、详情 | 已优化保留 | 结果页截图、F02 results 用例 | 新版列表判断效率更高 |
| 结果诊断/技术追溯 | 详情、诊断、图表、导出、SSE | 详情、诊断、技术追溯；无导出/实时订阅闭环 | 已重定位/部分保留 | `ResultsPage.vue`；旧 `resultPanel.js:35,1614-1668` | 日常排障更清楚，但报告/实时能力回归 |
| 结果分析看板 | KPI、良率、雷达、吞吐、CPK/MTBF | 当前列表/详情优先，无等价分析域 | 尚未迁移 | 旧 `index.html:420-661` | 管理/趋势分析能力减少 |
| 工作站列表/异常定位 | 实时 SSE、全站/单站、详情联动 | URL 筛选、9 种结果、摘要、详情 | 已优化保留（只读） | Station 1920/1366 截图 | 新版扫描与状态语义更好 |
| 工作站健康/最近结果 | 详情内健康、日志、命令、结果 | 健康快照和最近结果 | 已等价保留（只读子集） | `StationDetailPage.vue:366-501` | 基础核对可完成 |
| 工作站日志/命令/运行包 | 日志、命令、正式/测试包下发、审计 | 明确只读，无日志、命令和下发 | 未迁移 | 旧 `stationMonitorView.js:830-1005,1274`；新 `StationsPage.vue:249` | 现场处置闭环丢失 |
| 工作站结果导出 | CSV/JSON/Excel | 无入口 | 未迁移 | 旧 `stationMonitorView.js:1475-1477` | 现场取证效率下降 |
| 设置 | 外观、PLC、TCP、Station、存储、DB、运行保护、相机、AI、用户 | 顶栏“设置”实际进入 StudioUI 诊断 | 意外产品回归 | `ProductLayout.vue:74-82`；旧 `settingsView.js:321-366` | 新版无法承担设备与系统配置 |
| AI 工作台 | 澄清、计划、Build、Apply/Undo、恢复等 | 顶栏禁用 | 未迁移 | `ProductLayout.vue:73`；旧 `features/ai/` | 完整能力域缺失 |
| 诊断/关于 | 分散在旧设置与状态栏 | 独立 Diagnostics/About | 已优化保留 | `router.ts:123-139`、诊断截图 | 新版运维可观测性更好 |

## 3. 核心任务对标

点击数以本次 fixture 的直接导航起点计；文本输入和画布拖动单独说明。

| 用户任务 | 旧版体验 | 新版体验 | 胜出方 | 差距原因 | 严重度 |
|---|---|---|---|---|---|
| 登录并进入产品 | 单页登录后进入 legacy shell；初始化/路由边界较弱 | setup/login/change-password 分路由，401 与 returnTo 有明确闭环 | 新版 | 认证状态机和路由 guard 独立 | P2 |
| 搜索并打开工程工作区 | 工程导航 1 + 搜索 + 打开 1 + 流程导航 1，约 3 次点击 | 工程导航 1 + 打开工作区 1，约 2 次点击 | 新版（fixture） | 详情/工作区路由化；但当前 appsettings 实际禁用 Workspace | P1 |
| 新建空白工程并打开 | 约 4 次点击，支持进一步保存当前画布/模板语义 | 约 4 次点击，create reconcile 更可靠，但只支持空白工程 | 平手 | 新版可靠性更高，产品选项更少 | P1 |
| 导入/导出工程 | 工程页各 1 个直接入口，可完成 | 无入口、不可完成 | 旧版 | capability 未迁移 | P1 |
| 添加并编辑基础算子 | Rail 分组/搜索后选择算子，属性在同屏 | Rail/Flyout 1 + 算子 1，Inspector 同屏；依赖/校验更清楚 | 新版小胜 | Vue projection 与 typed registry | P2 |
| 配置相机/文件/全局变量/标定 | 入口和编辑器已存在 | 显示延后或按钮禁用，任务不可完成 | 旧版 | 特殊编辑器和全局变量 owner 未迁移 | P1 |
| 预览节点并编辑 ROI | 工作区同屏，功能成熟但状态块较碎 | 同屏 Preview/Image/ROI，成功/空/失败/取消层级稳定 | 新版 | Preview 与 ROI lifecycle owner 更清晰 | P2 |
| 保存并处理 409/响应丢失 | 有正式保存和草稿提醒，恢复语义较分散 | 1 次保存；409、unknown outcome、重试、GET reconcile、离开保护都有测试 | 新版 | PersistenceRevision 与单写入口完整 | P1（正向） |
| 单次正式运行、停止、恢复未知结果 | 顶栏运行并进入旧检测链路 | Workspace 内 1 次运行；stop/reconcile 就地出现 | 新版 | authority identity 与 mutation gate 明确 | P1（正向） |
| 连续生产检测与保护 | 独立检测页可达 | 检测导航禁用，任务不可完成 | 旧版 | 产品域未迁移 | P1 |
| 筛选结果并看诊断详情 | 首屏先是 KPI/图表，记录列表在下方，需要滚动；详情和导出较丰富 | 筛选与表格首屏可见，默认右侧详情，0-1 次点击 | 新版做日常排障；旧版做分析/导出 | 新版重排正确，但遗漏分析和导出 | P1 |
| 定位异常工作站 | 旧版全站矩阵 + 详情联动，信息多但卡片密集 | 表格 + 明确状态 + 右侧摘要，1 次进入详情 | 新版 | 状态词汇和表格扫描更成熟 | P2 |
| 查看日志、下发命令/运行包 | 选站后就地完成 | 只读，任务不可完成 | 旧版 | 新版主动缩成 read-only profile | P1 |
| 修改 PLC/相机/存储/运行保护设置 | 设置 1 + tab 1，保存当前页 | “设置”进入诊断，无业务设置 | 旧版 | 导航语义与实际 capability 不等价 | P1 |
| 使用 AI 生成/修改工程 | AI 1，进入既有澄清/计划/Build/Apply 链 | AI 禁用 | 旧版 | capability 未迁移 | P1 |

## 4. 视觉与专业度对标

### 4.1 工程页

- 旧版：[1920](./Studio_UI_Next_F04_2_审计截图/legacy-project-1920x1080.png) / [1366](./Studio_UI_Next_F04_2_审计截图/legacy-project-1366x768.png)
- 新版：[1920](./Studio_UI_Next_F04_2_审计截图/next-project-1920x1080.png) / [1366](./Studio_UI_Next_F04_2_审计截图/next-project-1366x768.png)
- 结论：新版表格列、最近工程、搜索和危险操作边界更清楚，视觉精度更高；但 1920 下有效内容只占上部约三分之一，页面标题/说明占用偏大，且动作少于旧版。旧版更紧凑但单行工程信息弱、大片留白同样存在。新版视觉胜，功能旧版胜。

### 4.2 流程工作台与属性检查器

- 旧版：[1920](./Studio_UI_Next_F04_2_审计截图/legacy-workspace-1920x1080.png) / [1366](./Studio_UI_Next_F04_2_审计截图/legacy-workspace-1366x768.png)
- 新版：[1920](./Studio_UI_Next_F04_2_审计截图/next-workspace-1920x1080.png) / [1366](./Studio_UI_Next_F04_2_审计截图/next-workspace-1366x768.png)
- 结论：新版把 Inspector、Canvas、Preview 形成稳定三栏，保存/运行/状态栏在 1366 首屏仍可达；splitter 和最小/最大尺寸均受 owner 约束。旧版空工程时 Canvas 更宽，但右侧结果卡堆叠且信息层级弱。新版工作区更像成熟工业工具。
- 回归：新版示例中的算子名、参数名和网络错误存在英文（如 `Inspector Source`、原始 request URL），与简体中文优先冲突；特殊编辑器缺失又使视觉上的完整 Inspector 产生“已全部迁移”的错觉。

### 4.3 节点预览、图像与 ROI

- 新版：[结构化结果](./Studio_UI_Next_F04_2_审计截图/next-preview-1920x1080.png) / [ROI 编辑](./Studio_UI_Next_F04_2_审计截图/next-roi-1920x1080.png) / [网络错误](./Studio_UI_Next_F04_2_审计截图/next-preview-error-1920x1080.png)
- 旧版预览位于上述 Workspace 截图右侧；本次 fixture 未提供同节点同图像，不能做像素级等状态比较。
- 结论：新版 Preview/ROI 的视觉层级、状态 badge、图像工具、结构化输出和就地错误恢复明显优于旧版。网络失败直接显示 `Request to http://127.0.0.1:5177/... failed...`，是 P1 中文错误恢复回归，必须改成“发生了什么、影响什么、下一步做什么”。

### 4.4 结果页与详情

- 旧版：[1920](./Studio_UI_Next_F04_2_审计截图/legacy-results-1920x1080.png) / [1366](./Studio_UI_Next_F04_2_审计截图/legacy-results-1366x768.png)
- 新版：[1920](./Studio_UI_Next_F04_2_审计截图/next-results-1920x1080.png) / [1366](./Studio_UI_Next_F04_2_审计截图/next-results-1366x768.png)
- 结论：旧版首屏被 8 个 KPI 和 3 个图表占满，历史记录需要向下滚动；新版把筛选、结果表和详情放到首屏，执行状态与判定结果分轴，是高频排障的实质提升。1366 下新版右侧诊断码被截成 `LEGACY_ER...`，关键标识缺少可见展开/tooltip，属 P2。
- 旧版的趋势、缺陷分布、吞吐、CPK/MTBF 和 CSV/JSON/Excel 不应被当作“旧后台模板”删除；它们应重定位为分析视图或可切换 tab。

### 4.5 监控页与异常详情

- 旧版：[1920](./Studio_UI_Next_F04_2_审计截图/legacy-stations-1920x1080.png) / [1366](./Studio_UI_Next_F04_2_审计截图/legacy-stations-1366x768.png)
- 新版：[1920](./Studio_UI_Next_F04_2_审计截图/next-stations-1920x1080.png) / [1366](./Studio_UI_Next_F04_2_审计截图/next-stations-1366x768.png) / [异常详情](./Studio_UI_Next_F04_2_审计截图/next-station-detail-1600x1000.png)
- 结论：新版表格、状态 badge、诊断摘要和详情页比旧版卡片矩阵更易扫描，状态颜色不承担唯一语义。旧版同页可联动日志、命令、运行包和导出，新版只有只读详情；视觉成熟不等于产品完整。

### 4.6 设置与诊断

- 旧版设置：[1920](./Studio_UI_Next_F04_2_审计截图/legacy-settings-1920x1080.png) / [1366](./Studio_UI_Next_F04_2_审计截图/legacy-settings-1366x768.png)
- 新版诊断：[1366](./Studio_UI_Next_F04_2_审计截图/next-diagnostics-1366x768.png)；新版 [概览 1920](./Studio_UI_Next_F04_2_审计截图/next-overview-1920x1080.png)
- 结论：两个页面不是等价替代。旧版设置在 1366 下仍能同时呈现 tab、字段和保存范围，功能密度成熟；新版诊断页面简洁、边界说明清楚，但把顶栏“设置”指向诊断会让用户误以为设备/系统设置仍存在。这是信息架构回归，不是局部样式问题。

### 4.7 视觉审计评分（`$impeccable audit`）

| 维度 | 分数 | 主要结论 |
|---|---:|---|
| Accessibility | 3/4 | skip link、单 main、focus-visible、aria-live、语义表格和表单 label 较完整；现有测试覆盖键盘与 reduced motion |
| Performance | 2/4 | Canvas owner 有优化，但 router 同步导入；888 KB 主 JS chunk 触发 >500 KB 警告 |
| Responsive | 3/4 | 1920/1366/1350 无全局水平溢出；结果详情关键长码仍截断 |
| Theming | 4/4 | light/dark、compact/comfortable、reduced motion 都由 tokens 投影 |
| Anti-patterns | 3/4 | 产品页总体不像通用 AI 后台；detect 仅命中内部 Design Lab 的 1 个 side-tab，不影响正式页；脚本体品牌字标和部分大标题/大留白略偏消费级 |
| **总分** | **15/20 Good** | 工程和局部 UI 可继续，但产品迁移完整性不在该分数内，不能用 15/20 为 KEEP 辩护 |

`$impeccable` anti-pattern verdict：**正式产品页不构成“AI 生成感”失败；真正的问题是成熟能力缺失和导航语义不等价。**

## 5. 工程架构对标

| 维度 | 旧版 | 新版 | 判断 |
|---|---|---|---|
| 组件/模块边界 | 约 99 JS + 20 MJS，已有 capability owner，但 `app.js` 3518 行、AI workspace 7200 行、resultPanel 3315 行 | 110 TS + 55 Vue；route、capability、owner、contract、adapter、DS 分层 | 新版明显胜 |
| 类型安全 | 动态对象、运行时兼容分支多 | `strict`、`noUncheckedIndexedAccess`、`exactOptionalPropertyTypes`，边界 decode | 新版明显胜 |
| 状态 Owner | legacy 已开始引入 capability owner，但全局 registry/DOM 依赖仍重 | ProductRuntime、query owner、workspace owner、preview/ROI/run/persistence owner 可单独 dispose | 新版明显胜 |
| 数据权威 | 复用既有 HTTP/保存链，但 DOM/local state 与服务定位交织 | Project/Results/Station 只作投影，PersistenceRevision 与本地 revision 分开 | 新版胜 |
| Canvas | canonical 内核成熟 | Vite alias 复用 legacy canonical Flow/Image Canvas，没有第二内核 | 新版正确保留资产 |
| 生命周期 | 有 managed listener 和 capability dispose，但跨模块一致性弱 | ownerCount、AbortController、订阅、timer、blob、artifact 等有 ledger 与 20-cycle 测试 | 新版明显胜 |
| 可测试性 | Playwright/Node 测试丰富，但很多直接绑定全局 DOM | unit + typed fixtures + route/capability e2e + browser/WebView2 harness | 新版胜 |
| 可访问性 | 主导航/表单已有 ARIA，但动态 innerHTML 容易漂移 | 语义 primitive、focus trap、skip link、reduced motion 更系统 | 新版胜 |
| 响应式/DPI | 已有 1024/1366/1920 Quiet Precision 证据 | compact/comfortable、1366/1350、DPR 矩阵更系统 | 新版胜；真实 125% 均未在本轮执行 |
| 新页面成本 | 需理解全局 app、registry 和手工 DOM 生命周期 | 路由 + typed contract + query owner + DS primitive 路径清楚 | 新版胜 |
| 当前复杂度风险 | 单文件超大、CSS 23k+ 行、动态兼容债务高 | `workspaceContracts.ts` 1444 行、`ResultsPage.vue` 1074 行；开始出现新 mega-file | 新版仍需治理 |
| 包体/加载 | 静态模块较多但无 Vue runtime | 当前单主 chunk 888 KB，路由未 lazy-load | 旧版当前加载成本可能更低；新版需拆包 |

### 新版工程收益能否在旧版渐进获得

- 可以渐进获得：更严格的 capability owner、AbortController 清理、统一错误映射、Design Tokens、可访问性 primitive、Playwright 状态矩阵。
- 难以低成本获得：Vue SFC 组合、严格 TypeScript 跨层合同、typed props/emits、路由级 auth guard、统一 query owner、可证明的 mount/dispose ledger。给 9 万行 legacy JS 全量补类型和 owner，实质上会形成另一场迁移。
- 结论：新版架构收益足以保留；但这些收益不能为功能缺口背书。正确路线是保留底层架构，重做产品层迁移，而不是继续按当前“只读页 + 禁用占位”直接收尾。

## 6. 回归与迁移缺口

### 明确延后

- 文件选择器、相机绑定 extension slot。
- 最终判定和全局变量可见入口只有禁用占位。
- 顶栏检测、AI 明确标注尚未接入。
- Station 以只读 profile 承载。

### 尚未迁移

- 工程导入、导出、运行包导出、模板/示例/保存当前画布为新工程。
- 连续检测工作台及生产保护说明/控制。
- 全局变量管理与参数绑定、最终判定编辑器。
- 标定/测量工作台等特殊编辑器。
- 结果分析看板、实时订阅、导出；完整 Station 日志、命令、运行包下发、结果导出。
- PLC、TCP、Station、存储、数据库、运行时、相机、AI、用户等业务设置。
- AI 澄清/计划/Build/Apply/Undo/恢复产品入口。

### 意外回归

- 顶栏“设置”实际导航到“诊断”，名称与能力不等价。
- 当前候选实际 appsettings 为新版壳层 on、Workspace off，不是完整候选 profile。
- 现成 diagnostics 截图循环使用 Operator 访问 Engineer/Admin 页面，证据链与权限合同漂移。
- Preview 网络错误直接暴露英文 URL/transport 文案。
- F04 design handoff baseline 仍写“Operator Flyout 未挂载”，但当前 `OperatorRail.vue` 已实际挂载 Flyout，阶段证据文档漂移。

### 视觉退步

- 部分页面标题/说明和大空白更像通用管理产品，不如旧版设置页的工业配置密度。
- 品牌手写体在紧凑工业工具顶栏略显消费级；不是阻断项。
- 英文算子/参数和 raw URL 破坏中文专业感。

### 操作效率退步

- 导入/导出、连续检测、设置、AI、全局变量、最终判定、Station 操作直接不可完成，不是“多点几次”。
- 结果分析/导出被迫离开新版或回旧版。
- 1366 结果详情关键长诊断码被截断。

### 架构收益

- 严格 TypeScript 与运行时合同 decode。
- 单一 ProductRuntime/query/workspace/capability owner。
- 复用 canonical Canvas 和现有 HTTP/保存权威。
- PersistenceRevision、unknown outcome reconcile、Leave Guard。
- 可观测的 lifecycle ledger 与资源释放测试。
- Design System、主题/密度/reduced motion、键盘和语义基础。

## 7. 三条路线成本

| 路线 | 修改范围 | 主要风险 | 重复工作 | 未来维护影响 | 结论 |
|---|---|---|---|---|---|
| 继续当前新版方向（KEEP） | 在现有页面局部补文案、样式和少量入口 | 会把系统性能力缺口误判为 polish；为了“看起来完成”可能继续放置禁用占位或只读替代 | 后续仍要二次重做信息架构与设置/检测/Station | 两套产品长期并存，用户和测试语义分叉 | 不推荐 |
| 保留 Vue 架构并重做产品层（REWORK） | 重构顶层导航与 capability map；迁移检测、设置、AI、全局变量、最终判定、特殊编辑器、Station 操作和导出；保留现有 owner/contracts/DS/Canvas | 容易越过后端权威或产生第二 owner；必须按 capability 单 owner、单写入口推进 | 已做的部分 Page composition 会调整，但底层 owner、contract、tests 可复用 | 产品与工程资产最终统一；新增页面成本最低 | **推荐** |
| 放弃新版回旧版治理（ABANDON） | 停止 Vue 路线，在 9 万行 legacy JS/2.3 万行 CSS 上继续拆 owner、加类型和测试 | 大文件、动态 DOM、全局耦合继续累积；难以获得严格跨层类型和可证明 lifecycle | 新版 Workspace/Results/Station/auth/DS/tests 大量作废；旧版还要再做同类治理 | 短期功能最完整，长期修改风险最高 | 不推荐 |

不提供精确工期：当前证据只能支持工作包规模和风险排序，不能支持人员、依赖和后端契约未确认时的日期承诺。

## 8. 最终建议

### 推荐路线

选择 **REWORK**。保留 Vue 新架构和当前可验证的工程资产，但把下一阶段定义为“旧版成熟产品语义回吸”，而不是继续当前页面做视觉精修。

支持该结论的充分证据：

1. 新版 Workspace、Preview/ROI、结果列表/详情、Station 只读监控、认证、保存和生命周期已有真实代码与通过的浏览器测试，不应丢弃。
2. 检测、AI、设置、最终判定、全局变量、特殊编辑器、工程/结果/Station 导出和 Station 操作是系统性缺口，KEEP 条件明确不成立。
3. 旧版大文件和动态 DOM 维护成本真实存在；全面回退会丢掉难以在旧版低成本复制的严格类型与 owner/lifecycle 资产，ABANDON 条件不成立。
4. 视觉失败不是全局：新版工作区、结果和监控已局部高于旧版；失败在于产品信息架构与能力覆盖，不是 Vue 本身。

### 为什么不推荐另外两条路线

- 不选 KEEP：需要重新设计主要产品导航、设置域、检测域和工作区高频操作，不是有限修正。
- 不选 ABANDON：新版工程收益显著且已有高价值能力；问题集中在产品层迁移策略，可通过受控重做收回，而不是底层架构不可用。

### 仍可能改变结论的未知项

- `origin/codex初稿` 比本次旧版工作树多 1 个提交；若该提交大幅改变上述能力，需要补审，但单个普通 bugfix 不太可能改变路线级结论。
- 真实 Windows 125% DPI、真实 WebView2、Release publish、真实 API/数据库、相机/PLC/Station 未执行。
- 尚未逐个核对所有算子特殊编辑器与所有 StationAdmin 权限组合。
- 未验证真实生产数据量下新版 888 KB chunk、500 条以上结果和复杂图像 artifact 的长期内存表现。

### 下一步最多允许执行什么

- 允许：只读冻结旧版 capability map；为 REWORK 编写一个新的、非阶段状态的产品层迁移提案；按单 capability owner 列出后端合同复用、页面结构、交互闭环和验收证据；先做信息架构和操作流原型审查。
- 禁止：直接进入 polish；宣称 F04/F04.1/F05 通过；默认打开 `NEXT_FULL_CANDIDATE`；复制 legacy 大文件到 Vue；新增第二 API client、保存链、EventBus、Canvas、HostBridge 或前端权威；在未迁移业务能力前删除/隐藏旧版入口；开始提交、推送或创建 PR。

---

AUDIT_STATE=DONE
RECOMMENDATION=REWORK
PRODUCT_VISUAL_CONFIRMATION=FAIL
