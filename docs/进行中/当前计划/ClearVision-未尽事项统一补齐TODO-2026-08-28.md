---
title: "ClearVision 未尽事项统一补齐 TODO"
doc_type: "plan"
status: "active"
topic: "跨计划闭环治理"
created: "2026-08-28"
updated: "2026-08-28"
code_baseline: "1e2342c3909cb1f157d902aef1338e92f1ac44a3"
review_input_revision: "78d693fb4"
---

# ClearVision 未尽事项统一补齐 TODO

> 本文是七组保留文档的唯一横向责任入口，也是 2026-08-28 专业当前性复核后的最终执行计划。专项文档继续保存历史背景和细节，但其中与本文冲突的早期判断、架构假设和验收口径均由本文取代。
>
> Wave 0 代码事实与本地验证绑定 implementation/evidence SHA `1e2342c3909cb1f157d902aef1338e92f1ac44a3`；随后仅以独立文档提交回填该 SHA。只存在于其他分支、stash、历史工作树或旧 CI SHA 的实现不算当前分支闭环。`origin/studio-ui-next@0c44df6c` 只作为 2026-08-25 的缓存分支观察，不作为当前分支事实或新鲜远端证明。

## 1. 状态与证据口径

| 状态 | 含义 |
| --- | --- |
| `IMPLEMENTED_SYNC_PENDING` | 原问题已由当前代码/测试消解，但源文档、证据链接或状态尚未回填；回填前不称 `CLOSED`。 |
| `OPEN_CONFIRMED` | 当前代码仍存在原问题，原问题边界基本有效。 |
| `OPEN_RESCOPED` | 早期描述部分过时或过宽，只执行本文重新定义的剩余边界。 |
| `DECISION_REQUIRED` | 当前实现与旧计划采用了不同产品语义；先作明确产品/架构决定，再实现或关闭。 |
| `BLOCKED_EXTERNAL` | 只能由授权设备、交付模型、现场数据、人工验收或新鲜远端 CI 关闭。 |
| `SUPERSEDED` | 旧任务已被当前架构决定或现有机制取代，不再按原文实施。 |

关闭证据按风险分层，不再要求每个小项机械绑定同一种远端测试：

1. 单项关闭：实现或可复核否定结论、focused regression、`git diff --check` 和残余边界。
2. Wave 关闭：相关完整本地 Gate 绑定同一 integration SHA，测试进程遵守根 `AGENTS.md` 串行规则。
3. Release 关闭：最终候选 SHA 的必要 GitHub checks、发布包和供应链证据一致。
4. 设备/模型/现场声明：只按目标发布 SKU/profile 取得实验室或现场证据；模拟结果不得冒充现场签核。
5. 文档关闭：代码与测试证据就位后回填源 ID、SHA、命令、结果和残余风险，最后才改为 `CLOSED` 或归档。

## 2. 当前事实与早期规划纠偏

| 主题 | 当前事实 | 最终决定 |
| --- | --- | --- |
| 前端生产路线 | 当前分支保留被 Desktop project/CI 隔离的 `Desktop/FrontendV2` 源码，不存在 `Desktop/StudioUI`；`WorkspaceV2Enabled` 已删除且 `/v2` 固定 404。缓存的 `studio-ui-next` 与当前分支大幅分叉。 | 本次发布以 `wwwroot/index.html + app.js + capability owners` 为唯一 production root；FrontendV2 为 non-production。未来 Vue/StudioUI 迁移必须另立完整 parity/migration epic，不再作为 G16 的“最后开关”。 |
| DeepLearning tensor 复制 | 正式推理从 `DeepLearningOperator.cs:559` 使用 `PreprocessImageLease()`；带 `ToArray()` 的 `PreprocessImage()` 是无调用私有 wrapper。 | 删除“整张 tensor 复制是生产阻断”的旧结论；可删除死 wrapper，但不作为发布门禁。真正残项是证据类型、模型身份、provider 和目标硬件性能。 |
| DeepLearning 精度证据 | `DeepLearning_coco_real_model_baseline` 使用 generated constant smoke model，AP50/Precision/Recall 为 0，却仍可 `Accepted=true` 并被质量聚合计为 Pass。 | 该报告降为机器可判的 inference smoke；模型精度按实际交付模型、manifest、hash、数据集和非零门槛验收。 |
| 数据库权威 | 生产只注册 `VisionDbContext`，启动/旧库修复已集中到 `VisionDatabaseInitializer`/maintenance；`AppDbContext` 无注册、无调用。 | 不重写全部 legacy repair SQL，也不要求任意 down migration；只删除/隔离死上下文并加架构守卫。 |
| 算子人口 | 产品目录为 158；若干 `full155` 工具、registry 和描述仍固定 155。`FrameChangeTrigger` 已有 product-public/package-internal 边界及测试。 | 保留稳定 artifact ID `full155`，人口和 completeness 改为从受治理 catalog 动态计算；不重开 FrameChangeTrigger 旧问题。 |
| SDK | Wave 0 已将 `global.json`、本地 wrapper、文档和 CI 统一为 `9.0.300 + latestPatch`；validator 实际只接受 `9.0.300`–`9.0.399`。 | 固定 9.0.3xx feature band，允许安全 patch，不跨 feature band；portable/SBOM/license 仍由 U04 后续项承接。 |
| Release | tag workflow 直接 publish+zip，只生成简化启动 bat；现场指南却把 GitHub Release 当交付入口。仓库 SPDX 是预制 seed，不是从最终产物生成。 | tag Release 定义为现场可交付 portable package；workflow 复用 portable packaging 或等价流程，并从最终 nupkg/zip 生成、校验和上传供应链产物。 |
| 测试分级 | 已有 `TestClassificationAttribute`、Domain/Purpose/Lane/Evidence/Oracle/Resource、`quality/test-gates.json` 和串行 Gate。 | 删除另建 A/B/C 分类体系；只补 critical-contract baseline、Owner、动态人口防回退和风险修复产生的公共合同测试。 |
| 全量五次 repeat | 没有证据证明所有测试都需重复五次，且全量 repeat 成本和噪声过高。 | 只对 blocking lane、已知 flaky、时序/资源敏感测试做重复运行并登记失败签名；普通全量 Gate 不机械 5 次。 |
| 正式 I/O | 保存后的工业流程必须能访问批准的文件、HTTP、DB、相机、TCP/PLC 等资源。风险来自不可信 Draft Flow 被正式入口提升权限。 | 不全面禁止工业 I/O；按 execution source、run mode、principal capability 和 resource binding 做准入。 |
| 会话 TTL | 当前桌面会话设计为进程期有效，测试已固定该产品策略。 | 不强加时间 TTL；治理全局/每用户容量、撤销事件和最旧 token 淘汰。若改变 TTL，另作产品策略决策。 |
| TCP Server | `TcpDeviceManager` 已通过全局 Profile 实现真实 Server；算子无 Profile 时拒绝 node-local Server 是架构边界。 | `CV-AUDIT-094` 转 `IMPLEMENTED_SYNC_PENDING`；只保留 node-local `Mode=Server` 命名/展示的派生 UX 收口。 |
| 取消外部副作用 | 已发出的 PLC 写、HTTP POST、文件 replace 不可能由客户端断开可靠回滚。 | 验收改为取消后不再 dispatch 新副作用；已 dispatch 操作用 correlation/idempotency 和 `indeterminate` 状态支持对账。 |

