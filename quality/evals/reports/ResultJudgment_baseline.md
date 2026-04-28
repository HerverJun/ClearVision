# ResultJudgment Contract Baseline

GeneratedAtUtc: `2026-04-28T05:25:08.5604218+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 29 |
| Passed | 29 |
| Failed | 0 |
| Runtime ms | 19.561 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| ResultJudgment | 29 | 29 | 0 | 0.675 | 4978 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Confidence Gate | 3 | 3 | 0 | 0.008 |
| Equal | 4 | 4 | 0 | 4.795 |
| Field Resolution | 2 | 2 | 0 | 0.01 |
| GreaterOrEqual | 2 | 2 | 0 | 0.009 |
| GreaterThan | 2 | 2 | 0 | 0.008 |
| LessOrEqual | 2 | 2 | 0 | 0.008 |
| LessThan | 2 | 2 | 0 | 0.01 |
| NotEqual | 2 | 2 | 0 | 0.016 |
| Output contract | 2 | 2 | 0 | 0.01 |
| Range | 3 | 3 | 0 | 0.011 |
| Validation contract | 5 | 5 | 0 | 0.036 |

## Cases

| Case | Scenario | Passed | Runtime ms | Judgment | Condition | Failure |
| --- | --- | --- | ---: | --- | --- | --- |
| equal_numeric_match | Equal | Yes | 18.496 | OK | Equal | - |
| equal_numeric_mismatch | Equal | Yes | 0.048 | NG | Equal | - |
| equal_string_match | Equal | Yes | 0.623 | OK | Equal | - |
| equal_string_mismatch | Equal | Yes | 0.014 | NG | Equal | - |
| notequal_numeric_diff | NotEqual | Yes | 0.016 | OK | NotEqual | - |
| notequal_numeric_same | NotEqual | Yes | 0.016 | NG | NotEqual | - |
| greaterthan_pass | GreaterThan | Yes | 0.009 | OK | GreaterThan | - |
| greaterthan_fail_equal | GreaterThan | Yes | 0.008 | NG | GreaterThan | - |
| lessthan_pass | LessThan | Yes | 0.01 | OK | LessThan | - |
| lessthan_fail_equal | LessThan | Yes | 0.01 | NG | LessThan | - |
| ge_pass_strict | GreaterOrEqual | Yes | 0.01 | OK | GreaterOrEqual | - |
| ge_pass_equal_tolerance | GreaterOrEqual | Yes | 0.008 | OK | GreaterOrEqual | - |
| le_pass_strict | LessOrEqual | Yes | 0.008 | OK | LessOrEqual | - |
| le_pass_equal_tolerance | LessOrEqual | Yes | 0.008 | OK | LessOrEqual | - |
| range_inside | Range | Yes | 0.012 | OK | Range | - |
| range_below | Range | Yes | 0.01 | NG | Range | - |
| range_above | Range | Yes | 0.01 | NG | Range | - |
| confidence_above_threshold | Confidence Gate | Yes | 0.009 | OK | Equal | - |
| confidence_below_threshold_gates_to_ng | Confidence Gate | Yes | 0.008 | NG | MinConfidenceGate | - |
| confidence_default_zero_always_passes | Confidence Gate | Yes | 0.008 | OK | Equal | - |
| field_custom | Field Resolution | Yes | 0.01 | OK | Equal | - |
| field_fallback_to_value | Field Resolution | Yes | 0.01 | OK | Equal | - |
| output_keys_when_ok | Output contract | Yes | 0.01 | OK | Equal | - |
| output_keys_when_ng | Output contract | Yes | 0.01 | NG | Equal | - |
| validate_defaults_ok | Validation contract | Yes | 0.131 | - | - | - |
| validate_min_confidence_below_zero | Validation contract | Yes | 0.039 | - | - | - |
| validate_min_confidence_above_one | Validation contract | Yes | 0.003 | - | - | - |
| validate_abs_tol_negative | Validation contract | Yes | 0.003 | - | - | - |
| validate_rel_tol_above_one | Validation contract | Yes | 0.004 | - | - | - |

## Notes

- Synthetic deterministic cases covering Equal/NotEqual/GreaterThan/LessThan/GreaterOrEqual/LessOrEqual/Range conditions.
- Confidence gate scenarios verify MinConfidence threshold short-circuits to NG with `MinConfidenceGate` condition.
- Field resolution scenarios cover custom FieldName lookup and fallback to `Value` input.
- Output contract scenarios assert the seven-key output bundle (JudgmentResult/IsOk/ConditionResult/JudgmentValue/Details/Condition/ActualValue).
- Validation contract scenarios exercise MinConfidence, NumericAbsTolerance, and NumericRelTolerance bounds checks.
