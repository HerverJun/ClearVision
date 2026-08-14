# ClearVision Studio UI Next 第二轮旗舰级视觉精修计划

```text
DOCUMENT_ROLE=CODEX_AUTOMATED_VISUAL_REFINEMENT_R2_PLAN
DOCUMENT_STATE=R2_LOCAL_IMPLEMENTATION_COMPLETE_EVIDENCE_PARTIAL
PLAN_DATE=2026-08-12
PLAN_UPDATED=2026-08-13
PLAN_BASELINE_HEAD=22a3d26a00a2d3b8098165aab5489ce54f5bc95b
PREVIOUS_VISUAL_IMPLEMENTATION_HEAD=bf662c838c4b066362169e06486f04a38be95899
PREVIOUS_VISUAL_EVIDENCE_HEAD=aba69626995ae65d38829b99ac9387eb7bc62111
BRANCH=studio-ui-next
UPSTREAM=origin/studio-ui-next
UPSTREAM_DELTA_AT_PLAN=AHEAD_7
CURRENT_STATUS_SOURCE=docs/进行中/StudioUINext/F10_ContractAndProductionPlan.md
PREVIOUS_VISUAL_PLAN=TODOView.md
PRODUCT_CONTEXT=PRODUCT.md
PRIMARY_UI_ROOT=ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI
LEGACY_SEMANTIC_BASELINE=ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot
QUALITY_BAR=FLAGSHIP_INDUSTRIAL_DESKTOP_TOOL
DESIGN_DIRECTION=APPLE_INSPIRED_PRODUCT_CRAFT_WITH_INDUSTRIAL_QUIET_PRECISION
MOTION_DIRECTION=QUIET_FUNCTIONAL_CONTINUITY
DEFAULT_THEME=LIGHT
DEFAULT_DENSITY=COMPACT
PRIMARY_VIEWPORT=1920x1080
SHORT_VIEWPORT_PRESSURE=1366x768_OR_WEBVIEW2_CLIENT_1350x704
PRIMARY_VISUAL_REVIEW=CONTROLLED_BEFORE_AFTER_PLUS_BLIND_INDEPENDENT_REVIEW
PRIMARY_ITERATION_BROWSER=CODEX_IN_APP_BROWSER
IN_APP_BROWSER_POLICY=REQUIRED_FOR_EACH_VISUAL_BATCH_WHEN_AVAILABLE
IN_APP_BROWSER_EVIDENCE_CLASS=DIRECTIONAL_BROWSER_ONLY
AUTO_VISUAL_ITERATION_MAX=3
FORMAL_BROWSER_EVIDENCE=REPOSITORY_PLAYWRIGHT_HARNESS
FORMAL_HOST_EVIDENCE=WINFORMS_WEBVIEW2
AUTO_CONTINUE=YES_AFTER_INTERNAL_GATE
AUTO_COMMIT=NO
AUTO_PUSH=NO
CURRENT_COMMIT_AUTHORIZATION=NOT_GRANTED
CURRENT_STAGE=R2.7_LOCAL_CHROMIUM_AUDIT_COMPLETE_EXTERNAL_GATES_OPEN
NEXT_ACTION=RUN_POST_DOCUMENT_FINAL_MATRIX_THEN_HAND_OFF_EXTERNAL_GATES
PREVIOUS_EVIDENCE_DISPOSITION=HISTORICAL_COMPARISON_ONLY
R2_SOFTWARE_EVIDENCE=PASS_LOCAL_DIRTY_CANDIDATE
R2_IN_APP_BROWSER_ITERATION=NOT_PERFORMED_IN_APP_BROWSER_UNAVAILABLE
R2_BROWSER_EVIDENCE=PASS_CHROMIUM_ONLY_DIRTY_CANDIDATE_42_OF_42
R2_MOTION_EVIDENCE=PASS_CHROMIUM_ONLY_SOURCE_SCAN_AND_REDUCED_MOTION
R2_WEBVIEW2_100=NOT_RUN
R2_WEBVIEW2_125=NOT_PERFORMED
R2_BLIND_REVIEW=NOT_PERFORMED
R2_PRODUCT_OWNER_SIGNOFF=NOT_GRANTED
R2_EXECUTION_LEDGER=docs/进行中/StudioUINext/R2_视觉精修执行台账.md
R2_EVIDENCE_INDEX=docs/进行中/StudioUINext/R2_视觉证据索引.md
R2_VALIDATION_ROOT=ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual
PRODUCTION_ACCEPTANCE=NOT_GRANTED
LEGACY_RETIREMENT=NOT_APPROVED
```

> 本文件是 `TODOView.md` 完成后的独立第二轮视觉执行队列。旧文件的 V0-V6/V6R、测试数字、截图和 SHA 全部保留为历史事实与比较输入，不自动成为本轮 PASS。本计划不维护业务、合同或生产状态权威；若与根 `AGENTS.md`、当前代码或 F10 冲突，以三者为准。

## 1. 为什么还需要 R2

上一轮已经消除了大量明显的组件漂移、状态缺口和粗糙实现，但当前产品仍偏向“整洁的工程后台”：页面主要靠完整描边矩形分区，表面权重接近，排版层级压缩，焦点与操作优先级不够明确。登录页、Product Shell、Workspace 和运维页虽然已经统一，却仍缺少旗舰产品应有的构图控制、材质关系、局部节奏与细节完成度。

R2 不再以“统一 token、圆角和间距”为主线，而以四个可以观察和比较的结果为主线：

1. **视觉焦点**：三秒内能定位当前对象、关键状态和唯一主操作。
2. **材质层级**：不用卡片海洋，也能分清 app、page、work surface、raised、floating 和 canvas。
3. **操作优先级**：浏览、编辑、保存、正式运行、停止、危险操作和恢复动作有稳定而清楚的秩序。
4. **跨页一致性**：登录、壳层、Workspace、结果、设置与 AI 像同一件产品，而不是同一套组件拼出的多个页面。

## 2. 目标与验收结果

R2 完成后必须同时满足：

- 默认 1920x1080 light/compact 下，登录到正式结果的黄金旅程具有清楚的视觉主次、连续对象上下文和稳定操作位置。
- 1366x768 或相近 client size 下不以隐藏核心能力换取整洁；Canvas、Inspector、Preview、保存、正式运行/停止和关键状态仍可达。
- 登录页不再是“小表单 + 大片空暗背景 + 缩小截图”，而是具有明确产品身份、真实工作内容和精确表单节奏的入口。
- Product Shell 不再像文档网站顶栏；active route、当前工程、全局状态和会话控制形成成熟桌面产品的导航结构。
- Workspace 让 Canvas/图像/流程成为视觉主体，辅助 chrome 减噪但不失能；高频编辑、保存和运行控制的层级稳定。
- 生产与调查页面优先支持扫描、比较和处置，不使用营销 KPI 或同权重大卡片。
- Settings、AI、Operators、Diagnostics 与 About 共享同一排版和表面语法，同时保持表单、工作流、目录和诊断各自应有的结构。
- 必要的状态反馈、浮层、选择与操作完成动效形成克制而一致的连续性；关闭动效时信息与效率不下降，Canvas、实时图像和高频事件流不被装饰动画干扰。
- light/dark、compact/comfortable、键盘、长中文、loading/empty/error/stale/readonly/unknown 等状态都不是事后补丁。
- 至少两名不知 before/after 标签的评审者在可比截图中对 R2 候选给出明确偏好；视觉焦点、材质层级、操作优先级和跨页一致性四项均达到 Gate 规定分数。
- Codex 对每个可运行的视觉批次先用内置浏览器打开真实目标 route，亲自查看并保存 before，修改后在同一受控场景复拍、检查 DOM/交互/控制台并据此回改；不得只改 CSS 后凭代码推测已变高级。

## 3. 非目标与不可越过的边界

- 不改变 Project、Flow、GlobalVariables、正式 assets、AgentRun、Runtime Package、Inspection、Results、Station 或 AI 资源的既有权威。
- 不新增第二 API transport、EventBus、ServiceRegistry、HostBridge、Canvas 内核、Project save endpoint、保存 client、SSE owner 或前端私有持久化链。
- 不重写正式运行、保存、reconcile、Leave Guard、Runtime、Station 或现场设备协议。
- 不复制已废弃 `FrontendV2/`；legacy 只用于核对功能、步骤、状态与错误路径，不复制其视觉。
- 不新增在线字体或为“高级感”引入不必要依赖。系统字体栈、简体中文和 Windows 清晰度优先。
- 不做苹果官网式营销 hero、macOS 窗口仿制、毛玻璃、装饰渐变、发光、霓虹、科技网格、漂浮大卡片、全局路由过渡或页面入场编舞。
- 不为每次 SSE、AI、Station、检测结果或实时图像事件逐条播放动画，不用动画延迟写入、停止、Leave Guard、错误呈现或正式状态投影。
- 不通过删功能、深藏常用操作、扩大固定 chrome、弱化错误或混淆 Preview/Formal Run 来换取干净截图。
- 不把 Chromium、DPR、浏览器缩放、静态截图、非空像素、lint/build 或 detector `[]` 写成 WebView2、真实 125%、人工审美或生产验收。
- 本计划不自动 commit、push、切换分支、修改 F10 状态、删除用户文件或处理 `.impeccable/`、`output/` 等未跟踪路径。
- 内置浏览器只用于当前候选的高频视觉调试和方向性截图，不取代仓库 Playwright、真实 WebView2、native DPI、no-Node、现场或人工签收；连接失败时不得静默换来源后继续声称是内置浏览器证据。

## 4. R2 视觉纲领

### 4.1 Apple-like 的准确含义

本项目借鉴的是苹果成熟产品的秩序、比例、内容优先、表面克制、排版精度和交互连续性，不是苹果官网的版式，也不是 macOS 皮肤。高级感来自“少而准确”：每个边界有理由、每个层级有距离、每个状态有反馈、每个高频动作有稳定位置。

### 4.2 五条强制构图规则

1. **每屏一个主舞台**：页面只能有一个最强视觉区域；Workspace 是 Canvas/图像，Results 是可比较的数据与调查内容，Settings 是当前设置组，Auth 是身份与登录任务。
2. **标题不与数据争夺**：page title 只建立对象和上下文；section title 服务扫描；面板内不使用 hero 尺寸，caption 不承担正文。
3. **连续区域不用重复成卡片**：优先用网格、留白、tonal surface、对齐和单向分隔线；只有独立对象、modal、popover 或真正 raised tool 才使用封闭容器。
4. **主操作保持稀缺**：一个局部操作组只允许一个 primary；正式运行、停止、保存、恢复和危险操作必须依语义而非品牌色区分。
5. **细节不改变几何**：hover、selected、loading、status 和动态图标不得引发布局位移；控件、工具条、棋盘/Canvas 和面板使用稳定尺寸约束。

### 4.3 需要消除的剩余廉价感

- 浅灰背景上堆叠白色描边框，所有模块像同权重线框稿。
- 小字号、相近字重和相近灰度挤在一起，标题、对象、metadata 与帮助文案层级不足。
- 顶栏 active route 主要靠 2px 下划线，导航像网站而非桌面工作台。
- 登录页真实产品画面只占暗色预览区的一小块，表单、品牌与产品内容之间缺少强构图关系。
- 页面头、工具条、状态条和局部操作各自成立，但组合后没有明确视觉中心。
- 相同的“标题 + 说明 + 多块 Panel”骨架跨页面复用过度，导致页面没有任务特征。
- 状态徽标、按钮和小型容器过多，使内容被 UI chrome 包围。
- 依赖边框表达可点击、选中、分组和异常，缺少更精细的形状、表面、排版与空间语言。

### 4.4 否决清单

发现以下任一项，本批视觉 Gate 直接失败：