## 3. 七组源文档当前结论

| 源文档 | 专业复核后的当前事实 | 本计划承接 |
| --- | --- | --- |
| [全面提升 TODO](./ClearVision-全面提升TODO-2026-05-09.md) | 按 46 个主题：35 个 `IMPLEMENTED_SYNC_PENDING`、10 个 `OPEN_RESCOPED`、P2-2 被本次前端架构决定取代。按 147 个 checkbox：原勾 3、实现待同步 106、实质残项 33、总关闭条件 5。 | U01-U06、U14 |
| [T01 测试与覆盖率治理总体计划](./测试治理/ClearVision_T01_测试与覆盖率治理总体计划_PROPOSED_AUDITED.md) | G01 阶段证据已归档，G01B-R3/G02 仍需当前 SHA；G03-G06 原方案有重复建设，G07 引用了非当前分支架构，G08 仍应 report-only，G09 按 SKU 外部验收。 | U05-U07 |
| [Studio2](../Studio2/README.md) | G00-G15 Goal 卡已完成或历史回填，但 G16 不能通过直接打开不具产品 parity 的 `/v2` 关闭；当前 release root 决定改为 legacy root + capability owners。 | U05 |
| [持续问题排查记录](../待复核/持续问题排查记录-2026-07-06.md) | 102 个源 ID：31 个 `IMPLEMENTED_SYNC_PENDING`、70 个仍开放、`CV-AUDIT-044` 已由 Wave 0 单独关闭；开放 ID 中多项需收窄或合并实现，但每个 ID 保留独立验收。 | U08-U14 |
| [0407 Qwen 排查](../未闭环事项/0407-Qwen排查未闭环.md) | #1-#26 已由当前实现、文件移除或等价契约覆盖，原状态未同步。 | U14 回填 |
| [0418 临时问题记录](../未闭环事项/0418-临时问题记录.md) | 主体实现已落地；工业证据尾项去重并入 U01/U03/U13。 | U01、U03、U13、U14 |
| [深度学习算子问题](../未闭环事项/深度学习算子问题.md) | 灰度/16-bit、NMS、异步加载和工程化契约已改造；旧 tensor-copy 阻断是死代码误判。真实残项是证据声明、模型身份、交付 profile 性能和现场签核。 | U01、U07、U14 |

## 4. 最终统一执行账本

### U01 DeepLearning 证据真实性、模型身份与交付性能

优先级：P0 声明真实性 / P1 交付性能。状态：`OPEN_RESCOPED`。Owner：算法/算子质量。依赖：U03 证据聚合，U07 交付 profile。

- [ ] 将 generated constant model 报告机器可判地标为 `InferenceSmokeOnly`；质量审计不得把 smoke、全 0 precision metrics 或 checksum mismatch 计为精度 Pass。
- [ ] 对每个实际交付模型生成独立 manifest：模型 hash、标签/输入输出契约、数据集版本、非零 AP50/Precision/Recall 门槛、失败边界和批准人；不要求框架用任意“通用真实模型”证明普适精度。
- [ ] 让实际选择的 provider、fallback 原因、模型内容身份进入稳定报告/输出；DeepLearning/SemanticSegmentation cache key 纳入模型内容 hash，并采用 lease-safe replacement/dispose。
- [ ] 仅对目标 SKU 支持的 CPU/CUDA/TensorRT profile 测灰度/16-bit、高候选量、高帧率、多线程和缓存替换，记录 SHA、硬件/驱动、模型/数据 checksum、p50/p95、峰值内存和失败路径。CPU 为基础 profile，GPU provider 不要求普通 CI 单机全覆盖。
非阻断债务：可删除无调用的 `PreprocessImage()` wrapper，但不得把该清理或其 `ToArray()` 重新变成发布门禁。

验收：smoke 与 precision 聚合隔离自测通过；support matrix 中每个 required delivery model/profile 都有通过非零门槛的 evidence pack 和目标硬件可复现报告。optional 模型可独立保持未验收，但不得声明支持。现场签核只在 U07 关闭。

