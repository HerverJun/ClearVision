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

每轮必须通过本轮门禁后才能进入下一轮，并在该轮停止边界处结束。当前 Prompt 2 完成后不自动实现 Design Lab 或 Canvas。

## 当前进度

- F01：`DONE`。Final SHA `f6d4d98a53914bac088cd62cda261b2c08a11670` 的
  workflow_dispatch Run `29411190713` 已成功；FrontendV2 退役、StudioUI build/Host/Design
  Foundation/canonical FlowCanvas/WebView2/DPI/publish/no-Node 地基已完成。
- F02：产品代码、Browser fixture、四档 DPR 性能、真实 Release WebView2、publish/no-Node 与 Tier 3
  已收口；当前唯一未闭合的产品门禁是用户视觉确认，状态为
  `F02_STATE=AWAITING_PRODUCT_VISUAL_CONFIRMATION`。Final SHA workflow_dispatch 的实际结论由不再修改
  该 SHA 的交付回报记录。
- F03：G1–G6 核心实现已经完成，`F03_G6_STATUS=DONE`；总体状态仍为 `PARTIAL`，唯一历史 evidence gap 是独立无 Node 目标机验证 `NOT_PERFORMED`。产品负责人已接受延期，该项在 F04 中为非阻塞治理项，不改写为 PASS 或 COMPLETE。
- F04：当前唯一执行权威是 [F04 完整开发计划](./Studio_UI_Next_F04_完整开发计划_PROPOSED_AUDITED.md)。G0/G1/G2/G3A 已完成；G3B 已实现 durable Project lifecycle operation authority、blank create、revision-aware delete、explicit open、cleanup/recovery 与结构化错误，`G3C_ENTRY=APPROVED`。G3C/G4–G6 尚未完成，必须继续串行。
- `Studio:StudioUiEnabled=false` 保持不变；legacy 仍是默认入口。

## 阶段门禁

- 每阶段必须有明确 scope、owner、共享文件协调人、回滚边界和证据清单。
- 未选中的旧/新 owner 必须真正停止挂载、订阅、timer、SSE、请求和写操作，不能只隐藏 DOM。
- 任何 Project/Flow/Variables 写入都必须回到现有保存链，并使用后端 `PersistenceRevision`。
- 任何 Canvas、WebView2、EventSource、AbortController 或 blob URL 都必须有可验证的 dispose 生命周期。
- 静态浏览器、Playwright、真实 WebView2、DPI、no-Node、现场硬件和 CI 分别报告；缺失证据不能由另一类测试替代。
- 上一阶段未过门禁时，不自动开始下一阶段。

## 文档导航

F04 执行期间以本目录链接的 F04 计划为唯一当前权威；仓库外来源文件或备份只作取证，不同步维护。

- [初始化基线](./初始化基线.md)
- [F01 完整开发计划（正式执行权威）](./Studio_UI_Next_F01_完整开发计划.md)
- [F01 架构决策记录](./F01_架构决策记录.md)
- [F01 五轮执行卡](./F01_五轮执行卡.md)
- [F02 架构决策记录](./F02_架构决策记录.md)
- [F02 API 与权限合同冻结](./F02_API与权限合同冻结.md)
- [F02 Initial 性能与生命周期基线](./F02_Initial性能与生命周期基线.md)
- [F02 Operator 合同同步矩阵](./F02_Operator合同同步矩阵.md)
- [F02 产品视觉证据索引](./F02_视觉证据索引.md)
- [F02 完成报告](./F02_完成报告.md)
- [F03 完整开发计划（历史权威与完成记录）](./Studio_UI_Next_F03_完整开发计划.md)
- [F03 输入与迁移边界（历史输入，已被 F03 完整计划取代）](./F03_输入与迁移边界.md)
- [F04 完整开发计划（当前唯一权威）](./Studio_UI_Next_F04_完整开发计划_PROPOSED_AUDITED.md)
- [F04 G1 产品合同与边界冻结](./F04_G1_产品合同与边界冻结.md)
- [F04 G2 Auth 生命周期闭环](./F04_G2_Auth生命周期闭环.md)
- [ADR F04-G2：401 会话失效与运行重认证协调](./ADR-F04-G2-401会话失效与运行重认证协调.md)
- [F04 G3A Project 生命周期合同决策](./F04_G3A_Project生命周期合同决策.md)
- [ADR F04-G3A：Project 生命周期合同](./ADR-F04-G3A-Project生命周期合同.md)
- [F04 G3B Project 生命周期后端闭环](./F04_G3B_Project生命周期后端闭环.md)
- [仓库级协作规则](../../../AGENTS.md)
- [旧 Studio2 历史入口](../Studio2/README.md)（历史取证，不是新计划）
