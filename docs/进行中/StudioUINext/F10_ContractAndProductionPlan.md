# F10 Studio UI Next 合同解阻与生产化执行台账

> 本文是 F10 当前执行 source of truth。状态只记录当前代码、测试和环境取得的证据，不把历史报告或未运行的门禁记为通过。

## 基线

```text
F10_STATE=ACTIVE
F10_START_HEAD=38b80b0dfcb66db67a9eab5ff84f80b994104606
F10_START_REMOTE_HEAD=38b80b0dfcb66db67a9eab5ff84f80b994104606
IMPLEMENTATION_HEAD=93cc880619b51d68828bcbc3336b77c83ad60dcc
DOCUMENTATION_HEAD=SELF
BRANCH_HEAD_AT_REVIEW=SELF
REMOTE_IMPLEMENTATION_HEAD=cd83db2bcc3117dbcd3039764ce8aa57206e5396
BRANCH=studio-ui-next
PROJECT_IMPORT_EXPORT=DONE
AI_ATTACHMENT_RESOURCE=DEFERRED
AI_MODEL_RESOURCE=DEFERRED
AI_TEMPLATE_ARTIFACT=DEFERRED
AI_CALIBRATION_PROJECTION=DEFERRED
NPOINT_AUTHORIZATION=DONE
PLANAR_CALIBRATION=DONE
RESULTS_BULK_EXPORT=DONE
LINE_SEQUENCE=DONE
F10_BROWSER_JOURNEYS=DONE
STATION_TEST_PACKAGE=PARTIAL
DATABASE_ADVANCED=DEFERRED
LOCAL_SOFTWARE_EVIDENCE=DONE
PARTIAL_EVIDENCE=PARTIAL_EXTERNAL
WEBVIEW2_125=NOT_PERFORMED
INDEPENDENT_NO_NODE=NOT_PERFORMED
REMOTE_CI=BLOCKED_BY_ENVIRONMENT
FINAL_GATE=PARTIAL
PRODUCTION_ACCEPTANCE=NOT_GRANTED
LEGACY_RETIREMENT=NOT_APPROVED
G1_STATE=DONE
G1_BASELINE_HEAD=21105d57de7e5b4ce41365c7827ed14e64ca7ba5
G1_IMPLEMENTATION_HEAD=98cb8c7f54d2d51ea5b59ca534aafd51544b773f
G1_WORKTREE_STATE=COMMITTED_IN_BRANCH_HISTORY
G1_VERIFICATION_DATE=2026-08-09
G2_STATE=DONE
G2_BASELINE_HEAD=21105d57de7e5b4ce41365c7827ed14e64ca7ba5
G2_IMPLEMENTATION_HEAD=98cb8c7f54d2d51ea5b59ca534aafd51544b773f
G2_WORKTREE_STATE=COMMITTED_IN_BRANCH_HISTORY
G2_VERIFICATION_DATE=2026-08-09
G3_STATE=DONE
G3_BASELINE_HEAD=98cb8c7f54d2d51ea5b59ca534aafd51544b773f
G3_PRODUCT_IMPLEMENTATION_HEAD=a3c043e77ff9bcbc80fbf638f8f9f52a217fa8a8
G3_EVIDENCE_HEAD=1c6e61e5a53d59ac3a7f78054af5eab3e86ec667
G3_WORKTREE_STATE=COMMITTED_IN_BRANCH_HISTORY
G3_VERIFICATION_DATE=2026-08-09
G4_STATE=DONE
G4_BASELINE_HEAD=1c6e61e5a53d59ac3a7f78054af5eab3e86ec667
G4_HOST_IMPLEMENTATION_HEAD=245e9cec9398cbcc2bc42d3d3cc79176634a76bb
G4_EVIDENCE_HEAD=1c8ad67f3a890ed0a8cd72702cef82ed9623f367
G4_WORKTREE_STATE=COMMITTED_IN_BRANCH_HISTORY
G4_VERIFICATION_DATE=2026-08-09
G4_WEBVIEW2_ROLLBACK_100_DEBUG=PASS_AUTOMATED
G5_STATE=DONE
G5_BASELINE_HEAD=1c8ad67f3a890ed0a8cd72702cef82ed9623f367
G5_PRODUCT_IMPLEMENTATION_HEAD=1ed799231bcc003c074d6787e9448433bed32956
G5_EVIDENCE_HEAD=93cc880619b51d68828bcbc3336b77c83ad60dcc
G5_VERIFICATION_DATE=2026-08-09
G5_LOCAL_SOFTWARE_GATE=PASS
G6_STATE=BLOCKED_BY_ENVIRONMENT
G6_LOCAL_EXTENSION_HEAD=f97009fabca7567598fab59e29ccc1037c472a09
G6_LOCAL_DEBUG_SIZE_HEAD=35631f5309231899f25e656f952c79c877cc20e7
G6_LOCAL_RELEASE_SIZE_HEAD=f97009fabca7567598fab59e29ccc1037c472a09
G6_LOCAL_UI_AUDIT=PASS
WEBVIEW2_100_LOCAL_AUTOMATED=PASS_DEBUG_AND_RELEASE
WEBVIEW2_100_SIZE_MATRIX=PASS_DEBUG_AND_RELEASE_1920_1536_1366
FIELD_CAMERA_PLC_STATION_AI=NOT_PERFORMED
PRODUCTION_SOAK=NOT_PERFORMED
```

## G0 候选冻结与稳定线语义同步

```text
G0_STATE=DONE
G0_REVIEW_DATE=2026-08-08
G0_PREVIOUS_CANDIDATE_HEAD=66ff270df251ca3b9106c9c89f3ed1ba308aa6f7
G0_IMPLEMENTATION_HEAD=21105d57de7e5b4ce41365c7827ed14e64ca7ba5
G0_DOCUMENTATION_COMMIT=SELF
G0_REMOTE_STUDIO_UI_NEXT_BEFORE_PUSH=f8569fa85244d19a18ba7308051e4d2b2ed4060a
G0_STABLE_REF=origin/codex初稿@e76c74e392bb14ffe02ef9ea9c7a614cb8987f04
G0_MERGE_BASE=e1bad492fecb6dff2c0a8f848db9ebfa18acf093
G0_DIVERGENCE=HEAD_ONLY_309_STABLE_ONLY_81
G0_STABLE_COMMITS_AUDITED=81
G0_STABLE_COMMITS_LEFT_ONLY=77
G0_STABLE_COMMITS_PATCH_EQUIVALENT=4
G0_GENERATED_CATALOGS=CONTROLLED_SOURCE_DERIVED_OUTPUTS_PLUS_NOT_APPLICABLE_HISTORICAL_SNAPSHOTS
G0_CONTROLLED_GENERATED_OUTPUTS=docs/算子资料/{算子目录.md,算子目录.json,算子变更记录.md,算子版本记录.json,算子名片/*catalog.json,算子名片/*version-history.json,算子名片/CATALOG.md,算子名片/CHANGELOG.md}
G0_REMOTE_REFRESH=PASS_NO_REMOTE_ADVANCE_OR_FORK
G0_WORKTREE_STATE=IMPLEMENTATION_CLEAN_BEFORE_DOCUMENTATION_COMMIT
```

远端刷新于 2026-08-08 执行：`git fetch origin --prune` 成功；`origin/studio-ui-next` 仍为
`f8569fa85244d19a18ba7308051e4d2b2ed4060a`，未前进、未分叉。候选实现先提交为
`21105d57de7e5b4ce41365c7827ed14e64ca7ba5`，本节文档随后单独提交；因此实现 SHA、文档来源和
最终远端 SHA 可以分别追溯。

### Stable-only 语义矩阵

下表覆盖稳定线 merge-base 之后的 81 个提交：77 个没有在 Next 历史中找到 patch-equivalent 的提交，
以及 4 个已由 Next 等价提交覆盖的提交。`MERGE` 表示已将源码、合同、权限、测试或 CI 语义以当前
架构落入 `21105d57d`；`SUPERSEDED` 表示无需再次合入；`NOT_APPLICABLE` 只适用于当前 G0 明确
排除的生成快照或历史远端证据，不代表对应源码合同被跳过；没有发现需要阻断的 `BLOCKED` 提交。

