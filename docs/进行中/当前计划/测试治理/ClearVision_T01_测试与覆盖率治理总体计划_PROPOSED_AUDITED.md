# ClearVision T01 测试与覆盖率治理总体计划（PROPOSED / AUDITED）

> 审计日期：2026-07-30（Asia/Shanghai）
> 原始审计基线：`origin/codex初稿@bea404394ac8cf403cca719c1990c426414a06c2`
> 2026-08-28 当前性复核代码基线：`c504ae1919cca0ff4df993c956c45742440fc471`（其后 `78d693fb4` 仅含文档治理）
> 文档性质：审计结论与分阶段计划；本轮没有实施正式治理改造
> 实施状态（2026-08-28）：G01 的覆盖率与专项 Gate 主体已形成阶段证据；历史 UI E2E 契约遗留已转入独立 R3 计划。G02-G09 不声明闭环。
> 横向责任入口（2026-08-28）：G01B-R3 与 G02-G09 已去重登记在 [ClearVision 未尽事项统一补齐 TODO](../ClearVision-未尽事项统一补齐TODO-2026-08-28.md) U01/U03/U05-U07；本文继续作为测试治理专项规格，不归档。
> 当前分支架构纠偏（2026-08-28）：第 1.3 节记录的 `studio-ui-next` 是另一条缓存分支，不是 `codex初稿` 当前事实。当前分支仍使用 `wwwroot/index.html + app.js + capability owners`，存在 non-production `FrontendV2`，不存在 `Desktop/StudioUI`。第 7 节及 G03-G07 已按当前代码重写；旧分支上的删除、路径和测试命令不得作为本分支执行指令。
> 时间面说明：第 1-6、12 节中未带“2026-08-28 当前性复核”标记的 `当前 HEAD`/`VERIFIED` 均指原始审计基线 `bea404...`；第 7-11 节的 disposition 和当前执行边界以 `c504ae...` 为准。两种证据不得交叉替代。

文档采用 `docs/进行中/当前计划/测试治理/`，而非直接放在 `docs/进行中/测试治理/`，因为仓库现行文档治理规则要求项目级 active 计划统一进入“当前计划”并由其 README 建立唯一入口。

当前执行责任入口为 [T01-G01B-R3 UI 契约回归闭环计划](./ClearVision_T01_G01B_R3_UI契约回归闭环计划_2026-08-28.md)。G01A-R2/R3、G01B-R1/R2 的历史实现与 CI 报告已迁入 [T01-G01 测试治理阶段归档](../../../归档/已关闭事项/2026-08-28-T01-G01-测试治理阶段归档/闭环说明.md)，不得用历史结果替代当前 HEAD 证据。

## 0. 证据状态与口径

本文中的审计结论统一使用以下状态：

- `VERIFIED`：由当前 Git 对象、当前 HEAD 的新鲜产物、可解析测试结果或 GitHub Actions 当前 HEAD 运行直接证明。
- `PARTIALLY_VERIFIED`：部分链路有直接证据，但范围、环境、模块集合或端到端闭环不完整。
- `INFERRED`：由代码结构、配置或静态模式推断，尚未用目标运行闭环。
- `UNKNOWN`：当前无法可靠验证，或现有证据不能绑定当前 HEAD。

计划中的目标、任务和完成标准均是未来条件，不使用上述状态冒充已完成事实。覆盖率百分比只在同时明确测试 Lane、程序集范围、SHA 和产物新鲜度时引用；仓库中历史报告不得替代当前 HEAD 结果。

## 1. Git 与审计基线

### 1.1 主工作树

| 项目 | 证据 | 状态 |
| --- | --- | --- |
| 工作树路径 | `C:\Users\HerverJun\Desktop\ClearVision` | `VERIFIED` |
| 当前分支 | `codex初稿` | `VERIFIED` |
| Initial HEAD | `bea404394ac8cf403cca719c1990c426414a06c2` | `VERIFIED` |
| 本地 tracking ref | `origin/codex初稿`，本地记录 SHA 为 `bea404394ac8cf403cca719c1990c426414a06c2` | `VERIFIED` |
| 远端最新 SHA | `git ls-remote` 返回 `refs/heads/codex初稿 = bea404394ac8cf403cca719c1990c426414a06c2` | `VERIFIED` |
| HEAD 与远端关系 | HEAD、本地 tracking SHA、远端 SHA 三者一致 | `VERIFIED` |
| 工作树洁净度 | 审计开始时不干净：12 个已跟踪修改、3 个未跟踪文件 | `VERIFIED` |
| 其他 Agent 在途改动 | 涉及 Preview/Observation 产品代码、Desktop/UI 测试及一份未跟踪分析报告；本轮未覆盖、回滚、暂存或提交 | `VERIFIED` |
| 本轮代码基线隔离 | 静态审计使用 Git 对象；执行验证使用仓库外 `bea40439...` archive 快照，未把主工作树改动计入结果 | `VERIFIED` |

主工作树在途文件不属于本计划交付物。本轮只允许计划文档及索引进入最终 diff。

### 1.2 `codex初稿` 与 `main`

| 项目 | 证据 | 状态 |
| --- | --- | --- |
| `origin/main` | `f4d392e2147adf175a2f8faa7d7c09b3d906ba8a` | `VERIFIED` |
| merge-base | `bf568d15745be258383e9d6f144ae3f89288077e` | `VERIFIED` |
| 分叉计数 | 相对 `main...codex初稿`：仅 main 1 个提交，仅 codex初稿 444 个提交 | `VERIFIED` |
| 祖先关系 | 两者均不是对方祖先；不能把 main 的 CI 结果视为 codex初稿结果 | `VERIFIED` |