- 主任务或关键状态被装饰遮蔽，或短屏下核心操作不可达。
- 新增卡片套卡片、普通 Panel 的描边加宽软阴影、无业务意义渐变/光效。
- 主操作超过一个且同权重，危险操作借用品牌主色，Preview 与 Formal Run 视觉语义混淆。
- 字号随 viewport 缩放、负 letter-spacing、正文小于既有可读基线、长中文无合理换行/tooltip。
- CSS 隐藏代替 owner unmount/dispose，或出现第二状态树、request owner、保存入口、Canvas facade、HostBridge。
- 只展示 happy path，或以自动测试/截图存在作为“更优雅”的唯一证据。

### 4.5 可证伪的初始质量预算

以下是 R2.0 必须测量并写入执行台账的初始验收带，不是让页面机械套用的 CSS 配方。若当前代码事实证明某项不适用，只能在台账中记录测量值、用户影响、替代阈值和主协调 Owner 的裁决，不能静默删除。

- **固定 chrome**：compact 下 `--cv-product-topbar-height <= 52px`、`--cv-workspace-toolbar-height <= 38px`、`--cv-workspace-status-height <= 22px`；comfortable 下分别不超过 56/44/24px。任何增加都必须先证明 B2 的 Canvas、Inspector、Preview 和主操作没有失能。
- **排版**：用户可见正文不小于 12px，常规正文以 14px 为基线，page title 保持 24-28px、section title 保持 16-18px；相邻语义层级至少在字号、字重、色调、上下间距四项中的两项有可观察差异。禁止流体字号和负 `letter-spacing`。
- **形状与 elevation**：圆角保持 3-8px；pill 只用于状态、标签或确有语义的紧凑选择，不用于普通文字命令；普通内容的封闭容器嵌套深度不超过 1，`card-inside-card=0`，主舞台完整描边不超过 1 层。
- **操作**：每个局部 command cluster 同时只允许 1 个 primary；关键操作在 B2 的截断数为 0、不可达数为 0；hover/active/loading/selected 导致的可测 layout shift 为 0。
- **滚动**：每条祖先-子级轴只允许一个明确 owner；并列 Workspace pane 可以各自滚动，但未记录的嵌套 scroll owner 数必须为 0，页面级水平 overflow 必须为 0。
- **内容主体**：核心场景的首个可比组必须使用具有真实产品语义的数据 fixture，不接受 skeleton、占位线、空白 Canvas 或纯错误页代替主场景；空、错误和 loading 作为独立状态评审。
- **表面**：app/page/work/raised/floating/canvas 的相邻层级不能只靠完整边框区分；去色截图中仍须能通过明度、空间或 elevation 识别主区域。品牌色不得成为大面积页面底色，状态色不得替代品牌或操作色。

R2.0 对这些预算建立 DOM box、computed style 和截图标注基线；后续每批 `review.json` 都记录适用指标的 before/final 数值。视觉判断仍以任务和盲评为准，计数器用于防止方案在不知不觉中退回卡片海洋和 chrome 膨胀。

### 4.6 动效治理与初始预算

当前 Vue 3、CSS 与既有 motion tokens 已足以完成本轮需要的微交互，适当增加动效可以改善状态反馈、浮层空间关系、选择连续性和操作完成感。本轮不预设引入 GSAP、Motion One、VueUse Motion 或其他动画依赖；只有内置机制无法满足一个已批准、可度量的产品场景时，才单独提交依赖、bundle、WebView2 兼容性与生命周期分析。

动效是支持视觉焦点、操作优先级和跨页连续性的横向质量层，不是独立装饰层。每个动效必须先归入以下目的之一，否则不实施：

| 目的 | 允许场景 | 明确禁止 |
| --- | --- | --- |
| 状态反馈 | hover/press、validation、保存结果、运行/停止确认、状态图标变化 | 用脉冲、呼吸或循环闪烁代替状态文案；为了“有生命力”持续运动 |
| 空间连续性 | menu、popover、modal、drawer、受控 flyout 的出现与退出 | 全局 route crossfade、页面加载编舞、装饰性视差或漂浮背景 |
| 焦点与选择连续性 | active route 指示、tab/row/node 选择、focus ring 与轻量强调 | 移动真实布局来追逐焦点；让动画成为唯一的选中线索 |
| 操作完成感 | toast、短暂成功/失败反馈、可取消的进度状态 | 延迟 API 写入、Leave Guard、停止命令、错误或 ARIA 更新来等待动画结束 |

实现与预算遵守以下硬规则：

- **机制选择**：仍 mounted 的状态变化使用 CSS transition 或 class/style binding；单一元素 enter/leave 使用 Vue `<Transition>`；`<TransitionGroup>` 只用于小型、稳定 keyed、低频变化的集合。key 必须是来自 canonical id 的稳定 primitive，同一逻辑对象跨更新保持不变，同屏对象不得复用；禁止数组 index、对象引用、时间戳和随机值。密集表格、虚拟列表和 SSE 高频列表不做逐行 enter/move/leave。
- **属性白名单**：优先 `opacity`、非 Canvas 宿主的 `transform`、颜色、`border-color` 和受控小阴影；不得动画 `width`、`height`、`grid-template-*`、`flex-basis`、pane 尺寸、splitter 几何或其他触发布局的属性，不得使用 `transition: all`。
- **时间与位移**：复用现有 instant/fast/normal/slow token，即 100/140/180/200ms 与最多 6px 位移；800ms progress token 只服务真实 loading/progress。无装饰 delay、全列表 stagger、bounce、elastic 或超调；无限 spinner 只能在真实未完成状态存在，终态立即停止并卸载。
- **实时表面禁区**：`FlowCanvas`、图像 viewport、ROI pointer/坐标投影、实时 camera frame、Canvas backing store 不做 CSS/Vue transform、crossfade 或基于 RAF 的装饰动画；Preview 折叠只允许图标/状态反馈，不动画 pane 高度。
- **状态与 Owner**：动画不得成为业务时钟或后端状态替身。timer、RAF、transition/animation listener 必须由当前 mounted capability owner 创建和清理；unmount、route leave、feature flag 切换和 reduced-motion 变化时可取消，不建立第二 EventBus 或 motion owner。
- **无障碍**：`prefers-reduced-motion: reduce` 下总 `duration + delay <= 1ms`、transform 位移为 0、无限动画为 0，信息、文本、图标、focus、ARIA 和终态仍完整；焦点不得在 exit 期间丢失，modal/menu/drawer 必须保持既有 trap、Escape、return-focus 和 screen-reader 顺序，业务逻辑不得依赖 `transitionend` 必然触发。
- **性能**：动效造成的 layout shift 为 0，交互不被动画阻塞，不产生可归因的 `>50ms` Long Task，不回退 Canvas/图像既有帧与 pointer 预算；同一时刻不启动页面级并发编舞。测试从 trigger 前一帧采样到 settled 后 200ms，使用 `PerformanceObserver` 记录全部 `layout-shift`（含 `hadRecentInput` 与 source target）和 `longtask`；以同 trigger 的 reduced-motion pair 作为业务 workload 基线，normal-only 或增量且与 motion lifecycle/target 重叠的条目计为 motion attributable，无法归因的条目保持 finding，不得静默忽略。

当前已存在 Button/Field/Navigation 等显式状态 transition、`CvModal`、`CvToastRegion` 和 AI drawer 动效；R2 优先统一和验证这些机制，并补齐共享 `CvMenu` 等真实连续性缺口，不把“动效数量增加”作为完成指标。

## 5. 证据、评审与状态协议

### 5.1 三类证据互不替代

| 层级 | 证明什么 | 不证明什么 |
| --- | --- | --- |
| E1 工程证据 | lint、typecheck、unit、build、bundle、合同、owner、overflow、console 与行为无回归 | 不证明高级感或人工偏好 |
| E2-I 内置浏览器迭代 | Codex 在当前 dirty/clean 候选上实际打开、交互、截图、观察并回改；快速证明方向和局部终态 | 不授予正式 Chromium/WebView2/DPI PASS，也不替代独立盲评 |
| E2-F 正式 Chromium | 仓库串行 Playwright 在固定 fixture、状态和候选 SHA 上形成可比矩阵 | 不证明真实 WebView2、native DPI、现场能力或生产接受 |
| E3 宿主/外部证据 | 真实 WebView2、Windows 125%、独立 no-Node、Remote CI、现场硬件、soak、Owner 签收 | 只证明实际运行范围，不自动替代人工视觉比较 |

所有历史证据均标为 `HISTORICAL_COMPARISON_ONLY`。R2 修改产品代码后，上一轮 `930/930`、`999/999`、`178 passed / 87 skipped` 与 WebView2 96 DPI 结果不得写成当前 PASS，必须重新运行才可授予。

### 5.2 可比性硬规则

before 与 after 必须固定：

- 同一 route、业务对象、数据 fixture、role/profile/feature flags 与状态；
- 同一 CSS viewport、窗口/client size、DPR/native DPI、theme、density 与 reduced-motion；
- 同一滚动位置、展开项、选中项、焦点与浮层；
- 相同截图裁剪、像素尺寸和采集宿主；
- 动态时间、随机 ID、动画和请求时序被固定或记录。

任一条件不同必须标记 `NON_COMPARABLE`，不可进入盲评或计算偏好。允许保留作诊断，但不能补数。可比性不由实施者手填授予：R2 validator 对 pair key、fixture snapshot/hash、请求摘要、截图尺寸、截图 SHA、viewport、theme、density、role、state、focus、scroll 和 hostKind 自动比对；缺字段或不一致时只能输出 `NON_COMPARABLE`。

### 5.3 截图与记录目录

```text
.tmp/studio-ui-next/view-polish-r2/<stage>/<source-sha-or-dirty-candidate>/<batch>/
  in-app-browser/
    iteration-01/
      before.png
      after.png
      dom-before.json
      dom-after.json
      interaction.json
      runtime.json
  before/
  candidate-a/
  candidate-b/
  final/
  manifest.json
  review.json
  blind-review.json
  findings.md
```

内置浏览器截图使用稳定命名 `<scene>-<state>-<viewport>-<theme>-<density>-<iteration>-<before|after>.png`。Codex 必须把截图实际载入视觉上下文，逐张检查全屏与必要局部，不得只验证文件存在、SHA、非空像素或让 detector 代替看图。正式 Playwright 与 WebView2 runner 继续使用脚本定义目录；R2 manifest 只引用真实路径和 SHA，不复制内置浏览器截图或旧证据冒充正式 run。

R2.0 必须先在以下现有测试目录下建立受版本控制的 schema 与 validator，不修改 `package.json` 也可直接用 Node 执行：

```text
ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/
  r2-evidence.schema.json
  r2-review.schema.json
  r2-motion.schema.json
  r2-in-app-browser.schema.json
  validate-r2-evidence.mjs
  motion-inventory.json
  r2-browser-fixture-session.mjs
  r2-in-app-browser-fixture.ts
  r2-visual-fixture.ts
  r2-visual.spec.ts
  r2-motion.spec.ts
```

`manifest.json` 至少记录：`schemaVersion`、`headSha`、`candidateContentHash`、`worktreeState`、stage、batch、route、state、fixtureId、fixtureHash、role、profile、flags、evidenceClass、hostKind、browserSurface、baseUrl、serverOwner/PID/port、viewport、window/client size、DPR/native DPI、theme、density、reducedMotion、motionProfile、motionClock、focus、scrollOwner、console/page errors、failed requests、unexpected writes、owner ledger、DOM box metrics、computed style metrics、before/after screenshot 路径与 SHA-256、DOM/interaction report、iteration、cleanup、pairId、comparability、nonComparableReasons 和 claimScope。manifest 不记录 cookie、token、密码、secret 或完整本地存储内容。

