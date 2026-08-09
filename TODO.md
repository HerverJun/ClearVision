# ClearVision Studio UI Next 收口 TODO

```text
DOCUMENT_ROLE=EXECUTION_PLAN
DOCUMENT_STATE=G4_READY
CURRENT_BRANCH=studio-ui-next
PLANNING_BASELINE_HEAD=f8569fa85244d19a18ba7308051e4d2b2ed4060a
IMPLEMENTATION_BASELINE_HEAD=21105d57de7e5b4ce41365c7827ed14e64ca7ba5
REFERENCE_STABLE_REF=origin/codex初稿@e76c74e392bb14ffe02ef9ea9c7a614cb8987f04
PLANNING_MERGE_BASE=e1bad492fecb6dff2c0a8f848db9ebfa18acf093
PLANNING_DIVERGENCE=STABLE_ONLY_81_NEXT_ONLY_294
REFS_REFRESHED_FOR_THIS_PLAN=YES
CURRENT_STATUS_SOURCE=docs/进行中/StudioUINext/F10_ContractAndProductionPlan.md
EXECUTION_POLICY=ONE_GATE_AT_A_TIME
G0_STATE=DONE
G0_IMPLEMENTATION_HEAD=21105d57de7e5b4ce41365c7827ed14e64ca7ba5
G0_STABLE_COMMITS_AUDITED=81
G0_STABLE_COMMITS_LEFT_ONLY=77
G0_STABLE_COMMITS_PATCH_EQUIVALENT=4
G0_GENERATED_ARTIFACTS=CONTROLLED_SOURCE_DERIVED_OUTPUTS_PLUS_NOT_APPLICABLE_HISTORICAL_SNAPSHOTS
G0_REMOTE_REFRESH=PASS_NO_REMOTE_ADVANCE_OR_FORK
G1_STATE=DONE
G1_BASELINE_HEAD=21105d57de7e5b4ce41365c7827ed14e64ca7ba5
G1_IMPLEMENTATION_HEAD=98cb8c7f54d2d51ea5b59ca534aafd51544b773f
G1_WORKTREE_STATE=COMMITTED_LOCAL_NOT_PUSHED
G1_VERIFICATION_DATE=2026-08-09
G2_STATE=DONE
G2_BASELINE_HEAD=21105d57de7e5b4ce41365c7827ed14e64ca7ba5
G2_IMPLEMENTATION_HEAD=98cb8c7f54d2d51ea5b59ca534aafd51544b773f
G2_WORKTREE_STATE=COMMITTED_LOCAL_NOT_PUSHED
G2_VERIFICATION_DATE=2026-08-09
G3_STATE=DONE
G3_BASELINE_HEAD=98cb8c7f54d2d51ea5b59ca534aafd51544b773f
G3_PRODUCT_IMPLEMENTATION_HEAD=a3c043e77ff9bcbc80fbf638f8f9f52a217fa8a8
G3_EVIDENCE_HEAD=1c6e61e5a53d59ac3a7f78054af5eab3e86ec667
G3_WORKTREE_STATE=COMMITTED_LOCAL_NOT_PUSHED
G3_VERIFICATION_DATE=2026-08-09
PRODUCTION_ACCEPTANCE=NOT_GRANTED
LEGACY_RETIREMENT=NOT_APPROVED
```

> 本文件只定义下一阶段的执行顺序、Owner、退出条件和验证门禁。当前事实状态以代码、配置和
> `F10_ContractAndProductionPlan.md` 为准；本文件与 F10 冲突时，先停止执行并按当前代码修正文档，
> 不得选择对实施更方便的旧结论。

## 1. 当前判断

Studio UI Next 已是默认入口，主体业务迁移进入后期收口，但尚未完成生产切换：

- Next 已建立 Router、Auth/Session、Leave Guard、唯一 API transport、Host adapter、query owner、
  workspace owner、canonical FlowCanvas/ImageCanvas 和 capability-local lifecycle owner。