### 1.3 前端迁移工作树（只读）

| 项目 | 证据 | 状态 |
| --- | --- | --- |
| 路径 / 分支 | `C:\Users\HerverJun\Desktop\ClearVision-UI-Next` / `studio-ui-next` | `VERIFIED` |
| 审计开始 HEAD | `85f79bc59d6495360905a09a3e3b8a55e95aeb16`，相对 `origin/studio-ui-next` ahead 1 | `VERIFIED` |
| 洁净度 | 不干净；审计期间仍有 Agent 持续修改 StudioUI、AgentRun、测试、锁文件和报告 | `VERIFIED` |
| 本轮写入 | 未修改该工作树 | `VERIFIED` |
| 与 codex初稿共同祖先 | `e1bad492fecb6dff2c0a8f848db9ebfa18acf093` | `VERIFIED` |
| 提交分叉 | 相对 `codex初稿...studio-ui-next`：43 / 187 | `VERIFIED` |

## 2. 当前测试体系全景

### 2.1 可执行测试工程与类型

| 资产 | 当前覆盖内容 | 当前规模/入口 | 状态 |
| --- | --- | --- | --- |
| Product xUnit | Core、Application、Infrastructure、Runtime、PLC、流程执行、项目持久化、算法算子、Vision Agent | `ClearVision.Product.Tests.csproj`；治理扫描 3121 个源码测试定义，其中 PR 2060、Nightly 1043、ReleaseManual 18 | `VERIFIED` |
| Desktop xUnit | HTTP/SignalR 端点、Desktop 宿主、Preview、Station、设置、WebView2、AgentRun | `ClearVision.Product.Desktop.Tests.csproj`；550 个源码测试定义，均属 PR Lane | `VERIFIED` |
| OperatorLibrary smoke | 已打包 NuGet 的契约、实例化、命名空间和代表性算子验收 | `ClearVision.OperatorLibrary.SmokeTests.csproj`；37 个源码测试定义 | `VERIFIED` |
| Legacy UI unit | `wwwroot` 模块级状态机、AI、Preview、ROI、结果、项目等 | Node test runner；最低 25 文件/650 用例；当前发现并执行 978 个 | `VERIFIED` |
| Legacy UI E2E | 旧页面、Preview/ROI、FinalDecision、Station、AI、视觉快照 | Playwright；26 文件、204 个已发现用例 | `VERIFIED` |
| FrontendV2 unit | 旧 Studio2 foundation/port 原型 | Vitest；当前 8 文件、43 用例 | `VERIFIED` |
| Vision Agent quality | 固定 mock/scripted Agent 工程子集、UI contract、端点、业务 benchmark、RuntimePreview metadata | `agent_engineering_harness_suite`，6 active + 1 manual，声明 active 预算约 1.6 分钟 | `VERIFIED` |
| 算法质量套件 | Quick contracts、Golden Core50、公开数据、重型数据集、Core20、Field Replay、性能/精度 runner | `quality/evals/suites/*.json` 共 10 套 manifest；active/manual/planned/blocked 分层 | `VERIFIED` |
| Python 工具自测 | suite/public benchmark/quasi-industrial 工具逻辑 | `quality/tools/tests` | `VERIFIED` |

### 2.2 分类治理

`run-test-governance.ps1 -FailOnWarning` 在当前 HEAD 快照产生以下结果：

| 指标 | 结果 | 状态 |
| --- | ---: | --- |
| xUnit 源码测试定义 | 3708 | `VERIFIED` |
| 未分类 | 0 | `VERIFIED` |
| Error / Warning | 0 / 0 | `VERIFIED` |
| PR / Nightly / ReleaseManual | 2647 / 1043 / 18 | `VERIFIED` |
| Regression | 3378 | `VERIFIED` |
| Accuracy / Determinism / Stability / Performance / Robustness | 6 / 19 / 3 / 23 / 2 | `VERIFIED` |

`TestClassificationAttribute` 要求 Domain、Purpose、Lane、EvidenceType、OracleType、ResourceRequirement、ExpectedDuration、FlakyPolicy、Owner 九个维度。治理程序能拒绝未分类、PR 资源依赖、PR 非 blocking flaky policy、未受控 `Random`、Accuracy 无独立 Oracle、Determinism 无 seed、Stability 无统计/变形 Oracle、Performance 无预算描述等情况。

`PARTIALLY_VERIFIED`：该机制能验证“声明与源码模式”一致，但不能自动证明标注的独立 Oracle、Golden 数据、Owner 或性能预算真的具有业务质量。例如 Accuracy 的弱断言检查依赖源码模式，无法替代逐测试审查。

### 2.3 Gate 配置与最低数量

`quality/test-gates.json` 定义 Product、Desktop、OperatorLibrary 的 PR/Nightly/ReleaseManual Gate。

- `VERIFIED`：所有 31 个分类 Gate 的 `minimumTotalTests` 都是 1。
- `VERIFIED`：Legacy UI unit 有 25 文件/650 用例下限，Agent UI contract 有 340 用例下限。
- `VERIFIED`：Vision Agent .NET 子集命令要求至少 560 通过，AI model endpoint 要求至少 42 通过。
- `VERIFIED`：FrontendV2 仅 `passWithNoTests: false`，没有 43 用例的防回退基线。
- `VERIFIED`：Playwright 没有 204 用例的数量基线。
- `PARTIALLY_VERIFIED`：测试数量防误删在 UI/Agent 特定入口有效，但主 Product/Desktop/OperatorLibrary Gate 只保证“至少还有一个测试”，不能阻止大规模误删。

