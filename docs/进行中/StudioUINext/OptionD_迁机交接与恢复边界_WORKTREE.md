# Studio UI Next Option D 迁机交接与恢复边界

> 本文记录 2026-08-25 迁机止血后的 Git 保全结果、结构冲突边界和后续恢复顺序。它不是 Gate 通过报告、发布清单或生产签收；远端 Git 只保全本文明确列出的源码、合同和最小视觉筛选集，不能替代被忽略运行数据的独立文件备份。

```text
DOCUMENT_ROLE=WORKTREE_MIGRATION_HANDOFF
DOCUMENT_STATE=REMOTE_GIT_CHECKPOINT_READY
SNAPSHOT_DATE=2026-08-25
CURRENT_STATUS_SOURCE=F10_ContractAndProductionPlan.md
CURRENT_BRANCH=studio-ui-next
PRESERVATION_BASE_HEAD=cdd114082821bbe750fb7945a0c3a4e89002d67c
PRESERVATION_COMMIT=98e669d3fc925d3ff17837476c33402e19d5c9d5
PRESERVATION_COMMIT_SUBJECT=chore(studio-ui):preserve-blocked-option-d-checkpoint
FINAL_HANDOFF_COMMIT=THIS_DOCUMENT_COMMIT
UPSTREAM=origin/studio-ui-next
TRACKING_REF_STATE=AHEAD_0_BEHIND_0_AFTER_FINAL_PUSH
REMOTE_URL=https://github.com/HerverJun/ClearVision.git
REMOTE_FETCH_BEFORE_COMMIT=PERFORMED_2026-08-25
WORKTREE_STATE=CLEAN_AFTER_FINAL_HANDOFF_COMMIT
STAGED_FILES=0
TRACKED_MODIFIED_FILES=0
UNTRACKED_FILES=0
PRESERVED_CHECKPOINT_FILES=333
PRESERVED_CHECKPOINT_DIFF_STATS=333_FILES_58475_INSERTIONS_839_DELETIONS
PRESERVED_OPTION_D_PNG_FILES=31
PRESERVED_OPTION_D_PNG_BYTES=64760391
PRESERVED_OPTION_D_MAX_PNG_BYTES=3537267
REMOVED_UNTRACKED_TARGETS=253
REMOVED_ROOT_PNPM_STORE=PERFORMED_AFTER_JUNCTION_DETACH
IGNORED_LOCAL_EVIDENCE_IN_GIT=NO
OPTION_D_G0_STATE=PASS
OPTION_D_G1_STATE=PASS
OPTION_D_G2_STATE=BLOCKED_BY_CONTRACT
OPTION_D_G3_STATE=BLOCKED_BY_DEPENDENCY
OPTION_D_G4_STATE=BLOCKED_BY_DEPENDENCY
OPTION_D_WHOLE_PAGE_VISUAL_AUTHORITY=INVALID_BY_INTERNAL_SHELL_CONFLICT
OPTION_D_GLOBAL_PRODUCT_NAV_TOP_RULE=FROZEN_TOP_ONLY
OPTION_D_LEFT_SIDE_SURFACE_SCOPE=PAGE_INTERNAL_TOOLS_CATEGORIES_OR_CONTEXT_ONLY
OPTION_D_UI_IMPLEMENTATION=PROHIBITED_PENDING_G2_VISUAL_AUTHORITY_AND_PAGE_SCOPE_REFREEZE
UI_IMPLEMENTATION_AFTER_SHELL_FREEZE=NO
STUDIOUI_LINT=PASS
STUDIOUI_TYPECHECK=PASS
STUDIOUI_UNIT=PASS_963_OF_963
UI_TESTS_UNIT_INITIAL=FAIL_13_ENOBUFS_WITH_64_MIB_BINARY_DIFF
UI_TESTS_UNIT_AFTER_COMMIT=PASS_1046_OF_1046
PLAYWRIGHT_WEBVIEW2_DPI_HARDWARE=NOT_RUN
COMMIT_THIS_STOPGAP=PERFORMED
PUSH_THIS_STOPGAP=VERIFIED_AFTER_THIS_DOCUMENT_COMMIT
GIT_CLEAN_UNTRACKED_THIS_STOPGAP=PERFORMED
STASH_RESET_THIS_STOPGAP=NOT_PERFORMED
```

