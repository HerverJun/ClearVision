# Operator Quality Evidence Manifest

GeneratedAtUtc: `2026-05-20T15:05:00+00:00`
SourceMatrix: `quality/evals/reports/operator_quality_matrix.md`
SourceCatalog: `docs/算子资料/算子名片/CATALOG.md`

## Purpose

This manifest defines the evidence vocabulary used by the 156-operator quality matrix. It is intentionally conservative: a row may say an operator is functionally usable, but industrial validation remains incomplete until real production-site data or sign-off is replayable and auditable.

## Machine Audit Contract

- `quality/evals/reports/operator_quality_matrix.md` must remain UTF-8 Markdown and be regenerated from current operator cards before release claims are updated.
- Ordinary xUnit tests are product regression coverage only; they do not count as quality contract evidence unless an accepted quality runner writes a baseline report under `quality/evals/reports/*_baseline.json`.
- Missing evidence is represented as `No` in the current matrix summary and row-level evidence columns.
- `Partial` means some evidence exists but is not sufficient to promote the layer to full coverage.
- Field-substitute replay is auditable regression evidence only; it is not real production-site sign-off.
- External real production-site data, customer sign-off, and line sign-off cannot be locally closed by this repository. Local work can only preserve the blocker as explicit evidence debt until an external evidence packet is attached.

## Evidence Layers

| Layer | Meaning | Minimum acceptance | Current gap |
|---|---|---|---|
| Contract | Deterministic API/port/parameter behavior evidence. | Happy path, missing input, parameter boundary, type/null boundary, structured failure message, recorded case count, zero accepted-run failures. | 39 operators are not yet evidenced. |
| Golden | Synthetic oracle, geometry oracle, protocol oracle, or fixed regression baseline evidence. | At least 20 deterministic oracle/regression cases where applicable, fixed runner or baseline, recorded runtime/memory, zero accepted-run failures. | 109 operators are not yet evidenced; 1 operator is partially evidenced. |
| Dataset | Public/licensed/semi-synthetic/curated dataset-tier evidence with manifest and metrics. | Manifest with source/version or seed, deterministic split, metric thresholds, failure/boundary taxonomy, reproducible runner, and accepted baseline result. | 133 operators are not yet evidenced; 6 operators are partially evidenced. |
| Field replay | Anonymized or field-substitute replay evidence with triage and regression conversion. | Replay manifest, replay command, triage labels, reproducible rate, regressionized rate, privacy/path leak checks, accepted baseline result. | 134 operators are not yet evidenced. |
| Industrial Status | Real production-site validation/sign-off state. | Real-site anonymized data or customer/line sign-off, replayable manifest, stable thresholds, and regressionized failures. | No operator should be described as real-site signed off unless an external production evidence packet is attached. |

## Current Matrix Statistics

- Total operators: 156
- Level counts: A=152, B=4
- Priority counts: P1=3, P2=30, P3=123
- Any evidence signal: Yes=156
- Contract: evidenced 117, not yet evidenced 39
- Golden: evidenced 46, partial 1, not yet evidenced 109
- Dataset: evidenced 17, partial 6, not yet evidenced 133
- Field replay: evidenced 22, not yet evidenced 134
- Cards with TODO: 0
- P0 without evidence signal: 0
- C-level without evidence signal: 0

## Audit Rules

- Do not treat `HasBenchmark=Yes` alone as a precision claim.
- Do not claim real industrial validation unless the evidence includes real production-site data or explicit line/customer sign-off.
- Field replay drills in the current baseline are field-substitute evidence, not full real-site validation.
- Unknown or missing evidence must stay visible as `No`/not evidenced, never be left blank.
- Formal catalog operators must not enter the current quality matrix with `Any evidence signal = No`; `quality/tools/generate_operator_quality_matrix.py` fails by default unless `--allow-no-evidence` is explicitly used for a pre-closure audit snapshot.

## FrameChangeTrigger Evidence Closure

`FrameChangeTrigger` closes its no-evidence gap through the following accepted local evidence:

- Contract baseline: `quality/evals/reports/FrameChangeTrigger_contract_baseline.json` and `.md`, 31/31 passed, EvidenceKind=`contract`.
- Dataset baseline: `quality/evals/reports/FrameChangeTrigger_dataset_baseline.json` and `.md`, 140/140 passed, EvidenceKind=`dataset`, fixed seed `20260518`.
- Dataset manifest: `quality/datasets/manifests/FrameChangeTrigger_synthetic_arrival_manifest.json`, recording seed, frame size, ROI, labels, expected trigger frames, noise models, and repo-local synthetic license state.
- Field-substitute replay: `quality/evals/reports/FrameChangeTrigger_field_substitute_baseline.json` and `.md`, 20/20 passed, EvidenceKind=`field`.
- Closure roll-up: `quality/evals/reports/QualityFlywheel_frame_change_trigger_evidence_closure_v1.md`.
- Boundary: field-substitute replay is auditable regression evidence for short-circuit semantics, not real production-site sign-off.

## Remaining Evidence Categories

- Contract gaps: operators with `HasContractTest = No`.
- Golden gaps: operators with `HasGoldenTest = No` or `Partial`.
- Dataset gaps: operators with `HasDatasetEvidence = No` or `Partial`.
- Field replay gaps: operators with `HasFieldReplay = No`.
- Industrial validation gaps: every operator until a production-site evidence packet is attached.
