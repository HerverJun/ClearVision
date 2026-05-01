# DeepLearning Postprocess Pressure Baseline

GeneratedAtUtc: `2026-04-28T16:12:02.1894665+00:00`

| Case | Candidates | Class distribution | Selected | CandidateLimit | DroppedBeforeNms | Runtime ms | Runtime budget ms | Memory bytes | Memory budget bytes | Passed | Failure |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| postprocess_1k_balanced_classes | 1000 | 0:334, 1:333, 2:333 | 1000 | 10000 | 0 | 16.961 | 750 | 1042680 | 8388608 | True | - |
| postprocess_5k_balanced_classes | 5000 | 0:1667, 1:1667, 2:1666 | 5000 | 10000 | 0 | 8.221 | 750 | 4988136 | 25165824 | True | - |
| postprocess_10k_skewed_classes | 10000 | 0:8710, 1:1000, 2:290 | 6420 | 10000 | 0 | 15.424 | 1000 | 7976432 | 50331648 | True | - |