- `comparability` 只允许 `COMPARABLE` / `NON_COMPARABLE`；`evidenceClass` 至少区分 `IN_APP_BROWSER_ITERATION`、`REPOSITORY_PLAYWRIGHT`、`WINFORMS_WEBVIEW2`；`claimScope` 只允许 `DIRECTIONAL_BROWSER`、`FORMAL_CHROMIUM`、`WEBVIEW2_100`、`WEBVIEW2_125`、`NO_NODE_TARGET`、`FIELD`。`IN_APP_BROWSER_ITERATION` 只能配 `DIRECTIONAL_BROWSER`。
- dirty batch 的 `candidateContentHash` 由 validator 对 `HEAD + tracked diff + 当前白名单内 untracked files` 计算，不接受手工录入；正式 R2.7 必须 `worktreeState=CLEAN_SHA`、`headSha` 为 40 位小写 Git SHA 且等于 `git rev-parse HEAD`。
- validator 必须校验 JSON schema、必填字段、截图文件存在与 SHA-256、pair 可比性、组数、票数、百分比向上取整和 reviewer 完整性；失败退出码非 0，相关 Gate 不得 DONE。
- 所有 R2 Markdown/JSON/fixture 使用 UTF-8；PowerShell 读取时显式指定 `-Encoding UTF8`，并在真实 Windows/WebView2 中复核中文渲染。终端默认编码显示异常不直接判定源文件损坏。

### 5.4 动效证据协议

静态截图只能证明起点或终态，不能证明动效质量。每个新增或实质修改的 motion case 必须有正常模式与 reduced-motion 的成对证据，并记录：`motionId`、目的、capability owner、业务 trigger/state、DOM target、Vue/CSS mechanism、properties、duration/easing/delay、stable key 来源、取消与 dispose、reduced-motion 计算值、focus/ARIA 行为、采样窗口、`layout-shift` value/hadRecentInput/sources、Long Task start/duration/attribution、匹配基线、timer/RAF/listener cleanup。

- 正常模式用受控时钟采集固定时间点帧或短录屏，并补一张稳定终态截图；reduced-motion 以同一 trigger/state 证明近乎即时且仍有非动态状态线索。
- 静态视觉 before/after 必须冻结动画到同一确定状态；动效比较必须固定 trigger、输入、开始时间和采样时间。时钟、帧点或 reduced-motion 不一致时标记 `NON_COMPARABLE`。
- Motion Gate 要求动画不阻塞点击/键盘/API/route leave，不造成 layout shift 或 `>50ms` attributable Long Task，不降低 Canvas/图像预算，unmount 后 timer/RAF/listener 归零。
- 动画不能作为唯一状态线索；录屏偏好不能替代 end-state 可读性、任务成功率、a11y 或 Owner/authority 验证。

### 5.5 盲评协议

- 局部批次和阶段至少两名相互独立的评审者；最终 Gate 至少三名。评审者不知道哪张是 before、候选 A/B 或 final，展示顺序由工具随机并记录 seed。
- 每组先做强制偏好选择：左 / 右 / 无明显差异。选择左/右时对所选版本评分；选择 `无明显差异` 时必须分别为左右两版填写四项分数与理由，validator 取每项较低分参与 Gate，且该票不计为 final 偏好。
- 四项各 1-5 分：`visual_focus`、`material_hierarchy`、`operation_priority`、`cross_page_consistency`。
- 每项必须附一句可观察理由，不接受“更高级”“更舒服”等无证据形容词。
- 评审者意见相反时记录到 `disagreement`，由主协调 Owner 结合任务效率和项目边界裁决；不得删除反对意见。
- 有效票必须包含偏好、适用的四项分数和四条可观察理由；`无明显差异` 还必须包含左右两套评分。有效组必须通过 schema/comparability，并且每名评审者都提交有效票；结果揭盲后不得把失败组改为无效。
- 局部批次至少 4 组：B0 主状态、B2 压力状态、一个异常状态、一个第二主题或 density；每组 final 偏好票须达到 `ceil(2/3 * 有效票数)`，四项中位数均不低于 4，且无单项低于 3。只有两名评审者时意味着两票都必须偏好 final。
- 阶段至少为每个受影响 Scene 提供 4 组，且总数不少于 12；通过组数须达到 `ceil(0.80 * 有效组数)`，四项阶段中位数均不低于 4，无重复系统性 P2。
- 最终盲评固定至少 42 组：S00-S13 每个 Scene 各一组 B0 主状态、一组 B2 短屏主状态和一组异常/权限/长中文状态；通过组数须达到 `ceil(0.85 * 有效组数)`，42 组时至少 36 组。四项总中位数均不低于 4，Product Owner 另行明确签收；自动化和独立评审者都不能代签。

### 5.6 评分锚点与任务指标

| 分数 | 视觉焦点 | 材质层级 | 操作优先级 | 跨页一致性 |
| --- | --- | --- | --- | --- |
| 1 | 3 秒内无法指出当前对象、关键状态或主操作 | 主次区域混成一片或被重复边框切碎 | 主/次/危险动作混淆或核心动作不可达 | 看起来像不同产品，术语或控件语义冲突 |
| 2 | 只能找到三项中的一至两项，且有明显竞争焦点 | 能分区但主要依赖完整描边、卡片或大阴影 | 多个同权 primary，需搜索或猜测下一步 | 只有颜色/圆角相似，结构和动作仍明显漂移 |
| 3 | 三项都能找到，但需要阅读多块同权内容或短屏退化 | 层级可理解但偏平、偏碎或 dark/comfortable 失真 | 任务可完成，但位置、步骤或反馈不稳定 | primitives 一致，页面仍反复套用同一骨架或旅程跳变 |
| 4 | 3 秒内稳定指出三项，B2 与异常状态仍成立 | 主舞台、辅助面、浮层清楚且无冗余边界 | 一个主操作、次要/危险动作清楚，高频步骤不多于 before | 同一产品语法清楚，各页面又有符合任务的独特构图 |
| 5 | 除 4 外，视线顺序自然且无需帮助文案解释层级 | 除 4 外，light/dark、密度和状态切换都保持精确材质关系 | 除 4 外，键盘、等待、失败与恢复全过程位置和反馈连续 | 黄金旅程跨页对象、状态、动作和节奏均连续且无突兀页面 |

盲评之外，每个批次还必须记录交互任务指标：任务成功率 100%；高频任务点击/键盘步数不得多于 before；首次正确动作在 3 秒观察后可被指出；B2 关键操作不可达数、关键中文截断数、未记录嵌套滚动数和交互 layout shift 均为 0。`cross_page_consistency` 只对包含 2-4 个连续页面的截图组评分，不从单张页面臆测。

### 5.7 缺陷、状态词与阶段回写

缺陷仍使用 `P0-P3`：P0 数据/执行/权威风险；P1 核心任务、状态或短屏受阻；P2 层级、密度、对齐、可访问性或一致性明显影响效率；P3 纯细节。

计划状态只使用：`LOCKED`、`READY`、`IN_PROGRESS`、`BLOCKED_BY_CONTRACT`、`BLOCKED_BY_ENVIRONMENT`、`DONE`、`DEFERRED`。证据状态只使用：`NOT_RUN`、`NOT_PERFORMED`、`PASS`、`FAIL`、`PARTIAL`、`NON_COMPARABLE`。不得用 `DONE` 暗示未执行的外部门禁。

- `READY -> IN_PROGRESS`：当前 Owner 已建立 `batch-contract.md`，冻结输入、精确文件白名单、测试、fixture、pairId、候选数和预期产物，并同步本文件顶部 `CURRENT_STAGE`。
- `IN_PROGRESS -> DONE`：全部 checkbox、阶段 Gate、schema validator、证据索引、评审和 P0-P2 disposition 齐全；执行台账记录 Owner、日期、source SHA/content hash、命令、结果与证据路径。
- R2.1-R2.6 每个包含视觉变化的 batch 必须具有有效 E2-I before/after、Codex 视觉复审、DOM/interaction report 与 cleanup；内置浏览器临时不可用时继续完成可独立推进的实现和 E1，但该 batch/阶段保持 `BLOCKED_BY_ENVIRONMENT` 或 `IN_PROGRESS`，恢复后从同一 candidate content hash 补跑，不用其他截图来源代签。
- 任一必需证据为 `FAIL`、`PARTIAL`、`NON_COMPARABLE` 或 `NOT_RUN` 时阶段不得 `DONE`。只有明确标注 `NON_BLOCKING_EARLY_PROBE` 的 R2.0/R2.1 环境探针可保持 `NOT_PERFORMED` 而不阻塞内部实现，但它不授予任何 E3 PASS。
- R2.7 `DONE` 后，主协调 Owner 将 R2.8 从 `LOCKED` 改为 `READY`；R2.8 各外部项可以独立进入 `IN_PROGRESS`，但只有对应责任人签署后才可 `DONE`。

### 5.8 初始执行台账

| 阶段 | plan_status | evidence_status | reviewer_status | comparability | source | open_findings | Owner |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R2.0 | IN_PROGRESS | PARTIAL | NOT_PERFORMED | 42/42 Chromium 可比 | `22a3d26a0...` + dirty content hash | 内置浏览器与盲评未执行 | `COORD-R2-VISUAL` |
| R2.1-R2.6 | IN_PROGRESS | PARTIAL | NOT_PERFORMED | 42/42 Chromium 可比 | dirty candidate | 实现完成；内置浏览器/盲评/宿主 Gate 未执行 | 见阶段 |
| R2.7 | IN_PROGRESS | PARTIAL | NOT_PERFORMED | 42/42 Chromium 可比 | dirty candidate | clean SHA、盲评、WebView2 未执行 | `COORD-R2-EVIDENCE` |
| R2.8.1 Windows 125% | READY | NOT_PERFORMED | N/A | N/A | N/A | native 120 DPI 环境 | 环境 Owner |
| R2.8.2 独立 no-Node | READY | NOT_PERFORMED | N/A | N/A | N/A | 独立目标机 | Release Owner |
| R2.8.3 Remote CI | READY | NOT_PERFORMED | N/A | N/A | N/A | clean candidate run | CI Owner |
| R2.8.4 现场能力 | READY | NOT_PERFORMED | N/A | N/A | N/A | Camera/PLC/Station/AI | Field Owner |
| R2.8.5 Production soak | READY | NOT_PERFORMED | N/A | N/A | N/A | 生产环境 | Operations Owner |
| R2.8.6 Product Owner | READY | NOT_PERFORMED | NOT_PERFORMED | N/A | N/A | 人工签收 | Product Owner |
| R2.8.7 Production/Legacy | READY | NOT_PERFORMED | N/A | N/A | N/A | F10 治理决策 | Governance Owner |

阶段状态在本表、顶部状态块和 `R2_视觉精修执行台账.md` 同一文档批次更新；三处不一致时一律按较低状态处理，禁止择高宣称。

## 6. 场景与环境矩阵

### 6.1 场景

| ID | Surface | R2 核心观察 |
| --- | --- | --- |
| S00 | Setup / Login / Change Password | 产品身份、真实工作画面、表单节奏、错误与恢复 |
| S01 | Product Shell / Overview | active route、当前上下文、健康摘要、主入口 |
| S02 | Projects / Project detail | 搜索、最近工程、对象列表、生命周期与危险操作 |
| S03 | Workspace chrome | 产品栏、命令栏、算子区、Canvas、Inspector、Preview、状态栏比例 |
| S04 | Flow / Operators / Inspector | 选中、节点/端口、参数层级、校验与键盘焦点 |
| S05 | Image / Preview / ROI | 图像主导、工具密度、fresh/stale/error、ROI 编辑 |
| S06 | Save / Leave / Formal Run | dirty/conflict/unknown、准入、运行/停止、reconcile、OK/NG/执行错误 |
| S07 | Results | 态势扫描、筛选、执行/判定双轴、详情/证据/对比/导出 |
| S08 | Stations | 在线/离线/stale/版本异常、详情、只读与管理反馈 |
| S09 | Inspection | 工程选择、前置条件、RunConsole、保护原因与最近结果 |
| S10 | Settings | 组导航、长表单、保存范围、权限、设备与 secret 状态 |
| S11 | AI Workbench | intent 到 apply/recovery 的阶段焦点、上下文与进度 |
| S12 | Operators | 目录、搜索、分类、端口与参数 metadata 的可扫描性 |
| S13 | Diagnostics / About / Error pages | 故障优先级、可复制技术信息、版本与恢复动作 |

