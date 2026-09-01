---
title: "ClearVision 未尽事项统一补齐 TODO"
doc_type: "plan"
status: "active"
topic: "跨计划闭环治理"
created: "2026-08-28"
updated: "2026-09-01"
code_baseline: "ec9192ad35932210c5c5d9434f7e1c649dacd92d"
review_input_revision: "78d693fb4"
---

# ClearVision 未尽事项统一补齐 TODO

> 本文是七组保留文档的唯一横向责任入口，也是 2026-08-28 专业当前性复核后的最终执行计划。专项文档继续保存历史背景和细节，但其中与本文冲突的早期判断、架构假设和验收口径均由本文取代。
>
> Wave 0 代码事实与本地验证绑定 implementation/evidence SHA `1e2342c3909cb1f157d902aef1338e92f1ac44a3`；随后仅以独立文档提交回填该 SHA。只存在于其他分支、stash、历史工作树或旧 CI SHA 的实现不算当前分支闭环。`origin/studio-ui-next@0c44df6c` 只作为 2026-08-25 的缓存分支观察，不作为当前分支事实或新鲜远端证明。
>
> Wave 1A 的 U13 实现 SHA 为 `d757efa3bcc0f69d1443c78a5982ff93e45da329`，U13 + replay P0 的 integration/evidence SHA 为 `16c03126774aaa18b6cb9c3105c44b5022f163d6`。`CV-AUDIT-092/099/100/101` 已按该 integration SHA 的 focused acceptance 关闭；`CV-AUDIT-086` 仅关闭“replay 写失败阻断正式结果”的 P0 子范围，P1 retention 仍开放。
>
> Wave 1B1 的 capability/password-policy 实现 SHA 为 `6a476939b143a62a104ebfd4e655979d117f15b2`，active Admin/installation latch 实现 SHA 为 `6c5ca3edacefe56a0ac998a683f5e68f95bdcf08`，本机 draft 隔离及最终 implementation/integration evidence SHA 为 `139e9a062102feab7e6d2a0fdef6085f5b078e34`。`CV-AUDIT-011/014/015/018/028/034/091` 已按该 integration SHA 的 focused acceptance 关闭；U08 仍为 `OPEN_RESCOPED`，其余主体归属、provenance 与 Station identity 项未开始。
>
> Wave 1B2 的 AI session owner 实现 SHA 为 `1cf200d5adb7d50083a046533a7badc72dce2af8`，标定 solve provenance 实现 SHA 为 `ebda3b26dd9fcd9589b5672c6a48f8ba881f0c23`，WebMessage 认证实现 SHA 为 `fd4b26d82df3f0802623a813abe88c0aa69c79fb`，显式 owner API hardening 及最终 implementation/integration evidence SHA 为 `4f0958ed5c03f93ae597d905b619da8e4f9ef74f`。`CV-AUDIT-023/024/025` 已按该 integration SHA 的 owner/authority 负向矩阵关闭；U08 因 `CV-AUDIT-036/064/077` 继续保持 `OPEN_RESCOPED`，U09/U10/G16 未改动。
>
> Wave 1B3 的结果图片 authority 实现 SHA 为 `41121ae647648cc209ad108508b838e0acda23c6`，Station 监控读分层实现 SHA 为 `c62eaabf3986b56d046da821429b7ff616e06f6c`，authenticated Station command result 绑定及最终 implementation/integration evidence SHA 为 `f602a5268284f6499610e6006e42f79ea6c89f65`。`CV-AUDIT-036/064/077` 已按图片软删除/opaque 状态码、Station 精确 JSON 字段/SSE 三路径以及跨站并发零副作用矩阵关闭；U08 改为 `CLOSED`，U09/U10/G16 未改动。
>
> Wave 2A 的 Project mutation authority 实现 SHA 为 `c4e51619ced47572e5530c303ae1935b1c3a6871`，revisioned global-variable patch 实现 SHA 为 `6892d84c69d6087814bb6f05092312519009d963`，staged project create 及最终 implementation/integration evidence SHA 为 `57aef33aa3f11db158ca1858a26ceccb31a092ee`。`CV-AUDIT-006/012/089` 已按 authoritative snapshot/diff/CAS、运行态 lease、专用 schema patch，以及 create fault-injection/restart recovery 矩阵关闭；U09 仍为 `OPEN_RESCOPED`，AppConfig、AI persistence、Station 双配置等其余边界未进入本轮。
>
> Wave 2B 的 AppConfig mutation authority、相机 persist/apply/lifecycle 及最终 implementation/integration evidence SHA 为 `5372fd011694b51a6e31fdeb323030efe67f0b3b`。`CV-AUDIT-009/021/029/042/083/084` 已按 degraded/last-good、revision CAS、candidate replace、persist/apply rollback/fence、reset 同步与 provider retirement 故障矩阵关闭；U09 与 U11 仍为 `OPEN_RESCOPED`，Station 双配置、AI persistence、database maintenance、legacy AI plan CAS 及其它资源治理未进入本轮。
>
> Wave 2C 的 AI model/secret、prompt/flow-version/metrics persistence 与 workspace PlanRun CAS 先行 implementation SHA 为 `7ad57cc2adebbe04dcc735f53d0fdc205ad1cac3`；Station 双配置、database maintenance、AI/PlanRun 补强及最终纯 implementation/integration evidence SHA 为 `431ab324afbe081f50c6738e749b6de9a10436a2`。`CV-AUDIT-040/041/069/070/079/080/082` 已按 generation transaction/recovery、完整串行 RMW、metrics fail-soft health、maintenance rollback/fence、stale/duplicate mutation 与长请求零覆盖矩阵全部关闭；U09 改为 `CLOSED`，U10/U11/G16 未改动。
>
> Wave 2D Execution Authority + Live Resource Safety 的实现/集成证据 SHA 为 `f06c95212fff33b53297c8a3157cd5b736cda3f6`；其运行时 operator metadata governance hygiene 已由独立 implementation/artifact SHA `1dc20977b3a5d166b572f087b2c9c1d4dd104d92` 关闭。该提交明确 version-bump 24 个受影响算子、更新 identity snapshot，并通过正式 OperatorDocGenerator、OperatorKnowledgeGraphRunner 和第二次生成 `0 diff`。历史 services `727/2/729` 保留为 Wave 2D 当时的结果；其中两项失败仅属 U14 生成文档同步，不能表述为 Wave 2D implementation 或产品回归失败。
>
> Wave 2E Engineering Audit Closure 的最终纯 implementation/integration evidence SHA 为 `dd5c066764b3f2e7b7d73fcc0b379e57dae1d857`（`feat: close wave 2e engineering audit findings`），不含两份 canonical 账本。该 SHA 关闭 U11/U12/U13 的余下 18 个工程 ID；正式 OperatorDocGenerator → OperatorKnowledgeGraphRunner 在实现后再次运行，1,061 个生成资产第二次生成 `0 diff`，identity snapshot 仍为 `EF6426A519C056D0240839C469295DC7FC5FAC02BA65F98EEF75E2E976DFBCF2`。显式版本递增为 Comparator `1.0.0→1.0.1`、DualModalVoting `1.0.0→1.0.1`、ModbusCommunication `1.1.0→1.1.1`、ResultOutput `1.0.1→1.0.2`、TextSave `1.0.1→1.0.2`；生成后复跑无额外 metadata 漂移。完整 Chromium 非视觉 lane 已在该 SHA 实跑，结果与遗留失败签名见 7.11，不能表述为 full Playwright PASS。
>
> Wave 3A U02 implementation/integration evidence SHA `27dff8bc923905133a83d2f24d9764fca3cdc38b` 删除未使用的 `Persistence/AppDbContext.cs` 并新增 production-context/Station DDL architecture guard；隔离临时 SQLite 的 initializer/maintenance focused acceptance `48/48` 通过（空库、N-1、完整/不完整 legacy adoption、safety backup、显式 discard）。U02=`CLOSED`；不要求 down migration、真实生产数据库或重写已集中 legacy repair SQL。
>
> Wave 3A U06A fixture/harness integration evidence SHA `ec9192ad35932210c5c5d9434f7e1c649dacd92d`；machine-readable registry 的 post-remediation baseline SHA 为 `2dbd58c084b50236d4796e8afde785e8696e4f5e`。当前 7 个正式 Chromium spec 在该 code/harness SHA 以单 worker 重跑为 `61 passed / 15 failed / 1 skipped`；Flow editor `5/5`、high-frequency `9/9`，ROI `24/24` 连跑三次 `72/72`（55.7/56.2/56.7s，p50 56.2s，p95 56.7s）。AI 四/五/三项与 Flow layout 三项仍分族开放；G08 继续 report-only，未 push 的 G02 继续开放，未更新 visual baseline。
>
> Wave 3A U14 逐项 settlement 已把 31 个旧 `IMPLEMENTED_SYNC_PENDING` 中的 30 项回填为 `CLOSED`。`CV-AUDIT-004` 复核后确认原问题仍存在（placeholder-disabled MQTT 仍进入公共 catalog），故转 `OPEN_RESCOPED`，不将其伪装为证据欠账。102 项当前为 `0 IMPLEMENTED_SYNC_PENDING / 1 engineering open / 101 CLOSED`；U14、U01-U07、G16、release/外部证据和同 SHA CI 不因此关闭。

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
| 数据库权威 | 生产只注册 `VisionDbContext`，启动/旧库修复已集中到 `VisionDatabaseInitializer`/maintenance；未使用的 `AppDbContext` 已删除，architecture guard 禁止其回归或分散 Station DDL。 | U02=`CLOSED`；不重写全部 legacy repair SQL，也不要求任意 down migration。 |
| 算子人口 | 产品目录为 158；若干 `full155` 工具、registry 和描述仍固定 155。`FrameChangeTrigger` 已有 product-public/package-internal 边界及测试。 | 保留稳定 artifact ID `full155`，人口和 completeness 改为从受治理 catalog 动态计算；不重开 FrameChangeTrigger 旧问题。 |
| SDK | Wave 0 已将 `global.json`、本地 wrapper、文档和 CI 统一为 `9.0.300 + latestPatch`；validator 实际只接受 `9.0.300`–`9.0.399`。 | 固定 9.0.3xx feature band，允许安全 patch，不跨 feature band；portable/SBOM/license 仍由 U04 后续项承接。 |
| Release | tag workflow 直接 publish+zip，只生成简化启动 bat；现场指南却把 GitHub Release 当交付入口。仓库 SPDX 是预制 seed，不是从最终产物生成。 | tag Release 定义为现场可交付 portable package；workflow 复用 portable packaging 或等价流程，并从最终 nupkg/zip 生成、校验和上传供应链产物。 |
| 测试分级 | 已有 `TestClassificationAttribute`、Domain/Purpose/Lane/Evidence/Oracle/Resource、`quality/test-gates.json` 和串行 Gate。 | 删除另建 A/B/C 分类体系；只补 critical-contract baseline、Owner、动态人口防回退和风险修复产生的公共合同测试。 |
| 全量五次 repeat | 没有证据证明所有测试都需重复五次，且全量 repeat 成本和噪声过高。 | 只对 blocking lane、已知 flaky、时序/资源敏感测试做重复运行并登记失败签名；普通全量 Gate 不机械 5 次。 |
| 正式 I/O | 保存后的工业流程必须能访问批准的文件、HTTP、DB、相机、TCP/PLC 等资源。风险来自不可信 Draft Flow 被正式入口提升权限。 | 不全面禁止工业 I/O；按 execution source、run mode、principal capability 和 resource binding 做准入。 |
| 会话 TTL | 当前桌面会话设计为进程期有效，测试已固定该产品策略。 | 不强加时间 TTL；治理全局/每用户容量、撤销事件和最旧 token 淘汰。若改变 TTL，另作产品策略决策。 |
| TCP Server | `TcpDeviceManager` 已通过全局 Profile 实现真实 Server；算子无 Profile 时拒绝 node-local Server 是架构边界。 | `CV-AUDIT-094=CLOSED`；只保留 node-local `Mode=Server` 命名/展示的派生 UX 收口。 |
| 取消外部副作用 | 已发出的 PLC 写、HTTP POST、文件 replace 不可能由客户端断开可靠回滚。 | 验收改为取消后不再 dispatch 新副作用；已 dispatch 操作用 correlation/idempotency 和 `indeterminate` 状态支持对账。 |

## 3. 七组源文档当前结论

| 源文档 | 专业复核后的当前事实 | 本计划承接 |
| --- | --- | --- |
| [全面提升 TODO](./ClearVision-全面提升TODO-2026-05-09.md) | 按 46 个主题：35 个既有主题已同步、P1-7 随 U02 关闭，故当前 36 个已实施、9 个 `OPEN_RESCOPED`、P2-2 被 production-root 架构决定取代；147 个 checkbox 中 112 已实施、27 个窄化残项、P2-2 3 项、总关闭条件 5 项。 | U01-U06、U14 |
| [T01 测试与覆盖率治理总体计划](./测试治理/ClearVision_T01_测试与覆盖率治理总体计划_PROPOSED_AUDITED.md) | G01 阶段证据已归档，G01B-R3/G02 仍需当前 SHA；G03-G06 原方案有重复建设，G07 引用了非当前分支架构，G08 仍应 report-only，G09 按 SKU 外部验收。 | U05-U07 |
| [Studio2](../Studio2/README.md) | G00-G15 Goal 卡已完成或历史回填，但 G16 不能通过直接打开不具产品 parity 的 `/v2` 关闭；当前 release root 决定改为 legacy root + capability owners。 | U05 |
| [持续问题排查记录](../待复核/持续问题排查记录-2026-07-06.md) | 102 个源 ID：`0 IMPLEMENTED_SYNC_PENDING / 1 engineering open / 101 CLOSED`。Wave 3A 回填并关闭其中 30 个旧待同步 ID；`CV-AUDIT-004` 因 MQTT placeholder 仍公开转 `OPEN_RESCOPED`。U14、G16 与发布/外部验证仍不因此关闭。 | U01-U07、U14 |
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

优先级：P2 架构清理。状态：`CLOSED`。Owner：持久化/架构。

- [x] 删除未使用的 `Persistence/AppDbContext.cs`；若短期不能删除，则明确 `deprecated/non-production`，并增加 architecture guard 禁止注册或生产引用。
- [x] 保持 `VisionDbContext + VisionDatabaseInitializer + VisionDatabaseMaintenance` 为唯一生产链路；禁止 Station DDL 再散落到 `Program.cs` 或业务服务，不重写已经集中的 legacy repair SQL。
- [x] 验收 SQLite 实际支持的空库、N-1 migration、完整 legacy adoption、不完整 legacy fail-closed、备份和显式 discard；不要求产品未承诺的任意 down migration。

实现/集成证据：`27dff8bc923905133a83d2f24d9764fca3cdc38b`。`VisionDatabaseArchitectureGuardTests` 固定唯一 EF 注册、bootstrap/maintenance 解析链和 Station DDL 集中性；`VisionDatabaseInitializerTests` 与 `VisionDatabaseMaintenanceServiceTests` 的隔离临时 SQLite focused acceptance 共 `48/48` 通过。负向边界：未跑真实生产库、无 down migration 要求，且 legacy repair SQL 仍只允许在 `VisionDatabaseMaintenance` 集中维护。

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