## 3. CI 全景与当前 HEAD 实际状态

### 3.1 工作流触发与能力

| 工作流 | `codex初稿` push | 测试/证据 | 覆盖率 | 当前结论 |
| --- | --- | --- | --- | --- |
| `.github/workflows/ci.yml` | 否，仅 `main`/`develop` | Product/Desktop PR、OperatorLibrary 包烟测、Quick quality、全 Legacy UI unit/E2E；Nightly/Manual 另分 Lane | Product/Desktop 生成 Cobertura，OperatorLibrary 生成 VS coverage | `VERIFIED`：不保护 codex初稿日常 push |
| `vision-agent-safe-ci.yml` | 是，显式列出 `codex初稿` | Product PR、Desktop PR、两个 Agent UI contract 文件 | 无 | `VERIFIED` |
| `vision-agent-quality.yml` | 是，`codex*` | Agent engineering suite、metadata benchmark、artifact assertions | 无 | `VERIFIED` |
| `codeql.yml` | 否，仅 main/develop | C#/JS 静态分析 | 不适用 | `VERIFIED` |
| `ClearVision.Product/.github/workflows/dotnet.yml` | 实际不位于仓库级 `.github/workflows` | 旧 Product test 配置 | 无 | `VERIFIED`：GitHub 不会把嵌套文件作为工作流加载 |

`VERIFIED`：根 CI 的 coverage step 只检查 Cobertura 文件存在并输出表格，没有阈值、基线对比、diff coverage 或下降阻断。

`VERIFIED`：根 CI 的完整 Legacy UI unit/E2E、OperatorLibrary 包烟测和 Quick contract suite 不会在每次 `codex初稿` push 执行。

### 3.2 当前 HEAD 的 GitHub Actions 事实

通过 GitHub CLI 查询 `bea40439...`：

| Run | 结果 | 关键事实 | 状态 |
| --- | --- | --- | --- |
| ClearVision Vision Agent Safe CI / `29710867720` | failure | Product PR：2442 pass、2 fail、2 skip；失败为 Unicode/本地化文件路径触发 OpenCV `Cannot marshal: Encountered unmappable character`；后续 Desktop/UI 均被跳过 | `VERIFIED` |
| Vision Agent Quality Suite / `29710867745` | failure | 首个 Agent 子集：611 pass、4 fail；失败集中于 BuildFromPlan/Readiness 对 camera binding/resource blocker 的预期不一致；后续 benchmark/assertion 被跳过 | `VERIFIED` |

`VERIFIED`：最近查询到的 20 条 `codex初稿` 工作流记录均为 failure；当前没有可作为绿色基线的 codex初稿 push。

`PARTIALLY_VERIFIED`：两条必要工作流确实每次 push 触发，但“触发”不等于“形成有效 Gate”；当前 HEAD 两条均红，Safe CI 因 fail-fast 没有执行 Desktop/UI。

`VERIFIED`：Vision Agent workflow 的上传步骤使用 `if: always()`，而产物断言步骤在首项失败后被跳过，因此失败 run 可上传 checkout 中的历史报告和部分新产物；消费方不能仅凭 artifact 存在判断新鲜度。

## 4. 当前 HEAD 测试执行与覆盖率证据

### 4.1 本轮新鲜执行

所有本机结果来自仓库外、内容等于 `bea40439...` 的短路径快照；未连接真实设备或生产数据。

| 入口 | 结果 | 墙钟/测试耗时 | 证据状态 |
| --- | --- | --- | --- |
| Test Governance | 3708 definitions，0 error/warning | 约 3.3 s（增量） | `VERIFIED` |
| Product PR，无 coverage，带 blame | 2446 total，2444 pass，2 skip，0 fail | 83.9 s；测试摘要 1m19s | `VERIFIED` |
| Product PR，完整 coverage | 干净 Rebuild 后 collector 15 分钟仍未完成，无 TRX/Cobertura | 超时，子进程已定向清理 | `UNKNOWN` |
| Product PR，异常中断后 no-build coverage | 2444 pass/2 skip；XML 仅含 5 个模块，缺 Core/Application/Infrastructure | 91.3 s | `PARTIALLY_VERIFIED`：产物新鲜但模块口径无效，不作为 Product 覆盖率 |
| Desktop PR + coverage | 619 total/pass，0 fail/skip | 141.5 s；测试主体 17 s | `VERIFIED` |
| Legacy UI unit | 978/978 pass；archive 首次因测试直接调用 `git ls-files` 失败，注入只读 Git 上下文后通过 | 18.3 s | `VERIFIED` |
| FrontendV2 lint/typecheck/unit | lint/typecheck pass；43/43 pass | Vitest 0.945 s；组合入口 36.5 s | `VERIFIED` |
| Legacy Playwright 全量 | 204 discovered；CI 模式 15 分钟未完成，产生大量 retry/error-context 后被定向清理 | 15 min 上限 | `UNKNOWN` |
| Legacy Playwright 单个失败候选 | 全套运行中出现 retry 产物的用例，隔离复跑 1/1 pass | 2.6 s | `VERIFIED`；支持顺序依赖/潜在 flaky 风险，不能证明整套绿色 |

### 4.2 可引用的当前覆盖率

Desktop PR Lane 新鲜 Cobertura：

| 口径 | Line | Branch | 状态 |
| --- | ---: | ---: | --- |
| 该 testhost 加载的全部 instrumented 模块 | 51996 / 185944 = 27.96% | 13127 / 68806 = 19.07% | `VERIFIED` |
| `ClearVision.Product.Desktop` 程序集 | 73.15% | 58.29% | `VERIFIED` |

