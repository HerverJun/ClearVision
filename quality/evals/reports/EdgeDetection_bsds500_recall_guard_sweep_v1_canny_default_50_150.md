# EdgeDetection BSDS500 Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-05-01T03:25:10.0109183+00:00`
Dataset: `BSDS500 human boundary annotations`
Index: `quality/datasets/bsds500_index.json`
Split: `test`
CandidateVersion: `baseline_proxy`
Profile: `canny_default_50_150`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 20 |
| Failed | 0 |
| Human annotations | 102 |
| Total pixels | 3088020 |
| Predicted edge pixels | 372819 |
| Union boundary pixels | 123061 |
| Consensus boundary pixels | 11986 |
| Boundary precision | 0.1691 |
| Boundary recall | 0.6858 |
| Boundary F1 | 0.2714 |
| Consensus boundary precision | 0.0622 |
| Consensus boundary recall | 0.8861 |
| Consensus boundary F1 | 0.1163 |
| Predicted to boundary mean distance px | 28.6484 |
| Boundary to predicted mean distance px | 11.1145 |
| Predicted to consensus mean distance px | 43.7651 |
| Consensus to predicted mean distance px | 3.839 |
| Boundary tolerance px | 2 |
| Runtime ms avg | 23.399 |
| Runtime ms p95 | 25.828 |

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
| test | 20 | 20 | 0 | 0.2717 | 0.1259 | 23.399 |

## Cases

| Case | Split | Passed | Size | Annotations | Thresholds | Union F1 | Union B->P px | Consensus F1 | Consensus B->P px | Predicted | Union | Consensus | Runtime ms | Failure |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 101027 | test | True | 481x321 | 5 | 50/150 | 0.3209 | 3.5821 | 0.1426 | 2.805 | 17173 | 7296 | 639 | 79.477 | - |
| 103006 | test | True | 481x321 | 5 | 50/150 | 0.3103 | 2.941 | 0.0828 | 1.2488 | 24250 | 7213 | 482 | 24.064 | - |
| 108069 | test | True | 481x321 | 5 | 50/150 | 0.1697 | 1.4943 | 0.0648 | 1.4111 | 21842 | 3181 | 330 | 21.308 | - |
| 109055 | test | True | 481x321 | 5 | 50/150 | 0.2435 | 21.3482 | 0.1071 | 2.1144 | 16995 | 6924 | 401 | 21.494 | - |
| 141012 | test | True | 481x321 | 5 | 50/150 | 0.1886 | 1.8184 | 0.0489 | 0.8113 | 35825 | 4668 | 287 | 22.161 | - |
| 159022 | test | True | 481x321 | 5 | 50/150 | 0.3225 | 9.0663 | 0.1407 | 2.9737 | 7946 | 6804 | 296 | 20.82 | - |
| 160067 | test | True | 481x321 | 5 | 50/150 | 0.303 | 1.9252 | 0.1509 | 1.5316 | 15194 | 5184 | 641 | 20.846 | - |
| 164046 | test | True | 321x481 | 5 | 50/150 | 0.3139 | 1.904 | 0.1574 | 0.9729 | 12129 | 4044 | 448 | 21.361 | - |
| 196088 | test | True | 481x321 | 5 | 50/150 | 0.3371 | 1.6539 | 0.0992 | 1.0007 | 35938 | 9099 | 652 | 23.287 | - |
| 202000 | test | True | 481x321 | 5 | 50/150 | 0.244 | 16.8434 | 0.1399 | 11.6562 | 14574 | 9636 | 1058 | 21.616 | - |
| 223060 | test | True | 481x321 | 6 | 50/150 | 0.3238 | 2.9404 | 0.1923 | 0.9042 | 20672 | 6849 | 1183 | 25.828 | - |
| 232076 | test | True | 481x321 | 5 | 50/150 | 0.2401 | 7.0813 | 0.1049 | 1.4273 | 16291 | 6104 | 444 | 18.911 | - |
| 302022 | test | True | 321x481 | 5 | 50/150 | 0.2192 | 14.2202 | 0.1131 | 1.8281 | 16333 | 5677 | 599 | 17.39 | - |
| 306052 | test | True | 481x321 | 5 | 50/150 | 0.2719 | 5.6847 | 0.0977 | 1.1423 | 14597 | 5194 | 420 | 19.534 | - |
| 326085 | test | True | 321x481 | 5 | 50/150 | 0.2518 | 1.9996 | 0.092 | 1.0333 | 22728 | 5599 | 487 | 19.17 | - |
| 33044 | test | True | 321x481 | 5 | 50/150 | 0.3296 | 1.6208 | 0.1315 | 0.7963 | 22874 | 6420 | 663 | 17.339 | - |
| 41096 | test | True | 481x321 | 6 | 50/150 | 0.242 | 2.2311 | 0.1386 | 1.3437 | 16537 | 4249 | 877 | 20.642 | - |
| 48017 | test | True | 481x321 | 5 | 50/150 | 0.2842 | 4.2845 | 0.1123 | 1.0701 | 15945 | 5212 | 555 | 17.143 | - |
| 49024 | test | True | 481x321 | 5 | 50/150 | 0.2305 | 96.6317 | 0.2325 | 41.5518 | 4123 | 6629 | 435 | 15.836 | - |
| 97010 | test | True | 481x321 | 5 | 50/150 | 0.2871 | 4.9485 | 0.168 | 2.419 | 20853 | 7079 | 1089 | 19.762 | - |