- [x] G01B-R3 先在当前 HEAD 运行现存 7 个 spec；历史“21 个 fixture”仅作采样，已消失项关闭，当前失败按 failure signature、Owner、产品回归/fixture/环境分类重新建账。
- [ ] G02 取得同一最终 SHA 的 Safe CI、Agent Quality、UI/主 CI 必要 checks；历史 SHA、仓库内旧 artifact 或本地 ahead 状态不能替代。
- [ ] G03 沿用现有 Domain/Purpose/Lane/Evidence/Oracle/Resource 分类，补 critical-contract Owner、批准 baseline/tolerance 和动态人口防回退；不引入 A/B/C 平行体系。
- [ ] G04 的公共合同测试从 U08-U13 每个真实修复的状态与故障矩阵产生；不建立无限扩张的独立“四象限状态机计划”，也不新增未经批准的私有反射 Oracle。
- [x] G05 只对 blocking lane、已知 flaky 和时序/资源敏感测试做重复运行，输出 machine-readable flake registry、失败签名、p50/p95、retry/skip/expiry；不要求全部套件五次。
- [ ] G06 仅要求 active/release-relevant 报告绑定 SHA、dirty、tool/data checksum 和环境；算法/人口证据实现分别由 U01/U03 承接，现场项由 U07 承接，不重生全部历史报告。
- [ ] G08 在多个绿色 SHA、稳定模块人口和下降/模块缺失/baseline-update 自测齐备前保持 report-only，再由评审决定是否 blocking。

Wave 3A 证据：`quality/test-gates.json.failureRegistry` 属于现有 gate manifest，不另建并行体系；integration SHA `ec9192ad35932210c5c5d9434f7e1c649dacd92d`。7 个正式 Chromium spec 的 serial recheck 为 `61 passed / 15 failed / 1 skipped`；修复的 Flow/Station/ROI fixture 或 stale assertion 均有 targeted result，ROI 三跑 `72/72`、p50/p95 `56.2/56.7s`。AI 与 Flow layout 失败族、Quiet Precision/visual 差异仍开放；G02 因未 push 不能用本地替代 CI，G08 继续 report-only。

### U07 按发布 SKU 的外部实验室与现场证据

优先级：Release profile。状态：`BLOCKED_EXTERNAL`。Owner：现场验证。

- [ ] 建立 release SKU/support matrix，将 PLC、相机、Station、模型/provider、LLM shadow、WebView2 环境标为 required/optional/unsupported。
- [ ] 每个 profile 只验证其声明支持的真实设备/协议/模型/环境，可独立关闭；未发布或实验能力不阻塞整项目。
- [ ] 记录 Owner、SHA、设备型号/序列号、固件、驱动、模型/数据 checksum、pass/fail、异常恢复和回滚；模拟/virtual 证据只关闭自动化层。

### U08 身份、能力、主体归属与资源 authority

优先级：P0 安全主线（`036` 结果图 authority、`077` Station identity）；`064` Station 敏感读分层为 P1。状态：`CLOSED`。Owner：Desktop 安全/权限。

- [x] 资源授权矩阵已完成 AI session/AgentRun 的统一 owner authority；Station ingress 使用 authenticated connection identity，共享 Project/InspectionResult 按项目存在性、删除态、所属关系和读取能力授权。未虚构“创建者 Owner”，也未重做已闭环的 preview artifact owner 保护。
- [x] `/auth/me` 返回服务端按 endpoint policy 投影的 action capabilities 与有效密码最小长度；Station 生产命令、设置 mutation、AI 模型管理、数据库危险维护及用户管理均 fail-closed gate，并在 handler 入口复核。相机操作遵循 `CanOperateHardware`，PLC 写和危险维护遵循 Admin policy。
- [x] AI session 与 active WebMessage list/get/delete/generate/cancel/planar2d/PickFile 均使用同一默认拒绝 admission 取得 authenticated principal；HTTP/WebMessage 共用 AgentRun 兼容 owner hash。旧 schema 1 或 ownerless 主/last-good store 整体隔离失效，不归给首个登录用户，也不恢复执行。
- [x] 在 SQLite 原子事务内保证至少一个 active Admin：唯一 active Admin 的删除、禁用或降级返回稳定 409；并发修改最后两个 active Admin 时恰有一个成功。安装完成 singleton latch 独立于用户表且由 trigger 保证不可逆，恢复只走默认关闭的显式本机 console break-glass。
- [x] planar2d 与 NPoint 标定 solve 生成 owner/project/kind/TTL/content-hash scoped 服务端 artifact；正式保存只提交 artifact reference、project/revision 与必要 asset context，由服务端重读 artifact bytes、验证 `CalibrationBundleV2` 后写入 project asset。
- [x] 结果图通过 `ProjectId + ResultId` 授权；正式、worker、continuous cache entry 都绑定项目/结果元数据，读取仍回查项目与结果。历史、详情、实时结果使用服务端 `imageReference`，旧裸 GUID 路由在认证/能力检查后恒定 opaque 404。
- [x] Station list/summary/results/health/detail/statistics/SSE 对 Operator/Engineer 返回专用安全 DTO，对 Admin 返回完整 DTO；logs/commands/audit/package 敏感读取使用 Admin policy，SSE initial/live/replay 共用同一 fail-closed 投影，非 Admin UI 不发敏感请求。
- [x] Station 命令回执以 authenticated Station identity 与持久命令记录的 `StationId` 联合授权；Hub、Registry、CentralStore 三层复核，跨站与不存在命令统一 opaque failure，失败不更新任何命令/审计或 SSE，合法/伪造并发只能由真实 owner 改变状态。
- [x] 本机 autosave draft 按 `userId + projectId` 隔离，key 与 versioned payload 双重绑定；logout/用户切换清空内存提示状态但保留退出用户自己的 namespace，legacy ownerless backup 删除且不猜测归属。共享 Project 不等于共享本机草稿。
- [x] U08 状态码/失败矩阵完整：缺认证 401、能力不足 403、图片 wrong project/result/cache metadata/软删除/伪造 ID 与 legacy GUID 均 opaque 404，既有 lease/revision 409 与 validation 422 保留；Station command wrong-station 与不存在 command 使用同一 Hub opaque failure。

Wave 1B1 已独立关闭 `CV-AUDIT-011/014/015/018/028/034/091`；Wave 1B2 已独立关闭 `CV-AUDIT-023/024/025`；Wave 1B3 已独立关闭 `CV-AUDIT-036/064/077`。U08 所有映射 ID 均有负向、并发、软删除与精确字段证据，因此状态为 `CLOSED`；Station Operator/Engineer 的脱敏运行监控继续保留。

### U09 分 authority 的持久化、运行态 apply 与恢复

优先级：P0/P1 数据一致性。状态：`CLOSED`。Owner：持久化/设置/AI。

- [x] ProjectMutationAuthority 在一次 project access 中加载 authoritative project/flow/global variables/assets/revision，按 absent-preserving patch 计算 candidate 与实际 diff，再决定 runtime mutation lease 并执行客户端 revision CAS；metadata-only 不迁移或重写 flow，global-variable schema 使用专用 patch且不复制旧 name/description/flow。该子范围关闭 `CV-AUDIT-006/012`。
- [x] AppConfigMutationAndApply authority 用一把 async gate 覆盖 authoritative reload、revision CAS、absent-preserving patch、validation、candidate replace 与可选 runtime apply；仅文件不存在可初始化默认，损坏/空内容/权限/锁定/I/O 错误保留 active bytes 和 last-good，并通过稳定 degraded/503 契约拒绝 mutation。该子范围关闭 `CV-AUDIT-021/029/042`。
- [x] 相机 binding save/reset 固定 camera operation gate → AppConfig gate，执行 validate/conflict → persist → apply；persist 失败运行态零变化，apply 失败恢复旧 durable snapshot/runtime 或进入 fence。删除/换 SerialNumber 会停止并 dispose 不再引用的 provider，共享 SerialNumber 保留；reset 同步清空 CameraManager 与串口光电触发配置。该子范围关闭 `CV-AUDIT-009/083/084`。
- [x] Station 双配置按规范路径共享单一 process-wide operation gate；每次 mutation 生成 GUID generation、独立 transaction 目录、唯一 temp、previous snapshot 与 `CommitIntended/Committed` marker。candidate hash 校验后才发布，失败同步 rollback；进程中断后新 store 只 roll-forward 完整 intended generation 或 rollback 完整 previous generation，不观察 mixed generation，也不宣称跨文件断电级绝对原子。该子范围关闭 `CV-AUDIT-040`。
- [x] AI model、role defaults、测试状态与 secret store 已进入按文件路径共享的串行 mutation authority；每次在锁内 reload authoritative generation，使用唯一 generation/candidate、DPAPI secret 目录与 previous recovery，durable commit 后才 swap memory，失败返回不含密钥的结构化 503。该子范围关闭 `CV-AUDIT-041/082`。
- [x] Prompt/flow-version/scenario persistence 已使用单一串行 mutation authority 锁住 load → merge/increment/activate → durable candidate → commit；并发 metrics/version/scenario save/activate 不再丢增量、生成重复版本号或让旧 active 快照覆盖新状态。该子范围关闭 `CV-AUDIT-069`。
- [x] AI 生成主结果与可选 metrics 已分离提交：metrics I/O 失败进入 bounded degraded health/retryable event，成功 LLM/flow 结果保持成功，失败路径保留原始异常；后续有效 metrics 写入才清除对应 degraded 状态。该子范围关闭 `CV-AUDIT-070`。
- [x] 数据库 status/repair/backup/restore/cleanup 按规范数据库路径共享 maintenance gate；restore 先生成 safety backup、candidate DB/package 与 hash，再写 recovery marker 并分阶段发布。普通失败恢复精确旧 DB/package，rollback/recovery 中断保留 marker、safety backup 并 fence 当前实例；干净实例启动时继续恢复，恢复期间其它维护操作不能进入不确定库。该子范围关闭 `CV-AUDIT-079`。
- [x] legacy `/api/ai/agent-plan` 与 production frontend fallback 已删除；正式 `/api/ai/agent-plan-runs` 强制 `workspaceExpectedRevision + clientMutationId`，初始与 terminal mutation 使用同一 CAS。runId/receipt 幂等，重复 mutation 只启动一次 planner；长请求期间的新保存会让旧 terminal 409 且零覆盖。该子范围关闭 `CV-AUDIT-080`。
- [x] Project create 进入 ProjectSaveCoordinator 的 `Prepared -> CommitIntended -> Completed` staged commit/recovery；DB aggregate、flow body/metadata、variable state 与 assets 共用 create manifest。pre-intent 丢弃、post-intent 同步回滚，回滚中断则 fenced 并在启动恢复继续 rollback；API 失败后 list/detail/flow 均不可见。该子范围关闭 `CV-AUDIT-089`。
- [x] `CV-AUDIT-044` 产品决定与实现已落地：`flow_templates.json` 是权威用户数据；GET pure，未初始化/损坏/空库/不可用统一返回稳定 degraded 503，不修改 active bytes 或生成 backup；built-in 初始化/升级只在显式 startup migration，修复只经 Admin maintenance endpoint。focused regression Product `24/24`、Desktop endpoint `9/9` PASS；该源 ID 已单独关闭。
- [x] AppConfig/相机 authority 已注入并发、旧 revision、malformed/empty/permission/I/O、candidate replace、persist/apply/rollback、活动 preview/acquisition、reset/save 竞争与重启/no-op reconciliation，验证结构化响应、原数据保留及运行态收敛。
- [x] Station、AI、prompt/flow/scenario、database maintenance 与 PlanRun CAS 均有独立 barrier/fault-injection：覆盖并发、权限/I/O、candidate/replace、pre/post commit 中断、半发布、rollback 二次失败、损坏/缺失 safety backup、重启恢复、stale revision、同 mutation ID 异 payload及重复 terminal；各 authority 不互借矩阵。

`CV-AUDIT-021` 的非 Admin 主题写入边界此前已关闭；Wave 2B 又关闭其剩余 stale read-modify-write/revision 子范围，因此该源 ID 现整体关闭。Wave 2C 已关闭 Station、AI model/secret/prompt/flow-version/metrics、database maintenance 与 PlanRun CAS 子范围；metrics 继续是辅助证据，不参与主结果成败。

Wave 2A 关闭 Project authority/create，Wave 2B 关闭 AppConfig mutation/apply 与相机 runtime/lifecycle，Wave 2C 最终关闭 Station 双配置、AI persistence、database maintenance 与 PlanRun CAS；U09 的全部映射 ID 已有独立故障矩阵与最终 evidence SHA，因此状态为 `CLOSED`。

### U10 执行来源、资源 capability、状态隔离与取消

优先级：P0 安全/副作用。状态：`CLOSED`。Owner：Runtime/执行安全。

- [x] 所有执行入口先构造不可变 admission snapshot，绑定 Source、RunMode、principal、Project/Package revision、flow hash、capability manifest 和 ResourceBindings；Operator 只接受 authoritative StoredProject/RuntimePackage。Draft 必须具备有效 project、expected revision、显式 capability、确认及审计，missing/deleted/stale/forged 一律 fail closed。
- [x] Preview/Debug 只接收有界输入并使用隔离 state；文件、网络、数据库和设备副作用均在 dispatch 前拒绝。正式 StoredProject/RuntimePackage 保留其已声明且经服务端解析的工业 I/O，不以全面禁用副作用换取通过。
- [x] Resource authority 在服务端 canonicalize 并绑定：文件受批准根、traversal/reparse-point/symlink 检查；HTTP 每次 DNS/redirect 校验 scheme/host/port/CIDR 与响应上限；DB、PLC、TCP、串口、相机仅能引用 profile/binding；标定写入 project calibration asset。Admin 身份不接收客户端 raw path、URL、connection string、CameraId 或 PLC/TCP/串口目标。
- [x] `CV-AUDIT-048-053` 的 Draft capability-escalation、路径/reparse、HTTP redirect/DNS、raw device target 及拒绝路径零 filesystem/network/database/device handler 调用均纳入 deterministic fake/barrier 验收。
- [x] 核实后删除无正式消费者的 `/autotune/operator` 与 `/autotune/flow-node`；保留的 scenario/flow-node preview 均经过 capability/admission gate，具有硬 iteration、deadline、per-principal/global concurrency quota、RequestAborted，并服从 Draft 无副作用规则。
- [x] Statistics、Timer、Frame、Trigger state 使用 project/session/flow/run/operator 复合作用域，Draft 不能复用正式 operatorId 污染状态。GenerateFlow 改为 owner+session+request 精确 compare-and-remove，删除 latest/session fallback；错误或缺失 ID 不取消其它任务。
- [x] dispatch journal 保证取消前未 dispatch 的副作用为零；已 dispatch 但结果未知时持久 correlation/outcome=`indeterminate`，不宣称回滚。正式、实时、Station package-load、node preview、single-op preview 与 AutoTune 均在副作用前完成 validation，architecture guard 阻止新外部入口绕过 admission。

Wave 2D 已独立关闭 `CV-AUDIT-032/048/049/050/051/052/053/055/056/065/072/102`；每个 ID 保留独立负向、并发和零 dispatch acceptance，最终 implementation/integration evidence SHA 见 7.10。

### U11 长进程资源、缓存、连接池与保留策略

优先级：P1；replay 阻断正式结果为 P0。状态：`CLOSED`（2026-08-31 Wave 2E）。Owner：Runtime/Station/资源治理。证据 SHA：`dd5c066764b3f2e7b7d73fcc0b379e57dae1d857`。

