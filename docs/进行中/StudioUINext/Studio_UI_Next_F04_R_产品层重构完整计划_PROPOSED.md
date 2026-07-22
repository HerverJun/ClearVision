# Studio UI Next F04-R 产品层重构完整计划（PROPOSED）

> 文档性质：基于 F04.2 严肃对标审计形成的纠偏计划。
> 当前状态：**IN PROGRESS / G3 ENGINEERING CANDIDATE DONE**。
> 本文不改写 F04、F04.1、F04.2 或 F05 的历史状态，也不授权默认入口切换。

---

## 0. 权威状态与决策口径

```text
PLAN_NAME=Studio UI Next F04-R
PLAN_THEME=保留新架构，重做产品层
PLAN_STATUS=IN_PROGRESS
PARENT_STAGE=F04

REWORK_DECISION=APPROVED_IN_PRINCIPLE
AUDIT_STATE=DONE
AUDIT_RECOMMENDATION=REWORK

NEXT_AUDIT_BASE_SHA=56fbf18fcb59f91e9d63666c08e302db92ff692c
LEGACY_AUDIT_WORKTREE_SHA=4386d8f3537e80084802567b41d96414b0ddacd0
LEGACY_REMOTE_SHA=bea404394ac8cf403cca719c1990c426414a06c2
ACTUAL_IMPLEMENTATION_BASE_SHA=56fbf18fcb59f91e9d63666c08e302db92ff692c
F04R_IMPLEMENTATION_BASE_STATE=G3_ENGINEERING_CANDIDATE
G3_IMPLEMENTATION_BASE_SHA=b0db2615e7bc15fc313416ff62ee65bd5121ec19

CURRENT_NEXT_VISUAL_CANDIDATE=REJECTED
CURRENT_NEXT_ARCHITECTURE=RETAIN
LEGACY_PRODUCT_SEMANTICS=MANDATORY_BASELINE
LEGACY_SOURCE_COPY=FORBIDDEN
THIRD_PARALLEL_UI=FORBIDDEN

PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER
OFFICIAL_STUDIO_UI_DEFAULT=false
OFFICIAL_WORKSPACE_DEFAULT=false
LEGACY_RETIREMENT=NOT_APPROVED
F05_STARTED=NO
F05_ENTRY=BLOCKED_UNTIL_F04R_EXIT

G0_STATUS=DONE
G1_AUDIT_STATE=DONE
G1_PROPOSAL_STATE=APPROVED
G1_STATUS=DONE
G2_ENTRY=APPROVED
G2_STATUS=DONE
GOLDEN_JOURNEY_TASK_SPEC=FROZEN
LEGACY_SEMANTIC_BASELINE=FROZEN
SPECIAL_EDITOR_DECISIONS=FROZEN
GLOBAL_VARIABLES_CONTRACT=FROZEN
FINAL_DECISION_CONTRACT=FROZEN
RUN_RESULT_EVIDENCE_CONTRACT=FROZEN
RUNTIME_PACKAGE_CONTRACT=FROZEN
A_B_ACCEPTANCE_MATRIX=FROZEN
STABLE_CAMERA_SEMANTIC_SYNC=PASS
BLOCKS_G3_IMPLEMENTATION=NO
BACKEND_POLICY_HARDENING=PASS
G3_ENTRY=APPROVED_AND_EXECUTED
G3_ENGINEERING_STATE=DONE
GOLDEN_JOURNEY_FUNCTIONAL_PARITY=PASS
BROWSER_GOLDEN_JOURNEY=PASS
PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER
REAL_WEBVIEW2_FINAL_GATE=NOT_YET_RUN
WINDOWS_125_DPI=NOT_YET_RUN
REAL_CAMERA=NOT_PERFORMED
F04R_STATUS=IN_PROGRESS
F05_ENTRY=BLOCKED
```

### 0.1 结论

F04-R 不再把当前问题定义为“继续优化视觉”，而定义为：

> 新版已经形成值得保留的工程架构，但产品层迁移策略失真；必须以旧版真实任务、信息密度和完整操作闭环为最低基线，重做导航、页面结构与能力承载方式。

F04-R 的目标不是完成全部旧版能力迁移，也不是继续扩建一批页面。它必须先证明：

1. 新架构能够承载一个不弱于旧版的完整核心旅程；
2. 产品信息架构、功能入口和视觉标准已经纠偏；
3. 后续 F05 可以在同一套规则下继续迁移，而不会再次形成“工程先进、产品退步”。

### 0.2 不改写既有历史

- F04 的工程实现、Remote CI 和 Final Gate 仍是历史完成事实。
- F04.1 的 Shell、Design System 和页面精修提交仍是有效代码历史，但其产品视觉结果已被用户拒绝。
- F04.2 是只读审计结论，不是实现阶段。
- F04-R 是纠偏阶段，不把 F04.1 伪造为 PASS，也不直接把 F05 标记为开始。

### 0.3 G0/G1 当前执行事实