### U02 数据库唯一生产上下文清理

优先级：P2 架构清理。状态：`OPEN_RESCOPED`。Owner：持久化/架构。

- [ ] 删除未使用的 `Persistence/AppDbContext.cs`；若短期不能删除，则明确 `deprecated/non-production`，并增加 architecture guard 禁止注册或生产引用。
- [ ] 保持 `VisionDbContext + VisionDatabaseInitializer + VisionDatabaseMaintenance` 为唯一生产链路；禁止 Station DDL 再散落到 `Program.cs` 或业务服务，不重写已经集中的 legacy repair SQL。
- [ ] 验收 SQLite 实际支持的空库、N-1 migration、完整 legacy adoption、不完整 legacy fail-closed、备份和显式 discard；不要求产品未承诺的任意 down migration。

### U03 交付资产、算子人口与人工证据治理

优先级：P1。状态：`OPEN_RESCOPED`。Owner：算子平台/质量工程。依赖：U01、U04。

- [ ] 不新造“产品场景包导入/发布”功能；在现有 CI、portable package、Vision Agent readiness 和 release review 链路执行 `PublishChecks`，对资产 hash、外部模型 manifest、`parametersNeedingReview` 和零 ROI fail-closed。
- [ ] 将 `OperatorModuleCatalog` 从 `Enum.GetValues` 默认公开改为显式 `package-public/package-internal/legacy-alias/disabled` 分类；新增或未知 enum fail closed。保留 FrameChangeTrigger 已有 product-public/package-internal 边界。
- [ ] Core20 人工结论放 sidecar ledger，记录 reviewer/date/card fingerprint/verdict、算法边界、失败模式、典型 I/O 和不可用场景；生成器不得覆盖人工结论。
- [ ] 保留稳定 artifact ID `full155`，但 suite、registry 和生成器从受治理 catalog 动态取得实际人口（当前 158）；新增算子缺 evidence entry 即失败，并输出 Contract/Golden/Dataset/Field replay delta/trend。

Wave 0 只做了一次既有 Gate 的 generated artifact hygiene：通过 `OperatorKnowledgeGraphRunner` 正式再生成 cards/graph/report，使 `StringFormat` 输出与当前 metadata 对齐为 `Result`、`IsEmpty`、`Length`，并新增两条确定性 PRODUCES edge；158 张卡中仅该算子发生结构变化，catalog fingerprint 保持 `3C7C69D1A08C481E227D2A3BCF11A839324B429BD01E3568E5EC8BB8C2DB4C53`。这不表示 U03 动态人口、catalog 分类或人工证据治理已开始或关闭。

### U04 SDK、现场 Release 与供应链契约

优先级：P1。状态：`OPEN_CONFIRMED`。Owner：构建发布/供应链。

- [x] 将 SDK 策略统一为 `9.0.300 + rollForward: latestPatch`，同步 `global.json`、根 README、项目总览、SDK 指南、`scripts/dotnet.ps1` 和 CI；`scripts/validate-dotnet-sdk-policy.ps1` 自测、实际 resolved SDK 与 10/10 workflow coverage 校验通过。
- [ ] 明确 PR/main 的 raw build artifact 不是现场交付包；tag Release 必须调用 `package-portable-deployment.ps1` 或功能等价的唯一 packaging implementation，统一启动文件、离线依赖和 `README-site-deploy.txt`。
- [ ] 从最终 `.nupkg` 与 portable zip 生成并核验 SPDX/SBOM、THIRD-PARTY-NOTICES 和 dependency report，随同一 GitHub Release 上传；仓库内预制 seed 只作输入，不作最终产物证据。
- [ ] 明确漏洞/许可证 fail-or-approved-exception 策略，发布前处置 `S7NetPlus` 的 `NOASSERTION`。

### U05 当前生产前端、Studio2 G16 与 T01-G07

优先级：P1 Release。状态：`OPEN_RESCOPED`。Owner：Desktop/Studio2。当前阻断：`BLOCKED_RELEASE_EVIDENCE_GAP`；`BLOCKED_OWNER_DISPOSITION_GAP` 已由 Wave 0 解除。

- [x] 执行本次架构决定：`wwwroot/index.html + app.js + capability owners` 是当前 release 唯一 production root；删除 `WorkspaceV2Enabled`、V2 startup injection/root resolver，并以 `/v2` 固定 404 和 architecture guard 防止误切。
- [x] Settings/Inspection/AI 的 disposition 固定为删除不完整实验 owner、adapter、服务端 flag 和客户端双重门禁；legacy `SettingsView`、`InspectionPanel`、`AiPanel` 分别担任唯一 production owner，并验证 lifecycle cleanup。
- [x] 退役 FrontendV2 production build/flag/publish 与 release Gate 路径；源码通过 Desktop project `Content/None Remove` 隔离为 non-production，未来迁移须另立完整 parity/migration epic。
- [ ] T01-G07 改为当前 UI 栈的 capability owner/legacy replacement matrix，覆盖现有 `wwwroot` unit、Playwright 和真实 WebView2；不得执行只存在于 `studio-ui-next` 的路径和命令。
- [ ] G16 仍需 clean clone build/publish、无 Node 目标机离线启动、真实 WebView2、100/125/150/200% OS scale 与 1366x768/1920x1080/2560x1440/3840x2160 批准组合、300/1000 primitive 性能、旧工程/包/Station/Agent/Project save 回归和同 SHA GitHub CI。Node 微基准只作 CPU signal，release 性能必须在最终 publish/current root 的真实 WebView2 记录 input-to-paint、RAF long-frame、p95 和 working set。

