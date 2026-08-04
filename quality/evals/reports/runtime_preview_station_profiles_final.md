# RuntimePreview Station Profiles Final

- Generated UTC: `2026-08-04T04:17:55.796269+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| sp-release-standard-v14 | standard_vision_ipc | true | redacted |
| sp-dl-review-v14 | deep_learning_review_ipc | true | redacted |
| sp-low-ipc-v12 | low_spec_ipc | true | redacted |
| sp-multi-camera-v14 | multi_camera_station | true | redacted |
| sp-output-lite-v14 | output_lite_station | true | redacted |
| sp-detection-only-v14 | model_limited_station | true | redacted |
| sp-legacy-runtime-v12 | legacy_runtime_station | true | redacted |
| sp-multi-station-v14 | multi_station_review | true | redacted |
| sp-plc-denied-v14 | plc_denied_station | true | redacted |
| sp-release-approval-v14 | release_approval_station | true | redacted |
| sp-template-only-v14 | template_only_station | true | redacted |
| sp-measurement-only-v14 | measurement_only_station | true | redacted |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
