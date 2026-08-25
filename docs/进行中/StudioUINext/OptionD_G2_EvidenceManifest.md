# Option D G2 Evidence Manifest

```text
EVIDENCE_ID=option-d-g2-cdd1140-local-close-20260824
GATE=G2
STATE=REOPENED_IN_PROGRESS
RUN_ID=option-d-g2-candidate-20260824
REFERENCE_GATE_INVOCATION_ID=14e1d9c0-46e2-466d-866c-0a43e5472db0
CANDIDATE_GATE_INVOCATION_ID=86b5e3c0-b974-4600-abd8-6e0cb0b56a0c
AUDIT_DATE=2026-08-24
TIMEZONE=Asia/Shanghai
SOURCE_HEAD=cdd114082821bbe750fb7945a0c3a4e89002d67c
G1_MANIFEST_SHA256=c7139d2f11dfcfba24536d019d4c4f394278c7ed086b946c6a60a37cc284d079
TRACKED_DIFF_SHA256=9c1d374ab7b08556a5f0179294f1b323a8ce6b2b50fec870d11384783c0a9bf1
UNTRACKED_FILENAME_LIST_SHA256=77a7fa1cf3fbd0672b912dbd770937487f36618f2e45b3b14b26675db5f7c4bd
G1_STATE=PASS
G3_STATE=BLOCKED_BY_DEPENDENCY
REOPEN_APPROVAL=APPROVED_HERVERJUN_2026_08_24
REOPEN_SIGNER_ROLES=PRODUCT_SECURITY_QA_RELEASE_CAPABILITY_OWNER
WHOLE_PAGE_VISUAL_AUTHORITY=RAW_OPTION_D_MASTER_ONLY
CAPABILITY_PRESERVATION=APPROVED_RELOCATION_OR_PROGRESSIVE_DISCLOSURE
G4_START_AUTHORIZATION=NOT_GRANTED
PRODUCTION_ACCEPTANCE=NOT_GRANTED
LEGACY_RETIREMENT=NOT_APPROVED
```

## 1. Authority And Scope

| Item | Result | Evidence |
| --- | --- | --- |
| Visual authority | PASS | Option D remains the only visual authority; no `_visual_master` PNG was regenerated, resampled, overwritten or replaced |
| Functional/architecture authority | PASS | current code + F10; no endpoint, API transport, HostBridge, permission, persistence chain, runtime authority or Station authority changed |
| Gate sequencing | PASS | G1 is PASS; work is limited to Product Shell, Auth, shared product states, G2 tests and evidence; no G3 capability page was implemented |
| Deterministic visual data | PASS | `option-d-g2-shell-auth.v1`, browser-test session, fixed theme/density/viewport and declared auth/startup responses |
| Canvas authority | PASS | no production FlowCanvas file changed; canonical node, port, connection, selection and execution semantics remain unchanged |
| Dark-theme authority | PASS | theme changes continue through global tokens; no D19/D20 route-specific dark CSS or Settings geometry fork was added |

The working tree already contained G0, G1, user-owned and anomalously named untracked files. They were preserved. No reset, clean, stash, branch switch, deletion or unrelated revert was performed.

## 2. Implementation Inventory

- Product Shell: stable product lockup, topbar, ordered navigation, cinnabar active state, service/appearance/more/account cluster and a single `ProductLayout` composition owner.
- Shell boundary: ordinary pages retain top navigation; workspace/settings shell modes consume the shared product rail instead of creating route-local shell owners.
- Auth and global states: D01 Login and D24 Forbidden were aligned to Option D; Setup and Not Found reuse the same shared geometry while retaining their distinct contracts. Change Password continues to reuse the existing Auth shell and session/leave authority.
- Admission: role, startup profile and feature-flag rejection remain in the router guard before protected capability mount; backend HTTP authorization remains final authority.
- Shared state/accessibility: one main landmark, skip link, heading hierarchy, route focus restoration, visible focus, reduced motion, short-viewport scroll recovery and long-token wrapping remain asserted.
- Token ownership: G2 shell/auth raw colors were moved into shared tokens without changing rendered values or adding route-specific theme branches.
- G2 gate: one invocation-bound reference/candidate path, frozen reference hashes, complete-capture postcondition, whole-image diff/overlay and hard Master anchor assertions.
- Regression tests: product-rail selectors now target the visible rail; workspace topbar budgets reflect the frozen G2 shell; large-canvas interaction uses the canonical world-to-canvas transform and verifies a real single-node selection before drag.

## 3. Master Measurement Gate

| Check | Result |
| --- | --- |
| Fixture | `option-d-g2-master-measurements.v2` |
| Sources | D01 Login / D24 Forbidden current Master PNGs |
| Source SHA-256 | 2/2 exact |
| Dimensions | 2/2 `3840x2160` |
| Geometry | 12/12 exact edge pixels; each edge exceeds its frozen minimum response |
| Color | 8/8 exact RGBA samples |
| Scale | output-to-CSS `2:1`; frozen 1920x1080 logical grid |
| Masks/resampling | none |
| Machine manifest | `.tmp/studio-ui-next/option-d-g2/master-measurements.json` |
| Manifest SHA-256 | `1b2f386d08b0e22641e05653289638ca03a2852cc9b235f36b1a0c009f71fc15` |