| ID | 精确剩余动作与验收 |
| --- | --- |
| 057 | [x] DeepLearning/SemanticSegmentation session key 增 canonical path + content hash；同路径同长度替换后新 lease 用新 session，旧 lease 释放后才 dispose。OnnxPatch 已完成子范围只回归。 |
| 058 | [x] 永久 `FileLocks` 已替换为 bounded striped lock；10k 唯一路径后回到基线/硬上限内，同一路径写入仍严格串行。 |
| 059 | [x] CameraId 先经 U10 binding authority；失败探测、Close、重复打开和并发 lease 后均安全回收 per-camera lock，不 double-open、不 dispose 使用中的 camera。 |
| 060 | [x] 仅 abandoned preview 使用 owner/session heartbeat+TTL 与虚拟时钟回收；活跃 preview 不误回收，direct acquisition 既有 idle 机制只回归。 |
| 063 | [x] PLC pool 已有 capacity、idle eviction、断线移除、lease-safe dispose 与可观察计数；目标仅来自 U10 profile，逐出不关闭仍有 lease 的连接。 |
| 066 | [x] `_runs` 为 bounded hot cache，仅逐出终态并 dispose CTS；durable replay 可恢复。stream token 有 TTL、硬容量和 owner binding，过期/逐出后不越权恢复。 |
| 067 | [x] 同一 serialized cleanup 同时裁剪 persistent store 与 session/event/report 内存索引；并发 query/write 不会复活已裁剪 ID。 |
| 068 | [x] 合法正式 ResultOutput 与允许 Draft 输出均经 governed root、records/bytes/days quota、trim 与 health；不再写入客户端可控 raw temp path。 |
| 071 | [x] 保持桌面进程期 session 策略；增加 opportunistic expiry cleanup、每用户/全局硬容量、最旧 token 淘汰，以及密码、权限与安全事件撤销相关 token；不记录 token 原文。 |
| 081 | [x] 上传图与正式 InspectionResult 图片使用独立 namespace/预算，上传压力不能淘汰正式结果图；Project+Result metadata 与 U08 读取授权保持不变，未恢复旧 `/api/images/{guid}` authority。 |
| 084 | [x] Wave 2B：绑定删除/换 SerialNumber 先检查活动 preview/acquisition，persist 后停止 idle stream 并 close/dispose 不再引用的 provider；共享 SerialNumber 不误关，apply/rollback/fence 归同一 AppConfig authority。该源 ID 已关闭；U11 其它资源项仍开放。 |
| 086 | [x] P0 fail-soft 保持；P1 continuous replay 按 tracks/bytes/days trim，暴露 health/gap，replay 写失败仍不阻断正式结果。 |
| 087 | [x] spool/deadletter JSONL 仅保存受限 blob/reference，不嵌 OutputImage；各自 records/bytes/days 上限、trim/gap/health，重启恢复无悬空引用。 |
| 090 | [x] command-result spool 独立 records/bytes/days、trim/gap/health，暴露 pending/bytes/oldest/trimmed；断网恢复稳定且不重复提交。 |
| 093 | [x] 图片治理兑现 RetentionDays 和 MinFreeSpaceGb：仅 ownership manifest 批准目录可清理，低空间 health warning，生产启动前稳定 fail closed。 |

Wave 2D 已关闭 `CV-AUDIT-057/058/059/060/063/071/081`；Wave 2E 已以 fake clock、故障注入和隔离目录关闭 `066/067/068/086/087/090/093`，U11=`CLOSED`。

### U12 端口恢复、发布洁净度、查询和导出边界

优先级：P1。状态：`CLOSED`（2026-08-31 Wave 2E）。Owner：Desktop/数据查询/发布。证据 SHA：`dd5c066764b3f2e7b7d73fcc0b379e57dae1d857`。

| ID | 精确剩余动作与验收 |
| --- | --- |
| 001 | [x] 共享 request recovery 对 GET/POST/PUT/PATCH/DELETE 在证明 connection-refused、且 discovery base 不同后仅重放一次；保留 body/auth/headers/abort，timeout/响应后不作歧义重试。 |
| 003 | [x] 删除三个 `patch_*.ps1`；Release publish hygiene denylist 拒绝 patch `.ps1/.bat/.cmd`、Node/node_modules、开发 manifest。 |
| 074 | [x] report/statistics/distribution/trend 共用 query budget，采用 DB aggregate/有界流，限制 query count、scan rows、time range 和 response points。 |
| 075 | [x] 独立覆盖 `start<=end`、最大跨度/points、DateTime 极值与线性 bucket 算法，不产生 `records × buckets` 扫描。 |
| 076 | [x] Station results/statistics 在 DB 侧 where/order/page/group，限制窗口/跨度；大数据集 SQL 行数与内存受 budget 断言。 |
| 078 | [x] `stationCommandUpdated` 进入 replay/initial snapshot；Last-Event-ID 重连后终态不丢失、不重复、不倒退，保留 U08 安全投影。 |
| 088 | [x] 共享 pure CSV sanitizer 处理中间空白、tab、CR/LF 后的 `=,+,-,@`；Station 伪 Excel 改为 `.csv`，恶意与正常 Unicode 覆盖通过。 |

### U13 算子能力声明、结果适配与 fail-closed 参数

优先级：P0 脚本/PLC 写保护，其余 P1。状态：`CLOSED`（2026-08-31 Wave 2E）。Owner：算子平台/Runtime。证据 SHA：`dd5c066764b3f2e7b7d73fcc0b379e57dae1d857`。

| ID | 精确剩余动作与验收 |
| --- | --- |
| 092 | [x] 从 metadata/import/正式生成资产移除 `CSharpScript`，不引入 Roslyn；旧 flow 以 `SCRIPT_LANGUAGE_UNSUPPORTED` 失败；非法 CSharpExpression 返回稳定 failure，不再把源码字符串当成功结果。 |
| 095 | [x] public metadata/UI/import/生成资产仅公开 Modbus TCP；旧 RTU flow 返回稳定 `MODBUS_RTU_UNSUPPORTED`，connection/device I/O 为 0。 |
| 096 | [x] Core/Application canonical detection adapter 统一正式、实时和 worker 对 DetectionList/typed/dictionary/JSON 的解析；malformed fail closed，真实 DeepLearning Defects 持久化。 |
| 097 | [x] DualModalVoting 复用同一 adapter；真实 `DeepLearning.Defects -> DualModalVoting` 连线验收通过，未用手工 dictionary 替身。 |
| 098 | [x] Comparator `Condition` 在 validator 和 execute 使用同一精确 allowlist；未知值在执行前稳定失败，绝不成功返回 false。 |
| 099 | [x] FINS 实现与 S7/MC 一致的 WaitForValue；三类 PLC 在 validator/direct execute 共用六值精确条件 allowlist，非法值在连接/读取/帧发送前失败。 |
| 100 | [x] S7/FINS Operation 仅接受精确 `Read|Write`，非法值在连接、write-value 解析或帧发送前失败；MC 既有双层保护由回归矩阵锁定。 |
| 101 | [x] StopBits/Parity/Encoding/HEX payload 在 validator/execute 双层 fail-fast；非法枚举或 HEX 不打开串口、不发送 bytes。 |

派生 UX 项：移除/重命名 node-local `Mode=Server`；由 `ProfileId` 对应全局 profile 决定 server/client，UI 不再暗示算子内启动 listener。`CV-AUDIT-094` 本身不重开。`CV-AUDIT-102` 的剩余 execution-surface validation 已移 U10。

### U14 文档同步、关闭与归档门禁

优先级：随项。状态：`OPEN_CONFIRMED`。Owner：文档治理。

- [x] 每个源 ID 建 ledger：disposition、精确剩余动作、acceptance、evidence SHA、Owner、依赖；合并实现不等于合并验收或丢失 ID。
- [x] 31 个 `IMPLEMENTED_SYNC_PENDING` 已逐项回填实现/测试依据；30 项为 `CLOSED`，`CV-AUDIT-004` 因原 MQTT catalog 问题仍存在转 `OPEN_RESCOPED`。Wave 2E 的 18 个工程项仍按逐 ID acceptance 保留，不能被误解为 U14 已关闭。
- [x] Wave 2D metadata governance：独立 hygiene SHA `1dc20977b3a5d166b572f087b2c9c1d4dd104d92` 已审查 identity 的旧/新 hash、24 个受影响 operator、显式版本递增、正式 catalog/cards/version history/knowledge graph 和二次确定性生成；这只关闭该 generated-artifact hygiene，不关闭 U03 catalog governance 或 U14 的其余回填。
- [x] 全面提升 TODO 回填 35 个既有已实现主题（106 个原待同步 checkbox）；U02 另关闭 P1-7，当前为 36 个已实施主题、112 个已实施 checkbox。9 个窄化主题、P2-2 标记和 5 个总关闭条件仍保持原状。
- [ ] 0407、0418、深度学习文档继续保留为历史快照；Studio2 仅在 G16 当前 release 验收关闭后整批归档 Goal 卡。
- [ ] U01-U07 与 U14 的逐 ID ledger、源文档回填、关闭核对全部完成后，才关闭 U14、将本文改为 `closed` 并生成归档说明；任一 required release profile 仍外部阻断时不得宣称全项目闭环。U11/U12/U13 的 engineering closure 不替代该门禁。

## 5. 持续问题池 102 项覆盖映射

### 5.1 `IMPLEMENTED_SYNC_PENDING`（0）

无。Wave 3A 已逐项结算此前 31 项；30 项有可定位实现/测试证据而关闭，不能把剩余 MQTT catalog 缺陷继续称作“仅待文档同步”。

### 5.2 工程开放（1）

`CV-AUDIT-004`：`MqttPublishOperator` 仍为 `placeholder-disabled` 且执行 fail-closed，但当前 `OperatorLifecyclePolicy` 和 `/operators/library` 尚未从公共 catalog 隐藏它。Owner：算子平台/产品契约；后续动作：在保持历史 flow fail-closed 的前提下，明确 public/legacy/disabled catalog policy 并补 API/UI contract test。U03/U13/U14 保持相关，不能因其余 30 项结算而关闭。

部分已关闭子范围不得回退：`057` 的 OnnxPatch cache、`060` direct-acquire idle、`081` U08 读取授权与 `086` replay fail-soft P0；Wave 2E 为 `086` 补齐 P1 retention/health，故其整体也已关闭。

### 5.3 已关闭（101）