### 6.2 页面差异化与主舞台合同

R2.0 必须把下表展开为每个 Scene 的 annotated before；后续阶段在实现前冻结 final 目标。`主舞台` 是第一视口中应获得最高注意力的业务内容，`结构签名` 是该页面区别于其他页面的构图，不能用统一的“page header + 多块 Panel”替代。

| Scene | 主舞台与第一阅读对象 | 结构签名 | 主要辅助区 | 明确禁止 |
| --- | --- | --- | --- | --- |
| S00 | 登录/设置任务与可检视的真实 Studio 画面 | 工作台画面作为沉浸产品背景/主视觉，表单作为精确独立 stage，与品牌共同构图 | 版本、环境、恢复入口 | 小截图漂浮在大片暗场；营销口号；移动端完全失去产品身份 |
| S01 | 当前工程/系统健康/下一步动作 | Shell 提供稳定桌面 chrome，Overview 采用异常优先的工作入口带 | 最近动作、会话、低频全局入口 | 网站式 underline nav；同权 KPI 卡片墙 |
| S02 | 可搜索、可比较的工程集合或当前工程 | 搜索/筛选 command strip + 高密度 project list + 受控 recent region | 生命周期信息、导入导出 | 每个工程一张大卡；危险动作与打开同权 |
| S03 | Canvas 与当前流程 | 全幅 central stage + 窄工具 rail + inspector + 可收缩 preview/status | 保存、全局变量、最终判定 | page hero；多层 toolbar；Canvas 退化为装饰窗 |
| S04 | 当前选中节点/连线及其可编辑参数 | Canvas 选择与 Inspector 对象身份联动，错误靠近参数 | 算子搜索、端口摘要、minimap | 所有参数同权；错误只在顶部 alert |
| S05 | 实际图像、overlay 与 ROI | 暗色 image stage + 贴边精简工具 + 可读像素/输出信息 | 输入/输出切换、预览状态 | 工具/说明占据图像中心；占位图冒充主场景 |
| S06 | 当前保存/执行状态与立即可用的安全动作 | 状态条/RunConsole 与 Workspace 上下文连续，执行与判定双轴 | admission、诊断、近期结果 | OK/NG/执行失败只靠颜色；Run 与 Preview 同标签 |
| S07 | 结果集合、异常排序与当前调查对象 | filter rail/strip + dense result table + contextual investigation pane | 趋势、证据、导出 | marketing KPI 首屏；每条结果一张卡；详情脱离选中对象 |
| S08 | 工作站队列与当前工作站健康 | 状态排序的 dense fleet list + object detail workbench | 日志、包、命令、结果 | 在线/离线只靠彩点；Admin 命令混入只读摘要 |
| S09 | 可运行工程或正在进行的检测 | admission-oriented selector 或稳定 RunConsole stage | 最近结果、保护原因 | 大量说明先于控制；停止动作随状态跳位 |
| S10 | 当前设置组和保存范围 | persistent group navigation + single long-form work surface + sticky scoped feedback | 帮助、设备测试、危险区 | 每组/每字段卡片化；多个保存按钮同权；嵌套滚动 |
| S11 | Agent 当前阶段、任务上下文和下一决定 | stage timeline/context rail + one active work surface | history、diagnostics、资源限制 | clarification/plan/build 同时成为大面板；聊天气泡化 |
| S12 | 可搜索的算子目录和技术定义 | taxonomy/search + dense catalog + definition/metadata pane | 端口、参数、版本 | 电商卡片网格；Settings 表单骨架 |
| S13 | 当前故障与恢复动作，或产品/宿主身份 | issue-first diagnostic sheet；About 采用紧凑 identity + facts | 可复制技术细节、版本、许可证 | 协议 dump 占满首屏；About 做营销介绍 |

每个 Scene 的可比主状态必须使用固定、具产品语义的 fixture：可辨识工程名、流程节点/连线、真实形态的图像与 ROI、结果/Station/设置/AI 数据。占位线、空对象、随机 lorem 或仅有 skeleton 的截图不计入主状态。R2.0 记录主舞台、辅助区和 fixed chrome 的 DOM box 占比，后续 final 不规定统一百分比，但主舞台占比不得下降，除非盲评和任务指标同时证明改善。

### 6.3 环境

| ID | 环境 | 用途 |
| --- | --- | --- |
| B0 | 1920x1080 light/compact | 默认全量主场景与盲评基准 |
| B1 | 1536x864 light/compact | 中等窗口压缩与跨页构图 |
| B2 | 1366x768 或 client 1350x704 light/compact | 短屏压力；不冒充真实 125% |
| B3 | 1920x1080 dark/compact | dark 同等层级与材质 |
| B4 | 1920x1080 light/dark comfortable | density 投影与几何稳定 |
| B5 | 真实 WinForms/WebView2 Windows 100% | 宿主、窗口、Canvas 与清理证据 |
| B6 | 真实 WinForms/WebView2 Windows 125% | 120 DPI 独立 Gate，浏览器 DPR 不替代 |

每个实现批次至少覆盖 B0、B2、受影响的第二主题或 density，以及直接修改的交互/异常状态。所有 batch manifest 必须记录 Windows 标题栏占用后的 client size，并使用固定长中文样本检查对象名、字段名、错误原因和主操作。阶段出口覆盖本阶段全部适用场景的 B0-B4；R2.7 执行正式 Chromium/WebView2 矩阵。

R2.0 在环境可用时对 S00 与 S03 各执行一次 B5/B6 早期探针，R2.1 对 Design Lab 执行一次 B5/B6 探针。早期探针只用于尽早发现系统字体、client size、DPI 和 Canvas backing 风险，标记 `NON_BLOCKING_EARLY_PROBE`，不得授予 R2.7/R2.8 正式 PASS。B6 只有 `DPI_TYPE=NATIVE_WINDOW_DPI_OBSERVED` 且 `nativeWindow.dpi=120` 才算真实 125%；`force-device-scale-factor`、浏览器 DPR 或 native DPI 96 均只能记为预检/100%。

### 6.4 Codex 内置浏览器阶段矩阵

下表是每阶段最低 E2-I 浏览器任务，不替代该阶段完整 state matrix。认证 route 的 `:id`、role/profile/flags 和 API 响应来自 R2.0 的共享 fixture contract，不允许执行者用临时随机对象或真实写入凑截图。

| 阶段 | 目标 hash route | Codex 必做交互 | 最低截图/状态 |
| --- | --- | --- | --- |
| R2.0 | `#/login`、可用时 `#/projects` 或 `#/labs/design` | 验证 browser binding、start/status/stop、viewport 切换、截图载入与 DOM/console 采集 | B0/B2 before、一个错误或长中文状态、cleanup |
| R2.1 | `#/labs/design` | 逐项触发 hover/focus/press/menu/modal/toast/drawer/validation 与 normal/reduced-motion | B0/B2、light/dark、compact/comfortable、primitive normal/reduced pair |
| R2.2 | `#/login`、`#/overview`、`#/projects`、`#/projects/:id` | 登录 validation/busy/error、Shell 导航/menu/theme/density、搜索/选择工程、生命周期确认与返回 | 每个受改 Scene 的 B0/B2 before/after，empty/error/long-zh-CN 至少一组 |
| R2.3 | `#/projects/:id/workspace` | 选择节点、打开算子 flyout、编辑/validation、Preview/ROI、pane 极值、保存状态、run/stop/reconcile/leave guard | Canvas/Inspector/Preview B0/B2，dirty/conflict/running/NG/error，关键交互前后 |
| R2.4 | `#/results`、`#/stations`、`#/stations/:stationId`、`#/inspection`、`#/projects/:id/inspection` | 筛选与选择详情、返回上下文、只读/Admin 边界、run/stop、offline/stale/partial/unknown | 各 Scene B0/B2 主状态与异常状态，dense table/详情/RunConsole 前后 |
| R2.5 | `#/settings`、`#/ai`、`#/projects/:id/ai`、`#/operators`、`#/operators/:operatorType`、`#/diagnostics`、`#/about` | 长表单滚动与保存反馈、设备测试、AI stage/drawer/failure/recovery、目录搜索、诊断复制/恢复 | 每个受改 Scene B0/B2，long form/403/error/secret/AI active/long metadata |
| R2.6 | 连续走 `#/login` -> `#/projects` -> Workspace -> Results -> Settings/AI -> Diagnostics/About | 键盘旅程、focus return、Escape、theme/density/reduced-motion、短屏和全状态连续巡检 | S00-S13 核心 final 预审组及跨页 before/final 对照 |

每一行完成后，Codex 在台账中记录实际访问 route、成功/失败交互、截图与 manifest 路径、未构造状态和 finding disposition。路由存在不等于场景通过；若 fixture、权限或 feature flag 未真实满足，标记不可构造或失败，不能截图 403/empty 后冒充目标页面。

## 7. Owner 与文件边界

默认由一个主协调 Owner 串行实施。共享层和 Workspace 纵向链不可拆成多个并行实现 owner。

| Owner | 负责范围 | 硬边界 |
| --- | --- | --- |
| `COORD-R2-VISUAL` | `TODOViewR2.md`、R2 台账/证据索引、`src/design-system/**`、`src/app/layouts/ProductLayout.vue`、`product-layout.css`、`src/app/base.css`、`src/labs/design/**` | 不改变业务合同、Router 语义或 Host authority |
| `OWN-R2-ENTRY` | `src/app/pages/auth/**`、`src/capabilities/overview/**`、`projects-read/**` 的 presentation 与局部测试 | 会话、Project lifecycle 与 Leave Guard owner 不变；Shell 共享文件回到协调 Owner |
| `OWN-R2-WORKSPACE` | `src/capabilities/project-workspace/**`、关联展示与测试 | FlowCanvas + Inspector + Preview + ROI + save/run chrome 为同一纵向 owner |
| `OWN-R2-OPERATIONS` | `src/capabilities/results-read/**`、`stations-read/**`、`inspection-run/**` 的展示与局部测试 | 不创建第二 query/SSE/command/export/run owner |
| `OWN-R2-SUPPORT` | `src/capabilities/settings/**`、`ai-workbench/**`、`operators-read/**`、`about/**` 与现有 diagnostics/error presentation | Settings write 与 AgentRun/AI resource authority 不变 |
| `COORD-R2-EVIDENCE` | `tests/e2e/studio-ui-next/r2-visual/**`、受影响现有 UI tests、R2 manifest/review、现有 WebView2 证据调用 | 不创建第二 fixture server/runner，不篡改状态以制造截图；共享 test config/runner 由主协调 Owner 修改 |

始终视为共享文件：`package.json`、lockfile、Vite、TypeScript、ESLint、Router、ProductLayout/App Shell、tokens、Design System exports、API contracts、HostBridge、`.csproj`、CI、Feature Flags、根 `AGENTS.md`、`PRODUCT.md`、F10 与共享 ADR。实现时只有主协调 Owner 可修改；新增依赖、公共 API 删除/重命名或合同缺口必须暂停取得明确决策。

## 8. 每批固定执行循环