`PARTIALLY_VERIFIED`：Desktop XML 同时包含 PlcComm、Application、Contracts、Core、Desktop、Infrastructure、Runtime、Runtime.Abstractions 和 Testing；整体 27.96% 不是“Desktop 源码覆盖率”，Desktop 程序集 73.15% 也不是“全仓覆盖率”。

`UNKNOWN`：当前 HEAD 的 Product 完整模块覆盖率、合并覆盖率和全仓覆盖率均未可靠获得。

`UNKNOWN`：由于 Product XML 口径不完整，不能将 8.59% / 5.81% 写入基线或用作阈值。

### 4.3 仓库内历史覆盖率/质量报告

- `VERIFIED`：HEAD 没有可绑定 `bea40439...` 的已提交 Cobertura/LCOV 代码覆盖率；`runtime_preview_operator_contract_coverage.*` 是合同覆盖清单，不是代码覆盖率。
- `VERIFIED`：`VisionAgent_business_benchmark_baseline.json` 与 `planner_autonomy_benchmark.json` 生成于 2026-06-14，`workflowRun.commitSha = local`。
- `VERIFIED`：`vision_agent_quality_artifact_manifest` 生成于 2026-06-07，所列报告大量标记 `commitSha/runId = local`。
- `VERIFIED`：Field Replay 报告生成于 2026-04，manifest 明确写明 `field-substitute`、semi-synthetic/protocol bridge，不是生产现场签字。
- `VERIFIED`：在质量报告、Product test_results 和根 test_results 中未找到当前 SHA `bea40439...`。
- `PARTIALLY_VERIFIED`：这些报告能证明工具链和历史样例存在，但只能作为历史数据，不能证明当前 HEAD 的算法质量或 Agent 质量。

## 5. 测试质量评估

### 5.1 高价值业务保护

| 资产 | 保护内容 | 评估 |
| --- | --- | --- |
| `FlowExecutionServiceTests` / `InspectionRuntimeCoordinatorTests` / `InspectionWorkerTests` | cancellation、并行兄弟取消、禁用/短路、重复 start、stopping 拒绝、异常订阅者后释放状态、终态替换 | `VERIFIED`：高价值；本机 PR 与远端日志均显示相关大量用例执行 |
| `ProjectSaveCoordinatorTests` / `ProjectServiceTests` / persistence concurrency | stale revision、commit intent、forward recovery、hash 篡改、fence、原子写、状态/flow/assets 一致性 | `VERIFIED`：高价值；远端 Product 日志显示关键保存/恢复用例执行 |
| Preview admission/endpoints | side-effect 阻断、ImageSave/TextSave/ResultOutput dry-run、不提交项目变量、camera read 阻断、取消、超时、artifact 安全 | `VERIFIED`：高价值，覆盖了 Preview/ROI 后端安全边界 |
| FinalDecision resolver / Station result mapper | 严格绑定来源、Invalid/Undetermined、numeric/string/bool canonical mapping、Station canonical outcome | `VERIFIED`：高价值；UI FinalDecision E2E 的当前全量状态仍未知 |
| Station offline/simulator/security/spool/sync | 离线 replay、command journal/spool、安全、package、映射、SignalR 客户端 | `VERIFIED`：有显著模拟保护；不等于真实 Station/网络现场验收 |
| Vision Agent Plan/Build/Readiness/Apply/Owner/Recovery | planner、readiness、build orchestration、AgentRun event/取消、session owner、RuntimePreview governance | `PARTIALLY_VERIFIED`：资产广，但当前 Agent Quality 有 4 个 Nightly 失败，真实 LLM/真实资源未验证 |

### 5.2 质量债务与潜在 flaky 信号

| 信号 | 静态/运行证据 | 评估 |
| --- | --- | --- |
| 非公开反射 | 36 个测试文件、130 处 `BindingFlags.NonPublic` | `VERIFIED`：存在实现耦合；集中在 DeepLearning、PLC 静态缓存、Station sync、WebMessageHandler 等 |
| Mock 密度 | 214 个文件、1469 次 `Substitute.For` | `VERIFIED`：Mock 使用广；不能按次数直接判坏，需按资产层抽样复核 Oracle 与真实边界 |
| 共享/静态状态 | 6 个集合显式 `DisableParallelization=true`；ProjectSave、PLC、Runtime 等依赖串行隔离 | `VERIFIED`：共享状态风险真实存在，但部分已被 collection 隔离 |
| 固定等待 | .NET 测试 7 处 `Thread.Sleep`；UI/E2E 104 处 `waitForTimeout` | `VERIFIED`：时序脆弱与反馈成本风险 |
| UI 选择器 | 21 处 nth/nth-child、1083 处 CSS locator 模式、仅 4 处 test-id 使用 | `VERIFIED`：Legacy UI 测试对 DOM 结构耦合显著 |
| CI 重试 | Playwright CI `retries=2`；本轮全量运行出现多组三轮结果目录 | `VERIFIED`：重试放大长尾并可能掩盖不稳定 |
| 随机性 | 正式 xUnit 中未发现 `Random.Shared` 或无参 `new Random()`；治理会阻断 | `VERIFIED`：受控 Random 治理有效；`Guid.NewGuid` 广泛用于隔离临时身份，不应当等同随机 Oracle |
| Skip | Product 2 个旧 UI Fact 带 2026-08-31 到期说明；Playwright 有显式 skip 和条件 skip | `VERIFIED`：有到期元数据，但无统一过期自动 Gate 证据 |
| 环境差异 | 本机 Product PR 通过；GitHub 同 SHA 因 Unicode marshal 失败 | `VERIFIED`：跨 locale/编码可移植性是当前真实红灯 |