## 1. 本轮止血范围

本轮迁机止血完成四件事：

1. 在根 `TODO.md`、当前状态源 `F10_ContractAndProductionPlan.md` 和 Option D 总计划中同步 Shell 合同阻断。
2. 冻结全局导航规则，暂停 G2/G3/G4 UI 实施，并保留所有历史 evidence、哈希、阈值和测试数字。
3. 将原工作区的源码、测试、合同和最小 Option D 筛图集提交为 `98e669d3fc925d3ff17837476c33402e19d5c9d5`。
4. 删除未保留的 Git-visible 生成物与异常残片，断开 Junction 后删除根级 `.pnpm-store/`，并把最终恢复边界写回本文。

Shell 冲突冻结后没有继续修改 Vue、CSS、Router 或后端；提交中包含的是冻结前已经存在的 UI/测试工作区改动。没有执行 `stash`、`reset` 或切分支。未跟踪清理只在保全提交和删除预演完成后执行。

## 2. 当前结论：存在结构性冲突

这批改动面对的是 P1 级 Shell / 信息架构冲突，不只是“画得不好看”。当前没有发现第二个 mounted Product Shell owner，问题是同一个 Shell 的视觉合同和导航层级互相冲突。

代码事实：

- 当前 `ProductLayout` 支持 `standard`、`workspace`、`product-rail` 三种 Shell mode：`StudioUI/src/app/layouts/ProductLayout.vue:32`。
- AI、Workspace 和 Settings 路由使用左侧全局 product rail：`StudioUI/src/app/router.ts:108`、`:127`、`:137`、`:208`。
- 左侧 product rail 的 DOM 位于 `ProductLayout.vue:285` 附近；对应 CSS 会隐藏顶部产品主导航：`StudioUI/src/app/layouts/product-layout.css:322-325`。
- 旧正式前端的全局主导航位于顶部：`wwwroot/index.html:76-101`；算子 rail、Inspector、Preview 位于页面内部 `main-content`，不是全局导航。
- 当前 Workspace 内部的命令条、算子发现、Canvas、Inspector 和 Preview 仍是页面内部结构；Settings 自身也有独立的设置分组导航，不能与全局 product rail 混同。

图片事实：

- D02-D04 使用顶部全局导航。
- D05-D08 同时包含左侧全局 product rail 与 Workspace 内部工具/上下文区；后者需要保留，前者违反当前冻结规则。
- D13-D14 使用左侧全局 product rail，但 AI 三分区和恢复上下文仍可作为页面内部候选。
- D16-D21 使用左侧全局 product rail，同时还有 Settings 页面内分组 rail；只允许保留后者。
- 因此 24 张 raw whole-page Master 不能继续作为一组唯一像素权威。历史图片及 SHA 不删除、不覆盖，只降级为待筛选资产和历史证据。

当前冻结规则：

> 全局产品导航始终位于顶部；左侧只能出现页面内部的工具、分类或上下文面板，不能替代或重复全局导航。

当前代码中的 `shellMode: 'product-rail'` 是受此前 Option D 方向影响留下的实现事实，不代表最终规划批准。删除或改写这些 mode 属于后续 UI 工作，本轮禁止执行。

## 3. 迁机前快照与最终保全结果

### 3.1 已跟踪改动

清理前有 39 个 unstaged tracked 文件，0 个 staged 文件。按类型分为：

| 类型 | 数量 | 范围 |
| --- | ---: | --- |
| 产品前端代码 | 20 | Product Shell、Router/Auth/Page State、Overview/Projects/Operators/About/Diagnostics、Design System、Labs |
| 已跟踪测试 | 13 | StudioUI unit 与 UI Tests Playwright |
| UI Tests 配置 | 2 | `package.json`、`playwright.config.ts` |
| 已跟踪文档 | 4 | Design System README、根 TODO、Parity ADR、F10 |

这些修改当时均未提交；现已包含在保全提交 `98e669d3fc925d3ff17837476c33402e19d5c9d5` 中。重新 clone 并 checkout 最终 `origin/studio-ui-next` 可以恢复，不再依赖旧电脑的 dirty worktree。

