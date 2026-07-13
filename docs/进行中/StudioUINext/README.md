# Studio UI Next

`studio-ui-next` 是 ClearVision Studio 的独立前端重构线。本目录只保存使命、边界、阶段门禁和少量长期上下文，不扩展成旧式 G00—G16 巨型流水账。

## 分支使命

- 在不重造业务权威的前提下，建立可维护、可验证、可逐步切换的新 Studio 前端。
- 允许重新设计 composition root、路由、App Shell、组件、Design System、UI 投影状态、HostBridge 适配和 Canvas 宿主边界。
- 保留 WinForms + WebView2 + ASP.NET Core Desktop 宿主，以及现有 Application、Runtime、Station 和结果权威。

## 与 `codex初稿` 的关系

- `codex初稿` 是稳定维护和回退基线。
- 稳定线的必要修复经审计后单向合入 `studio-ui-next`。
- 新前端未过阶段门禁前，不反向污染稳定线；不用手工复制文件替代 Git 合并。

## 不可越过的权威边界

- Project、Flow、GlobalVariables 和正式 Project assets：现有 Application Service + `ProjectSaveCoordinator`。
- AgentRun、EventStore、终态和恢复：现有 AgentRun 服务与 endpoint。
- Inspection、正式结果、Runtime Package、RuntimeHost 和 Station：现有后端与现场链路。
- `FlowCanvas`、`ImageCanvas`：现有命令式内核，通过窄 adapter 接入；新 UI 不复制内核。
- 前端 store 只保存 UI 投影、草稿和可丢弃缓存，不保存业务权威。

## `FrontendV2` 的废弃定位

`ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/` 仍存在于当前代码，并仍被 Desktop build/publish 生成到 `/v2`；默认配置没有把它作为正式入口。对本分支而言，它是已废弃的迁移原型：

- 可以读取其中的架构教训、权威边界和验收类别；
- 不复制其 Vue 组件、store、port、HostBridge、动态 legacy import、视觉样式或 Goal 路线；
- 不把 `/v2`、`Studio:WorkspaceV2Enabled` 或旧 Studio2 Goal 当作新实现的既定前提。

## 推荐阶段

1. **初始化与基线**：冻结真实仓库现状、规则、风险和未验证事项；只改文档。
2. **技术地基验证**：验证候选工具链、静态资产、Desktop build/publish、HostBridge/HTTP/Canvas 边界和 lifecycle；不迁移业务 capability。
3. **Design System**：建立 Quiet Precision tokens、可访问性、DPI/分辨率和视觉回归基线。
4. **低风险 capability**：先迁移只读、弱状态、易回滚的页面或区域。
5. **状态密集 capability**：迁移 Project、Variables、Results、AI 等强状态区域，逐项证明单 owner、单写入口和恢复语义。
6. **流程工作台**：接入 FlowCanvas、ImageCanvas、属性、预览和几何编辑，保持既有内核与保存权威。
7. **发布切换**：完成真实 WebView2、无 Node、DPI、生命周期、发布包、CI 和回滚证据后，才允许正式切换入口。

## 阶段门禁

- 每阶段必须有明确 scope、owner、共享文件协调人、回滚边界和证据清单。
- 未选中的旧/新 owner 必须真正停止挂载、订阅、timer、SSE、请求和写操作，不能只隐藏 DOM。
- 任何 Project/Flow/Variables 写入都必须回到现有保存链，并使用后端 `PersistenceRevision`。
- 任何 Canvas、WebView2、EventSource、AbortController 或 blob URL 都必须有可验证的 dispose 生命周期。
- 静态浏览器、Playwright、真实 WebView2、DPI、no-Node、现场硬件和 CI 分别报告；缺失证据不能由另一类测试替代。
- 上一阶段未过门禁时，不自动开始下一阶段。

## 文档导航

F01 执行期间以本目录链接的计划为唯一权威；仓库外来源文件或备份只作取证，不同步维护。

- [初始化基线](./初始化基线.md)
- [F01 完整开发计划（正式执行权威）](./Studio_UI_Next_F01_完整开发计划.md)
- [仓库级协作规则](../../../AGENTS.md)
- [旧 Studio2 历史入口](../Studio2/README.md)（历史取证，不是新计划）
