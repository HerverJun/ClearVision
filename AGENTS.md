# AGENTS

## 项目定位

- ClearVision 是工业视觉桌面平台，不是单一前端应用。主解决方案位于 `ClearVision.Product/ClearVision.Product.sln`。
- Studio 是工程配置与调试端：`ClearVision.Product.Desktop` 以 WinForms 承载 WebView2，并在同一进程启动 ASP.NET Core 本地 API。
- Runtime 负责版本化执行快照、运行包加载与流程执行；Station 是独立的现场 WinForms 宿主，加载运行包并与 Studio 同步。前端不得替代 Runtime 或 Station。
- 当前正式前端仍位于 `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/`。分支背景、基线和阶段门禁见 `docs/进行中/StudioUINext/`。

## `studio-ui-next` 分支使命

- `studio-ui-next` 是独立的 Studio 前端重构线；`codex初稿` 是稳定维护与回退基线。
- 稳定线变更应经过审计后单向合入 `studio-ui-next`。不得反向把未完成的新前端实验混入稳定线。
- 本分支可以重构前端 composition root、路由、组件、设计系统、UI 状态投影、HostBridge 适配层和 Canvas 宿主边界。
- 本分支不得擅自重造后端业务权威、执行状态机、保存协议、运行包格式或 Station 现场链路。
- `ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/` 是已废弃的迁移原型，只能用于历史取证。不得复制其代码、目录结构、Goal 路线或视觉实现作为新前端地基。

## 强制架构红线

- 一个 capability 在同一时刻只能有一个 mounted owner、一个订阅集合和一个写入口。Feature Flag 关闭或切换时，旧 owner 必须 unmount/dispose，并停止订阅、timer、SSE、请求和写操作；CSS 隐藏不算卸载。
- Pinia、Vue state、DOM、localStorage 和前端缓存只能保存 UI 投影、编辑草稿与可丢弃缓存，不能成为 Project、Flow、GlobalVariables、AgentRun 或执行结果权威。
- Project、Flow、GlobalVariables 和正式 Project assets 的保存必须复用现有 Application Service，并最终进入 `ProjectSaveCoordinator`。不得新增第二 Project save endpoint、第二保存 client 或前端私有持久化链。
- UI 本地 revision 只用于草稿和 stale 防护；后端 `PersistenceRevision` 才是正式保存并发身份，两者不得混用。
- AgentRun、`AgentRunEventStore`、终态 reservation、replay/recovery 和 Workspace Snapshot 权威不在前端重构范围内。新 UI 只能消费既有 endpoint、事件流和投影。
- Runtime Package、RuntimeHost、Station、Inspection 执行协调和正式结果持久化不能由前端私有模型替代。
- 正式执行与检测控制使用现有 authenticated HTTP/SSE 入口；WebMessage bridge 只保留宿主能力适配，不得恢复或新增绕过 HTTP 权威的执行通道。
- Vue 不直接代理或长期持有 `FlowCanvas`、`ImageCanvas`、`EventSource`、`AbortController`、WebView2 bridge 等命令式对象；由明确的 adapter/lifecycle owner 创建、暴露窄接口并负责 dispose。
- 不得建立第二 EventBus、第二 ServiceRegistry、第二 HTTP 基础设施、第二 Canvas 内核或第二 HostBridge。确需新增时，必须先有独立 ADR、冲突分析和明确批准。
- 新前端必须复用现有后端契约；发现契约缺口时先报告并由主协调代理决定，不得由 capability 实现者自行扩权。

## 并行开发与子代理

- 允许并行只读审计，也允许无文件重叠、无共享状态权威的独立工作包并行实现。
- Design System primitives、独立叶子组件、只读/低状态 capability 和 capability-local 测试适合并行；FlowCanvas + Property + Preview、Project save + GlobalVariables、bootstrap/router/providers 不得拆成多个并行 owner。
- 每个 capability 只能有一个实现 owner。子代理必须获得明确的文件白名单；越界需求只报告，不自行修改。
- 共享文件只由主协调代理修改和集成，包括 `package.json`、lockfile、Vite、Router、App Shell、Design Tokens、API contracts、HostBridge、`.csproj`、CI、Feature Flags、根 `AGENTS.md` 和共享 ADR/基线文档。
- 不得让多个代理分别创建状态树、API client、EventBus、保存链、Canvas facade 或同名 capability owner 后再事后合并。
- 同一 `.csproj` 的测试必须串行。不同项目只有在 build output、结果目录、数据库、端口、PLC/设备等外部资源完全隔离时才可并行。
- 多 worktree 同时运行时必须隔离 HTTP/CDP/PLC 端口、WebView2 user-data、测试数据库、测试结果目录和 publish 目录。

