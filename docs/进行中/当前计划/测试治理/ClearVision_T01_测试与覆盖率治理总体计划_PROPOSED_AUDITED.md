# ClearVision T01 测试与覆盖率治理总体计划（PROPOSED / AUDITED）

> 审计日期：2026-07-30（Asia/Shanghai）
> 唯一代码基线：`origin/codex初稿@bea404394ac8cf403cca719c1990c426414a06c2`
> 文档性质：审计结论与分阶段计划；本轮没有实施正式治理改造
> 实施状态：所有 Goal 均为待执行计划，不代表已经完成

文档采用 `docs/进行中/当前计划/测试治理/`，而非直接放在 `docs/进行中/测试治理/`，因为仓库现行文档治理规则要求项目级 active 计划统一进入“当前计划”并由其 README 建立唯一入口。

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
| Preview、ROI、副作用阻断 | 后端 side-effect admission、dry-run、不提交变量、设备读取阻断覆盖强；Legacy unit 很多 | Legacy E2E 全量未绿；StudioUI 正在重写 Preview/ROI，旧 UI 修补会快速失效 | `PARTIALLY_VERIFIED` |
| FinalDecision、结果统计 | resolver、canonical outcome、Station 映射和结果 UI unit 存在 | FinalDecision Playwright 当前全量状态未知；跨历史重放、统计口径、Invalid 与 execution failure 的新 UI 端到端尚待迁移接管 | `PARTIALLY_VERIFIED` |
| Station、PLC、TCP、设备资源 | local loopback、virtual PLC、simulator、offline replay、security、spool 较多 | 真实设备 Lane 18 个 ReleaseManual 定义未执行；端口/时序/网络抖动仍可能环境敏感 | `PARTIALLY_VERIFIED` |
| Agent Plan/Build/Validation/Apply Gate/Owner/Recovery | 762 个 Ai 域源码定义，加 UI/endpoint/benchmark | 当前 Agent Quality 4 fail；真实 LLM 是 manual；artifact 可在 assertion 跳过时上传历史内容；StudioUI AI owner 正在改动 | `PARTIALLY_VERIFIED` |
| 算法精度、稳定性、性能、现场证据 | Golden、公开数据、contract、性能 runner 和明确 manifest | 当前 SHA 无新鲜报告；Accuracy/Stability 源码定义仅 6/3；Field Replay 是 substitute；真实 field data 20 项 blocked | `PARTIALLY_VERIFIED` |
| 覆盖率治理 | main CI 会生成 Product/Desktop Cobertura | codex初稿不生成；无下降 Gate；Product 完整 collector 不稳定；模块集合未固定 | `PARTIALLY_VERIFIED` |

## 7. 与 `studio-ui-next` 的冲突矩阵

迁移分支相对共同祖先改动约 1001 个文件，其中 StudioUI 440、Product tests 105、Legacy `wwwroot` 12、FrontendV2 34；根 CI 和 Playwright 配置也已修改。迁移分支已删除 FrontendV2，并新增约 97 个 StudioUI unit spec 文件及 22 个 StudioUI E2E/支持文件。

| 工作 | 冲突等级 | 并行建议 | 依据/边界 | 状态 |
| --- | --- | --- | --- | --- |
| 覆盖率证据 schema、SHA/SDK/模块清单、Cobertura 解析器 | 低 | 立即并行 | 可放 `quality/coverage` 或新脚本，不触碰 StudioUI | `VERIFIED` |
| Product Core/Application/Runtime 的覆盖率口径与后端资产分级 | 低 | 立即并行 | 与前端组件结构独立；避开迁移已改的具体 endpoint/test 文件 | `INFERRED` |
| `quality/test-gates.json` 数量基线设计 | 低 | 立即并行准备 | 迁移提交未改该文件；实际基线要纳入合并后的 StudioUI 测试 | `VERIFIED` |
| 根 `.github/workflows/ci.yml` 接线 | 中 | 可准备，合并时单点协调 | 迁移已修改同一文件，主要是文本冲突 | `VERIFIED` |
| `playwright.config.ts`、UI package scripts | 中高 | 等迁移配置稳定或由迁移负责人接入 | 迁移已修改 scenario/server/testMatch | `VERIFIED` |
| FrontendV2 覆盖率、阈值、补测 | 极高/无效劳动 | 不实施 | 迁移分支已删除 FrontendV2 | `VERIFIED` |
| Legacy UI selector 大改、E2E 大规模重写 | 高 | 延期 | 新 StudioUI 将替代页面结构；只保留少量合同映射分析 | `VERIFIED` |
| StudioUI unit/E2E 数量 Gate、Vitest coverage、WebView2 验收 | 高 | 框架与测试入口稳定后由迁移线接管 | 当前迁移工作树仍在持续改 AI owner/endpoint/E2E | `VERIFIED` |
| 后端算法 Golden/Field/性能证据 | 低 | 立即并行 | 不依赖前端，真实设备 Lane 除外 | `INFERRED` |
| AgentRun endpoint/owner 恢复测试 | 高 | 当前不要双写 | 迁移工作树正修改 AgentRun endpoint、Desktop test 和 StudioUI owner | `VERIFIED` |