- G0 已在 2026-07-20 完成 Git、flags、stable-line、retained assets、shared owner 和 blocker 冻结，详见 [G0 基线冻结](./F04R_G0_进入治理与实施基线冻结.md)。
- `origin/codex初稿` 的相机单帧 Preview、图像 MIME、captured-frame/source identity 与 Preview/Image/Inspection lifecycle 已按 hunk 同步；Modbus SlaveId、`tmp/pdfs`、无关 lockfile 与无关 Legacy 视觉改动未纳入。
- G1 的产品域、导航、route/role/profile/owner 与黄金旅程范围已按本轮批准决策冻结；G1 历史提案仍保留为输入，不被改写。
- G2 已冻结真实黄金旅程、Legacy A/B、相机同步范围、GlobalVariables、FinalDecision、Run/Result/Evidence、Runtime Package 与权限 hardening 合同；G3 实现没有重新发散这些产品决策。
- Prompt 3 已完成 stable semantic sync、backend policy hardening 与黄金旅程工程候选，详见 [G3 工程候选与证据](./F04R_G3_黄金旅程工程候选与证据.md)。F04-R 仍等待产品视觉确认、真实 WebView2 与 Windows 125% DPI，不进入 F05。

---

# 1. 问题定义与成功标准

## 1.1 当前主要矛盾

当前新版的主要问题不是 Vue 或 TypeScript 不可用，而是：

- 用一张流程工作台参考图向全产品外推设计；
- 在真实能力尚未迁移时先统一了壳层和页面表面；
- 用禁用入口、只读页面或诊断页替代原本完整的产品域；
- 过度依赖测试通过、无溢出和组件规范作为产品质量证明；
- 未把旧版多年形成的高频操作、功能密度和异常闭环当作最低基线。

结果是新版在保存、Preview/ROI、结果排障、只读监控和生命周期治理上局部更好，但整体仍不能替代旧版。

## 1.2 F04-R 成功定义

F04-R 只有同时满足以下条件才可完成：

```text
ARCHITECTURE_RETAINED=YES
THIRD_UI_CREATED=NO
GOLDEN_JOURNEY_FUNCTIONAL_PARITY=PASS
GOLDEN_JOURNEY_TASK_EFFICIENCY=PASS
GOLDEN_JOURNEY_VISUAL_NOT_WORSE_THAN_LEGACY=PASS
REAL_WEBVIEW2_EVIDENCE=PASS
WINDOWS_125_DPI_EVIDENCE=PASS
PRODUCT_OWNER_VISUAL_CONFIRMATION=PASS
F05_INPUT_PLAN=READY_FOR_APPROVAL
```

其中“视觉不弱于旧版”必须由产品负责人根据真实截图和真实操作确认，自动化门禁不能代替。

## 1.3 黄金旅程

F04-R 只选择一条完整核心旅程作为重构样板：

```text
登录
→ 浏览/创建/导入工程
→ 打开流程工作区
→ 添加算子
→ 配置基础参数和至少一种特殊参数
→ 使用全局变量
→ 配置最终判定
→ 节点预览与 ROI
→ 正式保存
→ 单次正式运行 / 停止 / 未知结果恢复
→ 查看对应结果与诊断详情
→ 导出或保存可交付工程资产
```

这条旅程必须使用真实后端合同、真实 owner 和真实页面，不得用静态原型、模拟按钮或另建私有数据链完成。

---

# 2. 必须保留、允许重做与禁止复制的资产

## 2.1 必须保留的架构资产

以下资产默认为 `RETAIN_AND_HARDEN`，不得因重做页面而另建平行实现：

- Vue 3、严格 TypeScript、Vite 与现有构建链；
- Product Shell 的唯一 composition root；
- Auth lifecycle owner、route guard 与 session 失效闭环；
- ProductRuntime、query owner、workspace owner；
- 唯一 HTTP transport、错误映射和后端合同 decode；
- Project Application Service 与 `ProjectSaveCoordinator`；
- `PersistenceRevision`、unknown outcome reconcile 与 Leave Guard；
- canonical `FlowCanvas` / `ImageCanvas` 及窄 adapter；
- Preview、ROI、Formal Run、Stop、Reconcile 的现有 owner；
- lifecycle ledger、AbortController、timer、订阅、blob/artifact 释放治理；
- Design System 的 token、主题、密度、可访问性 primitive 与测试基础；
- 现有 Browser、Playwright、WebView2 runner 和证据工具。

若发现其中某项存在缺陷，应在原 owner 内修复；不得以“重构”为由新增第二套。

## 2.2 允许重做的产品层

以下内容属于 `DISPOSABLE_PRODUCT_COMPOSITION`：

- 当前顶层导航分组、入口名称和“更多”菜单结构；
- “设置”指向“诊断”等不等价导航；
- 当前 Overview、Projects、Results、Stations 等页面的版式和组合方式；
- 目前的 Panel、侧栏、表格与详情比例；
- 被用户拒绝的页面视觉完成度；
- 未迁移能力的禁用占位表达；
- 过度消费级的品牌字标或不适合工业工具的装饰；
- 页面级硬编码样式、临时技术文案和 fixture 英文；
- 当前未证明优于旧版的信息架构决定。

