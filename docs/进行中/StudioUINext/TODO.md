# Studio UI Next 能力补齐开发 TODO

```text
DOCUMENT_STATE=READY_FOR_EXECUTION
BRANCH=studio-ui-next
BASELINE_HEAD=68e6e4286d008433f804ef90de00c8017184c177
BASELINE_DATE=2026-08-07
SOURCE_OF_TRUTH=CURRENT_CODE_AND_CURRENT_CONFIG
IMPLEMENTATION_POLICY=ONE_WORK_PACKAGE_AT_A_TIME
COMMIT_POLICY=NO_COMMIT_OR_PUSH_UNTIL_REVIEWED
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

## 1. 文档用途

本文档把当前代码与 Legacy 对照调研中确认的迁移回归和能力缺口，整理为可逐批实现、验证和复审的开发清单。它不是完成报告，也不继承旧文档中的笼统 `MIGRATED` 结论。

执行规则：

- 每次只打开一个 work package；完成、验证、复审后再进入下一包。
- `[x]` 只表示已经由当前代码或本轮只读审计确认；`[ ]` 表示尚未完成或尚未取得当前 HEAD 的证据。
- 当前代码、配置和测试结果优先于历史文档、旧截图、旧 SHA 和过去的 PASS。
- Browser Playwright 不能替代真实 WebView2、Windows 125% DPI、独立 no-Node、现场硬件或生产 soak。
- 未实际运行的验证必须写成 `NOT RUN` 或 `NOT PERFORMED`，不得推断为通过。
- 不提交、不推送；每个工作包完成后等待人工复审。

## 2. 当前工作树基线

开始本文档时已确认：

- 分支：`studio-ui-next`
- HEAD：`68e6e4286d008433f804ef90de00c8017184c177`
- 与远端关系：`ahead 37`
- 已存在 4 个未提交文件，属于 WP0 的 Flyout / Inspector 定向修复，不得被后续工作覆盖或回滚：
  - `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/flow/FlowWorkspace.vue`
  - `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/flow/OperatorRail.vue`
  - `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/tests/unit/capabilities/project-workspace/operatorRail.spec.ts`
  - `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f03-workspace.spec.ts`
- 上述现有 diff：`4 files changed, 213 insertions(+), 17 deletions(-)`。
- 本文档以外的后续实现不得混入这 4 个文件，除非仍在完成 WP0。

每次开始工作包前执行并记录：

```powershell
git branch --show-current
git rev-parse HEAD
git status --short --branch
git diff --stat
```

若分支不是 `studio-ui-next`、HEAD 非预期、出现新的重叠修改或远端历史不一致，停止并报告；不得切分支、stash、reset、clean 或覆盖用户修改。

## 3. 已确认事实与差距台账

### 3.1 文件选择问题的最终解释

- [x] 后端 `FilePickerMessageHandler`、`PickFileCommand`、`FilePickedEvent` 仍存在。
- [x] Studio UI Next 的唯一通用 `StudioHostAdapter` 仍可发送和订阅 WebView2 消息。
- [x] Inspector 已识别 `dataType=file`，但 `parameterEditorRegistry.ts` 只返回 `extensionSlot='file-picker'` 和“文件选择器尚未接入当前工作区”的占位文案。
- [x] Next 缺少 picker-specific port/owner、请求关联、取消/销毁处理，以及 Inspector 和 AI 对该窄端口的接入。
- [x] 因此该提示表示“Next 前端尚未把现有 Host 文件选择合同接到当前 capability”，不是 Windows 文件对话框、算子 metadata 或后端 API 故障。
- [x] 现有消息合同没有 `requestId`，只用 `ParameterName` 关联结果；WP1 必须在前端串行化选择请求，不能假装支持无法可靠关联的并发 picker。

### 3.2 确认缺失或回归

| ID | 优先级 | 能力 | 当前代码事实 | 目标工作包 |
| --- | --- | --- | --- | --- |
| GAP-001 | P1 | Operator Flyout 生命周期 | Flyout 原由 Rail 私有持有并 absolute 覆盖 Inspector；已有未提交候选修复，尚待完整验证 | WP0 |
| GAP-002 | P1 | Inspector 文件参数 | `file` 仅显示占位扩展，不可选择或提交路径 | WP1 |
| GAP-003 | P1 | path-like `string` 兼容 | 仅 `dataType=file` 进入文件扩展；Legacy 兼容的路径字段可能退化为普通文本 | WP1 |
| GAP-004 | P2 | `dataType=color` | Registry 没有颜色编辑器，落入 unsupported | WP1 |
| GAP-005 | P2 | 参数恢复默认值 | 当前 Esc 只撤销本地草稿；缺少显式恢复 metadata 默认值的完整语义 | WP1 |
| GAP-006 | P1 | AI 附件 | Next 任务输入没有 Legacy 的本地附件选择链 | WP2 |
| GAP-007 | P1 | AI 待补文件参数 | Pending Parameters 仅提供标量输入，文件型参数没有 picker | WP2 |
| GAP-008 | P1 | AI 模型/模板/标定资源绑定 | 当前仅相机绑定可选，其他资源明确保持阻断 | WP2 |
| GAP-009 | P1 | 流程模板 | 缺少模板搜索、应用、另存和更新的 Next 工作流 | WP3-A |
| GAP-010 | P1 | 工程 JSON 导入/导出 | Next 工程生命周期没有对应入口和受控 owner | WP3-B |
| GAP-011 | P2 | 示例工程与引导 | 仍留在 Legacy fallback；现有 demo 写入不具备 Next lifecycle reconcile | WP3-C |
| GAP-012 | P1 | N 点标定工作台 | Next 仅有局部 draft/ROI 能力，没有完整标定任务闭环 | WP4-A |
| GAP-013 | P1 | 二维平面比例偏移标定 | 缺少 Legacy 的完整向导和正式资产保存链 | WP4-B |
| GAP-014 | P1 | 全局变量运行值控制 | 已有定义草稿/保存，缺少运行值手动写入、单项重置、全部重置 | WP5-A |
| GAP-015 | P1 | 结果分析与整批导出 | 缺少趋势、缺陷分布、整批 JSON/CSV 报告导出 | WP5-B |
| GAP-016 | P2 | 设置恢复默认 | 已有设置保存 owner，缺少完整的恢复默认操作语义 | WP6-C |
| GAP-017 | P1 | 数据库高级维护 | Next 明确只做 status/backup；repair/restore/cleanup/global reset 延期 | WP6-C |
| GAP-018 | P1 | 线序预览分析与自动调参 | 缺少预览分析和一键自动调参闭环 | WP6-A |
| GAP-019 | P1 | Station 测试运行包 | 已有普通 Runtime Package 导出，缺少生成并下发 Station 测试包的工作流 | WP6-B |

### 3.3 不应误报为普通功能缺失

- RuntimePreview Pilot 是 developer-only 产品决策，不在本轮用户能力补齐范围。
- 存储目录选择和“立即清理”在 Legacy 本身就是 unavailable，不按迁移回归实现。
- Operator 对正式运行和写操作只读是权限设计，不得由前端放宽。
- WebView2 125%、独立 no-Node、现场 Camera/PLC/Station 和 soak 是验收证据缺口，不等同于 Vue 页面缺失。
- Legacy 物理退役、production cutover 和 fallback 删除不属于本 TODO 的能力实现阶段。

### 3.4 文档漂移

- [x] `F09_G1_LegacyNext终局能力矩阵.md` 把 Inspector、全局变量、结果等能力整体标为 `MIGRATED`。
- [x] 当前代码证明这些 capability 的主体 owner 已迁移，但上表中的细分用户任务仍缺失。
- [ ] WP0 先把矩阵状态改为细粒度事实：主体已迁移、列出的操作为 `PARTIAL` / `MISSING` / `DEFERRED`。
- [ ] 后续每个工作包只更新自己关闭的行，禁止一次性把整个 capability 标成完成。

## 4. 强制架构边界

以下条件适用于所有工作包，任何一项不满足都不得合入：

- Project、Flow、GlobalVariables 和正式 Project assets 的权威仍在后端；正式保存最终进入 `ProjectSaveCoordinator`。
- 不新增第二 API transport、HostBridge、EventBus、ServiceRegistry、Canvas 内核或 Project save client。
- Vue 组件不直接订阅或长期持有 WebView2、FlowCanvas、ImageCanvas、EventSource、AbortController 等命令式对象。
- 每个 capability 同时只能有一个 mounted owner、一个订阅集合和一个写入口；unmount 必须 dispose。
- File picker 在 `StudioHostAdapter` 之上建立唯一窄 `FilePickerPort`；Inspector 和 AI 只能调用该 port，不能各自订阅 WebView2。
- File picker 复用现有 `PickFileCommand` / `FilePickedEvent`，不修改后端消息合同；同一时刻只允许一个 in-flight 请求。
- Inspector 的 picker 结果仍通过现有 `InspectorOwner.patchNodeParameter` 写入 canonical Flow 草稿，不复制参数校验和依赖规则。
- 模板应用只修改 canonical FlowCanvas 草稿；由用户显式保存，不能把模板缓存冒充正式保存。
- 工程导入必须经过 Project lifecycle，并复用 `ProjectSaveCoordinator`；不得复制 Legacy 私有 repository write。
- 全局变量“定义保存”和“运行值写入”是两种不同语义，不能共用一个前端 mutation。
- 数据库维护和 Station 下发属于高风险命令：必须保留后端权限、二次确认、pending/unknown-outcome/reconcile 和审计反馈。
- 发现现有后端合同不足时，先形成 contract-gap 记录并停止该子项；不得由前端自行扩权或持久化。

## 5. Owner 与串并行矩阵

| Work package | 唯一实现 owner | 可否与其他包并行 | 关键原因 |
| --- | --- | --- | --- |
| WP0 | `OWN-WORKSPACE` | 否 | 当前 dirty 文件和 Flow/Inspector 交互必须先独立收口 |
| WP1 | `COORD-HOST-WORKSPACE` | 否 | 同时触及共享 Host composition 与 Inspector |
| WP2 | `OWN-AI` | WP1 完成后执行 | 依赖唯一 FilePickerPort；不得建立 AI 私有 bridge |
| WP3-A/B/C | `OWN-PROJECT-LIFECYCLE` | 三批串行 | 共用 Project 生命周期和保存权威 |
| WP4-A/B | `OWN-CALIBRATION-WORKSPACE` | 两批严格串行 | Calibration、FlowCanvas、Inspector、Preview、assets 不得拆 owner |
| WP5-A | `OWN-WORKSPACE-PERSISTENCE` | 否 | Project save 与 GlobalVariables 不得并行拆分 |
| WP5-B | `OWN-RESULTS` | WP5-A 复审后执行 | 读投影/导出独立，但仍单独复审 |
| WP6-A | `OWN-LINE-SEQUENCE` | WP5 后执行 | 复用现有设备/预览权威 |
| WP6-B | `OWN-STATION-DEPLOYMENT` | WP6-A 复审后执行 | 命令终态和 unknown outcome 风险高 |
| WP6-C | `OWN-SETTINGS` | WP6-B 复审后执行 | 高风险数据库命令单独收口 |
| WP7 | `COORD-FINAL-EVIDENCE` | 否 | 统一执行最终验证、真实宿主证据和文档闭合 |

禁止把以下组合拆给多个并行实现者：

- FlowCanvas + Inspector + Preview + Calibration
- Project lifecycle + Project save + GlobalVariables
- bootstrap/router/providers/HostBridge
- 同一 `.csproj` 的测试

## 6. 依赖顺序

```text
WP0 Flyout 回归收口
  -> WP1 FilePickerPort + Inspector 参数兼容
    -> WP2 AI 文件与资源能力
    -> WP3-A 模板 -> WP3-B 导入导出 -> WP3-C 示例工程
      -> WP4-A N 点标定 -> WP4-B 二维平面标定
        -> WP5-A 全局变量运行值 -> WP5-B 结果分析/导出
          -> WP6-A 线序调参 -> WP6-B Station 测试包 -> WP6-C 设置高级维护
            -> WP7 Final Evidence 与文档闭合
