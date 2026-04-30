# EdgeDetection BSDS500 Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T01:49:18.0572960+00:00`
Dataset: `BSDS500 human boundary annotations`
Index: `quality/datasets/bsds500_index.json`
Split: `test`
CandidateVersion: `v1`
Profile: `fixed_50_150_l2`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 20 |
| Failed | 0 |
| Human annotations | 102 |
| Total pixels | 3088020 |
| Predicted edge pixels | 306598 |
| Union boundary pixels | 123061 |
| Consensus boundary pixels | 11986 |
| Boundary precision | 0.1811 |
| Boundary recall | 0.6292 |
| Boundary F1 | 0.2812 |
| Consensus boundary precision | 0.0704 |
| Consensus boundary recall | 0.8395 |
| Consensus boundary F1 | 0.1299 |
| Boundary tolerance px | 2 |
| Runtime ms avg | 21.236 |
| Runtime ms p95 | 21.265 |

## Failure Boundaries

- `mat_annotation_parse_failure`: baseline fails if any selected MATLAB v5 file cannot expose at least one `Boundaries` dense or sparse matrix.
- `operator_execution_failure`: baseline fails if product `CannyEdgeOperator` cannot process a selected BSDS500 image.
- `low_contrast_boundary`: tracked by low recall against human boundary union/consensus.
- `high_texture_false_positive`: tracked by low precision against dilated human boundaries.
- `thin_boundary_miss`: tracked by consensus recall with a fixed 2 px tolerance.
- Quality metrics are observational for this first real-data gate; pass/fail is reserved for parser and product execution integrity.

## Splits

| Split | Cases | Passed | Failed | Boundary F1 avg | Consensus F1 avg | Runtime ms avg |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| test | 20 | 20 | 0 | 0.2867 | 0.1451 | 21.236 |

## Cases

| Case | Split | Passed | Size | Annotations | Thresholds | Union F1 | Consensus F1 | Predicted | Union | Consensus | Runtime ms | Failure |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 101027 | test | True | 481x321 | 5 | 50/150 | 0.3285 | 0.1467 | 14605 | 7296 | 639 | 71.367 | - |
| 103006 | test | True | 481x321 | 5 | 50/150 | 0.3351 | 0.0977 | 19608 | 7213 | 482 | 20.01 | - |
| 108069 | test | True | 481x321 | 5 | 50/150 | 0.172 | 0.0712 | 17261 | 3181 | 330 | 18.834 | - |
| 109055 | test | True | 481x321 | 5 | 50/150 | 0.2328 | 0.1096 | 14681 | 6924 | 401 | 18.329 | - |
| 141012 | test | True | 481x321 | 5 | 50/150 | 0.1781 | 0.0513 | 29773 | 4668 | 287 | 18.285 | - |
| 159022 | test | True | 481x321 | 5 | 50/150 | 0.3221 | 0.1669 | 6072 | 6804 | 296 | 18 | - |
| 160067 | test | True | 481x321 | 5 | 50/150 | 0.2934 | 0.1521 | 13702 | 5184 | 641 | 17.89 | - |
| 164046 | test | True | 321x481 | 5 | 50/150 | 0.4444 | 0.2627 | 6405 | 4044 | 448 | 18.086 | - |
| 196088 | test | True | 481x321 | 5 | 50/150 | 0.339 | 0.1085 | 31749 | 9099 | 652 | 19.11 | - |
| 202000 | test | True | 481x321 | 5 | 50/150 | 0.2279 | 0.1354 | 13331 | 9636 | 1058 | 18.483 | - |
| 223060 | test | True | 481x321 | 6 | 50/150 | 0.3281 | 0.2059 | 18349 | 6849 | 1183 | 21.265 | - |
| 232076 | test | True | 481x321 | 5 | 50/150 | 0.2793 | 0.1399 | 11961 | 6104 | 444 | 19.839 | - |
| 302022 | test | True | 321x481 | 5 | 50/150 | 0.2002 | 0.1067 | 14110 | 5677 | 599 | 18.34 | - |
| 306052 | test | True | 481x321 | 5 | 50/150 | 0.3124 | 0.1216 | 9546 | 5194 | 420 | 18.875 | - |
| 326085 | test | True | 321x481 | 5 | 50/150 | 0.2727 | 0.1072 | 17338 | 5599 | 487 | 18.234 | - |
| 33044 | test | True | 321x481 | 5 | 50/150 | 0.3711 | 0.1588 | 17809 | 6420 | 663 | 18.772 | - |
| 41096 | test | True | 481x321 | 6 | 50/150 | 0.2652 | 0.1614 | 13980 | 4249 | 877 | 20.16 | - |
| 48017 | test | True | 481x321 | 5 | 50/150 | 0.2776 | 0.1236 | 13997 | 5212 | 555 | 15.986 | - |
| 49024 | test | True | 481x321 | 5 | 50/150 | 0.2709 | 0.3054 | 2712 | 6629 | 435 | 16.601 | - |
| 97010 | test | True | 481x321 | 5 | 50/150 | 0.2822 | 0.169 | 19609 | 7079 | 1089 | 18.256 | - |
