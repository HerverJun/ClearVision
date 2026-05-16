# Operator Quality Evidence Manifest

GeneratedAtUtc: `2026-05-16T06:53:09+00:00`
SourceMatrix: `quality/evals/reports/operator_quality_matrix.md`
SourceCatalog: `docs/算子资料/算子名片/CATALOG.md`

## Purpose

This manifest defines the evidence vocabulary used by the 156-operator quality matrix. It is intentionally conservative: a row may say an operator is functionally usable, but industrial validation remains incomplete until real production-site data or sign-off is replayable and auditable.

## Machine Audit Contract

- `quality/evals/reports/operator_quality_matrix.md` must remain UTF-8 Markdown and be regenerated from current operator cards before release claims are updated.
- Missing evidence is represented as `No` in the current matrix summary and row-level evidence columns.
- `Partial` means some evidence exists but is not sufficient to promote the layer to full coverage.
- Field-substitute replay is auditable regression evidence only; it is not real production-site sign-off.
- External real production-site data, customer sign-off, and line sign-off cannot be locally closed by this repository. Local work can only preserve the blocker as explicit evidence debt until an external evidence packet is attached.

## Evidence Layers

| Layer | Meaning | Minimum acceptance | Current gap |
|---|---|---|---|
| Contract | Deterministic API/port/parameter behavior evidence. | Happy path, missing input, parameter boundary, type/null boundary, structured failure message, recorded case count, zero accepted-run failures. | 40 operators are not yet evidenced. |
| Golden | Synthetic oracle, geometry oracle, protocol oracle, or fixed regression baseline evidence. | At least 20 deterministic oracle/regression cases where applicable, fixed runner or baseline, recorded runtime/memory, zero accepted-run failures. | 109 operators are not yet evidenced; 1 operator is partially evidenced. |
| Dataset | Public/licensed/semi-synthetic/curated dataset-tier evidence with manifest and metrics. | Manifest with source/version or seed, deterministic split, metric thresholds, failure/boundary taxonomy, reproducible runner, and accepted baseline result. | 134 operators are not yet evidenced; 6 operators are partially evidenced. |
| Field replay | Anonymized or field-substitute replay evidence with triage and regression conversion. | Replay manifest, replay command, triage labels, reproducible rate, regressionized rate, privacy/path leak checks, accepted baseline result. | 135 operators are not yet evidenced. |
| Industrial Status | Real production-site validation/sign-off state. | Real-site anonymized data or customer/line sign-off, replayable manifest, stable thresholds, and regressionized failures. | No operator should be described as real-site signed off unless an external production evidence packet is attached. |

## Current Matrix Statistics

- Total operators: 156
- Level counts: A=152, B=4
- Priority counts: P1=3, P2=30, P3=123
- Any evidence signal: Yes=155, No=1
- Contract: evidenced 116, not yet evidenced 40
- Golden: evidenced 46, partial 1, not yet evidenced 109
- Dataset: evidenced 16, partial 6, not yet evidenced 134
- Field replay: evidenced 21, not yet evidenced 135
- Cards with TODO: 0
- P0 without evidence signal: 0
- C-level without evidence signal: 0

## Audit Rules

- Do not treat `HasBenchmark=Yes` alone as a precision claim.
- Do not claim real industrial validation unless the evidence includes real production-site data or explicit line/customer sign-off.
- Field replay drills in the current baseline are field-substitute evidence, not full real-site validation.
- Unknown or missing evidence must stay visible as `No`/not evidenced, never be left blank.
- `FrameChangeTrigger` is now in the formal 156-operator catalog, but currently has no evidence signal in the matrix; it should receive contract and error-path coverage before release claims rely on it.

## Remaining Evidence Categories

- Contract gaps: operators with `HasContractTest = No`.
- Golden gaps: operators with `HasGoldenTest = No` or `Partial`.
- Dataset gaps: operators with `HasDatasetEvidence = No` or `Partial`.
- Field replay gaps: operators with `HasFieldReplay = No`.
- Industrial validation gaps: every operator until a production-site evidence packet is attached.
