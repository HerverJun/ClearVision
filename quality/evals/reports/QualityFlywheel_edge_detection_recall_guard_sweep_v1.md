# EdgeDetection Recall-Guard Sweep v1

GeneratedAtUtc: `2026-04-30T12:42:44+00:00`
Decision: `hold-current-no-recall-safe-profile`
SelectedProfile: `None`
ClaimBoundary: `BSDS500 replay-subset threshold probe only; no product default promotion.`

| Profile | Thresholds | L2 | Precision | Recall | F1 | Consensus recall | B->P px | Predicted | P95 ms |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|
| fixed_50_150_no_l2 | 50/150 | False | 0.1691 | 0.6858 | 0.2714 | 0.8861 | 11.1145 | 372819 | 24.613 |
| fixed_50_150_l2 | 50/150 | True | 0.1811 | 0.6292 | 0.2812 | 0.8395 | 12.85 | 306598 | 26.692 |
| recall_guard_45_135_l2 | 45/135 | True | 0.1765 | 0.663 | 0.2788 | 0.8672 | 11.641 | 338806 | 31.838 |
| recall_guard_40_120_l2 | 40/120 | True | 0.1703 | 0.6911 | 0.2733 | 0.8871 | 10.8675 | 372728 | 32.707 |
| recall_guard_35_105_l2 | 35/105 | True | 0.1659 | 0.721 | 0.2698 | 0.9055 | 7.3376 | 409424 | 26.913 |