1. 读取当前 HEAD、worktree、F10、`PRODUCT.md`、当前阶段的 route/capability 源码和测试、legacy 对应入口、相关 owner/合同及上一批未关闭发现；共享架构输入按根 `AGENTS.md` 定位，找不到时报告，不猜造第二份。
2. 写一句任务合同：谁在此页面完成什么、最高频动作是什么、当前视觉阻力是什么。
3. 在当前 batch 根写入 `batch-contract.md`，冻结 Owner、精确文件白名单、输入合同、测试命令、fixture/state、pairId、候选数、任务指标、视觉预算、适用的 motionId/正常与 reduced-motion 结果及一个可证伪的假设，例如“取消三级完整边框后，主列表仍能在三秒内被识别且选中态更清楚”。
4. 按 8.1 启动或复用受控 UI fixture，并用 Codex 内置浏览器打开目标 hash route；实际检查当前页面后采集可比较 before。若内置浏览器不可用或现有截图不满足同状态规则，记录真实状态，不强行复用或偷换截图来源。
5. 对重要构图先产出最多两个候选，避免直接在唯一方案上不断堆补丁；候选必须同数据、同尺寸、同状态。
6. 主协调 Owner 审查候选，选择一个进入实现；记录为何淘汰另一方案。
7. 实施最小完整改动；Vue 继续使用 Composition API、`<script setup lang="ts">`、显式 props/emits 和单一状态源。
8. 在同一内置浏览器场景刷新或重新导航，恢复同一 fixture/state/focus/scroll 后采集 after；亲自查看全屏和关键局部，执行导航、主操作、键盘、浮层和受影响状态 smoke，并检查 console、page error、failed request、unexpected write、owner、focus、scroll、overflow、长中文、loading/error/disabled。发现 P0-P2 时依据截图和 DOM/交互事实回改，最多三轮。
9. 运行受影响 unit、lint/typecheck；涉及动效时追加 normal/reduced-motion、取消、unmount cleanup、layout shift、Long Task 和终态检查。确定候选后采集 final，动效使用固定帧/短录屏与终态截图，随后执行匿名盲评和 100% 像素级复审；发现 P0-P2 必须回改并重新评审。
10. 阶段出口运行完整前端 Gate 与适用 Playwright。静态视觉矩阵冻结动画到确定终态，动效矩阵固定 trigger/clock/sample；二者都必须使用独立端口/fixture，`workers=1`/`fullyParallel=false` 或等价串行入口。现有全量回归可保持仓库配置。正式 WebView2 只在 R2.7 执行。
11. 只有实现、工程证据、可比截图、评审记录、finding disposition 均齐全时才标 `DONE`，随后自动进入下一批。

任何包含 `.vue`、Vue Router、Pinia 或 Vite 的实现批次，开始编码前必须完整读取 `vue-best-practices` 的 `reactivity.md`、`sfc.md`、`component-data-flow.md`、`composables.md`，并在 `batch-contract.md` 中写组件责任图、props/emits、状态 owner 与 dispose 边界。route/page 继续作为 composition surface；因视觉拆分组件时不得复制 state/watch/request，超大 SFC 只有在责任边界清楚且合同不漂移时才拆。

满足白名单、架构、测试、P0/P1=0、P2 已关闭、盲评 Gate 和无外部操作时自动继续。只有合同/authority 缺口、新依赖或共享 API 决策、需要真实环境/人工签收、Git/删除/推送等外部动作，或同一构图三轮仍失败时才暂停。

### 8.1 Codex 内置浏览器视觉调试协议

内置浏览器是 R2.1-R2.6 每个视觉 batch 的默认观察和调试界面。Codex 必须先看页面再改、改完再看，并把截图真正载入视觉上下文进行判断。浏览器自动化是一个有界循环，不是无限自行改 CSS，也不是正式证据 runner。

#### A. 启动与浏览器绑定

1. 先检查内置浏览器能力是否可绑定；使用 Browser skill 规定的 in-app-browser 选择与文档，不得用 Computer Use、外部 Chrome 或独立 Playwright 偷换成“内置浏览器”。单次连接有界等待，失败一次可重连一次；仍失败则记录 `IN_APP_BROWSER_UNAVAILABLE` 与诊断，继续不依赖截图的代码/测试工作，并把该视觉 batch 标为 `BLOCKED_BY_ENVIRONMENT`，不得授予 E2-I PASS。
2. 从 `ClearVision.Product/tests/ClearVision.Product.UI.Tests/` 复用现有 Studio UI Next fixture 基础设施：`CV_UI_SCENARIO=studio-ui-next`、默认 `CV_UI_PORT=5177`、base URL `http://127.0.0.1:5177/studio/index.html`。页面使用 hash history，例如公开登录页为 `#/login`、初始化页为 `#/setup`；认证页面按 R2 route-state fixture 清单导航。
3. StudioUI 当前没有 `npm run dev`，Vite 也没有 API proxy。不得把裸 `vite` 静态页当成认证场景，不得为截图新增第二 API/mock server。R2.0.15 必须在现有 `studio-ui-next-server.cjs`、startup/auth fixture 和唯一 API route fixture 边界内建立可供内置浏览器访问的受控会话；当前 server 的 evidence phase 白名单只有 F01-F07/F09，执行前必须把 `r2` 显式加入既有白名单并验证 `.tmp/studio-ui-next/r2/browser-fixture` 约束，不能假定当前已支持。`r2-browser-fixture-session.mjs` 只管理 fixture server 的 start/status/stop，不得导入 browser-client 或代替 Browser skill 控制内置浏览器。
4. 启动前探测 ready URL 与端口 owner。只有健康检查、fixture hash 和 candidate content hash 均匹配才复用已有 5177；否则使用记录在 batch contract 中的隔离端口。记录 PID/port/baseUrl/serverOwner；只清理由当前 batch 创建且已核对 PID/路径的进程与临时目录，不停止用户或其他 worktree 的 server。

#### B. 固定场景与 before

1. 冻结 route、fixtureId/hash、业务 state、role/profile/flags、theme、density、viewport/client size、reduced-motion、focus、scroll、展开/选中对象、动态时间和随机 ID。至少采集 B0 1920x1080 与 B2 1366x768 或 client 1350x704；修改 theme/density 时追加对应 B3/B4。
2. 导航后等待应用 readiness marker、字体、数据、图像/Canvas fixture 和适用请求稳定；等待条件必须是 DOM/应用状态，不使用任意长 sleep。静态场景冻结动画在确定终态，动效场景按 motion clock 采样。
3. 截取完整 viewport；必要时追加一个不改变页面状态的关键区域截图。保存 before PNG、可见 DOM/accessibility 摘要、关键 DOM box/computed style、focus/scroll/overflow、console/page errors、failed requests、unexpected writes、owner ledger 与 screenshot SHA-256。
4. Codex 逐张查看截图并写 `findings.md`：只记录可观察事实、任务影响、P0-P3、目标文件/Owner 和预期修复；不得以“看起来土”“像苹果”或像素 detector `[]` 代替诊断。DOM/交互事实优先于纯截图猜测，截图仍必须由 Codex 亲自视觉复审。

#### C. 修改、复拍与有界迭代

1. 只修改 batch 白名单内的确切代码。完成一组逻辑完整的视觉改动后，等待 rebuild/readiness，在同一 browser binding/context 内刷新或重导航；若状态被污染，重新创建受控 tab/context 并重新注入同一 fixture，不沿用脏状态。每次产品文件变化都重新计算 candidate content hash；before/after pair 必须绑定各自真实 hash，只有同一轮固定输入的比较才可进入评审。
2. 恢复与 before 完全相同的 route/state/theme/density/viewport/reduced-motion/focus/scroll，执行同一交互脚本并采集 after。validator 比对场景字段、截图尺寸和 hash；不一致即 `NON_COMPARABLE`。
3. Codex 同时查看 before/after，检查主舞台、阅读顺序、表面层级、排版、对齐、控件状态、浮层 containment、长中文、短屏、dark/density 和关键操作可达性；再执行键盘、主操作、错误/恢复及适用 motion smoke。截图改善但行为、状态或 owner 退化时以失败处理。
4. 每轮写入 iteration、changed files、source/candidate content hash、finding disposition、before/after 路径与 SHA、DOM/interaction 差异、console/network、测试和 cleanup。P0/P1 立即停止并修复；P2 必须在当前 batch 关闭；P3 可在预算内收敛。自动视觉回改最多三轮，同一问题第三轮仍未通过则停止局部微调，回到任务合同/构图或交人工评审，不无限循环。R2.6 跨页发现回写原 Owner 后会产生新 content hash，必须重新启动该轮 S00-S13 巡检；累计轮数仍计入三轮上限，不把每个页面重新计三轮。

#### D. 收尾与证据边界

1. 关闭临时 tab/context，验证当前 batch 创建的 timer/RAF/listener/request/server/user-data 和端口已清理；可复用 server 只有在 owner、端口、fixture/content hash 与下一 batch contract 一致时保留，并在台账中显式转交。
2. 内置浏览器 manifest 固定 `evidenceClass=IN_APP_BROWSER_ITERATION`、`claimScope=DIRECTIONAL_BROWSER`、`hostKind=IN_APP_BROWSER`，native DPI/WebView2/no-Node/field 全部为 `NOT_PERFORMED`。它可以解锁本地继续回改，但不能单独把阶段标为 `DONE`。
3. R2.7 必须在 clean SHA 上重新运行仓库正式 Playwright、串行 S00-S13 与 WinForms WebView2。不得把内置浏览器 PNG 改名、复制或重新登记为 `FORMAL_CHROMIUM`/`WEBVIEW2_*`，也不得用内置浏览器的 DPR 冒充 Windows 125%。

## 9. 阶段总览

| 阶段 | 目标 | 状态 | 解锁条件 |
| --- | --- | --- | --- |
| R2.0 | 冻结当前基线、可比性与盲评方法 | IN_PROGRESS / IMPLEMENTATION_COMPLETE | 内置浏览器与盲评仍未执行 |
| R2.1 | 建立旗舰级设计语言与质量防线 | IN_PROGRESS / IMPLEMENTATION_COMPLETE | 内置浏览器、盲评与宿主复核仍未执行 |
| R2.2 | Auth、Shell、Overview、Projects | IN_PROGRESS / IMPLEMENTATION_COMPLETE | 内置浏览器与盲评仍未执行 |
| R2.3 | Workspace、Canvas host、Inspector、Preview、Run | IN_PROGRESS / IMPLEMENTATION_COMPLETE | 内置浏览器与盲评仍未执行 |
| R2.4 | Results、Stations、Inspection | IN_PROGRESS / IMPLEMENTATION_COMPLETE | 内置浏览器与盲评仍未执行 |
| R2.5 | Settings、AI、Operators、Diagnostics、About | IN_PROGRESS / IMPLEMENTATION_COMPLETE | 内置浏览器与盲评仍未执行 |
| R2.6 | 全产品排版、动效、状态与响应式收口 | IN_PROGRESS / IMPLEMENTATION_COMPLETE | 内置浏览器与盲评仍未执行 |
| R2.7 | Chromium、Playwright 与真实 WebView2 证据 | IN_PROGRESS / LOCAL_CHROMIUM_COMPLETE | clean SHA、盲评与 WebView2 仍未执行 |
| R2.8 | 125%、no-Node、Remote CI、现场与 Owner 验收 | READY | 需要对应外部环境与责任人 |

## 10. R2.0：基线与审美验收冻结

**Owner**：`COORD-R2-VISUAL`。**目的**：让“更优雅、更高级”成为可比较的判断，而不是实施者自评。

