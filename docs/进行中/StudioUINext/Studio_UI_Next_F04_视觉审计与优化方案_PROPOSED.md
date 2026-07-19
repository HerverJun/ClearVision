# Studio UI Next F04 视觉审计与优化方案（PROPOSED）

> 状态：只读审计与下一轮优化规划，不代表实现批准。
> 审计日期：2026-07-19（Asia/Shanghai）
> 审计基线：`studio-ui-next` / `65640f02902c36f6cdcfd3b0f6b63e90db8f81cd`
> 审计范围：F04 Product Shell、Auth、Overview、Projects、Project Detail、Workspace、Operator Rail、FlowCanvas、Preview、Image/ROI、Inspector、Results，以及相关 Design System tokens/primitives。
> 本轮变更：仅新增本文档；未修改 UI、业务代码、设计 tokens、配置、测试、提交或远端分支。

## 0. 结论先行

F04 的工程门禁通过与“实际观感仍然普通”并不矛盾。现有门禁已经有效证明：路由、会话、Owner 生命周期、保存/正式运行合同、焦点、遮挡、横向溢出、Browser fixture、WebView2 force-scale、发布与回滚链路可工作；但这些门禁主要回答“能否正确、安全地完成任务”，没有充分回答“主任务是否一眼可见、空间是否像专业桌面工具、视觉是否具有克制而精密的产品判断”。

当前界面的主要问题不是 tokens 完全失控，也不是圆角或颜色单点错误，而是以下系统性组合：

1. **通用页面语法过强**：大量页面采用 `PageHeader + 带标题说明的 Panel + Panel`，所有区域都有完整边框、圆角、标题和说明，形成普通 SaaS 管理后台的平均化结构。
2. **表面角色使用错误**：`CvPanel` 默认同时使用 1px 边框、圆角和宽软阴影，连续工作区也被切成一组“独立卡片”，削弱工业桌面软件应有的连续性、精度和空间效率。
3. **Workspace 的业务主体被工程诊断语言包围**：Canvas、Preview、Inspector 本应成为视觉主体，但 `owner / draft / revision / metadata / Run / Stop / Reconcile / STALE / Inspector` 等开发与合同词直接进入核心界面，工具栏、状态徽标和底栏同时争夺注意力。
4. **功能承载仍是阶段性子集**：旧版中的最终判定、全局变量、工程导入/导出/运行包、示例工程、完整检测控制、结果图片/证据/对比/导出、AI、设置等尚未在 Next 产品入口中形成等价承载。受控 pilot 可以明确延后，但在默认替换旧版前属于阻断项。
5. **1080P 结论偏向“未溢出”，未证明“高效”**：当前布局可在自动测试中不溢出，但固定三栏、Preview 最小高度、多个固定 chrome、无 splitter/折叠，以及窄宽度直接隐藏 Inspector，会让有效工作区在 Windows 125% 或短屏窗口中明显变小。
6. **视觉证据覆盖仍有空白**：F04 产品截图为 light + compact；DPR 1.25/1.5/2 是 Browser emulation 或 WebView2 `force-device-scale-factor` 分层证据，不能替代真实 Windows 125% 系统缩放。F01 的 light/dark × compact/comfortable 证据来自 Design Lab，不等于 F04 产品页面。

一句话目标方向：

> 把 Studio UI Next 从“使用工业数据的企业后台”收敛为“以 Canvas、图像、参数和运行状态为主体的桌面工程工具”：连续表面、明确主次、低噪声 chrome、中文主语义、真实状态、可压缩但不丢能力。

### 0.1 审计健康度

| 维度 | 评分 | 结论 |
| --- | ---: | --- |
| 可访问性 | 3/4 | skip link、表单标签、Modal focus trap、reduced motion、对比度总体良好；仍有图标按钮命名、错误 ARIA role、Canvas 焦点变量和程序化焦点可见性问题。 |
| 性能 | 3/4 | Owner 生命周期与 Canvas 资源治理扎实；158 项算子列表全部渲染，未使用虚拟化或 `content-visibility`。 |
| 响应式与 1080P | 2/4 | 无全页横向溢出，但 Workspace 通过隐藏 Inspector/Operator Rail 应对窄宽度，没有可恢复入口；真实 Windows 125% 产品视觉证据未完成。 |
| 主题与 tokens | 3/4 | 颜色、字体、密度、状态 token 基础完整；主要问题是 primitive 默认映射与使用方式，而非 token 缺失。 |
| 反模式与产品辨识度 | 2/4 | 卡片化、重复眉题、状态胶囊、技术词泄漏和等权布局明显，尚未建立专业工业工作台的独特任务层级。 |
| **合计** | **13/20** | **Acceptable：工程基础可靠，但需要一次以 Workspace 为核心的系统视觉重构。** |

### 0.2 证据真实性

- 当前 `StudioUI/` 与 F04 final code SHA `0c78962d` 完全一致；与最终 WebView2 旅程的 `198b99f1` 也无 StudioUI 差异。因此现有最终截图可用于审计当前 UI。
- Browser 产品视觉矩阵：`.tmp/studio-ui-next/f04/visual-0c78962d-final-r3/`，30 张，19 场景，1366×768 / 1600×1000 / 1920×1080，全部为 light + compact。
- 最终真实 WebView2 旅程：`.tmp/studio-ui-next/f04/dev-final-5/g6-dev-final-5/`，覆盖 Setup、Login、Overview、Projects、Project Detail、Workspace、Preview、Save、Formal Run、Results、Delete。
- WebView2 force-scale 1.25 工作区证据：窗口 1600×1000，Browser logical viewport 1268×750，Canvas logical size 662×300；原生窗口 DPI 仍记录为 96，因此这是 WebView2 force-scale 证据，不是真实 Windows 125% 系统 DPI。
- F04 G6 已明确 `F04_PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER`。本审计是对“产品视觉确认尚未通过”的进一步解释，不否定已有工程 PASS。

## 1. 当前视觉问题总结

### 1.1 Product Shell：稳定，但像通用后台壳层

当前 Shell 的 208px 深色侧栏、48px 顶栏、页面标题、卡片区和右上角会话操作，是熟悉可靠的后台结构，但缺少工业桌面工具的“任务持续性”。

- 默认页面中，侧栏品牌、导航、工作模式说明、顶栏面包屑、服务状态、外观设置、用户、修改密码和退出同时常驻；其中“工作模式”说明属于架构解释，不应占据长期视觉位置。
- Workspace 模式把导航缩成单个汉字：`item.label.slice(0, 1)`（`ProductLayout.vue:133`）。截图中的“概 / 工 / 算 / 检 / 诊 / 关”辨识度低；若工作站 profile 开启，“工程”和“工作站”都可能显示为“工”。这不是高级的紧凑，而是语义丢失。
- 顶栏与 Workspace 工具栏都承载工程上下文，形成重复 chrome；Workspace 中仍常驻外观、用户和会话操作，挤占最宝贵的横向空间。
- Active nav 使用侧边短色条，常见于后台模板；其存在不构成功能错误，但进一步强化“普通管理后台”观感。