| ID | 关闭证据 |
| --- | --- |
| `CV-AUDIT-002/005/007/008/010/013/016/017/019/020/022/026/027/030/031/033/035/037/038/039/043/045/046/047/054/061/062/073/085/094` | Wave 3A 逐 ID settlement 见 canonical [持续问题排查记录](../待复核/持续问题排查记录-2026-07-06.md)：每项保留 implementation SHA、当前 test/static evidence、negative boundary、Owner 与 U 关联；它们是 30 个独立关闭项，不与全面提升主题数相加。 |
| `CV-AUDIT-001` | Wave 2E SHA `dd5c066764b3f2e7b7d73fcc0b379e57dae1d857`；共享 request recovery 对全部 HTTP verb 和 saved-port→5000 只在可证明未送达时重放一次；定向 Chromium 端口恢复通过。 |
| `CV-AUDIT-003` | Wave 2E evidence SHA 同上；三个 patch 脚本已移除，Studio/Station framework-dependent publish 各通过 denylist hygiene（382/230 files），无 Node、node_modules、patch 或开发 manifest。 |
| `CV-AUDIT-066` | Wave 2E evidence SHA 同上；AgentRun terminal-only bounded hot cache、CTS dispose、durable replay、TTL/capacity/owner-bound stream token 的 fake-clock/recovery 矩阵通过。 |
| `CV-AUDIT-067` | Wave 2E evidence SHA 同上；serialized Runtime Preview cleanup 对 durable 与 sessions/events/reports 内存索引一致裁剪，race 不复活已删 ID。 |
| `CV-AUDIT-068` | Wave 2E evidence SHA 同上；ResultOutput governed storage 的 records/bytes/days quota、trim/health 统一覆盖 formal 与允许 Draft。 |
| `CV-AUDIT-074` | Wave 2E evidence SHA 同上；分析 report/statistics/distribution/trend 共用有界 query budget，拒绝超预算查询。 |
| `CV-AUDIT-075` | Wave 2E evidence SHA 同上；trend 时间顺序、最大跨度/points 和 DateTime 极值及非乘积复杂度均有独立 acceptance。 |
| `CV-AUDIT-076` | Wave 2E evidence SHA 同上；Station result/statistics 为 DB-side filter/order/page/group，窗口与读取行数/内存/SQL 次数受预算。 |
| `CV-AUDIT-078` | Wave 2E evidence SHA 同上；command SSE replay/initial snapshot、Last-Event-ID reconnect 和终态单调性通过。 |
| `CV-AUDIT-086` | Wave 2E evidence SHA 同上；保留 P0 fail-soft 并完成 replay tracks/bytes/days trim、gap/health 与恢复矩阵。 |
| `CV-AUDIT-087` | Wave 2E evidence SHA 同上；spool/deadletter blob reference、独立 quota/trim/gap/health 和重启引用一致性通过。 |
| `CV-AUDIT-088` | Wave 2E evidence SHA 同上；纯 CSV sanitizer 统一保护用户/Station 可控字段，伪 Excel 导出为 CSV；恶意 Unicode acceptance 通过。 |
| `CV-AUDIT-090` | Wave 2E evidence SHA 同上；command-result spool quota/trim/gap/health、离线恢复有序且无重复提交。 |
| `CV-AUDIT-093` | Wave 2E evidence SHA 同上；manifest-owned storage cleanup、RetentionDays、MinFreeSpaceGb health warning 与生产启动 fail-closed 通过。 |
| `CV-AUDIT-095` | Wave 2E evidence SHA 同上；RTU 由 public contract 移除，legacy RTU 为 `MODBUS_RTU_UNSUPPORTED` 且零 I/O。 |
| `CV-AUDIT-096` | Wave 2E evidence SHA 同上；canonical detection adapter 解析所有正式形态并将真实 DeepLearning defects 持久化。 |
| `CV-AUDIT-097` | Wave 2E evidence SHA 同上；DualModalVoting 复用 adapter，真实 DeepLearning port connection acceptance 通过。 |
| `CV-AUDIT-098` | Wave 2E evidence SHA 同上；Comparator 双层 allowlist 使未知 Condition 在执行前稳定失败。 |
| `CV-AUDIT-006` | Project authority SHA `c4e51619ced47572e5530c303ae1935b1c3a6871`；Wave 2A integration/evidence SHA `57aef33aa3f11db158ca1858a26ceccb31a092ee`；metadata-only flow bytes/metadata 不变、实际 flow diff 的运行态 lease、no-op revision 与 stale CAS focused acceptance 通过。 |
| `CV-AUDIT-012` | revisioned patch SHA `6892d84c69d6087814bb6f05092312519009d963`；integration/evidence SHA 同上；专用 schema patch、同 revision 竞争最多一成功、404/409/422/200、冲突刷新并保留草稿，以及 metadata/flow 不丢更新矩阵通过。 |
| `CV-AUDIT-089` | staged create implementation/integration/evidence SHA `57aef33aa3f11db158ca1858a26ceccb31a092ee`；DB/flow body+metadata/manifest/commit marker/rollback interruption fault injection、同名并发、API 失败即时与重启后不可见矩阵通过。 |
| `CV-AUDIT-009` | Wave 2B implementation/integration/evidence SHA `5372fd011694b51a6e31fdeb323030efe67f0b3b`；reset 复用相机 operation authority，AppConfig durable commit 后同步 CameraManager/串口触发并清退 provider，no-op 也执行 reconciliation；Desktop focused `96/96`、Desktop endpoints `403/403`。 |
| `CV-AUDIT-021` | Wave 2B implementation/integration/evidence SHA `5372fd011694b51a6e31fdeb323030efe67f0b3b`；剩余 stale RMW 收敛为 `expectedRevision` CAS 与 absent-preserving patch，覆盖 409/no-op/+1 revision、刷新 authoritative revision 并保留 UI 草稿；Product focused `25/25`、UI unit `1020/1020`、定向 Playwright `5/5`。 |
| `CV-AUDIT-029` | Wave 2B implementation/integration/evidence SHA `5372fd011694b51a6e31fdeb323030efe67f0b3b`；AppConfig mutation surface 收敛单一 async authority，production `SaveAsync`、锁外 `LoadAsync→SaveAsync` 与 PLC 二次 `/api/settings` 写入均为 0；Desktop focused `96/96`。 |
| `CV-AUDIT-042` | Wave 2B implementation/integration/evidence SHA `5372fd011694b51a6e31fdeb323030efe67f0b3b`；仅 missing 初始化，malformed/empty/权限/锁定/I/O 保留 active bytes/cache/revision，last-good/unavailable 均结构化 503；Product focused `25/25` 覆盖 candidate/replace failure。 |
| `CV-AUDIT-083` | Wave 2B implementation/integration/evidence SHA `5372fd011694b51a6e31fdeb323030efe67f0b3b`；固定 camera gate → AppConfig gate 与 validate/prepare → persist → apply，覆盖 persist/apply/rollback/fence、活动流冲突、竞争与重启收敛；Desktop focused `96/96`。 |
| `CV-AUDIT-084` | Wave 2B implementation/integration/evidence SHA `5372fd011694b51a6e31fdeb323030efe67f0b3b`；删除/换 SerialNumber 停止 idle stream 并 close/dispose 无引用 provider，共享 serial 保留，活动流冲突零副作用，reset 同路径；Desktop focused `96/96`、Desktop endpoints `403/403`。 |
| `CV-AUDIT-040` | Wave 2C 最终 implementation/integration/evidence SHA `431ab324afbe081f50c6738e749b6de9a10436a2`；Station 双文件共用 canonical-path gate、GUID transaction、candidate hash 与 intent/completed marker；并发、权限/I/O、半发布与 restart roll-forward/rollback 均只得到完整 generation。 |
| `CV-AUDIT-041` | Wave 2C 最终 implementation/integration/evidence SHA 同上；AI model/secret generation 先 candidate、durable commit 后切内存，secret/IO/commit 失败返回 secret-free 503 且旧 generation 保持；最终 Product focused `104/104`、Desktop focused `171/171`。 |
| `CV-AUDIT-069` | Wave 2C 最终 implementation/integration/evidence SHA 同上；prompt、flow version、scenario save/activate 的完整 load→mutate→candidate→commit 由 path-keyed authority 串行，barrier 并发证明 metrics 不丢、version 单调唯一且 active 不回退。 |
| `CV-AUDIT-070` | Wave 2C 最终 implementation/integration/evidence SHA 同上；metrics persistence 失败按 durable order 记录 degraded/retryable health，成功生成不反向失败、失败生成保留原始 LLM 异常；成功/失败及跨实例 health race fault injection 均通过。 |
| `CV-AUDIT-079` | Wave 2C 最终 implementation/integration/evidence SHA 同上；database status/repair/backup/restore/cleanup 共用 canonical-path maintenance gate，safety backup、candidate hash、marker、rollback/fence 与 clean-instance recovery 覆盖全部维护竞态。 |
| `CV-AUDIT-080` | Wave 2C 最终 implementation/integration/evidence SHA 同上；旧 `/api/ai/agent-plan` 与前端 fallback 删除，PlanRun 强制 revision/mutation ID，初始/terminal 共用 CAS且 receipt 绑定完整 planning payload fingerprint；stale、重复及同 mutation ID 异 payload均零覆盖。 |
| `CV-AUDIT-082` | Wave 2C 最终 implementation/integration/evidence SHA 同上；所有 model mutation 在共享 authority 内重读最新 durable generation 后提交，GenerationId 严格 GUID-N/目录 containment，跨 store 并发、candidate/commit 中断与重启只恢复完整旧或完整新 generation。 |
| `CV-AUDIT-044` | Wave 0 implementation/evidence SHA `1e2342c3909cb1f157d902aef1338e92f1ac44a3`。 |
| `CV-AUDIT-011` | capability implementation SHA `6a476939b143a62a104ebfd4e655979d117f15b2`；Wave 1B1 integration/evidence SHA `139e9a062102feab7e6d2a0fdef6085f5b078e34`；Station UI/handler capability gate 与非视觉零 mutation 请求验收通过。 |
| `CV-AUDIT-014` | 同上；设置、PLC、相机 mutation 与实际 Admin/`CanOperateHardware` policy 对齐，缺失 capability 时 fail closed。 |
| `CV-AUDIT-015` | 同上；AI 模型新增、更新、删除、激活、设默认与连接测试均按 action capability gate。 |
| `CV-AUDIT-018` | 同上；database status/backup/repair/restore/cleanup 各自使用 Admin capability，handler 无权限时不发请求。 |
| `CV-AUDIT-023` | AI session owner implementation SHA `1cf200d5adb7d50083a046533a7badc72dce2af8`；owner API hardening 及 Wave 1B2 integration/evidence SHA `4f0958ed5c03f93ae597d905b619da8e4f9ef74f`；create/continue/list/get/delete/workspace/Plan/Build/AgentRun owner 一致性、重启与 ownerless 主/备隔离矩阵通过。 |
| `CV-AUDIT-024` | WebMessage implementation SHA `fd4b26d82df3f0802623a813abe88c0aa69c79fb`；integration/evidence SHA `4f0958ed5c03f93ae597d905b619da8e4f9ef74f`；`app.local` origin、token、policy、默认拒绝、legacy block 与 A/B rebind 异步投递隔离矩阵通过。 |
| `CV-AUDIT-025` | calibration provenance implementation SHA `ebda3b26dd9fcd9589b5672c6a48f8ba881f0c23`；integration/evidence SHA `4f0958ed5c03f93ae597d905b619da8e4f9ef74f`；planar2d/NPoint forged/wrong-owner/project/expired/revision/valid-save 矩阵通过。 |
| `CV-AUDIT-036` | Result image authority implementation SHA `41121ae647648cc209ad108508b838e0acda23c6`；Wave 1B3 integration/evidence SHA `f602a5268284f6499610e6006e42f79ea6c89f65`；Project/Result/cache 三重绑定、正式/continuous、软删除与 401/403/opaque 404/200 矩阵通过。 |
| `CV-AUDIT-064` | Station read tier implementation SHA `c62eaabf3986b56d046da821429b7ff616e06f6c`；integration/evidence SHA `f602a5268284f6499610e6006e42f79ea6c89f65`；Operator/Engineer 安全 DTO、Admin 完整 DTO、敏感 endpoint policy 与 SSE initial/live/replay 精确字段矩阵通过。 |
| `CV-AUDIT-077` | authenticated Station command-result implementation/integration/evidence SHA `f602a5268284f6499610e6006e42f79ea6c89f65`；Hub/Registry/CentralStore 跨站零副作用、opaque failure、合法后续回执与并发 owner-only 更新通过。 |
| `CV-AUDIT-028` | active Admin implementation SHA `6c5ca3edacefe56a0ac998a683f5e68f95bdcf08`；integration/evidence SHA 同上；file-backed SQLite 并发、installation latch、409 error code 与 local-only recovery 验收通过。 |
| `CV-AUDIT-034` | capability implementation SHA 与 integration/evidence SHA 同上；创建、重置、修改密码共用 `/auth/me.passwordPolicy.minimumLength` 投影，服务端仍独立返回 422。 |
| `CV-AUDIT-091` | draft isolation implementation/integration/evidence SHA `139e9a062102feab7e6d2a0fdef6085f5b078e34`；两用户、两工程、logout、legacy/corrupt payload、scoped clear 与浏览器隔离验收通过。 |
| `CV-AUDIT-092` | U13 implementation SHA `d757efa3bcc0f69d1443c78a5982ff93e45da329`；Wave 1A integration/evidence SHA `16c03126774aaa18b6cb9c3105c44b5022f163d6`。 |
| `CV-AUDIT-099` | 同上；三 PLC polling allowlist 与 FINS 匹配/超时/取消 focused acceptance 已通过。 |
| `CV-AUDIT-100` | 同上；S7/FINS 非法 Operation 与 MC 回归均证明零 device I/O。 |
| `CV-AUDIT-101` | 同上；非法 Serial 枚举/HEX 均证明零 open/zero bytes dispatch。 |
| `CV-AUDIT-032` | Wave 2D implementation/integration evidence SHA `f06c95212fff33b53297c8a3157cd5b736cda3f6`；无正式消费者的 `/autotune/operator` 与 `/autotune/flow-node` 已删除，保留 AutoTune 均经 admission/capability、硬 iteration/deadline/concurrency 与 RequestAborted，Draft 无外部副作用。 |
| `CV-AUDIT-048` | Wave 2D evidence SHA 同上；inline/Draft 不能借正式检测读取 raw 本机文件，文件输入仅为 approved-root canonical binding，missing/deleted/stale/forged project fail closed；拒绝路径 filesystem handler 为 0。 |
| `CV-AUDIT-049` | Wave 2D evidence SHA 同上；Draft/preview 的 TextSave/ImageSave 等写盘副作用在 dispatch 前拒绝，合法正式 StoredProject/RuntimePackage 仍可经资源绑定执行，拒绝路径 filesystem handler 为 0。 |
| `CV-AUDIT-050` | Wave 2D evidence SHA 同上；HTTP 仅经服务端 broker 和 profile/binding，逐次校验 scheme/host/port/CIDR、DNS 与 redirect，限制响应大小；raw URL/Draft 失败且 network handler 为 0。 |
| `CV-AUDIT-051` | Wave 2D evidence SHA 同上；标定读取/写入不能接收客户端目录或输出路径，正式写入 project calibration asset，Draft 外部文件副作用 fail closed。 |
| `CV-AUDIT-052` | Wave 2D evidence SHA 同上；DatabaseWrite 只引用服务端 profile/binding，客户端 connection string、DbType、table/raw parameters 不成为 authority；拒绝路径 database handler 为 0。 |
| `CV-AUDIT-053` | Wave 2D evidence SHA 同上；PLC/TCP/serial/camera 目标只能由服务端 profile/binding 解析，Admin 也不能提交 raw device target；fake device 矩阵的拒绝路径 dispatch 为 0。 |
| `CV-AUDIT-055` | Wave 2D evidence SHA 同上；Statistics/Timer state 按 project/session/flow/run/operator 复合作用域隔离，Draft 无法以 formal operatorId 污染正式运行。 |
| `CV-AUDIT-056` | Wave 2D evidence SHA 同上；FrameAveraging/FrameChangeTrigger 状态按完整 execution scope 隔离，Draft 与正式 flow 的同 operatorId 不共享帧窗口或基线。 |
| `CV-AUDIT-065` | Wave 2D evidence SHA 同上；TriggerModule 状态按完整 execution scope 隔离，Draft 复用 operatorId 不再改变正式 trigger count/time。 |
| `CV-AUDIT-072` | Wave 2D evidence SHA 同上；GenerateFlow registry 精确绑定 owner+session+request，并 compare-and-remove；无 ID/错误 ID 不再 latest fallback 取消他人任务，dispatch journal 记录 indeterminate 而不伪称回滚。 |
| `CV-AUDIT-102` | Wave 2D evidence SHA 同上；正式、实时、Station package-load、node preview、single-op preview 与 AutoTune 均在任意副作用前 validation/admission，architecture guard 阻止新增外部入口绕过 governed boundary。 |
| `CV-AUDIT-057` | Wave 2D evidence SHA 同上；DeepLearning/SemanticSegmentation cache key 为 canonical path+content hash，热替换后新 lease 使用新 session，旧 lease 归还后才 dispose；OnnxPatch 仅回归。 |
| `CV-AUDIT-058` | Wave 2D evidence SHA 同上；TextSave 使用 bounded striped lock，10k unique paths 不再单调增长且同路径写入仍串行。 |
| `CV-AUDIT-059` | Wave 2D evidence SHA 同上；CameraId 先经 binding authority，failed detect/Close/reopen/并发 lease 后 keyed lock 可回收，不 double-open、不提前 dispose。 |
| `CV-AUDIT-060` | Wave 2D evidence SHA 同上；owner/session-bound preview heartbeat+TTL 用虚拟时钟回收 abandoned preview，active preview 不误回收；direct-acquire idle 仅回归。 |
| `CV-AUDIT-063` | Wave 2D evidence SHA 同上；PLC pool 有 capacity、idle/断线 eviction、lease-safe dispose 和可观察计数；eviction 不关闭仍被 lease 的 profile connection。 |
| `CV-AUDIT-071` | Wave 2D evidence SHA 同上；桌面进程期 session 增 opportunistic expiry、per-user/global capacity、最旧 token 淘汰及密码/权限/安全事件撤销，不记录 token 原文。 |
| `CV-AUDIT-081` | Wave 2D evidence SHA 同上；upload/result image cache namespace 和预算彼此隔离，upload pressure 不驱逐正式 InspectionResult 图；U08 Project+Result read authority 与 legacy GUID 404 未改变。 |

## 6. 全面提升主题映射

| 状态 | 主题 | 责任入口 |
| --- | --- | --- |
| `OPEN_RESCOPED` | P0-9 | U01 |
| `OPEN_RESCOPED` | P1-14、P1-16、P2-6、P2-7 | U03 |
| `OPEN_CONFIRMED` | P1-20、P1-23、P2-8 | U04 |
| `SUPERSEDED` | P2-2 | U05 当前 production-root 架构决定 |
| `OPEN_RESCOPED` | P2-9 | U06 |
| `CLOSED` | P1-7 | U02；`27dff8bc923905133a83d2f24d9764fca3cdc38b` architecture/SQLite acceptance |

35 个既有已实施主题已由 U14 回填，P1-7 随 U02 关闭；两者都不再是 `IMPLEMENTED_SYNC_PENDING`。特别是 P1-2 的变量作用域主体和 P1-8 的数据库聚合/分页主体已实现；U10/U12 中的新问题不能反向把整个早期主题重新判为未完成。

## 7. 执行顺序与归档门禁

1. **Wave 0：事实与产品决定** — 本轮已完成 U09 模板 authority、U05 owner disposition、U04 SDK policy 及对应 canonical 文档同步；U04/U05/U09 的后续 release/authority 子项不因此整体关闭。
2. **Wave 1：安全与不可逆副作用** — Wave 1A 已完成 `CV-AUDIT-092/099/100/101` 与 `CV-AUDIT-086` replay fail-soft P0；Wave 1B1 已完成 `CV-AUDIT-011/014/015/018/028/034/091`；Wave 1B2 已完成 `CV-AUDIT-023/024/025`；Wave 1B3 已完成 `CV-AUDIT-036/064/077` 并关闭 U08。U10 Draft capability escalation 当时未开始，已由 Wave 2D 完成。
3. **Wave 2：一致性与长进程稳定性** — Wave 2A 已关闭 Project authority/create 的 `CV-AUDIT-006/012/089`；Wave 2B 已关闭 AppConfig/相机一致性的 `CV-AUDIT-009/021/029/042/083/084`；Wave 2C 已关闭全部 U09（Station 双配置、AI persistence、database maintenance 与 workspace CAS），不再将 `040/079` 单列为未关项；Wave 2D 已关闭 U10 全部及 U11 的 `057/058/059/060/063/071/081`；Wave 2E 已关闭余下 U11/U12/U13 的 18 个工程 ID。Wave 3A 再以证据结算 30 项旧待同步 ID；102 项审计池当前仅 `CV-AUDIT-004` engineering open，继续执行的是 U01-U07/U14 的证据、发布与外部验收门禁。
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

