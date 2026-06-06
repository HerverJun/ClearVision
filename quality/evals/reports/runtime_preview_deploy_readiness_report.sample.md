# RuntimePreview Deploy Readiness Report

- Generated UTC: `2026-06-06T12:31:57.847051+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`

| Case | Readiness | Preview | Precheck | Blocked |
| --- | --- | --- | --- | --- |
| RP-SE-001 | ready | True | True | False |
| RP-SE-002 | ready | True | True | False |
| RP-SE-003 | ready | True | True | False |
| RP-SE-004 | ready | True | True | False |
| RP-SE-005 | not_ready | False | False | True |
| RP-SE-006 | denied | False | False | True |
| RP-SE-007 | denied | False | False | True |
| RP-SE-008 | not_ready | False | False | True |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, packaging, deployment, hot-load, or Real RuntimePreview adapter.