### 1.2 Overview：信息正确，但主任务错误

Overview 的核心任务应是：**确认 Studio 是否可工作，并快速继续最近工程或进入当前工程任务**。

当前实现把“本地服务”和“当前会话”放成两个同等大 Panel，再将“最近工程”和“快速入口”放成第二组 Panel（`OverviewPage.vue:100-245`）。结果是：

- 服务端口、认证用户等系统信息获得了与工程同等甚至更高的视觉权重；
- 最近工程为空时，页面大部分区域成为无业务意义的留白；
- “快速入口”重复侧栏导航，没有提供新的高频价值；
- 页面更像运维门户首页，而不是视觉工程师的工作起点。

### 1.3 Projects：能完成任务，但不是工程资产工作台

Projects 已具备空白创建、搜索、排序、列表、最近工程、打开、详情和删除，是 F04 的重要进步。但页面仍然呈现为“一张大表格卡 + 一张最近工程卡”：

- `CvPageHeader` 的“工程管理 / 工程 / 技术说明”占据首屏纵向空间；
- 列表外层 Panel、Toolbar、表格边界和最近工程 Panel 叠加，边框层级偏多；
- 搜索框有可见 label，旁边又有“搜索”按钮，密集但不精炼；
- 页面主按钮“新建空白工程”使用与危险色接近的品牌红；同页“删除”也为红色，主操作和危险操作的视觉距离不足；
- “通过权威 open/delete command”“稳定摘要字段”等描述是实现合同，不是用户任务语言。

### 1.4 Project Detail：卡片套卡片感最明显

Project Detail 把工程摘要、编辑工程信息、流程摘要、正式资源摘要排成 2×2 Panel（`ProjectDetailPage.vue:329-402`）。每块都有标题、说明、边框、圆角和阴影，导致：

- 四块区域近似等权；打开工作区、工程身份、可编辑字段、流程就绪度之间没有主次；
- 页面在 1080P 下看似空旷，实际有效信息密度不高；
- “persistence revision”“schema”“详情响应”“保存链”等词让用户感知到内部实现，而不是工程状态；
- 视觉上非常接近企业后台的详情页模板。

### 1.5 Workspace：能力最强，也是视觉问题最集中的区域

Workspace 的核心任务是：**构建、调试、预览并保存一个可正式运行的视觉流程**。它应是 Studio 最有辨识度、最专业的界面。

当前优点：Canonical FlowCanvas、Operator Rail、Inspector、Image/ROI、Preview、Save、Formal Run、Results handoff 已形成完整的 Owner 边界；Canvas 命令与状态也较完整。

当前视觉与空间问题：

- 外层有 Product topbar、Workspace toolbar、FlowCanvas toolbar、FlowCanvas status、Workspace statusbar，固定 chrome 叠加约 176px；在短屏中明显侵蚀 Canvas 和 Preview。
- 三栏固定为约 196–232 / 520+ / 280–320px，Preview 固定为 180–280px；没有使用已有 `CvSplitter`，也没有折叠/恢复控制。
- `<1220px` 直接隐藏 Inspector，`<920px` 再隐藏 Operator Rail（`FlowWorkspace.vue:91-97`）。隐藏后没有明确的重新打开入口，属于能力可达性问题。
- Preview 内又分“图像 + 详情”，详情内包含 Alert、ROI 边框块、结构化结果、Artifacts、诊断；多个滚动 owner 与分隔线同时出现。
- Workspace toolbar 同时显示保存、Run、结果入口、Stop/Reconcile 条件操作和三个状态徽标，全部横向竞争。
- 底部状态栏显示 `Workspace owner / Inspector / Preview / Image / ROI / Persistence / F03 G1–G5`，这是测试与诊断信息，不是现场工程师需要的持续上下文。
- Canvas 节点、Inspector、Preview 和工具栏混用英文：`Run`、`Stop`、`Reconcile`、`Inspector`、`draft`、`metadata`、`STALE`、`Artifacts`、`NotExecuted`、`deprecated` 等。
- Canvas 仍是浅蓝网格主体，但周围大量白色面板、描边按钮、胶囊状态与阴影使主体边界不够强，整体更像嵌入后台页面的一块编辑器，而不是桌面应用本身。

### 1.6 Results：合同诚实，但产品能力与视觉承载不足

Results 当前明确只提供标量、诊断、缺陷摘要和追溯信息，并主动声明“不提供图片、感兴趣区域、对比、导出或重跑”（`ResultsPage.vue:691-694`）。这种合同诚实值得保留，但视觉上仍有两类问题：

- 筛选条件全部铺在一个 Panel 中，原始 ISO 时间输入、诊断码、数据源、工程、结果和分页大小同时出现，像管理后台的查询表单；
- 列表 Panel 与详情 Panel 等权并排，详情中技术字段很多，截图出现长诊断码、Flow Hash、Run ID、Session ID 折行，业务判断与技术追溯没有分层；
- 当前结果只有标量详情时，右侧详情区占用较大空间，但核心结果列表仍不突出；
- 旧版结果的 KPI、趋势、吞吐、图像、证据、对比、导出和实时更新能力尚未形成 Next 承载，不能把当前页面视为旧版追溯工作台的等价替代。

### 1.7 Auth：可靠，但过度暴露后端合同

Setup/Login 使用单个居中表单卡，结构清晰、焦点明确，但在 1600×1000 中留白极大，视觉上像通用管理系统认证页。主说明直接写 `/api/auth/me` 与 token 权威复核，使普通用户先看到实现细节。Auth 不需要做营销页，但应更像 ClearVision 桌面产品的入口，而不是 API 调试说明。

## 2. 旧版能力与新版承载缺口

下表以旧版 `codex初稿` 的真实入口和代码语义为基线，不复制旧版视觉。

