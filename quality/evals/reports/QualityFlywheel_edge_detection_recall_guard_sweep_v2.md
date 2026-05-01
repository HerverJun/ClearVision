# EdgeDetection Recall-Guard Sweep v2

GeneratedAtUtc: `2026-05-01T03:59:27+00:00`
Decision: `promotion-paused-recall-not-lower-required`
SelectedProfile: `None`
PromotionPaused: `True`
NextObjective: `Find a profile with boundary recall and consensus boundary recall not lower than canny_l2_50_150; F1 alone is not sufficient.`
ClaimBoundary: `BSDS500 replay-subset threshold probe only; EdgeDetection promotion is paused and product defaults are unchanged.`

| Profile | Thresholds | Auto | Strategy | L2 | Precision | Recall | F1 | Consensus recall | B->P px | Predicted | P95 ms |
|---|---:|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| canny_default_50_150 | 50/150 | False | MedianIntensity | False | 0.1691 | 0.6858 | 0.2714 | 0.8861 | 11.1145 | 372819 | 28.27 |
| canny_l2_50_150 | 50/150 | False | MedianIntensity | True | 0.1811 | 0.6292 | 0.2812 | 0.8395 | 12.85 | 306598 | 28.028 |
| canny_fixed_low_45_135 | 45/135 | False | MedianIntensity | True | 0.1765 | 0.663 | 0.2788 | 0.8672 | 11.641 | 338806 | 26.267 |
| canny_fixed_low_40_120 | 40/120 | False | MedianIntensity | True | 0.1703 | 0.6911 | 0.2733 | 0.8871 | 10.8675 | 372728 | 26.63 |
| canny_fixed_low_35_105 | 35/105 | False | MedianIntensity | True | 0.1659 | 0.721 | 0.2698 | 0.9055 | 7.3376 | 409424 | 26.496 |
| canny_recall_guard_percentile | 50/150 | True | RecallGuardPercentile | True | 0.1643 | 0.754 | 0.2698 | 0.9231 | 5.1874 | 426392 | 51.22 |
| canny_otsu_gradient | 50/150 | True | OtsuGradient | True | 0.1568 | 0.8307 | 0.2638 | 0.9689 | 3.1043 | 526377 | 27.124 |