- Project import/export、NPoint authorization、planar calibration、Results bulk export、Line Sequence
  软件闭环已在 F10 标记为 `DONE`，不得按旧 TODO 重复实现。
- Station 测试包/设备命令与整体证据仍为 `PARTIAL`。
- AI attachment、CV model artifact、TemplateMatching artifact、calibration asset projection、数据库高级维护
  仍为 `BLOCKED_BY_CONTRACT`；G2 ADR 已冻结延期边界，未由前端私有实现替代。
- GlobalVariables 类型过滤/Flow identity 校验和 Line Sequence 最近图像输入/返回预览投影已在
  `98cb8c7f5` 实现 checkpoint 完成；尚未推送远端。
- F10 已记录 implementation SHA 的 Chromium/fixture 定向 journey `7/7`；该结果不代表真实宿主通过。
- Remote CI 当前为 `BLOCKED_BY_ENVIRONMENT`，Final Gate 为 `PARTIAL`，生产验收仍为 `NOT_GRANTED`。
- 真实 WebView2 100%/125%、独立 no-Node、现场 Camera/PLC/Station、生产 soak 尚未完成。
- `NEXT_DEFAULT` 不等于 Legacy 已退役；Legacy 页面、静态文件和 compatibility chain 仍需受控隔离。

规划基线只是进入点。开始任何实现前必须执行 G0，刷新远端并重新冻结 clean SHA。

## 2. 状态与勾选规则

- 状态只使用：`LOCKED`、`READY`、`IN_PROGRESS`、`BLOCKED_BY_CONTRACT`、
  `BLOCKED_BY_ENVIRONMENT`、`DONE`、`DEFERRED`。
- `[x]` 表示当前代码和绑定 SHA 的证据已确认；`[ ]` 表示尚未执行或当前 SHA 尚无证据。
- 历史 PASS、历史截图、旧测试数量和旧 SHA 不得外推到新的候选 SHA。
- Browser/Chromium fixture 不得替代真实 WebView2、Windows DPI、no-Node 或现场硬件。
- 未运行写 `NOT RUN`；环境或人工验收未执行写 `NOT PERFORMED`。
- 每个 Gate 完成后先更新 F10、复审并冻结新 SHA，再解锁下一 Gate。
- Gate 表允许的只读审计可以并行；任何代码或共享文档实施仍服从前置 Gate 和唯一 Owner。

## 3. Gate 总览

| Gate | 优先级 | 工作包 | 当前状态 | 唯一 Owner | 解锁条件 |
| --- | --- | --- | --- | --- | --- |
| G0 | P0 | 候选冻结、稳定线语义同步、文档收敛 | DONE | 主协调 Owner | 无 |
| G1 | P0 | 请求/写入生命周期与跨工程状态安全 | DONE | Workspace lifecycle Owner | G0 DONE |
| G2 | P0/P1 | 后端合同解阻与功能差距决策 | DONE | 主协调 Owner + 对应后端 Owner | G0-G1 DONE |
| G3 | P1/P2 | 产品体验、视觉、中文与 Vue 可维护性收口 | DONE | UI Owner；共享面由主协调 Owner | G2 DONE |
| G4 | P0 | Legacy profile 隔离、rollback 与退役准备 | READY | Host/Release Owner | G0-G3 DONE |
| G5 | P0 | 同一 clean SHA 的本地软件证据 | LOCKED | Final Evidence Owner | G4 DONE |
| G6 | P0 | 真实宿主、目标机、远程与现场验收 | LOCKED | Release/Field Owner | G5 DONE |

## 4. G0：候选冻结与稳定线语义同步

**目标**：消除 `studio-ui-next` 与稳定维护线之间未经审计的 authority、contract、security 和修复差异，
建立后续工作的唯一 clean 基线。

**文件边界**：可能触及 Host、contracts、endpoints、AI、Preview、operator metadata、CI、配置和共享文档，
因此仅由主协调 Owner 集成；不并行修改共享文件。