Master source identities:

- D01 `01_login.png`: `bf3adebb2451161ca76902d531f9953c38bd6c6f1484145f4b4818935b50a241`.
- D24 `24_forbidden.png`: `e6171bedda03d2c06ae5bb6c66241a8993b08f360657ff35c8245ad7eeb208ca`.

## 4. Visual Evidence

Environment: Chromium; CSS viewports `1920x1080`, `1536x864` and `1366x768`; DSF `2`; light/compact; deterministic fixture. Comparison is whole-image RGBA with per-channel threshold `8`, maximum changed-pixel ratio `1%`, and `NO_MASKS`.

| Capture | Output | Reference / candidate SHA-256 | Changed pixels / ratio / max delta | Result |
| --- | ---: | --- | ---: | --- |
| D01 Login 1920x1080 | 3840x2160 | `e91dbd8f3fe24f8eeddfd85195e52a5fe6af5660fbb02d6a943364c7960f7497` | `0 / 0 / 0` | PASS |
| D24 Forbidden 1920x1080 | 3840x2160 | `f1b68e41e2e24eed3206b66585c27d07359fa78eb7a908bf1ed1e1127e2acbf4` | `0 / 0 / 0` | PASS |
| D01 Login 1536x864 | 3072x1728 | `294ecaec97fcd9e0eb7e185491e59b30f633ca44264e7f5bc4e7241dcc4b865e` | `0 / 0 / 0` | PASS |
| D24 Forbidden 1536x864 | 3072x1728 | `175ff491ffd616e573f6d1770616eabc46747360f54fc22387ca15a3c360a311` | `0 / 0 / 0` | PASS |
| D01 Login 1366x768 | 2732x1536 | `600bdf728c3d0dd412d15d9ec2b0e2deee906e1be9af33d106ee24ce0c617a1f` | `0 / 0 / 0` | PASS |
| D24 Forbidden 1366x768 | 2732x1536 | `452a1a083457f165f780577f28bb1a91a22a1944cece0e380ba20396f0c4ee9f` | `0 / 0 / 0` | PASS |

Reference manifest SHA-256: `2b4af7babcf0ae32d759032aa3cf46e0fa4a19b94d03f1379041018da84130f7`.

Candidate manifest SHA-256: `64dbb754b12c3fa3bce7a3b3ccbc2270734d05f1dba4d972bbf31920ab4437ed`.

Both manifests are schema v2, invocation-bound, complete `6/6`, `referenceSealStatus=FROZEN`, and `maskPolicy=NO_MASKS`. All 24 PNG artifacts are present: six reference, six candidate, six diff and six overlay files. Every reference/candidate pair is byte-identical; declared PNG dimensions and all declared artifact hashes match disk.

## 5. Functional, Security And Accessibility Evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Login contract | PASS | username/password, remember-username, password visibility, validation, loading, server error, session recovery and safe `returnTo` remain on the existing auth owner/transport |
| Public auth isolation | PASS | Login/Setup do not mount ProductRuntime or product navigation; authenticated/setup-required redirects retain existing semantics |
| Route admission | PASS | role/profile/flag checks execute before protected capability mount; denied Settings/AI/Station paths expose zero capability owner |
| Backend authority | PASS | browser fixtures use existing HTTP contracts; no WebMessage execution path, second API client or client-side permission authority was introduced |
| Error distinction | PASS | unauthorized, forbidden, not-found, setup-required and route-load failure remain separate product states |
| Shell accessibility | PASS | one `main`, skip link, route focus recovery, semantic headings, focus-visible controls, reduced motion and reachable short-viewport auth controls |
| Shell geometry | PASS | compact/comfortable topbar and product rail/top-navigation boundary are asserted across theme, density and supported viewport matrices |
| Dark geometry | PASS | shared theme tokens change appearance without route-specific D19/D20 geometry rules |
| Canonical FlowCanvas | PASS | no production canvas implementation changed; the G2 regression correction only fixes test-side coordinate conversion and retains exact selection/dirty/cleanup assertions |

## 6. Owner And Cleanup Evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Shell ownership | PASS | routes remain composition surfaces under the single Product runtime/layout boundary; no route-local EventBus, registry, transport or shell owner was added |
| Admission cleanup | PASS | denied profile/role/flag routes do not mount protected capability owners or subscriptions |
| Auth cleanup | PASS | public auth routes do not mount ProductRuntime; session transitions continue through the existing Auth/Session owner |
| Workspace regression | PASS | full regression retains one workspace/FlowCanvas owner; route leave returns subscriptions, RAF and observers to zero |
| Large-flow interaction | PASS | 100/150 and 300/450 fixtures require one real selected node, one dirty draft and a zero owner/resource ledger after route leave |

