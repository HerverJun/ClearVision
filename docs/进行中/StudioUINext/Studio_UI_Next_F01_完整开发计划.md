# Studio UI Next F01 完整开发计划

> 阶段代号：F01
> 阶段名称：新前端技术地基与现有 Canvas 宿主验证
> 目标分支：studio-ui-next
> 工作目录：C:\Users\HerverJun\Desktop\ClearVision-UI-Next
> 创建基线：e1bad492fecb6dff2c0a8f848db9ebfa18acf093
> 本计划审查基线：8e553c3a3418f6288ceec5a6e36e9339cbe3d68b
> 预计开发周期：6～9 个有效工作日；外部设备、GitHub Actions 排队/服务故障和用户确认等待不计入有效工作日，但真实 CI 与视觉确认仍是 F01 DONE 门禁
> 执行方式：1 个主协调 owner，按波次启用 2～3 个无文件重叠的受控工作包
> 计划属性：技术地基验证，不迁移正式业务页面，不执行发布切换

---

## 1. 执行摘要

F01 的目标不是把全部 Studio 页面迁移到 Vue，也不是在第一阶段重写流程画布。F01 只验证新前端路线是否具备继续投入的技术基础，并把当前分支最危险的基础问题收敛到可审计、可回滚的边界内。

本阶段必须回答以下问题：

1. 新 Vue 工程能否独立构建，并随 Desktop build 和 publish 进入正确的静态资产目录；
2. Desktop 能否通过默认关闭的启动选择加载新入口，同时确保 legacy 与新 UI 不会同时挂载；
3. 现有 host startup 注入、HTTP、HostBridge 和 Canvas 能否通过窄 adapter 接入，而不形成第二套运行时基础设施；
4. 现有 FlowCanvas 能否在 Vue 生命周期中稳定 mount、resize、交互、serialize 和 dispose；
5. 现有浏览器测试和 WebView2/CDP runner 候选实现能否先被准确定位、验证，再扩展到新入口；
6. 新入口验证成功后，旧 FrontendV2 构建、启动和 CI 链路能否安全退役；
7. 哪些证据已经实际运行；最终提交是否已通过真实 GitHub Actions；哪些仍属于真实 DPI 矩阵、干净 no-Node 目标机或现场环境的后续发布证据。

F01 默认不开发新的 Pointer Canvas Kernel。如果现有 FlowCanvas 被证据证明存在不可修复或改造成本接近重写的基础缺陷，F01 只能输出阻断报告和独立 ADR 建议。任何 Pointer Kernel 实验必须在用户明确批准后另立专项；即使批准，也不得与现有 FlowCanvas 同时成为生产候选。

---

## 2. 当前分支事实基线

### 2.1 分支与仓库

- 当前工作分支为 studio-ui-next；
- codex初稿 是稳定维护与回退基线；
- 当前分支在创建基线之后只增加了 Studio UI Next 的规则和初始化文档；
- F01 正式启动时必须重新记录实际 Initial SHA、origin/studio-ui-next 和 origin/codex初稿；
- 计划中的固定 SHA 只表示编写计划时的审查基线，不替代执行当天的 Git 事实。

### 2.2 Desktop 宿主

当前 Desktop 已具备：

- .NET 8 Windows；
- WinForms；
- WebView2；
- 同进程 ASP.NET Core 本地服务；
- localhost 同源页面与 API；
- self-contained、single-file、win-x64 Release publish；
- StudioStartupPageResolver；
- WebView2Host startup script 注入；
- 资产缺失时的 fail-closed 诊断页；
- legacy 与 FrontendV2 的启动选择测试。

因此 F01 不从零创建第二 resolver、第二 startup 注入器或第二 WebView2 host。新工作必须扩展、泛化和收口现有机制。

### 2.3 当前前端

正式前端仍位于：

~~~text
ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/
~~~

正式入口仍为：

~~~text
/index.html
~~~

当前 legacy composition 副作用分布在 index.html、wwwroot/src/app.js 和多个 core、feature 模块中。新 Studio UI 不导入 legacy app.js，不依赖旧页面 DOM，也不通过 CSS 隐藏的方式与 legacy 同时运行。

### 2.4 旧 FrontendV2

FrontendV2 仍参与：

- npm ci；
- Vite build；
- Desktop build；
- Desktop publish；
- wwwroot/v2 资产复制；
- CI dependency cache 与质量门禁；
- Studio:WorkspaceV2Enabled 启动选择；
- 多组 Desktop 和 Playwright 测试。

FrontendV2 只作历史取证，不作为新工程底座。F01 允许在新入口通过完整门禁后退役它，但退役必须覆盖源码、MSBuild、Host、CI、测试、文档入口和忽略规则，不能只删除目录。

### 2.5 Startup 注入

WebView2Host 当前已经注入并冻结：

- window.__CLEARVISION_STARTUP__；
- apiBaseUrl；
- hostKind；
- featureFlags；
- 旧 FrontendV2 相关字段；
- window.__API_BASE_URL__ legacy alias。

F01 应采用增量、版本化的 startup contract，不应再建立第二套全局配置对象。

### 2.6 FlowCanvas

正式 wwwroot 已存在：

- flowCanvas.js；
- flowCanvasAdapter.js；
- createHostedFlowCanvasAdapter；
- FlowCanvas.destroy；
- ResizeObserver；
- RAF 调度；
- DPR；
- serialize 和 deserialize；
- 连接索引；
- 主题 token；
- FlowEditorInteraction。

FlowEditorInteraction 仍包含 mouse handler 替换、DOM 事件重绑和交互增强。F01 的 Canvas 任务是审计和收口这些生命周期，而不是先写一个更小但功能不对等的新内核。

### 2.7 测试与 WebView2

当前唯一 UI/Playwright 工程位于：

~~~text
ClearVision.Product/tests/ClearVision.Product.UI.Tests/
~~~

当前 Playwright 默认只服务静态 wwwroot 并运行 Chromium，不能替代真实 WebView2。

当前静态搜索可定位到 WebView2/CDP runner 候选锚点，包括 `scripts/run-ai-webview2-release-smoke.ps1`、`scripts/run-ai-plan-build-readiness-p1-webview2.ps1` 及 `ClearVision.Product.UI.Tests/tests/e2e/` 下对应的 CDP 脚本；但这些候选尚未在本计划修订中实际运行，也不能据此断言已经存在一套可复用的通用 runner。F01-0 必须先定位入口、核心实现、调用链和真实能力：确认存在则复用并泛化，不存在或仅为历史残留则建立唯一的最小 runner，不创建第二套 WebView2 自动化基础设施。

### 2.8 DPI 与 no-Node 事实

当前 DPI 配置存在冲突：

- Desktop 项目声明 PerMonitorV2；
- Program.Main 显式调用 HighDpiMode.SystemAware；
- manifest 未形成统一的 DPI 权威。

因此 F01-0 必须确认当前实际生效的 DPI mode，并在 ADR 中决定项目属性、`Application.SetHighDpiMode` 或 manifest 中哪一处是未来唯一权威；三者不得长期冲突。DPI authority 本身是 F01 门禁，浏览器 DPR、WebView2 force-device-scale-factor、真实 Windows DPI 和跨显示器移动仍必须分开报告。

同时，下列证据也必须分开：

- publish 包不含 node_modules、源码或 npm cache；
- Desktop 运行期不启动 Node 子进程；
- 清理 PATH 后 Desktop 可以启动；
- 干净、未安装 Node 的 Windows 目标机能够启动。

前三项可在 F01 本机验证，最后一项属于后续发布环境证据。

---

## 3. F01 状态模型

F01 使用以下状态：

~~~text
NOT_STARTED
IN_PROGRESS
CODE_COMPLETE
AWAITING_VISUAL_CONFIRMATION
AWAITING_CI
BLOCKED_<REASON>
DONE
~~~

定义：

- CODE_COMPLETE：本地代码、构建、测试、publish 和真实 WebView2 门禁已完成，但可能仍等待用户视觉方向确认；
- AWAITING_VISUAL_CONFIRMATION：本地技术门禁完成，等待用户确认 F02 Design System 的视觉方向；
- AWAITING_CI：本地门禁和用户视觉确认均已完成，但仍等待至少一次针对最终提交的 GitHub Actions 验证；
- DONE：本地门禁、用户视觉确认、唯一 Canvas 结论、DPI authority 决策、FrontendV2 完整退役，以及至少一次针对最终提交的 GitHub Actions 成功验证全部完成；
- RELEASE_READY 不属于 F01 状态。真实 Windows DPI/跨显示器矩阵、干净 no-Node 目标机、现场硬件和发布切换在后续发布阶段单独判定。

F01 可以记录外部证据为 NOT RUN 或 NOT PERFORMED，但不得把它们误写为 PASS。

---

## 4. 强制架构原则

### 4.1 单一活动根

任一 Desktop 进程只允许加载一个页面根：

- legacy：/index.html；
- Studio UI：/studio/index.html；
- Diagnostic：宿主诊断页面。

启动选择是进程启动时的选择，不支持运行时热切换。修改启动 flag 后必须重启 Desktop。

不得：

- 同时 mount legacy 与 Studio UI；
- 用 CSS 隐藏未选中的根；
- 在新根中导入 legacy app.js；
- 用 query string 或 localStorage 覆盖宿主启动选择；
- 资产缺失时静默回退 legacy。

### 4.2 业务权威不进入前端

Vue、Pinia、DOM、localStorage 和缓存只能保存：

- UI 投影；
- 表单草稿；
- 选择状态；
- 过滤与展开状态；
- 可丢弃缓存；
- 测试诊断。