`INFERRED`：高 Mock、私有反射或固定等待中的一部分可能是必要的故障注入/兼容性测试；治理应先分级和抽样，不应批量删除或机械重写。

### 5.3 Oracle、数据集、Golden、Field Replay、性能预算

- `VERIFIED`：分类治理对 Accuracy/Determinism/Stability/Performance 的声明有硬校验。
- `VERIFIED`：Quick contract（11 active，约 9.8 分钟）和 Golden Core50（14 active，约 20 分钟）有串行 manifest 和产物新鲜度检查。
- `VERIFIED`：dataset_heavy 的 active 估时约 86.5 分钟，位于声明 120 分钟预算内；manual/planned 不会默认执行。
- `VERIFIED`：Core20 中 20 个真实 field data 项明确标为 `blocked-missing-field-data`，没有冒充完成。
- `VERIFIED`：公开数据 manifest 区分 BSDS500、HPatches、OpenCV samples、Kolektor 等许可/用途；部分场景明确为非商业或 proxy。
- `PARTIALLY_VERIFIED`：大量 baseline 内容与 manifest 能通过结构校验，但当前 SHA 未绑定，且 codex初稿 push 不执行 Quick/Golden/公开数据 Lane。
- `PARTIALLY_VERIFIED`：性能测试声明预算和 profile，但当前未取得同机型、多次重复的当前 HEAD 分布；历史单次报告不能证明稳定性能。
- `UNKNOWN`：真实现场样本、真实相机/光学链、真实 PLC/机器人/Station、真实生产数据库和真实模型服务的当前 HEAD 证据。

## 6. 关键风险与测试盲区

| 业务域 | 已有保护 | 仍缺/当前风险 | 状态 |
| --- | --- | --- | --- |
| 流程执行、取消、停止、异常终态 | 服务级取消、并行失败、stop、replacement、spool/dead-letter 较强 | 跨 Desktop/Station/设备的终态一致性、重复取消/断电恢复、全链路事件顺序未做当前现场验证 | `PARTIALLY_VERIFIED` |
| 项目保存、恢复、一致性 | commit intent、revision、hash、fence、flow/variables/assets 有高价值测试 | 真实进程崩溃/磁盘满/杀进程后的恢复演练未执行；部分测试共享静态状态并禁并行 | `PARTIALLY_VERIFIED` |
| Preview、ROI、副作用阻断 | 后端 side-effect admission、dry-run、不提交变量、设备读取阻断覆盖强；Legacy unit 很多 | 原审计基线的 Legacy E2E 全量未绿；2026-08-28 当前分支应验证当前 production root，缓存 StudioUI 分支不再作为修补取舍前提 | `PARTIALLY_VERIFIED` |
| FinalDecision、结果统计 | resolver、canonical outcome、Station 映射和结果 UI unit 存在 | FinalDecision Playwright 当前全量状态未知；跨历史重放、统计口径、Invalid 与 execution failure 的新 UI 端到端尚待迁移接管 | `PARTIALLY_VERIFIED` |
| Station、PLC、TCP、设备资源 | local loopback、virtual PLC、simulator、offline replay、security、spool 较多 | 真实设备 Lane 18 个 ReleaseManual 定义未执行；端口/时序/网络抖动仍可能环境敏感 | `PARTIALLY_VERIFIED` |
| Agent Plan/Build/Validation/Apply Gate/Owner/Recovery | 762 个 Ai 域源码定义，加 UI/endpoint/benchmark | 原审计基线曾有 Agent Quality 4 fail；当前 SHA 状态须重跑，真实 LLM 仍是 manual，缓存 StudioUI 分支改动不计当前事实 | `PARTIALLY_VERIFIED` |
| 算法精度、稳定性、性能、现场证据 | Golden、公开数据、contract、性能 runner 和明确 manifest | 当前 SHA 无新鲜报告；Accuracy/Stability 源码定义仅 6/3；Field Replay 是 substitute；真实 field data 20 项 blocked | `PARTIALLY_VERIFIED` |
| 覆盖率治理 | main CI 会生成 Product/Desktop Cobertura | codex初稿不生成；无下降 Gate；Product 完整 collector 不稳定；模块集合未固定 | `PARTIALLY_VERIFIED` |

## 7. 当前 UI 路线与 `studio-ui-next` 边界

`studio-ui-next` 的统计、删除 FrontendV2 决定和 StudioUI 测试路径只描述缓存的并行分支。当前分支没有合并这些事实，且缓存观察显示两条路线已经大幅分叉；在没有新鲜 fetch 和正式合并/架构决定前，不能把另一分支的未来状态写成本分支前提。

| 对象 | 当前分支事实 | 本计划决定 | 状态 |
| --- | --- | --- | --- |
| Production root | `wwwroot/index.html + app.js + capability owners` | 作为本次 release 唯一 production root 继续验收 | `VERIFIED` |
| FrontendV2 | 目录、构建和 `/v2` flag 仍存在，但 Tool/Review 等 capability 不完整且 flag 默认 false | 明确为 non-production；不补 production coverage，也不直接切换 | `VERIFIED` |
| StudioUI | 当前分支不存在 `Desktop/StudioUI` | 其 unit/E2E 路径和命令不得进入当前 Gate | `VERIFIED` |
| 缓存 `studio-ui-next` | 2026-08-25 缓存 HEAD 为 `0c44df6c`，与当前分支显著分叉；本轮 fetch 失败 | 只作迁移调研。若未来采用，另立完整 parity/migration epic 并经正常合并 | `PARTIALLY_VERIFIED` |
| 当前 UI 测试 | legacy/current `wwwroot` unit、Playwright、WebView2 与 capability owner 合同 | T01-G07 只接管这些当前可执行入口 | `VERIFIED` |
| 后端/算法/coverage | 与最终 UI 路线弱耦合 | 可按当前分支继续，但 endpoint/owner 变更仍需文件级协调 | `INFERRED` |