## 8. 分阶段总体计划

### T01-G01：建立可信、可复现的当前 HEAD 覆盖率证据合同

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

### T01-G02：修复当前红色 CI，并让 codex初稿发布非阻断覆盖率证据

- **目标**：先让 current-HEAD necessary checks 可重复执行，再把 G01 覆盖率作为 artifact/summary 发布；仍不做覆盖率下降阻断。
- **前置条件**：G01 模块口径稳定；Unicode marshal 与 4 个 Agent Nightly 失败有最小复现和责任归属。
- **允许修改范围**：失败对应的测试/必要产品修复（单独 PR）、Safe CI/Agent workflow、覆盖率上传步骤、artifact freshness assertion。
- **禁止修改范围**：通过放宽断言、删除失败用例、增加无条件 retry、伪造 artifact freshness 获绿；不接触 StudioUI 业务架构。
- **主要任务**：复现 CI locale；区分产品 Unicode 缺陷与测试夹具缺陷；修复 BuildFromPlan readiness 预期/实现合同；让 fail-fast 前也能上传明确标记 incomplete 的诊断；将 coverage artifact 绑定 SHA；协调 `ci.yml` 文本冲突。
- **验证命令**：Product PR/Agent FQN 通过串行脚本单进程运行；`gh run view <run> --json jobs` 验证远端；对 artifact 执行 SHA/schema/freshness 校验。
- **交付物**：绿色 HEAD run、失败根因记录、coverage artifact、incomplete artifact 语义。
- **完成标准**：同 SHA Safe CI 与 Agent Quality 均绿；Desktop/UI 不再因 Product fail-fast 永久跳过；coverage artifact 不能来自 checkout 历史文件。
- **风险与回滚**：CI 文件与迁移有文本冲突；按 step 级 commit 回滚，保留诊断 artifact，不回滚迁移代码。
- **与迁移冲突**：中（工作流文本）；AgentRun 相关修复高，需迁移负责人协调。
- **是否立即并行**：脚本/失败复现可以；工作流合并需协调。

### T01-G03：测试资产分级、Owner 与数量防回退

- **目标**：建立 A（关键业务保护）、B（合同/算法证据）、C（实现耦合/低价值候选）资产台账；把“至少 1 个”改为可审计基线与显式变更流程。
- **前置条件**：G01/G02 给出稳定动态计数；确认 theory 展开计数与源码定义计数的用途差异。
- **允许修改范围**：治理 schema/runner、`quality/test-gates.json`、Owner/资产清单、报告；不要求本阶段补测试。
- **禁止修改范围**：用总数替代质量；为了达标复制/参数化空洞用例；直接删除 C 类测试。
- **主要任务**：按 Domain/Lane/asset tier 记录源码定义数和动态数；Gate floor 使用批准后的 baseline+tolerance；删除/降级必须附 Owner 与原因；UI/Agent 现有下限纳入统一报告；FrontendV2 不再建立新基线。
- **验证命令**：`run-test-governance.ps1 -FailOnWarning`；三个分类 Gate discovery；Node TAP summary；StudioUI 稳定后再接入其 Vitest/E2E count。
- **交付物**：资产台账、Owner 映射、count baseline、批准变更模板、异常报告。
- **完成标准**：删除任一 A 类或超过批准容差的 Gate 测试会失败；合法重命名/合并可通过显式 baseline review 更新。
- **风险与回滚**：理论用例变化可能造成噪声；先 report-only，再启用 count blocking，可一键回退到报告模式。
- **与迁移冲突**：后端低；StudioUI count 部分高。
- **是否立即并行**：后端与治理框架可以；StudioUI 部分等待。

### T01-G04：关键后端业务域合同矩阵补强

