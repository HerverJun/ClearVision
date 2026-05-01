# EdgeDetection BSDS500 Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-05-01T03:59:17.3938579+00:00`
Dataset: `BSDS500 human boundary annotations`
Index: `quality/datasets/bsds500_index.json`
Split: `test`
CandidateVersion: `recall_not_lower_v2`
Profile: `canny_l2_50_150`

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
| Predicted to boundary mean distance px | 28.783 |
| Boundary to predicted mean distance px | 12.85 |
| Predicted to consensus mean distance px | 42.8076 |
| Consensus to predicted mean distance px | 4.5995 |
| Boundary tolerance px | 2 |
| Runtime ms avg | 25.822 |
| Runtime ms p95 | 28.028 |

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
| test | 20 | 20 | 0 | 0.2867 | 0.1451 | 25.822 |

## Cases

| Case | Split | Passed | Size | Annotations | Thresholds | Union F1 | Union B->P px | Consensus F1 | Consensus B->P px | Predicted | Union | Consensus | Runtime ms | Failure |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 101027 | test | True | 481x321 | 5 | 50/150 | 0.3285 | 5.1463 | 0.1467 | 5.5381 | 14605 | 7296 | 639 | 77.896 | - |
| 103006 | test | True | 481x321 | 5 | 50/150 | 0.3351 | 3.9327 | 0.0977 | 1.3047 | 19608 | 7213 | 482 | 24.268 | - |
| 108069 | test | True | 481x321 | 5 | 50/150 | 0.172 | 3.3514 | 0.0712 | 2.466 | 17261 | 3181 | 330 | 22.546 | - |
| 109055 | test | True | 481x321 | 5 | 50/150 | 0.2328 | 22.2958 | 0.1096 | 2.4301 | 14681 | 6924 | 401 | 22.755 | - |
| 141012 | test | True | 481x321 | 5 | 50/150 | 0.1781 | 3.9785 | 0.0513 | 1.0918 | 29773 | 4668 | 287 | 27.62 | - |
| 159022 | test | True | 481x321 | 5 | 50/150 | 0.3221 | 13.2905 | 0.1669 | 3.9553 | 6072 | 6804 | 296 | 22.718 | - |
| 160067 | test | True | 481x321 | 5 | 50/150 | 0.2934 | 3.2372 | 0.1521 | 2.7177 | 13702 | 5184 | 641 | 28.028 | - |
| 164046 | test | True | 321x481 | 5 | 50/150 | 0.4444 | 2.6684 | 0.2627 | 1.3636 | 6405 | 4044 | 448 | 21.885 | - |
| 196088 | test | True | 481x321 | 5 | 50/150 | 0.339 | 2.2956 | 0.1085 | 1.0521 | 31749 | 9099 | 652 | 24.812 | - |
| 202000 | test | True | 481x321 | 5 | 50/150 | 0.2279 | 18.9631 | 0.1354 | 13.2046 | 13331 | 9636 | 1058 | 22.034 | - |
| 223060 | test | True | 481x321 | 6 | 50/150 | 0.3281 | 5.933 | 0.2059 | 1.1827 | 18349 | 6849 | 1183 | 26.25 | - |
| 232076 | test | True | 481x321 | 5 | 50/150 | 0.2793 | 7.5708 | 0.1399 | 1.4169 | 11961 | 6104 | 444 | 19.825 | - |
| 302022 | test | True | 321x481 | 5 | 50/150 | 0.2002 | 15.7794 | 0.1067 | 4.1506 | 14110 | 5677 | 599 | 21.603 | - |
| 306052 | test | True | 481x321 | 5 | 50/150 | 0.3124 | 7.7235 | 0.1216 | 1.9903 | 9546 | 5194 | 420 | 25.092 | - |
| 326085 | test | True | 321x481 | 5 | 50/150 | 0.2727 | 2.6255 | 0.1072 | 1.3516 | 17338 | 5599 | 487 | 21.905 | - |
| 33044 | test | True | 321x481 | 5 | 50/150 | 0.3711 | 2.0115 | 0.1588 | 0.7981 | 17809 | 6420 | 663 | 23.689 | - |
| 41096 | test | True | 481x321 | 6 | 50/150 | 0.2652 | 2.3091 | 0.1614 | 1.2943 | 13980 | 4249 | 877 | 24.679 | - |
| 48017 | test | True | 481x321 | 5 | 50/150 | 0.2776 | 5.9392 | 0.1236 | 1.1085 | 13997 | 5212 | 555 | 21.353 | - |
| 49024 | test | True | 481x321 | 5 | 50/150 | 0.2709 | 102.6891 | 0.3054 | 43.8204 | 2712 | 6629 | 435 | 17.939 | - |
| 97010 | test | True | 481x321 | 5 | 50/150 | 0.2822 | 6.2919 | 0.169 | 3.0578 | 19609 | 7079 | 1089 | 19.539 | - |