## Git 与 worktree

- 当前 worktree 只能操作 `studio-ui-next`；不得进入、修改、清理或处理其他 worktree 的未提交内容。
- 不得自行切换分支、stash、reset、clean、删除文件或修复其他 worktree 元数据。
- `origin/codex初稿` 的稳定变更按需通过 Git 单向合入本分支；不得手工复制工作区文件冒充合并。
- 共享分支禁止强推，不随意 rebase。提交和推送前先 `git fetch origin --prune`，确认远端未发生不兼容前进或历史分叉。
- 若远端 `studio-ui-next` 已存在且历史不一致，停止并报告，不得覆盖。
- 首次推送后，upstream 必须指向 `origin/studio-ui-next`；`origin/codex初稿` 只作为显式同步源，避免普通 pull 意外混入维护线。
- 同步冲突按语义处理：后端 authority/contracts/security 修复以稳定线为准，新前端专属目录以本分支为准；Host、build、flags、CI 和共享 contracts 只能由主协调代理逐项合并，禁止整文件粗暴选择 `ours/theirs`。

## .NET 测试

- 同一 `.csproj` 绝不能同时启动多个 `dotnet test` 进程。
- 定向测试优先使用 `& "./scripts/run-dotnet-test-serial.ps1" ...`；必须从当前 PowerShell 调用，禁止再包一层 `powershell.exe -File`，以免遗留子进程。
- 同一测试项目需要验证多个类时，通过重复的 `-FullyQualifiedName` 合并到一次调用，不要并发启动多个命令。
- 匹配任务时优先使用固定入口：
  - `./scripts/run-tests-services-regression.ps1`
  - `./scripts/run-tests-phase42-regression.ps1`
  - `./scripts/run-tests-plc-regression.ps1`
  - `./scripts/run-tests-desktop-endpoints.ps1`
- 当前会话中同一项目已成功构建后，后续定向测试优先使用 `-NoBuild -NoRestore`。
- 尊重根目录 `global.json`，SDK 选择存在歧义时优先使用仓库 `scripts/dotnet.ps1`。

## 前端、截图、发布与临时产物

- 当前 UI/Playwright 入口位于 `ClearVision.Product/tests/ClearVision.Product.UI.Tests/`。其静态 Chromium 测试不能等同于真实 WebView2、真实 DPI 或真实端点联调。
- 截图优先使用内置浏览器或仓库脚本。只有浏览器无法完成所需交互或捕获时才使用 Computer Use。
- 临时 `dotnet publish` 或打包验证只能写入 `./.tmp/publish-check/` 或仓库外；完成后清理，除非用户明确要求保留。
- 不得在仓库根目录创建新的未忽略 publish、截图、日志、测试结果或生成物目录。
- Release publish、无 Node 目标机启动、真实 WebView2、DPI/分辨率矩阵、真实相机/PLC/Station 和完整 CI 是不同证据，不能互相替代。
- 普通分支 push 不等于完整 CI。未实际运行的验证必须明确写为 `NOT RUN` 或 `NOT PERFORMED`。

## 真实性要求

- 先读当前代码和配置，再修改。旧文档、旧 Goal、文件日期、历史测试数量和过去的 PASS 只能作为线索。
- 文档与代码冲突时，以当前代码和当前配置为准，并记录文档漂移。
- 报告时区分代码事实、文档事实和推断；不得宣称未运行的 build、test、Playwright、WebView2、DPI、现场硬件或真实模型验证已经通过。
- 根 `AGENTS.md` 只保留长期规则，不写易过期的测试数量、算子数量、日期、临时 SHA 或旧 Goal 状态。
