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

## `FrontendV2` 的最终定位

F01 Prompt 1 的最终决定是完整退役 `ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/` 及其 `/v2` build、publish、Host、配置、CI 和专属测试链：

- `FRONTEND_V2_DECISION=DELETE_COMPLETELY`；
- 不修复、不复用、不建立兼容层；
- 只允许在删除前审计其外围耦合；
- legacy `/index.html` 是当前正式入口和回退基线；
- StudioUI 从零建立，不继承其组件、store、router、port、HostBridge、Canvas adapter、样式、测试组织或旧 Goal 路线。

历史 Git 和 `docs/进行中/Studio2/` 可继续取证，但不充当当前执行事实。

## F01 五轮执行

1. **Prompt 1｜退役与构建地基**：完整退役 FrontendV2；完成 runner、DPI、CI 事实取证与 ADR；建立 StudioUI Vue 最小工程和 Desktop build/publish 静态资产链。
2. **Prompt 2｜Host 与最小 Platform**：增加 `/studio` 启动入口、`StudioUiEnabled`、StartupConfigV1、startup reader 和 minimal Host/API platform。
3. **Prompt 3｜Design Foundation**：建立 tokens、representative primitives、Design Foundation Lab、browser fixture 和 central Playwright。
4. **Prompt 4｜Canvas 与 WebView2**：接入 existing FlowCanvas canonical adapter，完成 lifecycle/identity/interaction、runner 泛化、Debug/publish WebView2 和标准化性能 A/B。
5. **Prompt 5｜最终收口**：完成全量回归、publish/no-Node 本机证据、架构守卫、用户视觉确认、GitHub Actions、最终报告和 F02 输入。

每轮必须通过本轮门禁后才能进入下一轮。Prompt 1 完成后必须停止，不自动实现 Host 新入口、Design Lab 或 Canvas。

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
- [F01 架构决策记录](./F01_架构决策记录.md)
- [F01 五轮执行卡](./F01_五轮执行卡.md)
- [仓库级协作规则](../../../AGENTS.md)
- [旧 Studio2 历史入口](../Studio2/README.md)（历史取证，不是新计划）
