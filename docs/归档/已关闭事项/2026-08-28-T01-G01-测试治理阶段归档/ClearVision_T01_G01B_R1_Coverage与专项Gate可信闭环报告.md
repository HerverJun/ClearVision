# ClearVision T01-G01B-R1 Coverage 与专项 Gate 可信闭环报告

## 结论

本轮代码改动已在本地完成 G01B-R1 的主体闭环，未修改 PPF 产品算法、TCP 产品语义或现有 coverage 口径。最终远端证据必须以包含本报告的最终 SHA 为准，且必须同时取得 Product coverage、PPF 和 TCP 的实际 workflow artifact；普通 Safe CI 或 Vision Agent Quality 不替代这些证据。

## 修复范围

- 共享 `scripts/trx-validation.ps1` 解析 TRX 的 reported、observed、effective counters 和 skip reason。完整绿色要求 `total = executed + notExecuted`、`executed = passed`，并要求 failed、error、timeout、aborted、inconclusive、notRunnable、disconnected、warning、inProgress、pending、passedButRunAborted 全为零；合法 `NotExecuted` 不再被误判为失败。
- Product coverage、classified Gate 和 serial test runner 共用同一判定逻辑。Coverage manifest 升级为 v2，保留当前 Cobertura line/branch 口径，不设置 coverage threshold。
- `tcp-device-regression` 已进入 `.github/workflows/ci.yml` 的正式 Product quality Lane，并上传 named Gate artifact。CI 同时校验 Product PR、coverage 和 TCP 的 source population 关系。
- 两个 TCP segmented-response 测试使用确定性双 chunk stream，每次响应都断言完整内容和 `ReadCount == 2`，重复 20 轮。
- `PPFMatcherContractTests` 保留 5 个快速 PR smoke 逻辑回归；重型点云、多 seed、遮挡和对称性测试保留在 `PPFMatcherRegressionTests` 的 10 个 Nightly regression 用例。

## 人口与本地证据

本地运行代码 SHA 为 `674d26930c525708fb7228944063b17390da41f1`，运行时 worktree 仅包含本轮待提交修改。

| Gate | TRX total | executed | passed | notExecuted | failed/error/timeout/aborted | 结果 |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Product PR | 2439 | 2437 | 2437 | 2 | 0/0/0/0 | PASS |
| Product coverage | 2430 | 2428 | 2428 | 2 | 0/0/0/0 | PASS |
| TCP named Gate | 9 | 9 | 9 | 0 | 0/0/0/0 | PASS |
| PPF PR smoke | 5 | 5 | 5 | 0 | 0/0/0/0 | PASS |
| PPF Nightly regression | 10 | 10 | 10 | 0 | 0/0/0/0 | PASS |
| Desktop PR | 620 | 620 | 620 | 0 | 0/0/0/0 | PASS |

Coverage 的两个合法 skip 均来自 `FlowEditorTests`，TRX reason 为迁移到 Playwright/可启动 host 的 UI gate，当前有效期到 `2026-08-31`。本轮 Product coverage 的 Cobertura 数值为：line `41.16%`，branch `33.87%`。

Test Governance：source definitions `3712`，unclassified `0`，errors `0`，warnings `0`。

人口防塌缩规则保留正常增删能力：Product coverage 的 TRX total 只要求不低于 `minimumTotalTests=2000`；CI 另外校验 `product-pr source population - tcp-device-regression source population = product-coverage source population`。当前 source population 为 `2053 - 9 = 2044`，不是固定测试数量阈值。

## 远端最终 SHA 证据要求

推送后使用最终 SHA 重新确认以下实际执行结果，并在交付汇报中记录 workflow run URL、`headSha`、job conclusion 和 artifact 名称：

| 证据 | 必须包含 |
| --- | --- |
| Product coverage | `TestResults/product/product-coverage.json`、Cobertura、TRX counters、两个合法 skip 和 current-HEAD 校验 |
| TCP | `TestResults/tcp-device-regression/tcp-device-regression.gate.json`，gate 为 `tcp-device-regression` |
| PPF PR | `TestResults/ppf-pr-smoke/ppf-pr-smoke.gate.json`，gate 为 `ppf-pr-smoke` |
| PPF Nightly | `TestResults/nightly/ppf-regression.gate.json`，gate 为 `ppf-regression` |
| Governance | `TestResults/governance/test-governance.json`，unclassified/errors/warnings 为零 |
| Safe CI | `ClearVision Vision Agent Safe CI` 的最终 SHA run 为 success |

只有上述专项 artifact 均绑定同一最终 SHA 且成功，才能将状态记为 `G01B_R1_COMPLETE`。