- [x] **R2.0.1 当前事实冻结**：记录 branch、HEAD、upstream delta、worktree 归属、F10 状态、上一轮实现/证据 SHA；未跟踪用户目录不清理、不纳入计划产物。
- [x] **R2.0.2 页面与状态清单**：从 Router、role/profile/flags、页面与 tests 重新生成 S00-S13 的 route-state-owner 清单，不从旧计划抄写当前事实。
- [x] **R2.0.3 能力防退化矩阵**：按 legacy/Next 当前代码，把入口、步骤、状态、错误、权限与写入标为已等价保留、已优化保留、已重定位、只读接受、按 profile 隐藏、明确延后或缺失/回归。
- [x] **R2.0.4 当前视觉基线**：在可用的同一采集宿主捕获 S00-S13 的 B0/B2 before，核心 S00-S07 追加 B3/B4；记录不可构造状态，不伪造。
- [x] **R2.0.5 结构化诊断**：将发现归入视觉焦点、材质层级、排版、操作优先级、状态、密度、跨页一致性、交互、文案、a11y 或架构，不再用“土/不高级”作为任务描述。
- [ ] **R2.0.6 盲评试运行**：用登录、Projects、Workspace、Results 四组历史/current 截图验证匿名顺序、评分表、分歧记录和可比性拒绝规则。
- [x] **R2.0.7 参考板冻结**：只记录可转译原则：内容优先、稳定比例、低噪声表面、精确排版、状态连续性；不收集无法落地的营销页或 macOS 装饰截图。
- [x] **R2.0.8 页面构图合同**：为 S00-S13 产出 annotated before、主舞台/辅助区 DOM box 占比、结构签名、禁用骨架和 fixed chrome 预算；不得只产出 token/组件问题清单。
- [x] **R2.0.9 Schema 与验证器**：在 `R2_VALIDATION_ROOT` 建立 manifest/review schema、可比性/content hash/盲评计数 validator 和独立串行 visual fixture/config；不得依赖当前 `playwright.config.ts` 的 `fullyParallel: true`。先用故意缺字段、错 SHA、错截图 hash、错 viewport、并行污染与不足票数的负样本证明会 FAIL。
- [x] **R2.0.10 执行台账与证据索引**：建立 `R2_EXECUTION_LEDGER`、`R2_EVIDENCE_INDEX`，记录每阶段 Owner、状态、日期、batch contract、source/content hash、命令、结果、证据路径、reviewer 与开放 finding。
- [ ] **R2.0.11 早期宿主探针**：环境可用时对 Auth/Workspace 运行 B5/B6；native 120 DPI 缺失则明确 `NON_BLOCKING_EARLY_PROBE=NOT_PERFORMED`，不得以 DPR 替代或阻塞可在本机推进的 R2.1-R2.6。
- [x] **R2.0.12 外部证据适配审计**：现有 `Test-StudioUiDpiEvidence.ps1` 未以 native 120 DPI 作为通过条件，现有 no-Node summary 又把本机 sanitized-path/runtime audit 与独立 no-Node 目标机分开。R2 必须在独立 evidence batch 中补充或外包裹严格 validator：96 DPI/force-scale 与 `cleanMachineWithoutNode=NOT_PERFORMED` 的负样本必须 FAIL 对应 R2.8 Gate；不得直接沿用脚本汇总的宽泛 `PASS`。
- [x] **R2.0.13 R2.1 白名单**：把系统性问题与 capability-local 构图问题分开，明确哪些必须由 tokens/primitives/Lab 解决，禁止页面重复打补丁或让 Design System 代替页面构图。
- [x] **R2.0.14 动效盘点与验证合同**：从 tokens、CSS、Vue `<Transition>`/`<TransitionGroup>`、spinner/progress、timer/RAF/listener 和 capability owner 生成 `motion-inventory.json`；每项记录 purpose、trigger、target、mechanism、properties、duration/easing/delay、key、owner/dispose、reduced-motion、focus/ARIA 与风险。标出 `CvMenu` 等连续性缺口以及 FlowCanvas、实时图像、ROI、splitter、SSE/大列表禁区；用任意时长、`transition: all`、布局属性、对象 key、缺 reduced-motion、缺 cleanup、禁区目标等负样本证明 schema/validator 会 FAIL。
- [x] **R2.0.15 内置浏览器会话与共享 fixture 适配**（实施 Owner：`COORD-R2-EVIDENCE`，由 `COORD-R2-VISUAL` 验收）：在 `R2_VALIDATION_ROOT` 建立 `r2-browser-fixture-session.mjs` 的 `start/status/stop` 合同，复用并最小扩展现有 `tests/support/studio-ui-next-server.cjs`/global setup：将 `r2` 加入当前仅含 F01-F07/F09 的 evidence phase 白名单，使 `CV_STUDIO_UI_EVIDENCE_PHASE=r2` 只写 `.tmp/studio-ui-next/r2/browser-fixture`，并以 unknown phase、越界输出路径和错误 owner 负样本证明会 FAIL。会话描述至少输出 ready URL、PID/port/serverOwner、source/content/fixture hash、createdByBatch 与 cleanup token。该脚本只管理 fixture server，内置浏览器仍必须通过 Browser skill/browser-client 的 in-app-browser binding 操作。先生成 `browser-fixture-capability.json`，逐个 Scene 记录现有 F02-F07 startup/auth/API fixture 是 `DIRECTLY_REUSABLE`、`REQUIRES_SHARED_EXTRACTION`、`PUBLIC_ROUTE_ONLY` 或 `UNAVAILABLE` 及原因；再抽取共享确定性 route-state 定义，让内置浏览器与正式 Playwright 消费同一合同。根据当前 Browser skill 的真实能力选择 tab request interception，或在同一个既有 fixture server 内提供 test-only responder，禁止第二 server、产品代码 fixture 分支、真实写入、secret 采集和修改 `package.json` 只为方便启动。
- [ ] **R2.0.16 Codex 内置浏览器演练**：由 Codex 实际绑定 in-app browser，运行 `start -> status -> open -> interact -> screenshot -> inspect -> stop`。至少用 `http://127.0.0.1:<isolated-port>/studio/index.html#/login` 完成 B0/B2 before，并在共享 fixture 可用时追加 Projects 或 Design Lab 认证场景；逐张载入截图，记录一组真实 finding、DOM/interaction report 与 cleanup。用错误 viewport、route/state、fixture hash、截图 hash、server owner、缺 cleanup、伪造 WebView2/DPI claim 和超过三轮 iteration 的负样本证明 validator 会 FAIL；若当前内置浏览器连接不可用，记录 `NON_BLOCKING_EARLY_PROBE=NOT_PERFORMED`，不阻塞 schema/adapter 建设，也不得伪称演练通过。

**R2.0 Gate**：S00-S13 的 B0/B2 计划样本中，可比 before 不少于 26 组且可比较率不低于 90%；四个试评页面各至少 4 组、共至少 16 组，评审与 validator 可复现；串行 visual config、motion schema、in-app-browser schema/session adapter 与 DPI/no-Node 严格适配器的负样本通过；内置浏览器演练实际结果或诚实的 `NON_BLOCKING_EARLY_PROBE=NOT_PERFORMED` 已入账；页面构图合同、动效盘点、台账和索引存在；所有 P0/P1 有 owner；Workspace owner 无争议；没有把历史 PASS 写成当前证据。

## 11. R2.1：设计语言与专业质量防线

**Owner**：`COORD-R2-VISUAL`。**目的**：先建立足以支撑旗舰完成度的系统语法，再进入页面。

- [x] **R2.1.1 表面与边界预算**：明确 app/page/work/raised/floating/canvas 六级用法；为每类页面设完整描边上限，移除同权重白框堆叠和双重边界。
- [x] **R2.1.2 排版重新定标**：以中文扫描效率重定 page title、object title、section、body、secondary、caption、numeric；拉开字号/字重/灰度的有效差异，保持 `letter-spacing: 0` 和固定字号。
- [x] **R2.1.3 布局节奏**：建立页面、工作台、面板、字段、表格与 toolbar 的纵横节奏；禁止随机 2/6/10px one-off 和仅靠 padding 制造层级。
- [x] **R2.1.4 控件精度**：复核 Button/IconButton/Field/Search/Select/Tabs/Menu/Modal/Table/Status 的轮廓、文本基线、图标光学尺寸、disabled/loading/focus 与命中区。
- [x] **R2.1.5 选择与导航语法**：定义 active route、selected row/node/tab、hover、focus 和 current object 的不同结构线索，避免全靠蓝底/边框/下划线。
- [x] **R2.1.6 状态语法**：让 OK、NG、执行错误、Warning、Info、Idle、Offline、Unknown、Disabled、Conflict 在颜色之外具有标签、图标或布局差异。
- [x] **R2.1.7 Elevation 与浮层**：只有 menu/popover/modal/toast/真正 raised tool 使用 elevation；统一边缘、遮罩、viewport containment、focus return 与滚动。
- [x] **R2.1.8 Design Lab 场景化**：不只陈列组件，增加 page header、data region、workbench pane、command cluster、status stack、long-zh-CN、light/dark 与 density 的组合场景。
- [x] **R2.1.9 视觉契约测试**：为 token 语义、focus、reduced motion、稳定尺寸、无嵌套卡片和关键对比度建立可维护 guard；不得用脆弱像素值锁死所有页面。
- [ ] **R2.1.10 Lab 盲评与回改**：至少比较两种表面/排版组合，选定后完成一次跨 light/dark/compact/comfortable 回改闭环。
- [x] **R2.1.11 构图原型**：在 Lab 中用真实语义 fixture 搭建 Auth、dense list、Workspace、investigation、long form、AI stage 六类主舞台原型；记录区域比例、表面关系和淘汰方案，证明系统语言支持页面差异化而非一种骨架。
- [ ] **R2.1.12 早期宿主复核**：环境可用时对 Design Lab 执行 B5/B6，核查系统字体、中文基线、client size、surface 与控件光学尺寸；B6 仍只接受 native 120 DPI。
- [x] **R2.1.13 Motion Lab 与 primitive 合同**：用真实语义状态建立 control press/focus、navigation selection、menu、modal、toast、drawer、validation、loading/progress 的 normal/reduced-motion 成对 specimen；统一 token、enter/leave 对称性、stable key、focus/ARIA 与 cleanup。优先补齐共享 `CvMenu` 等已证实缺口，不新增装饰 specimen、route transition 或动画依赖。
- [ ] **R2.1.14 Codex 浏览器精调**：按 6.4/8.1 在 `#/labs/design` 完成 B0/B2/light/dark/density 与 normal/reduced-motion before/after；Codex 逐张检查 primitives 和六类构图原型，最多三轮关闭 P0-P2，保存 DOM/interaction、finding disposition 与 cleanup。

**R2.1 Gate**：Design Lab、六类构图原型与 Motion Lab 的 B0-B4 通过；Codex 内置浏览器 E2-I 精调与 cleanup 通过；至少 12 个有效可比组、80% 以上偏好 final，四项盲评分中位数均不低于 4；normal/reduced-motion pair 完整，layout shift 与 attributable Long Task 超预算数为 0；系统性 P0/P1/P2 为 0；未新增 one-off 基础设施；完整 lint/typecheck/unit/build/bundle 通过。早期 B5/B6 只记录风险，不冒充 R2.7/R2.8。

## 12. R2.2：入口、壳层与工程入口

**Owner**：`OWN-R2-ENTRY`，共享 Shell/Design System 由 `COORD-R2-VISUAL` 串行协调。

- [x] **R2.2.1 Auth 构图重做**：重新分配产品预览与表单比例，让真实 Studio 工作画面成为第一视口信号；避免缩小截图漂在大暗场中，移动/短屏也保留适量产品身份。
- [x] **R2.2.2 Auth 细节**：精修品牌尺寸、标题层级、字段节奏、密码/错误/忙碌状态、回车与恢复路径；不暴露 API/profile/authority 语言。
- [x] **R2.2.3 Product Shell 导航**：从网站式顶栏转为成熟桌面产品 chrome；建立当前域、active route、对象上下文、全局状态与会话操作的稳定层级。
- [x] **R2.2.4 Shell 微交互**：menu、tooltip、focus、selected、theme/density 切换和长用户名保持精确、快速、无 layout shift；不仿 macOS。
- [x] **R2.2.5 Overview 构图**：以当前工程、系统健康、最近异常和可执行入口组织首屏；减少同权 KPI 块，让异常和下一步比装饰数字更突出。
- [x] **R2.2.6 Projects 扫描层**：统一搜索、最近工程、主列表、类型/时间/状态、只读与空态；列表承担密集信息，不把每个工程变成大卡片。
- [x] **R2.2.7 Project 生命周期**：创建、打开、关闭、导入、导出、删除、dirty/conflict/unknown 的动作层级和反馈保持清楚；危险操作与品牌强调分离。
- [x] **R2.2.8 跨页连续性**：Auth -> Projects -> Overview/Project detail 的品牌、标题、宽度、对象名称和返回路径连续，不出现视觉换软件感。
- [ ] **R2.2.9 入口盲评**：S00-S02 在 B0/B2/B3/B4 完成可比评审，至少覆盖 validation、auth error、empty、data、long-zh-CN、readonly 与 lifecycle error。
- [x] **R2.2.10 入口功能性动效**：为 Auth validation/busy/result、Shell menu、active navigation indicator、tooltip 和受控页面状态反馈建立 100-200ms 内的状态/空间连续性；ARIA/error 与 session/route 状态立即更新，不做登录背景循环动画、全局 route crossfade、页面入场 stagger 或用 exit 延迟 Leave Guard。
- [ ] **R2.2.11 Codex 浏览器精调**：按 6.4/8.1 实际走 `#/login` -> `#/projects` -> `#/overview`/`#/projects/:id`，完成验证、错误、菜单、主题/密度、搜索、生命周期、long-zh-CN 的 B0/B2 before/after 与最多三轮回改；不得只截登录 happy path。