- **目标**：按状态机和失败矩阵审查缺口，只补能防真实业务回归的测试，不追求任意百分比。
- **前置条件**：G03 A 类定义完成；每个域有产品 Owner 和 Oracle。
- **允许修改范围**：Product/Desktop 测试与必要测试夹具；产品代码修改必须拆分为独立缺陷修复。
- **禁止修改范围**：大规模产品重构、UI 架构修改、真实设备访问、把私有实现细节作为新 Oracle。
- **主要任务**：流程 terminal/cancel/stop 矩阵；项目 crash/recovery/fence 一致性；Preview side-effect/ROI 不提交；FinalDecision 与统计口径；Station/PLC/TCP virtual/offline；Agent Plan→Build→Validation→Apply/Owner/Recovery 合同。
- **验证命令**：按 AGENTS 规则将同项目多个 FQN 合并到一次 `run-dotnet-test-serial.ps1`；Desktop 与 Product 分开串行；不得启动真实设备脚本。
- **交付物**：域矩阵、A 类测试、失败注入夹具、Oracle 说明、TRX。
- **完成标准**：每个关键状态转换至少有成功、拒绝、取消/异常和恢复 Oracle；测试从公共合同观察结果；无新增非公开反射，或有批准豁免。
- **风险与回滚**：共享静态状态可能扩大 flaky；按域小批提交，回滚新增测试/夹具，不隐藏产品缺陷。
- **与迁移冲突**：Product core 低；Desktop endpoint/AgentRun 中高。
- **是否立即并行**：不与迁移重叠的后端域可以；AgentRun/Preview endpoint 需文件 Owner 协调。

### T01-G05：稳定性、隔离性与低价值测试治理

- **目标**：用运行证据处理 flaky、顺序依赖、共享状态、固定等待、过度 Mock 与私有反射；先观测再清理。
- **前置条件**：G03 资产分级；至少 5 次同环境重复结果与失败签名。
- **允许修改范围**：测试夹具、时间/随机/端口抽象、公共测试 seam、测试并行集合、报告。
- **禁止修改范围**：无证据批量删除；用 retry 掩盖；为了测试暴露产品私有状态；当前重写 Legacy UI selector。
- **主要任务**：记录 p50/p95/失败率；将真实 flaky 与稳定产品缺陷分开；替换固定 sleep 为条件等待；减少静态缓存反射清理；抽样检查 Mock Oracle；建立 skip 到期 Gate；Playwright 记录 retry/flaky 而非最终绿即通过。
- **验证命令**：计划新增串行 repeat runner；同一 csproj 逐次执行；UI 按单 worker 和 shard 隔离对比；输出 machine-readable flake report。
- **交付物**：flaky registry、隔离修复、reflection/mock 审计清单、skip expiry report。
- **完成标准**：A 类 Gate 连续运行无不解释失败；已知 flaky 有 Owner/到期日/非静默策略；retry 后通过仍在摘要中显式失败或警告。
- **风险与回滚**：时序改造可能改变测试速度；按夹具回滚，保留观测报告。
- **与迁移冲突**：后端低；Legacy/StudioUI 高。
- **是否立即并行**：后端观测可以；UI 改造等待。

### T01-G06：算法质量证据新鲜度、Oracle 与数据治理

- **目标**：让 Golden/public benchmark/性能/Field Replay 报告可绑定 SHA、数据版本和运行环境，保持现场声明诚实。
- **前置条件**：数据许可、manifest checksum、runner 版本和 Owner 清晰；当前历史报告只作参考。
- **允许修改范围**：`quality/**`、算法质量 runner/test、报告 schema、CI scheduled/manual lane。
- **禁止修改范围**：把 field-substitute 改名成真实现场；把公开非商业数据当商用签字；未校准环境下设置性能硬阈值；真实设备自动连接。
- **主要任务**：所有报告加入 source SHA/dirty/tool/data checksum；Quick/Golden current-HEAD 重跑；公开数据按许可分层；Accuracy 使用独立/标注 Oracle；Stability 使用多 seed/扰动分布；性能记录硬件与 p50/p95；20 个 blocked field 项保持阻断状态直至真实数据到位。
- **验证命令**：`python quality/tools/run_quality_suite.py --suite quick_contract_suite --validate-only/--run`；Golden 串行运行；dataset/field 默认 dry-run 或 manual。
- **交付物**：新鲜证据 manifest、数据卡、Oracle 说明、环境指纹、current-HEAD 报告。
- **完成标准**：报告能从 SHA+manifest+命令复现；历史/local 报告不能进入 current Gate；真实/替代/合成标签不可丢失。
- **风险与回滚**：报告体积和运行时间；保留 summary、外置大原始产物，回滚 Gate 接线而非篡改数据。
- **与迁移冲突**：低。
- **是否立即并行**：metadata/Quick/Golden 可以；重型数据与现场必须分 Lane。