- [x] G0.1 记录 `git status --short --branch`、当前 HEAD、upstream、merge-base 和分叉计数。
- [x] G0.2 执行 `git fetch origin --prune`；若远端 `studio-ui-next` 前进或历史分叉，停止并报告。
- [x] G0.3 冻结 clean 候选 SHA；审计期间禁止继续向候选混入无关功能。
- [x] G0.4 对 stable-only commits 建立语义矩阵：`MERGE` / `SUPERSEDED` / `NOT_APPLICABLE` /
  `BLOCKED`，每项记录代码锚点、风险、冲突和验证。
- [x] G0.5 第一批同步 AI workflow artifact authority：旧 Planner 退役、artifact admission、route registry、
  fingerprint、readiness/recovery、failed artifact summary、active model 和 enum compatibility。
- [x] G0.6 第二批同步 acquisition、Unicode 图像路径、Camera 厂商探测、Preview/ROI 和 1080P 修复。
- [x] G0.7 第三批同步 operator metadata/contracts、性能报告与 CI/测试稳定性修复。
- [x] G0.8 共享 `appsettings.json`、API contracts、Host、CI 和测试配置逐项语义合并，禁止整文件选择
  `ours` / `theirs`。
- [x] G0.9 每批完成后串行运行受影响项目的定向测试；同一 `.csproj` 不并发测试。
- [x] G0.10 更新 F10 当前 SHA、stable 同步 disposition 和证据；README、F09、M00、M09 标记
  `SUPERSEDED_FOR_CURRENT_STATUS_BY_F10`，不再维护第二套状态。

**G0 退出条件**：

- [x] 所有 stable-only authority/security/contract 提交都有明确 disposition。
- [x] 当前代码不再无说明地保留稳定线已退役的旧 Planner/Loop 权威。
- [x] F10 的 `IMPLEMENTATION_HEAD` 指向冻结实现 SHA，文档提交来源可追溯；当前 HEAD/upstream 包含该实现与
  对应文档提交，工作树 clean 且无无关修改。
- [x] 受影响定向测试有当前 SHA 结果，未运行项明确记录。

## 5. G1：生命周期与状态安全

**目标**：关闭“组件已卸载，但旧 owner、请求或写操作仍存活”的架构红线，并修复跨工程错误状态。

**Owner 约束**：Project save + GlobalVariables 由一个纵向 Owner 处理；FlowCanvas + Inspector + Preview
不得拆成多个并行实现 owner。

- [x] G1.1 盘点所有 capability 的 request、timer、SSE、subscription、AbortController 和写入口，生成
  `mount -> active -> dispose` 资源账本。
- [x] G1.2 所有可取消 GET/read 查询在 selection、project、route、session、flag 变化和 dispose 时 abort。
- [x] G1.3 为 PUT/POST 写入冻结统一语义：写操作由能跨组件卸载存活的唯一 Owner 持有，或阻止离开直到
  settle；网络结果未知必须按 `clientOperationId` reconcile。不得用“abort 后当作未提交”伪造确定性。
- [x] G1.4 修复 workspace save GET/PUT、Template GET/POST/PUT、GlobalVariables runtime GET/PUT/POST、
  Camera binding read 的生命周期缺口。
- [x] G1.5 修复跨工程切换时 variables/decision/package/template 弹窗继续持有旧 disposed owner；切换前
  关闭旧弹窗或以工程 identity 强制 remount。
- [x] G1.6 为 AI handoff 的 `stage -> acknowledge` 增加可恢复协议：acknowledge 失败时能够回滚 staging，
  或按 operation identity reconcile，不能留下来源不明的本地 Flow 草稿。
- [x] G1.7 连续检测工程选择页显式投影 `unauthorized`、`forbidden`、`stale`、`partial-failure` 和
  `aborted`，不得把 401/403 显示为“暂无工程”。
- [x] G1.8 为 artifact DELETE、Camera stop 等 dispose 后 cleanup 请求建立 ADR：明确唯一 Owner、幂等、
  超时、失败和允许的 cleanup 豁免范围。