验收：唯一 production root 与构建/发布资产一致；实验入口不可能被误切为生产；owner disposition 全部明确；上述 Release evidence 完整。G16 关闭后 Studio2 Goal 卡才整批归档。

### U06 当前 HEAD 测试治理与覆盖率趋势

优先级：P1。状态：`OPEN_RESCOPED`。Owner：测试/CI。依赖：U01、U03、U05、U08-U13 实际修复。

- [ ] G01B-R3 先在当前 HEAD 运行现存 7 个 spec；历史“21 个 fixture”仅作采样，已消失项关闭，当前失败按 failure signature、Owner、产品回归/fixture/环境分类重新建账。
- [ ] G02 取得同一最终 SHA 的 Safe CI、Agent Quality、UI/主 CI 必要 checks；历史 SHA、仓库内旧 artifact 或本地 ahead 状态不能替代。
- [ ] G03 沿用现有 Domain/Purpose/Lane/Evidence/Oracle/Resource 分类，补 critical-contract Owner、批准 baseline/tolerance 和动态人口防回退；不引入 A/B/C 平行体系。
- [ ] G04 的公共合同测试从 U08-U13 每个真实修复的状态与故障矩阵产生；不建立无限扩张的独立“四象限状态机计划”，也不新增未经批准的私有反射 Oracle。
- [ ] G05 只对 blocking lane、已知 flaky 和时序/资源敏感测试做重复运行，输出 machine-readable flake registry、失败签名、p50/p95、retry/skip/expiry；不要求全部套件五次。
- [ ] G06 仅要求 active/release-relevant 报告绑定 SHA、dirty、tool/data checksum 和环境；算法/人口证据实现分别由 U01/U03 承接，现场项由 U07 承接，不重生全部历史报告。
- [ ] G08 在多个绿色 SHA、稳定模块人口和下降/模块缺失/baseline-update 自测齐备前保持 report-only，再由评审决定是否 blocking。

### U07 按发布 SKU 的外部实验室与现场证据

优先级：Release profile。状态：`BLOCKED_EXTERNAL`。Owner：现场验证。

- [ ] 建立 release SKU/support matrix，将 PLC、相机、Station、模型/provider、LLM shadow、WebView2 环境标为 required/optional/unsupported。
- [ ] 每个 profile 只验证其声明支持的真实设备/协议/模型/环境，可独立关闭；未发布或实验能力不阻塞整项目。
- [ ] 记录 Owner、SHA、设备型号/序列号、固件、驱动、模型/数据 checksum、pass/fail、异常恢复和回滚；模拟/virtual 证据只关闭自动化层。

### U08 身份、能力、主体归属与资源 authority

优先级：P0 安全主线（`023/024` 剩余、`025` provenance、`028/036/077/091`）；其余 capability/UI 一致性为 P1。状态：`OPEN_RESCOPED`。Owner：Desktop 安全/权限。

- [ ] 建立资源授权矩阵：复用 preview artifact/AgentRun 已有的 user-owner 模型治理 AI session；Station ingress 使用 Station identity；共享 Project/InspectionResult 使用项目存在性、删除态和角色能力，不虚构“创建者 Owner”，也不重做已闭环的 preview artifact owner 保护。
- [ ] 由 `/auth/me` 或 capability endpoint 返回服务端计算的 capabilities 与密码策略；前端据此 gate Station 生产命令、设置 mutation、AI 模型管理和数据库危险维护，同时保留允许的只读监控/安全摘要。相机操作遵循 `CanOperateHardware`，PLC 写和危险维护遵循 Admin policy。
- [ ] 为 AI session 与仍开放的 WebMessage list/get/delete/generate/planar2d 命令传递 authenticated principal；复用 AgentRun owner 模型，并定义旧 ownerless session 的迁移、隔离或失效策略。
- [ ] 在数据库事务内保证至少一个 active Admin：唯一 active Admin 的删除、禁用或降级一律 409；并发修改最后两个 active Admin 时至多一个成功。安装完成 latch 独立于用户表，恢复只走显式本机 break-glass/CLI。
- [ ] 标定结果具有服务端 solve provenance，并由 calibration draft 提交为 project asset；结果图通过 `ProjectId + ResultId` 或等价反查授权，不再把裸 cache GUID 作为 authority。
- [ ] Station 命令回执以 authenticated Station identity 与持久命令记录的 `StationId` 联合授权；`commandId` 属于其他站点时不得更新状态、进度或审计，并补跨站伪造回归。
- [ ] 本机 autosave draft 按 `userId + projectId` 隔离；logout/用户切换后不得向下一用户展示、恢复或保存前一用户的未提交 flow。共享 Project 不等于共享本机草稿。
- [ ] 回归语义：缺失/无效 user 或 Station credential 返回 401；已认证但 capability 不足返回 403；需防枚举的 owner-scoped opaque resource 在不存在/wrong-owner 时统一 404；最后 Admin/revision 冲突返回 409；输入校验返回 422。跨用户、跨站点、软删除和伪造 ID 不泄露存在性。

已完成但仅作子范围回填：`CV-AUDIT-024` 的 legacy ExecuteOperator/UpdateFlow/Start/StopInspection；`CV-AUDIT-025` 的绝对路径/traversal 防护。`CV-AUDIT-011/014/015/018/034` 的后端 policy 主体已存在，剩余是 capability projection，不作为 P0 blocker。`CV-AUDIT-064` 的目标是 Operator 可读脱敏运行视图、Admin 可读 logs/commands/audit，不是全面禁止 Station telemetry。

### U09 分 authority 的持久化、运行态 apply 与恢复

优先级：P0/P1 数据一致性。状态：`OPEN_RESCOPED`。Owner：持久化/设置/AI。