### 7.2 Wave 1A 验证证据（2026-08-29）

- U13 implementation SHA：`d757efa3bcc0f69d1443c78a5982ff93e45da329`（`fix: fail closed industrial operator contracts`）。Wave 1A integration/evidence SHA：`16c03126774aaa18b6cb9c3105c44b5022f163d6`（`fix: keep continuous results alive when replay fails`）。本节所在的后续文档提交只引用上述实现 SHA，不把提交自身 SHA 写入 tracked 文件。
- Build：在 integration HEAD 顺序执行 Product test project `dotnet build --no-restore`，PASS，0 error；仅有既存 `System.Collections.Immutable` 8.0/9.0 冲突 warning。
- 最终合并 focused：通过 `scripts/run-dotnet-test-serial.ps1` 在一次 Product invocation 合并 Script、Serial、PLC、metadata/import/admission、knowledge、ContinuousRuntime、InspectionWorker 与 runtime DI 共 12 类，`223/223` PASS。精确非法值/零 dispatch 9 个 method filter 为 `64/64` PASS（Script 9、Serial 21、PLC 34）。
- PLC 与 services Gate：非 virtual `run-tests-plc-regression.ps1` 为 `126/126` PASS；`run-tests-services-regression.ps1` 为 `548/548` PASS。operator contract smoke 7 类为 `88/88` PASS。所有 Product 测试均按根 `AGENTS.md` 串行执行。
- Replay failure injection：纯内存 failing recorder 连续两次抛出带伪造敏感路径的 `IOException`；两个正式结果均写入 result channel，两个 `InspectionResultEvent` 均发布，下一 decision 继续处理。公开结果/SSE 投影均为 `replaySkipped=true`、`replayStatus=skipped_write_failed`、`replayFailureCode=CONTINUOUS_REPLAY_WRITE_FAILED` 与固定公开摘要，未包含路径、异常文本或堆栈。scheduler 坏 subscriber 不阻止其它 subscriber 或下一 item，错误进入日志与 `SubscriberFailureCount`。两条精确验收 `2/2` PASS。
- Script/PLC/Serial acceptance：正式 metadata、import/admission、生成 catalog/cards/knowledge graph 与前端生产投影不再公开 `CSharpScript`；旧 flow 原值不迁移并以稳定 unsupported code 失败。S7/MC/FINS 非法 PollingCondition、S7/FINS 非法 Operation 及 MC 保护回归均为零 device I/O；FINS WaitForValue 覆盖合法六条件、匹配、超时、interval 与取消。Serial 非法 StopBits/Parity/Encoding/HEX 为零 open、零 bytes dispatch。
- 生成资产：受治理 `OperatorDocGenerator` 目标生成 5 个算子；`OperatorKnowledgeGraphRunner` 生成 158 cards / 1984 edges。45 个受治理文件确定性再运行 `DETERMINISM_CHANGED=0`，13 个变更 JSON 全部解析。没有手改生成 JSON，也不表示 U03 开始或关闭。
- 残余扫描：正式生成资产和 production frontend/AI runtime surface 的 `CSharpScript` 命中为 0；生产 C# 仅保留两行旧值识别与 `SCRIPT_LANGUAGE_UNSUPPORTED` 拒绝逻辑。`scripts/OperatorDocGenerator` 旧兼容样例、`tools/sft/clearvision_sft_data.jsonl` 和归档文档仍保留历史样本，不属于正式 runtime/public generated surface。
- NOT RUN / 外部项：`P3CoreContractRunner` 与 `G1P3ContractBatch2Runner` 的受影响 quick-contract runner 因各自 csproj 仍引用已退役命名下的不存在 PLC 通信项目路径而在 runner build 阶段阻断，记为 `NOT RUN`，不推断通过；Desktop metadata/API 专项未运行（本轮无 Desktop source 或 endpoint projection diff）；真实 PLC、串口、现场设备、磁盘填满、replay bytes/days/tracks retention、完整 GitHub CI、G16 visual/release evidence 均未运行或未实施。
- Disposition：`CV-AUDIT-092/099/100/101 = CLOSED`；`CV-AUDIT-086 = OPEN_RESCOPED / P0_SUBRANGE_CLOSED`，P1 bytes/days/tracks trim、后台清理和完整 replay health 预算仍由 U11 承接。

### 7.3 Wave 1B1 验证证据（2026-08-29）

- Initial HEAD：`bc4c9a63538d6fc8f7f3eb2a40fe89e82c8b4f0a`。capability/password-policy implementation SHA：`6a476939b143a62a104ebfd4e655979d117f15b2`（`feat: project authenticated capabilities`）；active Admin/installation latch implementation SHA：`6c5ca3edacefe56a0ac998a683f5e68f95bdcf08`（`fix: preserve active admin authority`）；draft isolation 及最终 implementation/integration evidence SHA：`139e9a062102feab7e6d2a0fdef6085f5b078e34`（`fix: isolate local drafts by user`）。本节所在的后续文档提交只引用实现 SHA，不把提交自身绑定为 `code_baseline`。
- Auth context：`GET /api/auth/me` 的稳定 DTO 为 `userId`、`username`、`role`、排序去重的 `capabilities`、`passwordPolicy.minimumLength`。capability 由服务端 action-to-policy binding 投影；未知/缺失 capability 与 bootstrap 未完成均 fail closed，前端不从 role 或用户名 `admin` 反推 mutation 权限。Station、设置、PLC/相机、AI、数据库及用户管理 handler 在发请求前再次短路，endpoint policy 仍是真实安全边界。
- Admin invariant：SQLite singleton `InstallationStates(Id=1, IsCompleted, CompletedAtUtc, Revision)` 由 migration `20260829114942_PreserveActiveAdminAuthority` 创建；已有非删除用户升级时 backfill completed，`TR_InstallationStates_PreventReopen` 阻止 `true -> false`，legacy no-history repair 同步建表/回填/建 trigger，schema version 为 6。首次 Admin 以 latch CAS + insert 同事务提交；用户 update/delete 使用带“存在另一 active Admin”子查询的条件原子 SQL，稳定返回 `LAST_ACTIVE_ADMIN` 409，setup race 返回 409，校验返回 422，权限不足返回 403，缺失用户返回 404。
- 并发与恢复边界：并发验收使用真实 file-backed SQLite、`Pooling=False`、每任务独立 `VisionDbContext`，以 `TaskCompletionSource` rendezvous 在进入 repository operation 前确定性放行；最后两个 active Admin 并发降级恰有一成功一 `LAST_ACTIVE_ADMIN`，并发 setup-admin 恰有一成功一 installation-completed conflict，最终均保留一个 active Admin。`ClearVision.Product.AdminRecovery` 不注册 HTTP/Kestrel，默认要求进程级 `CLEARVISION_ENABLE_LOCAL_ADMIN_RECOVERY=1`、显式本机绝对数据库路径、拒绝 UNC/network drive、`--confirm RECOVER_LOCAL_ADMIN`，密码由 console 两次输入；只会保持/设置 completed latch，永不重开匿名 setup。
- Draft isolation：唯一 storage contract 使用 `cv_local_draft:v1:<encoded-userId>:<encoded-projectId>` 与 `schema=clearvision.local-project-draft/version=1` payload 双重核对 `userId/projectId/flow/timestamp`。无稳定当前 user 时 read/write/clear 均拒绝；logout/用户切换只清恢复提示与 signature 等内存状态，保留原用户 scoped draft；ownerless `cv_autosave_backup` 删除且不迁移；成功保存只清当前 user + saved project。
- .NET focused：`& './scripts/run-dotnet-test-serial.ps1' -Project 'ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj' -FullyQualifiedName 'AuthenticatedContextProjectionServiceTests','AuthServiceTests','UserAuthorityRepositoryTests' -NoBuild -NoRestore` 为 `33/33` PASS；Desktop 同一 serial runner 合并 `AuthEndpointsTests,UserEndpointsTests,VisionDatabaseInitializerTests` 为 `28/28` PASS。没有为同一 csproj 并发启动 `dotnet test`。
- UI focused：Node 单 invocation、`--test-concurrency=1` 合并 `local-draft-storage/auth-context/app-infrastructure/wave1b1-capability-gates/plc-settings` 为 `45/45` PASS。Playwright 只跑两条非视觉用例，Operator 即使强制触发 Station action 也没有 mutation request，A logout/B 同 project 后无法读取或清除 A 草稿，`2/2` PASS；没有更新 AI visual baseline。
- 完整 UI implementation-SHA 基线：`npm run test:unit` 为 `1004 total / 1003 pass / 1 fail / 0 skipped`。唯一失败是 repository naming guard 命中本文件 Wave 1A evidence 中初始 HEAD 已存在的旧占位命名；本次仅在最终 canonical 文档提交中改写该历史说明。该 implementation SHA 不虚报 full unit PASS。
- 文档工作树复核：仅修改上述两份 canonical 文档后，`node --test --test-concurrency=1 --test-name-pattern='repository naming guard' tests/unit/ai-agent-ui-contract.test.mjs` 为 `1/1` PASS，同一 UI 目录执行 `npm run test:unit` 为 `1004 total / 1004 pass / 0 fail / 0 skipped`。该结果只证明 docs-only 措辞修正后的工作树，不反向改写 implementation SHA 的 `1003/1004` 基线。
- 静态与 build：从 Initial HEAD 到 integration SHA 的 changed JS/MJS `node --check` 为 `18/18` PASS；AdminRecovery `packages.lock.json` parse `1/1`；`dotnet build ClearVision.Product.AdminRecovery.csproj --no-restore --nologo` PASS（0 warning / 0 error）；`git diff --check` PASS。
- 未覆盖与未开始：没有执行 clean clone、真实 WebView2、真实目标机、同 SHA GitHub CI、全量 Playwright 或 G16 release evidence；未进入 Wave 1B2/1B3 的 `CV-AUDIT-023/024/025/036/064/077`，U09/U10 未改动。U08 因这些开放项继续保持 `OPEN_RESCOPED`。
- Disposition：`CV-AUDIT-011/014/015/018/028/034/091 = CLOSED`；`CV-AUDIT-023/024/025/036/064/077` 状态不变，`CV-AUDIT-086` 仍为 `OPEN_RESCOPED / P0_SUBRANGE_CLOSED`。

### 7.4 Wave 1B2 验证证据（2026-08-30）

- Initial HEAD：`511342185ba3af85f10864577e051073d61351d9`。AI session owner implementation SHA：`1cf200d5adb7d50083a046533a7badc72dce2af8`（`fix: scope ai sessions to authenticated owners`）；calibration solve provenance implementation SHA：`ebda3b26dd9fcd9589b5672c6a48f8ba881f0c23`（`fix: bind calibration assets to solve provenance`）；WebMessage 认证 implementation SHA：`fd4b26d82df3f0802623a813abe88c0aa69c79fb`（`feat: authenticate webview message commands`）；显式 owner API hardening 及最终 implementation/integration evidence SHA：`4f0958ed5c03f93ae597d905b619da8e4f9ef74f`（`fix: require explicit ai session owners`）。本节所在的后续 docs-only 提交只引用上述实现 SHA，不把文档提交自身绑定为 `code_baseline`。
- WebMessage admission：唯一 `WebMessageReceived -> DispatchWebMessageAsync -> WebMessageAdmissionService` 链只接受正式 `https://app.local` origin；中央 bridge 自动附带 token、bindingId、navigationEpoch，服务端每次通过 `IAuthService` 解析 user/role 并计算 owner hash。policy 为 PickFile/`planar2d:save`=`CanEditProject`，AI session list/get/delete、Generate/Cancel、`planar2d:solve`、binding change=`RequireAuthenticated`；未知命令默认拒绝，ExecuteOperator/UpdateFlow/Start/StopInspection 固定 `forbidden`。稳定公开码为 `auth-required`、`forbidden`、`not-found`、`conflict`、`validation-error`，token 不进入日志、响应或持久化。
- WebMessage 异步隔离：active Generate/Cancel 同时绑定 owner、request、session、client binding 与 navigation epoch；logout、导航或 B 重新登录会取消/失效 A 的 binding，A 的 progress/result/path 不投递给 B，B 也不能取消 A 的任务。production JS 的 direct `window.chrome.webview.postMessage` 只剩 `webMessageBridge.js` 底层发送点。
- AI owner authority：`AuthenticatedOwnerResolver` 保持 `usr_ + SHA256("agent-run-owner:" + trimmedUserId)` 既有输出；Conversation store schema 2 仅持久化 ownerHash，不保存 token/显示名，也不向前端投影 ownerHash。create/continue/list/get/delete、workspace mutation、Plan/Build 与 AgentRun association 全部显式校验 owner；不存在与 wrong-owner 统一 opaque 404。正式接口已删除所有通用 ownerless overload、legacy trusted owner 常量与自动 fallback，`PrepareContext` 也显式要求 owner；Generate 缺 owner 稳定返回 `auth-required` 且生成服务零调用。只有具名 recovery API 可跨列表读取，并仍保留 session owner association 与 run/session owner 一致性校验。
- Ownerless 策略：旧 schema 1 主存储整体失效；schema 2 中 ownerHash 缺失/非法的 session 不加载。主存储损坏时，ownerless last-good 同样不恢复；数据不迁移给首个登录用户，也不重新进入 recovery/reconciliation 执行。
- Calibration provenance：planar2d 与 NPoint solve 均把 `CalibrationBundleV2` 放入 owner/project/kind/TTL/content-hash scoped `PreviewArtifactStore` artifact。客户端数值仅用于 draft 展示；formal save 只提交 solveArtifactId、projectId、expected revision 与必要 session/node/image/asset context。服务端按当前用户重读 artifact bytes，核对 owner/project/kind/TTL/hash/context，重新解析并要求 accepted bundle，再通过 mutation lease 与 `ProjectService.SaveCalibrationAssetAsync` 保存。wrong-owner/project/expired/forged/context mismatch 统一 404，lease/revision 为 409，bundle/input validation 为 422；无 project context 时仅可展示 draft，formal save 禁用。旧绝对路径/traversal 防护保留，正式 UI 不返回本机保存路径。
- .NET focused：按根 `AGENTS.md` 对同一 csproj 串行且合并类运行。Product 单 invocation 合并 `ConversationalFlowServiceTests,AiFlowGenerationServiceManualRetryTests,GenerateFlowMessageHandlerTests,BuildFromPlanEntryParityTests,AgentRunEventStreamServiceTests`，`187/187` PASS（含公共 ownerless surface/legacy fallback 反射防回退与 Generate 缺 owner 零调用）；Desktop 单 invocation 合并 `WebMessageHandlerTests,PlanarCalibrationWebMessageTests,CalibrationDraftEndpointsTests,PreviewArtifactStoreTests,AgentRunEndpointsTests,ProgramCorsTests,StudioStartupPageResolverTests,WebView2HostTests`，`163/163` PASS。两次均在最终 implementation SHA 使用 `-NoBuild -NoRestore`。
- UI 与静态：完整 `npm run test:unit` 为 48 files、`1008/1008` PASS；changed JS/MJS `node --check` 为 `8/8` PASS。focused calibration Playwright 为 planar2d 2 + NPoint 2=`4/4` PASS。focused AI 三 spec 为 `30 passed / 12 failed / 1 skipped`；12 个失败中 10 个为既有 Plan/readiness/resource-binding 旧契约，另 2 个是标题未含 `visual baseline` 的截图断言，未更新 baseline。
- 完整非视觉 lane：在最终 implementation SHA 以 Chromium 单 worker 执行 `--grep-invert='visual baseline'`，共 188 项，结果为 `164 passed / 23 failed / 1 skipped`，耗时 10.4 分钟；23 个失败由 AI 12、flow-editor-port 1、flow-layout 5、high-frequency settings/station 3、quiet-precision layout audit 2 组成。该结果不称完整 Playwright PASS，也未更新 visual baseline。
- 静态终验：implementation diff 的 `git diff --check` PASS；production direct postMessage、旧 `{ result, fileName }` formal authority、NPoint client CandidateBundle/ExpectedContentHash authority、frontend ownerHash 与缺 owner 的 external session calls 搜索均无回退命中。
- NOT RUN / 外部项：未执行 clean clone、真实 WebView2、真实目标机、真实 LLM/PLC/相机、同 SHA GitHub CI、视觉 baseline 更新或 G16 release evidence；没有进入 Wave 1B3、U09、U10。
- Disposition：`CV-AUDIT-023/024/025 = CLOSED`；`CV-AUDIT-036/064/077` 仍开放，U08 保持 `OPEN_RESCOPED`。102 个源 ID 重算为 31 个 `IMPLEMENTED_SYNC_PENDING`、56 个仍开放、15 个已关闭；`CV-AUDIT-086` 仍为 `OPEN_RESCOPED / P0_SUBRANGE_CLOSED`。