| 范围与稳定线提交 | disposition | 当前代码锚点 | 冲突/风险处理 | 验证与边界 |
| --- | --- | --- | --- | --- |
| `ac7701ffd`, `fe4b42f13`, `0ebbb6ecc`, `58545fab1`, `924e3afaf`, `2d941da14`, `dca452867`, `01e88c5e8`, `fe39d379c`, `988681cf6`, `59a6aede4`, `d5ef2232a` | `MERGE` | `OperatorMetadataScanner`、`OperatorFactory`、`OperatorService`、operator display-name/parameter contract tests | 以当前 metadata source 和 `OperatorCategoryId` 为准，保留 Next 现有 UI 投影；未使用整文件 ours/theirs | Product metadata/operator 定向 `49/49`；Product solution build `0 errors` |
| `4485c1d07`, `6976c7bcc` | `NOT_APPLICABLE`（历史生成快照） | 运行时继续使用当前 source metadata；未把 `docs/ai/operator-knowledge/*`、`docs/operators/catalog.json`、card/version-history 快照当作 authority | 稳定线旧提交主要重生成历史 catalog/card/knowledge snapshot；这些旧快照留在原状。实现提交中的 8 个 `docs/算子资料` 输出是仓库钩子从当前 source metadata 受控重算的独立输出，已在本次提交中明确纳入 | 旧 `OperatorKnowledgeGraphTests` 因历史 graph 的 DeepLearning 输出为 14、当前 source 为 31 而失败；记录为 `NOT_APPLICABLE_HISTORICAL_SNAPSHOT`，未静默改 fixture |
| `08125e8a7`, `82e34837a`, `6c0fa1f02`, `5667bfbe7`, `ce266626e`, `0a827d78c`, `97d25440b` | `MERGE` | `OperatorImageContracts`、depth/domain evaluators、operator quality contracts、classification/precision tests | Image depth 约束保持后端/operator contract 权威；quality lane 与 functional lane 分开，不改变 Runtime authority | Product image-contract/quality 定向测试通过；OperatorLibrary smoke `41/41` |
| `f1efcfc11`, `6e4906656` | `MERGE`（源码/门禁）+ `NOT_APPLICABLE`（生成 evidence） | `quality/test-gates.json`、`TestGovernanceRunner`、`OperatorQualityState`、CI quality jobs | 合并可执行的 governance runner 和 source contract；历史 generated quality/catalog report 不作为当前 SHA 证据 | Product/OperatorLibrary 本地门禁通过；生成报告不冒充当前 clean-checkout/Remote CI 证据 |
| `505a33a5f`, `f7fcd2fac`, `9df0fa73d`, `ef103c899`, `727414e2c`, `f7e9eea40`, `dcd16e005`, `549f56af1`, `5b9324d02`, `5306a570d`, `e7b34f591`, `afcbfd686`, `dfa5ea1ef` | `MERGE`（脚本/测试/质量规则）+ `NOT_APPLICABLE`（历史 benchmark 输出） | `scripts/run-test-quality-lane.ps1`、measurement/benchmark runners、`.gitattributes`、package smoke | 保留 evidence identity、顺序执行和 fixture byte identity；benchmark 数字必须由当前命令重现，不能复制历史结果 | OperatorLibrary pack + smoke `41/41`；性能/质量报告未宣称为 Remote CI 当前通过 |
| `b7667b01e`, `07ff5ede5`, `2eacc62d1`, `4403fee02`, `4386d8f35`, `bea404394`, `47d749468` | `MERGE` | planar calibration/1080P layout、operator search、single-frame preview/ROI、Unicode image paths、相关 Desktop/UI tests | 保留 canonical Canvas/ImageCanvas、Preview/ROI 和既有 camera/acquisition authority；只解决语义与布局差异 | Desktop synchronized group `106/106`；Unicode/acquisition、Preview/ROI 和 1080P 代码测试随 Product build 验证 |
| `5887387c0`, `d1abc87fc`, `3c83e48d9`, `8008ab435`, `7a8110bbc`, `6c8d8a81d`, `bdc26abc4`, `132b2d543`, `99e2a538a`, `f8adf5221`, `e609f47bf`, `753b1b188`, `c6bb5302e`, `fc8ba0e81`, `674d26930`, `1dad6ced6`, `809d220bf`, `e8efbb904`, `402cf4856`, `432d6e302`, `ce98ae9bd`, `684de59a3`, `ed8af7a04`, `3a5076b54`, `64ff3c73a`, `ef4d1872a` | `MERGE` | `.github/workflows/*`、serial test runners、Desktop/TCP/SSE/photoelectric tests、agent fixture contracts、quality boundary | 共享 CI、测试配置和 `.csproj` 逐项语义合并；同一 `.csproj` 串行运行，未引入第二 runner 或第二 gate | Desktop extension `113/114`；唯一失败为 Windows Event Log 写权限，标记 `BLOCKED_BY_ENVIRONMENT`；其余 OperatorLibrary/Product gates 通过 |
| `0ffa3b98b` | `MERGE`（测试/源闭包）+ `NOT_APPLICABLE`（generated metadata snapshot） | generated metadata governance test 与当前 source metadata contracts | 不把 generated catalog 变成前端或 AI authority；snapshot drift 显式保留 | 同上；历史 knowledge graph mismatch 单独记录，不作为源码合同失败 |
| `3725d7e97`, `c5bc80454`, `9a40c1bed` | `NOT_APPLICABLE`（历史远端 evidence） | 无运行时 authority 变更；F10 仅保留当前候选的本地证据 | 这些提交绑定旧的 G01B-R2 remote report SHA，不能冒充 `21105d57d` 的 Remote CI | Remote CI 当前候选 `NOT PERFORMED/BLOCKED_BY_ENVIRONMENT`；未创建 run |
| `8d72ba392`, `7dadc8535`, `eb199a8fa`, `2f44a8c13` | `MERGE` | Vision Agent planning readiness、artifact admission/fingerprint/recovery、active model、enum compatibility | 退役旧 Planner/Loop authority，保留 AgentRun/endpoint 后端权威；blob template route 和 failed artifact summary 只作为既有合同投影 | Product AI/Preview/runtime/operator group `84/85`；唯一失败为上表历史 knowledge snapshot，未改变 workflow authority |
| `c17a30ff2`, `80ef45cd8`, `f79582f61`, `e76c74e39` | `SUPERSEDED` | `c5d6c1c23`、`521fb7e70`、`effb42642`、`5c239db5b` 已提供 patch-equivalent 语义 | `git log --cherry-pick` 证明等价；不重复合入，不创建第二 AI/acquisition owner | 由当前 branch history 和本轮相关 Product/Desktop tests 覆盖 |

矩阵结论：所有 stable-only authority/security/contract、operator metadata、acquisition/Preview、质量和 CI
语义均有 `MERGE` 或 `SUPERSEDED` disposition；旧 catalog/card/version-history、AI knowledge snapshot
和旧远端报告均已明确 `NOT_APPLICABLE`，当前 source-derived `docs/算子资料` 输出则由提交钩子受控纳入
实现提交。没有 `BLOCKED` 的稳定线提交，也没有通过前端私有模型替代后端 authority 的改动。
`DeepLearning` 当前 source metadata 仍有 31 个输出，旧 knowledge graph 14 个输出是已知文档漂移，
后续如需更新该历史 snapshot 必须作为受控 artifact 变更单独审计。

### G0 当前候选验证

以下结果绑定实现 SHA `21105d57de7e5b4ce41365c7827ed14e64ca7ba5`；同一测试项目按仓库规则串行运行。

| 证据 | 状态 | 当前结果与边界 |
| --- | --- | --- |
| Product solution build | `PASS` | 0 errors；保留既有 `NU1900` vulnerability-feed warning 与一个 `System.Collections.Immutable` version-conflict warning |
| Product metadata/image-contract 定向 | `PASS` | `49/49` |
| Product Preview/AI/runtime/operator 定向 | `PARTIAL_NOT_APPLICABLE` | `84/85`；唯一失败是旧 `docs/ai/operator-knowledge/operator_knowledge_graph.json` 与当前 source metadata 不一致，按矩阵记为历史快照 `NOT_APPLICABLE` |
| Desktop 同步定向组 | `PASS` | `106/106` |
| Desktop 扩展定向组 | `BLOCKED_BY_ENVIRONMENT` | `113/114`；唯一失败为无权限写 Windows Event Log，未改产品代码或 ACL |
| OperatorLibrary pack + smoke | `PASS` | `41/41`；smoke restore/test 固定 `RuntimeIdentifier=win-x64`，避免无关 native asset 失败 |
| 当前 source-derived operator docs | `PASS_SOURCE_DERIVED` | 提交钩子生成并纳入 `docs/算子资料` 的 8 个目录/变更记录/version-history 输出；它们是文档投影，不是运行时 authority |
| `git diff --check` | `PASS` | 实现提交前通过；文档提交前及 push 前再次复核 |
| 真实 WebView2、Windows 100%/125%、独立 no-Node、Remote CI、Camera/PLC/Station、生产 soak | `NOT_PERFORMED` / `BLOCKED_BY_ENVIRONMENT` | 不以 Chromium、DPR、本机 build 或历史 report 替代 |

