# Option D G1 Evidence Manifest

```text
EVIDENCE_ID=option-d-g1-cdd1140-local-close-20260824
GATE=G1
STATE=PASS
RUN_ID=option-d-g1-candidate-20260824
REFERENCE_GATE_INVOCATION_ID=439e4f93-6003-4cf9-8edd-d0ca23808c1c
CANDIDATE_GATE_INVOCATION_ID=f85725a5-944b-4cc0-ae5d-3c157b58237f
AUDIT_DATE=2026-08-24
TIMEZONE=Asia/Shanghai
SOURCE_HEAD=cdd114082821bbe750fb7945a0c3a4e89002d67c
G0_STATE=PASS
G2_STATE=READY
PRODUCTION_ACCEPTANCE=NOT_GRANTED
LEGACY_RETIREMENT=NOT_APPROVED
```

## 1. Authority And Scope

| Item | Result | Evidence |
| --- | --- | --- |
| Visual authority | PASS | Option D remains the only visual authority; no `_visual_master` PNG was overwritten, regenerated or resampled |
| Functional/architecture authority | PASS | current code + F10; no endpoint, transport, HostBridge, authority, permission, persistence or business capability changed |
| Gate sequencing | PASS | G0 was already PASS; changes are limited to Design System/Labs/G1 tests and evidence; no G2 page was implemented |
| Deterministic data | PASS | `option-d-g1-design-system.v1`, browser-test session, fixed theme/density/motion and no business API data |
| Canvas authority | PASS | Canvas Lab reuses the canonical host and current node/port/connection semantics; no second Canvas or node CSS |

## 2. Implementation Inventory

- Shared foundation: generic border/focus/scrollbar/content geometry tokens and equivalent base CSS consumption.
- Design Lab: deterministic fixture identity, six composition samples, primitive/state/status matrix, light/dark, compact/comfortable and reduced-motion projection.
- Canvas Lab: fixture-consistent diagnostics, single owner/subscription lifecycle, cleanup-on-failure, live diagnostic status, canonical shortcut metadata and wrap-safe error surfaces.
- Design primitives: DataTable loading copy, semantic `h1`, loading/search Chinese copy and focused regression assertions.
- G1 visual gate: dedicated invocation-bound reference/candidate commands, fail-fast missing phase/invocation, frozen-hash reference authentication, complete-capture postcondition, whole-image diff/overlay and hard Master measurement assertions.

Unrelated user work is excluded, including the pre-existing `f03-workspace.spec.ts` edit, `_visual_master/` and anomalously named untracked root items. No reset, clean, stash, branch switch or deletion was performed.

## 3. Master Measurement Gate

| Check | Result |
| --- | --- |
| Fixture | `option-d-g1-master-measurements.v2` |
| Sources | D05 / D13 / D16 current Master PNGs |
| Source SHA-256 | 3/3 exact |
| Dimensions | 3/3 `3840x2160` |
| Geometry | 17/17 exact edge pixels; every edge exceeds its frozen minimum response |
| Color | 18/18 exact RGBA samples |
| Scale | output-to-CSS `2:1`, `1920x1080` CSS viewport |
| Masks/resampling | none |
| Machine manifest | `.tmp/studio-ui-next/option-d-g1/master-measurements.json` |
| Manifest SHA-256 | `0815b823d2c8d8ad73024dd2ab1fc287c1ad5351a97e9cbc21f5f4ada76595f1` |

Token dispositions and later-Gate consumption constraints are frozen in
`OptionD_G1_MasterAnchorTokenLedger.md`.

## 4. Visual Evidence

Environment: Chromium, `1920x1080` CSS viewport, DSF `2`, output `3840x2160`, light/compact and dark/compact. Comparison is exact whole-image RGBA with per-channel threshold `8`, changed-pixel ratio limit `1%`, and `NO_MASKS`.

| Capture | Reference SHA-256 | Candidate SHA-256 | Changed pixels / ratio | Result |
| --- | --- | --- | ---: | --- |
| Design light | `b4a7985a23bef122184737a1b99be2bf270002f9c607914b13018c16550aac60` | same | `0 / 0` | PASS |
| Design dark | `c0fdc24277cb50fd2fce252bd4809d97e0e60a9a111b9601136d2481f9bf319d` | same | `0 / 0` | PASS |
| Canvas light | `8d10052afe4ccf7746ea3bd81621036d4cd79312cf212c343fb0f87eacfac502` | `5e036313610079225747284726a17eb5f38cb6f75aa6427b1c3febaf1176070c` | `11487 / 0.001384910300925926` | PASS |
| Canvas dark | `37f91663f141ba7118179eac319f3e64158fcaa67682dd04529fa875ad5091bd` | `ce5083ca3d30a1553b836f89a989a25a7c9f4105565e6de8381c4de54ac45dca` | `11126 / 0.0013413869598765432` | PASS |

Reference manifest SHA-256: `13d034c8c040682e898e7b3e0ab2ebd044d58acad543b17c59fbe7459c8f9cac`.
Candidate manifest SHA-256: `7e839374f6a77024207aa98022b29ef82621731088c2da3fead4fdc2601b6908`.

All four candidate PNGs have diff and overlay artifacts. Design is byte-identical. Canvas differences are confined to antialiased diagnostic glyph edges; canonical node, port, connection, geometry and state semantics did not move. No mask was used.

