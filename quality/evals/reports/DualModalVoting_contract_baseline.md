# DualModalVoting Contract Baseline

GeneratedAtUtc: `2026-04-26T09:07:51.1929552+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 31 |
| Passed | 31 |
| Failed | 0 |
| Runtime ms | 51.038 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Input extraction | 5 | 5 | 0 | 0.229 |
| Majority strategy | 5 | 5 | 0 | 0.220 |
| Missing input contract | 3 | 3 | 0 | 0.202 |
| Output contract | 1 | 1 | 0 | 0.182 |
| Priority strategies | 2 | 2 | 0 | 0.285 |
| Private helper contract | 2 | 2 | 0 | 0.344 |
| Strategy parsing | 1 | 1 | 0 | 0.537 |
| Unanimous strategy | 2 | 2 | 0 | 0.188 |
| Validation contract | 4 | 4 | 0 | 0.077 |
| WeightedAverage strategy | 6 | 6 | 0 | 7.587 |

## Cases

| Case | Scenario | Passed | Runtime ms | Failure |
| --- | --- | --- | ---: | --- |
| weighted_average_ok_probability_mixed_modalities | WeightedAverage strategy | True | 44.331 |  |
| weighted_average_high_confidence_ng_wins | WeightedAverage strategy | True | 0.277 |  |
| weighted_average_boundary_is_ok | WeightedAverage strategy | True | 0.199 |  |
| weighted_average_custom_threshold_flips_to_ng | WeightedAverage strategy | True | 0.184 |  |
| weighted_average_zero_weights_fail | WeightedAverage strategy | True | 0.312 |  |
| weighted_average_custom_weights_normalized | WeightedAverage strategy | True | 0.218 |  |
| unanimous_both_ok_is_ok | Unanimous strategy | True | 0.189 |  |
| unanimous_one_ng_is_ng_confidence | Unanimous strategy | True | 0.188 |  |
| majority_same_ok_averages_ok_probability | Majority strategy | True | 0.274 |  |
| majority_same_ng_uses_final_ng_confidence | Majority strategy | True | 0.239 |  |
| majority_conflict_higher_dl_confidence_wins | Majority strategy | True | 0.220 |  |
| majority_conflict_higher_traditional_confidence_wins | Majority strategy | True | 0.191 |  |
| majority_conflict_equal_confidence_prefers_dl | Majority strategy | True | 0.178 |  |
| prioritize_deep_learning_follows_dl | Priority strategies | True | 0.340 |  |
| prioritize_traditional_follows_traditional | Priority strategies | True | 0.230 |  |
| case_insensitive_strategy_executes | Strategy parsing | True | 0.537 |  |
| dictionary_isok_confidence_clamps_high | Input extraction | True | 0.230 |  |
| dictionary_isok_confidence_clamps_low | Input extraction | True | 0.208 |  |
| defect_count_good_maps_to_ok | Input extraction | True | 0.228 |  |
| defect_count_uses_max_defect_confidence | Input extraction | True | 0.253 |  |
| defect_count_missing_confidence_is_conservative_ng | Input extraction | True | 0.228 |  |
| missing_traditional_uses_neutral_probability | Missing input contract | True | 0.251 |  |
| missing_dl_uses_neutral_probability | Missing input contract | True | 0.179 |  |
| no_valid_inputs_fail | Missing input contract | True | 0.177 |  |
| custom_judgment_values_are_used | Output contract | True | 0.182 |  |
| validate_defaults_valid | Validation contract | True | 0.072 |  |
| validate_bad_strategy_invalid | Validation contract | True | 0.141 |  |
| validate_weight_sum_zero_invalid | Validation contract | True | 0.053 |  |
| validate_weight_sum_not_one_invalid | Validation contract | True | 0.041 |  |
| normalize_strategy_trims_and_canonicalizes | Private helper contract | True | 0.561 |  |
| failed_detection_result_is_neutral | Private helper contract | True | 0.127 |  |

## Notes

- This is a pure contract baseline for dual-modal decision fusion using controlled DetectionResult and dictionary inputs.
- It validates all voting strategies, OK-probability conversion, missing-input behavior, DefectCount extraction, custom judgment values, strategy parsing, and validation failures.
- It does not claim vision-model accuracy; it locks the decision contract that consumes upstream model and rule outputs.