### 3.2 从未跟踪内容中选入 Git 的保留集

| 类型 | 数量 | 说明 |
| --- | ---: | --- |
| 新增 Auth 单测 | 1 | `StudioUI/tests/unit/auth/authShellPages.spec.ts` |
| Option D fixture/gate/visual 脚本 | 14 | G0-G3 deterministic fixture、gate、metrics、compare 与 visual spec |
| UI Tests lockfile | 1 | `ClearVision.Product/tests/ClearVision.Product.UI.Tests/pnpm-lock.yaml` |
| Option D 状态/证据文档 | 7 | G0-G3 ledger/manifest、G1 token ledger、Option D 总计划 |
| 本迁机交接文档 | 1 | 当前文件 |
| `_visual_master` 最小 Git 保留集 | 269 | 238 个文本合同/元数据；31 张 PNG 共 64,760,391 bytes，最大单文件 3,537,267 bytes |

保全提交共 333 个文件、58,475 行新增、839 行删除。`_visual_master/` 未整体进入 Git：仅保留恢复合同、24 张 Option D raw screen、3 张 Master、1 张 contact sheet 和 3 张 Flow blueprint；其余候选图、comparison、iteration、crop 和临时 board 已在预演后删除。

### 3.3 Git 不会自然带走的证据与外部依赖

- 已提交的 `_visual_master/option_D/screens/` 有 24 张 PNG（54,730,913 bytes），`option_D/masters/` 有 3 张 PNG（6,578,875 bytes）；raw whole-page 只用于后续筛选，不恢复为像素权威。未提交的 `iterations/` 等生成输出已经清理。
- `_visual_master/workflow/` 的生成脚本和文本合同已进入 Git，但仓库外 `C:\Users\HerverJun\Desktop\ppt`、被忽略输入和外部 `gpt-image-2` / PPT 生成链未进入 Git；本文不记录 token 或 API key。
- `.tmp/studio-ui-next/**` 被 Git 忽略。本轮只读统计约 27,264 个文件、3,948,106,264 bytes（不含其中的 `node_modules`），包含历史 WebView2/DPI/Playwright 结果、`vision.db`、trace 和归档；本轮未删除，也未备份到远端 Git。格式化旧电脑前如仍需历史审计，必须另行复制。
- `ClearVision.Product/.../bin/.../App_Data/ProjectFlows` 中仍有被忽略的 Flow JSON；Project 数据库、Runtime packages、Results、diagnostics、WebView2 user-data 和设备/Station 配置也可能位于仓库外。本轮均未声明已备份，不能把它们当作普通前端缓存。
- 秘密信息只迁移到受控凭据存储；不得把 token、API key、Station credential、明文 secret 写入本文或 Git。

## 4. 可重建内容与疑似残片

### 4.1 可重建，不作为迁机首要资产

- 根 `.pnpm-store/` 已加入 `.gitignore`。删除前确认 `v11/projects/...` 是指向 UI Tests 的 Junction；先只移除 Junction 并确认目标 `package.json` 仍在，再删除 store 父目录。依赖可由 package manifests 与 lockfile 重建。
- `node_modules/`、`.pyc` 和普通日志可重建；是否保留日志取决于是否还需要历史 evidence 审计。

### 4.2 已确认并删除的未跟踪残片

以下内容由 `git clean -nd` 预演确认不含真实源码/文档扩展名，已通过 `git clean -fd` 删除：

- 仓库根目录 46 个异常文件，共 205 bytes；45 个为 0-byte，`$null` 为两行 `rg` 错误输出。
- 字面目录 `%SystemDrive%/ProgramData/Microsoft/Windows/Caches/` 下 7 个 `.db`，共 2,825,704 bytes。
- UI Tests 目录下 2 个 `console.log...` 异常文件。
- `docs/进行中/StudioUINext/` 下 2 个 0-byte `n.endsWith...` 命令残片。
- Git-visible 的 `workflow/tmp/` 已删除；被忽略的 `_visual_master/workflow/__pycache__/` 和日志未纳入 Git 保全，可重建。

