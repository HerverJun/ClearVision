# ClearVision T01-G01B-R2：远端 CI 与专项证据可信闭环报告

## 结论

R2 已完成远端执行链修复、专项证据收集和失败归因，但不能标记为完整通过。最终远端代码 SHA `ef4d1872a69b75099edb207a708227273abaa0ef` 上，Measurement、Desktop、Product coverage、TCP、PPF、Governance、Product Nightly、Operator Precision、Safe CI 和 Vision Agent Quality 均已得到真实结果；主 CI 的 `Build & Test` 仍因 21 个旧 UI E2E fixture 失败而为 failure。

最终状态：`G01B_R2_BLOCKED_BY_REMOTE_CI`

该状态不是把 UI 失败降级为 warning，也不是通过 skip、quarantine、snapshot 批量更新或 CI 过滤伪造绿色。失败已稳定复现并归类为 `STALE_TEST_FIXTURE`；由于当前代码未修改对应 UI 产品或 UI 测试文件，R2 不越界修复该历史契约漂移。

## SHA 与边界

```text
INITIAL_SHA=1dad6ced67883c64470b98773cab25044fb4f786
CODE_SHA=ef4d1872a69b75099edb207a708227273abaa0ef
REMOTE_CODE_SHA=ef4d1872a69b75099edb207a708227273abaa0ef
REPORT_SHA=3725d7e972aa265b4fe624253f412a6c120f47ea；该字段只标识本报告文档提交，不替代 CODE_SHA
TARGET_BRANCH=codex初稿
WORKTREE=C:\cv-t01-g01b-r2-wt-20260802-
MAIN_WORKTREE_UNTOUCHED=true
STUDIO_UI_NEXT_UNTOUCHED=true
```

本地工作树在报告创建前为 `b91df4a9a6204b9e8f4243566259ed1ba1754df1`，相对 `origin/codex初稿` 为 `ahead 1, behind 2`。远端的 `ef4d1872` 是等价的 P3 contract runner 提交加编码修复；该分歧在报告提交后通过普通 merge 处理，不使用 force-push、reset、amend 或 squash。所有远端正式证据均以 `CODE_SHA`/`REMOTE_CODE_SHA` 为准。

## 远端主 CI

主 workflow：`ClearVision CI/CD`，run `30752858397`，三次 attempt 的 head SHA 均为 `ef4d1872a69b75099edb207a708227273abaa0ef`。

| Attempt | 关键 job | Job ID | 结果 | 归因 |
| ---: | --- | ---: | --- | --- |
| 1 | Nightly Quality Lane | 91509863729 | FAILURE | 初始 Product Nightly 16 项失败 |
| 1 | Measurement Performance Gate | 91509863735 | PASS | 独立 job，证据已上传 |
| 1 | Build & Test | 91509863760 | FAILURE | UI E2E 旧 fixture |
| 2 | Nightly Quality Lane | 91511797034 | FAILURE | 重跑时仍为同一批历史 Nightly/fixture 问题 |
| 2 | Measurement Performance Gate | 91511797467 | PASS | 独立 job，证据已上传 |
| 2 | Build & Test | 91511797000 | FAILURE | 与 attempt 3 相同的 21 个 UI E2E 失败 |
| 3 | Nightly Quality Lane | 91517291007 | PASS | Product Nightly `1561/1561` |
| 3 | Measurement Performance Gate | 91517291445 | PASS | warmup 与采样证据完整 |
| 3 | Operator Precision Benchmark | 91517291606 | PASS | `5/5` |
| 3 | Build & Test | 91517291033 | FAILURE | 仅 UI E2E 21 项旧 fixture 失败 |

主 run URL：<https://github.com/HerverJun/ClearVision/actions/runs/30752858397>

配套远端 run：

| Workflow | Run ID | Job ID | Head SHA | 结果 |
| --- | ---: | ---: | --- | --- |
| ClearVision Vision Agent Safe CI | 30752858405 | 91509863775 | `ef4d1872...` | PASS |
| Vision Agent Quality Suite | 30752858398 | 91509863643 | `ef4d1872...` | PASS |

## 专项证据

以下结果均来自 head SHA `ef4d1872...` 的远端执行或其同一 run 的非过期 artifact。`Build & Test` 的 job conclusion 仍为 failure，不因下表中已完成的专项 lane 而被改写。

