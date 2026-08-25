# Studio UI Next Option D 迁机交接与恢复边界

> 本文记录 2026-08-25 当前 dirty worktree 的迁机边界、止血决定和后续恢复顺序。它不是 Gate 通过报告、发布清单或生产签收，也不能替代旧电脑到外部介质或新电脑的实际文件复制。

```text
DOCUMENT_ROLE=WORKTREE_MIGRATION_HANDOFF
DOCUMENT_STATE=ACTIVE_UNCOMMITTED_BACKUP_REQUIRED
SNAPSHOT_DATE=2026-08-25
CURRENT_STATUS_SOURCE=F10_ContractAndProductionPlan.md
CURRENT_BRANCH=studio-ui-next
CURRENT_HEAD=cdd114082821bbe750fb7945a0c3a4e89002d67c
CURRENT_HEAD_SUBJECT=docs(archive): preserve StudioUI parity audit
UPSTREAM=origin/studio-ui-next
TRACKING_REF_STATE=AHEAD_0_BEHIND_0_WITHOUT_FETCH_THIS_STOPGAP
REMOTE_URL=https://github.com/HerverJun/ClearVision.git
REMOTE_FETCH_THIS_STOPGAP=NOT_PERFORMED
WORKTREE_STATE=DIRTY_UNCOMMITTED_PRESERVE
STAGED_FILES=0
TRACKED_MODIFIED_FILES=39
TRACKED_DIFF_STATS=39_FILES_2420_INSERTIONS_839_DELETIONS
TRACKED_DIFF_SHA256=81ea10d68dc721c8e1d95f4572a68e626f90a7ddf18e466e6b2b0fc29443b86e
TRACKED_DIFF_HASH_INPUT=git_diff_binary_no_ext_diff
UNTRACKED_FILES=2454
UNTRACKED_FILENAME_LIST_SHA256=e5ef7b9e7a345face784596363216f9597a3b518a845d2b444de01b8b8396688
UNTRACKED_HASH_INPUT=git_ls_files_others_exclude_standard_z
OPTION_D_G0_STATE=PASS
OPTION_D_G1_STATE=PASS
OPTION_D_G2_STATE=BLOCKED_BY_CONTRACT
OPTION_D_G3_STATE=BLOCKED_BY_DEPENDENCY
OPTION_D_G4_STATE=BLOCKED_BY_DEPENDENCY
OPTION_D_WHOLE_PAGE_VISUAL_AUTHORITY=INVALID_BY_INTERNAL_SHELL_CONFLICT
OPTION_D_GLOBAL_PRODUCT_NAV_TOP_RULE=FROZEN_TOP_ONLY
OPTION_D_LEFT_SIDE_SURFACE_SCOPE=PAGE_INTERNAL_TOOLS_CATEGORIES_OR_CONTEXT_ONLY
OPTION_D_UI_IMPLEMENTATION=PROHIBITED_PENDING_G2_VISUAL_AUTHORITY_AND_PAGE_SCOPE_REFREEZE
UI_OR_PRODUCT_CODE_CHANGED_BY_THIS_STOPGAP=NO
TESTS_THIS_STOPGAP=NOT_RUN_DOCS_ONLY
COMMIT_THIS_STOPGAP=NOT_PERFORMED
PUSH_THIS_STOPGAP=NOT_PERFORMED
STASH_RESET_CLEAN_DELETE_THIS_STOPGAP=NOT_PERFORMED
```

## 1. 本轮止血范围

本轮只做三件事：

1. 在根 `TODO.md`、当前状态源 `F10_ContractAndProductionPlan.md` 和 Option D 总计划中同步 Shell 合同阻断。
2. 冻结全局导航规则，暂停 G2/G3/G4 UI 实施，并保留所有历史 evidence、哈希、阈值和测试数字。
3. 写入本文，作为迁机时的工作区清单和恢复顺序。

本轮没有修改 Vue、CSS、Router、测试脚本、后端、配置或视觉图片；没有删除异常文件或缓存；没有执行 `stash`、`reset`、`clean`、切分支、commit、push 或远端 fetch。

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

## 3. Git 工作区快照

### 3.1 已跟踪改动

