# FrameChangeTrigger Field-Substitute Baseline

GeneratedAtUtc: `2026-05-20T15:04:50.6858939+00:00`
EvidenceKind: `field`
ReplayId: `frame_change_trigger_field_substitute_v1`
Pipeline: `ImageAcquisition(Continuous) -> FrameChangeTrigger -> DeepLearning -> BoxFilter -> BoxNms -> DetectionSequenceJudge -> ResultOutput`

## Summary

| Metric | Value | Gate |
| --- | ---: | ---: |
| Replay cases | 20 | >= 20 |
| Passed | 20 | 20 |
| Failed | 0 | 0 |
| No-material downstream executions | 0 | 0 |
| Arrival downstream executions | 14 | 14 |
| Trigger frame mismatches | 0 | 0 |

## Cases

| Case | Scenario | Expected | Downstream | No-material downstream | Passed |
| --- | --- | --- | --- | ---: | --- |
| static_empty_00 | static_empty |  |  | 0 | True |
| static_empty_01 | static_empty |  |  | 0 | True |
| terminal_enter_once_00 | terminal_enter_once | 2 | 2 | 0 | True |
| terminal_enter_once_01 | terminal_enter_once | 2 | 2 | 0 | True |
| terminal_stay_cooldown_00 | terminal_stay_cooldown | 2 | 2 | 0 | True |
| terminal_stay_cooldown_01 | terminal_stay_cooldown | 2 | 2 | 0 | True |
| terminal_reenter_after_cooldown_00 | terminal_reenter_after_cooldown | 1,6 | 1,6 | 0 | True |
| terminal_reenter_after_cooldown_01 | terminal_reenter_after_cooldown | 1,6 | 1,6 | 0 | True |
| salt_pepper_noise_00 | salt_pepper_noise |  |  | 0 | True |
| salt_pepper_noise_01 | salt_pepper_noise |  |  | 0 | True |
| lighting_drift_00 | lighting_drift |  |  | 0 | True |
| lighting_drift_01 | lighting_drift |  |  | 0 | True |
| outside_roi_motion_00 | outside_roi_motion |  |  | 0 | True |
| outside_roi_motion_01 | outside_roi_motion |  |  | 0 | True |
| roi_edge_enter_00 | roi_edge_enter | 2 | 2 | 0 | True |
| roi_edge_enter_01 | roi_edge_enter | 2 | 2 | 0 | True |
| partial_occlusion_enter_00 | partial_occlusion_enter | 2 | 2 | 0 | True |
| partial_occlusion_enter_01 | partial_occlusion_enter | 2 | 2 | 0 | True |
| low_contrast_enter_00 | low_contrast_enter | 2 | 2 | 0 | True |
| low_contrast_enter_01 | low_contrast_enter | 2 | 2 | 0 | True |

## Boundary Statement

- This report is field-substitute replay evidence built from anonymous synthetic frames and the wire-sequence video-stream topology.
- It validates that no-material frames short-circuit before DeepLearning and that arrival frames continue downstream.
- It is not a real production-site sign-off and must not be described as customer or line validation.