| Evidence | 结果 | 关键数据 |
| --- | --- | --- |
| Product coverage | PASS | `2428/2428`，2 个合法 skip；line `41.15%`，branch `33.86%` |
| Desktop coverage | PASS | `620/620` |
| TCP device regression | PASS | `9/9` |
| PPF PR smoke | PASS | `5/5` |
| PPF Nightly | PASS | `10/10` |
| Product Nightly | PASS | `1561/1561` |
| Test Governance | PASS | `3712` source definitions；unclassified/errors/warnings 均为 `0` |
| Measurement Performance | PASS | warmup `12`，measured `24`，总样本 `408`；Color p95 `26.3857 ms <= 33.75 ms`；Width p95 `21.1448 ms <= 45 ms` |
| Operator Precision | PASS | `5/5` |
| Safe CI | PASS | run `30752858405` |
| Vision Agent Quality | PASS | run `30752858398` |

最终非过期 artifact 记录：

| Artifact | ID |
| --- | ---: |
| `test-results` | `8835988985` |
| `nightly-quality-lane` | `8836007087` |
| `measurement-performance-report` | `8835032806` |
| `detection-performance-report` | `8835988212` |
| `quick-contract-quality-suite` | `8836003209` |
| `vision-agent-quality-suite` | `8836080653` |
| `ui-test-results` | `8836321425` |
| `operator-precision-benchmark` | `8835088719` |

artifact 的 head SHA 通过 GitHub run/job 元数据核验为 `ef4d1872a69b75099edb207a708227273abaa0ef`。同一 run 的重复 attempt 可能保留同名 artifact；本表记录的是可用的最终证据，而不是把 artifact 名称当作成功状态。

## 失败分类

### Measurement Performance

初始 SHA 的 Measurement 失败归类为 `PERFORMANCE_ENVIRONMENT_NOISE`，没有证据表明是产品算法回归。失败发生在独立 gate 尚未隔离 frontend setup、JIT/warmup 和采样边界时；本轮将 Measurement 从 `Build & Test` 内移为独立 job，先完成 frontend 安装和依赖构建，再以固定输入、固定迭代次数、明确 warmup 和 p95 统计采样。没有提高预算、删除慢样本或降低核心工作量。最终远端 `12/24/408` 采样通过，故该问题已闭环。

### Desktop SSE 与同步时序

基线的 Desktop Nightly `619/620` 归类为 `CI_OR_TIMING_FLAKE`，根因是 SSE 响应在 marker 后尚未收到完整事件分隔符，以及同步队列测试用普通线程调度放大 backpressure 时间竞争。本轮修复了等待完整事件边界和测试调度时序，最终 Desktop `620/620`。这与当前 `Build & Test` 的 UI E2E 失败是两条独立路径。

### Product Nightly 初始 16 项

基线 Product Nightly 的 16 项失败在后续远端 Nightly `1561/1561` 中全部关闭。逐项归因为旧 fixture 与当前 canonical resource/readiness、结果字段、资源路径或生成 manifest 漂移；未发现需要在本轮继续扩大范围的真实产品回归。