## Gate 状态

| Gate | 状态 | 当前证据 / blocker |
| --- | --- | --- |
| G0_REMOTE_CI | BLOCKED_BY_ENVIRONMENT | 当前 G5 候选为 `93cc88061`；`ci.yml` 支持 `workflow_dispatch`，但本机无有效 GitHub 认证入口，未创建当前候选 run，未改 trigger。普通分支 push 不等于完整 CI。 |
| G1_WORKSPACE_LIFECYCLE | DONE | `98cb8c7f5` 已完成请求/写入生命周期、跨工程状态隔离与 leave guard；13 个 G1 定向测试文件共 `103/103`，当前 G5 Studio UI 全量为 139 个文件、`903/903` 测试通过。实现已进入当前分支历史。 |
| G1_PROJECT_CONTRACT | DONE | 复用 `ProjectLifecycleCoordinator`、`ProjectSaveCoordinator` 和现有 Project Service；JSON schema/version、CREATE/OVERWRITE、权限、revision、clientOperationId、validation、partial-save 防护和 replay/reconcile 已由现有 endpoint/lifecycle tests 覆盖。 |
| G1_AI_RESOURCE_CONTRACT | DEFERRED | Camera resource identity/revision/decision 已有 authority；attachment、CV model artifact、TemplateMatching artifact 与 calibration asset-to-scale 投影缺正式后端合同，已按 G2 ADR 冻结延期且未新增私有 authority。 |
| G1_CALIBRATION_CONTRACT | DONE | N 点 draft/solve 继续要求 Engineer/Admin、非空且存在的 Project 上下文；`ScaleOffset` 复用同一 solver、candidate bundle 和 Project asset save 链。本轮新增 Chromium/fixture solve + formal asset save/reconcile journey，未新增 calibration authority。 |
| G2_CONTRACT_AND_GAP | DONE | GlobalVariables 类型/identity 校验与 Line Sequence 最近图像输入/返回预览已落地；AI resource、calibration projection、Database advanced 与旧版高级能力均已冻结 `DEFER/RELOCATE` 边界，完整矩阵见 ADR-G2。 |
| G2_RESULTS_EXPORT | DONE | 已补齐服务端 CSV/JSON export job、clientOperationId 幂等与对账、快照上界、取消、TTL、SHA-256 产物校验和权限错误映射；Results 页面仅对本机结果开放，Station 来源明确不支持。Studio UI 全量 `138/138` 文件、`869/869` 测试通过。 |
| G3_DEVICE_COMMANDS | PARTIAL | Station unknown/reconcile 已有 Chromium/fixture journey。Line Sequence 本轮不执行设备写入，Apply 仅修改 canonical flow draft；未引入新 command authority。现场 Station/PLC 仍未验证。 |
| G4_NEXT_UI_CONSUMPTION | DONE | Line Sequence 唯一 owner 挂载于 FlowWorkspace，只使用 shared `ApiTransport` 和 `FlowCanvasOwner.commands.patchNodeParameters`；Project/Results/Station/Template/Calibration 用户路径已有本轮 Chromium/fixture 证据。 |
| G5_LOCAL_SOFTWARE_EVIDENCE | DONE | `93cc88061` 的 StudioUI、Studio UI Next Playwright、受影响 .NET、真实 WebView2 Debug/Release scale 1.0、profiles、rollback、20-cycle soak、性能、publish 和本机 no-Node 证据均通过。 |
| G6_UX_HARDENING | DONE | G3 产品体验实现在 `a3c043e77`，evidence fixture 修正在 `1c6e61e5a`；F02 `73/73`、F03 `12/12` 方向性截图与 manifest 通过。 |
| G4_LEGACY_PROFILE_ISOLATION | DONE | `1c8ad67f3` 的真实 WinForms/WebView2 Debug 100% rollback manifest 为 PASS；Next/诊断/Legacy/Next 每次只有一个 root 和一个 profile 专属 Host message owner，dispose 后订阅归零，同一 Project/PersistenceRevision/Result authority identity 保持一致。 |
| G7_WEBVIEW2 | PARTIAL | G5 已取得真实 WebView2 Debug/Release 100% 自动证据；`35631f530` 补齐 Debug 三尺寸，`f97009fab` 又以 self-contained Release publish 补齐同一三尺寸与 light/dark、compact/comfortable 投影循环。六组 native DPI 都是 96；Windows 125% 仍为 `NOT_PERFORMED`。 |
| G8_NO_NODE | PARTIAL | 本机 publish 静态扫描、Desktop 进程树无 Node child、sanitized-path 启动均 PASS；独立无 Node 目标机仍为 `NOT_PERFORMED`。 |
| G9_FIELD_HARDWARE | NOT_PERFORMED | 当前环境没有现场 Camera、PLC、Station 验证条件。 |
| G10_FINAL_CI | PARTIAL | 当前 implementation SHA 本地软件 gates 通过；clean-checkout Remote CI、125% DPI、独立目标机、现场硬件、生产 soak 和产品签收未完成，不授予 production acceptance。 |

状态枚举：`DONE`、`PARTIAL`、`BLOCKED_BY_CONTRACT`、`BLOCKED_BY_ENVIRONMENT`、`NOT_PERFORMED`、`FAILED_RELATED`、`FAILED_UNRELATED`、`DEFERRED`。

## 架构权威

- Project、Flow、GlobalVariables、正式 assets、Runtime Package、Results 和 Station 状态继续由现有后端 Application Service、`ProjectSaveCoordinator`、Runtime/Station 链路负责。
- Studio UI Next 继续复用唯一 API transport、Host adapter、canonical FlowCanvas/ImageCanvas、现有保存链和 capability-local lifecycle owner。
- 本轮不新增第二 API transport、HostBridge、EventBus、Project repository、Calibration asset authority、Station command authority 或前端私有持久化链。
- Production acceptance 不由本台账自动授予；软件门禁与真实 WebView2、DPI、no-Node、现场硬件、生产 soak 和产品 owner 签收分别记录。

## 本轮工作记录（2026-08-09）

### G5 同一 clean SHA 的本地软件证据

G5 最终候选为 `93cc880619b51d68828bcbc3336b77c83ad60dcc`。G4 文档冻结后，产品与门禁收口按以下
checkpoint 进入当前历史：`8017f1f0e` 延迟加载 authenticated runtime，`66677079f` 修复最终 Browser
回归，`81ec82f9d` 对齐 Runtime package fixture，`1ed799231` 固化 AI revalidated candidate identity，
`1c466edb3` 补齐 final evidence canonical input。随后四个提交只修改 WebView2 evidence runner/tests：
`4dc7c7e86`、`82538e515`、`3824fa0b5`、`93cc88061`。由于 source SHA 已变化，前端、Playwright、
.NET 和真实 WebView2 最终门禁均在 `93cc88061` 重新执行，不把 `1c466edb3` 的历史 PASS 外推到新候选。

#### G5 执行环境

| 维度 | 当前事实 |
| --- | --- |
| OS / CPU / memory | Windows `10.0.22000` x64；Intel i7-12700F，20 logical CPUs，约 68.5 GB memory |
| WebView2 | Edge WebView2 `151.0.4129.72`；CDP protocol `1.3` |
| 本机显示证据 | 1920x1080 screen；native window 1600x1000、client 1584x961、Browser viewport 1584x936；native DPI 96、scale/DPR 1.0 |
| Node 角色 | 绝对路径 Node 仅作为 Desktop 进程树外的 CDP driver；所有发布 Desktop 进程树 `nodeDescendantCount=0` |
| 证据根 | `.tmp/studio-ui-next/g5-93cc88061/`；被 Git 忽略，未作为源码提交 |

本机 scale 1.0 证据不等于真实 Windows 125%。外置 Node driver 证明 Desktop 发布产物本身不派生 Node，
但不等于独立无 Node 目标机；两项边界分别保持 `WEBVIEW2_125=NOT_PERFORMED` 与
`INDEPENDENT_NO_NODE=NOT_PERFORMED`。

### G6 本机可完成项扩展（2026-08-09）