本轮共删除 253 个 `git clean` 目标。被忽略的 `.tmp/studio-ui-next`、ProjectFlows 和其他运行数据没有随 `git clean -fd` 删除。

## 5. 迁机前仍需决定的非 Git 备份

源码、测试、合同和最小视觉筛选集已通过远端 Git 保全。以下内容不属于 Git 工作区干净性的组成部分；若仍有取证或业务价值，格式化前必须单独迁移：

- [ ] 停止 Desktop Host、开发服务器和可能写入 Project/Results/evidence 的进程。
- [x] 将 333 文件的保全提交和最终交接提交推送至 `origin/studio-ui-next`。
- [x] 将 `option_D/screens/`、`masters/`、contact sheet、3 张 Flow blueprint、视觉合同/manifest 和 workflow 脚本纳入 Git。
- [ ] 按是否需要复核历史证据决定是否整体复制 `.tmp/studio-ui-next/`；它未进入 Git，且不是全部可字节级重建。
- [ ] 单独复制仓库外 `C:\Users\HerverJun\Desktop\ppt`（若仍需复现生成链）以及实际 Project/数据库/Runtime package/Results/diagnostics 路径。
- [x] 确认原 39 个 tracked 修改、关键新增文件和 UI Tests `pnpm-lock.yaml` 均在保全提交中。
- [ ] 对任何额外的非 Git 备份包记录总字节数和 SHA-256/文件清单；不要把凭据内容写入清单。
- [ ] 若制作额外备份，在旧电脑仍可用时于新位置执行只读抽查。

不建议对整个仓库使用 `/MIR` 或任何会删除目标多余文件、跟随 Junction 的命令。若迁移工具无法明确处理 reparse point，排除 `.pnpm-store/` 并在新电脑重建依赖。

## 6. 新电脑恢复顺序

1. 从远端 clone/fetch 后 checkout `studio-ui-next`，核对 upstream 为 `origin/studio-ui-next`。
2. 核对历史中包含保全提交 `98e669d3fc925d3ff17837476c33402e19d5c9d5`，并确认 `git status --short --branch` 无文件项、ahead/behind 为 `0/0`。
3. 核对 `_visual_master/` 最小保留集、Option D 7 份历史文档、14 个测试/门禁脚本、新 Auth 单测和 UI Tests lockfile。
4. 恢复仓库外 Project/数据库/Runtime/Results/diagnostics 与受控凭据；路径变化时先更新本机配置，不把旧绝对路径写进产品源码。
5. 通过 lockfile 重建 Node/pnpm 依赖；不要迁移 `.pnpm-store` Junction。按根 `global.json` 和仓库脚本恢复 .NET SDK/依赖。
6. 在恢复任何 UI 实施前，先按下一节完成筛图和版本化视觉合同冻结。
7. 只有 G2 解除 `BLOCKED_BY_CONTRACT` 后，才允许重新建立 visual invocation、功能回归和 cleanup 证据；G3/G4 仍按依赖顺序串行解锁。
8. 软件测试、Chromium、真实 WebView2、Windows 125%、no-Node、Remote CI 和现场硬件分别取证，不以任一类别替代另一类别。

## 7. 后续未尽事项

### 7.1 先筛图，再冻结视觉合同

- [ ] 对 24 张 raw 图片逐张标记 `KEEP_WHOLE_PAGE`、`KEEP_PAGE_INTERNAL_ONLY`、`REPLACE_SHELL` 或 `DROP`。
- [ ] D02-D04 可作为顶部全局导航候选，但仍须核对入口集合、顺序和页面范围。
- [ ] D05-D08 只保留 Workspace 内部算子发现、命令层、Canvas、Inspector、Preview/ROI 与 modal 关系；左侧全局 product rail 不保留。
- [ ] D13-D14 只保留 AI 工作区内部三分区、恢复/历史/诊断上下文；左侧全局 product rail 不保留。
- [ ] D16-D21 保留 Settings 页面内分组导航和各设置工作区；左侧全局 product rail 不保留。
- [ ] 对 D01/D24 的最小壳例外及 D09-D12、D15、D22-D23 的 Shell 逐页确认，不从旧 24 页范围自动继承。
- [ ] 生成新的版本化图片清单、页面范围、Shell 边界、SHA、阈值、Owner 和签字；不得覆盖历史 raw PNG 或篡改历史 manifest。