## 8. 分阶段总体计划

### T01-G01：建立可信、可复现的当前 HEAD 覆盖率证据合同

- **当前 disposition**：阶段主体已归档；只保留 G01B-R3 对当前 UI spec 的重基线，历史 G01 报告不得替代当前 SHA。
- **目标**：先得到可审计的 Product/Desktop 当前 SHA 证据，固定模块范围、命令、SDK、测试计数、耗时和失败语义；不设置阻断阈值。
- **前置条件**：干净的 `origin/codex初稿` 隔离 checkout；无同项目测试进程；保留本轮 Product collector 长尾作为复现用例。
- **允许修改范围**：新增 `quality/coverage/**`、覆盖率 runsettings、专用只读汇总脚本、测试产物 schema、文档；如需改现有串行 runner，必须拆为独立评审。
- **禁止修改范围**：产品源码、业务测试断言、StudioUI/Legacy UI、现有 Gate 阈值、真实设备配置。
- **主要任务**：定义程序集 allowlist/exclude；排除 test/生成代码；一次 clean build 后按项目串行采集；为每份 XML 写入 sidecar（SHA、dirty=false、SDK、命令、TRX counters、module list、timestamps）；诊断 Product coverlet 长尾；拒绝缺模块和中断后残留产物。
- **验证命令**：现有基线命令为 `& './scripts/run-classified-test-gate.ps1' -Gate product-pr ... -Collect 'XPlat Code Coverage'` 与 desktop 对应命令；实现后增加单一 `& './scripts/run-coverage-baseline.ps1' -Gate product-pr,desktop-pr`，内部仍逐项目串行。
- **交付物**：coverage contract/schema、runsettings、当前 SHA baseline JSON/Markdown、模块差异诊断、复现日志。
- **完成标准**：两个全新 checkout 各运行一次；测试计数一致；模块集合和 valid line/branch 总量一致；产物 SHA 等于 HEAD；Product 不再出现 15 分钟无产物；缺模块必须红灯。
- **风险与回滚**：collector 配置可能改变历史百分比；以删除新配置/脚本和非阻断产物回滚，不回滚产品代码。
- **与迁移冲突**：低。
- **是否立即并行**：是。

### T01-G02：current-SHA necessary checks 与 artifact freshness

- **当前 disposition**：未闭环，远端证据为 `UNKNOWN`。旧基线中的 Unicode/Agent 失败只能作为历史线索，不能预设为当前 HEAD 仍失败；本地分支 ahead 且本环境无可用 `gh`，尚无新鲜远端结论。
- **目标**：让同一最终 SHA 的 Safe CI、Agent Quality、UI/主 CI necessary checks 可重复执行，并让 coverage/diagnostic artifact 绑定 SHA、schema 和 freshness；覆盖率仍 report-only。
- **主要任务**：先运行当前 Gate；只对实际 failure signature 建账并区分产品、fixture、locale、环境；fail-fast 也上传明确标 `incomplete` 的诊断；拒绝 checkout 内历史 artifact。
- **禁止修改范围**：为复现旧失败而制造 backlog、放宽断言、删除失败用例、无条件 retry、伪造 freshness、把缓存远端或历史 run 当作 current SHA。
- **验证**：本地按根 `AGENTS.md` 串行运行对应 Gate；远端用 run URL/job/artifact 证明 SHA 一致；artifact 执行 SHA/schema/timestamp/module 校验。
- **完成标准**：同一最终 SHA 的 necessary checks 全绿；任何 skipped/fail-fast job 有明确状态；coverage artifact 不能来自旧 checkout 内容。

### T01-G03：测试资产分级、Owner 与数量防回退

- **当前 disposition**：`OPEN_RESCOPED`。仓库已有 `TestClassificationAttribute` 以及 Domain/Purpose/Lane/Evidence/Oracle/Resource 分类，不再建立 A/B/C 平行体系。
- **目标**：在现有分类上增加 critical-contract 标记、Owner、批准 baseline+tolerance 和动态人口防回退，把多数 `minimumTotalTests: 1` 的占位下限升级为可审计基线。
- **主要任务**：分别记录源码定义数和动态用例数；删除/降级 critical contract 必须附 Owner 与原因；理论用例合法重命名/合并通过显式 baseline review；仅接入当前分支可执行的 UI/Agent Gate。
- **禁止修改范围**：用总数代替质量、复制空洞测试、引入第二套分类 schema、为 non-production FrontendV2 或不存在的 StudioUI 建 production baseline。
- **完成标准**：删除一个受保护 contract 或超过批准 tolerance 会失败；新增/移除模块会触发显式人口差异；合法 baseline update 有可审计审批记录。

### T01-G04：关键后端业务域合同矩阵补强

