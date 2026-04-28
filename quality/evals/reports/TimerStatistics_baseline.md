# TimerStatistics Contract Baseline

GeneratedAtUtc: `2026-04-28T05:39:32.3708272+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 23 |
| Passed | 23 |
| Failed | 0 |
| Runtime ms | 667.294 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| TimerStatistics | 23 | 23 | 0 | 29.013 | 6017 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Average correctness | 1 | 1 | 0 | 91.729 |
| Cumulative | 3 | 3 | 0 | 30.309 |
| Mode parsing | 2 | 2 | 0 | 30.934 |
| Numeric finiteness | 1 | 1 | 0 | 31.664 |
| Output contract | 3 | 3 | 0 | 20.441 |
| Reset interval | 3 | 3 | 0 | 82.342 |
| SingleShot | 3 | 3 | 0 | 27.42 |
| Trigger passthrough | 2 | 2 | 0 | 0.03 |
| Validation contract | 5 | 5 | 0 | 0.088 |

## Cases

| Case | Scenario | Passed | Runtime ms | Final Count | Final Total ms | Failure |
| --- | --- | --- | ---: | ---: | ---: | --- |
| singleshot_first_call_zero | SingleShot | Yes | 10.626 | 1 | 0 | - |
| singleshot_second_call_positive | SingleShot | Yes | 40.708 | 1 | 40.6452 | - |
| singleshot_default_mode | SingleShot | Yes | 30.927 | 1 | 30.8815 | - |
| cumulative_first_call_count_one | Cumulative | Yes | 0.187 | 1 | 0 | - |
| cumulative_second_call_count_two | Cumulative | Yes | 30.423 | 2 | 30.3621 | - |
| cumulative_three_calls_count_three | Cumulative | Yes | 60.316 | 3 | 60.272800000000004 | - |
| cumulative_average_equals_total_over_count | Average correctness | Yes | 91.729 | 4 | 91.67049999999999 | - |
| reset_interval_zero_no_reset | Reset interval | Yes | 92.634 | 4 | 92.5767 | - |
| reset_interval_two_resets | Reset interval | Yes | 61.736 | 1 | 30.045 | - |
| reset_interval_three_resets | Reset interval | Yes | 92.656 | 1 | 30.8637 | - |
| output_keys_singleshot | Output contract | Yes | 30.853 | 1 | 30.7968 | - |
| output_keys_cumulative | Output contract | Yes | 30.441 | 2 | 30.3843 | - |
| output_no_trigger_when_absent | Output contract | Yes | 0.028 | 1 | 0 | - |
| trigger_passthrough_string | Trigger passthrough | Yes | 0.016 | 1 | 0 | - |
| trigger_passthrough_int | Trigger passthrough | Yes | 0.043 | 1 | 0 | - |
| mode_lowercase_cumulative | Mode parsing | Yes | 31.123 | 2 | 31.0923 | - |
| mode_uppercase_cumulative | Mode parsing | Yes | 30.746 | 2 | 30.7062 | - |
| numeric_outputs_finite | Numeric finiteness | Yes | 31.664 | 2 | 31.6273 | - |
| validate_default_mode_ok | Validation contract | Yes | 0.329 | - | - | - |
| validate_singleshot_ok | Validation contract | Yes | 0.033 | - | - | - |
| validate_cumulative_ok | Validation contract | Yes | 0.007 | - | - | - |
| validate_invalid_mode | Validation contract | Yes | 0.058 | - | - | - |
| validate_reset_interval_negative | Validation contract | Yes | 0.011 | - | - | - |

## Notes

- Synthetic deterministic cases covering SingleShot/Cumulative modes, ResetInterval semantics, and trigger passthrough.
- Multi-call cases use Task.Delay between executions to drive non-zero elapsed measurements.
- Average correctness scenario verifies AverageMs == TotalMs / Count exactly across cumulative calls.
- Reset interval scenarios exercise both no-reset (interval=0) and resetting (interval=2,3) flows, asserting count cycling.
- Output contract scenarios assert presence of ElapsedMs/TotalMs/AverageMs/Count keys and conditional Trigger pass-through.
- Validation contract scenarios cover Mode allowlist (SingleShot|Cumulative) and ResetInterval bounds.
