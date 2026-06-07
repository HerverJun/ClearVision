# RuntimePreview Governance Audit Sample

- Generated UTC: `2026-06-06T23:47:15.436775+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| session_created | session_created | true | true |
| config_changed | config_changed | true | true |
| catalog_loaded | catalog_loaded | true | true |
| allowlist_changed | allowlist_changed | true | true |
| readiness_checked | readiness_checked | true | true |
| permission_denied | permission_denied | true | true |
| simulation_started | simulation_started | true | true |
| simulation_completed | simulation_completed | true | true |
| report_generated | report_generated | true | true |
| deploy_readiness_generated | deploy_readiness_generated | true | true |
| package_readiness_generated | package_readiness_generated | true | true |
| manifest_dry_run_generated | manifest_dry_run_generated | true | true |
| station_compatibility_generated | station_compatibility_generated | true | true |
| operator_contract_validation_generated | operator_contract_validation_generated | true | true |
| pre_release_review_generated | pre_release_review_generated | true | true |
| governance_exported | governance_exported | true | true |
| retention_cleanup | retention_cleanup | true | true |
| corruption_recovered | corruption_recovered | true | true |
| session_cancelled | session_cancelled | true | true |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