它们不得成为：

- Project authority；
- Flow authority；
- GlobalVariables authority；
- PersistenceRevision authority；
- AgentRun authority；
- Inspection result authority；
- Runtime Package authority；
- Station authority。

### 4.3 基础设施唯一性

不得创建第二：

- EventBus；
- ServiceRegistry；
- Project save client；
- HTTP 端口发现机制；
- HostBridge runtime；
- Canvas 内核；
- WebView2 执行通道。

新 Studio UI 需要新的 bundle-local adapter 时，必须先由 ADR 说明：

1. 它是否只在 Studio UI 根活动；
2. legacy 根活动时它是否完全不存在；
3. 是否复用同一后端协议和宿主能力；
4. 如何通过 architecture guard 保证唯一直接访问点；
5. FrontendV2 退役后谁是唯一 owner。

### 4.4 命令式对象生命周期

以下对象不得由 Vue 组件或 Pinia 长期持有：

- FlowCanvas；
- FlowEditorInteraction；
- WebView2 bridge；
- EventSource；
- AbortController；
- ResizeObserver；
- requestAnimationFrame；
- blob URL；
- native image buffer。

它们必须由明确的 owner 创建，并在 dispose 时释放全部资源。

### 4.5 Canvas 红线

F01 只验证现有 FlowCanvas。

若验证失败：

1. 标记 BLOCKED_CANVAS_FOUNDATION；
2. 保留 Host、Build、Design Lab 和测试基础设施成果；
3. 输出失败矩阵和成本分析；
4. 输出独立 Pointer Canvas ADR 建议；
5. 等待用户明确批准后才可另立专项；
6. 即使另立专项，也不得让新旧 Canvas 同时成为生产候选。

### 4.6 证据真实性

静态浏览器、Playwright、真实 WebView2、模拟 DPR、真实 Windows DPI、publish、no-Node 目标机和 CI 是不同证据。
普通分支 push 不等于 CI PASS。GitHub Actions 证据必须记录 workflow run URL、run ID、commit SHA 和 conclusion。

任何未实际运行的项目必须标记：

~~~text
NOT RUN
NOT PERFORMED
~~~

---

## 5. 阶段范围

### 5.1 必须交付

F01 必须交付：

1. F01 架构 ADR，包含 WebView2 runner 事实取证、DPI authority 和 CI 取证路径；
2. 独立 StudioUI Vue 工程；
3. TypeScript strict、Vite、Vue Router、Vitest、ESLint 和 vue-tsc；
4. 明确的 Vite base 与路由策略；
5. Desktop build/publish 静态资产集成；
6. 默认关闭的 Studio:StudioUiEnabled；
7. /studio/index.html 启动入口；
8. startup contract schemaVersion 和 typed reader；
9. 最小 Host adapter 与 API transport；
10. 代表性的 Design Foundation Lab；
11. 现有 FlowCanvas Vue 宿主实验；
12. 基于现有 DTO/元数据的真实 Flow fixture；
13. 中央 Playwright 测试；
14. 经 F01-0 验证后复用并泛化的真实 WebView2 smoke runner；若确认不存在，则交付唯一的最小 runner；
15. Debug、Release publish 和运行期静态资产证据；
16. 本机 no-Node 静态与运行期证据；
17. FrontendV2 构建、启动和 CI 链退役；
18. 针对最终提交的真实 GitHub Actions 运行证据；
19. F01 完成报告和 F02 输入。

### 5.2 F01 不交付

F01 不负责：

- 完整 App Shell；
- 正式登录页迁移；
- Project 页面迁移；
- Project 保存；
- Flow 正式保存；
- GlobalVariables；
- Property；
- Preview；
- ImageCanvas；
- ROI；
- Final Decision；
- Inspection；
- Results 正式迁移；
- Station 操作页面；
- Settings；
- AI；
- AgentRun/SSE 业务接入；
- 完整 Design System primitives；
- 完整算子库；
- Pointer Canvas Kernel；
- Runtime、Station、Project 格式或保存协议变更；
- 正式发布入口切换；
- 现场相机、PLC 或 Station 验收。

### 5.3 外部发布证据

以下不作为 F01 本地开发工期承诺，但必须列入后续 release evidence：

- 干净 no-Node Windows 目标机启动；
- 真实多显示器和完整 100%、125%、150%、200% 系统缩放 DPI 矩阵；
- 真实相机、PLC、Station；
- 最终发布切换和回滚演练。

DPI authority 决策和针对最终提交的真实 GitHub Actions 不属于上述后续证据，必须在 F01 内完成。

---

## 6. F01-0：启动审计与 ADR

### 6.1 目标

在修改共享代码前冻结范围、owner、目录和唯一性策略。

### 6.2 必须完成

1. 核验当前分支、SHA 和工作区；
2. git fetch origin --prune；
3. 只审计 origin/codex初稿 的新提交，不在 F01 末尾自动同步；
4. 若稳定线存在安全、后端 authority 或契约修复，由主协调 owner 单独决定是否合入；
5. 建立 F01 文件清单和共享文件 owner；
6. 建立 FrontendV2 全引用清单；
7. 定位并验证 WebView2/CDP runner 的真实代码锚点、入口、调用链和当前能力；
8. 确认 Desktop 当前实际生效的 DPI mode，并形成唯一 DPI authority 决策；
9. 建立测试端口、数据库、WebView2 user-data、publish 和结果目录隔离方案；
10. 明确最终提交获得真实 GitHub Actions 的触发方式；
11. 编写并批准 ADR。

### 6.3 ADR 必须回答

ADR 至少覆盖：

- StudioUI 源码目录；
- 中间产物目录；
- OutDir 与 PublishDir；
- Vite base；
- Router 模式；
- /studio 静态文件映射；
- legacy、FrontendV2、StudioUi 的过渡关系；
- 两个 flag 同时开启时的行为；
- startup schema 版本策略；
- HostBridge 唯一性；
- API transport 唯一性；
- FlowCanvas canonical import 或重构边界；
- 中央 Playwright 接入；
- WebView2/CDP runner 事实清单及复用/唯一最小实现决策；
- Desktop 当前有效 DPI mode、未来唯一 DPI authority 和防回归证据；
- GitHub Actions 最终提交验证路径；
- FrontendV2 删除策略；
- 回滚边界。

### 6.4 WebView2/CDP runner 事实取证

ADR 必须逐项记录：

~~~text
现有 runner 是否真实存在：
入口脚本：
核心实现文件：
调用链：
依赖：
支持场景：
是否启动真实 Desktop：
是否连接真实 WebView2：
是否使用 CDP：
是否支持自定义端口：
是否支持独立数据库：
是否支持独立 WebView2 user-data：
是否能自动关闭和清理：
最近一次实际运行结果：
~~~

决策规则：

- 若真实实现确认存在，必须复用并泛化，只新增 Studio UI scenario；legacy 既有 scenario 不得被破坏；
- 若找不到真实实现，或候选只是历史文档、废弃脚本、无法运行的残留，ADR 必须如实写明，并在后续实现中建立一套最小、唯一的 WebView2 smoke runner；
- 事实未闭环前使用 `BLOCKED_WEBVIEW2_RUNNER_FACTS`，不得直接进入 WebView2 scenario 实现；
- 禁止同时保留两套 native window、CDP connect、认证 setup、进程关闭或清理基础设施。

### 6.5 F01-0A：Desktop DPI Authority

F01 必须：

1. 通过代码、运行时查询或最小诊断确认 Desktop 当前实际生效的 DPI mode；
2. 在 ADR 中决定未来唯一 DPI 权威来自项目属性、`Application.SetHighDpiMode` 或 manifest；
3. 不允许三者长期冲突；
4. 分别定义 Browser DPR、WebView2 force-device-scale-factor、JS `devicePixelRatio`、Canvas hit testing、native window size、真实 Windows system scale 和 per-monitor move 的证据含义；
5. 若当前 `SystemAware` 会使 Canvas 坐标、DPR 或 WebView2 结论失真，先统一 DPI mode，再完成 Canvas 门禁；
6. 增加 focused test 或可审计诊断，防止未来重新出现权威冲突；
7. 真实多显示器和完整系统缩放矩阵可以在 F01 报告中标记 `NOT PERFORMED`，但 DPI authority 决策不得后推到 F02 或发布阶段；
8. authority 未决或实际 mode 无法确认时使用 `BLOCKED_DPI_AUTHORITY`。

### 6.6 默认技术决策

除非 ADR 给出更充分理由，F01 默认采用：

- Vite base：/studio/；
- Vue Router：hash history；
- Lab URL：

~~~text
/studio/index.html#/labs/design
/studio/index.html#/labs/canvas
~~~

- 不增加 ASP.NET Core 全局 SPA fallback；
- 生成资产不写入源码 wwwroot/studio；
- E2E 使用现有 ClearVision.Product.UI.Tests；
- Canvas 只复用现有 FlowCanvas；
- Host shared buffer 延后到 ImageCanvas/Preview 阶段。

### 6.7 通过条件

- ADR 获得用户或指定架构 owner 的明确批准；
- 所有共享文件只有一个 owner；
- WebView2/CDP runner 事实取证已闭环，并已选择复用或唯一最小实现；
- 当前有效 DPI mode 已确认，未来唯一 DPI authority 已决定；
- 针对最终提交的 GitHub Actions 触发路径已确定；
- Pointer Kernel 不在 F01 实现范围；
- 不存在未解释的第二 HTTP、HostBridge 或 Canvas 方案。

---

## 7. 推荐目录与产物布局

### 7.1 源码