允许重做不等于全部推翻。每项必须先证明其妨碍真实任务，再修改。

## 2.3 旧版的使用方式

旧版承担两种角色：

```text
LEGACY_PRODUCT_SEMANTICS=AUTHORITATIVE_REFERENCE
LEGACY_VISUAL_IMPLEMENTATION=REFERENCE_ONLY
```

必须吸收：

- 高频任务入口；
- 页面和工作区信息密度；
- 真实功能闭环；
- 设置页“当前页保存”等成熟操作语义；
- 特殊参数编辑、最终判定、全局变量、检测、Station 和导出能力；
- 中文术语、错误恢复和现场工程师习惯。

禁止：

- 把旧版大型 JS、CSS、HTML 或 DOM 操作直接复制进 Vue；
- 在 Vue 旁边挂载旧 owner 形成双轨；
- 以 iframe、隐藏 DOM 或 WebMessage 绕过现有 HTTP 权威；
- 为追求像素相似而恢复旧版架构债务。

---

# 3. 强制架构与协作红线

1. 同一 capability 同时只能有一个 mounted owner、一个订阅集合和一个写入口。
2. 不新增第二 HTTP client、EventBus、ServiceRegistry、Canvas、HostBridge、Project save chain 或前端业务权威。
3. Project、Flow、GlobalVariables 和正式资产继续进入现有 Application Service 与 `ProjectSaveCoordinator`。
4. 正式运行、检测、结果、Runtime Package 和 Station 继续使用既有后端与现场链路。
5. Feature Flag 关闭时必须真实 unmount/dispose；隐藏 DOM 不算关闭。
6. 稳定线能力只通过 Git 与语义审计单向同步，不手工复制工作区文件。
7. 不修改正式默认 flags，不在 F04-R 中退役 Legacy。
8. 当前 `appsettings.json` 的用户本地修改不得纳入提交，也不得当作正式默认值证据。
9. 子代理最多 3 个；允许并行只读审计或无文件重叠的叶子组件，Shell、Router、Tokens、API contracts、Workspace、保存链由主协调者单轨处理。
10. 禁止使用 computer-use 操作用户屏幕；截图使用仓库 runner、浏览器脚本或 WebView2 harness。
11. 不以 Browser fixture 代替真实 WebView2、Windows DPI、真实端点或现场设备证据。
12. 任何页面“更好”的结论必须同时有任务证据、代码证据和视觉证据。

---

# 4. F04-R 范围与非目标

## 4.1 本阶段范围

F04-R 只完成以下纠偏：

- 冻结真实产品域、导航、角色、profile、owner 和 capability map；
- 修正不等价入口和误导性产品结构；
- 建立旧版到新版的黄金旅程逐步骤映射；
- 补齐黄金旅程必须依赖的产品能力；
- 重做黄金旅程涉及的页面组合和视觉层级；
- 建立逐页、逐任务的新旧 A/B 门禁；
- 在真实 WebView2 和 Windows 125% DPI 下验证黄金旅程；
- 输出后续 F05 的能力迁移顺序、依赖和门禁。

## 4.2 本阶段必须纳入黄金旅程的能力

- 工程创建、打开、删除及必要的导入/导出闭环；
- 算子添加、基础参数和至少一种文件/相机类特殊编辑器；
- 全局变量管理及参数绑定的最小完整闭环；
- 最终判定配置；
- Preview、图像工具、ROI；
- Save、409、unknown outcome 和离开保护；
- Formal Run、Stop、Reconcile；
- Results 列表与诊断详情；
- 与上述任务直接相关的权限、错误、空态和恢复。

## 4.3 明确延后到 F05 重新规划的领域

以下领域必须在 F04-R 中冻结迁移顺序和合同边界，但不要求全部实现：

- 连续检测工作台与生产保护；
- 结果分析、趋势、实时订阅和完整导出；
- PLC、TCP、Station、存储、数据库、运行时、相机、AI、用户等完整设置域；
- Station 日志、命令、运行包下发、审计和结果导出；
- AI 澄清、计划、Build、Apply、Undo 和恢复；
- 全部标定/测量类特殊工作台；
- Legacy 正式退役和默认入口切换。

## 4.4 非目标

- 算法、算子科学性或 Runtime 执行内核重构；
- 重造 Inspection、Station 或 Agent 后端状态机；
- 相机、PLC、机器人或现场硬件联调；
- 为视觉效果引入大型新 UI 框架；
- 制作与真实产品脱节的设计稿集；
- 一次性重做所有页面；
- 在产品负责人批准黄金旅程前扩散到剩余产品域。

---

# 5. 总体执行顺序

严格串行：

```text
G0 进入治理与真实基线冻结
→ G1 产品信息架构、Capability、Route、Role、Profile 与 Owner 合同冻结
→ G2 黄金旅程产品合同与旧版语义回吸
→ G3 黄金旅程真实实现与页面重构
→ G4 新旧 A/B、真实 WebView2、DPI 与产品视觉确认
→ G5 F04-R 收口与 F05 重规划输入
```

