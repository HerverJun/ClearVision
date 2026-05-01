# Operator Quality Evidence Manifest

GeneratedAtUtc: `2026-04-29T05:01:39+00:00`
SourceMatrix: `quality/evals/reports/operator_quality_matrix.md`
MachineReadableManifest: `quality/evals/reports/operator_quality_evidence_manifest.json`

## Purpose

This manifest defines the evidence vocabulary used by the 155-operator quality matrix. It is intentionally conservative: a cell may say an operator is functionally usable, but industrial validation remains incomplete until real production-site data or sign-off is replayable and auditable.

## Machine Audit Contract

- `quality/evals/reports/operator_quality_evidence_manifest.json` must remain UTF-8 and parse as strict JSON before publication.
- Missing evidence is represented with the exact token `Not yet evidenced`.
- Pending industrial validation is represented with the exact token `功能可用但未完成现场工业验证`.
- Field-substitute replay is auditable regression evidence only; it is not real production-site sign-off.
- External real production-site data, customer sign-off, and line sign-off cannot be locally closed by this repository. Local work can only preserve the blocker as explicit evidence debt until an external evidence packet is attached.

## Evidence Layers

| Layer | Meaning | Minimum acceptance | Current gap |
|---|---|---|---|
| Contract | Deterministic API/port/parameter behavior evidence. | Happy path, missing input, parameter boundary, type/null boundary, structured failure message, recorded case count, zero accepted-run failures. | 39 operators are Not yet evidenced. |
| Golden | Synthetic oracle, geometry oracle, protocol oracle, or fixed regression baseline evidence. | At least 20 deterministic oracle/regression cases where applicable, fixed runner or baseline, recorded runtime/memory, zero accepted-run failures. | 109 operators are Not yet evidenced. |
| Dataset | Public/licensed/semi-synthetic/curated dataset-tier evidence with manifest and metrics. | Manifest with source/version or seed, deterministic split, metric thresholds, failure/boundary taxonomy, reproducible runner, and accepted baseline result. | 134 operators are Not yet evidenced; 1 operator is partially evidenced. |
| Field replay | Anonymized or field-substitute replay evidence with triage and regression conversion. | Replay manifest, replay command, triage labels, reproducible rate >= 0.80, regressionized rate >= 0.60, privacy/path leak checks, accepted baseline result. | 150 operators are Not yet evidenced; 5 operators have substitute replay only. |
| Precision Claim | Strongest currently supported precision basis per operator. | Claim must name the strongest evidence basis or say Not yet evidenced; it must not imply real-site validation unless industrial status supports it. | 99 contract-only claims and 35 golden-only claims need dataset/field promotion. |
| Industrial Status | Real production-site validation/sign-off state. | Real-site anonymized data or customer/line sign-off, replayable manifest, stable thresholds, and regressionized failures. | 155 operators remain `功能可用但未完成现场工业验证`; no operator has completed real industrial validation in this matrix. |

## Current Matrix Statistics

- Total operators: 155
- Contract: evidenced 116, Not yet evidenced 39
- Golden: evidenced 46, Not yet evidenced 109
- Dataset: evidenced 20, partial 1, Not yet evidenced 134
- Field replay: evidenced 5, Not yet evidenced 150, substitute-only 5
- Precision claim basis: field replay 5, dataset 16, partial dataset 0, golden 35, contract-only 99, Not yet evidenced 0
- Industrial validation complete: 0
- 功能可用但未完成现场工业验证: 155

## Audit Rules

- Do not treat `HasBenchmark=Yes` alone as a precision claim.
- Do not upgrade `IndustrialStatus` from `功能可用但未完成现场工业验证` unless the evidence includes real production-site data or explicit line/customer sign-off.
- Field replay drills in the current baseline are field-substitute evidence, not full real-site validation.
- Unknown or missing evidence must be written as `Not yet evidenced`, never left blank.
- Precision claims must degrade gracefully: Field replay > Dataset > Golden > Contract > `Not yet evidenced`.

## Remaining Evidence Categories

- Contract gaps: operators with `ContractEvidence = Not yet evidenced`.
- Golden gaps: operators with `GoldenEvidence = Not yet evidenced`.
- Dataset gaps: operators with `DatasetEvidence = Not yet evidenced` or partial evidence.
- Field replay gaps: operators with `FieldReplayEvidence = Not yet evidenced` plus field-substitute-only operators that still need real-site replay.
- Industrial validation gaps: every operator until a production-site evidence packet is attached.