~~~text
ClearVision.Product/
├─ src/
│  └─ ClearVision.Product.Desktop/
│     ├─ StudioUI/
│     │  ├─ src/
│     │  │  ├─ app/
│     │  │  ├─ platform/
│     │  │  │  ├─ startup/
│     │  │  │  ├─ host/
│     │  │  │  ├─ api/
│     │  │  │  └─ diagnostics/
│     │  │  ├─ design-system/
│     │  │  │  ├─ tokens/
│     │  │  │  └─ primitives/
│     │  │  ├─ labs/
│     │  │  │  ├─ design/
│     │  │  │  └─ canvas/
│     │  │  ├─ shared/
│     │  │  └─ test-support/
│     │  ├─ tests/
│     │  │  └─ unit/
│     │  ├─ package.json
│     │  ├─ package-lock.json
│     │  ├─ vite.config.ts
│     │  ├─ vitest.config.ts
│     │  ├─ eslint.config.js
│     │  └─ tsconfig*.json
│     └─ wwwroot/
│        └─ legacy source assets only
└─ tests/
   └─ ClearVision.Product.UI.Tests/
      └─ tests/
         └─ e2e/
            └─ studio-ui-next/
~~~

### 7.2 生成产物

~~~text
obj/<Configuration>/<TargetFramework>/StudioUI/dist/
bin/<Configuration>/<TargetFramework>/<RID>/wwwroot/studio/
.tmp/publish-check/f01/<run>/wwwroot/studio/
~~~

不得把 Vite hashed assets 写入：

~~~text
ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/studio/
~~~

若源码目录中需要占位说明，只允许保留非生成的 README 或明确的占位文件，且不得参与正式资产校验。

### 7.3 临时证据

默认写入：

~~~text
.tmp/studio-ui-next/f01/
~~~

包括：

- publish；
- WebView2 user-data；
- 独立数据库；
- CDP 日志；
- 原始截图；
- Playwright 结果；
- heap 和性能数据。

只有经过审查、确需长期保留的精选证据，才可进入仓库既有 quality/evidence 体系。

---

## 8. F01-1：StudioUI 工程与构建链

### 8.1 目标

建立唯一的新 Vue 工程，并证明 Desktop build 和 publish 可以可靠生成 /studio 资产。

### 8.2 工具链

建立：

- Vue 3；
- TypeScript strict；
- Vite；
- Vue Router；
- Vitest；
- ESLint；
- vue-tsc；
- Pinia 仅作为依赖可用性验证，不在 F01 存放业务 authority。

依赖版本必须：

- 在 package.json 明确；
- 由 package-lock.json 固定；
- 声明 Node 和 npm engines；
- 与 CI Node 版本一致；
- 不通过 latest 或未固定脚本下载生产依赖。

### 8.3 Vite

必须配置：

- base 为 /studio/；
- manifest 为 true；
- sourcemap 默认关闭；
- 输出目录由 VITE_OUT_DIR 或等价受控参数注入；
- 不写 Desktop 源码 wwwroot；
- asset 文件名可重复构建；
- browser fixture 与 Desktop build 使用同一 production build 配置。

### 8.4 Router

F01 默认使用 hash history。

必须具备：

~~~text
/labs/design
/labs/canvas
/diagnostics
~~~

正式 URL 为：

~~~text
/studio/index.html#/labs/design
/studio/index.html#/labs/canvas
~~~

若改用 history mode，必须先在 ADR 中给出：

- scoped fallback；
- auth middleware 顺序；
- legacy/API 路由冲突分析；
- missing asset 行为；
- Desktop tests。

### 8.5 MSBuild

建议建立：

~~~text
StudioUiRoot
StudioUiIntermediateDist
StudioUiNodeModulesLock
SkipStudioUiBuild
SkipStudioUiInstall
~~~

MSBuild 必须：

1. npm ci 只在 lockfile 或 node_modules lock 需要更新时运行；
2. npm run build 输出到 obj；
3. build 后复制到 $(OutDir)wwwroot\studio；
4. publish 后复制到 $(PublishDir)wwwroot\studio；
5. clean 只删除自己拥有的 obj、OutDir 和 PublishDir 产物；
6. 不删除源码或用户目录；
7. 资产为空时失败；
8. 缺少 index.html、assets 或 manifest 时失败；
9. 增量 build 不重复无效安装；
10. 源码变化后必须重建；
11. hashed 旧资产不得残留。

### 8.6 构建验收

必须验证：

- clean frontend build；
- repeat frontend build；
- 修改一个源码文件后 rebuild；
- Desktop Debug build；
- repeat Desktop Debug build；
- Desktop Release publish；
- publish 包存在 wwwroot/studio/index.html；
- publish 包不存在 StudioUI 源码、node_modules、package-lock、npm cache 和 source map；
- Desktop 源码 wwwroot 未出现生成资产。

### 8.7 通过条件

- npm ci、lint、typecheck、unit 和 build 成功；
- Desktop build 自动包含 StudioUI；
- Desktop publish 自动包含 StudioUI；
- 增量输入输出关系可解释；
- Vite asset base 正确；
- 不依赖 Vite dev server 才能启动。

---

## 9. F01-2：Desktop Host 与 Startup Contract

### 9.1 目标

在现有 resolver、static assets 和 startup injection 上增加 Studio UI 入口，不创建第二 Host。

### 9.2 启动 flag

新增：

~~~text
Studio:StudioUiEnabled
~~~

默认值：

~~~text
false
~~~

过渡期间仍可能存在：

~~~text
Studio:WorkspaceV2Enabled
~~~

必须定义：

| StudioUiEnabled | WorkspaceV2Enabled | 结果 |
|---:|---:|---|
| false | false | Legacy |
| true | false | StudioUi |
| false | true | FrontendV2，仅在退役前 |
| true | true | Diagnostic，禁止自行选择优先级 |

FrontendV2 退役后删除 WorkspaceV2Enabled 和 FrontendV2 startup kind。

### 9.3 Resolver

过渡阶段允许：

~~~text
Legacy
FrontendV2
StudioUi
Diagnostic
Welcome
~~~

退役后只保留：

~~~text
Legacy
StudioUi
Diagnostic
Welcome
~~~

StudioUi 资产至少检查：

- index.html；
- assets 目录；
- 至少一个 asset；
- manifest。

资产缺失时：

- 显示 Diagnostic；
- 明确缺失路径；
- 不导航 legacy；
- 不加载 FrontendV2；
- 不在前端用重试循环猜测资产。

### 9.4 静态资产

/studio 必须从运行输出或 publish 根映射：

~~~text
<AppContext.BaseDirectory>/wwwroot/studio
~~~

Debug legacy 仍可按现有规则优先读取项目源码 wwwroot，但 StudioUi 不能依赖源码生成目录。

### 9.5 Startup schema

新增版本化结构：

~~~ts
interface StudioStartupConfigV1 {
  readonly schemaVersion: 1;
  readonly uiKind: 'studio-ui';
  readonly hostKind: 'desktop-webview2' | 'browser-test';
  readonly apiBaseUrl: string;
  readonly studioUiBasePath: '/studio/';
  readonly featureFlags: Readonly<Record<string, boolean>>;
}
~~~

Host 过渡期内部仍可识别 Legacy、FrontendV2、StudioUi 和 Diagnostic，但只有真正启动 StudioUI 时才向该页面注入 `StudioStartupConfigV1`。FrontendV2 兼容字段只存在于旧 Host 过渡逻辑，不进入新的 TypeScript v1 契约，也不要求 FrontendV2 退役后立即发布 `schemaVersion: 2`。

过渡期间 host 注入可保留旧 alias，但必须：

- 新字段为增量扩展；
- featureFlags 和 startup object 均冻结；
- window property 不可写、不可配置；
- legacy 的 window.__API_BASE_URL__ 在 legacy 仍活动时保持；
- StudioUI 不依赖 `workspaceV2Enabled`、`frontendV2BasePath` 或 `window.__API_BASE_URL__`；
- StudioUI 不从 localStorage、query string 或端口扫描补全 desktop config；
- browser-test 必须显式注入 fixture，且 `uiKind` 固定为 `studio-ui`。

### 9.6 Host focused tests

必须覆盖：

- flag off 使用 legacy；
- StudioUi flag on 使用 /studio/index.html；
- V2 flag on 在退役前使用 /v2/index.html；
- 两 flag 同开进入 Diagnostic；
- StudioUi 资产缺失 fail-closed；
- legacy 资产缺失保持 Welcome/Diagnostic 现有语义；
- startup schemaVersion；
- StudioUI 的 uiKind 只能为 `studio-ui`；
- Legacy 与 FrontendV2 不接收新的 `StudioStartupConfigV1`；
- apiBaseUrl；
- studioUiBasePath；
- featureFlags deep freeze；
- localhost 同源；
- invalid port 拒绝；
- static /studio 资产缓存规则。

### 9.7 通过条件

- 默认配置仍启动 legacy；
- Studio UI 启动时 legacy 和 V2 均未挂载；
- StudioStartupConfigV1 只描述 StudioUI，Host 过渡枚举不泄漏到新 TypeScript 契约；
- startup config 是 StudioUI 唯一宿主启动配置；
- 不支持运行时热切换；
- Host 变更不新增业务 endpoint。

---

## 10. F01-3：Platform 最小基础层

### 10.1 原则

Platform 只实现 F01 Lab 所需的最小能力。不得借机建立完整业务 SDK。

### 10.2 Startup reader

目录：

~~~text
StudioUI/src/platform/startup/
~~~

职责：