- [x] G1.9 增加 route param 切换、Feature Flag on/off、session 失效、运行中离开和晚到响应测试。
- [x] G1.10 diagnostics 证明 unmount 后旧 owner、subscription、timer、SSE 和可取消 request 数量归零。

**G1 退出条件**：

- [x] 任何 capability 切换后只有一个 mounted owner、一个订阅集合和一个写入口。
- [x] 不存在跨工程旧 owner 与新 Project 投影混用。
- [x] 每类写请求都有 committed / rejected / unknown-outcome / reconciled 的可证明终态。
- [x] cleanup ADR 获得批准，未批准的 dispose 后请求保持阻断。

G1 实现已在 `98cb8c7f5` checkpoint 提交但尚未推送；当前验证与边界见
`docs/进行中/StudioUINext/F10_ContractAndProductionPlan.md` 的 G1 工作记录。该 SHA 是本地候选，
不冒充远端候选或生产验收。

## 6. G2：合同解阻与功能差距

### 6.1 后端合同先行

以下任务在合同批准前只能取证和提出方案，不得由 Vue、localStorage 或第二 endpoint 替代：

- [x] G2.1 AI attachment：冻结为 `DEFER`；重新进入合同覆盖上传、resource reference、版本、权限、TTL 和 AgentRun 恢复。
- [x] G2.2 CV model artifact：冻结为 `DEFER`；视觉模型资产与 `/api/ai/models` 的 LLM 配置保持分离。
- [x] G2.3 TemplateMatching artifact：冻结为 `DEFER`；图像模板产物不得借用 Flow template authority。
- [x] G2.4 Calibration projection：冻结为 `DEFER`；正式 calibration asset 不静默投影为算子 numeric scale/offset。
- [x] G2.5 Database advanced：冻结为 `DEFER`；repair、restore、cleanup、global reset 在完整 Admin 合同前不暴露写入口。
- [x] G2.6 Station test package/device command：冻结 package identity、target Station、幂等操作身份、过期、
  查询、取消和终态 reconcile。

### 6.2 旧版能力逐项决策

每项必须得到 `MIGRATE`、`RELOCATE`、`DEFER` 或 `RETIRE_WITH_APPROVAL` 之一；不得因页面更简洁而静默删除。

- [x] G2.7 N 点标定高级工作流：冻结为 `DEFER`；保留当前采集、求解和正式 asset 保存，完整高级工作流按 ADR 重新进入。
- [x] G2.8 GlobalVariables：按端口/参数 data type 过滤候选，并校验变量、端口、参数存在性与兼容性；
  已在 `WorkspaceGlobalVariablesOwner` 与工作台实现，证据见 G2 ADR。
- [x] G2.9 通用 AutoTune：冻结为 `DEFER`；逐算子合同未齐前不恢复泛化入口。
- [x] G2.10 Line Sequence：Preview/最近检测图输入和返回预览图已迁移；AI parameter-only follow-up 按 ADR 延期。
- [x] G2.11 连续检测：冻结为 `RELOCATE`；缺料超时、连续 NG 保护和现场恢复策略保持在既有 Runtime/Inspection authority。
- [x] G2.12 Demo/示例工程：冻结为 `RELOCATE`；继续使用受控后端 Project lifecycle，不复制 demo Flow JSON 到前端。
- [x] G2.13 Camera 标定入口与 Settings 高风险操作：标定 `MIGRATE` 到 Workspace，高风险维护 `RELOCATE/DEFER` 到 Admin 合同。

G2 当前处置与合同矩阵见 `docs/进行中/StudioUINext/ADR-G2-合同解阻与能力处置.md`。主协调 Owner 已冻结
G2.1-G2.5、G2.7/G2.9、G2.10 AI follow-up 等延期边界；延期能力未来重新进入仍需产品/后端 owner 批准，
但不再阻塞本轮 G2 合同决策 Gate。延期不表示能力迁移、生产验收或 Legacy 退役。

**G2 退出条件**：

- [x] 每个合同项都有明确结论：已冻结可实施的后端 contract，或由主协调 Owner 冻结 `DEFER` / `RELOCATE`。
- [x] 对可实施项，contract 已覆盖 Owner、权限、并发身份、错误和 reconcile；对延期项，影响、fallback 和
  重新进入条件已记录。
