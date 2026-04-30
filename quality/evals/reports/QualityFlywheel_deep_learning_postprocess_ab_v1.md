# DeepLearning Postprocess A/B Report

GeneratedAtUtc: `2026-04-30T01:51:40.2173745+00:00`
Accepted: `True`

## Claim Boundary

- This report evaluates DeepLearning postprocess behavior only: NMS, letterbox coordinate inversion, and clamp policy.
- It does not claim model-training, model-weight, AP, precision, or recall improvement.
- Offline variants are candidate evidence and are not production behavior unless promoted in a later algorithm change.

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 6 |
| Comparisons | 28 |
| Accepted cases | 6 |
| Failed cases | 0 |
| NMS comparisons | 24 |
| Letterbox comparisons | 2 |
| Clamp comparisons | 2 |

## Cases

| Case | Topic | Accepted | Expected baseline detections | Actual baseline detections | Coordinate error px | Runtime ms |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| nms_same_class_overlap | nms | True | 2 | 2 | 0 | 19.302 |
| nms_cross_class_overlap | nms | True | 3 | 3 | 0 | 0.058 |
| letterbox_wide_image | letterbox | True | 1 | 1 | 0 | 0.166 |
| letterbox_tall_image | letterbox | True | 1 | 1 | 0 | 0.022 |
| clamp_top_left_overflow | clamp | True | 1 | 1 | 0 | 0.124 |
| clamp_bottom_right_overflow | clamp | True | 1 | 1 | 0 | 0.022 |

## A/B Comparisons

| Case | Topic | Baseline | Candidate | Baseline value | Candidate value | Delta | Baseline coord error | Candidate coord error | Accepted | Note |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- | --- |
| nms_same_class_overlap | nms | hard_nms_045_baseline | no_nms_candidate | 2 | 3 | 1 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| nms_same_class_overlap | nms | hard_nms_045_baseline | hard_nms_075_candidate | 2 | 3 | 1 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| nms_same_class_overlap | nms | hard_nms_045_baseline | soft_nms_linear_offline | 2 | 2 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| nms_same_class_overlap | nms | hard_nms_045_baseline | class_agnostic_hard_nms_offline | 2 | 2 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| nms_cross_class_overlap | nms | hard_nms_045_baseline | no_nms_candidate | 3 | 3 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| nms_cross_class_overlap | nms | hard_nms_045_baseline | hard_nms_075_candidate | 3 | 3 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| nms_cross_class_overlap | nms | hard_nms_045_baseline | soft_nms_linear_offline | 3 | 3 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| nms_cross_class_overlap | nms | hard_nms_045_baseline | class_agnostic_hard_nms_offline | 3 | 2 | -1 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| letterbox_wide_image | nms | hard_nms_045_baseline | no_nms_candidate | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| letterbox_wide_image | nms | hard_nms_045_baseline | hard_nms_075_candidate | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| letterbox_wide_image | nms | hard_nms_045_baseline | soft_nms_linear_offline | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| letterbox_wide_image | nms | hard_nms_045_baseline | class_agnostic_hard_nms_offline | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| letterbox_wide_image | letterbox | letterbox_inverse_baseline | naive_no_letterbox_inverse_offline | 1 | 1 | 122.5 | 0 | 122.5 | True | Positive delta means the candidate has larger coordinate error than the product letterbox inverse. |
| letterbox_tall_image | nms | hard_nms_045_baseline | no_nms_candidate | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| letterbox_tall_image | nms | hard_nms_045_baseline | hard_nms_075_candidate | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| letterbox_tall_image | nms | hard_nms_045_baseline | soft_nms_linear_offline | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| letterbox_tall_image | nms | hard_nms_045_baseline | class_agnostic_hard_nms_offline | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| letterbox_tall_image | letterbox | letterbox_inverse_baseline | naive_no_letterbox_inverse_offline | 1 | 1 | 118.125 | 0 | 118.125 | True | Positive delta means the candidate has larger coordinate error than the product letterbox inverse. |
| clamp_top_left_overflow | nms | hard_nms_045_baseline | no_nms_candidate | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| clamp_top_left_overflow | nms | hard_nms_045_baseline | hard_nms_075_candidate | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| clamp_top_left_overflow | nms | hard_nms_045_baseline | soft_nms_linear_offline | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| clamp_top_left_overflow | nms | hard_nms_045_baseline | class_agnostic_hard_nms_offline | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| clamp_top_left_overflow | clamp | product_clamp_baseline | no_clamp_offline | 0 | 1 | 1 | 0 | 1 | True | Positive delta means clamp removed invalid coordinates. |
| clamp_bottom_right_overflow | nms | hard_nms_045_baseline | no_nms_candidate | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| clamp_bottom_right_overflow | nms | hard_nms_045_baseline | hard_nms_075_candidate | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| clamp_bottom_right_overflow | nms | hard_nms_045_baseline | soft_nms_linear_offline | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| clamp_bottom_right_overflow | nms | hard_nms_045_baseline | class_agnostic_hard_nms_offline | 1 | 1 | 0 | 0 | 0 | True | Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner. |
| clamp_bottom_right_overflow | clamp | product_clamp_baseline | no_clamp_offline | 0 | 1 | 1 | 0 | 1 | True | Positive delta means clamp removed invalid coordinates. |