任一 Goal 未通过自己的门禁，不得进入下一 Goal。
不得把 G1/G2 的文档结论与 G3 实现并行，以免实现反向绑架产品决策。

---

# 6. G0：进入治理与真实基线冻结

## 6.1 目标

建立唯一、可追溯的 F04-R 实施起点，避免在旧报告、用户本地配置或已漂移稳定线之上直接施工。

## 6.2 必做事项

- `git fetch origin --prune`；
- 记录两个 worktree 的 branch、HEAD、upstream、remote、ahead/behind 和工作树状态；
- 审计 `origin/codex初稿` 相对 F04.2 旧版审计 SHA 的新增提交；
- 审计 `studio-ui-next` 相对 `56fbf18f...` 的新增提交；
- 分类稳定线差异：
  - `MUST_SYNC_BEFORE_F04R`
  - `ALREADY_EQUIVALENT`
  - `DEFER_WITH_REASON`
  - `CONFLICT_REQUIRES_DECISION`
  - `OUT_OF_SCOPE`
- 记录正式默认 flags、用户本地 override、Browser fixture flags 和 WebView2 runner flags；
- 冻结必须保留的 owners、contracts、adapters、tests 和 shared files；
- 建立 F04-R blocker registry；
- 将 F04.2 审计报告纳入受控文档链，但不得改写其结论。

## 6.3 交付物

- `F04R_G0_进入治理与实施基线冻结.md`
- stable-line disposition matrix
- protected worktree manifest
- retained asset registry
- disposable product composition registry
- blocker registry
- `F04R_IMPLEMENTATION_BASE_SHA`

## 6.4 门禁

```text
G0_STATUS=DONE
WORKTREE_PROTECTED=PASS
STABLE_LINE_AUDIT=PASS
STABLE_LINE_SYNC=EXPLICITLY_DEFERRED_PENDING_DECISION
RETAINED_ASSET_REGISTRY=FROZEN
DISPOSABLE_PRODUCT_COMPOSITION=FROZEN
F04R_IMPLEMENTATION_BASE_SHA=56fbf18fcb59f91e9d63666c08e302db92ff692c
G1_ENTRY=AUDIT_AND_PROPOSAL_ONLY
```

---

# 7. G1：产品信息架构与能力真值冻结

## 7.1 目标

先决定产品到底有哪些真实工作区、谁能访问、由哪个 owner 承载，再修改任何页面。

## 7.2 产品域

必须逐项决定下列产品域的真实状态：

```text
工程
流程
检测
追溯
监控
AI
设置
诊断
账户
关于
内部 Labs
```

每个产品域必须标记：

```text
DEFAULT_VISIBLE
ROLE_RESTRICTED
PROFILE_RESTRICTED
READ_ONLY_ACCEPTED
INTERNAL_ONLY
DEFERRED_TO_F05
BLOCKS_DEFAULT_ENTRY
```

## 7.3 强制纠偏

- “设置”不得继续指向“诊断”；
- 诊断必须使用独立名称和权限；
- 未实现能力不得用看似完整的空页面冒充；
- 对计划在后续迁移的能力，采用 profile 隐藏或明确的阶段提示，不在主导航长期摆放无效按钮；
- Flow 入口必须与当前工程上下文绑定；
- 高频工程操作不得全部隐藏进“更多”；
- Overview 不得替代真实产品域；
- 主导航必须以产品任务为中心，不以当前已经做完哪些页面为中心。

## 7.4 Owner 与合同矩阵

为每个页面和命令记录：

- route；
- capability flag；
- role/policy；
- GET/POST/PUT/DELETE/SSE endpoint；
- query owner；
- command owner；
- mounted owner；
- dispose owner；
- loading/empty/stale/error/401/403/404/409/unknown outcome；
- Legacy 语义来源；
- 当前 Next 状态；
- F04-R 处置；
- F05 延后理由。

## 7.5 信息架构验收

输出低保真结构和操作流时，只允许使用真实字段和真实动作。
可以使用现有 Vue route 建立无写入的结构验证，但不得制作脱离代码的假产品页面。

本轮已确认并回写的 G1 决策：

- 顶层导航；
- 工程到流程的上下文关系；
- 工作区全局操作位置；
- 诊断与设置的分离；
- 结果与检测的关系；
- 延后能力如何呈现。

## 7.6 门禁

```text
G1_AUDIT_STATE=DONE
G1_PROPOSAL_STATE=APPROVED
G1_STATUS=DONE
PRODUCT_DOMAIN_MAP=FROZEN
NAVIGATION_SEMANTICS=FROZEN
ROUTE_ROLE_PROFILE_MATRIX=FROZEN
OWNER_CONTRACT_MATRIX=FROZEN
MISLEADING_ENTRY_COUNT=6
GOLDEN_JOURNEY_SCOPE=FROZEN
G2_ENTRY=APPROVED
IMPLEMENTATION=FORBIDDEN
```

---

# 8. G2：黄金旅程合同与旧版产品语义回吸

## 8.1 目标

把黄金旅程拆成可验证的用户任务，而不是继续按页面名开发。

## 8.2 逐步骤对标

每一步必须记录：