```

WP1 完成后，WP2 与 WP3 理论上文件不重叠，但本计划仍按单包执行，避免共享 composition root、Project 上下文和 host 生命周期发生隐式竞争。

## 7. WP0：Operator Flyout / Inspector 回归收口

**目标**：完成当前 4 个 dirty 文件中的定向修复，恢复“打开算子库 -> 添加/选择 -> Flyout 退出 -> Inspector 立即可见”的工作流。

**当前候选设计**：

- [x] `FlowWorkspace.vue` 上移 `operatorFlyoutOpen`，成为临时面板生命周期协调者。
- [x] `OperatorRail.vue` 改为受控 `flyoutOpen` prop + `update:flyoutOpen` event；Rail 只保留分类、搜索、收藏、最近使用和拖拽的局部状态。
- [x] 添加算子成功后关闭 Flyout；命令失败时不误关，保留反馈上下文。
- [x] 画布 pointer/drop、窄屏 Inspector 打开和临时面板 Escape 会关闭 Flyout。
- [x] 重复点击当前分类可 toggle；Escape 关闭后焦点回到触发分类。
- [x] 不通过提高 Inspector z-index 解决遮挡。

**待办**：

- [ ] 审查 selection、pointer capture、drop 和键盘事件顺序，确认不会影响节点拖动、连线、ROI 或画布快捷键。
- [ ] 确认点击添加、拖拽添加、节点选择、连线选择均只关闭临时 Flyout，不改变 canonical selection。
- [ ] 确认搜索、分类、收藏、最近使用、兼容项和拖拽 payload 无回归。
- [ ] 确认 1920x1080 桌面 Inspector 的中心采样点命中 Inspector，而不是 Flyout。
- [ ] 确认窄屏 Flyout 保持在 viewport 内，Escape 第一层关闭 Flyout、第二层关闭 Rail。
- [ ] 运行相关 unit、typecheck、build 和 F03 Playwright。
- [ ] 单独汇报根因、状态 ownership、可见性证据、测试结果、diff stat 和工作树状态，等待复审。

**必须通过的回归场景**：

- [ ] A：打开分类 -> 添加 fixture 算子 -> 节点新增且选中 -> Flyout unmount -> Inspector `node` mode -> 参数控件可见且可命中。
- [ ] B：已有节点/连线 -> 打开 Flyout -> 点击对象 -> Flyout unmount -> Inspector 切换到对应对象且可命中。
- [ ] C：重复点击当前分类和 Escape 均能关闭；Escape 恢复触发按钮焦点。
- [ ] D：窄屏 Rail/Flyout/Inspector 不互相覆盖，不越出 viewport。

**直接测试入口**：

```powershell
# cwd: ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI
npm run test:unit -- tests/unit/capabilities/project-workspace/operatorRail.spec.ts
npm run typecheck
npm run build

