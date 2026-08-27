---
title: "ClearVision T01-G01B-R3 UI 契约回归闭环计划"
doc_type: "plan"
status: "active"
topic: "测试治理 / UI E2E 契约"
created: "2026-08-28"
updated: "2026-08-28"
---

# ClearVision T01-G01B-R3 UI 契约回归闭环计划

## 当前责任边界

本计划是 T01-G01B-R2 遗留 UI E2E 契约问题的唯一 active 入口。历史 R2 报告在 `ef4d1872a69b75099edb207a708227273abaa0ef` 上记录了 21 个 `STALE_TEST_FIXTURE` 失败；该结论只代表当时 SHA，不直接声明当前 HEAD 仍失败。

原始执行和 CI 证据保存在 [T01-G01 测试治理阶段归档](../../../归档/已关闭事项/2026-08-28-T01-G01-测试治理阶段归档/闭环说明.md)。归档历史报告不等于关闭本计划，也不等于当前主 CI 已全绿。

## 历史失败清单

| Spec | 历史失败点 | 数量 |
| --- | --- | ---: |
| `ai-agent-responsive.spec.ts` | `1249`, `1458`, `1631`, `1724` | 4 |
| `ai-build-workspace.spec.ts` | `325`, `348`, `535`, `542`, `576`（viewport `1024` 和 `390`） | 6 |
| `ai-plan-clarification.spec.ts` | `676` | 1 |
| `flow-editor-port-contract.spec.ts` | `236` | 1 |
| `flow-layout-vm.spec.ts` | `698`, `884`, `938`, `1193`, `1266` | 5 |
| `high-frequency-regression.spec.ts` | `714`, `745`, `807` | 3 |
| `plc-settings.spec.ts` | `329` | 1 |
| **合计** |  | **21** |

历史报告将失败归因于资源绑定命令、WebView2 文件选择、settings/PLC 专用 API、`NG` 文案大小写和 AI Plan/Build 工作区快照等契约漂移。重新执行前不得把该分类复制为当前结论。

## 执行清单

- [ ] 在隔离、可复现的当前 HEAD 上运行上述 7 个 spec，记录 SHA、命令、discovered/passed/failed/skipped、浏览器版本和失败 artifact。
- [ ] 为 21 个历史失败点逐项建立“已消失 / 当前产品回归 / 测试 fixture 过期 / 环境或时序问题”映射，不允许按 spec 粗粒度一次性关闭。
- [ ] 以当前产品合同、endpoint 合同和用户可达行为为权威；fixture 过期时更新测试，产品回归时修产品或兼容层。
- [ ] 对 snapshot 变更逐张人工核对，不批量接受；不得用 skip、quarantine、过滤、无条件 retry 或放宽断言获得绿色。
- [ ] 定向通过后运行 UI Playwright 全量，并确认没有新增 skip 或测试人口塌缩。
- [ ] 在同一最终 SHA 上取得主 CI 的 UI E2E 和必要上游 job 结果，记录 run URL、job conclusion 和 artifact。
- [ ] 输出 R3 闭环报告，明确当前 HEAD 结果、21 项处置映射、残余风险和后续边界。

## 验收标准

- 21 个历史失败点全部有当前 HEAD 的逐项结论和可追溯证据。
- 定向 7 个 spec 与 UI Playwright 全量均通过，或者剩余失败已转入新的、具备 Owner 和验收条件的独立计划；不得留在归档报告中继续充当 active backlog。
- 没有新增 skip/quarantine、批量 snapshot 接受、测试过滤或人口下限回退。
- 最终远端证据绑定同一 SHA，主 CI 状态不得由本地结果推断。
- 更新本计划为 `closed` 并链接 R3 闭环报告后，才可关闭本责任入口。
