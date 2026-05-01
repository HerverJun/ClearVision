# EdgeDetection BSDS500 Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-05-01T03:59:23.4668102+00:00`
Dataset: `BSDS500 human boundary annotations`
Index: `quality/datasets/bsds500_index.json`
Split: `test`
CandidateVersion: `recall_not_lower_v2`
Profile: `canny_fixed_low_35_105`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 20 |
| Failed | 0 |
| Human annotations | 102 |
| Total pixels | 3088020 |
| Predicted edge pixels | 409424 |
| Union boundary pixels | 123061 |
| Consensus boundary pixels | 11986 |
| Boundary precision | 0.1659 |
| Boundary recall | 0.721 |
| Boundary F1 | 0.2698 |
| Consensus boundary precision | 0.0589 |
| Consensus boundary recall | 0.9055 |
| Consensus boundary F1 | 0.1106 |
| Predicted to boundary mean distance px | 28.4638 |
| Boundary to predicted mean distance px | 7.3376 |
| Predicted to consensus mean distance px | 44.117 |
| Consensus to predicted mean distance px | 2.4782 |
| Boundary tolerance px | 2 |
| Runtime ms avg | 24.611 |
| Runtime ms p95 | 26.496 |

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
| test | 20 | 20 | 0 | 0.2711 | 0.1192 | 24.611 |

## Cases

| Case | Split | Passed | Size | Annotations | Thresholds | Union F1 | Union B->P px | Consensus F1 | Consensus B->P px | Predicted | Union | Consensus | Runtime ms | Failure |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 101027 | test | True | 481x321 | 5 | 35/105 | 0.3082 | 3.1155 | 0.1355 | 2.195 | 19263 | 7296 | 639 | 76.466 | - |
| 103006 | test | True | 481x321 | 5 | 35/105 | 0.3048 | 2.5971 | 0.0797 | 1.2109 | 26021 | 7213 | 482 | 21.516 | - |
| 108069 | test | True | 481x321 | 5 | 35/105 | 0.163 | 1.4709 | 0.0614 | 1.4555 | 23709 | 3181 | 330 | 22.295 | - |
| 109055 | test | True | 481x321 | 5 | 35/105 | 0.242 | 16.6476 | 0.102 | 1.7577 | 18363 | 6924 | 401 | 23.345 | - |
| 141012 | test | True | 481x321 | 5 | 35/105 | 0.1906 | 1.6135 | 0.0483 | 0.8284 | 37699 | 4668 | 287 | 23.404 | - |
| 159022 | test | True | 481x321 | 5 | 35/105 | 0.3394 | 7.6114 | 0.1353 | 2.5565 | 8779 | 6804 | 296 | 23.256 | - |
| 160067 | test | True | 481x321 | 5 | 35/105 | 0.3132 | 1.6807 | 0.1572 | 1.3695 | 15790 | 5184 | 641 | 21.888 | - |
| 164046 | test | True | 321x481 | 5 | 35/105 | 0.2819 | 1.1516 | 0.1325 | 0.6098 | 15592 | 4044 | 448 | 22.322 | - |
| 196088 | test | True | 481x321 | 5 | 35/105 | 0.3342 | 1.4181 | 0.0972 | 0.8843 | 38339 | 9099 | 652 | 23.521 | - |
| 202000 | test | True | 481x321 | 5 | 35/105 | 0.2747 | 12.1105 | 0.1448 | 10.1761 | 15476 | 9636 | 1058 | 21.064 | - |
| 223060 | test | True | 481x321 | 6 | 35/105 | 0.3138 | 2.0749 | 0.1792 | 0.8507 | 22615 | 6849 | 1183 | 26.496 | - |
| 232076 | test | True | 481x321 | 5 | 35/105 | 0.2331 | 6.8001 | 0.0942 | 1.4057 | 18381 | 6104 | 444 | 18.9 | - |
| 302022 | test | True | 321x481 | 5 | 35/105 | 0.2176 | 14.1648 | 0.1096 | 1.8255 | 16898 | 5677 | 599 | 20.329 | - |
| 306052 | test | True | 481x321 | 5 | 35/105 | 0.2475 | 4.2241 | 0.0809 | 1.1588 | 17898 | 5194 | 420 | 22.328 | - |
| 326085 | test | True | 321x481 | 5 | 35/105 | 0.2443 | 1.6852 | 0.0872 | 0.9942 | 24878 | 5599 | 487 | 21.946 | - |
| 33044 | test | True | 321x481 | 5 | 35/105 | 0.33 | 1.1411 | 0.1223 | 0.6974 | 25820 | 6420 | 663 | 21.593 | - |
| 41096 | test | True | 481x321 | 6 | 35/105 | 0.2383 | 1.9538 | 0.1342 | 1.1816 | 17852 | 4249 | 877 | 24.031 | - |
| 48017 | test | True | 481x321 | 5 | 35/105 | 0.2863 | 2.5085 | 0.0949 | 1.0421 | 19189 | 5212 | 555 | 17.713 | - |
| 49024 | test | True | 481x321 | 5 | 35/105 | 0.2727 | 46.4994 | 0.2222 | 10.7993 | 5044 | 6629 | 435 | 18.994 | - |
| 97010 | test | True | 481x321 | 5 | 35/105 | 0.2862 | 4.9184 | 0.1663 | 2.3644 | 21818 | 7079 | 1089 | 20.81 | - |