### 7.5 Wave 1B3 验证证据（2026-08-30）

- Initial HEAD：`fa7204f55b68ecd9dd9cad7e864c11323f2c2833`。Result image authority implementation SHA：`41121ae647648cc209ad108508b838e0acda23c6`（`fix: authorize result images through inspection results`）；Station read tier implementation SHA：`c62eaabf3986b56d046da821429b7ff616e06f6c`（`fix: tier station monitoring reads by capability`）；authenticated command result 绑定及最终 implementation/integration evidence SHA：`f602a5268284f6499610e6006e42f79ea6c89f65`（`fix: bind command results to station identity`）。最终实现提交同时修正一个旧 Studio2 Playwright `/auth/me` fixture，使其提供既有必填 `userId` 与服务端 capability；未改变 production 认证流程。本节所在的后续 docs-only 提交只引用实现 SHA，不把文档提交自身绑定为 `code_baseline`。
- Result image authority：读取路径固定为 `GET /api/projects/{projectId}/inspection-results/{resultId}/image`。每次先读取未软删除 Project，再以 `projectId + resultId + !IsDeleted` 读取 InspectionResult，最后核对 cache entry 的 `ProjectId + ResultId` authority；cache 命中不能替代数据库授权。正式 `InspectionService`、后台 `InspectionWorker` 与 continuous worker 都在得到持久结果 ID 后绑定同一元数据。历史、详情、比较、单次响应和 realtime event 均使用服务端 `imageReference`，production 前端不再从 imageId 拼 URL。旧 `GET /api/images/{guid}` 在认证及 `CanReadInspectionResults` 后恒定 404，不读取 cache bytes。
- 图片状态码矩阵：缺认证为 401；有效认证但缺结果读取能力为 403；不存在/软删除 Project，不存在/软删除/wrong-project Result，结果无图，cache miss、ownerless cache、cache project/result mismatch 与伪造路径 ID 均为同形 404；Project、Result、cache metadata 全部匹配时为 200 + 检测到的图片 content type。旧 GUID 路由对应 401/403/404，从不以 GUID 返回 bytes。Project/Result 删除后的拒绝依赖数据库复核，不依赖缓存清理时机。
- Station 字段矩阵：Operator/Engineer 的 status 仅包含 stationId/name/line、online/runtime state、last-seen、粗粒度 outcome/statistics/time；result 仅包含 station/line/sequence、outcome/decision、reason/diagnostic code、execution/start/complete/create time；health 仅包含 station/sequence/runtime/health state/create time。Admin 返回既有完整 DTO。非 Admin JSON 精确断言不存在 machine/client/package/run/flow hash、camera/PLC、spool/resource、PrimaryOutputsPreview、diagnostic/exception detail、logs、commands/payload/correlation、audit/user/IP 字段。summary/statistics 本身只含聚合计数；`/logs`、`/commands`、`/audit` 与 package 敏感读取继续使用 Admin policy。
- Station SSE：initialState、live 与 Last-Event-ID replay 都调用 `StationMonitoringProjection`。非 Admin 只允许 `stationUpserted`、`summaryUpdated`、`stationResultAdded`、`stationHealthUpdated`、heartbeat 的安全 DTO；log、command 与未知事件 fail closed 丢弃。Admin 保留完整 snapshot/event。`/auth/me` 投影 `station.sensitive.read`，UI 只按 capability 隐藏敏感 tab/字段并阻止请求，不从 role 猜测；Operator/Engineer 仍可使用脱敏监控。
- Command result authority：Hub 从当前 SignalR `ConnectionId` 解析 registered StationId，拒绝 DTO StationId 不一致；Registry 再次校验，CentralStore 在 command-result lock 内只按 `CommandId + authenticated StationId` 联合查询。未注册、DTO mismatch、跨站 commandId 与不存在 command 对调用者统一 `Station command result was not accepted.`；在 command 查询前失败或联合查询 miss 时，不改 status/progress/message/error/start/complete/accepted 时间，不写 audit，不更新 registry，也不发布 SSE。合法状态迁移、幂等/终态规则保留；合法 owner 随后可正常上报。并发伪造与合法回执测试证明最终只有真实 Station owner 可改变命令。
- .NET focused：Product image cache/正式/worker/continuous 合并 focused `74/74` PASS；Desktop image endpoint/history/realtime、Station REST/SSE/policy 与 Hub/Registry/CentralStore 合并 focused `122/122` PASS。跨站命令 authority 精确集合 `26/26`，Hub client/spool/replay/lifecycle `23/23`，均按根 `AGENTS.md` 对同一 csproj 串行执行。
- UI：完整 `npm run test:unit` 为 48 files、`1010/1010` PASS。inspection SSE 4 + Station monitor 5 的 targeted Playwright 为 `9/9` PASS；Station 单独 `5/5` PASS。Chromium 单 worker 非视觉 lane 首轮为 `163 passed / 24 failed / 1 skipped`，唯一新增签名是旧 Studio2 auth mock 缺必填 userId 导致重定向竞态；修正 fixture 后精确 `1/1` PASS，最终全 lane 为 `164 passed / 23 failed / 1 skipped`（10.5 分钟），与 Wave 1B2 基线和 12 AI + 1 flow-editor-port + 5 flow-layout + 3 high-frequency + 2 quiet-precision 签名完全一致。Station high-frequency 旧失败仍是测试选择 `Ng`，而 production option 一直为 `ng` 且本轮 diff 未改；与字段分层无关。未更新 visual baseline，也不称 full Playwright PASS。
- 静态与清理：changed JS/MJS `node --check` `6/6` PASS；production 裸 GUID image authority/前端 URL 构造、安全 Station DTO 敏感字段、只按 commandId 更新残余扫描无回退命中；`git diff --check` PASS。Playwright test-results/report 与仓库测试相关残留进程已清理。
- NOT RUN / 范围边界：未 push，未执行 visual baseline 更新、clean clone、真实 WebView2/目标机、真实 LLM/PLC/相机或同 SHA GitHub CI；未进入 U09/U10/G16。
- Disposition：`CV-AUDIT-036/064/077 = CLOSED`，U08=`CLOSED`。102 个源 ID 重算为 31 个 `IMPLEMENTED_SYNC_PENDING`、53 个仍开放、18 个已关闭；`CV-AUDIT-081` 的共享 LRU 写侧可用性问题仍归 U11，`CV-AUDIT-086` 仍为 `OPEN_RESCOPED / P0_SUBRANGE_CLOSED`。

### 7.6 Wave 2A 验证证据（2026-08-30）

- Initial HEAD：`5d88240204e109b6f3b366faef1c50ad642dabc4`。Project mutation authority implementation SHA：`c4e51619ced47572e5530c303ae1935b1c3a6871`（`refactor: centralize project mutation authority`）；revisioned global-variable patch SHA：`6892d84c69d6087814bb6f05092312519009d963`（`fix: require revisioned global variable patches`）；staged project create 及最终 implementation/integration evidence SHA：`57aef33aa3f11db158ca1858a26ceccb31a092ee`（`fix: stage project creation commits`）。本节所在的 docs-only 提交只引用上述实现 SHA，不把文档提交自身绑定为 `code_baseline`。
- Mutation/lock/CAS 契约：锁顺序固定为 project access 后 runtime mutation lease；同一 access 内读取 authoritative project、flow body/metadata、global-variable schema、assets/metadata 与 persistence revision。patch 的 absent 字段保留 authoritative value；candidate 实际 diff 决定 lease，no-op 不增 revision，成功 mutation 只增一次；客户端 stale revision 为 `PSV011/409`，实际 flow/schema mutation 在运行态无法取 lease 时为 `GV031/409`。metadata-only 不执行机会式 flow migration/metadata 补齐/变量名归一化，也不重写 flow bytes 或 metadata。
- Global-variable 契约：请求体固定为 `{ expectedPersistenceRevision, schema }`；服务端专用 schema patch 不复制 name/description/flow。输入错误为 422，missing/deleted project 为无正文 opaque 404；同 revision 的并发 metadata/flow/schema mutation 最多一项成功，其余 409。Legacy UI 保存携带当前 revision，成功回填新 revision；`PSV011` 后刷新 authoritative project/revision、保留 schema 草稿与可重试提示，不显示成功。FrontendV2 generic project PUT 不再重提 schema。
- Create recovery 状态机：所有 create 参与者共用 `Prepared -> CommitIntended -> Completed` manifest。`Prepared` 或 commit-intent marker 前失败直接丢弃 stage；`CommitIntended` 后任一 DB/asset/flow/variable/complete 失败同步验证 fence 并 rollback。rollback 自身中断时保持 `HiddenCreates + RecoveryRequired` fence，启动 recovery 再次验证 DB aggregate flow identity/revision 后继续 rollback；Completed 仅清 journal。API 成功前所有参与者可读，API 失败后 list/detail/flow 均不可见；同名 create 由 process-wide gate 串行，flow/assets delete 同时清理 temp/corrupt/metadata temp。
- .NET focused 与固定回归：Product authority/coordinator/service/concurrency/demo 合并 `88/88` PASS；services regression 为 `582 passed / 1 failed / 583 total`，唯一失败仍是既有 `AuthenticatedContextProjectionServiceTests.Project_ShouldReturnExactCapabilitySetsForEverySupportedRole`（Engineer 比旧期望多 `inspection.results.read`），本轮未修改 capability projection；Phase 4.2 `143/143`、PLC `126/126`。Desktop project/global-variable/create focused `46/46`，完整 Desktop endpoints `388/388`。同一 `.csproj` 均按根 `AGENTS.md` 串行执行。
- UI：Legacy `npm run test:unit` 为 48 files、`1014/1014` PASS；FrontendV2 为 `43/43`，typecheck、build 与 asset validation PASS。targeted Playwright 为 global-variable revision binding + Studio 2 flow/persistence `3/3` PASS。
- 完整非视觉 lane：Chromium 单 worker、`--grep-invert 'visual baseline'` 共 188 项，最终 `164 passed / 23 failed / 1 skipped`，耗时 10.0 分钟；失败签名与既有基线完全一致：AI 12、flow-editor-port 1、flow-layout 5、high-frequency 3、quiet-precision 2。没有新增失败，不称 full Playwright PASS，也未更新 visual baseline。
- 静态与残余扫描：changed JS/MJS `node --check` `5/5` PASS；production `/global-variables` PUT 只有 revisioned store 一处且两个调用点均显式传 revision；schema 保存不存在旧 name/description/flow snapshot payload；production `_projectRepository.AddAsync` 只在 `ProjectSaveCoordinator`。`git diff --check` PASS。
- 清理与边界：本轮 `.tmp/test_results`、Playwright `test-results`/`playwright-report` 已删除，仓库内新增 ProjectSaveTransactions residue 为 0，未发现命令行关联本仓库的 dotnet/testhost/node/Playwright 残留进程。未 push；未进入 AppConfig、AI persistence、U10、visual baseline、clean clone、真实 WebView2/目标机/设备或同 SHA GitHub CI。
- Disposition：`CV-AUDIT-006/012/089 = CLOSED`；102 个源 ID 重算为 31 个 `IMPLEMENTED_SYNC_PENDING`、50 个仍开放、21 个已关闭。U09 因 AppConfig/runtime apply、Station 双配置、AI persistence、database maintenance 与 legacy agent-plan CAS 等其余项继续 `OPEN_RESCOPED`；U08=`CLOSED`，U10 未改动。

### 7.7 Wave 2B 验证证据（2026-08-30）