- 读取 window.__CLEARVISION_STARTUP__；
- 校验 schemaVersion；
- 校验 hostKind、`uiKind === 'studio-ui'`、apiBaseUrl 和 base path；
- 输出 readonly typed model；
- desktop 缺失时 fail-fast；
- browser-test 只接受显式 fixture；
- 不自行探测 5000～5010；
- 不读取 localStorage 作为宿主配置。

### 10.3 Host adapter

目录：

~~~text
StudioUI/src/platform/host/
~~~

F01 仅允许：

- 检测 WebView2 是否存在；
- postMessage；
- host message subscribe/unsubscribe；
- dispose；
- 测试替身；
- 诊断当前 host kind。

F01 不实现：

- shared buffer；
- 图像传输；
- 文件选择业务 UI；
- 执行控制；
- Inspection 控制；
- AgentRun 消息；
- 绕过 HTTP 的业务请求。

直接访问 window.chrome.webview 只能存在于一个受审查文件中。

若 ADR 未解决 HostBridge 唯一性，不得实现真实 adapter，只保留 interface 和 browser fake。

### 10.4 API transport

目录：

~~~text
StudioUI/src/platform/api/
~~~

只实现：

- injected apiBaseUrl；
- 单一 request core；
- GET；
- AbortSignal；
- JSON decode；
- empty body；
- typed network error；
- 400、401、403、404、409、5xx 映射；
- token provider interface；
- diagnostics。

F01 允许的 smoke：

- public /health；
- public /api/auth/setup-status；
- 有 token 时 /api/auth/me；
- 仅在 authenticated test harness 中读取 /api/operators/library；
- 仅在 authenticated test harness 中读取 /api/projects。

不得：

- 新增 endpoint；
- 新建 Project save client；
- 自动重试写请求；
- 发现或缓存端口；
- 在 transport 内决定登录跳转；
- 在 transport 内持有正式用户 authority；
- 让组件直接 fetch。

### 10.5 Auth 边界

F01 不迁移登录页。

生产 Lab 行为：

- 无 token 时显示 unauthenticated diagnostics；
- 不自动创建用户；
- 不调用 Project 写接口；
- 不把未登录当作 transport 故障。

WebView2 和 Playwright authenticated smoke：

1. 使用隔离数据库；
2. 调用现有 setup/login；
3. 获得真实 token；
4. 在同源 sessionStorage 注入测试会话；
5. 导航 Studio UI；
6. 验证 read-only API；
7. 测试结束清理数据库和 user-data。

### 10.6 Architecture guards

必须建立守卫：

- StudioUI 只有 platform/api 可直接 fetch；
- 只有 platform/host 可访问 window.chrome.webview；
- 不出现 legacy app.js import；
- 不出现 FrontendV2 import；
- StudioStartupConfigV1 和 browser fixture 的 uiKind 只能为 `studio-ui`；
- StudioUI 不读取 `workspaceV2Enabled`、`frontendV2BasePath` 或 `window.__API_BASE_URL__`；
- 不创建 EventBus 或 ServiceRegistry；
- 不出现 ProjectSaveCoordinator；
- 不出现 new EventSource；
- 不出现第二 Canvas 类；
- Pinia store 不保存命令式对象；
- Lab 不调用 PUT、POST、PATCH 或 DELETE 业务接口。

---

## 11. F01-4：Design Foundation Lab

### 11.1 定位

F01 只验证视觉方向和基础 token 是否适合继续发展，不完成整个 Design System。

正式 Design System 定型、完整 primitives 和视觉回归扩展属于 F02。

### 11.2 设计基线

新 Lab 不加载 legacy CSS，但应审计正式 wwwroot 的 Quiet Precision 语义基线：

- 中性表面；
- 单一品牌强调色；
- OK、NG、Error、Info 与品牌色分离；
- 浅色和深色；
- 工业信息密度；
- 克制阴影；
- 明确 focus；
- Canvas 独立视觉语义。

不得复制旧 FrontendV2 的绿色 Workspace 样式。

### 11.3 F01 token

建立最小 token：

- color；
- typography；
- spacing；
- radius；
- elevation；
- motion；
- z-index；
- status；
- canvas；
- density。

### 11.4 F01 primitives

只要求代表性组件：

- Typography；
- Surface；
- Button；
- IconButton；
- Field；
- Select；
- Panel；
- StatusBadge；
- Modal；
- Toast；
- Splitter。

状态：

- default；
- hover；
- active；
- focus-visible；
- disabled；
- loading；
- error。

### 11.5 可访问性与视觉

必须覆盖：

- keyboard navigation；
- visible focus；
- modal focus trap；
- Escape；
- reduced motion；
- light；
- dark；
- 1366×768；
- 1920×1080；
- 短屏不丢失操作；
- 品牌色不与 NG 混淆；
- 不使用 legacy CSS 修补。

### 11.6 用户确认

用户确认只判断：

- 方向是否继续；
- 信息密度是否合适；
- 品牌色、状态色和表面层级是否符合预期；
- F02 是否可以扩展完整 Design System。

若技术门禁已通过但用户尚未确认，F01 状态为 AWAITING_VISUAL_CONFIRMATION；用户确认完成而最终提交尚未通过 GitHub Actions 时，状态转为 AWAITING_CI。

---

## 12. F01-5：现有 FlowCanvas Vue 宿主验证

### 12.1 目标

验证现有 FlowCanvas 能否成为 Studio UI Next 的唯一流程画布内核。

### 12.2 禁止事项

不得：

- 创建 Pointer Canvas Kernel；
- 复制 flowCanvas.js；
- 复制 FrontendV2 adapter；
- 复制 StudioFlowEditorPort；
- 导入 legacy app.js；
- 建立第二 flow model；
- 在 Pinia 保存 Canvas 实例；
- 让 Vue 组件直接修改 Canvas 内部 Map；
- 为通过测试而使用空端口或伪造 Number 类型。

### 12.3 Canonical adapter 策略

优先复用或重构正式：

~~~text
wwwroot/src/core/canvas/flowCanvas.js
wwwroot/src/core/canvas/flowCanvasAdapter.js
~~~

若现有路径不适合 Vite 静态 bundling，应由主协调 owner 在 ADR 指定的 neutral shared path 中移动或提取 canonical 模块。

允许重构，禁止复制。

Lab owner 必须拥有：

- 一个 FlowCanvas；
- 一个 canonical adapter；
- 如确有需要，一个 FlowEditorInteraction；
- 所有 listener；
- ResizeObserver；
- RAF；
- theme observer；
- diagnostics；
- dispose 顺序。

### 12.4 Fixture

Fixture 必须直接使用当前 OperatorFlowDto 语义：

- flow id；
- flow name；
- operators；
- operator id；
- type；
- name；
- x、y；
- inputPorts；
- outputPorts；
- port id；
- name；
- direction；
- dataType；
- isRequired；
- parameters；
- connections；
- sourceOperatorId；
- sourcePortId；
- targetOperatorId；
- targetPortId；
- decisionConfiguration。

Fixture 应从当前 operator metadata 或现有 canonical DTO 生成后固化，禁止凭空发明生产类型。

最低覆盖：

- ImageAcquisition Image 到 Thresholding Image；
- Thresholding Image 到 BlobAnalysis Image；
- 一个实际存在的 Integer 或 Float 兼容连接；
- Image 到 Region 不兼容；
- 已占用输入端；
- 重复连接；
- 同节点连接。

兼容字段处理集中在一个 decoder：

~~~text
id / Id
type / Type
inputPorts / InputPorts
outputPorts / OutputPorts
connections / Connections
~~~

### 12.5 交互矩阵

连接：

- output 到 input；
- input 到 output，如果正式 FlowEditorInteraction 定义该手势；
- 空白释放；
- Esc 取消；
- 离开 Canvas；
- 不兼容拒绝；
- 同节点拒绝；
- 重复拒绝；
- 已占用输入拒绝；
- 删除连接；
- 点击连接与拖拽连接区分。

节点：

- 单节点拖动；
- 缩放后拖动；
- pan 后拖动；
- resize 后拖动；
- selection；
- 点击空白取消；
- 边界命中；
- disabled 节点显示。

视图：

- zoom 50%、75%、100%、150%、200%；
- pan；
- Dock resize；
- window resize；
- hide/show；
- browser DPR 1、1.25、1.5、2；
- 1366×768；
- 1920×1080。

### 12.6 生命周期矩阵

- mount/unmount 20 次；
- mounted owner 计数始终为 1；
- listener 数量不单调增长；
- ResizeObserver 释放；
- RAF 释放；
- theme observer 释放；
- FlowEditorInteraction handler 恢复；
- route 切换后无双触发；
- dispose 可重复调用；
- serialize 后重新加载 identity 保持；
- destroy 后 hosted adapter registry 不残留。

### 12.7 Identity 定义

serialize 到 reload 必须保持：

- operator ID；
- port ID；
- connection ID；
- operator type；
- position；
- parameter value；
- enabled 状态；
- decisionConfiguration；
- 连接端点。

允许忽略：

- JSON 属性顺序；
- 纯 UI hover；
- selection；
- 临时 connection preview；
- 非正式 diagnostics。

### 12.8 性能

性能比较必须是 Legacy 正式宿主与 Vue StudioUI 宿主的标准化 A/B，不接受不同机器、不同 fixture 或不同 DPI 条件下的百分比对比。

A/B 必须保持：

- 同一台机器；
- 同一 Windows 会话；
- 同一 Release 构建策略；
- 同一 fixture；
- 同一节点和连接数量；
- 同一分辨率；
- 同一 DPI/DPR 条件；
- 同一测试动作；
- 尽量相同的后台负载。