本节记录两组本机扩展证据：UI 审计与 Debug 三尺寸绑定
`35631f5309231899f25e656f952c79c877cc20e7`，self-contained Release 三尺寸绑定
`f97009fabca7567598fab59e29ccc1037c472a09`。两者之间只修改本执行台账与根 TODO，没有产品代码差异；这些
证据不改变 G6 的外部环境与人工验收边界。

| 命令 / 证据 | 结果 |
| --- | --- |
| `impeccable detect --json StudioUI/src` + Web Interface Guidelines 定向源码扫描 | PASS；detector 返回 `[]`，未发现点击式 `div/span`、无替代焦点、`transition: all`、禁用缩放、缺图像 `alt`/尺寸或 token 外硬编码颜色等可复现问题；这是代码级审计，不替代真实 WebView2 可访问性人工验收 |
| `npm run lint` | PASS；`--max-warnings=0` |
| `npm run typecheck` | PASS；app、Vitest 与 Node 三组 TypeScript 配置 |
| `npm run test:unit` | PASS；139 files、`903/903` tests |
| `npm run bundle:ci` | PASS；Vite 530 modules，production bundle budget gate PASS |
| `npm run bundle:verify` | PASS；两次 production build 产物可复现 |
| WebView2 Debug 1920x1080 window | PASS；client 1904x1041、viewport 1904x1016、native DPI 96、DPR 1、overflow 0、Desktop Node descendant 0 |
| WebView2 Debug 1536x864 window | PASS；client 1520x825、viewport 1520x800、native DPI 96、DPR 1、overflow 0、Desktop Node descendant 0 |
| WebView2 Debug 1366x768 window | PASS；client 1350x729、viewport 1350x704、native DPI 96、DPR 1、overflow 0、Desktop Node descendant 0 |
| Debug 三组主题 / 密度投影 | PASS；每组均由真实 WebView2 UI 从 light/compact 切到 dark、comfortable，再恢复 light/compact；未产生 HTTP 写请求 |
| Debug 截图与像素复审 | PASS；3 张真实 WebView2 PNG 尺寸分别为 1904x1016、1520x800、1350x704，抽样颜色分别为 179、183、223，非空且未见遮挡、截断、越界浮层或异常滚动 |
| Debug shutdown / cleanup | PASS；每组 10 个 shutdown stage records，forced exit、uncertain、parse error、deadline violation 均为 0；Desktop 进程与端口清理完成 |
| Debug 证据根 | `.tmp/studio-ui-next/g6-35631f530/100dpi/`；Git ignored，不作为源码 authority |
| Release self-contained publish | PASS；通过仓库 `scripts/dotnet.ps1` 生成，StudioUI Vite 530 modules；受限网络下仅有 NuGet vulnerability feed `NU1900`，publish 返回 0，product version/source SHA 均为 `f97009fab` |
| WebView2 Release 三尺寸 | PASS；1920x1080、1536x864、1366x768 window 的 client/viewport 分别为 1904x1041/1904x1016、1520x825/1520x800、1350x729/1350x704；native DPI 96、DPR 1、PerMonitorV2、overflow 0、Desktop Node descendant 0 |
| Release 主题 / 密度、owner 与 Canvas | PASS；三组均实际完成 light/compact → dark → comfortable → light → compact，Workspace/FlowCanvas/Inspector/ImageCanvas/ROI/Preview owner 均为 1、冲突 0；Canvas backing store 与 pointer hit-test 均通过 |
| `Test-StudioUiDpiEvidence.ps1 -ExpectedScales 1.0` | PASS；3/3 Release manifests 通过 project DPI authority、runtime awareness、WebView2 force scale、CDP/JS/screenshot scale 与 Canvas backing/hit-test 全部层级 |
| Release 截图与像素复审 | PASS；PNG 尺寸与 viewport 一致，8px 网格抽样颜色数为 587、556、534，非白像素占比 69.21%、62.61%、60.16%，亮度跨度 18-255；目视未见遮挡、文本截断、越界浮层或异常滚动 |
| Release shutdown / cleanup | PASS；三组均无 forced exit、uncertain、console error、request failure 或 deadline violation；Host message owner dispose 后订阅为 0，Desktop、端口、数据库、runtime root 与 WebView2 user-data 全部清理 |
| Release 证据根与临时发布 | `.tmp/studio-ui-next/g6-f97009fab/100dpi-release/` 保留为 Git ignored evidence；`.tmp/publish-check/g6-f97009fab-100dpi*` 已验证路径后删除 |

失败样本没有计入 PASS：第一次调用在宿主启动前被当前 PowerShell execution policy 拒绝；第二次因外层工具等待仅
5 秒而中断，遗留的单个 Desktop 进程已按精确 PID 停止。最终 1920x1080 使用新 run `r3` 完整通过，后续两组
均使用独立端口、WebView2 user-data、数据库与 isolation root。

本轮观测的真实桌面仍为 1920x1080、native 96 DPI。上述 Debug/Release 尺寸与主题/密度证据补齐 G6.2 的
100% 本机侧；不能将 DPR、window resize 或 Chromium fixture 冒充 Windows 125%，因此 G6.1/G6.2 仍不勾选。

#### StudioUI 与 Browser 门禁

| 命令 / 证据 | 结果 |
| --- | --- |
| `npm run lint` | PASS；`--max-warnings=0` |
| `npm run typecheck` | PASS；app、Vitest 与 Node 三组 TypeScript 配置 |
| `npm run test:unit` | PASS；`139` files、`903/903` tests |
| `npm run build` | PASS；Vite `530` modules |
| `npm run build:production` + `npm run bundle:gate` | PASS；JS `1,765,384 B`、CSS `328,876 B`、other `15,998 B`、total `2,110,258 B` |
| `npm run bundle:verify` | PASS；两次 production build 的规范化报告一致，全部 budget PASS |
| `CV_UI_SCENARIO=studio-ui-next npx playwright test --reporter=list` | PASS；`164 passed / 52 evidence-only skipped / 0 failed`，216 total |
| `soak-metric-analysis.test.cjs` | PASS；`4/4`，覆盖有界预热、持续泄漏、总量阈值和跨度缩放 |
| `studio-ui-next-infrastructure.test.mjs` | PASS；`3/3`，锁定性能 handle dispose 与 completion signal 协议 |
| 相关 `.cjs` `node --check` | PASS |

52 个 evidence-only skip 需要正式 SHA/环境变量和独立 evidence directory；本轮没有用占位 SHA 强行执行。
正式方向性截图证据仍以既有 F02/F03 manifest 为准，当前 G5 的功能 Playwright 不冒充 Windows DPI。

#### .NET 串行门禁

所有调用均通过 `run-dotnet-test-serial.ps1` 项目锁；同一 `.csproj` 没有并发。Services、Runtime/Station、
Desktop endpoint 与 Station fixture 的最终 PASS 在沙箱外正常 Windows 路径执行，未修改 ACL、产品代码或
测试断言。

| 命令 / 过滤范围 | 结果 |
| --- | --- |
| `run-tests-services-regression.ps1 -NoBuild -NoRestore` | PASS；`523/523` |
| `run-tests-phase42-regression.ps1 -NoBuild -NoRestore` | PASS；`119/119` |
| `run-tests-plc-regression.ps1 -NoBuild -NoRestore` | PASS；软件回归 `56/56`，不等于真实 PLC |
| `run-tests-desktop-endpoints.ps1 -NoBuild -NoRestore` | PASS；`430/430` |
| Product Runtime/Station 13 类单次合并过滤 | PASS；`108/108` |
| Desktop Inspection/Station 6 类单次合并过滤 | PASS；`53/53` |
| `AgentRunEndpointsTests` | PASS；`78/78` |
| AI revalidation 5 类单次合并过滤 | PASS；`114/114` |

TRX 与 classified-gate JSON 位于 `.tmp/studio-ui-next/g5-93cc88061/dotnet/`，每组均校验 total、executed、
passed、failed/error/timeout/aborted 计数；不存在零测试或仅 discovery 成功的伪 PASS。

#### 真实 WinForms / WebView2 与发布证据