| 步骤 | 旧版入口与动作 | 新版当前状态 | 目标交互 | 后端权威 | Owner | 错误与恢复 | A/B 指标 |
|---|---|---|---|---|---|---|---|

本轮冻结的完整任务合同、owner、写入链、权限、状态和 Prompt 3 文件边界见 [G2 黄金旅程任务合同](./F04R_G2_黄金旅程任务合同.md)。旧版与新版的实际点击/切页/滚动/上下文基线见 [G2 A/B 验收矩阵](./F04R_G2_旧版新版AB验收矩阵.md)。至少包含：

1. 创建空白工程；
2. 从模板/示例或导入工程；
3. 打开工作区；
4. 搜索并添加算子；
5. 配置基础参数；
6. 配置文件或相机类特殊参数；
7. 创建和绑定全局变量；
8. 配置最终判定；
9. 预览、像素探针和 ROI；
10. 保存与冲突恢复；
11. 正式运行、停止和未知结果恢复；
12. 打开对应结果、诊断和技术追溯；
13. 导出工程或可交付资产。

## 8.3 产品效率基线

对旧版实际任务记录：

- 点击次数；
- 必要页面切换；
- 需要滚动的区域；
- 首屏可见的重要信息；
- 错误恢复步骤；
- 完成任务所需的上下文记忆；
- 不能丢失的中文术语和状态。

新版目标不是机械减少所有点击，而是：

```text
TASK_COMPLETION=COMPLETE
TASK_STEPS<=LEGACY_OR_JUSTIFIED
CONTEXT_SWITCH<=LEGACY_OR_JUSTIFIED
CRITICAL_INFO_VISIBILITY>=LEGACY
ERROR_RECOVERY>LEGACY
```

## 8.4 特殊编辑器决策

文件选择、相机绑定、全局变量、最终判定及标定/测量类能力必须逐项决定：

- 复用哪个后端合同；
- 由哪个现有或新 capability-local owner 承载；
- 是否为 Inspector extension、独立工作台、Modal 或 Drawer；
- 如何保存；
- 如何处理取消、dirty、权限和错误；
- 如何避免把所有能力塞进通用表单。

不得把缺少合同的问题用纯前端字段补齐。相机绑定/单帧捕获的 stable sync 精确文件范围见 [相机合同](./F04R_G2_相机绑定与单帧捕获合同.md)；GlobalVariables 与 FinalDecision 的唯一 owner、dirty、校验、hash 和保存边界见 [变量与判定合同](./F04R_G2_GlobalVariables与FinalDecision合同.md)。

## 8.5 门禁

```text
G2_STATUS=DONE
GOLDEN_JOURNEY_TASK_SPEC=FROZEN
LEGACY_SEMANTIC_BASELINE=FROZEN
SPECIAL_EDITOR_DECISIONS=FROZEN
GLOBAL_VARIABLES_CONTRACT=FROZEN
FINAL_DECISION_CONTRACT=FROZEN
RUN_RESULT_EVIDENCE_CONTRACT=FROZEN
RUNTIME_PACKAGE_CONTRACT=FROZEN
BACKEND_CONTRACT_GAPS=SEPARATELY_APPROVED_OR_PENDING_HARDENING
A_B_ACCEPTANCE_MATRIX=FROZEN
STABLE_CAMERA_SEMANTIC_SYNC=PENDING
BLOCKS_G3_IMPLEMENTATION=YES
BACKEND_POLICY_HARDENING=PENDING
G3_ENTRY=AWAITING_PRODUCT_OWNER_APPROVAL
```

## 8.6 G2 交付状态与 Prompt 3 入口

本轮合同与矩阵完整，G2 可以标记完成；这不是 G3 实现批准。Prompt 3 只能按以下顺序进入：

```text
重新 fetch/审计远端
→ stable camera/MIME/Preview/Legacy fallback 语义同步
→ 串行同步测试
→ 既有 endpoint policy hardening
→ Next capability-local owner 实现
→ Browser/Playwright 与 owner guard
→ 真实 WebView2 / Windows 125% / 真实端点证据
```

Prompt 3 开始条件：G2 文档已提交；产品负责人批准 G3；稳定线同步方案获批准且不带入 Modbus/临时 pdf；GlobalVariables/FinalDecision 继续使用 ProjectSaveCoordinator；任何 capability 只有一个 mounted owner、订阅集合和 writer。`STABLE_CAMERA_SEMANTIC_SYNC=PENDING` 时 `BLOCKS_G3_IMPLEMENTATION=YES`。

---

# 9. G3：黄金旅程实现与产品层重构

## 9.1 目标

在保留架构资产的前提下，完成一个真实、完整且不弱于旧版的核心工作区。

## 9.2 实现顺序

建议串行拆分为四个实施包：

### G3A｜工程入口与资产闭环

- 创建、模板/示例或批准的替代路径；
- 导入、导出；
- 打开、关闭、删除；
- 最近工程；
- 未保存工程切换保护；
- 可交付资产或运行包入口的批准范围；
- 可靠的 404/409/unknown outcome 恢复。

### G3B｜工作区信息架构重构

