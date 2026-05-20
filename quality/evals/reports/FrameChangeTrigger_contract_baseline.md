# FrameChangeTrigger Contract Baseline

GeneratedAtUtc: `2026-05-20T15:04:34.0932487+00:00`
EvidenceKind: `contract`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 31 |
| Passed | 31 |
| Failed | 0 |
| Runtime ms | 130.700 |
| Avg runtime ms | 4.216 |
| Avg memory bytes | 36070 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| baseline and short-circuit | 4 | 4 | 0 | 27.900 |
| enablement | 2 | 2 | 0 | 0.904 |
| input failure | 2 | 2 | 0 | 0.258 |
| output contract | 1 | 1 | 0 | 0.332 |
| parameter validation | 14 | 14 | 0 | 0.624 |
| robustness | 2 | 2 | 0 | 1.830 |
| roi boundary | 1 | 1 | 0 | 1.089 |
| state isolation | 2 | 2 | 0 | 1.168 |
| trigger semantics | 3 | 3 | 0 | 0.211 |

## Cases

| Case | Scenario | Passed | Runtime ms | Failure |
| --- | --- | --- | ---: | --- |
| first_frame_builds_baseline_and_short_circuits | baseline and short-circuit | True | 87.195 |  |
| large_area_change_triggers_and_continues | baseline and short-circuit | True | 23.355 |  |
| small_change_below_threshold_short_circuits | baseline and short-circuit | True | 0.591 |  |
| cooldown_suppresses_duplicate_arrival | baseline and short-circuit | True | 0.459 |  |
| disabled_passthrough_does_not_short_circuit | enablement | True | 1.013 |  |
| short_circuit_false_passes_untriggered_frame | enablement | True | 0.794 |  |
| missing_image_fails_with_stable_message | input failure | True | 0.434 |  |
| empty_image_fails_with_stable_message | input failure | True | 0.081 |  |
| invalid_pixel_threshold_low_rejected | parameter validation | True | 0.429 |  |
| invalid_pixel_threshold_high_rejected | parameter validation | True | 0.039 |  |
| invalid_min_change_ratio_low_rejected | parameter validation | True | 0.088 |  |
| invalid_min_change_ratio_high_rejected | parameter validation | True | 0.101 |  |
| invalid_min_change_pixels_rejected | parameter validation | True | 0.267 |  |
| invalid_cooldown_low_rejected | parameter validation | True | 0.036 |  |
| invalid_cooldown_high_rejected | parameter validation | True | 0.020 |  |
| invalid_roi_negative_rejected | parameter validation | True | 0.017 |  |
| invalid_enabled_type_rejected | parameter validation | True | 4.468 |  |
| invalid_short_circuit_type_rejected | parameter validation | True | 0.093 |  |
| invalid_normalize_mode_rejected | parameter validation | True | 2.806 |  |
| invalid_reference_update_mode_rejected | parameter validation | True | 0.287 |  |
| invalid_blur_size_even_rejected | parameter validation | True | 0.046 |  |
| invalid_reference_alpha_rejected | parameter validation | True | 0.032 |  |
| roi_clamps_to_image_boundary | roi boundary | True | 1.089 |  |
| output_fields_are_complete | output contract | True | 0.332 |  |
| operator_instances_keep_independent_baselines | state isolation | True | 0.435 |  |
| dispose_clears_state_for_long_running_reuse | state isolation | True | 1.900 |  |
| mean_shift_lighting_drift_does_not_trigger | robustness | True | 3.029 |  |
| noise_guard_suppresses_salt_pepper_noise | robustness | True | 0.632 |  |
| min_consecutive_changed_frames_suppresses_single_flash | trigger semantics | True | 0.266 |  |
| rising_edge_only_suppresses_sustained_change | trigger semantics | True | 0.192 |  |
| reset_after_no_change_allows_second_arrival | trigger semantics | True | 0.174 |  |

## Notes

- Ordinary xUnit tests remain product regression tests; this runner is the accepted quality contract evidence source for the matrix.
- Contract coverage includes baseline short-circuit behavior, trigger/pass-through semantics, cooldown, ROI clamping, validation failures, output-field completeness, state isolation, and default-off robustness knobs.
- This report is deterministic synthetic contract evidence; it is not a real production-site sign-off.