| 入口与 manifest | 结果 |
| --- | --- |
| `Invoke-StudioUiProfileEvidence.ps1`；`webview2/profiles-final2/studio-ui-profile-evidence.json` | PASS；8/8：Legacy default/fallback、三个 pilot、candidate、`NEXT_DEFAULT`、missing-assets |
| `Invoke-StudioUiRollbackEvidence.ps1`；`webview2/rollback/studio-ui-rollback-evidence.json` | PASS；Next → missing-assets → Legacy → Next，同库 authority identity、唯一 root/Host owner、订阅 dispose 与数据库清理通过 |
| `Invoke-StudioUiFinalEvidence.ps1 -SoakCycles 20`；`webview2/final-final/studio-ui-final-evidence.json` | PASS；create/run/logout、reopen/delete、soak 三阶段全部 PASS；2 次重启、20/20 cycles、20 unique Results |
| `Invoke-StudioUiWebView2Matrix.ps1 -RunScope publish-only`；`webview2/publish-final/studio-ui-webview2-matrix.json` | PASS；Release 7/7：Legacy、Overview、Projects、Operators、Stations、Results、missing-assets |
| `studio-ui-no-node-evidence.json` | 本机 PASS；publish static scan、published runtime、Desktop child-process audit、sanitized-path startup 均 PASS；独立无 Node 机器未执行 |
| `studio-ui-product-performance.json` + cleanup manifest | PASS；source SHA、runtime errors、exit completion、ports、database、runtime root 与 shutdown stages 均通过 |

Final soak 在两次 warmup 后观测 18 个样本：heap 总增长 `2,141,012 B`，低于 `8 MiB` 总阈值；tail
增长 `190,688 B`，低于按跨度缩放的 `986,895 B` 持续增长阈值。DOM nodes、JS event listeners 与 document
count delta 均为 0；working set、private memory、handle count 不呈持续单调增长。GC、WeakRef、logout 后
owner/resource 释放和两个数据库删除均 PASS。

产品性能采样结果为 primary median `35.17 ms`、secondary median `40.62 ms`、route switch p95
`40.93 ms`；heap delta `+298,560 B`，CDP nodes delta `0`，CDP JS event listeners delta `0`，runtime
errors `0`。instrumentation listener `+200` 与 completed-request AbortController `+30` 是采样器/已完成请求
观测，不单独作为泄漏判据；真正的 CDP DOM/listener、timer/interval、owner ledger 与 cleanup gate 均通过。

Release publish 只进入 `.tmp/publish-check/studio-ui-next-f09/g5-93cc88061-publish-final/`。manifest 记录
publish、missing-assets、build artifacts 与 runtime directory 全部删除；复核后又删除了 harness 遗留的空 run
包装目录。发布 evidence 本身保留在 `.tmp/studio-ui-next/g5-93cc88061/webview2/publish-final/`。

#### 失败与非通过样本分类

| 样本 | 分类与处理 |
| --- | --- |
| Services 首次沙箱运行 | `BLOCKED_BY_ENVIRONMENT`；真实 AppData 写入被拒，沙箱外同组 `523/523` PASS |
| Product Runtime/Station 首次沙箱运行 | `BLOCKED_BY_ENVIRONMENT`；AppData/result-record 写入被拒，沙箱外同组 `108/108` PASS |
| Desktop endpoint 首次沙箱运行 | `BLOCKED_BY_ENVIRONMENT`；Windows Event Log 写权限被拒，沙箱外 `430/430` PASS |
| Station/Inspection 历史首次运行 | `BLOCKED_BY_ENVIRONMENT`；Station fixture/AppData 权限，最终沙箱外 6 类 `53/53` PASS |
| 旧 SHA rollback 样本 | `FAILED_THEN_FIXED_NOT_COUNTED_AS_PASS`；reconcile 采样竞态，最终 `93cc88061` manifest 使用全新目录并 PASS |
| MSBuild canonical input 漏项 | `FAILED_THEN_FIXED_NOT_COUNTED_AS_PASS`；`1c466edb3` 将输入纳入 `.csproj`，当前 build/publish PASS |
| Final runner 算子 Flyout | `FAILED_THEN_FIXED_NOT_COUNTED_AS_PASS`；旧 runner 重复点击 active trigger 导致关闭；`4dc7c7e86` 保持 Flyout，`82538e515` 验证加入后真实 detached |
| 旧 heap 门禁 | `FAILED_THEN_FIXED_NOT_COUNTED_AS_PASS`；未区分有界收敛与持续泄漏；`3824fa0b5` 增加总量与 tail span 门禁及 4 个分析测试 |
| 旧性能 runner | `FAILED_THEN_FIXED_NOT_COUNTED_AS_PASS`；未 dispose CDP handles 产生假泄漏且缺 completion signal；`93cc88061` 使用 locator/显式 handle dispose 并写 `CV_NODE_COMPLETION_SIGNAL` |
| 性能入口两次前置失败 | `PREFLIGHT_REJECTED_NOT_RUNTIME_FAILURE`；参数/环境校验阶段即退出，未启动 Desktop，不计产品性能失败 |
| Profile 沙箱入口 | `BLOCKED_BY_ENVIRONMENT`；`Win32_Process` 查询被拒，只完成前置 build；新目录沙箱外 8/8 PASS |
| 未设置 `CV_UI_SCENARIO` 的 `npm test` | `WRONG_LANE_NOT_G5_EVIDENCE`；进入 Legacy 199-test lane，外层 20 分钟超时且含 Legacy 失败标记；未计入 G5，随后正确 Next lane 164/52/0 PASS，残留 Node 进程为 0 |

任何失败目录、旧 SHA 或前置失败都没有被复用为最终 PASS manifest。

#### G5 退出与 G6 保留项

G5 的同一 SHA 本地软件门禁为 `DONE`。下列证据超出当前机器、认证和现场资源，继续保持真实状态：

| G6 项 | 当前状态 |
| --- | --- |
| Windows WebView2 125% | `NOT_PERFORMED`；本轮新增 Debug/Release 各 3 组真实 WebView2 100% size/theme/density 证据，但六组全部只观察 native 96 DPI / scale 1.0 |
| 独立无 Node 目标机 | `NOT_PERFORMED`；本机静态/进程树 PASS 不替代目标机安装、升级、卸载 |
| Remote CI clean checkout | `BLOCKED_BY_ENVIRONMENT`；无有效 GitHub 认证入口，未创建当前候选 run，普通 push 不等于完整 CI |
| 真实 Camera / PLC / TCP / Station / AI | `NOT_PERFORMED`；软件 fixture 与 endpoint 回归不替代现场设备/模型 |
| 长时间生产 soak | `NOT_PERFORMED`；本机 20-cycle bounded soak 仅为软件门禁 |
| 产品 Owner 签收 | `NOT_PERFORMED` |

因此 `FINAL_GATE=PARTIAL`、`PRODUCTION_ACCEPTANCE=NOT_GRANTED`、`LEGACY_RETIREMENT=NOT_APPROVED` 保持不变。
G6 当前为 `BLOCKED_BY_ENVIRONMENT`，需要 Release/Field/Product Owner 在目标环境继续执行。

### G3 产品体验、视觉、中文与 Vue 工程收口

- G3 产品实现提交为 `a3c043e77ff9bcbc80fbf638f8f9f52a217fa8a8`；Station 健康态 evidence fixture
  修正提交为 `1c6e61e5a53d59ac3a7f78054af5eab3e86ec667`。后者只把健康 Station 的待回放计数归零，
  未放宽产品异常排序或测试断言。
- Results 和 Stations 均建立“态势总览 / 调查详情”两层视图；Projects 宽屏与最近工程密度完成收口；
  Workspace、Results 短屏、长中文、命中区、菜单 Escape/点击外部/焦点返回与 viewport 约束已覆盖。
- Diagnostics/About 改为投影真实产品、宿主和后端版本；用户可见研发语言已清理。Results、AI Settings、
  WorkspaceShell、TCP Settings 与 Projects 的展示责任已拆分，未复制 capability owner、状态树或写入口。
- 新增组件均为展示或窄交互边界：`WorkspaceCommandBar`、`ProjectsRecentPanel`、
  `ResultsSituationSummary`、`SettingsAiModelCatalog`、`SettingsTcpProfileList`、`CvViewTabs`。
- 架构审计未发现第二 API transport、EventBus、ServiceRegistry、Canvas kernel、HostBridge 执行通道、
  Project 保存链、query owner 或 write owner；Project/Flow/Runtime/Station/Results authority 未改变。

#### G3 checkpoint 验证

