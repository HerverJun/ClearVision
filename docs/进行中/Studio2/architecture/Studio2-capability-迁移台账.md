# Studio 2.0 capability 迁移白名单与 Feature Flag 台账

> 状态：G01 台账
> 日期：2026-07-01
> Initial SHA：`789e9ec643390f5a79c68cfa6c4b401c1a679be3`
> 维护规则：本文件是 Studio 2.0 capability 迁移的唯一白名单。未登记的 capability 不得迁移、挂载或建立运行时 Feature Flag。

## 总规则

- 同一 capability 任一时刻只能有一个 mounted owner、一个订阅集合、一个 timer 集合和一个写入口。
- Feature Flag 只控制挂载 owner，不改变业务权威。flag on 后旧实现必须不挂载、不订阅、不运行 timer、不持有资源。
- 不允许通过 CSS 隐藏旧实现冒充切换。
- 本轮只登记 flag，不实现 runtime flag，不修改 `appsettings`、生产配置或发布包。
- V2 typed API 必须包裹现有 `httpClient`，不得重做 auth、端口发现和网络错误策略。
- Project、Flow、GlobalVariables 的正式保存仍经 `ProjectService + ProjectSaveCoordinator`。
- Pinia、DOM、localStorage 只能作为投影或编辑草稿，不得作为正式业务权威。

## capability 台账

| capability | legacy owner | planned V2 owner | read source | write entry | migration Goal | cutover Goal | deletion Goal | Feature Flag | flag off 行为 | flag on 行为 | rollback | 当前状态 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Workspace Shell | `app.js` + `viewManager` | V2 Workspace Shell root | 现有 DOM view state、Project 投影、Host 注入 API base URL | 只允许切换 view 和注册 capability；不写业务 authority | G03 | G03 | G16 | `Studio2.WorkspaceShell` | 旧 `app.js` 是唯一 mounted root | V2 Shell 是唯一 mounted root，旧 Shell 不订阅、不运行 timer | flag off 恢复旧 Shell | LOCKED |
| Flow Editor | `FlowCanvas` + `FlowEditorInteraction` + `FlowCanvasAdapter` | V2 Flow Editor | `FlowCanvasAdapter.serialize()`、Project flow 投影 | V2 只经 `FlowCanvasAdapter` 修改画布；正式保存经 ProjectSaveCoordinator | G04A | G04B | G16 | `Studio2.FlowEditor` | 旧 Flow Editor 挂载并持有 FlowCanvas | V2 Flow Editor 挂载，旧 Flow Editor release 订阅和 timer | flag off 恢复旧 Flow Editor 与 adapter | LOCKED |
| Property Panel | legacy property panel/sidebar | V2 Property Panel | selected node projection、operator metadata、Project schema | editor draft commit 到 FlowCanvasAdapter 或 Project save path | G15.1 | G15.1 | G16 | `Studio2.PropertyPanel` | 旧 Property Panel 是唯一 owner | V2 Property Panel 是唯一 owner，旧 panel 不订阅 selection | flag off 恢复旧 property panel | LOCKED |
| Preview Panel | `NodePreviewCoordinator`、preview overlay、image viewer | V2 Preview Panel | preview endpoint、artifact URL、current flow/node projection | 不写正式结果；draft request 只走既有 preview API | G15.2 | G15.2 | G16 | `Studio2.PreviewPanel` | 旧 preview panel/overlay 挂载 | V2 Preview Panel 挂载，旧 preview overlay 释放资源 | flag off 恢复旧 preview panel | LOCKED |
| Global Variables | legacy global variable panel/store | V2 Global Variables editor | Project `globalVariables` schema、Project variable session projection | `ProjectService.UpdateGlobalVariablesAsync()` -> `ProjectSaveCoordinator` | G15.3 | G15.3 | G16 | `Studio2.GlobalVariables` | 旧变量面板是唯一 mounted owner | V2 变量面板是唯一 owner，旧面板不订阅 Project | flag off 恢复旧变量面板 | LOCKED |
| Settings | legacy settings/theme/diagnostic controls | V2 Settings | 当前 settings projection、Host/desktop settings endpoint | 只写既有 settings endpoint 或 local UI preference 草稿 | G15.5 | G15.5 | G16 | `Studio2.Settings` | 旧 Settings 控件有效 | V2 Settings 有效，旧控件不挂载 | flag off 恢复旧设置入口 | LOCKED |
| Project | `projectManager` + legacy Project page | V2 Project page/API wrapper | `/api/projects/*`、Project DTO、`PersistenceRevision` | V2 typed API 包裹 `httpClient`，正式写入仍经 ProjectService | G15.8 | G15.8 | G16 | `Studio2.Project` | 旧 Project 页面是唯一 owner | V2 Project 页面是唯一 owner，旧页面不订阅 Project list | flag off 恢复旧 Project 页面 | LOCKED |
| Inspection | `inspectionController` + legacy inspection panel | V2 Inspection workspace | existing inspection state、run endpoints、current project projection | 只调用既有 inspection/run API，不写 AgentRun authority | G15.6 | G15.6 | G16 | `Studio2.Inspection` | 旧 Inspection panel 运行 | V2 Inspection 挂载，旧 panel 停止 realtime/timer | flag off 恢复旧 Inspection panel | LOCKED |
| Results/Review | legacy results panel + analytics refresh | V2 Results/Review | result history endpoint、artifact metadata、Scene projection | 只读 review/annotation draft；正式 evidence 走后续 G14C 契约 | G15.7 | G15.7 | G16 | `Studio2.ResultsReview` | 旧 Results panel 挂载并管理 refresh timer | V2 Results 挂载，旧 refresh timer 关闭 | flag off 恢复旧 Results panel | LOCKED |
| AI Panel | legacy AI panel + generation controller | V2 AI Panel shell | AgentRun events、session projection、existing AI controller | 只调用现有 AgentRun/build entry，不建立第二 Agent authority | G15.4 | G15.4 | G16 | `Studio2.AIPanel` | 旧 AI Panel 是唯一 owner | V2 AI Panel 是唯一 owner，旧 panel 不订阅 AgentRun | flag off 恢复旧 AI Panel | LOCKED |

