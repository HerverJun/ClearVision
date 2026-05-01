# Quality Flywheel Release Gate

GeneratedAtUtc: `2026-04-29T00:00:00Z`
Passed: `Yes`

## Checks

| Check | Status | Details |
|---|---|---|
| matrix_total_operators | Pass | 155 |
| matrix_all_a_level | Pass | A=155 |
| matrix_all_have_evidence_signal | Pass | Yes=155 |
| matrix_no_card_todo | Pass | 0 |
| g3_dataset_20_closed | Pass | 20 operators, failed=0 |
| field_replay_baseline_passed | Pass | passed=True, samples=100 |
| field_replay_three_consecutive_drills | Pass | quality/evals/reports/field_replay_drill_2026_04_01.json, quality/evals/reports/field_replay_drill_2026_04_02.json, quality/evals/reports/field_replay_drill_2026_04_03.json |
| quick_suite_budget | Pass | 10 minutes |
| dataset_suite_manual_or_nightly_budgeted | Pass | 120 minutes |

## Release Rule

A new or materially changed core operator must carry contract/golden/dataset/field evidence, or an explicit waiver with owner and expiry, before it can be advertised as production-trustworthy A level.
