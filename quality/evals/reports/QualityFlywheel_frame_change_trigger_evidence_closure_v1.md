# QualityFlywheel FrameChangeTrigger Evidence Closure v1

GeneratedAtUtc: `2026-05-20T15:05:00+00:00`

## Closure Summary

| Evidence layer | Source | Result | Matrix status |
|---|---|---:|---|
| Contract | `FrameChangeTrigger_contract_baseline.*` | 31/31 passed | HasContractTest=Yes |
| Dataset | `FrameChangeTrigger_dataset_baseline.*` | 140/140 passed | HasDatasetEvidence=Yes |
| Field-substitute replay | `FrameChangeTrigger_field_substitute_baseline.*` | 20/20 passed | HasFieldReplay=Yes |

`quality/evals/reports/operator_quality_matrix.md` now reports `Any evidence signal: Yes=156`; the `FrameChangeTrigger` row is `Contract=Yes`, `Dataset=Yes`, `Field=Yes`, `Any evidence signal=Yes`.

## Dataset Gate

| Metric | Value | Gate |
|---|---:|---:|
| Trigger Precision | 1.0000 | >= 0.9800 |
| Trigger Recall | 1.0000 | >= 0.9500 |
| Duplicate Suppression Rate | 1.0000 | >= 0.9800 |
| Static/Noise False Trigger Rate | 0.0000 | <= 0.0200 |
| P95 Runtime ms for 256x256 ROI | 0.285 | <= 3.000 |

## Field-Substitute Boundary

The replay path is:

`ImageAcquisition(Continuous) -> FrameChangeTrigger -> DeepLearning -> BoxFilter -> BoxNms -> DetectionSequenceJudge -> ResultOutput`

No-material frames produced 0 downstream executions. Arrival frames produced 14 downstream executions for 14 annotated trigger frames. This is field-substitute evidence only and does not claim real production-site or customer line sign-off.

## Prevention Rule

`quality/tools/generate_operator_quality_matrix.py` now refuses to generate a current matrix when any formal catalog operator has no accepted contract, golden, dataset, or field evidence signal. `--allow-no-evidence` is reserved for pre-closure audit snapshots.
