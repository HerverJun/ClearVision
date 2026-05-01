# Quality Flywheel Field Replay Drill

DrillId: `2026-04-drill-03`
GeneratedAtUtc: `2026-04-28T17:29:21+00:00`
Manifest: `quality/field_replay/manifests/field_replay_manifest.json`

## Summary

- Operators covered: 5
- Samples replayed: 100
- Reproducible rate: 90.00%
- Regressionized rate: 70.00%
- Privacy/raw-path leaks: 0/0
- Drill passed: Yes

## Operators

| Operator | Samples | Reproducible | Regressionized | Replay Tier | Labels |
|---|---:|---:|---:|---|---|
| DeepLearning | 20 | 18 | 13 | field-substitute | p1, ai-detection, postprocess, runtime |
| TemplateMatching | 20 | 17 | 12 | field-substitute | p1, matching, homography |
| CaliperTool | 20 | 19 | 15 | field-substitute | p1, measurement, edge-pair |
| SurfaceDefectDetection | 20 | 18 | 11 | field-substitute | p1, inspection, surface-defect |
| CameraCalibration | 20 | 18 | 12 | field-substitute | p1, calibration, geometry |

## Gate Interpretation

- P0/P1 triage SLA is represented by manifest metadata and validated during replay manifest checks.
- Samples in this seed set are anonymized field-substitute records; raw customer paths are forbidden.
- A drill can pass only when reproducible rate and regressionization rate meet the manifest policy.