场景至少覆盖 100 节点/150 连接的 pan、zoom、drag，以及 300 节点压力、destroy 后残留。每个场景：

1. 预热不少于 2 次；
2. 正式执行不少于 5 次；
3. 保存每次运行的原始样本，而不是只报告百分比；
4. 分别记录 median、p95、maximum、long task 数量、未处理异常、heap 或可观察内存，以及 destroy 后残留；
5. 报告测试动作、采样窗口、构建配置、分辨率和 DPI/DPR，使结果可复现。

判定采用 warning-first：

- 100 节点交互的单组标准化 A/B 中，StudioUI p95 相对 Legacy 恶化超过 20% 时标记 `PERFORMANCE_WARNING`，先复核原始样本和噪声，不立即否决方案；
- 只有连续三组完整、标准化测试均超过 20%，且已排除测试噪声，才升级为 `BLOCKED_CANVAS_PERFORMANCE`；
- 崩溃、连接丢失、序列化破坏、重复事件、持续内存增长、操作不可完成或明显长时间卡死直接阻断，不等待性能百分比；
- 300 节点压力不得崩溃、卡死或破坏序列化；
- 20 次 mount/unmount 后 heap 和 listener 不得持续单调增长；
- 不得出现未处理异常。

F01 的 Canvas 主要硬门禁是行为正确、生命周期可释放、身份保持、无持续泄漏和无不可接受卡顿；20% 是需要标准化复核的性能预警线，不是脱离上下文的单次否决线。

### 12.9 决策

若全部关键矩阵通过：

~~~text
CANVAS_DECISION=REUSE_EXISTING_FLOW_CANVAS
~~~

若关键矩阵失败：

~~~text
BLOCKED_CANVAS_FOUNDATION
~~~

若标准化性能复核达到阻断条件：

~~~text
BLOCKED_CANVAS_PERFORMANCE
~~~

失败后不自动选择新内核，只输出：

- 失败项；
- root cause；
- 可修复性；
- 预计改造成本；
- 与新内核的冲突分析需求；
- 独立 ADR 建议。

---

## 13. F01-6：Playwright 与真实 WebView2

### 13.1 中央 Playwright

Studio UI E2E 必须加入：

~~~text
ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/
~~~

不得在 StudioUI 内创建第二 Playwright package 或第二 node_modules。

现有 playwright.config.ts 应支持通过环境变量选择测试 web root，但默认 legacy 行为不得改变。

建议：

~~~text
CV_UI_WEB_ROOT
CV_UI_BASE_URL
CV_UI_SCENARIO
~~~

### 13.2 Browser fixture

浏览器测试显式注入：

- schemaVersion；
- uiKind=studio-ui；
- hostKind=browser-test；
- apiBaseUrl；
- studioUiBasePath；
- frozen featureFlags；
- authenticated token 或 unauthenticated 状态。

Browser fixture 不代表真实 WebView2。

### 13.3 先取证，再复用或建立唯一 runner

本阶段不得仅凭历史文档或候选文件名断言 runner 已存在。必须先完成 6.4 的事实取证；事实未闭环时使用 `BLOCKED_WEBVIEW2_RUNNER_FACTS`。

若确认真实实现存在：

- 复用并泛化现有进程启动、真实 Desktop、WebView2 连接、CDP、auth setup、端口隔离和进程关闭逻辑；
- legacy 既有 scenario 继续可用；
- 只新增 Studio UI scenario；
- 不创建第二套 runner 或复制公共实现。

若确认不存在、无法运行或只剩历史残留：

- ADR 如实记录；
- 建立一套最小、唯一的 WebView2 smoke runner；
- 新入口、公共实现和清理策略必须只有一个 owner；
- 工期、风险和最终报告明确记录该变化。

只有在取证后确认需要独立场景入口时，才可建立 `scripts/run-studio-ui-next-webview2-smoke.ps1`；它必须调用唯一共享实现，而不是复制 native window、CDP connect、认证或关闭逻辑。

### 13.4 隔离

每次 WebView2 smoke 必须隔离：

- HTTP 端口；
- CDP 端口；
- SQLite 数据库；
- WebView2 user-data；
- Agent/Conversation store；
- evidence 目录；
- publish 目录。

若当前 Host 无法安全注入 user-data 或固定端口，由主协调 owner 先报告最小 host test option，不允许前端自行扫描端口。

### 13.5 进程场景

每个启动选择使用独立 Desktop 进程：

1. flag off；
2. StudioUi flag on；
3. 两 flag 同开；
4. StudioUi 资产缺失；
5. Debug output；
6. Release publish。

不得在同一个进程中热改 flag 后宣称完成切换。

资产缺失场景必须在 .tmp 中复制一个专用 publish 样本并移除其 StudioUI 资产，不得删除或重命名工作区正常输出。

### 13.6 Ready 与 diagnostics

Studio UI 可以暴露只读诊断：

~~~text
window.__STUDIO_UI_READY__
window.__STUDIO_UI_DIAGNOSTICS__
~~~

要求：

- READY 只在 Vue mount 和 Router ready 后为 true；
- diagnostics 不保存业务 authority；
- mountCount 为 1；
- activeRoot 为 studio-ui；
- Canvas ownerCount 可读取；
- console unhandled error 可统计；
- property 不可被业务组件用作写入口。

### 13.7 WebView2 验收

必须验证：

- URL 正确；
- startup schema 正确；
- Vue 只 mount 一次；
- legacy root 未挂载；
- FrontendV2 未挂载；
- Design Lab 可见；
- Canvas Lab 可见；
- API public health；
- authenticated read-only smoke；
- Canvas diagnostics；
- console 无未处理错误；
- 自动关闭成功；
- Desktop 与 WebView2 子进程在超时内退出；
- user-data 可清理；
- 没有白屏或 silent fallback。

### 13.8 no-Node 本机证据

F01 必须分别记录：

1. publish 静态扫描；
2. Desktop 子进程树不包含 node；
3. 清理 PATH 后 Desktop 可以运行；
4. Node 仅作为外部 CDP 测试驱动时，明确写明测试驱动仍使用 Node。

不得把第 4 项写成干净 no-Node 目标机 PASS。

### 13.9 DPI 证据

F01 必须在完成 Canvas 最终门禁前：

- 确认 Desktop 实际生效的 DPI mode；
- 在 ADR 中确定项目属性、`Application.SetHighDpiMode` 或 manifest 中的唯一 DPI authority；
- 消除会使 Canvas 坐标、DPR 或 WebView2 证据失真的长期冲突；
- 增加 focused test 或可审计诊断，证明未来不会重新出现多重权威。

F01 的可执行证据包括：

- browser DPR 模拟；
- WebView2 force-device-scale-factor；
- JS devicePixelRatio；
- Canvas hit testing；
- native window size。

这些证据不能互相替代，也不能替代真实 Windows 系统缩放。F01 可以记录为 `NOT PERFORMED` 的现场矩阵仅包括：

- 真实 Windows 系统缩放切换；
- PerMonitorV2 跨显示器移动；
- 真实 200% 工业屏幕。

真实矩阵属于后续 release evidence；DPI authority 和当前有效 mode 的结论不得留到 F02 或发布阶段。

---

## 14. F01-7：FrontendV2 退役

### 14.1 前置条件

只有以下全部成立后才能退役：

- StudioUI build 成功；
- Desktop Debug build 成功；
- Release publish 成功；
- /studio startup 成功；
- flag off legacy 回归成功；
- missing StudioUI asset fail-closed；
- startup contract 通过；
- StudioStartupConfigV1 的 `uiKind` 仅为 `studio-ui`；
- DPI authority 已确定，Canvas/WebView2 证据未受冲突 mode 污染；
- Design Lab 通过；
- 现有 FlowCanvas 宿主通过；
- Playwright 通过；
- 真实 WebView2 Debug 和 publish smoke 通过。

### 14.2 必须删除或替换

- FrontendV2 源码；
- FrontendV2 package 和 lockfile；
- FrontendV2 build scripts；
- FrontendV2 MSBuild properties；
- FrontendV2 install target；
- FrontendV2 build target；
- /v2 output/publish/clean target；
- WorkspaceV2Enabled；
- FrontendV2 startup kind；
- /v2 static provider；
- V2 resolver 路径；
- FrontendV2 专属 Playwright；
- FrontendV2 专属 unit tests；
- FrontendV2 asset validator。

### 14.3 必须同步更新

- .github/workflows/ci.yml；
- setup-node cache dependency paths；
- npm ci working directory；
- frontend quality gates；
- SkipFrontendV2Install；
- .gitignore；
- CLAUDE.md 当前开发说明；
- DesktopWebRootResolverTests；
- ProgramStaticAssetsTests；
- StudioStartupPageResolverTests；
- WebView2HostTests；
- Studio2ArchitectureGuardTests 中的 V2 专属部分；
- StudioUINext README 当前事实；
- 初始化基线中的历史说明。

### 14.4 混合架构测试

Studio2ArchitectureGuardTests 同时包含 V2 专属断言和当前正式 owner/authority 断言。

不得删除整个文件。

处理方式：

1. 识别 V2-only cases；
2. 删除或迁移 V2-only cases；
3. 保留 Project、Flow、AgentRun、Preview、Results 等长期 authority guards；
4. 新增 StudioUI architecture guards；
5. 必要时将通用 guard 重命名到中性文件。

### 14.5 历史文档

- Git 历史不删除；
- docs/进行中/Studio2 保持历史取证；
- TODO 中历史完成记录不改写为当前事实；
- 在 StudioUINext README 明确 FrontendV2 已于 F01 退役；
- 不把旧 Goal 继续当作执行计划。