| 能力 | 旧版证据 | Next 当前承载 | 判定 | 影响 |
| --- | --- | --- | --- | --- |
| 主导航 | `wwwroot/index.html:79-100`：工程、流程、检测、追溯、监控、AI、设置 | `navigation.ts:9-26`：概览、工程、算子库、检测结果、诊断、关于；工作站受 profile 控制 | 已重定位 + 明确延后 | Flow 被重定位到工程工作区是合理的；检测、AI、设置没有产品入口，不能默认替换旧版。 |
| 全局高频操作 | `index.html:104-163`：最终判定、保存、运行、全局变量 | Save/Formal Run 仅在 Workspace；最终判定和全局变量无可见入口 | 部分优化保留 + 缺失/回归 | 用户进入 Workspace 后仍找不到正式判定配置与全局变量；能力闭环不完整。 |
| 工程列表/搜索/最近 | `index.html:319-408` | `ProjectsPage.vue:264-462` | 已优化保留 | 合同更清晰，但视觉仍像通用数据管理页。 |
| 创建标准/示例工程 | `projectView.js:472-480` | 仅“新建空白工程” | 缺失/回归或需明确延后 | 首次使用和快速试用能力退化。 |
| 工程导入/导出/运行包 | `index.html:366-385`、`app.js:2868-3209` | 产品 Projects 无入口；只在工作站/结果字段中读取运行包名称 | 缺失/回归或需明确延后 | 资产迁移、备份和现场交付链无法从 Next 完成。 |
| Canvas 编辑 | 旧版 canonical FlowCanvas、连线、环路、快捷键、Lint/DryRun | Next 复用 canonical FlowCanvas；添加、拖拽、复制、删除、启停、缩放、连接校验已保留 | 已优化保留 | 架构正确；子图、Lint/DryRun 的可见入口仍需核对或明确延后。 |
| 算子库 | 旧版分组、搜索、拖拽 | Next `OperatorRail.vue` 支持搜索、分类、点击和拖拽 | 已优化保留 | 视觉密度偏卡片化；158 项全部渲染。 |
| 属性检查器 | 旧版 `propertyPanel.js`：参数类型、依赖、绑定、校验、特殊编辑器 | Next 已有节点/连线、参数编辑、校验、端口摘要 | 部分等价保留 | 基础能力已在；全局变量绑定、特殊工作台、中文术语和参数分组深度仍不足。 |
| Preview/Image/ROI | 旧版 `previewPanel.js`、ImageCanvas、ROI editor | Next 已有手动预览、自动预览限制、ImageCanvas、ROI、Artifacts、诊断、stale/error | 已优化保留 | 能力真实，但布局层级、中文和 1080P 空间明显需要精修。 |
| 最终判定 | `finalDecisionPanel.js` | 无产品入口 | 缺失/回归 | 正式运行 OK/NG 事实源不可配置；默认替换前必须解决。 |
| 检测控制 | `index.html:260-317`、`inspectionPanel.js`：单次/连续/停止、保护、统计、进度 | Workspace 只有正式运行/停止；无完整检测工作台 | 明确延后 / 缺失 | Preview、正式运行、现场连续检测之间缺少完整用户路径。 |
| Results/追溯 | 旧版 `index.html:438-737`、`resultPanel.js:24-133`：KPI、趋势、吞吐、实时刷新、图片、证据、对比、导出 | Next 明确只读标量/诊断/缺陷摘要 | 只读接受 + 缺失/回归 | 当前页面可用于合同验证，不是完整追溯工作台。 |
| Station/监控 | 旧版工作站通讯、命令、健康、日志、运行包和结果 | Next 只读页面，默认 profile 隐藏 | 只读接受 + 按 profile 隐藏 | 作为受控 profile 合理；不能代表旧版现场工作台等价迁移。 |
| AI | 旧版规划、Build、Apply/Undo、恢复 | 无 Next 产品入口 | 明确延后 | 不应在视觉优化中私自补合同，但默认替换前必须有迁移决策。 |
| 设置/设备 | 旧版相机、PLC、TCP、Station、系统、用户安全 | 无 Next 产品入口 | 明确延后 | 工程配置工具的完整性尚未达到旧版。 |
| 底部持续上下文 | 旧版用户、就绪/运行、资源/FPS、当前工程、版本 | Next 普通 Shell 无统一状态栏；Workspace 底栏以 Owner 诊断为主 | 缺失/重定位不当 | 用户需要的工程与运行上下文被开发诊断信息替代。 |

结论：当前 Next 可作为**受控工程 pilot**，但不具备“视觉精修后即可默认替换旧版”的功能前提。视觉优化不得掩盖这些缺口，也不得由前端自行新增第二套后端权威。

## 3. 中文体验问题

### 3.1 用户主语言与技术诊断没有分层

以下词直接出现在核心工作界面：

- `Save / Formal Run / Preview`（`ProductLayout.vue:141`）
- `Run / Stop / Reconcile / draft`（`WorkspaceShell.vue:218,234,243,267,275`）
- `ProjectId / PersistenceRevision`（`WorkspaceShell.vue:194-197`）
- `Workspace owner / Inspector / Preview / Image / ROI / Persistence`（`WorkspaceShell.vue:395-407`）
- `Inspector / editable / empty / metadata / draft`（`InspectorPanel.vue:56-83,218-253`）
- `metadata`（`OperatorRail.vue:103`）
- `STALE / Artifacts / Flow draft`（`PreviewPanel.vue:60,171,182`）
- `Station / Run ID / Session ID / Flow Hash / Execution / Decision`（Results/Stations 详情）

建议不是简单删除技术信息，而是建立两层语义：

| 当前 | 中文主标签 | 技术信息放置 |
| --- | --- | --- |
| Run | 正式运行 | tooltip/诊断：Formal Run |
| Stop | 停止运行 | 运行详情可保留 command code |
| Reconcile | 查询运行结果 / 核对保存结果 | 次级说明保留 reconcile |
| draft | 本地草稿 | 诊断区显示 draft revision |
| PersistenceRevision | 服务端修订号 | 详情/冲突信息中显示原字段名 |
| Inspector | 属性检查器 | 开发诊断页可保留 Inspector owner |
| STALE | 预览已过期 | 状态详情显示 stale reason |
| Artifacts | 预览产物 | 列表项保留 MIME、artifact ID |
| metadata | 算子元数据 / 参数定义 | 内部日志保留 metadata |

### 3.2 页面说明在讲实现，而不是讲任务

典型文案包括：

- “通过权威 open/delete command 进入后续任务”
- “列表仅显示稳定摘要字段，不从列表数据推断流程信息”
- “详情统计只来自当前工程的只读详情接口”
- “名称与描述更新复用现有工程保存链”
- “只有服务端 tombstone 成功后才会返回工程列表”

这些文案适合 ADR、诊断页或测试证据，不适合产品首屏。产品文案应优先回答：用户现在能做什么、当前状态意味着什么、下一步是什么。

### 3.3 错误与冲突文案已有基础，但仍需产品化

当前 unknown outcome、revision conflict、delete confirmation 的安全语义总体可靠。下一轮应保留合同含义，同时改成：

> 发生了什么 → 当前影响 → 下一步操作 → 可选技术详情

示例：