- Initial HEAD：`172a7f2264c893c8ee8d47fcd5686d6472dc58e6`。AppConfig mutation authority、相机 persist/apply/lifecycle 及最终 implementation/integration evidence SHA：`5372fd011694b51a6e31fdeb323030efe67f0b3b`（`refactor: centralize app config mutation authority`）。本节所在的 docs-only 提交只引用该已存在的纯实现 SHA，不把文档提交自身写入 tracked evidence。
- AppConfig read/degraded 契约：`ReadAsync` 显式返回 `Healthy|Initialized|DegradedLastGood|Unavailable`；只有文件缺失才写入 revision 0 默认值。malformed、empty、access denied、sharing lock 与普通 I/O 分别使用 `APP_CONFIG_MALFORMED|APP_CONFIG_EMPTY|APP_CONFIG_ACCESS_DENIED|APP_CONFIG_LOCKED|APP_CONFIG_IO_ERROR`，保留 active bytes/cache/revision；有 last-good 时 503 返回 `degraded=true`、`hasLastGood=true`、`revision` 与 `lastGood`，无 last-good 时同样 503 且不伪装默认 GET 成功。
- Mutation/CAS/persist 契约：单一 async gate 覆盖 authoritative reload → expected revision → absent-preserving server patch → validation → candidate write/replace/cleanup；missing expected revision=`APP_CONFIG_EXPECTED_REVISION_REQUIRED/422`，stale=`APP_CONFIG_REVISION_CONFLICT/409`，validation=`APP_CONFIG_VALIDATION_FAILED/422`，persist=`APP_CONFIG_PERSIST_FAILED/503`。no-op 不写盘且 revision 不变，成功 mutation 只 `+1`；candidate/replace 失败不污染 active、last-good 或 revision。主设置兼容入口只合并允许 section；PLC、camera、TCP、RuntimePreview Pilot、Station 都使用专用 revisioned mutation，PLC UI 不再二次写 `/api/settings`。409 只刷新 authoritative revision，保留当前 tab 草稿。
- Camera contract：锁顺序固定 camera operation gate → AppConfig mutation gate；保存/reset 在 persist 前完成 normalization/validation、活动 preview/direct acquisition 冲突和 old/new diff。persist 失败时 manager/trigger/preview/provider 零变化；persist 成功后才 release idle stream、`ApplyBindingsAsync` 与 trigger reconfigure。apply 失败先恢复旧 durable AppConfig 并 rollback runtime；任一 durable/runtime rollback 失败进入 `APP_CONFIG_FENCED/503`，不返回保存成功。删除/换 SerialNumber 会停止并 close/dispose 退役 provider，共享 serial 保留；reset 复用同一路径，即使 AppConfig 已是默认 no-op 也重新协调 CameraManager/trigger，重启从 committed config 收敛。
- .NET focused：Product 以 `run-dotnet-test-serial.ps1` 合并 `JsonConfigurationServiceTests,AppConfigThemeNormalizationTests` 且 `-NoBuild -NoRestore`，`25/25` PASS；Desktop 合并 `SettingsThemeEndpointTests,SettingsResetEndpointTests,PlcSettingsEndpointTests,CameraBindingsEndpointTests,StationHardwareSettingsServiceTests,CameraConfigurationCoordinatorTests,CameraManagerLifecycleTests,TcpEndpointsTests,AiModelEndpointsTests`，`96/96` PASS。两项目在本轮已成功 build。
- 固定回归：`run-tests-services-regression.ps1 -NoBuild -NoRestore` 为 `596 passed / 1 failed / 597 total`；唯一失败仍是既有 `AuthenticatedContextProjectionServiceTests.Project_ShouldReturnExactCapabilitySetsForEverySupportedRole`，Engineer 比旧期望多 `inspection.results.read`。相对 Wave 2A 的 `582/1/583` 只是已分类测试人口增加 14，失败签名未增加。`run-tests-phase42-regression.ps1`=`143/143`、`run-tests-plc-regression.ps1`=`126/126`、`run-tests-desktop-endpoints.ps1`=`403/403`，均使用 `-NoBuild -NoRestore` 串行执行。
- UI：受影响 `node --test --test-reporter=spec tests/unit/plc-settings.test.mjs`=`13/13`；最终完整 `npm run test:unit`=48 files、`1020/1020`。Playwright Chromium 单 worker 定向执行 theme 2、PLC 1、high-frequency camera 1，并额外纳入 high-frequency PLC 单写，共 `5/5` PASS；测试 fixture 已迁移到 revisioned response/payload，未更新视觉快照。
- 完整非视觉 lane：`npx playwright test --project=chromium --workers=1 --grep-invert "visual baseline" --reporter=list` 共 188 项，`166 passed / 21 failed / 1 skipped`，耗时 10.3 分钟。相对 Wave 2A `164/23/1` 精确消除 high-frequency camera 与 PLC 两条旧设置失败，无新增签名；剩余既有集合为 AI 12、flow-editor-port 1、flow-layout 5、Station high-frequency 1、Quiet Precision 2，不称 full Playwright PASS。
- 静态/残余/清理：changed JS/MJS `node --check` `10/10`，changed PowerShell/JSON 均为 0 文件；`git diff --check` PASS。production `IConfigurationService.SaveAsync` 调用 0、`LoadAsync→SaveAsync` 组合 0、`UpdateBindings` 调用 0、PLC tab `/api/settings` 写入 0；所有 production config mutation surface 均能追到 expected revision。candidate/recovery/camera fake residue 0；`.tmp/test_results`、Playwright `test-results`/`playwright-report` 已删除，仓库关联 dotnet/testhost/node/Playwright 进程 0。
- 范围与残余原子边界：AI model reset 保持既有独立 store，未宣称与 AppConfig 跨 authority 原子；Station 双配置 generation/transaction、AI model/secret/prompt/metrics persistence、database maintenance gate、legacy AI plan CAS、U10/G16、FrontendV2、visual baseline、clean clone、真实 WebView2/目标机/LLM/PLC/相机和同 SHA GitHub CI 均未进入或未执行。未 push。
- Disposition：`CV-AUDIT-009/021/029/042/083/084 = CLOSED`。102 个源 ID 重算为 31 个 `IMPLEMENTED_SYNC_PENDING`、44 个仍开放、27 个已关闭；U09 因 Station/AI/database/legacy plan 等剩余 authority 保持 `OPEN_RESCOPED`，U11 因 057/058/059/060/063/066/067/068/071/081/086-P1/087/090/093 等资源治理保持 `OPEN_RESCOPED`。U10、G16 与排除 ID 未改动。

### 7.8 Wave 2C AI 阶段验证证据（2026-08-31）

- Code baseline HEAD：`50e18563afc7c87b825fe16c6b91477f150a0faa`；先行 Wave 2B docs-only 同步 SHA：`c9fe781eb33ee98acadff1f5bdc44e3add4930bc`；AI persistence/workspace CAS 最终纯 implementation/integration evidence SHA：`7ad57cc2adebbe04dcc735f53d0fdc205ad1cac3`（`fix: harden AI persistence and workspace CAS`）。本节所在的 docs-only 提交只引用该实现 SHA，不把文档提交自身写入 tracked evidence。
- AI model/secret generation：按 `ai_models.json` 规范路径共享 process-wide mutation gate，每次 mutation 在 gate 内重读最新 durable generation；add/update/delete/activate/planner/shadow default/test status/reset 全部构造独立 generation。DPAPI secrets 先写唯一 candidate 目录，model schema-v2 document 再写唯一 durable candidate，secret generation 就位后才 atomic replace model document，durable commit 后才替换内存；active 与 `.previous` 最多保留两个完整 generation。candidate/commit 中断、active document/secret 损坏与 legacy backup recovery 在重启时只选择完整旧或完整新 generation。失败使用不含路径、key 或异常正文的 `AiConfigPersistenceException`/503，不返回假成功。
- Prompt/flow/scenario authority：`prompt_versions.json` 与 `ai_flow_versions.json` 各自使用 path-keyed gate，完整覆盖 load/recover → create/delete/activate/metrics increment 或 flow version/scenario save/activate → unique durable candidate → atomic commit。并发 metrics 不丢增量，同 flow version number 单调唯一，scenario 同 identity 恰有一个 active；candidate failure 保留旧文件，restart interruption 选择完整旧/新 document。
- Metrics fail-soft：prompt metrics 写失败记录 bounded `AiAuxiliaryPersistenceHealth` active failure 与 retryable event，并由 Admin-only `/api/ai/persistence-health` 投影；不存在版本不会伪清 degraded，后续有效写入才 recovery。`AiFlowGenerationService` 与 legacy `AIWorkflowService` 的成功/失败 metrics 均隔离异常：已完成 LLM/flow 结果不反向失败，原始 LLM 异常不被二次 metrics 异常覆盖。
- PlanRun CAS：确认 production 调用面后删除 `/api/ai/agent-plan` 及 frontend ordinary-POST fallback。`/api/ai/agent-plan-runs` 强制非负 `workspaceExpectedRevision` 与非空 `clientMutationId`；初始 planning 与 completed/failed/cancelled terminal mutation 均 `RequireExpectedRevisionWhenWorkspaceExists=true`。owner/session/mutation 生成确定性 runId，receipt 保存原始 `AppliedRevision`，重复请求幂等且 planner 只启动一次；长请求期间 user save 推进 revision 后，旧 terminal mutation 返回 conflict warning并零覆盖，延迟重复也不会把当前 revision 误当作原 terminal CAS 基线。
- 确定性故障矩阵：barrier 覆盖同 store/跨 store model mutation、并发 metrics、flow version、scenario save/activate 与长 PlanRun/user save；fault injector 覆盖 secret permission、candidate document/commit failure、pre/post commit interruption、active/backup/secret corruption、restart recovery、metrics success/failure I/O、stale/missing revision、duplicate/不同 payload mutation ID、terminal complete/fail/cancel 竞争。公开响应不含 API key；磁盘与内存不出现 mixed generation、旧 snapshot 覆盖或 terminal workspace 覆盖。
- .NET focused：Product 通过 `run-dotnet-test-serial.ps1` 单 invocation 合并 `AiConfigStoreTests,PromptVersionManagerTests,AIGeneratedFlowVersionManagerTests,AiFlowGenerationServiceManualRetryTests`，最终 `79/79` PASS；Desktop 单 invocation 合并 `AgentRunEndpointsTests,AiModelEndpointsTests`，最终 `104/104` PASS。两者后续均使用 `-NoBuild -NoRestore`，同一 `.csproj` 未并发启动测试。
- 固定回归：services `596 passed / 1 failed / 597 total`，唯一失败与 Wave 2B 相同，仍是 `AuthenticatedContextProjectionServiceTests.Project_ShouldReturnExactCapabilitySetsForEverySupportedRole`（Engineer 比旧期望多 `inspection.results.read`）；Phase 4.2 `143/143`、PLC `126/126`、Desktop endpoints `407/407`。相对 Wave 2B，Desktop endpoints 测试人口增加 4 且无新失败。
- UI：AI UI contract `388/388`，最终完整 `npm run test:unit` 为 48 files、`1019/1019` PASS。定向 AI Playwright 5 个 spec、Chromium 单 worker、排除标题含 `visual baseline` 的项目，最终 `47 passed / 12 failed / 1 skipped`；正式 PlanRun shell 与 Build-via-AgentRun 用例通过，12 个失败与 Wave 2B 既有 AI 集合完全一致。
- 完整非视觉 lane：Chromium 单 worker 共 188 项，`166 passed / 21 failed / 1 skipped`，耗时 10.1 分钟；失败精确保持 AI 12、flow-editor-port 1、flow-layout 5、Station high-frequency 1、Quiet Precision 2。没有新增签名，不称 full Playwright PASS，也未更新 visual baseline。
- 静态/残余/清理：changed JS/CJS/MJS `node --check` `3/3`、`git diff --check` PASS；production 旧 `/api/ai/agent-plan` route/fallback、锁外 model/prompt/flow 整文件 RMW、固定共享 AI temp 与无版本 PlanRun workspace mutation 扫描均为 0。仅 non-authoritative unique residue cleanup 允许 best-effort；权威 Save/commit 异常均传播或按 metrics auxiliary 契约记录 degraded。`.tmp/test_results`、Playwright `test-results`/`playwright-report` 已删除；未发现关联本仓库的 dotnet/testhost/node/Chromium/Playwright 残留进程。
- 阶段范围与 disposition：未 push，未执行 visual baseline 更新、clean clone、真实 WebView2/目标机、真实 LLM/PLC/相机或同 SHA GitHub CI；该阶段尚未进入 Station 双配置、database maintenance、U10/U11/G16。`CV-AUDIT-041/069/070/080/082 = CLOSED`；当时 102 个源 ID 为 31 个 `IMPLEMENTED_SYNC_PENDING`、39 个仍开放、32 个已关闭，U09 因 `CV-AUDIT-040/079` 暂时 `OPEN_RESCOPED`。后续最终 disposition 见 7.9。

### 7.9 Wave 2C authority completion 验证证据（2026-08-31）

- 用户给定基线为 `50e18563afc7c87b825fe16c6b91477f150a0faa`；实际接手时工作树已干净地位于 `a9d011a3c381fa8cef428d1d0eb65ee4cba43732`（ahead 31），其中包含 Wave 2B docs-only `c9fe781eb33ee98acadff1f5bdc44e3add4930bc`、先行 AI persistence/workspace CAS 实现 `7ad57cc2adebbe04dcc735f53d0fdc205ad1cac3` 与其阶段文档。未 reset/checkout/stash/覆盖既有工作。Station/database/AI 补强及最终纯 implementation/integration evidence SHA 为 `431ab324afbe081f50c6738e749b6de9a10436a2`（`fix: close remaining U09 persistence authorities`）；该 SHA 的 18 个文件不含 canonical 文档。本节所在 docs-only 提交只引用已存在实现 SHA，不写入自身 SHA。
- Station generation authority：Studio ingress 与 local Station Sync 两个文件按规范路径共享 process-wide gate；每次 save/token regeneration 先读取同一 authoritative generation，再写 GUID transaction 目录中的两个 candidate、previous snapshots 与 SHA-256，持久化 `CommitIntended` marker 后发布，最后写 `Committed`。同步失败 rollback；pre-intent residue 丢弃，半发布 restart 优先验证并 roll-forward 完整 intended generation，candidate 缺失/损坏则 rollback 完整 previous generation。所有 temp 唯一，响应使用稳定 503 errorCode/stage/retryable/recoveryRequired 且不暴露 token；该恢复协议保证收敛到完整 generation，不宣称跨文件断电绝对原子。
- AI model/secret generation：`ai_models.json` 同路径 store 共用 mutation gate，每次 add/update/delete/activate/role default/test/reset 在 gate 内 reload durable generation并构造独立 candidate；DPAPI secret candidate、model schema-v2 candidate、secret final directory 与 model atomic replace 全部成功后才切内存。active/previous 顺序、previous unreadable fail-closed、candidate cleanup fail-soft、严格 GUID-N `GenerationId`、canonical containment 与 reparse-point 拒绝阻止恶意 generation 触发 secret pruning；失败只投影 secret-free 503，restart 只选择完整旧/新 generation。
- Prompt/flow/scenario 与 metrics：`prompt_versions.json`、`ai_flow_versions.json` 各自用 path-keyed gate 覆盖 load/recover → create/delete/activate/metrics increment 或 version/scenario save/activate → unique durable candidate → atomic commit。并发 metrics 不丢增量，version 单调唯一且 active 不回退。metrics mutation 与 degraded/recovered health transition 在同一 durable order 内完成；I/O 失败记录 bounded retryable event，成功 LLM/flow 不反向失败，失败路径保留原始异常。
- Database maintenance authority：status/repair/backup/restore/cleanup 按规范数据库路径共享 gate。restore 先生成 safety backup、DB/package candidate 与 hash，写 durable recovery marker 后才发布；candidate 在破坏性步骤前再次验证。普通 publish 失败精确恢复旧 DB/package；中断或二次 rollback 失败保留 safety backup、non-completed marker并 fence 当前实例，干净实例重启后继续 recovery。marker 的 operation/state/hash/root containment 均 fail closed，恢复中的库不会被并发维护命中；协议保证可恢复收敛，不宣称跨文件系统断电原子。
- PlanRun CAS：production 精确旧 `/api/ai/agent-plan` route 与 ordinary frontend fallback 为 0，唯一正式入口为 `/api/ai/agent-plan-runs`。初始 planning 与 completed/failed/cancelled terminal mutation 均强制 `workspaceExpectedRevision + clientMutationId` 并使用同一 workspace CAS；receipt 绑定完整 planning request fingerprint。相同 mutation/payload 幂等重放且 planner 只启动一次，同 mutation ID 异 payload 返回 `409 workspace_mutation_id_conflict`；长请求期间 user save 推进 revision 后，旧 terminal conflict 且零覆盖。
- 确定性故障矩阵：barrier 覆盖 Station save/token 跨实例、AI 同/跨 store mutation、metrics/health 顺序、flow version、scenario save/activate、database 五类维护并发与长 PlanRun/user save。fault injector 覆盖权限/I/O、secret write、candidate write/read/hash、marker/replace、pre/post commit、Station/DB 半发布、rollback 二次失败、缺失/损坏/out-of-root safety backup、restart recovery、metrics success/failure、missing/stale revision、duplicate/异 payload mutation ID及 terminal complete/fail/cancel 竞争。所有失败验证旧文件/内存不被假成功污染，公开响应不泄密。
- .NET focused：Product 以 `run-dotnet-test-serial.ps1` 对同一 csproj 合并 AI model/prompt/flow/metrics 类，最终 `104/104` PASS；Desktop 合并 AgentRun、AI endpoint、Station store/endpoint 与 database maintenance 类，最终 `171/171` PASS。首次成功 build 后的复跑均使用 `-NoBuild -NoRestore`，同一 csproj 始终单进程串行。
- 固定回归：services `596 passed / 1 failed / 597 total`；唯一失败仍为 `AuthenticatedContextProjectionServiceTests.Project_ShouldReturnExactCapabilitySetsForEverySupportedRole`，Engineer 比旧期望多 `inspection.results.read`。Phase 4.2 `143/143`、PLC `126/126`、Desktop endpoints `412/412`；相对阶段 SHA 的 `407/407` 仅测试人口增加 5，无新失败。
- UI：Agent UI contract `389/389`，完整 `npm run test:unit` 为 48 files、`1020/1020` PASS。Station+AI 四个 spec 的 Chromium 单 worker定向执行共 61 项，`35 passed / 25 failed / 1 skipped`；Station `5/5` 全过，25 个失败由完整非视觉基线中的 12 个既有 AI 签名及额外 13 个显式视觉 baseline 差异组成，未更新快照、未称 targeted full PASS。
- 完整非视觉 lane：`npx playwright test --project=chromium --workers=1 --grep-invert "visual baseline" --reporter=list` 共 188 项，`166 passed / 21 failed / 1 skipped`，耗时 9.9 分钟；失败精确保持 AI 12、flow-editor-port 1、flow-layout 5、Station high-frequency 1、Quiet Precision 2。相对给定基线无数量或签名变化，不称 full Playwright PASS，也未更新 visual baseline。
- 静态/残余/清理：changed JS/MJS `node --check` `1/1`；changed PowerShell/JSON 均 0 文件且对应 AST/parse error 为 0；`git diff --check` PASS。production 精确旧 `/api/ai/agent-plan` route/fallback、锁外 model/prompt/flow 整文件 RMW、固定共享 temp、无版本 PlanRun workspace mutation 与吞掉权威 Save/commit 异常扫描均为 0；仅唯一 non-authoritative candidate/completed residue cleanup 允许 best-effort。`.tmp/test_results`、Playwright `test-results`/`playwright-report`、临时 DB/config/secret/recovery 及仓库关联测试进程均已清理。
- 范围与 disposition：未 push，未执行 visual baseline 更新、clean clone、真实 WebView2/目标机、真实 LLM/PLC/相机或同 SHA GitHub CI；未进入 U10/U11/G16、FrontendV2 或 Wave 3。`CV-AUDIT-040/041/069/070/079/080/082 = CLOSED`；102 个源 ID 重算为 31 个 `IMPLEMENTED_SYNC_PENDING`、37 个仍开放、34 个已关闭，U09=`CLOSED`。U11 与 `CV-AUDIT-086`、U10、G16 状态不变。