- **当前 disposition**：`OPEN_RESCOPED`。不再建设脱离真实修复的无限状态矩阵。
- **目标**：U08-U13 每个实际修复在实现提交中补公共合同、拒绝/故障和恢复 Oracle；测试范围由风险与变更边界决定。
- **主要任务**：执行准入、authority、持久化恢复、资源上限、fail-closed 参数等修复各自产生对应回归；跨域公共合同才提升为 critical contract。
- **禁止修改范围**：为凑“四象限”机械复制测试、用私有反射作为新 Oracle、在普通 CI 访问真实设备、把产品缺陷改成宽松断言。
- **完成标准**：每个 U08-U13 源 ID 有独立 acceptance 与 focused test，治理台账能追溯到修复 SHA；是否需要成功/拒绝/取消/恢复由该风险模型明确说明。

### T01-G05：稳定性、隔离性与低价值测试治理

- **当前 disposition**：`OPEN_RESCOPED`。仓库尚无 machine-readable flake registry，但不要求整个全量套件固定五次。
- **目标**：对 blocking lane、已知 flaky、时序/资源敏感测试进行有界 repeat，记录失败签名、p50/p95、retry、skip expiry 和 Owner。
- **主要任务**：区分稳定产品缺陷与真实 flaky；固定 sleep 改条件等待；静态状态和端口资源显式隔离；Playwright retry 后通过仍显式记账。
- **完成标准**：受治理 lane 的重复次数由风险说明；所有不稳定结果有 Owner/到期日/非静默策略；普通全量 Gate 不因机械 repeat 放大成本和噪声。

### T01-G06：算法质量证据新鲜度、Oracle 与数据治理

- **当前 disposition**：治理要求保留，具体实现去重到统一计划 U01/U03/U07。
- **目标**：只让 active/release-relevant 报告绑定 source SHA、dirty、tool/data checksum、环境和 evidence kind；历史报告保留为历史，不要求全部重生。
- **职责边界**：DeepLearning smoke/precision、交付模型和 provider 由 U01；算子人口、Core20、场景资产由 U03；真实 field/device/profile 由 U07。
- **完成标准**：current Gate 不能消费历史/错误类型产物；真实、公开、替代、合成标签机器可判且不可丢失；现场阻断只能由真实 profile 证据解除。

### T01-G07：当前 production UI 的 capability owner 测试接管

- **当前 disposition**：原 StudioUI 路径和命令 `SUPERSEDED`。当前分支不存在 `Desktop/StudioUI`，FrontendV2 为 non-production，本次 release 不切 `/v2`。
- **目标**：为 `wwwroot/index.html + app.js + capability owners` 建立 owner/legacy replacement matrix，保证 Settings、AI、Project、Inspection、Results、Preview 等实际 production capability 只有一个 mounted owner 和一组权威业务合同。
- **允许修改范围**：当前 `wwwroot` unit、现有 Playwright、WebView2 脚本、capability owner architecture guard、CI 对应步骤和 legacy retirement ledger。
- **禁止修改范围**：调用不存在的 StudioUI 测试命令；为 non-production FrontendV2 建发布阻断覆盖率；在 owner parity 未明确前删除现有业务保护。
- **主要任务**：逐 capability 决定晋级或删除实验 owner/flag；current root 的 unit/E2E/WebView2 接管；每退役一组 legacy 行为必须有等价公共合同证据。
- **完成标准**：production root、mounted owner、默认 flag、构建产物和测试 Gate 一致；真实 WebView2 及 G16 release matrix 通过。未来 Vue/StudioUI 迁移另立 epic，不在本 Goal 偷渡。

### T01-G08：数据驱动的覆盖率防回退 Gate

- **目标**：在可信、稳定的 current-HEAD 基线之上防止覆盖率下降；不采用预设 80%。
- **前置条件**：G01-G05 完成；模块集合固定；至少多个绿色 HEAD 运行可估计自然波动；critical-contract baseline 可用。
- **允许修改范围**：coverage policy、CI comparison、approved baseline、changed-code policy、例外流程。
- **禁止修改范围**：用全仓单一百分比驱动无价值测试；忽略生成代码/模块变化；在当前红 CI 上直接 blocking。
- **主要任务**：选择按程序集/关键命名空间/changed-code 的组合策略；line 与 branch 分开；模块增删单独审批；阈值来源于实际基线、风险与波动；覆盖率下降与 critical-contract 删除/降级形成双 Gate。
- **验证命令**：基线/候选两次报告比较；构造下降、模块缺失、合法 baseline update 三类自测。
- **交付物**：policy、baseline、comparison report、例外模板、blocking workflow。
- **完成标准**：真实下降可阻断，等价重构/模块移动可审查更新，无“删除未测代码反而变绿”的漏洞。
- **风险与回滚**：初期噪声阻塞交付；支持 report-only 开关和按程序集回滚，不删除历史 baseline。
- **与迁移冲突**：后端低；前端阈值等 G07 后加入。
- **是否立即并行**：暂不建议启用 blocking；可以预研 schema。

### T01-G09：真实设备、现场数据与人工验收

- **当前 disposition**：`BLOCKED_EXTERNAL`，但按 release SKU/profile 独立关闭，不要求一次验证仓库声称过的所有 PLC、相机、LLM 和 GPU provider。
- **前置条件**：先建立 support matrix，将设备、协议、相机、模型/provider、Station/WebView2 标为 required/optional/unsupported，并准备隔离实验室、脱敏数据和回滚 SOP。
- **主要任务**：只对目标 SKU 声明支持的 PLC/TCP、相机/光学、Station、交付模型/provider、真实 LLM shadow 和当前 production UI 的 WebView2/DPI/键鼠做真实验收。
- **完成标准**：每个 profile 有 Owner、SHA、设备型号/序列号、固件/驱动、数据 checksum、pass/fail、异常恢复和回滚；profile 可独立关闭，未发布/实验能力不阻塞项目。
- **禁止修改范围**：普通 CI 连接真实设备或生产 DB；用 simulator/public/field-substitute 结果冒充现场；执行不存在的 StudioUI 人工验收路径。