- “保存冲突：服务器 revision 5……”应改为“工程已在其他位置更新，本次修改尚未保存。请先载入最新版本，再选择重新应用本地修改或放弃本地修改。”技术详情再显示“服务端修订号 5”。
- “保存响应未知；必须先 GET reconcile”应改为“尚未确认保存是否成功。为避免重复写入，请先查询服务端状态。”

### 3.4 中文字段和结果筛选不够自然

- Results 使用原始 ISO 时间文本框与 `Z` 时区格式，专业但不友好；主输入应符合中文桌面习惯，UTC/ISO 作为次级提示。
- “标准结果”建议统一为“判定结果”；“Execution / Decision”统一为“执行状态 / 判定结果”。
- Projects 列表中的“打开”与“打开工作区”不一致，应统一动作对象。
- 长工程名、设备名、诊断码、Hash 和错误原因需要明确的换行、复制、tooltip 和详情策略，不能依赖自然折行。

## 4. 1080P 空间与布局问题

### 4.1 当前空间预算

普通页面：

- 左侧栏：208px；
- 顶栏：至少 48px；
- compact 页面 padding：20px；comfortable：28px；
- 页面内容最大宽度：1720px。

Workspace：

- 产品窄导航：52px（comfortable 60px）；
- Product topbar：48px；
- Workspace toolbar：44px（comfortable 52px）；
- FlowCanvas toolbar：36px；
- FlowCanvas status：24px；
- Workspace statusbar：24px（comfortable 28px）；
- Operator Rail：196–232px；
- Inspector：280–320px；
- Preview：180–280px；
- Canvas 最小高度：300px。

在 1600×1000、WebView2 force DPR 1.25 的现有证据中，逻辑 viewport 只有 1268×750，Canvas logical size 为 662×300。门禁证明它可点击、可选中，不代表这是高效的常态工作尺寸。

### 4.2 主要问题

1. **固定 chrome 过多**：Canvas 之前和之后有五条横向结构，工程身份、状态和诊断重复。
2. **Preview 永久占用 180–280px**：即使没有图像或只需要状态，也不能收缩为单行状态条。
3. **没有 splitter/collapse**：已有 `CvSplitter` primitive，但实际 Workspace 不使用；用户无法在“搭流程 / 调参数 / 看图像”三种工作模式之间主动分配空间。
4. **窄宽度以隐藏能力解决**：`<1220px` 隐藏 Inspector，`<920px` 隐藏 Operator Rail，没有 dock、抽屉或恢复按钮。
5. **嵌套滚动**：Operator list、Inspector body、Preview details、结构化结果 `pre`、表格、页面都可能滚动。每个轴的滚动 owner 不够清楚。
6. **comfortable 对 Workspace 成本过高**：toolbar、status、product rail 同时增大，工作区没有对应的内容压缩策略。
7. **实际 Windows 125% 尚未验证**：当前证据是 force-scale/Browser DPR；真实系统标题栏、字体栅格、输入法、弹窗和 client size 仍需单独验收。

### 4.3 推荐空间目标

- 1920×1080 / Windows 100%：Canvas 在默认搭流程模式下应至少约 900×480 CSS px；Inspector 与 Preview 同时可达。
- 1920×1080 / Windows 125%：以约 1536×864 桌面有效逻辑尺寸和真实 WebView2 client size复核；Canvas 默认至少约 760×360 CSS px。
- 1350×704 短屏压力：Canvas 不低于 600×300；Preview 默认可收缩为 36–40px 状态条；Inspector 和 Operator Rail 不得无入口消失。
- 每个轴只有一个主要滚动 owner；结构化原始数据应在受控详情内滚动，不推动整个 Workspace。
- Product topbar + Workspace toolbar 应合并或明确分工，避免重复工程身份和重复状态。

## 5. 视觉层级、组件和状态体系问题

### 5.1 `CvPanel` 默认样式制造 SaaS 卡片感

`CvPanel.vue:69-76` 默认同时使用：

- `border: 1px solid`
- `border-radius: 10px`
- `box-shadow: elevation-1`

而 `elevation-1` 又包含 22px 模糊阴影（`tokens.css:93-95`）。这使任何内容一旦放入 `CvPanel` 就自动成为“浮起的卡片”。Overview、Projects、Project Detail、Results 都大量复用该默认值，因此视觉结果高度一致，也高度普通。

目标应把容器分成：

1. **continuous**：连续工作区，仅表面差或单向分隔线；
2. **raised**：真正独立、可操作的局部区域；
3. **floating**：popover、modal、菜单等真实 elevation。

Panel 不应默认同时拥有完整边框与宽软阴影。

### 5.2 页面标题体系重复且偏后台

`CvPageHeader` 支持 `eyebrow + title + description + actions`，Projects、Results、Overview 反复使用“眉题—大标题—说明”。中文产品页不需要每页都先写“工程管理 / 质量追溯 / 工作台”。这会形成标准化后台页面节奏，并额外占用 40–70px。

建议：

- 高频工具页只保留标题、关键状态和主操作；
- 说明文字按需进入空状态、帮助 popover 或次级信息区；
- Workspace 不使用 page hero；
- Auth 说明不展示 API 路径。

### 5.3 状态徽标过多、等权

Workspace toolbar 同时出现“保存合同：兼容”“已保存”“Formal Run: ready”等多个胶囊。它们颜色相近、尺寸相同、位置相同，导致用户必须逐个阅读。

推荐状态层次：

- 一级：当前可执行状态，例如“可正式运行 / 运行中 / 保存冲突 / 结果未知”；
- 二级：未保存、预览过期、只读等局部状态；
- 三级：修订号、owner count、snapshot hash 等诊断信息，仅在诊断抽屉或 tooltip。

状态色必须继续区分品牌、OK、NG、Warning、Info、Idle；但品牌红 `#b6453c` 与 NG 红 `#d12f3f` 视觉距离较近，主操作与危险操作需要通过形状、填充方式、位置和图标进一步分离。

### 5.4 圆角、阴影与间距数值本身尚可，使用密度有问题

tokens 的 3/5/7/10/14px 圆角、4px 间距基线和 Segoe UI/微软雅黑字体栈符合 Quiet Precision。问题在于：

- 几乎所有区域都使用完整容器外观；
- `Panel + Toolbar + DataTable + InlineAlert` 各自再画边界；
- 工具区大量 26–30px 描边按钮形成“控件墙”；
- 相同 gap 在页面、面板、工具栏中反复使用，节奏过于平均。

下一轮不应先“把所有圆角变小”，而应先减少需要圆角和阴影的容器数量。

### 5.5 当前为何像普通 SaaS，而不像工业桌面软件

普通 SaaS 的典型视觉逻辑是：导航到页面 → 看标题和说明 → 操作卡片 → 查看表格。当前 Overview、Projects、Project Detail、Results 基本遵循这一语法。