- [x] 每个旧版用户任务都有明确 disposition、入口、测试和中文状态语义。
- [x] 延期能力保持不可用且没有前端私有替代实现；重新进入不作为本轮 G2 退出条件。

## 7. G3：产品体验、视觉与 Vue 工程收口

**目标**：在不减少能力、不改变 authority 的前提下完成 Quiet Precision、工业高信息密度和简体中文体验。

- [x] G3.1 Results 建立“态势总览 / 调查详情”两层视图；保留执行状态与判定结果双轴，不恢复虚假的空 KPI。
- [x] G3.2 Stations 建立全站概览、异常优先排序和详情调查层，兼顾鸟瞰扫描与命令真实性。
- [x] G3.3 Projects 改善 1920 宽屏利用率、空数据布局和最近工程密度，不增加营销式卡片或大标题。
- [x] G3.4 清理页面整圈 Panel、卡片套卡片和无效留白；每个轴只保留一个明确滚动 Owner。
- [x] G3.5 解决 Results 短屏 `overflow:hidden`、Workspace 9/10/11px 文本、26px 命中区和长中文截断。
- [x] G3.6 外观/更多菜单支持点击外部关闭、Escape、焦点返回和 viewport 边界约束。
- [x] G3.7 统一简体中文词表，移除面向用户的 `authority`、`Profile`、`Admin only`、`safe read`、
  `G3`、`下一阶段` 等研发语言。
- [x] G3.8 修正 Diagnostics/About 的默认入口、产品版本、宿主/后端版本、许可证和支持信息。
- [x] G3.9 拆分超大 SFC 的渲染/组合责任：Results、AI Settings、WorkspaceShell、TCP Settings、Projects；
  capability lifecycle owner 和写入口保持唯一，不因拆组件复制状态树。
- [x] G3.10 覆盖 loading、empty、error、401、403、offline、stale、conflict、unknown-outcome、长中文、
  reduced motion 和键盘路径。

**G3 退出条件**：

- [x] 1920x1080、1536x864、1366x768 在 light/dark、compact/comfortable 下无非预期水平滚动、
  双层滚动、越界浮层或文本遮挡。
- [x] Canvas、Inspector、Preview、保存、正式运行和核心状态在 125% 等效短屏首屏可达。
- [x] Browser 截图完成方向性复审；真实 WebView2/DPI 仍留到 G6，不提前写 PASS。

G3 冻结在 `1c6e61e5a53d59ac3a7f78054af5eab3e86ec667`。F02 方向性证据位于
`.tmp/studio-ui-next/f02-1/g3-1c6e61e5a/`（73 组 PNG/JSON），F03 Workspace 证据位于
`.tmp/studio-ui-next/f03/g3-1c6e61e5a/`（12 组 PNG/JSON）；两组均为 `BROWSER_FIXTURE` +
`HARNESS_SEEDED_SESSION`，不能替代真实 WebView2 或 Windows DPI。

## 8. G4：Legacy 隔离与退役准备

**目标**：让 `NEXT_DEFAULT` 只挂载 Next composition root，同时保留可审计、可演练的命名式 Legacy fallback。

- [ ] G4.1 盘点 Next build 仍复用的 Legacy canonical Canvas、Preview、ROI、参数依赖和 visual metadata 模块；
  标记为共享底层依赖，而不是第二业务 composition root。
- [ ] G4.2 按 Startup Profile 隔离静态入口：Next profile 不挂载、订阅或执行 Legacy `app.js`；
  `LEGACY_FALLBACK` 仅通过显式配置和重启启用。