| 证据 | 状态 | 当前结果与边界 |
| --- | --- | --- |
| Impeccable detector | `PASS` | 当前 StudioUI `src` 扫描结果 `[]`。 |
| Studio UI lint / typecheck | `PASS` | `npm run lint` 与 `npm run typecheck` 通过。 |
| Studio UI full unit | `PASS` | `139` 个文件、`900/900` 个测试通过。 |
| F02 affected without formal evidence | `PASS_WITH_GATED_SKIP` | `18` 个通过，`49` 个按 evidence gate 跳过；随后在同一 SHA 运行正式 evidence。 |
| F02 Browser evidence | `PASS_BROWSER_FIXTURE` | `.tmp/studio-ui-next/f02-1/g3-1c6e61e5a/`；`73` PNG + `73` JSON，全部绑定 `1c6e61e5a`；最大水平 overflow `0`、runtime error `0`、截图哈希错误 `0`、适用页面 viewport/theme/density drift `0`。 |
| F03 Workspace evidence | `PASS_BROWSER_FIXTURE` | `.tmp/studio-ui-next/f03/g3-1c6e61e5a/`；`12` PNG + `12` JSON，绑定当前 SHA 与 stable audit SHA `e76c74e3`；水平/垂直 overflow、runtime error、allowlist failure、owner conflict、viewport drift、截图哈希错误均为 `0`。 |
| WebView2 host targeted | `PASS` | `WebView2HostTests -NoBuild -NoRestore`：`66/66`。这是宿主代码测试，不是手工真实 WebView2/DPI 验收。 |
| 视觉复审 | `PASS_BROWSER_DIRECTIONAL` | 已复审 Projects、Results、Stations、Diagnostics、About、Station Admin 与 Workspace 的 light/dark、compact/comfortable、1920x1080/1536x864/1366x768 代表截图；未发现遮挡、截断、非预期滚动或卡片套卡片。 |
| Windows DPI / native WebView2 | `NOT_PERFORMED`（G3 当时） | F02/F03 均明确记录 `BROWSER_FIXTURE`、`HARNESS_SEEDED_SESSION` 与 `windowsDpi/nativeDpi=NOT_PERFORMED`；当前 G5 已取得本机 Debug/Release 100% 自动证据，仍不把方向性 fixture 冒充 Windows 125%。 |

G3 退出条件已满足并解锁 G4。浏览器 fixture 只承担方向性视觉、交互和 owner 投影证据。当前 G5 已补齐
本机真实 WebView2 Debug/Release 100%；Windows 125%、独立 no-Node 目标机、现场硬件、Remote CI、生产 soak
与产品 Owner 签收继续留在 G6，`PRODUCTION_ACCEPTANCE=NOT_GRANTED`、`LEGACY_RETIREMENT=NOT_APPROVED`
保持不变。

### G4 Legacy profile 隔离、rollback 与退役准备

- Host/profile 隔离实现提交为 `245e9cec9398cbcc2bc42d3d3cc79176634a76bb`；真实 WebView2 harness 的
  G3 启动字段与只读请求合同漂移分别在 `893159d88`、`1c8ad67f3` 修正。最终证据只绑定后者，
  两个失败尝试不作为通过证据。
- `Program.UseDesktopStaticAssets` 在进程启动时解析一次受验证的 Startup Profile。Next 只挂载 `/studio`
  provider，Legacy `index.html`、`src/app.js` 返回 404；Legacy 只挂载根 provider 并明确排除 `/studio`。
  Next 资源缺失时不挂载 Legacy provider，由现有 `StudioStartupPageResolver` fail-closed 到诊断页。
- `MainForm` 每进程只解析一个 `IDesktopWebMessageOwner`：Next 为 `StudioHostCapabilityMessageHandler`，
  精确白名单只有 `PickFileCommand`；Legacy 才解析完整 `WebMessageHandler` compatibility chain。
  `WebView2Host` 中失效的第二消息分发器、事件与发送入口已删除；没有新增 HostBridge 或执行旁路。
- StudioUI 的 canonical dependency inventory 固定为 Canvas/interaction、Preview formatter/coordinator、
  ImageCanvas、ROI geometry/editor、pixel probe、parameter dependency、operator visual/feature registry 与其
  UI/logging 支撑模块。`.csproj` 与 Vite alias guard 明确排除 Legacy `wwwroot/index.html` 和
  `wwwroot/src/app.js` composition root；canonical FlowCanvas/ImageCanvas 仍各只有一个内核与 mounted owner。
- 当前 fallback 入口为 `Studio:StartupProfile=LEGACY_FALLBACK` 或环境变量
  `Studio__StartupProfile=LEGACY_FALLBACK`。适用范围仅为受控恢复与历史兼容：先停止当前 Host，保留数据库、
  Project、运行包、Result 与 diagnostics，修改同一权威配置后重启，核对启动日志 profile、唯一 Legacy root、
  `legacy-compatibility` owner 与同一 Project/PersistenceRevision；问题解决后以同样方式重启回
  `NEXT_DEFAULT`。profile 切换不要求修改源码、重编译或新增数据迁移/双写。
- Legacy 源码与打包资产继续保留。物理删除必须等 G6 的完整 WebView2/DPI、独立 no-Node、Remote CI、
  现场 Camera/PLC/Station、生产 soak 与产品 Owner 签收全部取得证据，并另行完成 capability disposition、
  数据/客户支持迁移方案、独立 ADR、回退窗口、备份策略和发布审批；当前
  `LEGACY_RETIREMENT=NOT_APPROVED`。

#### G4 checkpoint 验证

| 证据 | 状态 | 当前结果与边界 |
| --- | --- | --- |
| Desktop profile/Host 定向 | `PASS` | 单次串行调用覆盖 `ProgramStaticAssetsTests`、profile isolation architecture、`WebView2HostTests`、`WebMessageHandlerTests`：`89/89`；NuGet vulnerability feed 在受限网络下产生 `NU1900`，锁定依赖构建与测试通过。 |
| PowerShell / Node runner syntax | `PASS` | 两个 evidence PowerShell 脚本 parser error `0`；`studio-ui-webview2-smoke.cjs` 通过 `node --check`。 |
| 最终 rollback manifest | `PASS_REAL_WEBVIEW2_DEBUG_100_AUTOMATED` | `.tmp/studio-ui-next/f09/rollback/g4-1c8ad67f3/studio-ui-rollback-evidence.json`；`sourceSha=1c8ad67f3a890ed0a8cd72702cef82ed9623f367`，顺序为 Next → 缺失资源诊断 → Legacy → Next，四阶段 runtime/cleanup 均 PASS。 |
| Authority identity | `PASS` | 同一 Project、`PersistenceRevision=4`、Flow、正式 Result、ExecutionSnapshot、flow/decision hash、image reference 与 history identity 跨重启一致；无 migration、无 dual-write，隔离数据库最终删除。 |
| Host owner lifecycle | `PASS` | Next/诊断挂载 `studio-host-capabilities`，active subscription `1 -> 0`；Legacy 挂载 `legacy-compatibility`，active subscription `4 -> 0`；每次进程退出、端口、WebView2 user-data 与请求资源清理通过。 |
| 失败样本 | `FAILED_THEN_FIXED_NOT_COUNTED_AS_PASS` | `g4-245e9cec9` 因 G3 新增 product/host version 后 smoke 旧字段集失败；`g4-893159d88` 因 Template/Results 正式只读请求未进入旧 allowlist 失败。两者均在首阶段停止并完成清理，最终 manifest 未复用其数据。 |
| G6 外部环境 | `PARTIAL` | G4 当时只覆盖本机 Debug、scale 1.0 自动化；当前 G5 已补齐本机 Debug/Release scale 1.0。Windows 125%、独立 no-Node 目标机、Remote CI、现场硬件、长时间生产 soak 与产品 Owner 签收仍未执行。 |

G4 退出条件已满足并解锁 G5。该结论只批准继续收集同一 clean SHA 的本地软件证据，不授予生产接受，
也不批准 Legacy 物理退役。

### G1 请求/写入生命周期与跨工程状态安全