- [ ] ProjectMutationAuthority 在一次 project access 中加载 authoritative snapshot、应用 patch、计算 candidate diff、决定 runtime mutation lease 并执行 revision CAS；global-variable schema 使用专用 patch，不复制旧 name/description。
- [ ] AppConfigMutationAndApply coordinator 用一把 async gate 覆盖 reload/merge/write；仅文件不存在可初始化默认，损坏/权限/I/O 错误必须保留原 bytes、拒绝 mutation，并从 last-good 恢复或暴露 degraded health。
- [ ] 相机绑定/reset 等运行态变更执行 validate/prepare → persist → apply；apply 失败回滚持久化或进入明确 fenced/degraded 状态。相机换 SerialNumber/删除绑定还必须安全停旧流并 dispose 旧 provider。
- [ ] Station 双配置采用 operation lock、generationId、唯一 temp 与 commit/recovery marker，或收敛单一 authority；故障/重启后不得观察混合 generation，不承诺跨文件断电级绝对原子。
- [ ] AI model、role defaults 与 secret store 采用 candidate-first 可恢复提交，全部持久化成功后才 swap memory；失败返回结构化 5xx。
- [ ] Prompt/flow-version persistence 使用单一串行 mutation authority，锁住 load → merge/increment → candidate persist 全过程；并发 metrics/version/scenario activation 不得丢增量、生成重复版本号或让旧快照覆盖新状态。
- [ ] AI 生成主结果与可选 metrics 分离提交：LLM 已成功时，metrics I/O 失败只记录 degraded health/可重试事件，不得把生成反向判失败；失败路径 metrics 也不得二次覆盖原始错误。
- [ ] 数据库 repair/backup/restore/cleanup 通过同一 maintenance operation gate 串行；恢复期间不得让并发清理/备份命中不确定库，失败保留 safety backup 和明确 recovery state。
- [ ] legacy `/api/ai/agent-plan` 的 workspace mutation 携带 expected revision/clientMutationId 并服从同一 CAS，或删除该 fallback；长请求不得覆盖期间的新 workspace snapshot。
- [ ] Project create 进入 ProjectSaveCoordinator staged commit/recovery；DB project、flow body、metadata 任一步失败都不得留下 API 报失败但列表可见/可读的半创建工程。
- [x] `CV-AUDIT-044` 产品决定与实现已落地：`flow_templates.json` 是权威用户数据；GET pure，未初始化/损坏/空库/不可用统一返回稳定 degraded 503，不修改 active bytes 或生成 backup；built-in 初始化/升级只在显式 startup migration，修复只经 Admin maintenance endpoint。focused regression Product `24/24`、Desktop endpoint `9/9` PASS；该源 ID 已单独关闭，U09 其余 authority 项仍开放。
- [ ] 为各 authority 注入并发、旧 revision、磁盘满、权限、损坏 JSON、进程中断、半写 secret 和 runtime apply 失败，验证响应、原数据保留与重启恢复。

`CV-AUDIT-021` 的非 Admin 主题写入已关闭，只剩 stale read-modify-write 并入 AppConfig authority。AI metrics 不参与主结果成败，但其 auxiliary persistence、mutation 和 recovery 仍由 U09 承接。

### U10 执行来源、资源 capability、状态隔离与取消

优先级：P0 安全/副作用。状态：`OPEN_RESCOPED`。Owner：Runtime/执行安全。

- [ ] 建立 ExecutionAuthorityMatrix，输入至少包含 `ExecutionSnapshot.Source`、`RunMode`、principal capabilities 和 `ResourceBindings`：Operator 只能运行 authoritative StoredProject/RuntimePackage；Engineer/Admin 的 Draft 正式执行必须绑定有效项目、expected revision、显式 capability manifest、审计与确认；Preview/Debug 可读取经批准且有界的输入文件并写隔离 session state，但不得执行外部写入、设备写入或网络副作用。
- [ ] 正式 StoredProject/RuntimePackage 保留声明的真实 MES/PLC/相机/文件/网络 I/O；禁止的是未授权 Draft 借正式入口提升能力。以 operator resource metadata 生成唯一 manifest，通过 broker 解析批准资源：文件须做 canonical full-path、批准根和 Windows reparse point/symlink 检查；HTTP 校验 destination；DB/PLC/TCP 使用 profile；标定输出使用 project calibration asset。
- [ ] 将 `CV-AUDIT-048-053` 作为 Draft capability escalation 回归矩阵；删除“不存在/已删除 project 可绕过”的已修复子复现。HTTP broker 校验 scheme/host/port/CIDR、DNS 与每次 redirect，限制响应大小。
- [ ] 先检查公开文档、外部兼容调用和实际部署，再决定 `/autotune/operator`、`/autotune/flow-node`：确认无消费者后删除；否则 feature-gate，并在 service boundary 限制迭代、deadline、并发 quota、取消和副作用能力。
- [ ] Statistics/Timer/Frame/Trigger 等状态使用 project/session/flow/run/operator 复合作用域；保留 GenerateFlow compare-and-remove 和客户端断开传播回归。
- [ ] 取消后不再启动新副作用；目标协议支持时使用 idempotency key，否则持久记录 dispatch attempt/correlation/outcome；无法确认时标 `indeterminate` 并支持对账，不承诺虚假回滚。
- [ ] 完成 user-facing execution surface matrix：正式/实时/node preview 已接 admission 的事实回填；补 Station package-load、operator preview/single-op 参数 validation。architecture guard 禁止 user-facing/external surface 绕过 admission；受控内部 coordinator 必须携带已准入 snapshot/capability context，而非一刀切禁止 raw engine 内部使用。

### U11 长进程资源、缓存、连接池与保留策略

