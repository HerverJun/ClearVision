# DualModalVoting Contract Baseline

GeneratedAtUtc: `2026-04-29T03:30:11.0758739+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 31 |
| Passed | 31 |
| Failed | 0 |
| Runtime ms | 104.744 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Input extraction | 5 | 5 | 0 | 0.225 |
| Majority strategy | 5 | 5 | 0 | 0.178 |
| Missing input contract | 3 | 3 | 0 | 0.216 |
| Output contract | 1 | 1 | 0 | 0.186 |
| Priority strategies | 2 | 2 | 0 | 0.274 |
| Private helper contract | 2 | 2 | 0 | 0.338 |
| Strategy parsing | 1 | 1 | 0 | 0.531 |
| Unanimous strategy | 2 | 2 | 0 | 0.190 |
| Validation contract | 4 | 4 | 0 | 0.082 |
| WeightedAverage strategy | 6 | 6 | 0 | 16.572 |

## Cases

| Case | Scenario | Passed | Runtime ms | Failure |
| --- | --- | --- | ---: | --- |
| weighted_average_ok_probability_mixed_modalities | WeightedAverage strategy | True | 98.267 |  |
| weighted_average_high_confidence_ng_wins | WeightedAverage strategy | True | 0.278 |  |
| weighted_average_boundary_is_ok | WeightedAverage strategy | True | 0.198 |  |
| weighted_average_custom_threshold_flips_to_ng | WeightedAverage strategy | True | 0.186 |  |
| weighted_average_zero_weights_fail | WeightedAverage strategy | True | 0.306 |  |
| weighted_average_custom_weights_normalized | WeightedAverage strategy | True | 0.198 |  |
| unanimous_both_ok_is_ok | Unanimous strategy | True | 0.184 |  |
| unanimous_one_ng_is_ng_confidence | Unanimous strategy | True | 0.196 |  |
| majority_same_ok_averages_ok_probability | Majority strategy | True | 0.174 |  |
| majority_same_ng_uses_final_ng_confidence | Majority strategy | True | 0.188 |  |
| majority_conflict_higher_dl_confidence_wins | Majority strategy | True | 0.187 |  |
| majority_conflict_higher_traditional_confidence_wins | Majority strategy | True | 0.175 |  |
| majority_conflict_equal_confidence_prefers_dl | Majority strategy | True | 0.166 |  |
| prioritize_deep_learning_follows_dl | Priority strategies | True | 0.325 |  |
| prioritize_traditional_follows_traditional | Priority strategies | True | 0.222 |  |
| case_insensitive_strategy_executes | Strategy parsing | True | 0.531 |  |
| dictionary_isok_confidence_clamps_high | Input extraction | True | 0.221 |  |
| dictionary_isok_confidence_clamps_low | Input extraction | True | 0.211 |  |
| defect_count_good_maps_to_ok | Input extraction | True | 0.240 |  |
| defect_count_uses_max_defect_confidence | Input extraction | True | 0.248 |  |
| defect_count_missing_confidence_is_conservative_ng | Input extraction | True | 0.207 |  |
| missing_traditional_uses_neutral_probability | Missing input contract | True | 0.246 |  |
| missing_dl_uses_neutral_probability | Missing input contract | True | 0.209 |  |
| no_valid_inputs_fail | Missing input contract | True | 0.193 |  |
| custom_judgment_values_are_used | Output contract | True | 0.186 |  |
| validate_defaults_valid | Validation contract | True | 0.072 |  |
| validate_bad_strategy_invalid | Validation contract | True | 0.148 |  |
| validate_weight_sum_zero_invalid | Validation contract | True | 0.061 |  |
| validate_weight_sum_not_one_invalid | Validation contract | True | 0.046 |  |
| normalize_strategy_trims_and_canonicalizes | Private helper contract | True | 0.549 |  |
| failed_detection_result_is_neutral | Private helper contract | True | 0.126 |  |

## Notes

- This is a pure contract baseline for dual-modal decision fusion using controlled DetectionResult and dictionary inputs.
- It validates all voting strategies, OK-probability conversion, missing-input behavior, DefectCount extraction, custom judgment values, strategy parsing, and validation failures.
- It does not claim vision-model accuracy; it locks the decision contract that consumes upstream model and rule outputs.