当前有 39 个 unstaged tracked 文件，0 个 staged 文件。按类型分为：

| 类型 | 数量 | 范围 |
| --- | ---: | --- |
| 产品前端代码 | 20 | Product Shell、Router/Auth/Page State、Overview/Projects/Operators/About/Diagnostics、Design System、Labs |
| 已跟踪测试 | 13 | StudioUI unit 与 UI Tests Playwright |
| UI Tests 配置 | 2 | `package.json`、`playwright.config.ts` |
| 已跟踪文档 | 4 | Design System README、根 TODO、Parity ADR、F10 |

这些修改均未提交，必须与 `.git` 一起原样迁移。仅重新 clone 当前 HEAD 会丢失全部 39 个文件中的工作区差异。

### 3.2 未跟踪但必须保留

| 类型 | 数量 | 说明 |
| --- | ---: | --- |
| 新增 Auth 单测 | 1 | `StudioUI/tests/unit/auth/authShellPages.spec.ts` |
| Option D fixture/gate/visual 脚本 | 14 | G0-G3 deterministic fixture、gate、metrics、compare 与 visual spec |
| UI Tests lockfile | 1 | `ClearVision.Product/tests/ClearVision.Product.UI.Tests/pnpm-lock.yaml` |
| Option D 状态/证据文档 | 7 | G0-G3 ledger/manifest、G1 token ledger、Option D 总计划 |
| 本迁机交接文档 | 1 | 当前文件 |
| `_visual_master` Git 可见资产 | 1,357 | 约 1,394,043,711 bytes，不能由 clone 恢复 |

`_visual_master/` 的物理视图共有 1,501 个文件、1,394,914,387 bytes；其中还包括被 `.gitignore` 忽略的 139 个 `.log` 和 5 个 `.pyc`。仅看 `git status` 会漏掉这些文件。

### 3.3 Git 不会自然带走的证据与外部依赖

- `_visual_master/` 应优先整体备份。其 `option_D/screens/` 有 24 张 PNG（54,730,913 bytes），`option_D/masters/` 有 3 张 PNG（6,578,875 bytes），`option_D/iterations/` 约 480.8 MB；原始生成输出无法保证字节级重现。
- `_visual_master/workflow/` 包含 6 个生成脚本，并依赖仓库外 `C:\Users\HerverJun\Desktop\ppt`、被忽略的 `.tmp` 输入和外部 `gpt-image-2` / PPT 生成链。若要保留完整生成来源，必须另外迁移外部 `ppt` 目录和相关 `.tmp` 输入；本文不记录 token 或 API key。
- `.tmp/studio-ui-next/**` 被 Git 忽略，历史 WebView2、DPI、Gate manifest、PNG/JSON 和当前视觉输入不会由 clone 恢复。需要保留历史审计能力时，应整体备份 `.tmp/studio-ui-next/`。
- Project 数据库、工程文件、Runtime packages、Results、diagnostics、WebView2 user-data 和设备/Station 配置可能位于仓库外或用户数据目录。迁机前按实际配置单独备份，不把数据库、运行包、结果或诊断日志当作前端缓存。
- 秘密信息只迁移到受控凭据存储；不得把 token、API key、Station credential、明文 secret 写入本文或 Git。

## 4. 可重建内容与疑似残片

### 4.1 可重建，不作为迁机首要资产

- `.pnpm-store/` Git 可见为 1,016 个文件、26,824,449 bytes。其真实 store-owned 内容主要是 `v11/files/` 825 个文件和 `v11/index.db`，可由 package manifests 与 lockfile 重建。
- `.pnpm-store/v11/projects/...` 是指向 UI Tests 目录的 Junction。递归工具会把它计算为 4,681 个重复文件；不要把 Junction 当作独立副本，也不要使用会盲目跟随 reparse point 的镜像/删除操作。
- `node_modules/`、`.pyc` 和普通日志可重建；是否保留日志取决于是否还需要历史 evidence 审计。

### 4.2 疑似命令残片，本轮不删除

以下内容疑似由错误命令或未转义输出产生，当前没有发现产品引用，但迁机止血阶段不做清理：

