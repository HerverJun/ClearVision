# EdgeDetection BSDS500 Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-05-01T03:25:22.8009339+00:00`
Dataset: `BSDS500 human boundary annotations`
Index: `quality/datasets/bsds500_index.json`
Split: `test`
CandidateVersion: `recall_guard_v1`
Profile: `canny_otsu_gradient`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 20 |
| Failed | 0 |
| Human annotations | 102 |
| Total pixels | 3088020 |
| Predicted edge pixels | 526377 |
| Union boundary pixels | 123061 |
| Consensus boundary pixels | 11986 |
| Boundary precision | 0.1568 |
| Boundary recall | 0.8307 |
| Boundary F1 | 0.2638 |
| Consensus boundary precision | 0.0508 |
| Consensus boundary recall | 0.9689 |
| Consensus boundary F1 | 0.0965 |
| Predicted to boundary mean distance px | 28.4532 |
| Boundary to predicted mean distance px | 3.1043 |
| Predicted to consensus mean distance px | 44.6114 |
| Consensus to predicted mean distance px | 1.027 |
| Boundary tolerance px | 2 |
| Runtime ms avg | 26.304 |
| Runtime ms p95 | 27.533 |

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
| test | 20 | 20 | 0 | 0.2649 | 0.0999 | 26.304 |

## Cases

| Case | Split | Passed | Size | Annotations | Thresholds | Union F1 | Union B->P px | Consensus F1 | Consensus B->P px | Predicted | Union | Consensus | Runtime ms | Failure |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 101027 | test | True | 481x321 | 5 | 26.5/60.95 | 0.2774 | 2.3752 | 0.1135 | 1.4296 | 26282 | 7296 | 639 | 79.233 | - |
| 103006 | test | True | 481x321 | 5 | 28.5/65.55 | 0.2989 | 2.0941 | 0.0707 | 1.1848 | 30488 | 7213 | 482 | 25.248 | - |
| 108069 | test | True | 481x321 | 5 | 32/73.6 | 0.1545 | 1.1374 | 0.0563 | 0.965 | 27942 | 3181 | 330 | 23.852 | - |
| 109055 | test | True | 481x321 | 5 | 26.5/60.95 | 0.2561 | 4.1882 | 0.0875 | 0.8465 | 23218 | 6924 | 401 | 23.133 | - |
| 141012 | test | True | 481x321 | 5 | 24/55.2 | 0.1975 | 1.0167 | 0.0433 | 0.7746 | 44312 | 4668 | 287 | 24.675 | - |
| 159022 | test | True | 481x321 | 5 | 18.5/42.55 | 0.3771 | 2.5435 | 0.1108 | 0.9313 | 14703 | 6804 | 296 | 22.963 | - |
| 160067 | test | True | 481x321 | 5 | 26.5/60.95 | 0.3112 | 1.4201 | 0.1549 | 1.1716 | 17569 | 5184 | 641 | 22.222 | - |
| 164046 | test | True | 321x481 | 5 | 27.5/63.25 | 0.227 | 0.9409 | 0.0967 | 0.56 | 22763 | 4044 | 448 | 21.849 | - |
| 196088 | test | True | 481x321 | 5 | 31.5/72.45 | 0.3363 | 1.0724 | 0.092 | 0.8524 | 41388 | 9099 | 652 | 25.669 | - |
| 202000 | test | True | 481x321 | 5 | 18/41.4 | 0.3851 | 2.7197 | 0.1698 | 1.1492 | 20646 | 9636 | 1058 | 21.372 | - |
| 223060 | test | True | 481x321 | 6 | 27.5/63.25 | 0.2998 | 1.1954 | 0.157 | 0.7582 | 27955 | 6849 | 1183 | 27.533 | - |
| 232076 | test | True | 481x321 | 5 | 27.5/63.25 | 0.2355 | 4.8062 | 0.0795 | 1.215 | 22246 | 6104 | 444 | 23.803 | - |
| 302022 | test | True | 321x481 | 5 | 23.5/54.05 | 0.2634 | 3.4682 | 0.0995 | 1.1036 | 21213 | 5677 | 599 | 26.451 | - |
| 306052 | test | True | 481x321 | 5 | 22.5/51.75 | 0.2158 | 1.5266 | 0.051 | 0.8498 | 30594 | 5194 | 420 | 22.594 | - |
| 326085 | test | True | 321x481 | 5 | 21.5/49.45 | 0.2239 | 1.2004 | 0.0725 | 0.8411 | 31677 | 5599 | 487 | 23.108 | - |
| 33044 | test | True | 321x481 | 5 | 21/48.3 | 0.2871 | 0.9543 | 0.0995 | 0.6367 | 35788 | 6420 | 663 | 22.647 | - |
| 41096 | test | True | 481x321 | 6 | 20.5/47.15 | 0.2102 | 1.2132 | 0.1139 | 0.872 | 22979 | 4249 | 877 | 23.97 | - |
| 48017 | test | True | 481x321 | 5 | 39/89.7 | 0.2871 | 2.3324 | 0.0915 | 1.0466 | 19877 | 5212 | 555 | 22.279 | - |
| 49024 | test | True | 481x321 | 5 | 18.5/42.55 | 0.1799 | 20.5738 | 0.0895 | 2.6672 | 15077 | 6629 | 435 | 21.543 | - |
| 97010 | test | True | 481x321 | 5 | 20.5/47.15 | 0.2733 | 2.2742 | 0.1488 | 1.0132 | 29660 | 7079 | 1089 | 21.929 | - |