| # | Test | 分类 | 归因/闭环动作 |
| ---: | --- | --- | --- |
| 1 | `DeepLearningMultiTaskOperatorTests.ClassificationExplicitTask_ShouldRunRealOnnxInference` | `STALE_TEST_FIXTURE` | 当前分类输出不再提供旧测试期待的空 `DetectionList`；按当前分类输出契约校正断言。 |
| 2 | `VisionAgentConsolidationTests.CompleteLesionFlow` | `STALE_TEST_FIXTURE` | fixture 未提供 canonical camera resource decision；补齐 bound camera identity/readiness 输入。 |
| 3 | `BuildFromPlanEntryParityTests.LegacyContractWithoutHash_ShouldSucceedWithExplicitWarningThroughRunService` | `STALE_TEST_FIXTURE` | legacy warning 与当前 resource/readiness contract 漂移；使用当前显式资源决策语义。 |
| 4 | `VisionAgentBuildOrchestratorTests.BuildAsync_BlackBoxHoleDistance_ShouldProduceMeasurementDraftWithCalibrationBlockers` | `STALE_TEST_FIXTURE` | calibration blocker 的 resource type/key 已 canonical 化，旧 expected 仍使用 `measurement_parameter`。 |
| 5 | `BuildFromPlanEntryParityTests.ToolLoopMode_ShouldMatchAcrossAgentRunWebMessageAndInternalEntries` | `STALE_TEST_FIXTURE` | tool-loop entry 与当前 BuildFromPlan/agent-run 字段契约漂移，按当前入口一致性修正 fixture。 |
| 6 | `SemanticPlannerConfirmationBuild_MatureStrawberry_ShouldCreateEditableClassificationDraft` | `STALE_TEST_FIXTURE` | requirement mode 与当前可编辑 draft/build candidate 语义不一致，补齐当前 draft contract。 |
| 7 | `PreviewBuildReadiness_StationCamera_ShouldKeepCameraBindingResourcePending` | `STALE_TEST_FIXTURE` | station camera 的 pending/bound 判定已由 canonical resource evaluator 统一，旧 fixture 未带正确 identity。 |
| 8 | `CircleCaliperFitV2DatasetTests.Manifest_ShouldHaveStableIdentityVersionAndHash` | `STALE_TEST_FIXTURE` | 生成 manifest 内容已变化，更新与实际 manifest 对应的 sha256，不改变数据语义。 |
| 9 | `PreviewBuildReadiness_StrictAndDraftDefer_ShouldUseCanonicalEvaluator` | `STALE_TEST_FIXTURE` | strict/draft defer 预期仍绑定旧 readiness evaluator，改为共享当前 canonical evaluator。 |
| 10 | `MetalScratchScenario_ShouldBuildRealSurfaceDefectDraft` | `STALE_TEST_FIXTURE` | industrial scenario 使用旧 resource/confirmation 形态，改为当前 surface-defect draft schema。 |
| 11 | `HoleDistanceScenario_ShouldBuildCalibratedMeasurementDraft` | `STALE_TEST_FIXTURE` | hole-distance scenario 未按当前 calibration resource/readiness contract 提供输入。 |
| 12 | `ModifyExistingFlowScenario_ShouldPreserveExistingNodes` | `STALE_TEST_FIXTURE` | modify-flow fixture 与当前节点保留及 canonical build draft 结构不一致。 |
| 13 | `ResultAnalysisServiceIntegrationTests.ExportToCsvAsync_ShouldGenerateCorrectFormat` | `STALE_TEST_FIXTURE` | CSV 已采用兼容状态、执行结果、判定结果、CanonicalOutcome 和原因码等当前 schema，旧 header 过期。 |
| 14 | `ResultAnalysisServiceIntegrationTests.GetStatisticsAsync_WithResults_ShouldReturnCorrectStatistics` | `STALE_TEST_FIXTURE` | 统计 fixture 未填当前 canonical decision 计数，补齐 `ValidDecisionCount` 等现行结果字段。 |
| 15 | `RuntimeHost_RunPackageConfiguredSingleAsync_ShouldUsePackageConfiguredInputs` | `STALE_TEST_FIXTURE` | package-configured ImageAcquisition fixture 缺少有效 `FilePath`；使用仓库内稳定测试资源。 |
| 16 | `RuntimeHost_EventSubscribers_WhenOneThrows_ShouldContinuePublishing` | `STALE_TEST_FIXTURE` | 测试仍受旧 conditional ImageAcquisition/FilePath fixture 影响，先对齐 package-configured source contract 再验证事件发布语义。 |

上述 16 项修复均限于测试 fixture、测试断言、资源身份或生成 manifest 的当前契约对齐；没有通过 skip、quarantine、Nightly 排除项或降低门槛关闭失败。

## UI E2E 阻断

远端主 CI attempt 2、3 的失败集合完全相同，共 21 项，归类为 `STALE_TEST_FIXTURE`。本地 UI E2E 也稳定复现。当前资源绑定契约使用 `pick_model_resource` 和 WebView2 文件选择器，不再提供旧测试所期待的 `data-resource-input`/`bind_model_resource`；settings/PLC 使用领域专用 PUT API，不再接受旧的统一 `/api/settings` 写入；结果过滤项当前为 `NG`，旧测试选择 `Ng`；失败截图和快照属于旧版 AI Plan/Build 工作区。

| Spec | 失败行 |
| --- | --- |
| `ai-agent-responsive.spec.ts` | `1249`, `1458`, `1631`, `1724` |
| `ai-build-workspace.spec.ts` | `325`, `348`, `535`, `542`, `576`（viewport `1024`）、`576`（viewport `390`） |
| `ai-plan-clarification.spec.ts` | `676` |
| `flow-editor-port-contract.spec.ts` | `236` |
| `flow-layout-vm.spec.ts` | `698`, `884`, `938`, `1193`, `1266` |
| `high-frequency-regression.spec.ts` | `714`, `745`, `807` |
| `plc-settings.spec.ts` | `329` |

从 `INITIAL_SHA..CODE_SHA` 的变更清单看，不包含 `wwwroot`、UI E2E 测试文件或 UI snapshot；因此没有证据把这 21 项归因到 R2 的当前代码变更。R2 不批量更新 snapshot/expected，不加 skip/quarantine，也不修改 CI filter。

## CI DAG 前后变化