### 14.6 退役验收

- rg 无活动代码、CI 或测试引用 FrontendV2；
- rg 无活动代码引用 WorkspaceV2Enabled；
- 新 StudioUI TypeScript startup contract 和 browser fixture 只接受 `studio-ui` discriminator；
- clean build 不创建 wwwroot/v2；
- publish 不包含 /v2；
- legacy /index.html 仍可启动；
- Studio UI /studio/index.html 可启动；
- CI 配置指向 StudioUI；
- architecture guards 通过。

---

## 15. 执行顺序与依赖 DAG

### Wave 0：只读审计与 ADR

主协调 owner：

- Git 和 worktree；
- FrontendV2 引用清单；
- ADR；
- WebView2/CDP runner 事实清单；
- 当前有效 DPI mode 与唯一 DPI authority；
- 最终提交 CI 触发路径；
- 文件白名单；
- evidence 矩阵；
- 用户批准。

未批准 ADR 前不修改共享实现。

### Wave 1：共享地基

仅主协调 owner 修改：

- package.json；
- package-lock.json；
- Vite；
- Router；
- main.ts；
- App.vue；
- Design Tokens；
- startup contract；
- HostBridge interface；
- API transport interface；
- Desktop.csproj；
- StudioOptions；
- Resolver；
- WebView2Host；
- Program static assets；
- CI。

输出最小可构建骨架后，再开放叶子工作包。

### Wave 2：可并行叶子工作

在明确白名单下并行：

- Design primitives；
- Design Lab page；
- canonical Flow fixture；
- Canvas Lab owner；
- unit tests；
- Playwright scenario；
- WebView2 Studio UI scenario；若 F01-0 证明没有可复用 runner，则由唯一 owner 建立最小 runner 后再添加 scenario。

Canvas 只能有一个实现 owner。

### Wave 3：集成

主协调 owner：

- 合并叶子工作；
- 处理 shared contract；
- 运行前端质量门禁；
- 串行运行 Desktop tests；
- 运行 build/publish；
- 运行 WebView2；
- 在 DPI authority 已确定的前提下运行 Canvas 标准化 A/B；
- 作出 Canvas 结论。

### Wave 4：FrontendV2 退役

只有 Wave 3 通过后执行。

### Wave 5：收口

- 完成本地报告草案、技术债和 F02 输入；
- 获得用户视觉确认；
- 形成包含 FrontendV2 退役和全部本地证据的最终候选提交；
- 使用现有 `workflow_dispatch`、适当分支触发规则或 Draft PR 中的一种方式，对最终提交运行真实 GitHub Actions；
- 记录 workflow run URL、run ID、commit SHA 和 conclusion；
- 若 CI 证据写回仓库后产生新最终提交，必须针对新提交重新运行 CI；
- CI 外部服务故障时保持 AWAITING_CI；配置无法触发或最终提交验证失败时使用对应阻断码；
- 完成 Git 状态、工作区与远端一致性审计，只有全部门禁通过才标记 DONE。

---

## 16. Owner 与文件白名单

### 16.1 主协调 owner

独占：

- package 与 lockfile；
- Vite；
- Router；
- App Shell；
- Design Tokens；
- platform contracts；
- HostBridge；
- API transport core；
- Canvas canonical boundary；
- Desktop.csproj；
- Host；
- Feature Flags；
- CI；
- shared ADR；
- final report。

### 16.2 Design 叶子 owner 示例