### 7.2 再处理当前代码

- [ ] 由 Shell 单一 Owner 评估 `router.ts` 中 AI、Workspace、Settings 的 `shellMode`，以及 `ProductLayout.vue` / `product-layout.css` 的 product rail 与隐藏顶部导航行为。
- [ ] 保持一个 Product Shell owner；不得把页面内部 rail 提升为第二全局导航，也不得让顶部导航与左侧全局导航重复出现。
- [ ] Workspace 的 OperatorRail、Inspector、Preview/ROI 和 SettingsGroupNavigation 必须按页面内部 owner 保留，不能因移除 global rail 而误删。
- [ ] 更新 G2 capability relocation/progressive-disclosure map、路由/角色/profile/flag admission 和键盘入口。
- [ ] 新建 G2 invocation；完成新视觉权威下的全页比较、受影响功能回归、owner cleanup、独立 review 和状态回写。
- [ ] G2 PASS 后才恢复 G3；G4 继续等待 G3，不得并行绕过。

### 7.3 仍未完成的外部门禁

- Windows 125% 真实 WebView2：`NOT_PERFORMED`。
- 独立无 Node 目标机：`NOT_PERFORMED`。
- 当前 HEAD Remote CI clean checkout：`NOT_RUN / BLOCKED_BY_ENVIRONMENT`。
- 真实 Camera、PLC、TCP、Station、Inspection、AI provider：`NOT_PERFORMED`。
- 长时间生产 soak 与产品 Owner 签收：`NOT_PERFORMED`。
- `PRODUCTION_ACCEPTANCE=NOT_GRANTED`，`LEGACY_RETIREMENT=NOT_APPROVED`。

## 8. 历史证据边界

- `OptionD_G0_EvidenceManifest.md`、`OptionD_G1_EvidenceManifest.md`、`OptionD_G2_EvidenceManifest.md` 和 `OptionD_G3_EvidenceManifest.md` 记录各自 invocation 的历史证据，不修改其中的 PASS/FAIL 数字、Master SHA、diff ratio、阈值或 artifact hash。
- G2 manifest 中的历史 `REOPENED_IN_PROGRESS`、G3 manifest 中的 raw whole-page sole-authority 文字已被 2026-08-25 的 F10 当前止血决定取代；恢复时不得把 manifest 顶部旧状态当作当前执行授权。
- Shell 冲突冻结后没有继续改 UI；保全提交包含冻结前原有的 UI 改动。
- StudioUI `lint`、`typecheck` 和 unit（963/963）通过。UI Tests 首轮因 64 MiB 以上的 staged binary diff 触发 `spawnSync git ENOBUFS`；提交后同一入口重跑为 1,046/1,046 通过，证明它是 dirty diff 体积问题而非断言失败。
- Playwright 视觉执行、真实 WebView2、Windows 125%、.NET、Remote CI 和现场硬件均未运行，不能写成 PASS。
- `git diff --cached --check` 在历史 audit Markdown 的显式硬换行和少量历史文件 EOF 空行上报格式告警；为保持历史 evidence 原文，本轮未改写这些证据文件。

## 9. 当前状态入口

- 当前状态源：[F10_ContractAndProductionPlan.md](./F10_ContractAndProductionPlan.md)
- 执行摘要：[根 TODO](../../../TODO.md)
- Option D 总计划：[Studio_UI_Next_Option_D_像素级复刻与功能完整继承计划_PROPOSED_AUDITED.md](./Studio_UI_Next_Option_D_像素级复刻与功能完整继承计划_PROPOSED_AUDITED.md)
- 历史 G2 证据：[OptionD_G2_EvidenceManifest.md](./OptionD_G2_EvidenceManifest.md)
- 历史 G3 证据：[OptionD_G3_EvidenceManifest.md](./OptionD_G3_EvidenceManifest.md)

迁机恢复时先读本文和 F10，再读总计划与历史 manifest；不要从历史 PASS 或 raw 图片直接恢复 UI 实施。