| 阶段 | R1/基线行为 | R2 行为 |
| --- | --- | --- |
| Measurement | 在 `Build & Test` job 中执行；失败会阻断同一 job 后续 Desktop coverage、Coverage Summary 和上传步骤。 | 拆为独立 `measurement-performance` job；完成 restore、Node/frontend setup、依赖 build 后运行，始终上传自身报告。 |
| Product/desktop evidence | 与 Measurement 共用 job 生命周期，性能失败可能使核心 coverage 证据缺失。 | `Build & Test` 不再包含 Measurement step；Product/TCP/PPF/Governance、Desktop coverage 和 Coverage Summary 独立完成。 |
| Artifact upload | 某些 step 失败或 skipped 时仍可能进入错误的上传路径，或无法区分“未执行”和“执行失败”。 | 对 operator benchmark、UI E2E 使用 step id；上传条件区分 `skipped`，`if-no-files-found: error` 保持证据真实性。 |
| Nightly measurement harness | 依赖默认冷启动采样。 | Nightly 设置 `CV_MEASUREMENT_PERF_WARMUP_ITERS=12`，报告显式记录 warmup/measured/sample metadata。 |

这次 DAG 修复的目标是“性能 gate 失败仍然是真失败，但不能截断其它证据”；最终主 CI 仍显示 failure，说明该目标没有通过 `continue-on-error` 被伪装成绿色。

## 本地证据

| 项目 | 结果 |
| --- | --- |
| P3 core contract runner | `381/381` |
| Desktop 定向回归 | `3/3`，重复运行稳定；SSE 和 sync backpressure 修复路径已验证。 |
| Product fixture/nightly 定向回归 | R2 失败集合与最终远端 Nightly 结果对照完成；最终远端 `1561/1561`。 |
| UI E2E | 稳定复现 21 个旧 fixture 失败，未修改产品或测试以隐藏失败。 |
| 工作树边界 | 主工作树和 `studio-ui-next` 未触碰；生成报告改动未纳入本报告提交。 |

## 残余风险与后续边界

1. 当前主 CI 仍不能作为全绿 release signal，原因是 `Build & Test` 的 21 项 UI E2E 失败；需要由 UI/契约迁移任务单独决定更新测试、补充兼容层或确认产品契约变更。
2. 当前 UI 失败虽然已归类为 `STALE_TEST_FIXTURE`，但不能仅凭分类删除；后续仍应逐项核对当前 frontend/webview/settings 契约并保留回归覆盖。
3. Measurement 当前在 `CODE_SHA` 上通过固定 warmup 与 24 次 measured sampling；后续若性能基准策略改变，应重新验证环境隔离、样本量和预算，不能只提高 allowed budget。
4. `REPORT_SHA` 仅用于定位本报告文档提交；被验证的代码、workflow、测试和 artifact 始终以 `CODE_SHA`/`REMOTE_CODE_SHA` 为准。

## 最终摘要

```text
INITIAL_SHA=1dad6ced67883c64470b98773cab25044fb4f786
CODE_SHA=ef4d1872a69b75099edb207a708227273abaa0ef
REPORT_SHA=3725d7e972aa265b4fe624253f412a6c120f47ea
REMOTE_CODE_SHA=ef4d1872a69b75099edb207a708227273abaa0ef

MEASUREMENT_FAILURE_CLASSIFICATION=PERFORMANCE_ENVIRONMENT_NOISE
MEASUREMENT_RESULT=PASS warmup=12 measured=24 samples=408

DESKTOP_FAILURE_CLASSIFICATION=CI_OR_TIMING_FLAKE (已修复)
DESKTOP_RESULT=PASS 620/620

PRODUCT_NIGHTLY_FAILURE_COUNT_BEFORE=16
PRODUCT_NIGHTLY_FAILURE_COUNT_AFTER=0
PRODUCT_NIGHTLY_CLASSIFICATION_COUNTS=STALE_TEST_FIXTURE=16

PRODUCT_PR_RESULT=PASS（远端 Build & Test 内的 Product lane）
PRODUCT_COVERAGE_RESULT=PASS 2428/2428，2 valid skips
DESKTOP_PR_RESULT=PASS
DESKTOP_COVERAGE_RESULT=PASS 620/620
COVERAGE_SUMMARY_RESULT=PASS
TCP_GATE_RESULT=PASS 9/9
PPF_PR_RESULT=PASS 5/5
PPF_NIGHTLY_RESULT=PASS 10/10
GOVERNANCE_RESULT=PASS 3712 definitions，0/0/0
SAFE_CI_RESULT=PASS
VISION_AGENT_QUALITY_RESULT=PASS

REMOTE_RUN_IDS=30752858397(attempt 1-3),30752858405,30752858398,30714868337(baseline)
ARTIFACT_HEAD_SHA_VERIFIED=ef4d1872a69b75099edb207a708227273abaa0ef
MAIN_WORKTREE_UNTOUCHED=true
STUDIO_UI_NEXT_UNTOUCHED=true
PUSH=SUCCESS；force=false fast-forward 到 3725d7e972aa265b4fe624253f412a6c120f47ea（Git 原生 push 因 Schannel TLS 失败，改用已认证 Git API）
G01B_R2_STATE=G01B_R2_BLOCKED_BY_REMOTE_CI
```