**R2.2 Gate**：登录、Shell、Overview、Projects 阶段可比组 80% 以上偏好 final；Codex 内置浏览器 E2-I 旅程、DOM/interaction 与 cleanup 通过；四项中位数均不低于 4；B2 无关键操作丢失；入口 motion normal/reduced pair、焦点与终态通过且不阻塞交互；会话、Leave Guard、Project lifecycle 与权限语义无回归。

## 13. R2.3：Workspace 主工作台

**Owner**：`OWN-R2-WORKSPACE`，不得把 FlowCanvas、Inspector、Preview、ROI、保存与 Formal Run 拆给多个实现 owner。

- [x] **R2.3.1 空间预算**：重新冻结 Product chrome、Workspace command bar、operator rail、Canvas、Inspector、Preview、run/status bar 的比例；先保证 Canvas/图像主导，再压缩低价值 chrome。
- [x] **R2.3.2 Command hierarchy**：保存、撤销/重做、预览、正式运行/停止、全局变量、最终判定和低频工具形成稳定分组；常用图标配 tooltip，主操作不泛滥。
- [x] **R2.3.3 Operator Rail/Flyout**：提升搜索、分类、当前选择、拖拽/点击添加和长算子名的扫描效率；flyout 是工具，不是卡片目录墙。
- [x] **R2.3.4 Canvas host**：精修背景、网格、节点、端口、连线、selection、minimap、空画布与缩放工具，但继续复用 canonical FlowCanvas，不修改其 authority/pointer 语义。
- [x] **R2.3.5 Inspector**：以节点身份、错误、高频参数、分组、单位、依赖和高级参数建立明确层级；字段密度高但不挤，长中文和 validation 可定位。
- [x] **R2.3.6 Preview/Image/ROI**：让图像成为视觉主体；工具条退后，输入/输出、fresh/stale/error、overlay、pixel probe、ROI edit 与撤销/重做清楚可达。
- [x] **R2.3.7 Splitter 与折叠**：在 B0/B2/B4 下验证 pane 极值、Preview 收缩、Inspector 可用宽度和唯一滚动 owner；动态内容不得推动主布局。
- [x] **R2.3.8 Save/Leave**：dirty、saving、saved、conflict、unknown outcome、readonly 与 reconcile 的位置、层级和中文下一步一致，正式 PersistenceRevision 不与本地草稿混淆。
- [x] **R2.3.9 Formal Run safety**：admission、运行中、停止、断线、reconcile、OK、NG、执行错误、unknown 形成清楚双轴；停止/危险控制稳定可见，不用颜色单独传达。
- [ ] **R2.3.10 Workspace 盲评**：S03-S06 以无选中、节点选中、参数错误、fresh/stale image、ROI edit、dirty/conflict、running/NG/error 为可比组，覆盖 B0/B2/B3/B4。
- [x] **R2.3.11 Workspace 功能性动效**：只为 Operator flyout、menu/modal、control press/focus、selection/status 与保存/运行结果反馈提供受控动效；FlowCanvas/节点坐标/连线/实时图像/ROI pointer 不做 CSS/Vue transform 或 crossfade，Preview/pane/splitter 不做高度宽度动画，Leave Guard、保存、运行与停止不等待动效。
- [ ] **R2.3.12 Codex 浏览器精调**：按 6.4/8.1 在 `#/projects/:id/workspace` 实际选择节点、打开 flyout、编辑/校验、操作 Preview/ROI、pane 极值、保存与 Formal Run 状态；对 B0/B2、dirty/conflict/running/NG/error 采集 before/after，逐图复审 Canvas/图像主导性和关键操作可达性，最多三轮关闭 P0-P2。

**R2.3 Gate**：Canvas/图像在核心截图中是最强视觉区域；Codex 内置浏览器 E2-I Workspace 交互、DOM/owner 与 cleanup 通过；B2 首屏核心操作与状态可达；盲评中视觉焦点和操作优先级中位数至少 4；motion 不改变 Canvas/image/pointer/pane 几何与性能，owner ledger、timer/RAF/listener dispose 归零；保存、Canvas、Preview 与 Formal Run 合同无回归。

## 14. R2.4：生产运行与调查页面

**Owner**：`OWN-R2-OPERATIONS`。三个 capability 可分批串行，不能共享或复制 query/SSE/command/run owner。

- [x] **R2.4.1 Results 态势层**：让筛选、时间范围、来源、执行/判定双轴、吞吐/缺陷摘要和异常列表形成扫描路径；减少同权指标容器。
- [x] **R2.4.2 Results 调查层**：详情、诊断、证据、对比、partial/expired/not-produced 和导出反馈围绕当前结果组织，技术信息按需展开。
- [x] **R2.4.3 Stations 总览**：在线、离线、stale、warning、版本/运行包异常通过排序、分组、图标和文案共同表达；表格与列表保持高密度。
- [x] **R2.4.4 Station 详情**：健康、日志、运行包、结果与命令反馈构成一个对象工作台；Operator 只读与 Admin 操作在层级和 affordance 上明确。
- [x] **R2.4.5 Inspection 选择页**：工程、准入条件、权限、stale/partial/empty 和恢复动作清楚；401/403 不伪装成无数据。
- [x] **R2.4.6 Inspection RunConsole**：运行、停止、进度、保护原因、连续运行、结果与最近记录位置稳定；执行成功但 NG 与执行失败不可混淆。
- [x] **R2.4.7 跨页调查连续性**：Overview/Inspection/Station -> Results -> Detail -> 返回时保留对象、来源和筛选上下文。
- [ ] **R2.4.8 生产页盲评**：S07-S09 覆盖 data/empty/loading/offline/stale/warning/partial/unknown/readonly/admin feedback，按 B0-B4 适用组合比较。
- [x] **R2.4.9 生产页状态动效**：只对用户触发的筛选/详情揭示、命令反馈和低频边界状态提供短动效；Results、Stations、Inspection 的 SSE/poll/连续运行事件不逐条动画，dense table 不 stagger、不整表 crossfade，OK/NG/执行错误/Offline/Unknown 的文字与图标立即投影。
- [ ] **R2.4.10 Codex 浏览器精调**：按 6.4/8.1 连续操作 Results/Stations/Inspection 的筛选、详情、返回、权限、run/stop 和异常状态；每个 Scene 采集 B0/B2 主状态与 offline/stale/partial/unknown before/after，检查 dense list 扫描、命令反馈、状态双轴与 SSE 不抖动，最多三轮关闭 P0-P2。

**R2.4 Gate**：阶段可比组 80% 以上偏好 final；Codex 内置浏览器 E2-I 生产旅程、DOM/interaction 与 cleanup 通过；三项扫描任务在三秒内识别目标；无页面级水平滚动或固定 chrome 遮挡；高频事件、dense table 与连续运行无逐事件动画或主线程预算回退；query/SSE/command/run owner 单一；权限与状态语义无回归。

## 15. R2.5：设置、AI 与支持页面

**Owner**：`OWN-R2-SUPPORT`。按 capability 串行推进，Settings 与 AI 的共享状态 owner 不拆分。

- [x] **R2.5.1 Settings 框架**：把组导航、当前设置对象、保存范围、dirty/保存反馈和长表单滚动做成稳定工作台，不做独立卡片墙。
- [x] **R2.5.2 Settings 表单精度**：通用、存储、运行时、数据库、安全、用户、Camera、PLC/TCP/Station 与 AI model 的 label/help/unit/secret/validation/readonly/unknown 一致而可扫描。
- [x] **R2.5.3 设备工作台**：发现、绑定、连接测试、单帧/连续预览、停止、收发调试和错误恢复具有清楚主次；fixture 与真实设备状态明确区分。
- [x] **R2.5.4 AI 信息架构**：intent、clarification、plan、build、validate、apply preview、cancel、failure、recovery、history 每阶段有唯一焦点，任务上下文持续可见。
- [x] **R2.5.5 AI 进度与结果**：长运行状态、待确认参数、资源限制、失败诊断和 Apply/Undo 不堆叠同权面板；不新增 AgentRun/EventStore/resource authority。
- [x] **R2.5.6 Operators**：目录、分类、搜索、详情、端口与参数 metadata 采用适合技术资料的密集列表/描述布局，不套用 Settings 或 marketing card。
- [x] **R2.5.7 Diagnostics/About/Error**：首屏突出问题、影响和恢复动作；版本、宿主、后端、许可证与可复制技术信息层级清楚，不默认倾倒协议细节。
- [ ] **R2.5.8 支持页盲评**：S10-S13 覆盖长表单、403、save success/failure/unknown、secret、AI active/failure/recovery、长 metadata 与 service fault。
- [x] **R2.5.9 支持页功能性动效**：Settings validation/save、设备测试反馈、AI drawer/stage 切换及 Diagnostics disclosure 只使用低频状态/空间连续性；AI token/event stream、长表单各字段、Operators dense catalog 不逐项动画，AgentRun 阶段权威、Apply/Undo 与错误呈现不等待动画。
- [ ] **R2.5.10 Codex 浏览器精调**：按 6.4/8.1 实际操作 Settings 长表单/保存/设备反馈、AI stage/failure/recovery、Operators 搜索/详情和 Diagnostics/About；覆盖 B0/B2、403/error/secret/long metadata，逐图复审滚动、浮层、主操作和状态连续性，最多三轮关闭 P0-P2。

**R2.5 Gate**：低频页面与主产品共用视觉语法但保留任务特征；阶段可比组 80% 以上偏好 final；Codex 内置浏览器 E2-I 支持页旅程、DOM/interaction 与 cleanup 通过；normal/reduced-motion 下 Settings/AI 状态、焦点和操作终态一致；Settings write 与 AI owner 唯一；B2 长表单、浮层和操作区无越界或双层滚动。

## 16. R2.6：跨产品精度收口

**Owner**：`COORD-R2-VISUAL`。只在各页面构图稳定后做，避免用全局补丁掩盖局部结构问题。

- [x] **R2.6.1 排版巡检**：逐页检查标题级别、中文基线、数字/单位、metadata、帮助和错误；消除相同字号/灰度承载不同层级的情况。
- [x] **R2.6.2 几何巡检**：统一列、基线、控件高度、图标光学尺寸、toolbar 组距、表头/行与 pane header；修复 1-3px 的系统性错位，而非无意义像素洁癖。
- [x] **R2.6.3 边界预算巡检**：统计完整描边、shadow、badge、pill、panel nesting，关闭跨页重复的边框驱动问题。
- [x] **R2.6.4 动作一致性**：同一保存、运行、停止、刷新、筛选、缩放、撤销/重做、导出和危险命令使用同一图标、名称、层级和反馈。
- [x] **R2.6.5 全状态韧性**：逐页覆盖 loading、empty、401、403、offline、stale、partial、conflict、unknown、aborted、readonly、disabled、long-zh-CN 与最大合理数值。
- [x] **R2.6.6 键盘与焦点**：验证 skip link、Tab、toolbar 方向键、Escape、focus trap/return、route focus、Canvas/ROI 快捷键与焦点可见性。
- [x] **R2.6.7 Motion 全产品巡检**：以 `motion-inventory.json` 逐项核对 purpose、机制、token、属性、stable key、normal/reduced-motion、focus/ARIA、取消、dispose 与性能；移除无目的动效、任意 duration/delay、`transition: all`、布局动画、全局 route transition、全列表 stagger、长期 timer/RAF 和 Canvas/实时事件禁区动画，并验证动画不是唯一状态线索。
- [x] **R2.6.8 响应式与滚动**：每轴唯一 scroll owner；修复短屏、长中文、modal/menu 边缘、sticky 遮挡、splitter 极值和水平滚动。
- [x] **R2.6.9 全产品连续巡检**：连续走 S00-S13，检查局部优秀但跨页节奏、表面或控件语义不一致的问题。
- [ ] **R2.6.10 最终盲评预审**：从各阶段抽取核心组，随机混入上一轮 before，确认 final 偏好不是由标签、数据或截图差异造成。
- [ ] **R2.6.11 Codex 浏览器全旅程收口**：按 6.4/8.1 在同一 candidate content hash 上连续走 S00-S13，逐张查看核心 final、执行键盘/focus/Escape/theme/density/reduced-motion/短屏旅程，将跨页视觉或交互漂移回写至原 Owner；最多三轮全局收口，完整记录 cleanup 与未构造场景。