## 5. Functional, Accessibility And Cleanup Evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Design state matrix | PASS | buttons/fields/table/pagination/overlays/splitter/tabs/status/page patterns exercised across themes, density and reduced motion |
| Canvas owner | PASS | one active owner; conflict rejected; disposed controller commands rejected |
| Canvas cleanup | PASS | 20 route mount/unmount cycles; owner count, subscriptions, observers, RAF, timer, interaction and adapter resources return to zero |
| Failure cleanup | PASS | subscribe, initial diagnostics callback, interaction dispose and adapter dispose error paths retain ordered cleanup |
| Diagnostic accessibility | PASS | polite atomic live status, merged canonical tooltip/help/status descriptions, actual scoped shortcut metadata, and unavailable-runtime errors included in the described live text |
| Long diagnostics | PASS | alert and Canvas state messages use `overflow-wrap:anywhere` and browser-computed style is asserted |
| Canonical keyboard facts | PARTIAL_BY_EXISTING_CAPABILITY | copy/paste/delete/undo/redo/select-all/Escape and toolbar commands exist; complete node keyboard navigation/move/connect is not claimed and remains G7 scope |
| Deterministic network | PASS | declared health/auth routes only; unhandled `/api/**`, failed requests and non-2xx API/health responses fail the visual evidence |
| Web Interface Guidelines | PASS_WITH_RECORDED_RESIDUAL | source `vercel-labs/web-interface-guidelines/command.md`, commit `e3d624baaf29dc1fc645aff3e38f03e564d2d6b1`, source commit time `2026-08-17T17:21:06-07:00`, fetched 2026-08-24; in-scope G1 findings fixed, canonical keyboard residual recorded for G7 |

## 6. Verification Matrix

| Verification | Result | Notes |
| --- | --- | --- |
| G1 reference hard gate | PASS | Master assertions + Canvas Foundation 7 + Design Foundation 6 + visual 4 = `17/17`, one worker; immutable PNG hashes and schema-v2 invocation-bound reference manifest passed |
| G1 candidate hard gate | PASS | Master assertions + Canvas Foundation 7 + Design Foundation 6 + visual 4 = `17/17`, one worker; invocation-bound candidate manifest and diff/overlay postcondition passed |
| missing visual phase/invocation | PASS | direct visual spec command fails at config before server startup, exit code `1` |
| StudioUI lint | PASS | `eslint . --max-warnings=0` |
| StudioUI typecheck | PASS | app/vitest/node TypeScript projects |
| focused Canvas owner unit | PASS | 1 file / 13 tests; frozen validation call identities and disposed facade listener included |
| StudioUI full Vitest | PASS | 144 files / 953 tests |
| production build | PASS | Vite 8.1.4, 544 modules |
| bundle gate | PASS | production bundle budgets |
| affected Playwright | PASS | final G1 hard gate `17/17` |
| independent code/Owner review | PASS | fresh narrow review returned `NO_APPLICABLE_P0_P3_FINDINGS` |
| independent visual/gate review | PASS | stale schema-v1 reference manifest P1 was fixed; fresh fix re-review returned `NO_APPLICABLE_P0_P3_FINDINGS` |
| independent documentation/status review | PASS | stale G0-era state wording was fixed; fresh fix re-review returned `NO_APPLICABLE_P0_P3_FINDINGS` |
| `git diff --check` | PASS | tracked changes and every untracked G1 implementation/evidence file have no whitespace errors; no G2 page is in the changed-file set |

Two earlier hard-gate diagnostic runs failed only on new Playwright expectation assumptions: canonical `aria-describedby` ownership and fixture display identity. The implementation was made additive to the canonical tooltip; the expectations were corrected to actual fixture identities. The final `17/17` run supersedes those related failures.

The first independent close review found two P1 visual-gate completeness/reference-authentication gaps and four applicable P2 evidence/accessibility/test-sensitivity gaps. The first final re-review then found one additional P1: the existing immutable reference PNGs were still paired with a schema-v1 manifest that the invocation-bound gate could not refresh. Reference verification now reuses and authenticates immutable PNGs while replacing only the per-invocation schema-v2 manifest; fresh reference and candidate hard gates both passed `17/17`. Documentation review also found stale G0-era READY/not-started wording, which was corrected without changing G2 sequencing. Fresh visual-fix and documentation-fix reviewers both returned `NO_APPLICABLE_P0_P3_FINDINGS`; the code/Owner review had already returned the same result. The gate invalidates stale Master PASS output, rejects unhandled API traffic, announces Canvas runtime errors, and asserts canonical connection arguments plus facade subscription cleanup. The frozen `1%` whole-image threshold is retained exactly as specified by the plan; the review suggestion to change it was not applicable.

## 7. External Evidence Boundaries

| Evidence class | State |
| --- | --- |
| real WebView2 this Gate | NOT_PERFORMED |
| Windows 125% native DPI | NOT_PERFORMED |
| independent no-Node target | NOT_PERFORMED |
| Remote CI | NOT_RUN |
| field Camera/PLC/Station/AI | NOT_PERFORMED |
| production soak/signoff | NOT_PERFORMED |

Chromium DSF2, local production build and historical host evidence do not replace these classes. G1 does not grant production acceptance or Legacy retirement.

## 8. Final Gate Decision

```text
ODD_DS_01=PASS
ODD_DS_02=PASS
ODD_DS_03=PASS
G1_LOCAL_IMPLEMENTATION=PASS
G1_INDEPENDENT_REVIEW=PASS
G1_STATE=PASS
G2_STATE=READY
```