- 仓库根目录 46 个异常文件，共 205 bytes；45 个为 0-byte，`$null` 为两行 `rg` 错误输出。
- 字面目录 `%SystemDrive%/ProgramData/Microsoft/Windows/Caches/` 下 7 个 `.db`，共 2,825,704 bytes。
- UI Tests 目录下 2 个 `console.log...` 异常文件。
- `docs/进行中/StudioUINext/` 下 2 个 0-byte `n.endsWith...` 命令残片。
- `_visual_master/workflow/__pycache__/`、`workflow/tmp/` 和被忽略日志须在完成 evidence 价值判断后再清理。

后续清理必须先逐个确认引用、解析实际路径，并特别避开 Junction；禁止用 `git clean` 或递归删除一次性处理。

## 5. 迁机前必须完成的外部备份

本文位于同一个 dirty worktree 内，因此“文档存在”不等于已经完成异机备份。关闭旧电脑前至少完成：

- [ ] 停止 Desktop Host、开发服务器和可能写入 Project/Results/evidence 的进程。
- [ ] 完整复制当前仓库目录，并确认隐藏的 `.git` 被包含；不要只 clone HEAD。
- [ ] 整体复制 `_visual_master/`；空间不足时也必须保留 `option_D/screens/`、`masters/`、`visual_constitution.md`、`image_prompts.json`、`functional_remapping.json`、`manifest.json`、canonical FlowCanvas 审计文件和 6 个 workflow 脚本。
- [ ] 按是否需要复核历史证据决定是否整体复制 `.tmp/studio-ui-next/`；至少保留当前 Option D invocation 的 manifest、reference/candidate/diff/overlay 和输入源。
- [ ] 单独复制仓库外 `C:\Users\HerverJun\Desktop\ppt`（若仍需复现生成链）以及实际 Project/数据库/Runtime package/Results/diagnostics 路径。
- [ ] 确认 39 个 tracked 修改、全部未跟踪关键文件和 UI Tests `pnpm-lock.yaml` 都出现在备份中。
- [ ] 对备份包或目标目录记录总字节数和 SHA-256/文件清单；不要把凭据内容写入清单。
- [ ] 在旧电脑仍可用时，于新位置执行只读抽查并打开本文、F10、Option D 总计划和至少一张 raw PNG。

不建议对整个仓库使用 `/MIR` 或任何会删除目标多余文件、跟随 Junction 的命令。若迁移工具无法明确处理 reparse point，排除 `.pnpm-store/` 并在新电脑重建依赖。

## 6. 新电脑恢复顺序

1. 恢复完整仓库目录和 `.git`，先不要运行 checkout、pull、stash、reset、clean 或格式化工具。
2. 核对分支、HEAD、upstream、tracked diff、untracked 文件数量及本文记录的两个 SHA-256；远端是否前进须另行 fetch 后判断，本文的 ahead/behind 只基于旧电脑现有 tracking ref。
3. 核对 `_visual_master/`、Option D 7 份历史文档、14 个测试/门禁脚本、新 Auth 单测和 UI Tests lockfile。
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
- 本轮没有改 UI，因此没有运行 lint、typecheck、unit、Playwright、WebView2 或 .NET 测试；这些项目均为 `NOT RUN`，不是 PASS。
- 本轮只应执行文档状态一致性、链接、Git 快照与 `git diff --check` 检查。

## 9. 当前状态入口

- 当前状态源：[F10_ContractAndProductionPlan.md](./F10_ContractAndProductionPlan.md)
- 执行摘要：[根 TODO](../../../TODO.md)
- Option D 总计划：[Studio_UI_Next_Option_D_像素级复刻与功能完整继承计划_PROPOSED_AUDITED.md](./Studio_UI_Next_Option_D_像素级复刻与功能完整继承计划_PROPOSED_AUDITED.md)
- 历史 G2 证据：[OptionD_G2_EvidenceManifest.md](./OptionD_G2_EvidenceManifest.md)
- 历史 G3 证据：[OptionD_G3_EvidenceManifest.md](./OptionD_G3_EvidenceManifest.md)

迁机恢复时先读本文和 F10，再读总计划与历史 manifest；不要从历史 PASS 或 raw 图片直接恢复 UI 实施。