### T01-G07：StudioUI 测试接管与 Legacy 退役

- **目标**：新前端稳定后，按业务合同接管 Legacy 测试，不按文件逐行翻译；建立 StudioUI unit、E2E、WebView2 与前端覆盖率基线。
- **前置条件**：StudioUI 路由、host adapter、Preview/ROI/AI/FinalDecision/Station owner 接口稳定；迁移负责人声明测试入口冻结窗口。
- **允许修改范围**：`StudioUI/tests`、StudioUI Vitest 配置、`tests/e2e/studio-ui-next`、Playwright scenario、CI 对应步骤、Legacy retirement map。
- **禁止修改范围**：继续建设已删除的 FrontendV2；新旧页面双份无 Owner 测试长期共存；在功能未接管前删除 Legacy A 类保护。
- **主要任务**：建立 Legacy→StudioUI 业务合同映射；补 Preview/ROI/FinalDecision/Result/Station/AI Owner E2E；设置 test count 与 Vitest coverage 报告；WebView2 与浏览器静态 E2E 分 Lane；每退役一组 Legacy 测试必须有替代证据。
- **验证命令**：StudioUI `npm run lint/typecheck/test:unit/bundle:ci`；`CV_UI_SCENARIO=studio-ui-next` Playwright；WebView2 固定脚本；均由迁移线提供稳定入口。
- **交付物**：接管矩阵、StudioUI baseline、Legacy retirement ledger、WebView2 evidence。
- **完成标准**：每个 Legacy A 类合同都有新 owner 和可执行替代；Legacy 删除不会降低业务保护；StudioUI 测试数量/覆盖率/flake 报告可审计。
- **风险与回滚**：迁移结构变化使测试快速失效；按 capability 接管，未完成 capability 保留 Legacy Gate。
- **与迁移冲突**：高。
- **是否立即并行**：否，等待框架与 owner 稳定。

### T01-G08：数据驱动的覆盖率防回退 Gate

- **目标**：在可信、稳定的 current-HEAD 基线之上防止覆盖率下降；不采用预设 80%。
- **前置条件**：G01-G05 完成；模块集合固定；至少多个绿色 HEAD 运行可估计自然波动；A 类资产台账可用。
- **允许修改范围**：coverage policy、CI comparison、approved baseline、changed-code policy、例外流程。
- **禁止修改范围**：用全仓单一百分比驱动无价值测试；忽略生成代码/模块变化；在当前红 CI 上直接 blocking。
- **主要任务**：选择按程序集/关键命名空间/changed-code 的组合策略；line 与 branch 分开；模块增删单独审批；阈值来源于实际基线、风险与波动；覆盖率下降与 A 类测试删除双 Gate。
- **验证命令**：基线/候选两次报告比较；构造下降、模块缺失、合法 baseline update 三类自测。
- **交付物**：policy、baseline、comparison report、例外模板、blocking workflow。
- **完成标准**：真实下降可阻断，等价重构/模块移动可审查更新，无“删除未测代码反而变绿”的漏洞。
- **风险与回滚**：初期噪声阻塞交付；支持 report-only 开关和按程序集回滚，不删除历史 baseline。
- **与迁移冲突**：后端低；前端阈值等 G07 后加入。
- **是否立即并行**：暂不建议启用 blocking；可以预研 schema。

### T01-G09：真实设备、现场数据与人工验收

- **目标**：补齐自动模拟不能证明的设备、现场、性能和人工体验证据。
- **前置条件**：隔离实验室、设备清单、脱敏数据、回滚 SOP、人工验收人和时间窗。
- **允许修改范围**：ReleaseManual 配置、实验室脚本、外置证据、脱敏 manifest、验收记录。
- **禁止修改范围**：普通 CI 自动连接真实 PLC/相机/机器人/Station/生产 DB；未授权写生产配置。
- **主要任务**：PLC/TCP 抖动与断线恢复；相机/光学/编码路径；Station 部署/离线/重连；真实算法数据；真实 LLM shadow；StudioUI WebView2/DPI/键鼠人工验收。
- **验证命令**：仅实验室批准 SOP；命令、设备序列、固件、数据 checksum、操作者均记录。
- **交付物**：签字 evidence pack、field replay manifest、设备/环境指纹、失败回滚记录。
- **完成标准**：每项声明有真实来源、Owner、时间、SHA、设备/数据版本与明确 pass/fail；替代数据不冒充现场。
- **风险与回滚**：设备或数据副作用；使用隔离账户/配置快照/停止开关，失败立即按 SOP 恢复。
- **与迁移冲突**：前端人工验收高，后端设备证据中。
- **是否立即并行**：否，需要设备与人工授权。