- 以真实高频任务重排导航、算子、Inspector、Canvas、Preview 和全局操作；
- 保留 canonical Canvas 和现有 split/layout owner；
- 不把四栏 Stitch 构图视为不可修改的视觉权威；
- 画布、图像和参数是视觉主体；
- 1920×1080 和 1366×768 下保存、运行、判定、变量、预览等高频操作必须可达；
- 不用大标题、空白或卡片墙消耗工作区。

### G3C｜关键专业能力闭环

- 文件或相机类特殊参数编辑；
- 全局变量管理与参数绑定；
- 最终判定配置；
- 必要的 Lint/DryRun/子图入口；
- Preview、ROI 与结构化输出衔接；
- 中文错误、权限和恢复；
- 所有写入回到既有权威链。

### G3D｜运行与结果闭环

- Save、dirty、409、unknown outcome、Leave Guard；
- Formal Run、Stop、Reconcile；
- 结果跳转、筛选、详情和诊断；
- 结果与本次工程/运行身份可追溯；
- 不在本轮扩展完整分析看板，但不得破坏后续迁移位置。

## 9.3 视觉实施原则

F04-R 不再使用 Stitch 作为唯一视觉母版。视觉权威顺序调整为：

```text
真实任务与操作效率
> 旧版成熟产品语义
> 当前新架构与 Design System
> 已批准的新版高质量局部
> 外部视觉参考
```

必须做到：

- 不低于旧版的信息密度；
- 通过对齐、比例、排版、控件细节和状态层级建立高级感；
- 不用大面积留白、巨型标题、卡片套卡片、毛玻璃或宽软阴影；
- 丹红只承担品牌与关键意图；
- 中文主标签，技术标识作为次级信息；
- 长工程名、长诊断码、真实错误文本有完整承载；
- 所有页面至少使用真实长度 fixture，而不是整齐的短文本样本。

## 9.4 测试要求

- typecheck、lint、build；
- owner/contract/route/import guard；
- capability mount/unmount/dispose；
- Project create/import/export/open/delete/save；
- GlobalVariables 与 FinalDecision persistence；
- 特殊编辑器权限、取消、dirty、保存与错误；
- Preview/ROI/Run/Results 回归；
- Playwright 黄金旅程；
- 20-cycle 或等价生命周期压力；
- 主 chunk 和 route lazy-load 审计，避免继续扩大 888 KB 单 chunk；
- 不得用更新截图快照掩盖行为回归。

## 9.5 门禁

```text
G3_STATUS=DONE
GOLDEN_JOURNEY_FUNCTIONAL_PARITY=PASS
SPECIAL_EDITOR_CLOSURE=PASS
GLOBAL_VARIABLES_CLOSURE=PASS
FINAL_DECISION_CLOSURE=PASS
SAVE_RUN_RESULTS_CLOSURE=PASS
SINGLE_OWNER_GUARDS=PASS
TYPECHECK_LINT_BUILD=PASS
TARGETED_TESTS=PASS
BROWSER_GOLDEN_JOURNEY=PASS
PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER
G4_ENTRY=APPROVED_FOR_EVIDENCE
```

## 9.6 Prompt 3 实际状态（2026-07-22）

Prompt 3 已形成工程候选，代码与证据索引见 [G3 工程候选与证据](./F04R_G3_黄金旅程工程候选与证据.md)。这里的 `DONE` 只表示 G3 工程闭环，不替代 G4 的真实宿主、DPI 与产品负责人视觉门禁。

```text
G3_ENGINEERING_STATE=DONE
STABLE_CAMERA_SEMANTIC_SYNC=PASS
BACKEND_POLICY_HARDENING=PASS
GOLDEN_JOURNEY_FUNCTIONAL_PARITY=PASS
BROWSER_GOLDEN_JOURNEY=PASS
PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER
REAL_WEBVIEW2_FINAL_GATE=NOT_YET_RUN
WINDOWS_125_DPI=NOT_YET_RUN
REAL_CAMERA=NOT_PERFORMED
F04R_STATUS=IN_PROGRESS
F05_ENTRY=BLOCKED
```

---

# 10. G4：新旧 A/B、真实宿主与产品视觉确认

## 10.1 目标

证明新版黄金旅程在真实使用中至少不弱于旧版，而不是只证明代码能运行。

## 10.2 A/B 证据

在相同任务、相同工程、相同数据和尽可能相同状态下对比：

- 工程入口；
- 工作区空态与复杂流程；
- Inspector 基础参数与特殊编辑器；
- 全局变量；
- 最终判定；
- Preview 与 ROI；
- 保存冲突；
- 正式运行；
- 结果详情；
- 长文本、错误、空态、权限和禁用状态。

至少覆盖：

- 1920×1080；
- 1366×768；
- Windows 125%；
- light/compact 主生产组合；
- dark 与 comfortable 作为兼容证据，不要求成为主视觉方向；
- 真实中文长文本；
- 真实或高可信复杂数据 fixture。

## 10.3 真实宿主证据

必须分别报告：