- G1 实现 checkpoint 为 `98cb8c7f54d2d51ea5b59ca534aafd51544b773f`，已进入当前分支历史；本节保留该 checkpoint 当时的验证与边界，不把历史结果冒充当前 G5 或生产验收。
- Workspace diagnostics capability 账本已覆盖最终判定、运行包、线序、标定和 handoff；GET/read 在 route、工程、session、flag 变化及 dispose 时 abort，晚到响应按 owner/workspace identity 丢弃，request/controller/timer/subscription 资源在释放后归零。
- Project save、Global Variables、Template、Camera binding、runtime package、final decision、line sequence、calibration、AI handoff、新工程 draft 与 handoff receiver 均接入唯一 owner 的 pending/committed/rejected/unknown-outcome/reconciled 语义；服务端无 operation identity 的未知写入不自动重放，并由 leave guard 阻断离开直到可协调。
- WorkspaceRuntime 统一纳入正式 workspace owner、新工程 draft、handoff receiver 和 FlowCanvas lifecycle participant；跨工程切换会关闭旧 popup、释放旧 owner，并防止旧工程响应投影到新工程。Inspection 页面显式区分 `unauthorized`、`forbidden`、`aborted`、`stale` 与 `partial-failure`。
- Cleanup 与写入边界见 [ADR-G1：Cleanup 与写入生命周期](./ADR-G1-Cleanup与写入生命周期.md)。本轮未新增 Project save endpoint、HTTP transport、EventBus、Canvas kernel、HostBridge 或前端持久化 authority。
- Camera continuous-preview 的 stop 只由 camera owner 发起一次；dispose 后 diagnostics lease 保持非零直到有界 cleanup settle，失败不自动重试并阻断 leave。

#### G1 checkpoint 验证

| 证据 | 状态 | 当前结果与边界 |
| --- | --- | --- |
| `npm run lint` | `PASS` | G1 checkpoint 工作树通过。 |
| `npm run typecheck` | `PASS` | G1 checkpoint 工作树通过。 |
| `npm run test:unit` | `PASS` | Studio UI 全量 `139` 个文件、`891/891` 个测试通过。 |
| G1 生命周期定向测试 | `PASS` | `13` 个文件、`103/103`；覆盖 runtime、handoff/new-draft、leave guard、read/query、camera cleanup 与 diagnostics 生命周期语义。 |
| `npm run build` | `PASS` | Vite 转换 `511` modules；仅保留既有 chunk size warning。 |
| `git diff --check` | `PASS` | 文档回写完成后重新复核。 |
| Remote CI | `NOT_PERFORMED` / `BLOCKED_BY_ENVIRONMENT` | 当前无有效 GitHub 认证入口，未创建 remote run，未修改 trigger，未跳过 required job。 |
| WebView2 100% / 125% | `NOT_PERFORMED`（G1 当时） | G1 checkpoint 未以 Chromium/fixture 证据替代真实 WebView2 与 Windows DPI；当前 G5 已取得本机 Debug/Release 100% 自动证据，125% 仍未执行。 |
| Independent no-Node | `NOT_PERFORMED` | 未执行独立目标机发布启动验证。 |
| Camera / PLC / Station | `NOT_PERFORMED` | 当前未取得现场硬件与 Station 联调证据。 |
| Production soak | `NOT_PERFORMED` | 未执行生产环境 soak；`PRODUCTION_ACCEPTANCE` 仍为 `NOT_GRANTED`。 |

### G2 合同解阻与功能差距（2026-08-09）

- G2 实现与 G1 生命周期收口共同冻结在 `98cb8c7f54d2d51ea5b59ca534aafd51544b773f`；
  该 checkpoint 已进入当前分支历史。本节保留 G2 当时的验证，不把它写成当前 G5 或生产候选。
- GlobalVariables 由同一 `WorkspaceGlobalVariablesOwner` 读取当前 Flow draft，工作台只展示可映射到四种标量
  类型的端口/参数；应用前重新校验算子、端口、参数 identity 和兼容性，错误码为 `GV009/GV010/GV011/GV014/GV015`。
  正式定义/绑定仍进入既有 Project save chain，运行值仍使用后端 version 和 G1 unknown/reconcile 语义。
- Line Sequence 继续使用现有 AutoTune preview/scenario endpoint。输入优先取同一 Preview owner 的当前输入图，
  其次取当前输出图，预览 stale/未完成时不发送旧图；owner 解码并投影输入图、返回预览图、Outputs 和 scenario
  `FinalPreview`。Apply 仍只修改 canonical Flow draft，不写设备、不保存工程。
- Line Sequence 的 AI parameter-only follow-up 因缺少已批准的跨 capability composer/queue 合同而 `DEFER`；
  不新增 AI session、第二 endpoint 或 localStorage 队列。
- 其余 G2.1-G2.5、G2.7、G2.9 及高级能力的 owner、权限、并发身份、错误和 reconcile 处置见
  [ADR-G2：合同解阻与能力处置](./ADR-G2-合同解阻与能力处置.md)。主协调 Owner 已冻结
  `DEFER/RELOCATE` 边界，G2 合同决策 Gate 为 `DONE`；延期能力未迁移，未来重新进入仍需审批。

#### G2 checkpoint 验证

| 证据 | 状态 | 当前结果与边界 |
| --- | --- | --- |
| Studio UI typecheck | `PASS` | G2 checkpoint 工作树 `npm run typecheck` 通过。 |
| GlobalVariables owner targeted | `PASS` | `7/7`；覆盖类型兼容、Flow identity、运行值和 dispose 生命周期。 |
| Line Sequence owner/contract targeted | `PASS` | `11/11`；覆盖 InputImageBase64、返回预览图、FinalPreview、stale 和晚到响应。 |
| Studio UI lint | `PASS` | G2 checkpoint 工作树 `npm run lint` 通过，`--max-warnings=0`。 |
| Studio UI full unit | `PASS` | `139` 个文件、`891/891` 个测试通过。 |
| Studio UI build | `PASS` | Vite 转换 `511` modules；保留既有 chunk size warning。 |
| Product/Desktop/.NET endpoint tests | `NOT RUN` | 本轮未修改后端 endpoint；沿用既有证据，不外推到新工作树。 |
| Browser / Playwright | `NOT RUN` | 尚未为 G2 新增 journey；不以 unit 结果替代 Chromium 或真实宿主。 |
| WebView2 / DPI / no-Node / Remote CI / 现场硬件 | `NOT_PERFORMED` | 环境不具备或尚未执行，仍不授予生产验收。 |

### AI Resource Contract

- 可复用：Camera resource 已有 identity、revision、binding decision 与工程关联合同，不需要新建前端资源库。
- `model_resource=DEFERRED`：CV 算子消费 `ModelPath/ModelId`；`/api/ai/models` 是 LLM provider/model 配置，不是视觉模型 asset authority。
- `template_artifact=DEFERRED`：`/api/templates` 拥有 flow template，不拥有 TemplateMatching 图像模板产物。
- `calibration_resource=DEFERRED`：Project assets 拥有正式 calibration bundle，但 AI/UnitConvert 当前消费 numeric `Scale`，尚无权威 asset-to-scale projection。
- `attachment_resource=DEFERRED`：Legacy 本地路径只是主机路径；AgentRun 会剥离路径，当前没有上传、版本、权限和 resource reference store。
- 因此本轮不把本地路径冒充正式资源，不为 model/template/calibration 创建前端私有绑定。

### Line Sequence authority 与闭环

- Analyze authority：`POST /api/autotune/flow-node/preview`，要求 Engineer/Admin，使用 `ExecutionAdmissionSurface.AutoTunePreview` 拒绝真实外部副作用。
- Recommendation authority：`POST /api/autotune/scenario`，仅接受 `wire-sequence-terminal`，迭代限制为 1..5 轮；允许 flow-owned File 输入，非收敛但已有迭代结果时返回可审查 recommendation。
- Apply authority：Next 只通过 `FlowCanvasOwner.commands.patchNodeParameters` 修改最近上游 `BoxNms`/`DeepLearning` 草稿；白名单仅含 `ScoreThreshold`、`IouThreshold`、`Confidence`，值必须是 `[0,1]` 内有限数。
- Formal save authority：仍为现有 Project save chain；Analyze/Recommendation/Apply 不写设备、不保存 Project、不实现生产算法。如未来需要设备 Apply，必须进入现有 command authority 并补身份、冲突、unknown-outcome/reconcile，不在 Vue 内扩展。
- Lifecycle：owner 在节点选择、flow revision 变化或 dispose 时 abort/丢弃旧响应；保留 `stale` 以防止过期 recommendation Apply。持久化数值算子 identity（`61/140/150`）已纳入 owner 与工作台识别。

### Browser evidence

- Chromium/fixture 定向组 `7/7` 通过：Project canonical JSON import/export、Results server-side full-batch export/download、Station lost-response + duplicate lock + request identity reconcile、Template apply-to-draft、N Point solve + Project asset save/reconcile、Line Sequence Analyze/Recommendation/Apply，以及 draft stale/backend safety rejection。
- 该证据只代表 Playwright Chromium + fixture contract，不代表真实 WebView2、DPI、Desktop endpoint 联调或现场设备。

### Remote CI / Final Gate（当前 G5 候选）

