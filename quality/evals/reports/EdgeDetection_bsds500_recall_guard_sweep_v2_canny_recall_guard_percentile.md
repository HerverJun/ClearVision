# EdgeDetection BSDS500 Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-05-01T03:59:25.8147180+00:00`
Dataset: `BSDS500 human boundary annotations`
Index: `quality/datasets/bsds500_index.json`
Split: `test`
CandidateVersion: `recall_not_lower_v2`
Profile: `canny_recall_guard_percentile`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 20 |
| Failed | 0 |
| Human annotations | 102 |
| Total pixels | 3088020 |
| Predicted edge pixels | 426392 |
| Union boundary pixels | 123061 |
| Consensus boundary pixels | 11986 |
| Boundary precision | 0.1643 |
| Boundary recall | 0.754 |
| Boundary F1 | 0.2698 |
| Consensus boundary precision | 0.0577 |
| Consensus boundary recall | 0.9231 |
| Consensus boundary F1 | 0.1086 |
| Predicted to boundary mean distance px | 28.7618 |
| Boundary to predicted mean distance px | 5.1874 |
| Predicted to consensus mean distance px | 44.264 |
| Consensus to predicted mean distance px | 1.5774 |
| Boundary tolerance px | 2 |
| Runtime ms avg | 41.92 |
| Runtime ms p95 | 51.22 |

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
| test | 20 | 20 | 0 | 0.269 | 0.1096 | 41.92 |

## Cases

| Case | Split | Passed | Size | Annotations | Thresholds | Union F1 | Union B->P px | Consensus F1 | Consensus B->P px | Predicted | Union | Consensus | Runtime ms | Failure |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 101027 | test | True | 481x321 | 5 | 29.155/99.126 | 0.2992 | 2.8788 | 0.1307 | 1.9154 | 20971 | 7296 | 639 | 109.981 | - |
| 103006 | test | True | 481x321 | 5 | 46.84/112.606 | 0.3162 | 2.9451 | 0.0854 | 1.2506 | 23532 | 7213 | 482 | 49.683 | - |
| 108069 | test | True | 481x321 | 5 | 47.011/122.613 | 0.1714 | 2.0869 | 0.0658 | 1.8436 | 20464 | 3181 | 330 | 51.22 | - |
| 109055 | test | True | 481x321 | 5 | 32.65/99.85 | 0.2401 | 16.2572 | 0.1 | 1.7363 | 18774 | 6924 | 401 | 50.456 | - |
| 141012 | test | True | 481x321 | 5 | 63.702/117.924 | 0.1855 | 2.6235 | 0.0505 | 1.0328 | 29318 | 4668 | 287 | 49.935 | - |
| 159022 | test | True | 481x321 | 5 | 7.211/40.249 | 0.4051 | 1.6009 | 0.1078 | 0.8379 | 16026 | 6804 | 296 | 36.574 | - |
| 160067 | test | True | 481x321 | 5 | 15.556/108.747 | 0.3198 | 1.4417 | 0.1601 | 1.3085 | 16173 | 5184 | 641 | 35.867 | - |
| 164046 | test | True | 321x481 | 5 | 25.298/63.388 | 0.2244 | 0.9376 | 0.0953 | 0.56 | 23172 | 4044 | 448 | 35.145 | - |
| 196088 | test | True | 481x321 | 5 | 69.857/137.179 | 0.355 | 2.2998 | 0.1181 | 1.1165 | 28507 | 9099 | 652 | 37.927 | - |
| 202000 | test | True | 481x321 | 5 | 14.142/76.655 | 0.3131 | 8.3209 | 0.1563 | 4.2215 | 17648 | 9636 | 1058 | 34.377 | - |
| 223060 | test | True | 481x321 | 6 | 31.401/109.772 | 0.3106 | 2.1975 | 0.1767 | 0.894 | 22705 | 6849 | 1183 | 37.713 | - |
| 232076 | test | True | 481x321 | 5 | 25.495/80.411 | 0.2368 | 4.8803 | 0.0818 | 1.24 | 21554 | 6104 | 444 | 35.131 | - |
| 302022 | test | True | 321x481 | 5 | 29.967/89.107 | 0.2203 | 14.0863 | 0.1081 | 1.8043 | 17843 | 5677 | 599 | 33.589 | - |
| 306052 | test | True | 481x321 | 5 | 31.016/71.694 | 0.2154 | 3.4624 | 0.0619 | 1.1225 | 23948 | 5194 | 420 | 33.582 | - |
| 326085 | test | True | 321x481 | 5 | 43.267/95.258 | 0.2496 | 1.6205 | 0.0882 | 0.9673 | 24240 | 5599 | 487 | 34.872 | - |
| 33044 | test | True | 321x481 | 5 | 42.521/93.606 | 0.3338 | 1.167 | 0.1253 | 0.7022 | 25185 | 6420 | 663 | 36.168 | - |
| 41096 | test | True | 481x321 | 6 | 21.26/88.814 | 0.2219 | 1.5882 | 0.1216 | 1.0739 | 20352 | 4249 | 877 | 34.343 | - |
| 48017 | test | True | 481x321 | 5 | 34.059/127.279 | 0.2939 | 3.42 | 0.1118 | 1.064 | 15851 | 5212 | 555 | 35.19 | - |
| 49024 | test | True | 481x321 | 5 | 14.422/37.577 | 0.1797 | 18.8944 | 0.082 | 2.1891 | 17652 | 6629 | 435 | 31.473 | - |
| 97010 | test | True | 481x321 | 5 | 27.785/108.167 | 0.288 | 4.5518 | 0.1654 | 2.1727 | 22477 | 7079 | 1089 | 35.175 | - |