```text
BROWSER_FIXTURE
PLAYWRIGHT
REAL_WEBVIEW2_DEBUG
REAL_WEBVIEW2_RELEASE
WINDOWS_125_DPI
PUBLISH_NO_NODE
REAL_ENDPOINT_OR_HARNESS
```

其中 F04-R 最低要求：

- 真实 WebView2 Debug：PASS；
- 真实 WebView2 Release：PASS；
- Windows 125% DPI：PASS；
- Browser fixture 与真实宿主差异：已记录并处置。

现场相机、PLC、Station 和独立无 Node 目标机若未执行，必须继续标记 `NOT_PERFORMED`，不能由本阶段证据替代。

## 10.4 产品负责人验收

产品负责人必须逐项确认：

- 是否仍有“草台班子”或通用后台感；
- 是否至少达到旧版专业度；
- 工作区是否适合长期使用；
- 高频操作是否比旧版更清楚；
- 功能是否真实可完成；
- 是否允许该信息架构扩展到后续产品域。

禁止由 Agent 自行把产品视觉门禁判定为 PASS。

## 10.5 门禁

```text
G4_STATUS=DONE
LEGACY_NEXT_A_B_TASK_GATE=PASS
REAL_WEBVIEW2_DEBUG=PASS
REAL_WEBVIEW2_RELEASE=PASS
WINDOWS_125_DPI=PASS
FUTURE_G4_PRODUCT_VISUAL_GATE=PASS
GOLDEN_JOURNEY_APPROVED_AS_F05_PATTERN=YES
G5_ENTRY=APPROVED
```

若产品视觉再次失败：

```text
F04R_RECOMMENDATION=REASSESS_OR_ABANDON
F05_ENTRY=BLOCKED
```

不得继续向剩余页面扩散。

---

# 11. G5：F04-R 收口与 F05 重规划输入

## 11.1 目标

把黄金旅程中验证有效的架构、产品模式和验收规则固化为后续唯一输入，而不是直接把当前页面复制到所有领域。

## 11.2 F05 候选迁移波次

F04-R 只输出顺序和依赖，不自动启动：

### Wave A｜检测与结果完整闭环

- 连续检测；
- 单次/连续/停止；
- 生产保护；
- 实时结果；
- 分析视图；
- 图像、Evidence、对比和导出。

### Wave B｜设置、设备与 Station 操作

- 外观与通用设置；
- 相机；
- PLC；
- TCP；
- Station；
- 存储与数据库；
- 运行保护；
- 用户与权限；
- Station 日志、命令、运行包下发、审计和结果导出。

### Wave C｜AI 工作区

- 模型设置与测试；
- 澄清；
- 计划；
- Build；
- Apply/Undo；
- 恢复；
- 与工程、保存、资源 readiness 的一致闭环。

每个 Wave 必须单独冻结 contract、owner、权限、写入和 A/B 任务，不得因为 F04-R 样板通过就批量复制页面。

## 11.3 最终交付

- F04-R 完成报告；
- retained asset final registry；
- golden journey code/evidence index；
- product IA contract；
- visual and interaction baseline；
- F05 capability roadmap；
- blocker/deferred evidence registry；
- legacy retirement prerequisites；
- default entry decision input。

## 11.4 F04-R 退出状态

成功时：

```text
F04R_STATUS=COMPLETE
F04R_REWORK_RESULT=PASS
CURRENT_NEXT_ARCHITECTURE=RETAIN
GOLDEN_JOURNEY_PRODUCT_RESULT=PASS
FUTURE_EXIT_PRODUCT_VISUAL_GATE=PASS
LEGACY_RETIREMENT=NOT_APPROVED
DEFAULT_ENTRY_CHANGE=NOT_AUTOMATIC
F05_ENTRY=READY_FOR_PLANNING
```

失败时：

```text
F04R_STATUS=BLOCKED
F04R_REWORK_RESULT=FAIL
PRODUCT_VISUAL_CONFIRMATION=FAIL
F05_ENTRY=BLOCKED
ROUTE_DECISION=REASSESS_OR_ABANDON
```

---

# 12. 证据矩阵

| 证据 | G0 | G1 | G2 | G3 | G4 | G5 |
|---|---:|---:|---:|---:|---:|---:|
| Git/Worktree/Remote 基线 | 必须 | 复核 | 复核 | 复核 | Final SHA | Final |
| Stable-line 语义同步 | 必须 | 按需 | 按需 | 按需 | 冻结 | 记录 |
| Product domain / route / role / profile |  | 必须 | 冻结 | 守卫 | 回归 | 输入 |
| Owner / contract matrix |  | 必须 | 冻结 | 守卫 | 回归 | 输入 |
| Legacy task baseline |  |  | 必须 | 对照 | A/B | 归档 |
| Functional tests |  |  | 合同 | 必须 | 全量 | Final |
| Browser / Playwright |  | 结构 | 任务基线 | 必须 | A/B | Final |
| Real WebView2 |  |  |  | 定向 | 必须 | Final |
| Windows 125% DPI |  |  |  | 定向 | 必须 | Final |
| Product visual approval |  | 结构确认 | 任务确认 | AWAITING | 必须 PASS | 记录 |
| F05 roadmap |  |  | 初稿 | 更新 | 冻结 | 必须 |