- [ ] G4.3 隔离 Legacy WebMessage compatibility chain；Next 只保留 Host capability adapter，不恢复执行旁路。
- [ ] G4.4 Studio UI 资源缺失继续 fail-closed 到诊断页，不静默回退 Legacy。
- [ ] G4.5 证明 profile 切换会 unmount/dispose 旧 owner，并停止 subscription、timer、SSE、request 和写入口。
- [ ] G4.6 进行 Next -> Legacy -> Next rollback drill，验证同一 Project、PersistenceRevision、启动诊断和进程退出。
- [ ] G4.7 在 G6 全部通过前不删除 Legacy 源码；物理删除必须是单独批准的最终工作包。

**G4 退出条件**：

- [ ] Next profile 不存在可运行的第二前端业务 root。
- [ ] Legacy fallback 入口、适用范围、恢复步骤和删除条件均有文档与自动证据。
- [ ] canonical Canvas/ImageCanvas 仍只有一个内核和一个 mounted owner。

## 9. G5：同一 clean SHA 的本地软件证据

所有命令绑定同一个 clean source SHA；任一实现修改都会使本 Gate 重新开始。

- [ ] G5.1 StudioUI：`npm run lint`。
- [ ] G5.2 StudioUI：`npm run typecheck`。
- [ ] G5.3 StudioUI：`npm run test:unit`。
- [ ] G5.4 StudioUI：`npm run build`、production build 和既有 bundle gate。
- [ ] G5.5 Studio UI Next Playwright：Project、Workspace、Results、Stations、Settings、AI、Inspection、
  lifecycle/flag/session、responsive/accessibility 全部受影响 journey。
- [ ] G5.6 .NET：按仓库固定脚本串行运行受影响的 Product、Desktop endpoints、Services、Runtime、Station 测试；
  同一 `.csproj` 合并过滤条件，不并发启动。
- [ ] G5.7 Release publish 只写 `./.tmp/publish-check/`，验证 hashed assets、manifest、Next/Legacy profile 和 stale chunk。
- [ ] G5.8 静态 no-Node 扫描、启动配置、rollback runner、性能/内存基线分别记录；不把扫描冒充目标机启动。
- [ ] G5.9 运行 `git diff --check`，确认无未忽略 publish、截图、日志和测试结果产物。
- [ ] G5.10 更新 F10 evidence manifest：source SHA、命令、环境、结果、产物、失败分类和未执行项完整。

**G5 退出条件**：当前 clean SHA 的所有软件 Gate 为 PASS 或有经批准的明确 blocker；历史 F09/M00/M09 PASS
不得充当当前证据。

## 10. G6：真实环境与生产验收

- [ ] G6.1 真实 WinForms + WebView2：Windows 100% 和 125%，Debug/Release，启动、登录、会话失效、关闭。
- [ ] G6.2 1920x1080、1536x864、1366x768/等效 client size；light/dark、compact/comfortable。
- [ ] G6.3 独立无 Node 目标机安装、启动、升级、资源加载和卸载验证。
- [ ] G6.4 Remote CI clean checkout；required jobs 和 Final Gate 全部通过，不放宽性能/质量阈值。
- [ ] G6.5 真实 Camera：发现、绑定、单帧、连续预览、触发、断连恢复、标定。
- [ ] G6.6 真实 PLC/TCP：连接、收发、超时、断连、重连和高风险命令状态。
- [ ] G6.7 真实 Station：运行包、测试包、部署、命令 unknown-outcome/reconcile、结果/日志/健康回流。
- [ ] G6.8 真实 AI 模型与资源：澄清、Build、attachment/resource、handoff、恢复、取消和正式保存。
- [ ] G6.9 rollback drill、长时间运行、生产 soak、内存/SSE/request 资源稳定性。
- [ ] G6.10 产品 Owner 签收；只有签收后才能设置 `PRODUCTION_ACCEPTANCE=GRANTED`，另行批准
  `LEGACY_RETIREMENT`。

## 11. Owner 与并行规则