### 7.10 Wave 2D Execution Authority + Live Resource Safety 验证证据（2026-08-31）

- 用户给定代码基线为 `4b2af7e4ec7ace45360f9fa71364311916a94e11`；先行独立 U14 doc-only 纠正 SHA 为 `dccc41f257d245ea36f4d5477f16920155cb4f32`。Wave 2D 最终纯 implementation/integration evidence SHA 为 `f06c95212fff33b53297c8a3157cd5b736cda3f6`（`feat: enforce execution authority and live resource safety`）；该实现提交不含 canonical 文档。
- Execution authority：immutable snapshot 将 Source、RunMode、principal、project/package revision、flow hash、capability manifest 与 ResourceBindings 一次绑定；Operator 只能执行 authoritative StoredProject/RuntimePackage，Draft 对 project/revision/capability/confirmation/audit 逐项 fail closed。正式、实时、Station package-load、node preview、single-op preview、AutoTune 均在任意副作用前完成 validation/admission；Preview/Debug 的有界输入与隔离状态不获得文件、网络、数据库或设备副作用。
- Resource matrix：文件为 approved root + canonical path + traversal/reparse/symlink 防护；HTTP broker 每次 DNS/redirect 校验 scheme/host/port/CIDR 且限制响应；DB、PLC、TCP、串口、相机只读服务端 profile/binding，标定写 project calibration asset。Admin 不能以角色直接提交 raw path/URL/connection string/CameraId/PLC/TCP/serial target；合法 authoritative industrial I/O 仍保留。
- State/cancellation：Statistics、Timer、Frame、Trigger 采用 project/session/flow/run/operator 复合作用域；GenerateFlow registry 为 owner+session+request compare-and-remove，删除 latest fallback。取消前 zero dispatch；已 dispatch 结果未知时 FileOperatorDispatchJournal 持久 correlation/outcome=`indeterminate`，不声称回滚。无消费者的 `/autotune/operator`、`/autotune/flow-node` 已删除，保留 AutoTune 面有 capability、iteration、deadline、concurrency 和 RequestAborted gate。
- Live resource matrix：DeepLearning/SemanticSegmentation 以 canonical path+SHA-256 热替换且 lease-safe；TextSave 使用 bounded striped lock；Camera lock/lease 可回收；preview 仅增加 owner/session heartbeat+TTL；PLC pool 具 capacity、idle/断线 eviction 与 lease-safe dispose；Auth session 具 expiry/capacity/revocation；upload/result image cache namespace/budget 独立。`057` 的 OnnxPatch、`060` direct-acquire idle、`081` U08 读取授权仅做回归，未重做或改 disposition。
- Deterministic fake/barrier：覆盖 role×source×mode×resource、path/reparse、HTTP redirect/DNS、raw device target、state pollution、cancel race、model hot swap、10k locks、camera lease、preview TTL、PLC eviction、token capacity/revocation 与 image-cache pressure；所有拒绝路径均断言 filesystem/network/database/device handler 调用为 0。只使用 fake broker/device，未访问真实 PLC、相机、数据库或外网。
- .NET focused：Product `390/390` PASS，Desktop `224/224` PASS。回归为 Phase 4.2 `143/143`、PLC fake-only `135/135`、Desktop endpoints `453/453`；完整 UI unit 为 49 files、`1023/1023` PASS。Chromium 只跑 nonvisual 单 worker targeted subset，`22/22` PASS；保留既有完整 nonvisual 基线 `166 passed / 21 failed / 1 skipped`，未更新视觉 baseline，也不称 full Playwright PASS。
- Services regression 为 `727 passed / 2 failed / 729 total`，不是 full PASS：`GeneratedCatalogsCardsAndKnowledgeGraph_ShouldMatchRuntimeMetadata` 与 `OperatorIdentitySnapshot_ShouldRemainStable` 均因 calibration asset metadata 已改变而等待 U14 源文档同步。相较此前 `596/1/597` 的既有 Engineer capability 期望差异，测试人口与失败集合均已变化，故只如实记录差异，不将其包装为同签名 PASS。
- 静态 residual 为 0：governed boundary 外 raw flow-engine、governed adapter 外 authority scope、latest/session cancel fallback、永久 FileLocks、不可回收 camera locks、operatorId-only state dictionary、共享 upload/result eviction 均为 0；PLC capacity/eviction 与 Auth per-user/global capacity 均有 production markers。
- 范围与 disposition：未 push；未进入 U12/U13/G16、Wave 3、visual baseline、clean clone 或外部设备验证。`CV-AUDIT-032/048/049/050/051/052/053/055/056/065/072/102/057/058/059/060/063/071/081 = CLOSED`；账本为 31 个 `IMPLEMENTED_SYNC_PENDING`、18 个 open、53 个 closed，U10=`CLOSED`，U11=`OPEN_RESCOPED`，仅余 `066/067/068/086-P1/087/090/093`。

### 7.11 Wave 2E Engineering Audit Closure 验证证据（2026-08-31）

- Code baseline：Wave 2D doc-only SHA `426f2c35cf20f188d9e833c0b144effa363fb3fa` 之后的最终纯 implementation/integration evidence SHA 为 `dd5c066764b3f2e7b7d73fcc0b379e57dae1d857`（`feat: close wave 2e engineering audit findings`）。该实现提交包含正式生成资产，但不含本文件或持续问题记录；本节所在的后续 docs-only 提交仅引用该 SHA。
- Metadata gate：先审查 Wave 2D approved identity 从 `8A40F224C5FC16FE04225B4C0DE457DB4DAFD7DC4F6DD85DC622D40FA1DAE44D` 到 `EF6426A519C056D0240839C469295DC7FC5FAC02BA65F98EEF75E2E976DFBCF2` 的精确 snapshot 差异（24 个 Wave 2D operator version bump），再运行正式 OperatorDocGenerator → OperatorKnowledgeGraphRunner。Wave 2E 的 U13 改动只造成 Comparator、DualModalVoting、ModbusCommunication、ResultOutput、TextSave 五张正式卡的显式 version/fingerprint 更新；post-implementation identity snapshot 仍为后者，未发生未审查的 public contract 漂移。两项 metadata focused `2/2` PASS，1,061 个生成资产连续第二次生成 `0 diff`。

| 域 | 闭合 ID | 稳定契约/故障矩阵 |
| --- | --- | --- |
| U11 retention | `066/067/068/086/087/090/093` | AgentRun hot cache/token、Preview serialized cleanup、ResultOutput governed storage、replay/spool/deadletter/command spool quota+gap+health、ownership manifest 及 low-space fail-closed；fake clock、fault injection、restart/concurrency/isolated directory。 |
| U12 query/export | `001/003/074/075/076/078/088` | 一次可证明未送达的 cross-base retry、publish denylist、analysis/Station DB query budget、trend bounds、command SSE replay/monotonicity、共享 CSV sanitizer。 |
| U13 contract | `095/096/097/098` | RTU public removal + legacy zero-I/O unsupported、canonical detection adapter、real DeepLearning→DualModalVoting line、Comparator validator/execute shared allowlist。 |

- .NET：Product focused `645/645`、Desktop focused `55/55`、services `735/735`、Phase 4.2 `143/143`、PLC fake-only `135/135`、Desktop endpoints `460/460` 均 PASS；同一 csproj 始终串行。UI unit 为 `1029/1029` PASS；新增 Wave 2E Chromium acceptance `4/4` PASS（idempotent port recovery、analysis budget 400、Station command SSE reconnect/Last-Event-ID/terminal monotonicity、CSV 下载和公式中和）。
- Publish/静态：Studio framework-dependent publish `382` files、Station `230` files，`Test-ReleasePublishHygiene.ps1` 均 PASS；9 份 RID lock file 以 `win-x64 --locked-mode` 对 Desktop/Station 复验可重现。changed JS/MJS `node --check` `9/9`、PowerShell AST `3/3`、JSON parse 与 `git diff --check` PASS；`.tmp/publish-check/wave2e`、外部 UI logs、TRX、Playwright `test-results`/report 均已清理。
- 完整 Chromium 非视觉 lane：在 `dd5c0667` 实际执行 `npx playwright test --project=chromium --workers=1 --grep-invert "visual baseline" --reporter=list`，得到 `137 passed / 54 failed / 1 skipped`（192 项，23.1m）。四个新增 Wave 2E 用例均通过；跳过项是 `AI panel uses WebView2 postMessage contract for generation turns`。这是非零失败的真实结果，不更新 visual baseline，也不宣称 full Playwright PASS。
- 相对历史 `166/21/1`：历史 21 个失败族在此次仍可观察；本次额外观察到 Flow layout 9 项、continuous-run 1 项和 ROI 23 项，共增加 33 个失败条目。它们是完整 lane 的质量/环境债务，不改变已通过的 Wave 2E targeted acceptance 或 18 个工程 ID 的 disposition。
- Chromium 失败签名（54，逐项）：
  - AI agent responsive（4）：`Plan pending recommendation records defer without becoming an effective answer`（`locator.check` 30s）；`AI agent workbench default Plan view hides raw semantic trace until diagnostics expand`（clarification question 缺失）；`AI agent Plan to Build exposes visible Apply button and applies through real click path`、`AI agent Apply button keeps dimensions on narrow viewport after Build ready`（`#ai-btn-start-build` 保持 disabled / build-gate validation failed）。
  - AI build workspace（5）：`resource workspace reuses the existing binding action and updates canonical result data`（resource input 30s 缺失）；`mixed ordinary and resource parameters stay partitioned through confirmation and binding`（resource input count=0）；`Applied remains inside Build and preserves the engineering history`（`build-applied-light-1366` 10777-pixel screenshot diff）；`Build remains reachable without horizontal overflow at 1024x768`、`...at 390x844`（resource input 缺失）。
  - AI plan clarification（3）：`manual supplement uses explicit_user_text and does not copy the main Composer`；`keyboard path reaches options and keeps the current action unique`；`confirmed clarification collapses into a low-emphasis light summary`。
  - Flow editor port（1）：`preview workbench prompts to configure the acquisition source (no console errors)`。
  - Flow layout（14）：`supports global operator search, scoped category search, drag-add, and rail scroll retention`；`adds an operator from flyout and switches inspector and preview workbench`；`resizes the preview workbench wider with a real splitter drag and keeps core controls first-screen`；`shows output image summary and debug image operations in the right workbench`；`routes a real retargeted pointermove through the image stage`；`keeps the pixel probe live across hover, lock, ROI, and preview changes`；`marks old preview stale after parameter edit and clears stale after manual preview`；`prevents duplicate manual preview while loading and exposes cancel state`；`syncs dependency-controlled fields for template matching`；`shows missing camera prerequisite for camera acquisition without CameraId`；`captures one camera frame into the preview workbench and reuses it in the ROI editor at 1920x1080`；`shows blank, no-image and preview-failure states`；`clears current preview state for connection, blank selection and deleted selected node`；`shows layered Chinese diagnostics for missing resources and failed operator metadata`。
  - High frequency（2）：`continuous run regression: protection guidance is visible before and after continuous run`；`station result filter regression: server-paged filters refresh monitor results`。后者的 fixture 尝试选择过时值 `Ng`，而实际 option value 是既有 `ng`，因此不是 Wave 2E DB budget regression。
  - Quiet Precision（2）：`initial 1920 desktop layout audit (light)`、`initial 1920 desktop layout audit (dark)`，均为 30s layout audit timeout。
  - ROI editor（23）：`shows ROI editor for rectangle circle and valid polygon shapes`；`image editing headers remain readable in the narrow inspector`；`drawing a new rectangle updates XYWH and triggers one extra preview`；`dragging and resizing the ROI updates parameters`；`circle ROI move and radius handle update CenterX CenterY and Radius`；`circle ROI blank drag creates a circle from the selection box`；`CircleMeasurement CaliperFitV2 groups parameters and mounts circle search geometry`；`CircleMeasurement circle search center drag commits explicit geometry once`；`PolarUnwrap arc editing updates annulus radii and angle params`；`polygon ROI vertex drag insert delete undo redo updates PolygonPoints only on commit`；`polygon ROI stays editable with empty PolygonPoints and blank drag writes valid points`；`polygon ROI drag clamps at image bounds instead of freezing`；`NPoint draft workbench edits samples while ROI editor stays unmounted`；`pointer movement stays local draft until mouseup commit`；`pointer capture commits once when released outside the ROI canvas`；`unreleased pointer drag is canceled when the preview input image switches`；`...when the preview input image disappears`；`...when switching to another node`；`...when ROI editor is destroyed`；`delayed older preview image cannot replace newer preview during unreleased drag`；`pointer cancel and ROI editor destroy roll back active drafts without commit`；`keyboard nudge undo redo and escape cancel remain local to the ROI draft session`；`manual parameter edits sync overlay and right-button pan does not mutate ROI`。共同失败点为 `waitForRoiEditorReady` 的 `currentImageSource && imageCanvas.image` 在 30s 内未成立。
- 范围与 disposition：未 push，未执行 visual baseline、clean clone、真实 WebView2/目标机、真实 LLM/PLC/相机/外部数据库或同 SHA GitHub CI。`CV-AUDIT-001/003/066/067/068/074/075/076/078/086/087/088/090/093/095/096/097/098 = CLOSED`；102 项账本为 `31 IMPLEMENTED_SYNC_PENDING / 0 engineering open / 71 closed`，U11=`CLOSED`，U12=`CLOSED`，U13=`CLOSED`，`CV-AUDIT-086=CLOSED`。U14、G16、发布和外部证据仍开放。

Wave 可以拆成小提交，但每个源 ID 必须保留独立验收行。只有在 required profiles、最终 Release SHA 和源文档回填全部关闭后，才归档本文及用户指定的七组文档。