# cwd: ClearVision.Product/tests/ClearVision.Product.UI.Tests
$env:CV_UI_SCENARIO='studio-ui-next'
npx playwright test tests/e2e/studio-ui-next/f03-workspace.spec.ts --reporter=list
```

**完成条件**：WP0 的 4 个文件不再与后续能力混改，全部验证有当前 HEAD 结果，人工复审后才开始 WP1。

## 8. WP1：唯一 FilePickerPort 与 Inspector 参数兼容

### 8.1 组件与 ownership 设计

| 对象 | 单一职责 | 合同 |
| --- | --- | --- |
| `StudioPlatform` | 在 composition root 创建并销毁一次 FilePickerPort | 暴露窄 `filePicker`，先 dispose port 再 dispose host |
| `FilePickerPort` | 串行发送 `PickFileCommand`、解码 `FilePickedEvent`、处理取消/超时/dispose | 不暴露原始 Host；同一时刻一个请求 |
| `FileParameterEditor.vue` | 展示当前路径、选择/清除/错误/忙碌状态 | props down，emit selected value up |
| `InspectorPanel.vue` | 组合资源参数编辑器 | 选择成功后调用现有 parameter commit；取消不改值 |
| `InspectorOwner` | 保持现有参数 patch、依赖、校验和 draft 语义 | 不复制 FilePicker 生命周期 |

**预计文件范围**（实施前以当前代码复核为准）：

- `StudioUI/src/platform/host/filePickerPort.ts`（新增）
- `StudioUI/src/platform/host/index.ts`
- `StudioUI/src/app/studioPlatform.ts`
- `StudioUI/src/capabilities/project-workspace/inspector/FileParameterEditor.vue`（新增）
- `StudioUI/src/capabilities/project-workspace/inspector/parameterEditorRegistry.ts`
- `StudioUI/src/capabilities/project-workspace/inspector/ParameterEditor.vue`
- `StudioUI/src/capabilities/project-workspace/inspector/InspectorPanel.vue`
- 对应 unit/component tests 和 F03 E2E fixture

### 8.2 FilePickerPort 待办

- [ ] 定义冻结的 request/result/error 类型，字段严格映射当前 `MessageType`、`ParameterName`、`Filter`、`FilePath`、`IsCancelled`。
- [ ] 由 port 建立唯一 host subscription；Inspector/AI 不直接调用 `host.subscribe`。
- [ ] 因后端没有 requestId，请求按 FIFO 串行；第二个请求排队或返回明确 busy，禁止并发误配。
- [ ] 对 camelCase/PascalCase 输入做受控 decoder，拒绝错误 `MessageType`、空 `ParameterName` 和无效 payload。
- [ ] 用户取消返回 typed cancelled result，不清空原值、不显示错误。
- [ ] host unavailable、超时、dispose、过期响应和组件 unmount 有明确状态；晚到响应不得写入已切换节点。
- [ ] Browser fake 可确定性注入成功、取消、错误和晚到事件。
- [ ] diagnostics 能证明 port dispose 后 subscription count 归零。

### 8.3 Inspector 待办

- [ ] `dataType=file` 显示路径字段与“选择文件”按钮，不再显示占位提示。
- [ ] 过滤器沿用 Legacy 已验证规则；图像、模型、模板和通用文件不使用同一个错误 filter。
- [ ] 对 Legacy path-like `string` 建立最小兼容 allowlist/metadata 判定；禁止把所有 string 猜成路径。
- [ ] 保持只读、运行中锁定、dependency-disabled、required、nullable 和校验状态。
- [ ] 选择成功仍走 `patchNodeParameter`；取消保持原值；清除动作只在参数合同允许空值时出现。
- [ ] 节点或参数切换后旧 picker 结果不得写入新 selection。
- [ ] 为 `dataType=color` 增加颜色 swatch + 标准颜色输入/文本兼容，保留合同的原始序列化格式。
- [ ] 增加“恢复默认值”明确命令；先审计 canonical adapter 是否支持删除 explicit value。若只能把值写成 default 而无法恢复 `valueSource=metadata-default`，记录 contract gap 并停止，不伪造完成。
- [ ] Esc 仍只撤销当前编辑草稿，不与“恢复默认值”混为一谈。

### 8.4 WP1 测试与验收

- [ ] Unit：成功、取消、错误、错误消息类型、FIFO、超时、dispose、晚到响应。
- [ ] Unit：`file`、path-like string、普通 string、`color`、default reset registry 分派。
- [ ] Component：选择成功提交一次；取消零提交；selection 变化后晚到结果零提交。
- [ ] Component：readonly/running/disabled 时不发 picker 命令。
- [ ] E2E Browser fake：文件按钮可用、路径提交后 Inspector 和 canonical draft 同步。
- [ ] 真实 WebView2 smoke：Windows 文件对话框可打开、选择、取消；Browser fake 通过不能替代此项。
- [ ] 1920x1080、125% 等效压力尺寸和窄屏下控件不溢出、不遮挡 Canvas/Inspector。

**WP1 完成条件**：仓库内只有一个 file-picker host subscription owner；Inspector 文件、路径兼容、颜色和默认恢复均有明确结果或单独 contract-gap，不修改后端消息/API 合同。

## 9. WP2：AI 附件、文件参数与资源绑定

**前置条件**：WP1 FilePickerPort 已通过复审。

**预计 owner/文件范围**：`StudioUI/src/capabilities/ai-workbench/**`、AI unit、`f06-ai-workbench.spec.ts`；共享 host 文件仅由 WP1 的既有 port 提供，不在 WP2 新增 bridge。

**待办**：

- [ ] 对照 Legacy `aiPanelAttachments.js`，确认当前 AgentRun endpoint 已接受的附件字段、大小/类型限制和错误语义。
- [ ] 在任务输入区增加附件选择、列表、移除、重复文件处理和上传/提交忙碌态。
- [ ] 附件只保存当前任务草稿投影，不进入 localStorage 或前端正式资产权威。
- [ ] 对照 Legacy `aiPanelPendingParameters.js`，为 file/path 参数复用 FilePickerPort；标量校验继续复用 `parameterValidation.ts`。
- [ ] 对照 Legacy `aiPanelResourceBinding.js`，补齐模型、模板/图像、标定资源的受控选择；相机继续使用现有 authoritative binding list。
- [ ] 文件路径与正式 Project asset 身份分开；需要上传/登记时必须调用既有后端资产合同，不能把本地路径冒充 asset id。
- [ ] AgentRun session restore 后，失效本地路径必须显示 stale/需重选，不自动重用不可验证文件。
- [ ] Apply/Undo/恢复仍服从现有 AgentRun、handoff 和 canonical workspace authority。
- [ ] 若现有 endpoint 不接受所需附件/资源字段，记录精确 contract gap、调用方和服务端 owner，停止该子项；不新增第二 AI endpoint。

**测试**：

- [ ] Unit/component：附件添加/移除、取消、重复、无效类型、超限、dispose、session restore stale。
- [ ] Unit/component：待补 file 参数与模型/模板/标定资源决策生成正确既有 payload。
- [ ] E2E：`f06-ai-workbench.spec.ts` 覆盖选择文件 -> 计划/构建 -> 资源确认 -> apply preview，全程无原始 Host 订阅泄漏。
- [ ] 真实 WebView2 smoke：AI 文件选择、取消、窗口关闭后 subscription 清零。

**完成条件**：AI 的三类文件入口均复用 WP1 端口，AgentRun authority 和现有安全身份不变。

## 10. WP3：模板、工程导入导出与示例工程

WP3 必须拆成三个串行复审批次，不允许一次改完。

### 10.1 WP3-A 流程模板

- [ ] 取证模板搜索、分页/筛选、应用、另存和更新的现有后端合同与 Legacy 错误语义。
- [ ] 建立单一 Template owner；缓存仅为可丢弃查询投影。
- [ ] 模板应用写入 canonical FlowCanvas 草稿，生成 dirty revision，但不自动正式保存。
- [ ] 应用前处理当前 dirty 草稿；需要替换现有流程时给出明确确认。
- [ ] 另存/更新保留权限、并发冲突、失败和 unknown outcome；不得用 localStorage 保存模板。
- [ ] 模板中的未知算子、版本不兼容、缺失资源和 metadata 失配必须可诊断，不静默丢字段。
- [ ] Unit：query/decoder/owner、应用转换、未知算子、冲突、dispose。
- [ ] E2E：搜索 -> 预览 -> 应用 -> Canvas dirty -> 显式保存 -> 重载一致。

### 10.2 WP3-B 工程 JSON 导入/导出

- [ ] 确认现有 Project import/export contract、schema/version、权限和文件承载方式；缺口先报告。
- [ ] 导入使用 WP1 FilePickerPort 选择文件，但解析/校验和正式写入服从既有 Project lifecycle。
- [ ] 导入不得直接替换 Pinia/Vue state，也不得复制 Legacy repository write。
- [ ] 创建/覆盖/冲突/重名必须走 `projectLifecycleCommandOwner` 和 `ProjectSaveCoordinator` 的既有身份/reconcile。
- [ ] 导出必须来自后端/canonical persisted project，不从不完整 DOM 或 UI 投影拼 JSON。
- [ ] 区分“导出工程 JSON”和现有“导出 Runtime Package”，文案和文件扩展名不得混用。
- [ ] Unit：schema 错误、版本不兼容、重名、无权限、unknown outcome、取消和重复提交。
- [ ] E2E：导出 -> 关闭工程 -> 导入 -> 打开 -> Flow/GlobalVariables/assets/revision 一致。

### 10.3 WP3-C 示例工程与引导

- [ ] 复核 `DemoProjectService` 与 `/api/demo/create*`；保留 `CanEditProject`，Operator 不得创建。
- [ ] 先解决或批准 Next lifecycle 的 `clientOperationId` / reconcile；未解决前保持 `BLOCKED_BY_CONTRACT`。
- [ ] 示例创建成功后由 Project lifecycle owner 接管打开/选择，不直接写前端列表。
- [ ] 示例内容必须来自后端权威，前端不复制一份 demo flow JSON。
- [ ] 引导只解释真实可用任务，不加入营销式 onboarding 或遮挡工作区的长引导。
- [ ] Unit/E2E：Admin/Engineer 成功、Operator 403、重复 operation reconcile、创建后打开、失败不产生幽灵项目。

**WP3 完成条件**：三个批次各自通过复审；模板、导入和示例都没有绕过 Project lifecycle/save authority。

## 11. WP4：标定工作台

WP4-A 与 WP4-B 必须由同一个纵向 owner 串行完成。该 owner 统一协调 Inspector 参数、ImageCanvas/ROI 交互、Preview 输入和正式 Project asset 保存。

### 11.1 WP4-A N 点标定

- [ ] 对照 Legacy 完整记录入口、点采集、编辑、排序、删除、有效性、拟合、残差、失败和保存流程。
- [ ] 冻结现有 draft endpoint 与正式 asset endpoint；明确 draft、preview result、formal asset 三种身份。
- [ ] 新建 capability-local calibration owner；Vue 只消费冻结投影和窄 commands。
- [ ] 复用 `imageCanvasOwner` / `roiInteractionOwner`，不创建第二 ImageCanvas 或 pointer owner。
- [ ] 处理图像/节点/工程切换导致的 stale draft；离开前进入现有 Leave Guard。
- [ ] 正式保存进入 Project asset save chain 和 `ProjectSaveCoordinator`。
- [ ] Unit：点增删改、最小点数、重复/共线/越界、拟合失败、stale、dispose、保存冲突。
- [ ] E2E：采集 N 点 -> 校验/拟合 -> 保存 -> Inspector 绑定 -> 预览使用 -> 重载一致。

### 11.2 WP4-B 二维平面比例偏移标定

- [ ] 对照 Legacy 向导步骤、输入单位、比例/偏移计算、验证样本和错误阈值。
- [ ] 复用 WP4-A owner/asset contract，不建立第二 calibration 状态树。
- [ ] 区分本地草稿结果、验证结果和正式标定资产。
- [ ] 单位、数值精度、坐标方向和异常数据有显式中文反馈。
- [ ] Unit：比例/偏移边界、零跨度、反向轴、精度、stale、冲突。
- [ ] E2E：向导完成 -> 保存正式资产 -> 绑定算子 -> 重载 -> 结果一致。

**布局验收**：1920x1080/125% 下图像、点表、误差和主操作首屏可达；短屏只有一个明确纵向滚动 owner，浮层不越界。

## 12. WP5：全局变量运行值与结果分析

### 12.1 WP5-A 全局变量运行值

- [ ] 保留现有 `workspaceGlobalVariablesOwner` 的“定义草稿/应用/正式保存”语义。
- [ ] 取证运行值读取、单项写入、单项重置、全部重置的现有 authenticated runtime endpoints。
- [ ] 为运行值建立独立窄 command owner；不得把运行值写回工程定义 JSON。
- [ ] UI 同时展示定义值、当前运行值、来源、更新时间和 stale/offline 状态。
- [ ] 单项/全部重置需要权限和确认；运行中、离线、unknown outcome 有明确 gate/reconcile。
- [ ] Project 切换、正式运行开始/结束、owner dispose 时停止订阅和 pending 请求。
- [ ] Unit：定义保存不触发 runtime write；runtime write 不标记 Project dirty；reset/403/409/unknown/dispose。
- [ ] E2E：修改定义并保存、手动写运行值、单项重置、全部重置，逐项证明两种语义不串线。

### 12.2 WP5-B 结果趋势、分布与整批导出

- [ ] 复核 results query/export 现有 endpoint 的筛选、分页、批次、时区和权限合同。
- [ ] 趋势与缺陷分布来自后端结果投影；不只对当前页前端聚合后冒充整批统计。
- [ ] 区分 OK、NG、执行失败、未判定和 unknown；不能把执行状态等同于判定结果。
- [ ] JSON/CSV 整批导出复用现有 export service；保持筛选快照、列定义、单位、编码和时区。
- [ ] 大批量导出显示 pending/progress/completed/failed/unknown，不在主线程拼接超大字符串。
- [ ] 文件落地复用批准的 host/download 路径；不得新建第二 HostBridge。
- [ ] Unit：filters snapshot、decoder、统计状态、CSV/JSON 参数、取消/失败/unknown/dispose。
- [ ] E2E：同一筛选下趋势/分布/列表一致；导出请求覆盖整批而非当前页；Operator 保持只读。

**完成条件**：GlobalVariables 的两类写语义完全隔离；结果统计和导出有后端整批证据。

## 13. WP6：线序、Station 测试包与设置高级维护

### 13.1 WP6-A 线序预览分析与一键自动调参

- [ ] 取证 Legacy 的输入、预览证据、推荐参数、应用/撤销和错误语义。
- [ ] 复用现有设备/预览 API 和 owner；前端不实现私有检测算法。
- [ ] “分析结果”与“已应用参数”分离；应用前展示 diff，应用后可按既有合同撤销。
- [ ] 自动调参不得绕过设备权限、运行中锁定和参数范围校验。
- [ ] stale 图像、设备断连、请求超时和 unknown outcome 有明确恢复路径。
- [ ] Unit/E2E：预览 -> 分析 -> diff -> 应用 -> 验证 -> 撤销；失败时原参数不被误报为已更新。

### 13.2 WP6-B 生成并下发 Station 测试运行包

- [ ] 明确它与现有本地 Runtime Package 导出的差异、现有 endpoint 和 Station command authority。
- [ ] 生成输入必须来自已保存 revision；有 dirty 草稿时阻止或要求先保存。
- [ ] 建立单一 Station deployment command owner，复用现有 station lifecycle/read projection。
- [ ] 显示目标 Station、package identity、project/revision、生成状态、下发状态和最终 reconcile。
- [ ] 下发前二次确认；403/409/offline/timeout/unknown outcome 不得显示成功。
- [ ] 页面关闭或路由切换只取消本地等待，不擅自假定服务端命令已取消。
- [ ] Unit/E2E：生成、权限、目标离线、重复命令、unknown outcome、重连 reconcile、成功后 Station 投影一致。

### 13.3 WP6-C 设置恢复默认与数据库高级维护

- [ ] 逐设置组冻结“恢复默认”的后端合同、默认来源、保存范围和权限；禁止前端硬编码另一套默认值。
- [ ] 恢复默认先显示 diff/范围，确认后经现有 `settingsWriteCoordinator` 写入。
- [ ] 数据库 repair/restore/cleanup/global reset 逐项确认现有 endpoint、Admin policy、备份前置和互斥规则。
- [ ] restore 必须明确备份身份；repair/cleanup 显示影响范围；global reset 使用更强确认。
- [ ] 所有数据库命令实现 pending、success、failed、unknown-outcome 和重新读取/reconcile。
- [ ] 不显示敏感路径，不把数据库文件内容带入 Vue state，不从前端直接操作 SQLite。
- [ ] 存储目录选择和“立即清理”保持 Legacy unavailable，不借本包扩项。
- [ ] 同一 `.csproj` 的后端定向测试通过串行脚本一次运行，禁止并发 `dotnet test`。
- [ ] Unit/E2E：权限、确认取消、成功、失败、unknown、互斥、刷新恢复；真实 restore/repair 只在隔离测试数据库执行。

**完成条件**：三个高风险子包分别复审，权限、确认、终态和 reconcile 均有证据；未批准合同继续标记 blocked，不做前端替代实现。

## 14. WP7：最终验证、真实宿主证据与文档闭合

### 14.1 前端静态与单元测试

- [ ] `npm run lint`
- [ ] `npm run typecheck`
- [ ] `npm run test:unit`
- [ ] `npm run build`
- [ ] 需要发布边界时另跑 `npm run build:production` / bundle gate，不用普通 Vite build 冒充 release publish。

### 14.2 Studio UI Next Playwright

- [ ] `f03-workspace.spec.ts`：Flyout、Inspector、file picker、标定和全局变量工作区链路。
- [ ] `f04-project-lifecycle.spec.ts`：模板、导入、示例工程与生命周期。
- [ ] `f06-ai-workbench.spec.ts` 及 handoff/history：附件、文件参数、资源绑定、恢复。
- [ ] `f02-results.spec.ts`：趋势、分布、整批导出。
- [ ] `f07-device-workbench.spec.ts` / `f07-settings-shell.spec.ts`：线序与设置维护。
- [ ] Stations/Runtime Package 对应 spec：测试包生成、下发和 reconcile。
- [ ] `m07-accessibility-resilience.spec.ts`：键盘、焦点、长中文、错误/空/加载和响应式状态。

### 14.3 真实环境证据

- [ ] 真实 WebView2：Windows 100% 和 125%，至少 1920x1080；125% 不由 Chromium DPR 代替。
- [ ] 短屏压力：约 1366x768 / 1350x704 client size，检查单一滚动 owner、浮层和固定操作区。
- [ ] compact / comfortable 两种 density。
- [ ] 文件选择：Inspector 与 AI 的打开、成功、取消、窗口关闭和重复使用。
- [ ] Release publish 输出只写 `.tmp/publish-check/` 或仓库外；检查 hashed assets 和 stale chunk。
- [ ] 独立 no-Node 目标机启动；本机 Desktop 子进程没有 Node 不能替代。
- [ ] Camera/PLC/Station/数据库维护在隔离或现场环境验证；Browser fixture 不冒充硬件通过。
- [ ] Remote CI、Final Gate 和生产 soak 分别记录；普通分支 push 不等于完整 CI。

### 14.4 文档闭合

- [ ] 更新 `F09_G1_LegacyNext终局能力矩阵.md` 的细粒度状态、owner、evidence 和 issue id。
- [ ] 更新 `F09_OPEN_ISSUES.md`，只关闭有当前代码和测试证据的条目。
- [ ] 更新 M00 差异矩阵，区分“主体迁移”与“细分操作闭合”。
- [ ] 每项证据记录 source SHA、命令、结果、环境和产物路径。
- [ ] 未完成的 WebView2 125%、no-Node、现场硬件、Remote CI 或 soak 保持 `NOT PERFORMED` / `BLOCKED`。
- [ ] 最终仍需产品负责人签收，不能由自动测试自行授予 `PRODUCTION_ACCEPTANCE`。

## 15. 每个工作包的通用 Definition of Done

- [ ] 需求中的完整用户路径可完成，不只验证 DOM 存在。
- [ ] 单一 owner、唯一订阅、唯一写入口和 dispose 证据明确。
- [ ] 未新增第二 API、HostBridge、Canvas、EventBus、ServiceRegistry 或保存链。
- [ ] 权限、readonly、running、loading、empty、error、stale、conflict 和 unknown outcome 按风险覆盖。
- [ ] unit/component tests 覆盖成功、取消/失败、过期结果和 dispose。
- [ ] Playwright 验证实际可见、可命中、焦点和不遮挡；不能只用 `toBeAttached`。
- [ ] 1920x1080 和短屏无新增水平滚动、双层滚动或越界浮层。
- [ ] 简体中文术语一致；按钮表达动作，错误说明原因、影响和下一步。
- [ ] `typecheck`、相关 unit、build、对应 E2E 都有当前 HEAD 结果。
- [ ] `git diff --check` 无 whitespace error；`git diff --stat` 与文件白名单一致。
- [ ] 汇报所有与本任务无关的未提交修改；不提交、不推送，等待复审。

## 16. 停止条件

遇到以下任一情况，停止当前子项并报告，不自行扩权：

- 后端没有实现所需命令、查询、文件承载或 reconcile 合同。
- 现有合同无法区分请求身份，且串行化仍不能保证正确关联。
- 正式保存无法进入 `ProjectSaveCoordinator`。
- 需要第二 HostBridge、第二 Canvas、第二 API transport 或第二保存链才能继续。
- capability owner 与当前 dirty 文件、其他 worktree 或并行 owner 发生重叠。
- 高风险数据库/Station 命令没有权限、确认、审计或 unknown-outcome 恢复语义。
- 测试必须依赖真实设备/目标机但当前环境不具备；此时记录 `NOT PERFORMED`，不写 PASS。

Contract-gap 报告至少包含：缺失合同、当前调用路径、Legacy 行为、受影响用户任务、建议服务端 owner、权限/并发/保存风险，以及前端为何不能安全替代。

## 17. 每批交付汇报模板

```text
Work package:
Baseline HEAD:
Current HEAD:

1. 根因/缺口最终确认：
2. 修改文件：
3. 单一 owner 与数据流：
4. 后端 authority / 保存链是否保持：
5. 新增或修改的测试：
6. 实际运行结果（PASS / FAIL / NOT RUN）：
7. Browser、WebView2、DPI、no-Node、硬件证据边界：
8. git diff --stat：
9. 与本任务无关的未提交修改：
10. 已知剩余风险或 contract gap：
11. 是否提交/推送：NO，等待复审
```

## 18. 下一步唯一动作

- [ ] 只执行 WP0：复核现有 Flyout diff，运行相关 unit、typecheck、build 和 `f03-workspace.spec.ts`，按第 17 节汇报并等待复审。
- [ ] WP0 未通过或未复审前，不开始 FilePickerPort、AI、Project、Calibration、GlobalVariables、Results、Station 或 Settings 的任何实现。

## 19. 2026-08-07 当前执行与审计附录

第 1-18 章保留为原始计划和进入条件。本附录记录本次实际执行结果，并在状态冲突时优先于原始“下一步唯一动作”：本次已按用户授权完成可安全完成的实现、验证、审计、提交和推送准备；后端合同不足的子项按停止条件保留阻断，不由前端伪造闭环。

```text
AUDIT_BASELINE_HEAD=68e6e4286d008433f804ef90de00c8017184c177
IMPLEMENTATION_COMMIT_SHA=418406e620082fdedf46cd2a180b44a27c43d002
AUDIT_BRANCH=studio-ui-next
REMOTE_STUDIO_UI_NEXT_HEAD=7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5
REMOTE_RELATION=REMOTE_ANCESTOR_AHEAD_37_NO_DIVERGENCE
WORKTREE_STATE=DIRTY_SCOPED_CANDIDATE_BEFORE_COMMIT
NODE=v24.14.0
NPM=11.9.0
PLAYWRIGHT=1.58.1
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

### 19.1 当前工作包结论

| 工作包 | 当前结论 | 本轮实际闭合 | 仍保留的边界 |
| --- | --- | --- | --- |
| WP0 | `PASS` | Flyout 受控 owner、Escape/选择/拖放关闭、Inspector 可见性；F03 全量通过 | 真实 WebView2/DPI 仍不由 Chromium 证据替代 |
| WP1 | `IMPLEMENTED_WITH_LOCAL_EVIDENCE` | 唯一 `FilePickerPort`、Inspector file/path/color 编辑器、取消/过期结果保护 | Host 消息没有 request id，port 继续串行化；AI 附件合同不在本包 |
| WP2 | `PARTIAL` | AI Pending file 参数复用同一 port，取消和 busy 状态接入 | AgentRun 附件、模型/模板/标定资源字段未取得可安全复用的 Next 合同，保持 `BLOCKED_BY_CONTRACT` |
| WP3-A | `IMPLEMENTED_WITH_PARTIAL_EVIDENCE` | Template owner、查询/筛选、转换诊断、应用、另存/更新的权限/冲突/unknown 状态及 unit 覆盖 | 尚无完整 Template Playwright 旅程，记为 `CV-AUDIT-046` |
| WP3-B | `BLOCKED_BY_CONTRACT` | 已审计现有 API 和 Next lifecycle 边界 | 未发现 Next Project JSON import/export schema、承载方式和 reconcile 合同；不复制 Legacy repository write，记为 `CV-AUDIT-047` |
| WP3-C | `BLOCKED_BY_CONTRACT` | 保留 Legacy demo 事实和 `CanEditProject` 证据 | `DemoProjectService` 仍缺 Next lifecycle `clientOperationId`/reconcile，继续使用 `F09-I003` |
| WP4-A | `PARTIAL` | 单一 calibration owner、N 点 draft/solve、候选与正式 asset 分离、stale/dispose、Project asset 正式保存及 unit；正式保存 Operator `403` 已验证 | 尚无完整采点到重载的 Playwright；draft solve endpoint 的权限语义仍需后端审计，见 `CV-AUDIT-045` |
| WP4-B | `BLOCKED_BY_CONTRACT` | 没有建立第二 calibration 状态树或私有资产模型 | 未发现正式二维比例/偏移向导和 asset contract；前端停止，记为 `CV-AUDIT-048` |
| WP5-A | `IMPLEMENTED_WITH_LOCAL_EVIDENCE` | 运行值读取、单项写入、单项/全部重置与工程定义保存保持独立 owner/语义 | 当前证据以 unit 和 Browser fixture 为主，真实运行设备验证未执行 |
| WP5-B | `PARTIAL` | 趋势、缺陷分布、分析报告查询 owner，source 切换时 abort/dispose | 未发现整批 JSON/CSV export contract/UI 证据，不把当前页聚合冒充整批导出，记为 `CV-AUDIT-049` |
| WP6-A/B/C | `BLOCKED_BY_CONTRACT` | 完成合同取证和停止记录 | 线序分析、Station 测试包命令、设置高级维护缺少可由 Next 安全复用的完整合同，记为 `CV-AUDIT-050`；数据库高级维护仍为 `F09-I006` |
| WP7 | `LOCAL_PASS_WITH_ACCEPTANCE_DEBT` | 静态门禁、unit、build、F03/F04-R、定向 Desktop 测试及文档审计完成 | WebView2 125%、独立 no-Node、现场硬件、Remote CI、Final Gate、生产 soak 均保持 `NOT_PERFORMED`/`NOT_RUN`/`BLOCKED` |

### 19.2 Owner、authority 与修改范围

- `FilePickerPort` 是 `StudioHostAdapter` 之上的唯一文件选择窄端口；Inspector 与 AI Pending 参数均从 composition root 获取，不直接订阅 WebView2。Host 合同无 request id，因此同一时刻只允许一个 in-flight picker。
- Template owner 只把已诊断的模板转换结果写入 canonical Flow 草稿；显式保存仍由既有 `ProjectSaveCoordinator` 处理。未知算子、连接或参数 metadata 不静默丢失。
- Calibration owner 复用现有 Flow/ImageCanvas owner 的选择、图像和点击投影。solve 只产生 draft/candidate，formal save 使用既有 Project asset endpoint 和 persistence revision；未建立前端资产权威。
- GlobalVariables 运行值 mutation 与工程定义保存保持两条语义；Results analysis 是只读查询 owner，项目/source 切换会 dispose/abort 旧 owner。
- 本次 scoped 文件仅位于 `StudioUI/src`、对应 `StudioUI/tests/unit`、Studio UI Next F03 fixture/spec，以及本 TODO 和四份 F09/M00 证据文档；未发现与本计划无关的工作树文件。

### 19.3 实际验证记录

| 命令/证据 | 结果 | 环境与边界 |
| --- | --- | --- |
| `npm run lint` | `PASS` | StudioUI，当前候选工作树 |
| `npm run typecheck` | `PASS` | Vue/Vitest/Node 三个 tsconfig |
| `npm run test:unit` | `PASS`, 136 files / 837 tests | Vitest/jsdom；不等同真实 WebView2 |
| `npm run build` | `PASS` | Vite Debug output 写入既有 `obj/Debug/.../StudioUI/dist` |
| `npx.cmd playwright test .../f03-workspace.spec.ts --project=chromium --workers=1 --reporter=list` | `PASS`, 59/59 | 静态 Chromium fixture，包含 file picker、Flyout、Inspector、生命周期及短屏场景 |
| `npx.cmd playwright test .../f04-project-lifecycle.spec.ts --project=chromium --workers=1 --reporter=list` | `PASS`, 2/2 | 工程 lifecycle 的 create/open/rename/delete/reconcile/conflict 旅程 |
| `run-dotnet-test-serial.ps1 ... CalibrationDraftEndpointsTests --NoBuild --NoRestore` | `PASS`, 4/4 | Desktop endpoint 定向测试；formal save Operator `403` 覆盖，draft solve 权限未宣称已解决 |
| `git diff --check` | `PASS` | 仅有 Git 的 LF/CRLF 提示，无 whitespace error |
| `npm run build:production`, bundle gate, release publish | `NOT RUN` | 本轮未把普通 build 扩写为 release 证据 |

### 19.4 Contract gap 与真实环境边界

- Project JSON import/export、Next demo lifecycle reconcile、AI 附件/资源字段、二维比例偏移 calibration、结果整批 JSON/CSV export、线序分析、Station 测试包和数据库高级维护均未取得足以支持 Next 实现的当前后端合同；按停止条件不新增 endpoint、transport、save chain 或前端伪权威。
- `POST /api/projects/{projectId}/calibration-assets/from-draft` 已挂 `CanEditProject` 且 Operator `403` 测试通过；`POST /api/calibration/npoint-draft/solve` 当前代码未挂显式 permission guard。本附录将 `CV-AUDIT-045` 拆为“正式保存权限已解决”和“draft solve 权限语义待后端审计”，不笼统写成正式保存缺少权限。
- 本轮 Playwright 使用 Chromium 静态 fixture；真实 WebView2、Windows 125%、独立 no-Node 目标机、Camera/PLC/Station/数据库隔离环境、Remote CI、Final Gate 和生产 soak 未执行。`PRODUCTION_ACCEPTANCE` 继续为 `NOT_GRANTED`。

本附录中的测试 source anchor 是 `AUDIT_BASELINE_HEAD` 加其 scoped working-tree diff；实现内容已落在 `IMPLEMENTATION_COMMIT_SHA=418406e620082fdedf46cd2a180b44a27c43d002`，后续文档提交仅更新 provenance，不改变实现内容。
