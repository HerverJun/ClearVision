# 算子审计

本目录保存算子工业级审计的计划入口、执行台账、证据链、优先级清单和周报。

## 当前入口

- [算子库质量审计报告（2026-08-25）](./operator-library-quality-audit-2026-08-25.md)
- [算子库质量审计矩阵（158/158）](./operator-library-quality-audit-matrix-2026-08-25.csv)
- [算子库质量审计记录（机器可读）](./operator-library-quality-audit-record-2026-08-25.json)
- [算子工业级审计 TODO](./TODO.md)
- [审计执行标准](./审计执行标准.md)
- [类别审计报告模板](./类别审计报告模板.md)
- [Week5 入口卡片](./Week5-入口卡片.md)

## 2026-08-25 全量质量审计

本批次按 158 个规范算子逐项核查端口、属性、运行时和预览工作台，确认严重问题 5 项、一般问题 2 项，另保留待确认风险 4 项。审计只产出报告和证据，不修改产品源码；开放问题及验证边界已同步到 [`TODO.md`](./TODO.md)。

| 证据 | 用途 |
|---|---|
| [`operator-library-quality-audit-2026-08-25.md`](./operator-library-quality-audit-2026-08-25.md) | 最终人工结论、证据、复现、影响和修复建议 |
| [`operator-library-quality-audit-matrix-2026-08-25.csv`](./operator-library-quality-audit-matrix-2026-08-25.csv) | 158 个算子的端口、属性、静态信号和预览复核矩阵 |
| [`operator-library-quality-audit-record-2026-08-25.json`](./operator-library-quality-audit-record-2026-08-25.json) | 最终数量口径、测试边界、文件 SHA-256 和优先级 |
| [`operator-library-quality-audit-static-evidence-2026-08-25.json`](./operator-library-quality-audit-static-evidence-2026-08-25.json) | 完整机器可读静态证据 |
| [`operator-library-quality-audit-static-summary-2026-08-25.json`](./operator-library-quality-audit-static-summary-2026-08-25.json) | 静态扫描摘要；其中 `confirmedCount=2` 不是最终人工缺陷总数 |

## 文档分工

| 文件类型 | 用途 |
|---|---|
| `启动计划/WeekN-长时启动计划.md` | 单周执行入口 |
| `WeekN-审计台账.md` | 主执行记录、范围冻结、静态盘点 |
| `WeekN-已升级证据链.md` | 脚本、样本、结果和复核状态 |
| `WeekN-修复优先级清单.md` | 已核验证据支撑的问题池 |
| `WeekN-审计周报.md` | 阶段结论、遗留和移交 |
| `TODO.md` | 全局分类状态和滚动 DoD |
| `operator-library-quality-audit-*` | 2026-08-25 全量端口、属性、执行和预览审计证据包 |

## 维护规则

- 新问题进入修复清单前必须有证据路径或复现入口。
- 周内结果回填到既有 Week 文档，不新建零散子计划。
- 已关闭的审计阶段迁入 `docs/归档/已关闭事项/`，当前目录只保留仍有维护价值的入口和台账。
