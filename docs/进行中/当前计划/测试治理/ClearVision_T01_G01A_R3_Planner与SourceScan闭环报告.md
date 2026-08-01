# ClearVision T01-G01A-R3 Planner 与 Source-Scan 闭环报告

## 结论

- Planner Autonomy 已恢复为 `21/21`（Planner `15` 个案例 + 权限负例 `6` 个），`accepted=true`。
- Business Benchmark 保持 `120/120`，`accepted=true`。
- Artifact source-scan 已通过，`forbiddenHitCount=0`。
- Vision Agent Quality suite 已通过：backend `615/615`、UI `397/397`、desktop endpoint `44/44`、Business `120/120`、RuntimePreview `60/60`、Planner `21/21`，进程退出码 `0`。
- 当前退出状态：`G01A_R3_QUALITY_CHAIN_GREEN_PPF_COVERAGE_DEFERRED`。本轮要求的正式质量链为绿；已知 PPFMatcher coverage 阻断按要求继续保留且未运行 Product 全量 coverage。

## 基线与提交边界

- 隔离工作树：`C:\cv-t01-g01a-r1-wt-20260730-213926`
- 本轮起始 HEAD：`d1abc87fc96301331739789877e9e3d95a04331b`
- 保留且未改写：
  - `47d7494688cf335cfb3d3ead1f3fcdc67fea2eec`
  - `5887387c0f9bc03489df994adbda5b0f2f6b039d`
  - `d1abc87fc96301331739789877e9e3d95a04331b`
- 本轮第一笔提交：`3c83e48d`，`test(agent): align planner benchmark with canonical resources`。
- 第二笔提交包含本报告、source-scan 修复和其余最终生成产物；不改写上述既有提交。

## Planner 根因与修复

三项原失败（`VA-PL-002`、`VA-PL-004`、`VA-PL-008`）不是产品 readiness、validation 或 precheck 合同的问题，而是 Planner Benchmark fixture 没有同步当前 canonical resource contract：

1. ready flow builder 仍在部分相机输入上写入旧的 `CameraBindingId`，而当前 canonical 参数是 `ImageAcquisition.CameraId`。
2. ready template matching flow 只提供了 `TemplateId` 元数据，没有通过 metadata-only 的模板输入源连接 `TemplateMatching.Template`；产品 precheck 正确地将该 canonical 输入判定为未绑定。
3. manual confirmation helper 按旧参数别名生成了非 canonical `resourceKey`，没有使用规范化 resource type、operator ordinal 和 canonical parameter 组成的 `canonicalId`，因此确认没有与 precheck 的资源身份相交。

修复仅在 `quality/tools/VisionAgentPlannerAutonomyBenchmarkRunner/Program.cs`：ready builder 统一使用 `CameraId`；template ready flow 增加 metadata-only template source 和 `Template` connection；confirmation 使用 canonical identity，并写入 `canonicalId`、`status=bound`、`valueSummary`。没有通过修改期望值、降低门槛或改变产品合同来使 benchmark 通过。

`VA-PL-005/006/007` 的故意缺资源语义保持有效：分别缺 `CameraId`、`ModelPath`、`Template`，仍为 `readyForDeployment=false`、可生成 workflow draft 但被部署阻断。Planner 仍为 15 个案例，权限负例仍为 6 个；ready case 没有改成 `expectedPrecheckReady=false`。

## Source-scan 命中与修复

初始命中：

- 文件：`ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/unit/inspection-controller-memory.test.mjs`
- 规则：`Authorization bearer literal`
- 脱敏后的命中类型：UI 单测中用于模拟图片请求鉴权的 fake bearer token 示例，即 `Bearer inspection-image-token`；不是真实凭据，也没有私有端点或真实服务地址。

修复将该示例缩短为 `Bearer image-token`，同时更新同一断言，保持测试对 Authorization header 传递行为的覆盖。未关闭扫描、未扩大目录排除、未降级为 warning。最终扫描：`3302` 个 source files、`33` 个 reports、`forbiddenHitCount=0`、`redactionPass=true`。

## 正式验证

| Gate | 结果 | 证据或命令 |
| --- | --- | --- |
| Planner Autonomy | PASS `21/21`, `accepted=true` | `quality/evals/reports/planner_autonomy_benchmark.json` |
| Business Benchmark | PASS `120/120`, `accepted=true` | `quality/evals/reports/VisionAgent_business_benchmark_baseline.json` |
| Vision Agent Quality | PASS；backend `615/615`、UI `397/397`、desktop endpoint `44/44`、Business `120/120`、RuntimePreview `60/60`、Planner `21/21` | `python quality/tools/run_quality_suite.py --suite agent_engineering_harness_suite --run` |
| Artifact source-scan | PASS；`3302` source files，`0` forbidden hits | `python quality/tools/assert_vision_agent_report_artifacts.py --scan-source-files --write-manifest quality/evals/reports/vision_agent_quality_artifact_manifest.json --write-report quality/evals/reports/vision_agent_quality_artifact_manifest.md` |
| Product PR | PASS；`2448 total / 2446 passed / 2 skipped / 0 failed` | `C:\cv-t01-g01a-r3-evidence-20260801\formal\product-pr\product-pr.gate.json` |
| Safe CI Desktop PR | PASS；`619/619` | `C:\cv-t01-g01a-r3-evidence-20260801\formal\safe-ci\desktop-pr\desktop-pr.gate.json` |
| Safe CI JavaScript syntax | PASS；`31` files | workflow-equivalent `node --check` |
| Safe CI UI contract | PASS；`397/397` | `npm run test:agent-ui-contract` |
| Safe CI diff checks | PASS | `resolve-ci-diff-base.ps1 -SelfTest` and `check-diff-hygiene.ps1 -BaseRef origin/codex初稿` |
| Changed UI unit test | PASS；`17/17` | `inspection-controller-memory.test.mjs` |
| Desktop Debug build | PASS；`0` warnings，`0` errors | formal local build |
| Test Governance | PASS；`3710` definitions，`0` issues | `C:\cv-t01-g01a-r3-evidence-20260801\formal\governance\test-governance.json` |

## 变更范围与合同确认

修改内容仅限 Planner Benchmark runner、Planner/Business/RuntimePreview/quality suite 生成报告、artifact manifest、source-scan 命中的 UI 专用测试和本阶段报告。未修改产品源码、canonical resource contract、readiness、validation、precheck、PPFMatcher、coverage、CI workflow、FrontendV2、StudioUI、Playwright，也未改动已完成的 Unicode 与 BuildFromPlan 修复。

远端 `codex初稿` SHA 与推送资格必须在所有提交完成后用 Git 原生命令重新确认；该 handoff 结果不从本地报告推断。TLS 失败或 SHA 不精确匹配时不得推送。

PPFMatcher coverage 的已知问题继续保留。本轮没有运行会被该问题阻断的 Product 全量 coverage。