工业桌面 Studio 应采用：工程上下文持续存在 → Canvas/图像/数据成为主体 → 工具与参数贴近对象 → 状态与错误贴近操作 → 低频说明退出中心区域。当前最接近这一方向的是 Workspace，但仍被通用 Shell、技术诊断词和多个等权子面板拉回后台风格。

## 6. 按严重程度排序的问题清单

### [P0] 默认替换旧版前存在核心任务族回归

类型：功能
证据：旧版 `index.html:79-163`、`projectView.js:472-480`、`app.js:2868-3209`、`resultPanel.js:24-133`；Next `navigation.ts:9-26`、`ResultsPage.vue:691-694`，并且当前产品代码无最终判定、全局变量、工程导入/导出/运行包、AI、设置入口。
影响：若把 Next 作为默认 Studio，用户将无法从同一产品完成旧版已支持的关键任务；视觉变漂亮也会掩盖功能回归。
建议：维持受控 pilot；建立 capability migration decision 表，逐项标记“已保留 / 已重定位 / 只读 / profile 隐藏 / 明确延后 / 缺失”。任何默认入口建议必须先关闭最终判定、全局变量、工程资产迁移、检测控制、Results 深度、AI/设置等阻断项，或由用户明确批准阶段性产品范围。
验证：按旧版完整任务路径做真实 WebView2 走查；不得以路由存在或空状态存在判定能力完整。

### [P1] Workspace 在短屏/125% 下通过隐藏面板而非重排能力

类型：布局 / 交互
证据：`FlowWorkspace.vue:91-97` 在 `<1220px` 隐藏 Inspector、`<920px` 隐藏 Operator Rail；没有 splitter、dock 或恢复入口。
影响：参数编辑或算子添加可能从首屏直接消失；Windows 125% 和非最大化窗口中最明显。
建议：引入单一 Workspace layout owner 下的可调整 splitter、折叠与恢复按钮；保留用户上次布局只作为 UI preference，不成为业务权威。
验证：1920×1080 100%/125%、1600×900、1350×704；折叠/恢复后 Owner 数、订阅和写入口不变。

### [P1] Canvas 没有获得足够视觉与空间主权

类型：布局 / 视觉
证据：Product topbar、Workspace toolbar、Flow toolbar、Flow status、Workspace statusbar 叠加；Preview 固定 180–280px（`FlowWorkspace.vue:91-97`）；force DPR 1.25 证据中的 Canvas logical size 为 662×300。
影响：流程越复杂、中文参数越长，Canvas 越容易退化为中间小窗口；Preview/Inspector 同时拥挤。
建议：合并重复工程身份与状态；Preview 提供“收缩为状态条 / 展开调试”两态；底部只保留用户有价值的工程、保存、预览、运行上下文。
验证：100/300 节点、长中文工程名、长参数、Preview 图像与结果同时存在时截图；Canvas 不被说明文字和诊断状态侵占。

### [P1] Workspace 单字导航造成发现性与辨识回归

类型：交互 / 文案
证据：`ProductLayout.vue:133` 使用 `item.label.slice(0, 1)`；真实 WebView2 截图显示“概/工/算/检/诊/关”。
影响：用户需要记忆每个汉字含义；工程/工作站可能冲突；不像成熟桌面工具的图标栏。
建议：使用统一工业图标 + 完整 tooltip/accessible label；当前任务保持明确选中态；必要时提供可展开 rail，而不是截取首字。
验证：默认 profile、Stations profile、键盘导航、屏幕阅读器名称、125% 窄窗口。

### [P1] F04 尚无真实 Windows 125% 与产品 dark/comfortable 视觉批准证据

类型：布局 / 视觉 / 证据
证据：F04 manifest 为 light + compact；WebView2 1.25 记录 `force-device-scale-factor=1.25` 且 native window DPI=96；F01 的主题/密度矩阵只覆盖 Design Lab。
影响：当前“DPI PASS”证明 Canvas scaling 和命中，不证明真实系统字体、标题栏、输入法、弹窗、client size 与产品页面观感。
建议：下一轮把真实 Windows 100%/125% WebView2、light/dark、compact/comfortable 纳入产品视觉门禁，证据类型分开记录。
验证：系统设置真实切换并重启 Desktop；记录 native DPI、client size、JS DPR、截图像素和页面逻辑尺寸。

### [P1] Results 作为产品级追溯入口时承载不足

类型：功能 / 布局
证据：`ResultsPage.vue:691-694` 明确不提供图片、ROI、对比、导出或重跑；旧版 `resultPanel.js` 已有 comparison/evidence export/live analytics 等合同。
影响：用户看到“检测结果”主导航，会自然期待完整追溯；当前页面更像合同浏览器，容易造成能力已迁移的错觉。
建议：在范围未补齐前明确标记“基础追溯（只读）”，或按 profile 限制；后续以证据图像和诊断任务为中心重构，不复制旧版 KPI 卡片视觉。
验证：OK、NG、执行失败、未判定、无图像、图像已清理、证据可用、无权限、实时断线等状态。

### [P2] 默认 Panel 语法导致卡片套卡片和区域等权

类型：视觉
证据：`CvPanel.vue:69-78` 默认边框 + 圆角 + 宽软阴影；Overview、Project Detail、Results 多次并列复用。Impeccable detector 同时识别了 Alert side-tab 反模式。
影响：页面像标准后台模板，信息越多越碎，用户无法快速识别主要工作区。
建议：新增/调整 continuous、raised、floating 语义；普通分组使用表面差、列对齐和单向分隔线；只有真正独立容器才使用完整外框。
验证：页面缩略图下仍能一眼看出主区域；连续工作区不出现多层描边与阴影。

### [P2] 中文产品界面泄漏开发与合同词

类型：文案
证据：`ProductLayout.vue:141,296`、`WorkspaceShell.vue:194-275,395-407`、`InspectorPanel.vue:56-83,218-253`、`PreviewPanel.vue:60-63,171-182`。
影响：中文用户需要同时理解业务和工程实现；状态在高压调试环境中难以扫读。
建议：中文主标签 + 技术次级详情；Owner 数、revision、hash、contract status 移入诊断抽屉；统一“工程、流程、算子、属性检查器、预览、正式运行、服务端修订号、本地草稿”。
验证：对产品页面做面向用户字符串扫描；核心操作区不得出现无中文主标签的英文术语。

### [P2] Overview 与 Project Detail 的任务层级错误

类型：布局 / 视觉 / 文案
证据：Overview 把服务、会话、最近工程、快速入口等权卡片化；Project Detail 使用四个同外观 Panel。
影响：首屏不能突出“继续工程 / 打开工作区 / 工程是否可运行”；大量留白仍显拥挤，因为层级靠边框而非任务建立。
建议：Overview 以最近/当前工程和系统就绪摘要为主；Project Detail 以工程身份 + 打开工作区为主，编辑字段与资源/流程摘要改为连续信息区。
验证：空、1 个、长列表、长中文工程名、冲突和无权限场景。