---

# 13. 阻断码

```text
F04R-B00  工作树或远端分叉未治理
F04R-B01  稳定线关键语义未同步
F04R-B02  出现第二 owner / client / canvas / save chain
F04R-B03  Product domain、route、role 或 profile 未冻结
F04R-B04  “设置”等导航继续指向不等价能力
F04R-B05  黄金旅程存在不可完成步骤
F04R-B06  特殊编辑器缺少后端合同而被前端伪造
F04R-B07  GlobalVariables 或 FinalDecision 未进入正式保存链
F04R-B08  Browser fixture 被冒充真实 WebView2/DPI
F04R-B09  新版任务效率或信息密度明显低于旧版
F04R-B10  产品负责人视觉确认失败
F04R-B11  正式 flags 或 Legacy 退役被擅自修改
F04R-B12  未完成 F04-R 即启动 F05 实现
```

---

# 14. 风险与控制

| 风险 | 表现 | 控制 |
|---|---|---|
| 沉没成本偏差 | 为保护现有页面而降低产品标准 | 当前页面 composition 明确可丢弃 |
| 过度回归旧版 | 复制 legacy DOM/JS/CSS | 只吸收语义，代码复制守卫 |
| 再次视觉先行 | 先换皮、后补能力 | G1/G2 先冻结任务与合同 |
| F04-R 无限膨胀 | 把所有缺口一次迁完 | 仅做黄金旅程，剩余进入 F05 roadmap |
| 双轨 Owner | 新页面另建临时状态链 | Owner matrix + import/source guards |
| 证据失真 | fixture 代替真实宿主 | G4 强制 WebView2 + 125% DPI |
| 新 mega-file | Results/Workspace 继续膨胀 | 组件边界、composable、route lazy-load 门禁 |
| 功能完整但仍难看 | 自动化全部 PASS 但产品不成熟 | 用户视觉确认是独立阻断门禁 |
| 旧版变化未吸收 | 稳定线继续前进 | 每 Goal 入口检查 stable-line drift |
| 提前切换默认入口 | Pilot 误当正式批准 | flags 与 Legacy retirement 独立决策 |

---

# 15. 执行组织建议

F04-R 分为 4 个 Prompt：

- Prompt 1：只读 G0 + G1 审计与产品提案；已完成并形成批准输入；
- Prompt 2：G1 获批后冻结 G2 黄金旅程合同、旧版语义、相机方案、变量/判定、运行结果、运行包、权限与 A/B 基线；本轮完成；
- Prompt 3：在 G2 获批后执行 G3 真实实现；
- Prompt 4：G4 真实证据、用户验收准备与 G5 收口。

除 G3 外，不应把多个 Goal 合并为一次长实现任务。
G3 内可使用最多 3 个子代理，但共享 Shell、Router、Workspace、Tokens 和 contracts 由主 Agent 统一修改。

---

# 16. 批准与当前状态检查

计划获准执行前，产品负责人至少确认：

- 同意 F04-R 只证明一个黄金旅程，不一次迁完全部产品域；
- 同意当前页面 composition 可被重做；
- 同意旧版是产品语义基线，但禁止复制旧版实现；
- 同意 F05 在 F04-R 用户视觉确认通过前继续阻塞；
- 同意真实 WebView2 与 Windows 125% DPI 是强制退出证据；
- 同意产品视觉再次失败时重新评估 ABANDON，而不是自动继续投入。

```text
PLAN_APPROVAL=APPROVED_FOR_G3
G0_STATUS=DONE
G1_AUDIT_STATE=DONE
G1_PROPOSAL_STATE=APPROVED
G1_STATUS=DONE
G2_ENTRY=APPROVED
G2_STATUS=DONE
GOLDEN_JOURNEY_TASK_SPEC=FROZEN
LEGACY_SEMANTIC_BASELINE=FROZEN
SPECIAL_EDITOR_DECISIONS=FROZEN
GLOBAL_VARIABLES_CONTRACT=FROZEN
FINAL_DECISION_CONTRACT=FROZEN
RUN_RESULT_EVIDENCE_CONTRACT=FROZEN
RUNTIME_PACKAGE_CONTRACT=FROZEN
A_B_ACCEPTANCE_MATRIX=FROZEN
STABLE_CAMERA_SEMANTIC_SYNC=PASS
BLOCKS_G3_IMPLEMENTATION=NO
BACKEND_POLICY_HARDENING=PASS
G3_ENTRY=APPROVED_AND_EXECUTED
G3_ENGINEERING_STATE=DONE
GOLDEN_JOURNEY_FUNCTIONAL_PARITY=PASS
BROWSER_GOLDEN_JOURNEY=PASS
PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER
REAL_WEBVIEW2_FINAL_GATE=NOT_YET_RUN
WINDOWS_125_DPI=NOT_YET_RUN
REAL_CAMERA=NOT_PERFORMED
F04R_STATUS=IN_PROGRESS
F05_ENTRY=BLOCKED
```
