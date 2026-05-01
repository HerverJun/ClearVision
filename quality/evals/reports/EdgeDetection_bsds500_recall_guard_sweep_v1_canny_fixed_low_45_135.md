# EdgeDetection BSDS500 Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-05-01T03:25:14.1513182+00:00`
Dataset: `BSDS500 human boundary annotations`
Index: `quality/datasets/bsds500_index.json`
Split: `test`
CandidateVersion: `recall_guard_v1`
Profile: `canny_fixed_low_45_135`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 20 |
| Failed | 0 |
| Human annotations | 102 |
| Total pixels | 3088020 |
| Predicted edge pixels | 338806 |
| Union boundary pixels | 123061 |
| Consensus boundary pixels | 11986 |
| Boundary precision | 0.1765 |
| Boundary recall | 0.663 |
| Boundary F1 | 0.2788 |
| Consensus boundary precision | 0.0666 |
| Consensus boundary recall | 0.8672 |
| Consensus boundary F1 | 0.1237 |
| Predicted to boundary mean distance px | 28.579 |
| Boundary to predicted mean distance px | 11.641 |
| Predicted to consensus mean distance px | 43.2516 |
| Consensus to predicted mean distance px | 4.1753 |
| Boundary tolerance px | 2 |
| Runtime ms avg | 24.399 |
| Runtime ms p95 | 27.089 |

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
| test | 20 | 20 | 0 | 0.2824 | 0.1363 | 24.399 |

## Cases

| Case | Split | Passed | Size | Annotations | Thresholds | Union F1 | Union B->P px | Consensus F1 | Consensus B->P px | Predicted | Union | Consensus | Runtime ms | Failure |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 101027 | test | True | 481x321 | 5 | 45/135 | 0.3302 | 4.0036 | 0.1495 | 3.6077 | 15559 | 7296 | 639 | 80.827 | - |
| 103006 | test | True | 481x321 | 5 | 45/135 | 0.3257 | 3.1173 | 0.0906 | 1.2638 | 21730 | 7213 | 482 | 22.826 | - |
| 108069 | test | True | 481x321 | 5 | 45/135 | 0.1666 | 2.4553 | 0.067 | 1.9201 | 19459 | 3181 | 330 | 21.371 | - |
| 109055 | test | True | 481x321 | 5 | 45/135 | 0.2426 | 18.9381 | 0.1107 | 1.8626 | 16111 | 6924 | 401 | 21.356 | - |
| 141012 | test | True | 481x321 | 5 | 45/135 | 0.1808 | 3.223 | 0.0498 | 1.015 | 32653 | 4668 | 287 | 22.958 | - |
| 159022 | test | True | 481x321 | 5 | 45/135 | 0.3318 | 10.1125 | 0.1508 | 3.2954 | 7071 | 6804 | 296 | 20.526 | - |
| 160067 | test | True | 481x321 | 5 | 45/135 | 0.3001 | 2.5663 | 0.1524 | 2.2295 | 14363 | 5184 | 641 | 21.297 | - |
| 164046 | test | True | 321x481 | 5 | 45/135 | 0.4034 | 1.3369 | 0.2157 | 0.6487 | 8939 | 4044 | 448 | 21.393 | - |
| 196088 | test | True | 481x321 | 5 | 45/135 | 0.3387 | 1.9579 | 0.1033 | 1.0163 | 34139 | 9099 | 652 | 27.089 | - |
| 202000 | test | True | 481x321 | 5 | 45/135 | 0.2414 | 18.4769 | 0.1417 | 12.7532 | 14195 | 9636 | 1058 | 21.173 | - |
| 223060 | test | True | 481x321 | 6 | 45/135 | 0.329 | 3.577 | 0.2008 | 1.0448 | 19149 | 6849 | 1183 | 25.636 | - |
| 232076 | test | True | 481x321 | 5 | 45/135 | 0.2592 | 7.2494 | 0.1217 | 1.4124 | 13907 | 6104 | 444 | 19.627 | - |
| 302022 | test | True | 321x481 | 5 | 45/135 | 0.2098 | 15.0615 | 0.1064 | 3.4377 | 15248 | 5677 | 599 | 19.412 | - |
| 306052 | test | True | 481x321 | 5 | 45/135 | 0.2876 | 7.1261 | 0.1069 | 1.5499 | 12116 | 5194 | 420 | 18.098 | - |
| 326085 | test | True | 321x481 | 5 | 45/135 | 0.2588 | 2.4219 | 0.0978 | 1.2488 | 19912 | 5599 | 487 | 21.541 | - |
| 33044 | test | True | 321x481 | 5 | 45/135 | 0.3587 | 1.8057 | 0.1459 | 0.7787 | 20070 | 6420 | 663 | 20.798 | - |
| 41096 | test | True | 481x321 | 6 | 45/135 | 0.2562 | 2.1199 | 0.1515 | 1.1843 | 15447 | 4249 | 877 | 24.107 | - |
| 48017 | test | True | 481x321 | 5 | 45/135 | 0.2797 | 4.2651 | 0.1157 | 1.0846 | 15150 | 5212 | 555 | 18.321 | - |
| 49024 | test | True | 481x321 | 5 | 45/135 | 0.2644 | 99.4064 | 0.2799 | 42.5731 | 3335 | 6629 | 435 | 18.981 | - |
| 97010 | test | True | 481x321 | 5 | 45/135 | 0.2839 | 5.2099 | 0.168 | 2.5253 | 20253 | 7079 | 1089 | 20.645 | - |