- `93cc88061` 是当前 G5 实现与证据候选；实现/evidence commit 与本次文档提交分离，最终 fetch 审计与普通 push 另行记录。
- `ci.yml` 存在 `workflow_dispatch`，但 `gh auth status` 显示 token 失效；内置浏览器登录页已打开后按用户要求先跳过 GitHub 登录。本轮未创建 remote run，未修改 trigger，未跳过 required job。
- `FINAL_GATE=PARTIAL`：当前 implementation SHA 的 G5 本地软件 gates 已通过；clean-checkout Remote CI、真实 Windows 125%、独立无 Node 目标机、现场硬件、长时间生产 soak 与产品 Owner 签收仍缺失。本机真实 WebView2 Debug/Release 100% 不再列为缺失项。

## 前序工作记录（历史 checkpoint）

### G0 Remote CI（前序 checkpoint）

- 已执行 `git fetch origin --prune`，远端 `studio-ui-next` 与本地基线一致。
- 已审计 workflow 触发条件；普通 `studio-ui-next` push 不触发完整 CI。
- 当前 `REMOTE_CI=NOT_PERFORMED`，不修改 workflow trigger，不合并，不改 main。

### G1 Calibration authorization

- `POST /api/calibration/npoint-draft/solve` 复用 `RequireEngineerOrAdmin`，拒绝 Operator 和未认证会话；空 `ProjectId` 返回 `PROJECT_CONTEXT_REQUIRED`，不存在工程返回 `PROJECT_NOT_FOUND`。
- draft solve 仍只产生 draft/candidate/preview artifact，不写正式 Project asset；正式保存继续走既有 `CanEditProject` 和 `ProjectService` 链。
- `CalibrationDraftEndpointsTests` `8/8`、`NPointCalibrationSolverTests` `10/10`、`NPointCalibrationOperatorTests` `12/12`、Planar service 纯计算 `2/2` 通过；`calibrationOwner` 对 solve 与 formal save 的 `403` 提示分别投影，当前 calibration contract/owner UI tests `10/10`。
- `ScaleOffset` 走 `draft -> solve -> candidate -> Project asset save`；没有新增第二 calibration authority 或第二保存协议。直接 `PlanarScaleOffsetCalibrationService.SaveCalibrationAsync` 测试在当前沙箱无法写真实 `%APPDATA%\\ClearVision\\calibration`，状态保留为环境限制。

### G1 Project lifecycle import/export

- formal persisted export 使用 dedicated `ProjectExportDocumentV1` projection；import 明确区分 `CREATE_NEW` 与 `OVERWRITE_EXISTING`，由后端验证 schema/version、权限、operator/parameter compatibility、revision 和 operation identity。
- create/overwrite、invalid document no-mutation、unknown operator/parameter、Operator denial、export permission、replay/reconcile 由现有 Desktop endpoint 与 lifecycle service tests 覆盖；没有新增第二 Project repository、第二 save chain 或前端私有持久化。

### G2 Results bulk export

- `ResultsExportJobService` 复用现有 `IResultAnalysisService` 生成服务端 CSV/JSON；作业状态、稳定 snapshot upper bound、取消 token、artifact TTL、SHA-256 和 `clientOperationId` fingerprint 均由 Desktop 进程内唯一 owner 管理，不创建第二 Results 持久化权威。
- `ResultsExportEndpoints` 提供创建、状态、按操作身份对账、取消和下载入口；沿用 Engineer/Admin 权限策略，Station source 在服务端拒绝，过期产物返回明确 `410`。
- Results UI 只在本机结果页创建 `resultsExportOwner`，把当前工程、时间、结果状态、缺陷类型和诊断码作为范围发送；网络未知结果只允许按操作身份对账，不自动重发创建请求，切换 capability 时 dispose 请求、timer 和 controller。
- 已通过 `ResultsExportJobServiceTests` `5/5`、`ResultsExportEndpointsTests` `4/4`、Studio UI 全量 `137/137` 文件和 `859/859` 测试；本轮另有 Results 分析工程切换与 Global Variables runtime-read 生命周期竞态测试通过。Browser / Playwright、真实 WebView2 和生产环境证据仍未执行。

### G3 Station package and command evidence

- 当前 backend 已具备 `clientRequestId` 幂等、同请求查询、过期命令收敛、Station admin 权限、正式包身份完整性与部署准入检查。
- `StationEndpointsTests` 在 `Logging__EventLog__LogLevel__Default=None` 且允许既有 AppData fixture 写入的受控进程环境下 `30/30` 通过；默认沙箱首次失败归类为 EventLog/AppData 权限，不改产品代码或目录 ACL。
- Next 只读 Station projection 保留 submitting/unknown/reconciling 与 command-created/in-progress/terminal-failed/awaiting-active-identity/identity-mismatch/succeeded 状态；owner dispose 会把未决提交或核对转为 unknown-outcome，核对期间 reset/重复提交被拒绝。这不等同于现场 Station、PLC、Camera 或完整 unknown-outcome soak 已验收。

### Contract blockers retained

- AI attachment、CV model artifact、TemplateMatching artifact、calibration asset-to-scale projection 与 Advanced Settings 仍按合同缺口记录；G2 ADR 已补齐 owner、权限、并发身份、错误/reconcile、fallback 和重新进入条件，不新增第二套 authority。
- GlobalVariables 类型/identity 校验与 Line Sequence Preview 输入/返回图已进入 `98cb8c7f5`；Line Sequence AI parameter-only follow-up、通用 AutoTune 入口和 N 点高级工作流仍按 ADR 延期。
- Line Sequence 软件闭环仍不包含设备写入；当前 G5 已取得本机真实 WebView2 Debug/Release 100% 自动证据。Remote CI、Windows 125%、独立 no-Node 目标机、现场硬件与生产 soak 仍未取得证据。

## 测试与真实环境（前序 F10 checkpoint；当前 G5 证据见上节）

> 本节保留前序 F10 的历史证据，不把旧 checkpoint 的数量或通过结论外推到当前实现 SHA。下表 WebView2 100% 的 `NOT_PERFORMED` 是前序状态；当前 G5 状态以本页上方 G5 证据为准。

| 证据 | 状态 |
| --- | --- |
| `npm run lint` | PASS |
| `npm run typecheck` | PASS |
| `npm run test:unit` | PASS；全量 `138` 个文件、`869` 个测试 |
| `npm run build` | PASS；Vite 转换 `509` modules |
| Results Application targeted | PASS；`ResultsExportJobServiceTests` `5/5` |
| Results Desktop targeted | PASS；`ResultsExportEndpointsTests` `4/4` |
| Project lifecycle targeted | PASS；create/overwrite/reconcile/validation/permission tests 已随 `eaafef9f0` checkpoint 通过 |
| Calibration solver/operator targeted | PASS；`10/10` + `12/12` |
| Calibration Desktop endpoint targeted | PASS；`8/8` |
| Planar service solve targeted | PASS；`2/2`；legacy file-save test `BLOCKED_BY_ENVIRONMENT`（真实 AppData 无写权限） |
| Line Sequence owner targeted | PASS；`9/9`，连同 Calibration owner 定向组 `16/16` |
| Browser / Playwright | PASS；F10 Chromium/fixture 定向 journey `7/7` |
| Product/Desktop build | PASS；Desktop targeted test invocation 完成 build，0 error；观察到环境 `NU1900` vulnerability-feed unavailable warning |
| Product/Desktop targeted | PASS；`AutoTuneEndpointsTests` `10/10`；`AutoTuneServiceTests` + `ExecutionAdmissionServiceTests` 合并 `101/101` |
| `git diff --check` | PASS |
| Remote CI | BLOCKED_BY_ENVIRONMENT；workflow 可 dispatch，但当前无有效 GitHub 认证入口，未创建 run |
| Final Gate | PARTIAL；本地 gates 通过，remote clean-checkout 与真实环境未通过 |
| WebView2 100% | NOT_PERFORMED |
| WebView2 125% | NOT_PERFORMED |
| Independent no-Node | NOT_PERFORMED |
| Camera / PLC / Station | NOT_PERFORMED |
| Production soak | NOT_PERFORMED |

## 提交

当前 G5 implementation/evidence checkpoint：`93cc88061`。历史 checkpoint：G4 `1c8ad67f3`，G1/G2 `98cb8c7f5`，G0 `21105d57d`；更早 checkpoint 为 `026768cf4`、`1af7b2ec6`、`8846c52e4`、`d469a4740`。文档提交单独记录并随本分支普通推送；提交、推送和软件测试都不会自动授予生产验收。