~~~text
StudioUI/src/design-system/primitives/**
StudioUI/src/labs/design/**
StudioUI/tests/unit/design/**
~~~

不得修改：

- tokens；
- Router；
- package；
- main；
- App.vue；
- API；
- Host；
- Canvas。

### 16.3 Canvas owner 示例

~~~text
StudioUI/src/labs/canvas/**
StudioUI/tests/unit/canvas/**
ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/canvas*
~~~

任何 canonical FlowCanvas 文件移动或修改由主协调 owner 执行。

### 16.4 Verification owner 示例

~~~text
ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/**
scripts/studio-ui-next/**
~~~

共享 runner 的公共文件由主协调 owner 集成。

---

## 17. 测试计划

### 17.1 前端质量

在 StudioUI 目录：

~~~powershell
npm ci
npm run lint
npm run typecheck
npm run test:unit
npm run build
~~~

### 17.2 Desktop build

尊重 global.json 和仓库脚本。

至少执行：

~~~powershell
./scripts/dotnet.ps1 build ClearVision.Product/ClearVision.Product.sln -c Debug
~~~

若已完成 npm ci 并由 MSBuild 支持安全跳过安装，可使用对应 SkipStudioUiInstall 属性，但不得跳过 StudioUI build 本身。

### 17.3 Desktop focused tests

同一 Desktop.Tests.csproj 必须串行，并在一次调用中合并：

- StudioStartupPageResolverTests；
- WebView2HostTests；
- DesktopWebRootResolverTests；
- ProgramStaticAssetsTests；
- 新 StudioUiArchitectureGuardTests；
- 保留的长期 authority guards。

示例：

~~~powershell
& "./scripts/run-dotnet-test-serial.ps1" -Project "ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/ClearVision.Product.Desktop.Tests.csproj" -FullyQualifiedName StudioStartupPageResolverTests,WebView2HostTests,DesktopWebRootResolverTests,ProgramStaticAssetsTests,StudioUiArchitectureGuardTests
~~~

实际 FullyQualifiedName 参数按脚本支持方式填写，不并发启动第二个 dotnet test。

focused tests 或可审计诊断必须覆盖实际生效 DPI mode 与 ADR 选定 authority 的一致性，防止项目属性、`Application.SetHighDpiMode` 和 manifest 再次形成未解释冲突。

### 17.4 Playwright

在现有 UI tests 工程：

~~~powershell
npm ci
npx playwright test tests/e2e/studio-ui-next --reporter=list
~~~

必须区分：

- browser fixture；
- built StudioUI assets；
- legacy regression；
- StudioUI labs。

### 17.5 Publish

临时 publish 只写：

~~~text
.tmp/publish-check/f01/
~~~

示例：

~~~powershell
./scripts/dotnet.ps1 publish ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj -c Release -r win-x64 --self-contained true -o ./.tmp/publish-check/f01/release
~~~

### 17.6 Publish audit

检查：

- Desktop exe；
- appsettings；
- wwwroot/index.html；
- wwwroot/studio/index.html；
- wwwroot/studio/assets；
- StudioUI manifest；
- 无 wwwroot/v2；
- 无 node_modules；
- 无 package-lock；
- 无 StudioUI/src；
- 无 npm cache；
- 无非预期 source map；
- 无 Vite dev server 配置依赖。

### 17.7 WebView2

运行：

- Debug StudioUi flag on；
- Debug flag off；
- Debug 两 flag冲突；
- missing asset；
- Release publish flag on；
- Design Lab；
- Canvas Lab；
- authenticated read-only API。

运行前必须已经完成 6.4 的 runner 事实取证；存在则复用，确认不存在才允许建立唯一的最小 runner。报告必须写出入口、核心文件、调用链、隔离能力和最近一次真实运行结果。

### 17.8 Canvas 标准化 A/B

按 12.8 的统一条件比较 Legacy 与 StudioUI：

- 每个场景预热不少于 2 次；
- 每个场景正式执行不少于 5 次；
- 保留原始样本；
- 报告 median、p95、maximum、long tasks、未处理异常和 heap/可观察内存；
- 单组超过 20% 只标记 `PERFORMANCE_WARNING`；
- 连续三组标准化测试均超过 20% 且排除噪声，才标记 `BLOCKED_CANVAS_PERFORMANCE`；
- 正确性、身份、生命周期、持续泄漏和不可接受卡顿仍是硬门禁。

### 17.9 Git 与文档卫生

~~~powershell
git diff --check
git status --short
~~~

检查：

- 无临时 publish；
- 无截图散落根目录；
- 无 node_modules；
- 无日志；
- 无测试结果；
- 无秘密；
- 无无关修改。

### 17.10 GitHub Actions

当前 workflow 的自动 push 分支可能不包含 `studio-ui-next`，但仓库存在 `workflow_dispatch`。F01 必须使用以下任一真实路径验证最终提交：

1. 优先使用可对目标 ref 生效的 `workflow_dispatch`；
2. 为本分支临时或正式加入适当触发规则；
3. 创建 Draft PR 触发现有 pull request workflow。

要求：

- 普通 `git push` 不得写成 CI PASS；
- workflow 必须针对最终提交 SHA；
- 记录 workflow run URL、run ID、commit SHA 和 conclusion；
- CI 外部服务故障时保持 `AWAITING_CI`，不得标记 DONE；
- 无法建立有效触发路径时使用 `BLOCKED_CI_CONFIGURATION`；
- 最终提交的 workflow 失败时使用 `BLOCKED_CI_FAILED`，修复并重新运行；
- 真实相机、PLC、Station 和完整现场验收仍不属于 F01。

### 17.11 明确不替代

- Playwright PASS 不替代 WebView2；
- WebView2 force scale 不替代真实 DPI；
- publish scan 不替代 no-Node 目标机；
- branch push 不替代完整 CI；
- fixture API 不替代真实现场 endpoint。

---

## 18. 时间安排

### Day 0～1：ADR 与最小骨架

- Git 审计；
- 共享 owner；
- WebView2/CDP runner 事实取证；
- 有效 DPI mode 与唯一 authority 决策；
- 最终提交 CI 触发路径；
- ADR；
- StudioUI scaffold；
- Vite base；
- hash router；
- frontend quality gates。

### Day 2～3：Build、Host、Platform

- MSBuild；
- /studio static mapping；
- StudioUiEnabled；
- startup schema；
- `uiKind: 'studio-ui'` 的 StudioStartupConfigV1；
- startup reader；
- minimal API transport；
- Host adapter interface；
- focused Desktop tests。

### Day 4：Design 与 browser tests

- tokens；
- representative primitives；
- Design Lab；
- central Playwright；
- light/dark；
- 1366 和 1920；
- keyboard/reduced motion。

### Day 5～6：FlowCanvas 与 WebView2

- canonical Flow fixture；
- existing FlowCanvas owner；
- lifecycle matrix；
- interaction matrix；
- 标准化 A/B performance baseline；
- DPI authority focused test/诊断与分层证据；
- Debug WebView2；
- Release publish WebView2。

若 F01-0 证明候选 runner 不存在或不可运行，本时段由唯一 owner 建立最小 runner，并使用 Day 9 缓冲；不得并行保留第二套实现。

### Day 7～8：FrontendV2 退役与回归

- 删除 V2 build/host/CI；
- 更新 guards；
- clean build；
- publish audit；
- legacy regression；
- StudioUI regression；
- 形成最终候选提交并准备 CI 取证。

### Day 9：缓冲

只用于：

- MSBuild 增量问题；
- WebView2 user-data 或端口隔离；
- Canvas dispose；
- test flake；
- CI 配置修复；
- 最终提交 GitHub Actions 运行、失败修复与重跑；
- 文档与证据收口。

不得趁缓冲时间迁移业务页面。

GitHub Actions 排队/外部服务故障和用户确认等待不计入 6～9 个有效工作日，但未取得这些证据前状态只能是 AWAITING_VISUAL_CONFIRMATION、AWAITING_CI 或对应 BLOCKED，不能标记 DONE。

---

## 19. 风险登记

| 风险 | 概率 | 影响 | 应对 |
|---|---:|---:|---|
| 未经批准创建第二 Canvas 内核 | 中 | 极高 | F01 禁止 Pointer Kernel，失败后独立 ADR |
| 新 adapter 形成第二 HTTP/HostBridge | 中高 | 高 | ADR、单直接访问点、architecture guard |
| 生成资产污染源码 wwwroot | 中 | 高 | 只写 obj、OutDir、PublishDir |
| hash/history 路由与 Desktop 静态服务不一致 | 中 | 高 | F01 默认 hash history |
| 两个启动 flag 同时开启 | 中 | 高 | fail-closed Diagnostic |
| FrontendV2 与 StudioUI 同时构建拖慢或冲突 | 高 | 中高 | 仅过渡保留，门禁后单独退役 |
| 删除 FrontendV2 破坏 CI | 高 | 高 | 退役清单必须包含 ci.yml 和 cache |
| 删除混合架构守卫造成覆盖丢失 | 中 | 高 | 只移除 V2-only cases，保留长期 guards |
| project/operator smoke 缺少认证 | 高 | 中 | 隔离 DB setup/login 与同源 sessionStorage |
| WebView2 runner 候选未被真实验证 | 中高 | 高 | F01-0 记录代码锚点与运行结果；存在则复用，不存在则建立唯一最小 runner |
| WebView2 使用共享 user-data | 高 | 高 | runner 参数化并清理 |
| 固定 5000/CDP 端口冲突 | 中高 | 高 | 测试串行、参数化、禁止前端端口扫描 |
| DPI 多重权威使 Canvas/DPR 证据失真 | 高 | 高 | F01 ADR 决定唯一 authority，确认实际 mode，并用 focused test/诊断防回归 |
| DPI 模拟被误报为真实 DPI | 高 | 高 | 分开 Browser DPR、WebView2 simulated scale、真实系统 DPI 与跨屏证据 |
| Canvas 单次性能噪声误杀方案 | 中高 | 高 | 同条件 A/B、至少 2 次预热和 5 次测量、原始样本、warning-first |
| studio-ui-next 无有效 CI 触发路径 | 中 | 高 | 优先 workflow_dispatch，否则适当 trigger 或 Draft PR；无法配置则阻断 |
| 最终提交 CI 失败 | 中 | 高 | 标记 BLOCKED_CI_FAILED，修复后对新最终 SHA 重跑 |
| Node 测试驱动被误报为 no-Node 环境 | 中 | 高 | 明确外部 driver 与 Desktop runtime |
| Design Lab 过度扩张为完整系统 | 中 | 中 | F01 仅代表性 primitives，F02 完成定型 |
| F01 偷跑业务 capability | 中 | 高 | scope guard 和 review |
| 稳定线在 F01 末尾自动合入导致失效 | 中 | 高 | 开始时审计，结束只 fetch/report |

---

## 20. 阻断码

~~~text
BLOCKED_WRONG_BRANCH
BLOCKED_OVERLAPPING_DIRTY_WORKTREE
BLOCKED_REMOTE_DIVERGED
BLOCKED_ADR_NOT_APPROVED
BLOCKED_ARCHITECTURE_DEVIATION
BLOCKED_SECOND_HTTP_INFRASTRUCTURE
BLOCKED_SECOND_HOST_BRIDGE
BLOCKED_CANVAS_FOUNDATION
BLOCKED_CANVAS_NEW_KERNEL_APPROVAL
BLOCKED_STUDIO_UI_BUILD
BLOCKED_STUDIO_UI_ASSETS
BLOCKED_STARTUP_CONTRACT
BLOCKED_WEBVIEW2_RUNNER_FACTS
BLOCKED_WEBVIEW2_STARTUP
BLOCKED_WEBVIEW2_ISOLATION
BLOCKED_CANVAS_PERFORMANCE
BLOCKED_DPI_AUTHORITY
BLOCKED_RELEASE_PUBLISH
BLOCKED_FRONTENDV2_RETIREMENT
BLOCKED_AUTH_SMOKE
BLOCKED_VISUAL_DIRECTION
BLOCKED_CI_CONFIGURATION
BLOCKED_CI_FAILED
~~~

工作区存在未提交内容时，不应机械阻断。只有目标文件重叠、来源不明或无法安全隔离时才使用 BLOCKED_OVERLAPPING_DIRTY_WORKTREE。

阻断报告必须包含：

- 失败步骤；
- 真实错误；
- 已运行命令；
- 已完成内容；
- 未完成内容；
- 是否修改文件；
- 是否产生提交；
- 是否可保留部分成果；
- 建议的最小后续工作。

---

## 21. 提交策略

建议 6～8 个可独立审查的提交：

~~~text
docs(studio-ui): approve F01 foundation ADR
chore(studio-ui): establish Vue build foundation
feat(desktop): add Studio UI startup route
feat(studio-ui): add platform and design labs
test(studio-ui): validate existing FlowCanvas lifecycle
test(desktop): extend WebView2 Studio UI smoke
refactor(desktop): retire FrontendV2 pipeline
docs(studio-ui): record F01 decision and evidence
~~~

规则：

- 每个提交保持可构建；
- 主协调 owner 修改共享文件；
- 叶子 owner 不直接推共享分支；
- 不强推；
- 不随意 rebase；
- 提交前 fetch origin；
- 远端前进时先分析；
- 不夹带后端无关修复；
- FrontendV2 退役使用单独提交，便于 Git 回滚；
- 不在文档中硬编码最终提交 SHA，最终报告运行时填写。

---

## 22. 最终交付清单

### 22.1 代码

- StudioUI；
- build/publish targets；
- /studio static provider；
- StudioUiEnabled；
- startup schema；
- `uiKind: 'studio-ui'` 的 StudioStartupConfigV1；
- startup reader；
- minimal Host adapter；
- minimal API transport；
- Design Lab；
- existing FlowCanvas Lab；
- canonical fixture；
- unit tests；
- Playwright tests；
- 经事实取证后复用的 WebView2 Studio UI scenario，或唯一的最小 runner + scenario；
- StudioUI architecture guards；
- FrontendV2 retirement。

### 22.2 文档

- F01 ADR，包含 runner 事实清单、DPI authority 与 CI 路径；
- F01 执行卡；
- Canvas 验证报告，包含标准化 A/B 原始样本与 warning/pass/blocked 结论；
- WebView2 报告，包含 runner 入口、核心文件、调用链、能力和最近运行结果；
- DPI authority 与分层 DPI 证据报告；
- GitHub Actions workflow run 证据；
- publish/no-Node 本机报告；
- Design Lab 截图索引；
- FrontendV2 退役清单；
- 技术债；
- F02 输入；
- NOT RUN / NOT PERFORMED 清单。

### 22.3 证据

- npm ci；
- lint；
- typecheck；
- unit；
- frontend build；
- Desktop Debug build；
- Desktop focused tests；
- Playwright；
- Release publish；
- publish asset scan；
- WebView2 Debug；
- WebView2 publish；
- flag off/on/conflict；
- missing asset；
- Canvas interaction；
- mount/unmount；
- 100/300 node 标准化 A/B 原始样本、median、p95、maximum、long tasks 与 memory；
- 实际生效 DPI mode 与 authority 一致性诊断；
- Desktop runtime child-process audit；
- 针对最终提交的 GitHub Actions URL、run ID、commit SHA 和 conclusion；
- git diff --check。

---

## 23. F01 通过门禁

### 23.1 Architecture

- ADR 已批准；
- 无第二 Canvas 内核；
- 无第二 EventBus/ServiceRegistry；
- HTTP 和 HostBridge 唯一性有守卫；
- 新 UI 不导入 legacy app.js 或 FrontendV2；
- StudioStartupConfigV1 与 browser fixture 只接受 `studio-ui` discriminator；
- Desktop 当前有效 DPI mode 与唯一 authority 已在 ADR 中闭环；
- 不新增业务 authority 或写 endpoint。

### 23.2 Build

- StudioUI 独立构建；
- Desktop build 自动包含 StudioUI；
- publish 自动包含 StudioUI；
- 生成资产只存在于受控输出；
- asset base 正确；
- clean/incremental/rebuild 正确；
- publish 无 Node/source/dev artifacts。

### 23.3 Host

- legacy 默认不变；
- StudioUi flag on 只有新根；
- 两 flag 同开 Diagnostic；
- missing asset fail-closed；
- startup config typed、readonly、versioned；
- StudioStartupConfigV1 的 uiKind 固定为 `studio-ui`，只在 StudioUI 启动时注入；
- localhost 同源；
- 不支持热切换。

### 23.4 Platform

- desktop 缺 startup fail-fast；
- browser fixture 显式；
- 无端口扫描；
- API transport 只有一个 request core；
- AbortSignal 有效；
- 401/403/409/5xx typed；
- 无业务写请求；
- Host adapter 可 dispose。

### 23.5 Design

- light/dark；
- 1366×768；
- 1920×1080；
- keyboard focus；
- reduced motion；
- representative primitives；
- 不加载 legacy CSS；
- 用户视觉方向确认。

### 23.6 Canvas

- 使用现有 FlowCanvas；
- 使用 canonical adapter；
- 真实 DTO ports；
- interaction matrix；
- zoom/pan/resize；
- browser DPR；
- DPI authority 已确定，测试所用有效 mode 可审计；
- mount/unmount；
- serialize/reload identity；
- dispose 无持续增长；
- 100/300 节点标准化 A/B，至少 2 次预热、5 次正式测量并保留原始样本；
- median、p95、maximum、long tasks 和 memory 已报告；
- 单组超过 20% 采用 `PERFORMANCE_WARNING`，只有连续三组标准化复核超阈且排除噪声才阻断；
- CANVAS_DECISION=REUSE_EXISTING_FLOW_CANVAS。

若 Canvas 不通过，F01 不得标记 DONE。

### 23.7 Verification

- WebView2/CDP runner 事实清单已完成，复用或唯一最小实现决策有证据；
- Playwright 通过；
- WebView2 Debug 通过；
- WebView2 Release publish 通过；
- flag off/on/conflict 通过；
- missing asset 通过；
- console 无未处理错误；
- 进程退出；
- user-data 清理；
- git diff --check；
- 未执行证据明确标记。

### 23.8 Retirement

- FrontendV2 源码和构建链退役；
- WorkspaceV2Enabled 退役；
- /v2 不再生成；
- CI 更新；
- 新 startup contract 与 browser fixture 不含 FrontendV2 uiKind；
- V2-only tests 退役；
- 长期 architecture guards 保留；
- StudioUI guards 生效。

### 23.9 CI

- 已通过 workflow_dispatch、适当分支触发规则或 Draft PR 获得真实 GitHub Actions；
- workflow run 针对最终提交 SHA；
- workflow run URL、run ID、commit SHA 和 conclusion 已记录；
- 普通 push 未被当作 CI PASS；
- CI conclusion 成功；外部服务故障时保持 AWAITING_CI，配置或测试失败时使用对应阻断码。

---

## 24. 不属于 F01 DONE 的发布门禁

以下必须在正式发布切换前完成，但不伪装为 F01 本地证据：

- 干净 no-Node Windows 机器启动；
- 真实 100%、125%、150%、200% 系统缩放；
- 多显示器移动；
- 安装包升级与回滚；
- 真实相机、PLC、Station；
- 现场长时间运行。

F01 完成报告必须列出这些项目的当前状态。真实系统 DPI 与跨显示器矩阵可以是 `NOT PERFORMED`，但唯一 DPI authority 和针对最终提交的真实 GitHub Actions 必须已在 F01 完成，不能放入本节后推。

---

## 25. F02 入口条件

只有 F01 DONE 后才能启动 F02。

F02 入口必须同时满足：

- F01 本地技术门禁全部通过；
- 用户视觉方向已确认；
- 最终提交已通过真实 GitHub Actions；
- DPI authority 已确定；
- Canvas 结论唯一；
- FrontendV2 已完整退役；
- 工作区干净，且本地与 `origin/studio-ui-next` 一致。

F02 建议名称：

> F02｜Design System 定型与只读低风险 Capability

F02 输入：

- 稳定 StudioUI build/host；
- startup schema v1；
- minimal API/Host adapters；
- Design tokens foundation；
- 用户视觉方向；
- existing FlowCanvas 唯一结论；
- Playwright 与 WebView2 baseline；
- FrontendV2 已退役；
- 技术债清单。

F02 优先：

- App Shell；
- navigation；
- About/Diagnostics；
- operator catalog 只读视图；
- Station 只读状态；
- Results 只读摘要；
- 通用 Empty/Error/Loading patterns。

F02 不应优先：

- Project create/delete/save；
- Settings 写操作；
- Flow 正式工作台；
- GlobalVariables；
- Inspection 控制；
- AI；
- Runtime Package；
- Station deployment。

---

## 26. 最终报告模板

~~~markdown
# Studio UI Next F01 完成报告

## 1. Git
- Initial SHA:
- Final SHA:
- origin/studio-ui-next:
- origin/codex初稿:
- Worktree clean:
- Final status:

## 2. ADR
- Approved:
- Router:
- Asset path:
- Startup schema:
- HTTP uniqueness:
- HostBridge uniqueness:
- Canvas rule:
- WebView2 runner decision:
- DPI authority:
- CI trigger path:

## 3. Build
- npm ci:
- lint:
- typecheck:
- unit:
- frontend build:
- Desktop Debug:
- Release publish:
- Incremental:
- Asset audit:

## 4. Host
- Legacy flag off:
- Studio UI flag on:
- Flag conflict:
- Missing asset:
- Startup readonly:
- Startup schema:
- StudioStartupConfigV1 uiKind:
- FrontendV2 compatibility fields location:

## 5. Platform
- Startup reader:
- Host adapter:
- API transport:
- Abort:
- Authenticated read-only smoke:
- Architecture guards:

## 6. Design
- Light:
- Dark:
- 1366×768:
- 1920×1080:
- Keyboard:
- Reduced motion:
- User confirmation:

## 7. Canvas
- Canonical FlowCanvas:
- Fixture:
- Interaction:
- Lifecycle:
- Identity:
- Legacy baseline:
- StudioUI baseline:
- Measurement method:
- Raw samples:
- Median delta:
- P95 delta:
- Maximum:
- Long tasks:
- Memory / heap:
- 300 nodes:
- Warning / Pass / Blocked:
- Decision:

## 8. WebView2
- Existing runner actually exists:
- Entry script:
- Core implementation files:
- Call chain:
- Dependencies:
- Supported scenarios:
- Starts real Desktop:
- Connects real WebView2:
- Uses CDP:
- Custom ports:
- Isolated database:
- Isolated WebView2 user-data:
- Automatic shutdown and cleanup:
- Latest actual run:
- Debug:
- Release publish:
- Flag matrix:
- Missing asset:
- Console:
- Process cleanup:
- User-data cleanup:

## 9. FrontendV2 retirement
- Source removed:
- MSBuild removed:
- Host removed:
- CI updated:
- Tests migrated:
- /v2 absent:
- New TypeScript discriminator is StudioUI-only:

## 10. DPI evidence
- DPI authority:
- Effective runtime DPI mode:
- Browser DPR:
- WebView2 simulated scale:
- JS devicePixelRatio:
- Canvas hit testing:
- Native window size:
- Real Windows DPI:
- Per-monitor move:

## 11. GitHub Actions
- Trigger method:
- Workflow run URL:
- Run ID:
- Commit SHA:
- Conclusion:

## 12. Evidence truth table
- Browser:
- Playwright:
- WebView2:
- Publish:
- Runtime no Node child:
- Clean no-Node target:
- Hardware:

## 13. NOT RUN / NOT PERFORMED
- ...

## 14. Risks and debt
- ...

## 15. F02
- READY / NOT READY
- Reason:
~~~

---

## 27. 结论

F01 的成功标准不是页面数量，也不是同时造出两套 Canvas 进行主观比较。

F01 必须做到：

1. 新 Studio UI 独立、可构建、可发布；
2. Host 只加载一个活动根；
3. 新 StartupConfigV1 只描述 StudioUI，startup、HTTP、HostBridge 和 Canvas 不形成第二 authority；
4. WebView2/CDP runner 的真实锚点和能力先被取证，存在则复用，不存在则只建立唯一最小实现；
5. DPI authority 在 F01 ADR 中确定，当前有效 mode 可审计；
6. 现有 FlowCanvas 在 Vue 生命周期中被真实验证，并以标准化 A/B、原始样本和 warning-first 规则评估性能；
7. 浏览器、Playwright、真实 WebView2、模拟 DPI 和真实 Windows DPI 证据分开；
8. FrontendV2 在新链路通过后完整退役；
9. 最终提交至少通过一次真实 GitHub Actions；未运行的真实 DPI 矩阵、干净 no-Node 目标机和硬件证据保持诚实。

只有本地技术门禁、用户视觉确认、CI、DPI authority、唯一 Canvas 结论、FrontendV2 退役以及工作区/远端一致性全部完成，F02 的 Design System 和低风险 capability 迁移才具备可靠地基。