优先级：P1；replay 阻断正式结果为 P0。状态：`OPEN_RESCOPED`。Owner：Runtime/Station/资源治理。

| ID | 精确剩余动作与验收 |
| --- | --- |
| 057 | 仅 DeepLearning/SemanticSegmentation session 增内容 hash 与 lease-safe replacement；同路径同长度换内容后新租约用新 session，旧租约释放后 dispose。OnnxPatch 已完成的模式不重做。 |
| 058 | 用 bounded striped lock 或引用计数 keyed lock 替换永久 `FileLocks`；10k 唯一路径后回基线/受硬上限，同路径仍串行。 |
| 059 | 失败探测和 Close 后竞态安全回收 per-camera lock；inline CameraId 授权归 U10。 |
| 060 | 只为 abandoned preview session 增 heartbeat/TTL；direct acquire 已有 idle 机制，不重做。用虚拟时钟验证失联回收。 |
| 063 | PLC pool 增容量、idle eviction、断线移除、lease-safe dispose 和可观察计数；目标授权归 U10。 |
| 066 | `_runs` 改为 bounded hot cache，逐出终态时 dispose CTS 但保留持久 replay；stream token 清过期并设硬容量，验证逐出后可恢复。 |
| 067 | 一次 cleanup 一致裁剪持久 store 与 sessions/events/reports 全部内存索引；同进程查询被裁剪 ID 必须不存在。 |
| 068 | ResultOutput 的合法正式输出和 Draft 路径都受 bytes/days/count quota、清理与 health 约束；不得只阻断未授权 Draft 而放任正式流程在临时目录长期单调累积。 |
| 071 | 不改变桌面进程期会话策略；增加每用户/全局容量、最旧 token 淘汰和密码/权限/安全事件撤销。 |
| 081 | 上传图与正式结果图使用独立 namespace/budget；上传压力不得驱逐结果图。读取授权归 U08。 |
| 084 | 绑定删除/换 SerialNumber 安全停止旧流并 close/dispose provider；持久化/apply 事务归 U09。 |
| 086 | P0：replay I/O/磁盘满不得终止 scheduler，正式结果仍发布并记录 `replaySkipped`；P1：按 bytes/days/tracks trim 并暴露 health。 |
| 087 | spool JSONL 不嵌大图；spool/deadletter 各有 records/bytes/days 上限、trim/gap/health，行大小与图像大小解耦。 |
| 090 | command-result spool 独立 records/bytes/days 上限；trim 记录 gap，health 暴露 pending/bytes/oldest/trimmed，覆盖长断网恢复。 |
| 093 | 实现图片 RetentionDays 与 MinFreeSpaceGb 告警/生产启动保护，或删除/禁用字段和承诺；Runtime Preview cleanup 不能替代。 |

### U12 端口恢复、发布洁净度、查询和导出边界

优先级：P1。状态：`OPEN_RESCOPED`。Owner：Desktop/数据查询/发布。

| ID | 精确剩余动作与验收 |
| --- | --- |
| 001 | 抽共享 request recovery helper；首次失败后，只要 discovery base 与失败 base 不同就重试一次，包括错误 saved port→默认 5000；覆盖全部 verb 且无重复 retry。 |
| 003 | 删除三个 `patch_*.ps1`；publish denylist 断言无开发补丁 `.ps1/.bat/.cmd` 资产。 |
| 074 | 合并 report/statistics/distribution/trend 查询预算；DB 聚合或有界流式处理，禁止无界 materialization，并限制查询次数/响应点数；不强制每个聚合都 SQL 下推。 |
| 075 | 并入 074 实现但独立验收 `start<=end`、最大跨度/points、DateTime 极值不溢出，复杂度不随 `records × buckets` 增长。 |
| 076 | Station results/statistics 在 DB 侧 where/order/page/group；设置时间窗和跨度；大数据集下内存/SQL 行数受预算约束。 |
| 078 | command update 进入 replay 或 initial snapshot；断线期间变化后带 Last-Event-ID 重连，最终状态恢复且不重复/倒退。 |
| 088 | 共享 CSV sanitizer 识别前导空白/tab/CRLF 后的 `=,+,-,@`；伪 Excel 改名 CSV 或生成真 xlsx；全部可控字段做恶意样本 pure-helper 测试。 |

### U13 算子能力声明、结果适配与 fail-closed 参数

优先级：P0 脚本/PLC 写保护，其余 P1。状态：`OPEN_RESCOPED`。Owner：算子平台/Runtime。

| ID | 精确剩余动作与验收 |
| --- | --- |
| 092 | 从 metadata/import 移除 `CSharpScript`，不引入 Roslyn；旧 flow 以稳定 unsupported code 失败；非法 CSharpExpression 返回 failure，不能把源码字符串当成功结果。 |
| 095 | 从 public metadata 移除/disabled RTU；旧 RTU flow 稳定 unsupported；alias public/internal 分类归 U03。 |
| 096 | canonical detection adapter 下沉 Core/Application，统一正式、实时、worker 解析 DetectionList/typed/dictionary/JSON；真实 DeepLearning 输出持久化 Defects。 |
| 097 | 复用 096 adapter；以真实 `DeepLearning.Defects -> DualModalVoting` 连线验收，不用手工 dictionary 替身。 |
| 098 | Comparator Condition 在 validator/execute 双层 allowlist；未知值 fail-fast，不得成功返回 false。 |
| 099 | FINS 实现 polling 或移除 metadata；S7/MC PollingCondition 在 admission/direct execute 均拒绝未知值。 |
| 100 | 只修 S7/FINS Operation `Read|Write` 双层 allowlist；矩阵锁定 MC 已有正确行为，非法值绝不发送写帧。 |
| 101 | StopBits/Parity/Encoding/HEX payload 在 validator/execute 双层 fail-fast，不得静默默认或按 UTF8 发送。 |