## 9. 推荐执行顺序

| 顺序 | Goal | 时间分类 | 原因 |
| ---: | --- | --- | --- |
| 1 | G01B-R3 当前 UI 契约重基线 | 可立即执行本地部分 | 先运行现存 7 个 spec，历史 21 个 failure point 不预设为当前 backlog |
| 2 | G02 current-SHA CI/freshness | 本地准备；远端证据受阻 | 没有同一 SHA 绿色 necessary checks，后续阈值无意义 |
| 3 | G03 现有分类的 critical-contract baseline | 可与风险修复并行 | 不重复建设 A/B/C，先固定 Owner 与动态人口 |
| 4 | G04 随 U08-U13 修复落地 | 随实现执行 | 公共合同测试由真实风险产生，不另建无限矩阵 |
| 5 | G05 风险定向 flaky 治理 | 有运行证据后执行 | 只 repeat blocking/已知敏感 lane |
| 6 | G06/U01/U03 证据真实性 | 可分 lane 执行 | active/release evidence 优先，历史报告不重生 |
| 7 | G07 当前 production UI 接管 | 与 G16 同步 | 验证当前 root/capability owners，不等待不存在的 StudioUI |
| 8 | G08 coverage blocking | 暂不实施 | 需多个绿色 SHA 和稳定模块人口 |
| 9 | G09 目标 SKU 现场验收 | 需要授权环境 | 每个 profile 独立关闭 |

明确暂不建议：全仓 80% 硬阈值、批量删除低覆盖代码、批量重写 Mock/反射测试、用 retry 获绿、当前重构 Legacy UI selector、给 FrontendV2 新增治理、把历史/local/field-substitute 报告写成 current/现场完成。

## 10. 下一可执行子 Goal 的明确边界

下一项是 [T01-G01B-R3 UI 契约回归闭环计划](./ClearVision_T01_G01B_R3_UI契约回归闭环计划_2026-08-28.md)：在当前 HEAD 运行仍存在的 7 个 spec，按当前 failure signature 重建账本。已消失的历史 fixture 直接记录关闭依据，实际失败才进入产品回归、fixture 过时或环境问题分类。

本子 Goal 不修改不存在的 StudioUI，不为 non-production FrontendV2 建发布阻断覆盖率，不预创建 21 个永久 backlog，也不以本地结果替代 G02 的同 SHA 远端 necessary checks。所有 .NET targeted tests 继续通过仓库串行 runner 合并同项目 FQN。

## 11. 尚不能验证的事项

- `UNKNOWN`：当前 HEAD 的 Product 完整代码覆盖率与全仓合并覆盖率。
- `UNKNOWN`：当前 HEAD Legacy 204 个 Playwright E2E 的完整通过/失败总数；本轮 15 分钟中断产物不能作完整结果。
- `UNKNOWN`：当前 HEAD 的 OperatorLibrary 当前源码包烟测；codex初稿 push 不执行打包后 override 测试，本轮未另行打包。
- `UNKNOWN`：Desktop PR 在 GitHub 当前 HEAD 的结果；Safe CI 在 Product 失败后将其跳过。
- `UNKNOWN`：Product Nightly 1043 源码定义和 ReleaseManual 18 定义的完整动态计数/状态。
- `UNKNOWN`：Quick/Golden/public dataset/性能 suite 在当前 HEAD 的新鲜结果。
- `UNKNOWN`：真实 LLM、真实相机/PLC/机器人/Station/生产数据库与真实现场数据的当前 HEAD 结果。
- `UNKNOWN`：`studio-ui-next` 的新鲜远端状态和完整 unit/E2E 结果；2026-08-28 fetch 因 TLS EOF 失败，缓存分支事实不得用于当前 Gate。
- `UNKNOWN`：覆盖率自然波动分布和合理阻断阈值；当前无足够多可信基线运行。

## 12. 本轮主要验证命令与结果摘要

| 命令/查询 | 结果 | 状态 |
| --- | --- | --- |
| `git ls-remote --heads origin codex初稿 main` | 远端 SHA 与记录一致 | `VERIFIED` |
| `git merge-base` / `git rev-list --left-right --count` | codex初稿 与 main、studio-ui-next 分叉已记录 | `VERIFIED` |
| `& './scripts/run-test-governance.ps1' ... -FailOnWarning` | 3708 / 0 / 0 / 0 | `VERIFIED` |
| Product `run-classified-test-gate` 无 coverage + blame | 2444 pass、2 skip | `VERIFIED` |
| Product clean coverage | 15 分钟无完整产物 | `UNKNOWN` |
| Desktop PR + XPlat Code Coverage | 619 pass；新鲜 Cobertura | `VERIFIED` |
| Legacy UI `npm run test:unit` | 978 pass（只读 Git 上下文） | `VERIFIED` |
| FrontendV2 lint/typecheck/Vitest | 全绿，43 tests | `VERIFIED` |
| Playwright `--list` | 204 tests / 26 files | `VERIFIED` |
| Playwright full CI mode | 15 分钟未完成；多 retry 产物 | `UNKNOWN` |
| `gh run list/view --branch codex初稿` | HEAD 两条 push workflow 均 failure，失败原因已定位 | `VERIFIED` |
| quality suite/manifests/reports 静态审计 | current SHA 未绑定；field substitute 边界明确 | `VERIFIED` |