### [P2] 状态胶囊、主操作和危险操作缺乏足够区分

类型：状态 / 视觉
证据：Workspace toolbar 同时出现多个相同形态 badge；品牌红与 NG 红接近；Projects/Project Detail 的 primary 与 delete 同屏均偏红。
影响：用户需要阅读文字才能区分普通主操作、系统正常状态与危险操作。
建议：一级状态不超过一个；危险操作保持红色但不与品牌实心主按钮并列抢占；主操作可通过位置、填充、图标和文案建立区分。
验证：色盲模拟、灰度截图、浅/深色、禁用/加载/错误状态。

### [P2] Results 筛选与技术详情占据过多首屏

类型：布局 / 文案
证据：`ResultsPage.vue:478-533` 同时铺开 7 类筛选；详情显示长 Hash/ID；截图右侧详情滚动、主列表只有一行仍占较小区域。
影响：1080P 下结果判断不够直接；原始 ISO 时间和技术字段增加认知负担。
建议：高频筛选常驻，低频诊断码/精确时间折叠为“高级筛选”；详情先显示结果、执行、判定、耗时和缺陷，再显示追溯 ID。
验证：20/50 行、长诊断码、长项目名、空结果、多个缺陷、分页恢复。

### [P2] Web Interface Guidelines 静态问题

类型：交互 / 可访问性 / 性能
证据：

- `FlowCanvasSurface.vue:50-67,115-138`：符号型图标按钮只有 `title`，缺少明确 `aria-label`；
- `OperatorRail.vue:169-174`：`button` 被覆盖为 `role="listitem"`，破坏原生按钮语义；
- `OperatorRail.vue:169`：158 项列表全部渲染，未虚拟化；
- `ImageViewport.vue:79`：`aria-label="Preview ImageCanvas"` 未中文化；Canvas 无 `tabindex`；
- `ImageViewport.vue:127`：引用不存在的 `--cv-color-focus-ring`，实际 token 为 `--cv-focus-ring-color`；
- `product-layout.css:286-290`：程序化聚焦主内容后无可见焦点提示；
- `CvInlineAlert.vue:82-91`：完整边框叠加 2px 左侧状态条，属于视觉反模式。

影响：键盘/辅助技术用户难以识别命令与焦点；长算子目录可能产生不必要渲染成本。
建议：保留原生语义、补充中文 accessible name、修复焦点 token、评估虚拟化或 `content-visibility`；视觉优化最后用最新 Web Interface Guidelines 复核。
验证：Tab/Shift+Tab、屏幕阅读器名称、焦点截图、158/300 项目录、reduced motion。

### [P3] 微观精修尚未形成统一节奏

类型：视觉
证据：状态点、胶囊、输入框、按钮、分隔线、卡片阴影、短色条和不同字重同时存在；部分标题与字段基线不齐，长英文/ID 折行生硬。
影响：不阻塞任务，但削弱“精密”和“可信”的整体感。
建议：在 P0–P2 关闭后，再统一图标线宽、基线、数字字宽、滚动条、hover/focus、tooltip、空状态和轻量状态动效。
验证：100% 与 125% 像素级截图对比，浅/深色与 compact/comfortable 全覆盖。

## 7. 推荐的目标视觉方向

### 7.1 物理使用场景

视觉工程师在工厂办公室或控制室的 1920×1080 Windows 显示器上，长时间进行工程配置、图像调试和运行排障；环境光中等，操作频繁，任何装饰都必须让位于 Canvas、图像、参数和状态。

### 7.2 Quiet Precision 目标语法

1. **Canvas / 图像 / 数据优先**：工作对象本身是视觉主角，外围 chrome 保持安静。
2. **连续表面优先**：Shell、Canvas、Inspector、Preview 通过 graphite 表面差与单向分隔线组织，不把每块内容做成卡片。
3. **一组一个主操作**：Workspace 顶部只突出“保存”或“正式运行”中的当前主操作；停止、冲突核对、危险操作按状态出现。
4. **状态真实但不喧闹**：一级状态一个，局部状态贴近对象；颜色、中文标签和图标共同表达。
5. **中文主语义**：技术字段作为次级诊断，不进入主按钮、主标题和普通状态。
6. **密集但可扫读**：列对齐、数字等宽、单位靠近值、字段组有稳定节奏；减少说明段落。
7. **桌面原生感**：系统字体、清晰焦点、克制动效、合理右键/快捷键提示；不模仿 macOS 窗口控件，不使用消费级大胶囊。
8. **浅色与深色都服务生产环境**：浅色 graphite 作为明亮环境主方案，深色用于低光或图像调试；两者均保持品牌与状态色分离。

### 7.3 目标页面结构

- **Shell**：可展开的窄导航 rail；工作区模式只保留任务导航、当前工程、系统关键状态和会话入口。
- **Overview**：当前/最近工程为主；系统就绪摘要为次；不重复侧栏导航。
- **Projects**：工具条 + 连续表格为主；最近工程作为筛选/分组，而不是独立大卡。
- **Project Detail**：工程身份和“打开工作区”为主；编辑、流程、资源采用连续分组或双栏描述区。
- **Workspace**：Operator Rail / Canvas / Inspector 三栏可调；Preview 可折叠；Canvas 默认获得最大面积。
- **Results**：结果列表和证据详情为主；高频筛选常驻，高级筛选折叠；技术追溯次级显示。
- **Auth**：保留简单单卡结构，但减少 API 文案和无效留白，强化 ClearVision 产品身份与安全说明。

## 8. 分阶段改进计划

### 阶段 V0：冻结视觉合同与功能承载边界

目标：在写 UI 前明确哪些是视觉重构，哪些是 capability 缺口，防止视觉实现者自行扩权。

- 建立旧版能力对照矩阵的批准版本；
- 确认 Next pilot 的可见导航和明确延后项；
- 定义 Workspace 空间预算、状态主次、中文词表和截图矩阵；
- 明确真实 Windows 125% 证据方法；
- 不改 API、保存链、Canvas 内核、Owner 或 feature flag 语义。

### 阶段 V1：Workspace Shell / Chrome 试点

目标：先解决最能决定产品气质的区域，不触碰 Canvas 内核和业务合同。

- Product workspace rail：图标 + tooltip + 可展开，不使用首字截取；
- 合并/重排 Product topbar 与 Workspace toolbar 的重复工程身份；
- 状态分为一级业务状态与次级诊断；
- 清理底栏 Owner 诊断，改为工程/保存/预览/运行/缩放等用户上下文；
- 引入可恢复的 rail/inspector/preview 折叠入口；
- 建立 1080P/125% 默认空间预算。