派生 UX 项：移除/重命名 node-local `Mode=Server`；由 `ProfileId` 对应全局 profile 决定 server/client，UI 不再暗示算子内启动 listener。`CV-AUDIT-094` 本身不重开。`CV-AUDIT-102` 的剩余 execution-surface validation 已移 U10。

### U14 文档同步、关闭与归档门禁

优先级：随项。状态：`OPEN_CONFIRMED`。Owner：文档治理。

- [ ] 每个源 ID 建 ledger：disposition、精确剩余动作、acceptance、evidence SHA、Owner、依赖；合并实现不等于合并验收或丢失 ID。
- [ ] 31 个 `IMPLEMENTED_SYNC_PENDING` 逐项回填实现/测试依据后才标 `CLOSED`；除 Wave 0 已单独关闭的 `CV-AUDIT-044` 外，其余 70 个开放 ID 按本计划实际关闭，不能按治理线整体勾选。
- [ ] 全面提升 TODO 回填 35 个已实现主题（106 checkbox），10 个窄化主题随 U01-U06 关闭，P2-2 标记由前端架构决定取代；5 个总关闭条件最后验收。
- [ ] 0407、0418、深度学习文档继续保留为历史快照；Studio2 仅在 G16 当前 release 验收关闭后整批归档 Goal 卡。
- [ ] U01-U13 与 U14 的逐 ID ledger、源文档回填、关闭核对全部完成后，才关闭 U14、将本文改为 `closed` 并生成归档说明；任一 required release profile 仍外部阻断时不得宣称全项目闭环。

## 5. 持续问题池 102 项覆盖映射

### 5.1 `IMPLEMENTED_SYNC_PENDING`（31）

`CV-AUDIT-002, CV-AUDIT-004, CV-AUDIT-005, CV-AUDIT-007, CV-AUDIT-008, CV-AUDIT-010, CV-AUDIT-013, CV-AUDIT-016, CV-AUDIT-017, CV-AUDIT-019, CV-AUDIT-020, CV-AUDIT-022, CV-AUDIT-026, CV-AUDIT-027, CV-AUDIT-030, CV-AUDIT-031, CV-AUDIT-033, CV-AUDIT-035, CV-AUDIT-037, CV-AUDIT-038, CV-AUDIT-039, CV-AUDIT-043, CV-AUDIT-045, CV-AUDIT-046, CV-AUDIT-047, CV-AUDIT-054, CV-AUDIT-061, CV-AUDIT-062, CV-AUDIT-073, CV-AUDIT-085, CV-AUDIT-094`

这些 ID 只能在源文档补齐当前事实和证据后转 `CLOSED`。其中 `CV-AUDIT-094` 的关闭依据是全局 Profile/TcpDeviceManager 已实现真实 TCP Server；node-local UX 是派生项，不占用该 ID。

### 5.2 仍开放（71）

| 唯一治理线 | ID |
| --- | --- |
| U08 身份/能力/authority | `CV-AUDIT-011, CV-AUDIT-014, CV-AUDIT-015, CV-AUDIT-018, CV-AUDIT-023, CV-AUDIT-024, CV-AUDIT-025, CV-AUDIT-028, CV-AUDIT-034, CV-AUDIT-036, CV-AUDIT-064, CV-AUDIT-077, CV-AUDIT-091` |
| U09 持久化/apply/recovery | `CV-AUDIT-006, CV-AUDIT-009, CV-AUDIT-012, CV-AUDIT-021, CV-AUDIT-029, CV-AUDIT-040, CV-AUDIT-041, CV-AUDIT-042, CV-AUDIT-044, CV-AUDIT-069, CV-AUDIT-070, CV-AUDIT-079, CV-AUDIT-080, CV-AUDIT-082, CV-AUDIT-083, CV-AUDIT-089` |
| U10 execution authority/state | `CV-AUDIT-032, CV-AUDIT-048, CV-AUDIT-049, CV-AUDIT-050, CV-AUDIT-051, CV-AUDIT-052, CV-AUDIT-053, CV-AUDIT-055, CV-AUDIT-056, CV-AUDIT-065, CV-AUDIT-072, CV-AUDIT-102` |
| U11 长进程资源/保留 | `CV-AUDIT-057, CV-AUDIT-058, CV-AUDIT-059, CV-AUDIT-060, CV-AUDIT-063, CV-AUDIT-066, CV-AUDIT-067, CV-AUDIT-068, CV-AUDIT-071, CV-AUDIT-081, CV-AUDIT-084, CV-AUDIT-086, CV-AUDIT-087, CV-AUDIT-090, CV-AUDIT-093` |
| U12 查询/发布/导出 | `CV-AUDIT-001, CV-AUDIT-003, CV-AUDIT-074, CV-AUDIT-075, CV-AUDIT-076, CV-AUDIT-078, CV-AUDIT-088` |
| U13 算子契约 | `CV-AUDIT-092, CV-AUDIT-095, CV-AUDIT-096, CV-AUDIT-097, CV-AUDIT-098, CV-AUDIT-099, CV-AUDIT-100, CV-AUDIT-101` |

部分已关闭子范围不得回退：`021` 非 Admin 主题写入、`024` legacy execution bridge、`025` 路径逃逸、`057` OnnxPatch cache、`071` 原时间 TTL 诉求、`100` Mitsubishi MC Operation、`102` Studio formal/realtime/node preview admission。剩余动作仍按对应源 ID 独立验收。

## 6. 全面提升主题映射