| 范围 | 执行规则 |
| --- | --- |
| G0 stable 同步、Host、contracts、CI、配置、Router、App Shell | 主协调 Owner 串行处理 |
| G1 实施 + G2/G3 只读审计 | G0 完成后可并行；G2/G3 在前置 Gate 完成前不得修改代码或共享文档 |
| FlowCanvas + Inspector + Preview + ROI + Calibration | 一个纵向实现 Owner，不拆并行 |
| Project lifecycle + Project save + GlobalVariables | 一个纵向实现 Owner，不拆并行 |
| G2 独立叶子 capability | 合同冻结后可并行；每个 capability 只有一个 Owner 和文件白名单 |
| Design tokens、共享 primitives、App Shell | 仅主协调 Owner 修改 |
| Results/Stations/Settings 独立页面视觉 | 文件无重叠、状态无共享时可并行 |
| 同一 `.csproj` 测试 | 必须串行；不同项目仅在端口、数据库、设备和输出目录完全隔离时并行 |

共享文件包括 `package.json`、lockfile、Vite、Router、App Shell、Design Tokens、API contracts、HostBridge、
`.csproj`、CI、Feature Flags、根 `AGENTS.md`、根 `TODO.md` 和 F10。

## 12. 通用 Definition of Done

- [ ] 完整用户路径可完成，不只证明路由、按钮或 DOM 存在。
- [ ] 旧版能力已标记为保留、优化、重定位、只读、隐藏、延后或经批准退役。
- [ ] 后端 authority、`ProjectSaveCoordinator`、Runtime/Station、AgentRun 和正式结果权威未改变。
- [ ] 没有第二 API transport、HostBridge、EventBus、ServiceRegistry、Canvas 或保存链。
- [ ] 唯一 owner、subscription、request、timer、SSE 和写入口具有 mount/dispose 证据。
- [ ] 权限、readonly、running、loading、empty、error、offline、stale、conflict、unknown outcome 已覆盖。
- [ ] 简体中文术语一致；错误说明发生原因、影响和下一步，不用诊断码代替解释。
- [ ] 1920x1080 和 125% 等效短屏下核心操作可达，无水平滚动、双层滚动或越界浮层。
- [ ] unit/component、Playwright、相关 .NET 测试和真实环境证据按风险完成。
- [ ] 所有证据绑定当前 clean SHA；未运行项诚实记录。
- [ ] 文件白名单、`git diff --stat`、`git diff --check` 和临时产物检查通过。
- [ ] F10 已更新并经过复审；未由自动测试自行授予生产验收。

## 13. 停止条件

出现以下任一情况立即停止当前子项并报告：

- 后端缺少所需 authority、权限、并发身份、文件承载或 reconcile 合同。
- 正式 Project/Flow/GlobalVariables/assets 保存无法进入现有 Application Service 和 `ProjectSaveCoordinator`。
- 需要第二 API、HostBridge、Canvas、EventBus、ServiceRegistry 或保存链才能继续。
- capability 与其他实现 Owner、共享文件或其他 worktree 发生重叠。
- 远端 `studio-ui-next` 历史前进、分叉或与冻结 SHA 不一致。
- 测试依赖真实设备或目标机而环境不具备；此时记录 `NOT PERFORMED`，不写 PASS。

## 14. 下一步唯一动作

- [x] **G0.1-G0.4** 已完成：远端刷新、候选冻结和 stable-only commit 语义矩阵见 F10。
- [x] **G1.1-G1.10** 已完成：请求/写入生命周期、跨工程状态安全、handoff 恢复和 diagnostics 账本已在 `98cb8c7f5` 提交。
- [x] 开始 G2 合同解阻与功能差距决策；GlobalVariables 与 Line Sequence 可实施投影已落地，合同缺口与
  旧版能力处置见 G2 ADR。
- [x] 完成 G2.1-G2.5 处置冻结、延期项重新进入条件和 `98cb8c7f5` 验证；G3 已解锁。
- [x] 完成 G3 产品体验、视觉、中文与 Vue 工程收口；实现与浏览器方向性证据冻结在 `1c6e61e5a`。
- [ ] 开始 G4 Legacy profile 隔离、rollback 与退役准备；保留 Legacy 源码，不提前授予退役批准。
- [x] G1 实现未改变后端 authority、保存链、Runtime/Station、AgentRun，也未引入第二套基础设施。
