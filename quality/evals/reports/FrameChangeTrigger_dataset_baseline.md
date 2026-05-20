# FrameChangeTrigger Dataset Baseline

GeneratedAtUtc: `2026-05-20T15:04:50.6292458+00:00`
EvidenceKind: `dataset`
DatasetId: `frame_change_trigger_synthetic_arrival_v1`
Profile: `line_fast_default`
Seed: `20260518`

## Summary

| Metric | Value | Gate |
| --- | ---: | ---: |
| Sequences | 140 | >= 120 |
| Passed | 140 | 140 |
| Failed | 0 | 0 |
| Trigger Precision | 1.0000 | >= 0.9800 |
| Trigger Recall | 1.0000 | >= 0.9500 |
| Duplicate Suppression Rate | 1.0000 | >= 0.9800 |
| Static/Noise False Trigger Rate | 0.0000 | <= 0.0200 |
| P95 Runtime ms | 0.285 | <= 3.000 |

## Scenarios

| Scenario | Cases | Passed | Failed | False Triggers | Misses | Avg ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| camera_jitter | 10 | 10 | 0 | 0 | 0 | 0.115 |
| compression_noise | 10 | 10 | 0 | 0 | 0 | 0.249 |
| lighting_drift | 10 | 10 | 0 | 0 | 0 | 0.115 |
| local_glare_flash | 10 | 10 | 0 | 0 | 0 | 0.110 |
| low_contrast_enter | 10 | 10 | 0 | 0 | 0 | 0.115 |
| outside_roi_motion | 10 | 10 | 0 | 0 | 0 | 0.104 |
| partial_occlusion_enter | 10 | 10 | 0 | 0 | 0 | 0.108 |
| roi_edge_enter | 10 | 10 | 0 | 0 | 0 | 0.097 |
| salt_pepper_noise | 10 | 10 | 0 | 0 | 0 | 0.133 |
| small_area_noise | 10 | 10 | 0 | 0 | 0 | 0.111 |
| static_empty | 10 | 10 | 0 | 0 | 0 | 3.179 |
| terminal_enter_once | 10 | 10 | 0 | 0 | 0 | 0.114 |
| terminal_reenter_after_cooldown | 10 | 10 | 0 | 0 | 0 | 0.119 |
| terminal_stay_cooldown | 10 | 10 | 0 | 0 | 0 | 0.105 |

## Dataset Contract

- Manifest: `quality/datasets/manifests/FrameChangeTrigger_synthetic_arrival_manifest.json`
- Frame size: 320x320; ROI: 32,32,256,256
- Scenarios cover static empty frames, arrival, dwell suppression, re-entry, small area noise, salt-pepper noise, compression noise, lighting drift, local glare, camera jitter, ROI-outside motion, ROI-edge entry, partial occlusion, and low-contrast entry.
- This is deterministic repo-local synthetic evidence; it does not claim real production-line validation.