| 状态 | 主题 | 责任入口 |
| --- | --- | --- |
| `OPEN_RESCOPED` | P0-9 | U01 |
| `OPEN_RESCOPED` | P1-7 | U02 |
| `OPEN_RESCOPED` | P1-14、P1-16、P2-6、P2-7 | U03 |
| `OPEN_CONFIRMED` | P1-20、P1-23、P2-8 | U04 |
| `SUPERSEDED` | P2-2 | U05 当前 production-root 架构决定 |
| `OPEN_RESCOPED` | P2-9 | U06 |

其余 35 个主题为 `IMPLEMENTED_SYNC_PENDING`，由 U14 回填，不重新实施。特别是 P1-2 的变量作用域主体和 P1-8 的数据库聚合/分页主体已实现；U10/U12 中的新问题不能反向把整个早期主题重新判为未完成。

## 7. 执行顺序与归档门禁

1. **Wave 0：事实与产品决定** — 本轮已完成 U09 模板 authority、U05 owner disposition、U04 SDK policy 及对应 canonical 文档同步；U04/U05/U09 的后续 release/authority 子项不因此整体关闭。
2. **Wave 1：安全与不可逆副作用** — U08 的 P0 子项、U10 Draft capability escalation、U13 Script/S7/FINS fail-closed，以及 U11 replay fail-soft。
3. **Wave 2：一致性与长进程稳定性** — U02、U09、U11 其余项、U12。
4. **Wave 3：质量、发布和当前 UI 证据** — U01、U03、U04、U05、U06。
5. **Wave 4：目标 SKU 外部验收与归档** — U07、U14。

### 7.1 Wave 0 验证证据（2026-08-28）

- Implementation/evidence SHA：`1e2342c3909cb1f157d902aef1338e92f1ac44a3`（`feat: close wave 0 governance decisions`）。本节所在的后续文档提交只引用该实现 SHA，不把提交自身 SHA 写入 tracked 文件。
- Build：Product test project PASS（1 个既存 `System.Collections.Immutable` 冲突 warning，0 error）；Desktop test project PASS（0 warning，0 error）。
- 模板 pure-read/repair：通过 `scripts/run-dotnet-test-serial.ps1` 串行执行 Product canonical focused，`58/58` PASS；其中 `Sprint7_AiEvolutionTests` `24/24`、knowledge-graph runtime 精确回归 `3/3` PASS。Desktop 六类合并回归 `62/62` PASS，其中 `TemplateEndpointTests` `9/9` PASS。
- StringFormat hygiene：`& './scripts/dotnet.ps1' run --project 'quality/tools/OperatorKnowledgeGraphRunner/OperatorKnowledgeGraphRunner.csproj' --configuration Debug` PASS（158 cards / 1984 edges）；`Artifact_ShouldAlignPortsAndParametersWithOperatorMetadata` 精确回归 `1/1` PASS；加入该项的 Product 扩展集合 `59/59` PASS。生成器先显式执行 startup migration，未手改 JSON，也未勾选 U03。
- UI archived gate：精确 Node test `1/1` PASS；完整 `npm run test:unit` 为 `988/988` PASS。测试现读取 `docs/归档/过期计划/VisionAgent-旧阶段计划/VisionAgent_RuntimePreview_Pilot_Gate.md` 并同时断言归档 README 的 superseded 历史定位；fixed shadow、permission negative、default closed、resource allowlist、offline fallback 等安全契约保留，旧计划未复制回当前计划。
- Playwright：首次实际运行本次 7 个 spec 为 `67 passed / 6 failed`；其中 1 项是 PLC 旧断言错误要求同时写 `/api/plc/settings` 与 `/api/settings`，修正为反向证明第二写 authority 为 0 后精确回归 `1/1` PASS，七 spec 的非视觉最终回归 `68/68` PASS。剩余 5 个 `ai-shell-visual` baseline 在当前 Chromium 环境为字体抗锯齿像素差异（约 1%–4%）；未覆盖治理快照，仍是 G16 release evidence blocker，不能写成 Playwright full PASS。
- SDK：`validate-dotnet-sdk-policy.ps1 -SelfTest` 为 `12/12` PASS；普通 policy validation 解析系统 SDK `9.0.301`；`-ValidateWorkflows` 为 `10 setup-dotnet / 10 validator` PASS；`scripts/dotnet.ps1 --version` 为 `9.0.300`。
- 本地 publish：`scripts/dotnet.ps1 publish ClearVision.Product.Desktop.csproj --configuration Release --no-restore --output ./.tmp/publish-check/wave0-close/` PASS。产物共 198 files / 332,738,267 bytes；`wwwroot/index.html`、`wwwroot/src/app.js` 存在，`wwwroot/v2`、FrontendV2、Node runtime、`node_modules`、package manifest/lockfile 均为 0；检查后已删除该临时目录。这只证明当前工作树 production publish path，不替代 clean clone、最终 release candidate 或真实 no-Node 目标机启动。
- 静态终验：changed JS/MJS `node --check` 3/3、PowerShell AST parse 3/3、JSON parse 4/4、production retired flag/owner/global 与 FrontendV2 build/publish 残余扫描 0、`git diff --check` PASS；未发现仓库/Playwright 关联的 `dotnet`、`testhost`、Node/http-server 或浏览器服务进程。
- 仍未完成：真实 WebView2、DPI/分辨率批准矩阵、no-Node 目标机离线启动、clean clone 全链路、300/1000 primitive 最终 publish 性能、旧工程/包/Station/Agent/Project save 完整回归与同 SHA GitHub CI；继续归入 G16/U04 release evidence。

Wave 可以拆成小提交，但每个源 ID 必须保留独立验收行。只有在 required profiles、最终 Release SHA 和源文档回填全部关闭后，才归档本文及用户指定的七组文档。