### 阶段 V2：Canvas / Operator Rail / Preview / Inspector 精修

目标：让工作对象成为主角，并完成中文与局部层级。

- Operator Rail 从“列表卡片”收敛为高密度工具目录；
- Flow toolbar 分组、图标、快捷键和危险操作统一；
- Inspector 改为“属性检查器”，参数分组、端口、校验、状态分层；
- Preview 建立收缩/展开、图像/结构化结果/ROI/诊断的层级；
- 技术词中文化，诊断 ID 进入次级区；
- 补焦点语义、aria-label 和大列表性能。

> 架构约束：FlowCanvas + Property/Inspector + Preview 不得拆成多个并行实现 Owner。该阶段应由同一实现 Owner 顺序完成，避免共享状态与布局权威分裂。

### 阶段 V3：共享 primitives 与普通页面迁移

目标：把 Workspace 试点确认的视觉语法抽成最小共享能力。

- `CvPanel/CvSurface` 增加 continuous/raised/floating 映射；
- `CvPageHeader` 增加 compact/tool-page 变体；
- `CvStatusBadge` 增加一级状态、局部状态和技术 badge 的层级；
- 统一 button、danger、inline alert、table、description list；
- 迁移 Overview、Projects、Project Detail；
- 不一次性全局改 token，先用已批准页面验证影响。

### 阶段 V4：Results / Auth / 只读页面精修

目标：形成完整产品一致性，并对 capability 缺口保持诚实。

- Results 高频筛选、列表、详情、技术追溯重排；
- 在未补能力前明确“基础追溯（只读）”范围；
- Auth 去除 API 主文案，优化桌面比例；
- Operators、Stations、Diagnostics、About 按共享语法迁移；
- Stations 继续遵守 profile 和只读边界。

### 阶段 V5：真实环境视觉验收与回归

目标：建立产品视觉门禁，不把自动工程 PASS 当作审美批准。

- Browser fixture：多状态、长中文、无障碍、overflow；
- 真实 WebView2：1920×1080 Windows 100%/125%；
- light/dark × compact/comfortable；
- 1350×704 压力；
- 功能路径、Owner/订阅/请求/保存/运行回归；
- 用户产品视觉确认后，才讨论 pilot 扩大或默认入口。

## 9. 每阶段涉及页面、组件和风险

| 阶段 | 页面/区域 | 主要组件 | 主要风险 | 控制方式 |
| --- | --- | --- | --- | --- |
| V0 | 全局 | docs、visual contract | 把功能缺口误当纯视觉问题 | 先批准 capability 矩阵；契约缺口只报告。 |
| V1 | Product Shell、Workspace Shell | `ProductLayout`、`WorkspaceShell`、navigation、status | 破坏路由、Leave Guard、Owner mount 边界 | 只改视觉组合和 UI preference；保留唯一 composition root 与 Owner。 |
| V2 | Operator Rail、Canvas、Preview、Image/ROI、Inspector | `FlowWorkspace`、`FlowCanvasSurface`、`OperatorRail`、`PreviewPanel`、`ImageViewport`、`InspectorPanel` | Canvas 尺寸、pointer、快捷键、ROI 同步、滚动 owner 回归 | 单一实现 Owner；逐状态截图；Canvas/ROI/WebView2 定向回归。 |
| V3 | Overview、Projects、Project Detail | `CvPanel`、`CvSurface`、`CvPageHeader`、`CvStatusBadge`、table/form | primitive 改动波及所有页面和 Lab | 先新增变体，后迁移；避免一次性修改默认语义。 |
| V4 | Results、Auth、Operators、Stations、Diagnostics、About | filter toolbar、detail layout、auth shell | 视觉暗示超出真实合同；profile 页面被误认为可写 | 文案明确范围；不新增未批准命令或后端接口。 |
| V5 | 全产品 | Playwright、WebView2 runner、截图索引 | 把 DPR 模拟冒充系统 DPI；只测空状态 | 分层记录 Browser/WebView2/System DPI；覆盖真实数据与错误状态。 |

## 10. 截图验收标准

### 10.1 必须覆盖的环境

1. 真实 WebView2，1920×1080，Windows 100%。
2. 真实 WebView2，1920×1080，Windows 125% 系统缩放；记录 native DPI、client size、JS DPR 和截图像素。
3. Browser fixture，1366×768、1600×1000、1920×1080；仅作为分层证据。
4. 1350×704 或相近短屏压力。
5. light/dark × compact/comfortable 产品页面，不以 Design Lab 替代。
6. `prefers-reduced-motion` 与显式 reduced motion。

### 10.2 Shell / 普通页面

- 当前导航、当前工程、用户和系统关键状态可辨识；无首字歧义。
- 普通页面无全局水平滚动；长工程名和错误原因不遮挡操作。
- 标题区不形成大 hero；首屏主操作与主内容同时可见。
- Overview 有/无最近工程均不出现大面积无意义卡片留白。
- Projects 20 行数据时表格仍是主体；最近工程不挤压主列表。
- Project Detail 的“打开工作区”是清晰主操作，删除保持危险但不抢占。

### 10.3 Workspace

- 1920×1080 100% 默认 Canvas 约不小于 900×480 CSS px；125% 约不小于 760×360。
- 1350×704 下 Canvas 不低于 600×300；Preview 可收缩为 36–40px 状态条。
- Operator Rail、Inspector、Preview 任一折叠后都有明确恢复入口；不以 `display:none` 永久丢能力。
- rail/center/inspector 与 canvas/preview 可调，且布局切换不改变 Owner 数、订阅数和写入口。
- 保存、正式运行/停止、当前结果、未保存、冲突、结果未知均在首屏可达。
- Preview、正式运行、历史结果在标题、状态和颜色上严格区分。
- 结构化结果、Artifacts、诊断长内容只在自己的受控滚动区内滚动；无三层同轴滚动。
- 长中文参数名、必填、依赖、只读、错误和单位可读；当前错误不被折叠到不可见区域。
- 所有核心用户文案以中文为主；技术词只在次级详情出现。

### 10.4 状态场景

至少截图并人工复审：

- loading、empty、ready、readonly、forbidden、offline；
- dirty、saving、saved、save error、save conflict、save unknown outcome；
- preview idle/loading/success/stale/blocked/error/no-image；
- ROI editing/undo/redo/cancel/confirm/readonly；
- formal run ready/running/stopping/succeeded/failed/unknown outcome；
- OK、NG、执行失败、未判定、无缺陷、长诊断码；
- 401 会话失效、Leave Guard、删除确认、长中文错误。

### 10.5 视觉判定标准