## Feature Flag 生命周期

| Feature Flag | owner | 创建 Goal | runtime 实现 Goal | cutover Goal | 删除 Goal | 默认值 | flag off 必须保证 | flag on 必须保证 | 当前状态 |
|---|---|---|---|---|---|---|---|---|---|
| `Studio2.WorkspaceShell` | Workspace Shell | G01 登记 | G03 | G03 | G16 | off | 旧 Shell 唯一 mounted | V2 Shell 唯一 mounted | REGISTERED_ONLY |
| `Studio2.FlowEditor` | Flow Editor | G01 登记 | G04A | G04B | G16 | off | 旧 Flow Editor 持有 FlowCanvas | V2 经 FlowCanvasAdapter 写入 | REGISTERED_ONLY |
| `Studio2.PropertyPanel` | Property Panel | G01 登记 | G15.1 | G15.1 | G16 | off | 旧 Property Panel 订阅 selection | V2 Property Panel 唯一订阅 selection | REGISTERED_ONLY |
| `Studio2.PreviewPanel` | Preview Panel | G01 登记 | G15.2 | G15.2 | G16 | off | 旧 preview overlay 生效 | V2 Preview 唯一持有 preview 资源 | REGISTERED_ONLY |
| `Studio2.GlobalVariables` | Global Variables | G01 登记 | G15.3 | G15.3 | G16 | off | 旧变量面板写入后端 | V2 变量面板写入同一后端 | REGISTERED_ONLY |
| `Studio2.Settings` | Settings | G01 登记 | G15.5 | G15.5 | G16 | off | 旧 Settings 控件生效 | V2 Settings 控件生效 | REGISTERED_ONLY |
| `Studio2.Project` | Project | G01 登记 | G15.8 | G15.8 | G16 | off | 旧 Project 页面唯一 owner | V2 Project 页面唯一 owner | REGISTERED_ONLY |
| `Studio2.Inspection` | Inspection | G01 登记 | G15.6 | G15.6 | G16 | off | 旧 Inspection panel 和 timer 生效 | V2 Inspection 唯一 owner，旧 timer 停止 | REGISTERED_ONLY |
| `Studio2.ResultsReview` | Results/Review | G01 登记 | G15.7 | G15.7 | G16 | off | 旧 Results panel 和 refresh timer 生效 | V2 Results 唯一 owner，旧 timer 停止 | REGISTERED_ONLY |
| `Studio2.AIPanel` | AI Panel | G01 登记 | G15.4 | G15.4 | G16 | off | 旧 AI Panel 订阅 AgentRun | V2 AI Panel 订阅现有 AgentRun 投影 | REGISTERED_ONLY |

## 非阻断技术债

- G01 只登记 Feature Flag，不实现 runtime flag、配置文件或发布包切换。
- `FrontendV2` 尚不存在，自动守卫当前只验证受控 scope 为空时的明确行为，并在未来目录出现后扫描 V2 文件。
- 后续 Goal 若选择不同 V2 目录，必须先更新 ADR、台账和 `Studio2ArchitectureGuardTests` 的受控 scope。