## 7. Verification Matrix

| Verification | Result | Notes |
| --- | --- | --- |
| G2 reference hard gate | PASS | Master assertions plus visual `6/6`, one worker; frozen screenshot hashes and invocation-bound schema-v2 reference manifest passed |
| G2 candidate hard gate | PASS | Master assertions plus visual `6/6`, one worker; candidate manifest and all diff/overlay postconditions passed |
| independent artifact audit | PASS | 27 files audited; JSON parse, dimensions, disk hashes and all six zero-diff comparisons agree |
| focused StudioUI Vitest | PASS | 14/14 |
| StudioUI lint | PASS | `eslint . --max-warnings=0` |
| StudioUI typecheck | PASS | app/vitest/node TypeScript projects |
| StudioUI full Vitest | PASS | 145 files / 959 tests |
| production build | PASS | Vite production build |
| bundle budget gate | PASS | production bundle budgets |
| bundle reproducibility | PASS | repeated production bundle identity gate |
| UI.Tests unit | PASS | 1046/1046 |
| Agent UI contract | PASS | 390/390 |
| affected Playwright contracts | PASS | workspace shell 12/12; auth short viewport 1/1; settings rail 4/4; initial large-flow 2/2 |
| repeated large-flow regression | PASS | five repetitions of both 100/150 and 300/450: 10/10 |
| full StudioUI Next Playwright | PASS | 294 discovered: 193 passed, 101 declared evidence-only skips, 0 failed |
| independent code/Owner/security review | PASS | one P3 tautological short-viewport assertion was tightened and passed 1/1; the dark-rail P2 was disproved by D13/D16 light-theme Masters; fresh re-review returned `NO_APPLICABLE_P0_P3_FINDINGS` |
| independent visual/gate review | PASS | reference seal, invocation binding, Master assertions, whole-image comparison, no-mask policy and disk artifacts returned `NO_APPLICABLE_P0_P3_FINDINGS` |
| independent documentation/status review | PASS | metadata, hashes, counts, external boundaries and namespaced state transitions returned `NO_APPLICABLE_P0_P3_FINDINGS` |
| `git diff --check` | PASS | tracked changes and G2 untracked implementation/evidence files contain no whitespace errors; CRLF conversion notices are non-failing warnings |

The full Playwright run excludes dedicated Option D visual specs from the ordinary matrix; those six captures are run only through the invocation-bound reference/candidate hard gates above. The 101 skips are declared evidence-capture variants, not failures. Chromium evidence does not replace real WebView2 or native DPI evidence.

## 8. Provenance And External Evidence Boundaries

| Evidence class | State |
| --- | --- |
| source HEAD | `cdd114082821bbe750fb7945a0c3a4e89002d67c` |
| G1 evidence manifest | SHA-256 `c7139d2f11dfcfba24536d019d4c4f394278c7ed086b946c6a60a37cc284d079` |
| tracked diff identity | `9c1d374ab7b08556a5f0179294f1b323a8ce6b2b50fec870d11384783c0a9bf1` |
| untracked filename-list identity | `77a7fa1cf3fbd0672b912dbd770937487f36618f2e45b3b14b26675db5f7c4bd` (2421 NUL-delimited paths) |
| real WebView2 this Gate | NOT_PERFORMED |
| Windows 125% native DPI | NOT_PERFORMED |
| independent no-Node target | NOT_PERFORMED |
| Remote CI | NOT_RUN |
| field Camera/PLC/Station/AI | NOT_PERFORMED |
| production soak/signoff | NOT_PERFORMED |

Local Chromium DSF2, production build and historical host evidence do not replace these external classes. G2 does not grant production acceptance or Legacy retirement.

## 9. Final Gate Decision

```text
G2_LOCAL_IMPLEMENTATION=PASS
G2_VISUAL_GATE=PASS_6_OF_6_ZERO_DIFF_NO_MASKS
G2_OWNER_CLEANUP=PASS
G2_INDEPENDENT_REVIEW=PASS
G2_HISTORICAL_STATE=PASS_SUPERSEDED_2026_08_24
G2_STATE=REOPENED_IN_PROGRESS
G3_STATE=BLOCKED_BY_DEPENDENCY
```

## 10. Reopen Decision

HerverJun signed the Product, Security, QA/Release and affected capability-owner decision on 2026-08-24 to reopen affected G2/G3 work. Raw whole-page files under `_visual_master/option_D/screens/` are the sole visual authority. Every admitted route and real capability must remain reachable through an approved relocation or progressive-disclosure entry; CSS hiding and silent removal are prohibited.

The prior G2 `PASS` proved deterministic candidate-to-reference capture and bounded Master anchors, but did not prove raw whole-page Master parity. It is retained above as historical evidence and superseded as the current Gate decision. G3 and G4 remain `BLOCKED_BY_DEPENDENCY` until the reopened G2 implementation, functional/Owner regression, raw Master gate and independent review are complete.
