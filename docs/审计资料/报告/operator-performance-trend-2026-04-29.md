# Operator Performance Trend Report

- Current report: `docs/审计资料/报告/operator-performance-benchmark-current.json`
- Current generated UTC: `2026-04-29T01:09:07.8697545+00:00`
- Current mode: `smoke`
- Current cases passed: 4/4
- Baseline report: not provided

No baseline was supplied, so this report records the current run as the first comparable point.

## Case Delta

| Case | Baseline mean ms | Current mean ms | Delta | Baseline alloc/iter | Current alloc/iter | Passed |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| caliper_tool_horizontal_edge_pair | n/a | 1.445 | n/a | n/a | 162512 | True |
| edge_detection_640x480_auto_threshold | n/a | 1.818 | n/a | n/a | 13200 | True |
| mean_filter_640x480_k5 | n/a | 1.012 | n/a | n/a | 3245 | True |
| translation_rotation_calibration_20_points_svd | n/a | 0.220 | n/a | n/a | 70768 | True |

## Gate Notes

- `smoke` mode is suitable for CI signal and trend drift detection.
- Release gates should compare against a pinned baseline from the same machine profile.
- A single local run is not a release conclusion by itself.
