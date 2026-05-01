# EdgeDetection BSDS500 Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-05-01T03:59:21.4215369+00:00`
Dataset: `BSDS500 human boundary annotations`
Index: `quality/datasets/bsds500_index.json`
Split: `test`
CandidateVersion: `recall_not_lower_v2`
Profile: `canny_fixed_low_40_120`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 20 |
| Failed | 0 |
| Human annotations | 102 |
| Total pixels | 3088020 |
| Predicted edge pixels | 372728 |
| Union boundary pixels | 123061 |
| Consensus boundary pixels | 11986 |
| Boundary precision | 0.1703 |
| Boundary recall | 0.6911 |
| Boundary F1 | 0.2733 |
| Consensus boundary precision | 0.0627 |
| Consensus boundary recall | 0.8871 |
| Consensus boundary F1 | 0.1171 |
| Predicted to boundary mean distance px | 28.5345 |
| Boundary to predicted mean distance px | 10.8675 |
| Predicted to consensus mean distance px | 43.7745 |
| Consensus to predicted mean distance px | 3.7465 |
| Boundary tolerance px | 2 |
| Runtime ms avg | 23.805 |
| Runtime ms p95 | 26.63 |

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
| test | 20 | 20 | 0 | 0.2743 | 0.1273 | 23.805 |

## Cases

| Case | Split | Passed | Size | Annotations | Thresholds | Union F1 | Union B->P px | Consensus F1 | Consensus B->P px | Predicted | Union | Consensus | Runtime ms | Failure |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 101027 | test | True | 481x321 | 5 | 40/120 | 0.32 | 3.5727 | 0.143 | 2.8206 | 16889 | 7296 | 639 | 74.834 | - |
| 103006 | test | True | 481x321 | 5 | 40/120 | 0.3156 | 2.8758 | 0.0851 | 1.2202 | 23786 | 7213 | 482 | 22.367 | - |
| 108069 | test | True | 481x321 | 5 | 40/120 | 0.1713 | 1.5191 | 0.0655 | 1.4613 | 21736 | 3181 | 330 | 21.049 | - |
| 109055 | test | True | 481x321 | 5 | 40/120 | 0.2432 | 18.3795 | 0.1085 | 1.7886 | 17039 | 6924 | 401 | 20.918 | - |
| 141012 | test | True | 481x321 | 5 | 40/120 | 0.1878 | 2.0953 | 0.0501 | 0.8756 | 35330 | 4668 | 287 | 22.899 | - |
| 159022 | test | True | 481x321 | 5 | 40/120 | 0.3372 | 9.0547 | 0.1429 | 2.8981 | 7995 | 6804 | 296 | 22.124 | - |
| 160067 | test | True | 481x321 | 5 | 40/120 | 0.3139 | 1.7311 | 0.1574 | 1.4353 | 15257 | 5184 | 641 | 21.404 | - |
| 164046 | test | True | 321x481 | 5 | 40/120 | 0.3312 | 1.2733 | 0.1692 | 0.6162 | 11736 | 4044 | 448 | 21.384 | - |
| 196088 | test | True | 481x321 | 5 | 40/120 | 0.3375 | 1.6629 | 0.0988 | 0.9467 | 36361 | 9099 | 652 | 22.828 | - |
| 202000 | test | True | 481x321 | 5 | 40/120 | 0.2574 | 16.4554 | 0.1504 | 10.1803 | 14753 | 9636 | 1058 | 22.062 | - |
| 223060 | test | True | 481x321 | 6 | 40/120 | 0.3208 | 3.0194 | 0.1887 | 1.0336 | 20709 | 6849 | 1183 | 26.63 | - |
| 232076 | test | True | 481x321 | 5 | 40/120 | 0.2471 | 6.9261 | 0.1062 | 1.4124 | 16185 | 6104 | 444 | 18.593 | - |
| 302022 | test | True | 321x481 | 5 | 40/120 | 0.2099 | 14.8725 | 0.1051 | 3.3021 | 15877 | 5677 | 599 | 18.061 | - |
| 306052 | test | True | 481x321 | 5 | 40/120 | 0.2611 | 5.5829 | 0.0953 | 1.1588 | 15031 | 5194 | 420 | 19.176 | - |
| 326085 | test | True | 321x481 | 5 | 40/120 | 0.2487 | 2.14 | 0.0917 | 1.0824 | 22434 | 5599 | 487 | 21.36 | - |
| 33044 | test | True | 321x481 | 5 | 40/120 | 0.3409 | 1.315 | 0.1318 | 0.7069 | 23000 | 6420 | 663 | 22.368 | - |
| 41096 | test | True | 481x321 | 6 | 40/120 | 0.2488 | 1.9695 | 0.1421 | 1.1827 | 16700 | 4249 | 877 | 24.818 | - |
| 48017 | test | True | 481x321 | 5 | 40/120 | 0.2672 | 3.4721 | 0.1042 | 1.0716 | 17025 | 5212 | 555 | 19.171 | - |
| 49024 | test | True | 481x321 | 5 | 40/120 | 0.2437 | 96.4929 | 0.2436 | 41.4172 | 3993 | 6629 | 435 | 17.166 | - |
| 97010 | test | True | 481x321 | 5 | 40/120 | 0.2831 | 5.1314 | 0.1664 | 2.4457 | 20892 | 7079 | 1089 | 16.882 | - |