**R2.6 Gate**：P0/P1=0；P2=0 或仅保留有明确 owner、影响范围和关闭日期的非系统性项；无重复系统性 P3；Codex 内置浏览器 S00-S13 E2-I 全旅程、DOM/interaction 与 cleanup 通过；motion inventory 无未裁决项，normal/reduced-motion 的交互、a11y、layout shift、Long Task 与 cleanup Gate 通过；阶段盲评 Gate 通过；完整 lint/typecheck/unit/build/bundle 与适用 a11y/Playwright 通过。

## 17. R2.7：当前候选的正式本机证据

**Owner**：`COORD-R2-EVIDENCE`。任何产品代码变化都会使本阶段的 SHA-bound 证据失效。

- [ ] **R2.7.1 候选冻结**：取得明确 commit 授权后，先 `git fetch origin --prune` 并检查远端无不兼容前进/分叉，再形成 clean source SHA；未授权前保持 dirty candidate，不伪称冻结。
- [x] **R2.7.2 软件 Gate**：在候选 SHA 上运行 StudioUI lint、typecheck、full unit、production build、bundle gate/reproducibility；需要 legacy/UI contracts 时串行运行并分别报告。当前证据绑定 dirty candidate，因此只授予本机 E1 PASS，不冒充 clean-SHA Gate。
- [x] **R2.7.3 正式 Chromium**：运行 `CV_UI_SCENARIO=studio-ui-next` 的相关全量 Playwright；现有配置只提供 Chromium project，报告必须写 `CHROMIUM_ONLY`。evidence-only skip 保留真实原因，不用占位 SHA 或篡改 fixture 强行通过。
- [x] **R2.7.4 S00-S13 最终矩阵**：使用 R2.0 建立的独立串行 config/入口，在同一受控 Chromium 宿主完成 B0-B4 的适用 route/state/theme/density/role；必须实际报告 `workers=1`、`fullyParallel=false`（或由独立 runner 证明等价串行）、隔离端口和 fixture hash，记录 console、page error、failed requests、focus、scroll、overflow、owner 与 cleanup。不得直接用默认本地并行配置采集可比矩阵，也不得以 E2-I session/PNG/manifest 代替；可以复用同一只读 route-state fixture contract，但必须由仓库 Playwright 在 clean SHA 上重新导航、交互和采集。当前 42/42 是 dirty-candidate Chromium comparison，formal acceptance 仍为 PARTIAL。
- [ ] **R2.7.5 最终盲评**：至少三名相互独立的评审者对固定 42 组核心可比样本匿名评审；至少 36 组偏好 R2，四项总中位数均不低于 4，分歧完整保留。
- [ ] **R2.7.6 WebView2 100%**：使用 `scripts/studio-ui-next/Invoke-StudioUiWebView2Matrix.ps1` 与现有审计脚本采集 Debug/Release 的 1920x1080、1536x864、1366x768，覆盖 light/dark、compact/comfortable、Canvas backing/pointer、owner、runtime error 与 cleanup；正式 manifest 必须由 runner 的 `git rev-parse HEAD` 绑定 40 位 SHA，不接受 scenario 环境变量自报或 `unknown`。
- [x] **R2.7.7 证据审计**：核对 manifest、PNG、SHA-256、native DPI、窗口/client size、进程、端口、user-data 与 publish/runtime 清理；不删除正式证据。当前只完成 Chromium 适用项；native DPI/WebView2 保持未执行。
- [x] **R2.7.8 报告回写**：只将真实运行结果和未执行项写入 R2 报告；是否更新 F10/根 TODO 由主协调 Owner 在独立文档批次决定。
- [ ] **R2.7.9 正式 Motion matrix**：在同一 clean SHA、串行 Chromium 与 WebView2 100% 候选上，对入口、menu/modal/toast/drawer、Workspace flyout/status、生产命令反馈、Settings/AI 各取代表 motionId；以固定 trigger/clock/frame 或短录屏验证 normal/reduced-motion、focus/ARIA、可取消性、终态、layout shift、Long Task 与 timer/RAF/listener cleanup。静态 S00-S13 截图固定在稳定终态，不能用不确定中间帧进入盲评。

**R2.7 Gate**：软件、Playwright、S00-S13 可比矩阵、正式 Motion matrix、最终盲评和 WebView2 100% 均绑定同一产品候选，P0/P1/P2=0，才可授予 `R2_VISUAL_ENGINEERING_DONE`。任一项 PARTIAL 就保持 PARTIAL；历史证据不可补位。

## 18. R2.8：外部环境与人工接受

这些 Gate 可以按环境独立完成，但互不替代，也不因本机工作耗尽而自动勾选。

- [ ] **R2.8.1 Windows 125%**：在真实 120 DPI Windows 会话运行 WebView2 matrix、`scripts/studio-ui-next/Test-StudioUiDpiEvidence.ps1` 和 R2.0 的严格 DPI validator；只有 `DPI_TYPE=NATIVE_WINDOW_DPI_OBSERVED` 且所有目标窗口 `nativeWindow.dpi=120` 才 PASS，force scale/DPR 或原脚本的宽泛 PASS 不替代。记录 window/client size、Canvas backing/pointer、浮层、短屏、长中文、normal/reduced-motion 固定帧与终态，证明位移和浮层 containment 不因 DPI 漂移。
- [ ] **R2.8.2 独立 no-Node 目标机**：被测 Release 运行环境和产品进程树必须无 Node 工具链/Node 子进程；允许隔离的外部证据采集机使用 Node/CDP driver，但 driver 不得位于被测目标机的产品运行环境或成为产品子进程。使用 `Test-StudioUiNoNodeEvidence.ps1` 和 R2.0 的严格 no-Node validator 审计；只有 `cleanMachineWithoutNode` 的独立证据为 PASS 才关闭本项，矩阵 summary 的本机 `PASS`、外部 driver 或 sanitized PATH 均不等价。
- [ ] **R2.8.3 Remote CI**：在当前 clean candidate 触发并取得真实 run；普通 push、本机 PASS 或历史 run 不替代。
- [ ] **R2.8.4 现场能力**：按既有合同验证 Camera、PLC、Station 与 AI 的适用链路；视觉 fixture 不冒充设备/模型质量。
- [ ] **R2.8.5 生产 soak**：按 F10/正式运行要求验证长时间资源、订阅、timer、RAF、transition/animation listener、SSE、Canvas 与 WebView2 稳定性；反复打开/关闭浮层、切 route/flag、进入/退出运行态后 motion owner 与资源必须归零。
- [ ] **R2.8.6 Product Owner 签收**：以黄金旅程、final 盲评结果、WebView2 100%/125% 对照和开放问题为输入，记录接受、返工或拒绝，不把评审者多数票自动等同 Owner 授权。
- [ ] **R2.8.7 Production/Legacy 决策**：只由现有生产治理授予 `PRODUCTION_ACCEPTANCE` 或 `LEGACY_RETIREMENT`；R2 计划无权自行改变。

**R2.8 Gate**：仅当各外部 Gate 实际完成并由相应责任人签署时更新状态。`R2_VISUAL_ACCEPTANCE_GRANTED` 至少需要真实 Windows 125% 与 Product Owner 通过；production acceptance 与 Legacy retirement 继续服从 F10。

## 19. 停止、回滚与失败处理

- 若视觉改动导致功能入口、步骤、状态、错误、权限或写入语义回归，立即回滚该小批次的 R2 改动，不通过“后续再补”进入下一阶段。
- 若发现后端合同或 owner 缺口，标记 `BLOCKED_BY_CONTRACT` 并报告证据；不得在 capability 内新造 authority。
- 若同一构图连续三轮盲评失败，停止局部微调，回到页面任务合同和信息架构重新 shape；不继续叠加阴影、颜色或间距。
- 若内置浏览器绑定、fixture readiness 或 route/state 注入失败，最多重连/重启一次；保留日志、已完成截图和 manifest，安全停止本 batch 创建的 server/context，标记 `IN_APP_BROWSER_UNAVAILABLE` 或 `FIXTURE_UNAVAILABLE`。不得无限重试、用 sleep 掩盖 readiness、改真实数据、改权限或另造 server。
- 若浏览器、WebView2、125% 或外部环境不可用，保留可完成的实现与已取得的 E1/E2 证据，明确写 `BLOCKED_BY_ENVIRONMENT`；不使用低层证据冒充。内置浏览器失败不妨碍后续正式 Playwright 可独立运行，反之亦然，但缺失证据不能相互补位。
- 回滚粒度为当前小批次和白名单文件；不得 reset、clean、切分支或覆盖用户已有改动。需要 Git 回滚、删除或清理时另取明确授权。
- 每阶段保留 before、最终候选、finding disposition 和验证命令；不保留无价值的中间截图洪水或临时 publish/runtime 目录。

## 20. 完成定义

R2 只有同时满足以下条件才算视觉工程完成：

- R2.0-R2.7 全部 `DONE`，当前候选的 E1/E2/E3 本机适用证据齐全；
- 固定 42 组核心场景中至少 36 组偏好 R2，四项总中位数均不低于 4；
- P0/P1/P2=0，P3 无跨页面系统性重复；
- 1920x1080、短屏、light/dark、compact/comfortable、键盘、长中文、异常状态均完成复核；
- R2.1-R2.6 每个视觉 batch 均有 Codex 内置浏览器 before/after、亲自视觉复审、DOM/interaction report、最多三轮 finding closure 与 cleanup；这些记录保持 `DIRECTIONAL_BROWSER`，未冒充正式 Chromium/WebView2；
- 全部已批准 motionId 的 normal/reduced-motion、固定帧/终态、焦点/ARIA、layout shift、Long Task 与 cleanup 证据完成，无装饰性、高频事件或 Canvas/实时图像禁区动画；
- Canvas、保存、运行、Results、Station、Settings、AI、HostBridge 与单一 Owner 边界无漂移；
- 未执行的 Windows 125%、no-Node、Remote CI、现场、soak 或 Owner 签收仍诚实保留，不被其他证据覆盖。

达到上述条件只授予 `R2_VISUAL_ENGINEERING_DONE`。真实 Windows 125% 与 Product Owner 通过后才可授予 `R2_VISUAL_ACCEPTANCE_GRANTED`；`PRODUCTION_ACCEPTANCE` 与 `LEGACY_RETIREMENT` 不属于本计划自行授予的结论。

## 21. 下一唯一动作

从 `R2.0` 开始：以当前 HEAD 重新冻结 route/state/owner，先建立 in-app-browser schema/session/shared fixture，并让 Codex 用内置浏览器实际打开 Auth 与可用认证场景，完成 B0/B2 before、DOM/interaction 检查和 cleanup；随后建立匿名盲评表并完成 Auth、Projects、Workspace、Results 四组试评。试评 Gate 通过后自动进入 `R2.1`；未取得 commit、push、删除或外部门禁授权前，不执行这些动作。