- 缩略图观看时能立即识别页面主区域，不是所有 Panel 等权。
- 连续工作区最多一个外轮廓；内部主要依靠表面差和单向分隔线。
- 普通 Panel 不同时使用完整边框和宽软阴影。
- 每个局部操作组只有一个明确主操作。
- 品牌、OK、NG、Warning、Info、Idle 在灰度和色盲模拟下仍能通过文字/图标/结构区分。
- 圆角、阴影只表达真实容器与 elevation；无玻璃、发光、装饰性渐变和无意义动画。
- 100%/125% 下中文字体、数字、单位和 1px 分隔线清晰，不因缩放产生拥挤或模糊。

## 11. 下一步推荐

### 11.1 应先优化的页面/区域

**优先试点：Project Workspace 的 Shell/Chrome 与空间布局，不先改 Canvas 内核。**

具体包括：workspace 导航 rail、Product topbar、工程上下文、保存/正式运行状态、底部状态栏、Operator/Canvas/Inspector 三栏比例、Preview 收缩/展开和恢复入口。

### 11.2 为什么适合作为试点

1. Workspace 是 Studio 与普通 SaaS 后台最本质的差异所在，最能验证 Quiet Precision 是否成立。
2. 现有 Owner、保存、运行、Canvas、Preview、ROI 合同已经工程闭环，视觉重构有真实功能可承载。
3. 它同时暴露 tokens、primitives、中文、状态、1080P、滚动和可访问性问题，试点结论可反向指导普通页面。
4. 先改外层 chrome 和布局，可避免一开始触碰 Canvas 内核和后端权威。
5. 该区域风险高但边界清晰，必须由单一实现 Owner 顺序完成，符合根 `AGENTS.md`。

### 11.3 建议拆成 5 个 Codex Prompt

1. **Prompt 1｜Workspace 视觉合同与空间预算冻结**
   只读核对 Owner、DOM、滚动、断点、状态和真实 WebView2 尺寸；输出批准后的 layout/state/copy contract，不改代码。
2. **Prompt 2｜Workspace Shell/Chrome 试点实现**
   只改 Product workspace rail、topbar、workspace toolbar、statusbar、布局折叠/恢复；不改 Canvas 内核、保存/运行合同和 API。
3. **Prompt 3｜Operator Rail + Canvas 工具区 + Inspector/Preview 精修**
   同一实现 Owner 顺序完成中文、层级、splitter/折叠、焦点与滚动；不得拆并行 Owner。
4. **Prompt 4｜共享视觉语法抽取并迁移 Overview/Projects/Project Detail/Results**
   在试点获得用户方向确认后，抽取 continuous/raised/floating、compact header、status hierarchy；逐页迁移。
5. **Prompt 5｜真实 WebView2 100%/125% 与产品视觉 Final Evidence**
   覆盖 light/dark、compact/comfortable、1350×704、长中文和关键状态；重跑功能/Owner/Canvas/Save/Run 回归并生成新截图索引。

### 11.4 可共享到其他页面的改动

- continuous / raised / floating 表面语义；
- compact tool-page header；
- 一级业务状态与技术 badge 分层；
- 品牌主操作、普通操作和危险操作的统一规则；
- 中文状态/合同词映射；
- 长中文、ID、诊断码的截断/换行/复制/tooltip 规则；
- splitter、折叠/恢复、短屏布局模式；
- focus-visible、icon accessible name、reduced motion；
- 视觉截图矩阵与验收脚本。

### 11.5 必须等待用户视觉确认的问题

以下属于产品审美与工作方式选择，不能由实现代理单方面决定：

1. 浅色 graphite 的冷暖与整体明度；
2. 品牌砖红是否继续作为实心主操作，以及与危险红的视觉距离；
3. Workspace 导航采用纯图标 rail、图标+短标签，还是默认可展开 rail；
4. Preview 默认位于底部还是右侧 dock，以及默认展开高度；
5. compact/comfortable 的默认密度；
6. Canvas 网格强度、节点表面和选中态的最终视觉；
7. 普通页面是否完全取消卡片阴影，还是保留极弱 raised surface；
8. 深色主题是否面向日常使用还是主要用于图像调试/低光环境。

以下不应等待审美确认，应直接作为质量问题关闭：

- 单字导航歧义；
- 核心操作英文泄漏；
- 面板隐藏后无恢复入口；
- 错误 ARIA role、缺 aria-label、焦点 token 错误；
- 真实 Windows 125% 产品视觉证据缺失；
- 把 Owner/revision/hash 等开发诊断长期显示在普通用户主界面；
- 把阶段性只读/延后能力误称为旧版等价迁移。

## 12. 正向发现：应继续保留

- Design tokens 已区分品牌、OK、NG、Warning、Info、Idle；文字对比度抽查均达到 WCAG AA：light secondary 约 6.92:1、light muted 约 5.19:1、dark secondary 约 9.89:1。
- 字体栈适合 Windows 与简体中文，字号尺度克制，没有营销式巨型标题。
- 圆角 token 本身合理，没有 24–40px 过度圆角。
- skip link、表单 label、Modal focus trap、Escape、焦点恢复、reduced motion、Intl 日期格式和 tabular numerals 基础良好。
- Canonical FlowCanvas、ImageCanvas、Preview、ROI、Persistence、Formal Run 和 Leave Guard 的单一 Owner/权威边界应保持不动。
- unknown outcome、conflict、readonly、stale、preview/formal-run 区分已经具备真实合同基础；下一轮应做产品化表达，而不是简化掉状态。

## 13. Web Interface Guidelines 文件级静态发现

```text
ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/flow/FlowCanvasSurface.vue:50 - 符号型图标按钮缺少明确 aria-label
ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/flow/FlowCanvasSurface.vue:115 - 缩放图标按钮缺少明确 aria-label
ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/flow/OperatorRail.vue:169 - button 被 role="listitem" 覆盖，破坏原生按钮语义
ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/flow/OperatorRail.vue:169 - 158 项列表未虚拟化或使用 content-visibility
ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/image/ImageViewport.vue:79 - Canvas accessible name 未中文化且元素不可键盘聚焦
ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/image/ImageViewport.vue:127 - 使用不存在的 --cv-color-focus-ring token
ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/app/layouts/product-layout.css:286 - 程序化主内容焦点无可见提示
ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/design-system/primitives/CvInlineAlert.vue:82 - 完整边框叠加 2px 左侧状态条，视觉噪声偏高
ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/design-system/primitives/CvPanel.vue:72 - 默认边框与 22px 模糊阴影同时使用，形成 ghost-card 语法
```

---

最终建议：**不要先从 Overview 换颜色或统一圆角开始。先以 Workspace Shell/Chrome 为试点，解决空间、主次、中文、状态和连续表面；用户确认方向后，再抽取共享 primitives 并迁移普通页面。**