## 9. 推荐执行顺序

| 顺序 | Goal | 时间分类 | 原因 |
| ---: | --- | --- | --- |
| 1 | G01 可信覆盖率证据合同 | 可立即执行 | 当前最核心缺口；与迁移低冲突 |
| 2 | G02 当前红 CI 与 artifact freshness | 可立即准备/协调合并 | 没有绿色 current-HEAD Gate，后续阈值无意义 |
| 3 | G03 资产分级与数量防回退 | 可立即执行后端部分 | 先知道保护资产，再谈补测/删测 |
| 4 | G04 关键后端合同矩阵 | 可立即执行非重叠域 | 直接保护业务风险，不追数值 |
| 5 | G06 算法证据新鲜度 | metadata/Quick 可立即；重型 manual | 现有证据大多是历史/local |
| 6 | G05 稳定性治理 | 后端可开始，UI 等待 | 需要运行历史，不宜一次性大改 |
| 7 | G07 StudioUI 接管 | 等前端框架稳定 | 避免 FrontendV2/Legacy 无效劳动 |
| 8 | G08 覆盖率 blocking | 暂不建议实施 | 需稳定基线和多次分布 |
| 9 | G09 真实设备/人工验收 | 需要实验室/人工 | 普通 CI 禁止执行 |

明确暂不建议：全仓 80% 硬阈值、批量删除低覆盖代码、批量重写 Mock/反射测试、用 retry 获绿、当前重构 Legacy UI selector、给 FrontendV2 新增治理、把历史/local/field-substitute 报告写成 current/现场完成。

## 10. 首个可执行 Goal 的明确边界

首个执行 Goal 推荐为 **T01-G01：可信覆盖率证据合同**。

### 文件白名单

- 新增 `quality/coverage/**`；
- 新增专用 coverage runsettings/schema；
- 新增一个覆盖率串行 orchestration/report 脚本；
- 更新测试治理文档；
- 生成文件只进入 ignored `TestResults`/`.tmp` 或 CI artifact。

### 明确非目标

- 不修改产品源码和业务测试断言；
- 不修复本轮发现的 CI 测试失败；
- 不改 StudioUI、Legacy UI、FrontendV2；
- 不设置任何 coverage blocking threshold；
- 不连接真实资源；
- 不声称 Product 覆盖率，直到完整模块集合可重复生成。

### 首个 Goal 验收重点

1. 新鲜产物必须绑定 `HEAD`、dirty 状态、SDK、命令、TRX counters 和模块集合。
2. Product/Desktop 同项目只允许单进程；多 FQN 合并一次调用。
3. collector 中断后必须清理/重建，不得消费残留 instrumented 输出。
4. 模块缺失、SHA 不符、旧时间戳、无 TRX、测试失败均使证据无效。
5. 两个干净 checkout 的 valid line/branch 总量和模块集合一致后，才登记 baseline。

## 11. 尚不能验证的事项

- `UNKNOWN`：当前 HEAD 的 Product 完整代码覆盖率与全仓合并覆盖率。
- `UNKNOWN`：当前 HEAD Legacy 204 个 Playwright E2E 的完整通过/失败总数；本轮 15 分钟中断产物不能作完整结果。
- `UNKNOWN`：当前 HEAD 的 OperatorLibrary 当前源码包烟测；codex初稿 push 不执行打包后 override 测试，本轮未另行打包。
- `UNKNOWN`：Desktop PR 在 GitHub 当前 HEAD 的结果；Safe CI 在 Product 失败后将其跳过。
- `UNKNOWN`：Product Nightly 1043 源码定义和 ReleaseManual 18 定义的完整动态计数/状态。
- `UNKNOWN`：Quick/Golden/public dataset/性能 suite 在当前 HEAD 的新鲜结果。
- `UNKNOWN`：真实 LLM、真实相机/PLC/机器人/Station/生产数据库与真实现场数据的当前 HEAD 结果。
- `UNKNOWN`：`studio-ui-next` 当前脏工作树的完整 unit/E2E 状态；本轮严格只读且未干扰其他 Agent。
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
