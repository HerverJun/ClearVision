# Option D G3 Evidence Manifest

```text
EVIDENCE_ID=option-d-g3-cdd1140-blocked-20260824
GATE=G3
STATE=BLOCKED_BY_DEPENDENCY
AUDIT_DATE=2026-08-24
TIMEZONE=Asia/Shanghai
SOURCE_HEAD=cdd114082821bbe750fb7945a0c3a4e89002d67c
REFERENCE_GATE_INVOCATION_ID=94c5c4ac-0c44-40d4-af75-769b5fd1afd9
CANDIDATE_GATE_INVOCATION_ID=ee3c661a-ed2a-40bc-8dee-dc746aead6e1
G2_STATE=REOPENED_IN_PROGRESS
G4_STATE=BLOCKED_BY_DEPENDENCY
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
| Visual authority | PASS | `_visual_master/option_D/screens` remains unchanged and is the only raw visual authority |
| Functional/architecture authority | PASS | current code + F10; no endpoint, API transport, HostBridge, permission, persistence, Runtime or Station authority changed |
| Gate sequencing | PASS | G2 remains historically closed; G3 touched only D02-D04/D15/D22-D23 capability surfaces, tests and G3 evidence; G4 was not started |
| Deterministic fixture | PASS | `option-d-g3-read-surfaces.v1` consumes the approved `option-d-g0-deterministic.v1` inputs with fixed time, locale, theme, density, role and feature flags |
| Canonical FlowCanvas | NOT_APPLICABLE | G3 does not touch D05-D07 or the production Canvas implementation |
| Master pixel acceptance | FAIL | all six raw Master-to-candidate comparisons exceed the frozen 1% changed-pixel limit |

The dirty worktree already contained G0-G2, user-owned changes and anomalously named untracked files. No reset, clean, stash, branch switch, deletion or unrelated revert was performed.

## 2. Implementation Inventory

- D02 Overview now follows the Option D information hierarchy while retaining `/api/projects/recent`, refresh, every query state and dispose. Recent projects are `RETAIN_RELOCATED` here; D03/D04 do not duplicate a recent-project panel.
- D03/D04 Projects share one command/search/table/footer frame. Populated and empty states replace only the row region; create/open/rename/export/delete continue through the existing Project lifecycle command owner.
- D15 Operators remains GET-only and retains URL-backed search/category/lifecycle/visibility/port/parameter filters, pagination and the existing detail route.
- D22 Diagnostics retains the shared service/session/host projections, refresh and clipboard export. Service, session and host icons now follow `info`, `warning` and `error` state semantics instead of remaining healthy green.
- D23 About continues to project real version, environment, license, support and product-composition facts without inventing update or support services.
- Overview exposes one complete `运行环境与当前会话` region, phase-specific service tones and full-value hover text. These changes are covered by direct unit and degraded-service E2E assertions.
- No second owner, request client, event bus, registry, persistence path or command authority was added.

## 3. Functional And Owner Evidence

| Verification | Result | Notes |
| --- | --- | --- |
| focused StudioUI Vitest | PASS | 7 files / 51 tests |
| affected G3 Playwright | PASS | 16 passed / 44 declared evidence-capture skips / 0 failed |
| Project lifecycle command journey | PASS | 1/1 create/reconcile/open/rename/delete/tombstone journey |
| StudioUI lint | PASS | `eslint . --max-warnings=0` |
| StudioUI typecheck | PASS | app, Vitest and Node TypeScript projects |
| StudioUI full Vitest | PASS | 145 files / 963 tests |
| StudioUI build | PASS | Vite production build |
| UI.Tests unit | PASS | 46 files / 1046 tests |
| full StudioUI Next Playwright | PASS | 294 discovered: 193 passed / 101 declared skips / 0 failed; isolated port 5188 |
| candidate functional audit | PASS | all six captures import their `functional_audit` and `forbidden_additions` checks |
| request audit | PASS | G3 visual fixture traffic is GET-only; no unhandled fail-closed request |
| owner cleanup | PASS | each capture reports the single Product query owner, zero active requests and one undisposed Project lifecycle owner with zero controllers/commands |
| runtime errors / overflow | PASS | zero runtime errors, zero global/content/page horizontal overflow, zero vertical overflow, one `main`, zero topbar overlap |
| post-review selector regression | PASS | Overview Vitest 1 file / 6 tests; degraded Diagnostics Playwright 1/1 |
| gate hardening syntax | PASS | `node --check` passed for both G3 gate scripts |
| invocation-bound hard gate rerun | EXPECTED_FAIL_MASTER_0_OF_6 | candidate capture 6/6 PASS, validated raw comparison 0/6, process exit `1` as required |

The 101 full-suite skips are declared evidence-only variants. Dedicated G3 visual captures run separately through their invocation-bound scripts.

## 4. Determinism Track

The `option-d-g3-gate.mjs` reference/candidate capture phase passed 6/6 in each phase at 1920x1080 CSS, DSF2, light/compact, `NO_MASKS`. It proves that the implementation is deterministic and that reference/candidate artifacts, functional assertions, anchors and cleanup manifests are internally consistent. The candidate hard gate now runs the raw Master audit after that phase and returns non-zero while the Master threshold fails.

| Screen | Frozen reference / candidate SHA-256 | Reference-to-candidate changed pixels | Result |
| --- | --- | ---: | --- |
| D02 | `9092fd277e68b22ba7ebd24621b964a0bae0d5145d4f037cfdcd1317dfa3828a` | 0 | PASS |
| D03 | `f23c0f7c2b2f1dd0b96ba65fd155082a1fea4bb1e3a4d79b6066971a3d678d76` | 0 | PASS |
| D04 | `36e60095f9e6709f38ce59da5222461d75f42600dfe7a7b9f2ff9ddbbb07c50a` | 0 | PASS |
| D15 | `e95611647aaa6453e3233e61752f52664833d045f6cedf1d7cf039ca197f3276` | 0 | PASS |
| D22 | `e634b795acb655dd135ebdedb7ee2bd19c777dc61752d2657f9c1961029b9371` | 0 | PASS |
| D23 | `41ea8777ff348d262bdf49c24718217d2b67b0c52c854faf36e5b2d534160dea` | 0 | PASS |

Artifact identities:

- Master measurement manifest: `2cd23273016d72b62f60a6b9e0ad12b4ce2acc52e998b4e65fd40cfda329c87b`.
- Deterministic reference manifest: `4ea8efce2ce73ef6629b04a25a1153b0c444bea1e7cbae317e0a50766e899fa4`.
- Deterministic candidate manifest: `bd14111dae7dbafa8f99766ba5d1ee778220ada2d645f7ac1b010b8172f512d3`.
- Candidate hard-gate script: `1b1d902930be45d66b87af9e132430fe8d3f9423d66f2e4d6d11a90ac123a331`.
- Raw Master comparator script: `43874b110c41d9de64ab75bbe71a16fb91e91ca4ceb98f6aa33f5ab3c3d74c5f`.

## 5. Raw Master Pixel Gate

`option-d-g3-master-compare.mjs` compares each 3840x2160 candidate directly with its raw Option D Master, without masks or resampling. It uses the plan's per-channel delta `8` and maximum changed-pixel ratio `1%`. The supplementary global luminance SSIM diagnostic is not used to excuse the already-decisive changed-pixel failures.

| Screen | Raw Master SHA-256 | Changed pixels | Changed ratio | Result |
| --- | --- | ---: | ---: | --- |
| D02 | `a6a902196b5486817f80c094d469fa4d96e8c934fb2a36c5e7947fc3d5f24769` | 706,055 | 8.5124% | FAIL |
| D03 | `fe6d5e6c368573de83d0a6a0ed46148a2f0e6f01d9e002565c63c6d4047c5e94` | 1,363,619 | 16.4402% | FAIL |
| D04 | `a0117dcd2b62a5cef6c499f4e0f658a4c3de255ae3da783640f6d6952f030087` | 1,389,554 | 16.7529% | FAIL |
| D15 | `a01ee6cfbcd1344c2340ce18ced5eb2cfce66450dd0509f5e87ccedead3a2d1f` | 1,667,020 | 20.0981% | FAIL |
| D22 | `0415729663e19fb6b2527956eec74d9f64284cefa8b1ba7be7f8c4d5c9e6ee97` | 1,191,724 | 14.3678% | FAIL |
| D23 | `4b085e25511b6fcffc72d6d6af33c39574baa9bfb7106729485b4035a77c8e84` | 3,011,417 | 36.3066% | FAIL |

Machine manifest: `.tmp/studio-ui-next/option-d-g3/master-comparison/manifest.json`, SHA-256 `5eb4e24c372dccf6ac15bfc748a640005bcc82dc51c255edec4d7d40f44dab2d`. Schema v2 binds the candidate manifest hash and invocation ID, six frozen raw Master hashes, exact 3840x2160 candidate paths and hashes, six full-page diff PNGs and six overlay PNGs. The standalone audit exits `2`; the candidate hard gate accepts that evidence exit only long enough to validate the manifest, then exits `1` after reporting all six capture-phase passes and all six Master failures. These are expected truthful gate results, not environment failures.

## 6. Independent Review

- Independent code/Owner reviews found three applicable state-semantics issues: Overview non-online tones, the incomplete current-session region, and Diagnostics icons fixed to healthy green. All three were corrected and covered by new tests. Findings about hiding recent projects on D03 and absent Operator Inspection controls were rejected against the approved G0 disposition and current role/route contract.
- A fresh independent D02 visual review returned P1: the candidate visibly differs from `02_overview.png`, and the zero-diff manifest compares candidate to an implementation-captured reference rather than to the Master. It found no black screen, overlap or clipping. This P1 is confirmed by the raw Master machine gate.
- A focused independent gate review found two P1 fail-closed gaps and one P2 anchor check gap. The comparator now freezes Master SHA-256 and 3840x2160 dimensions, binds every candidate to `candidate.json`, emits schema v2 provenance, and the hard gate validates that manifest plus exact artifacts before returning the truthful failure. Anchor deltas use absolute magnitude. Both fragile status assertions were narrowed to their named service facts; targeted regressions pass. A fresh, three-assertion final re-review confirmed all prior gate findings closed (`A=YES`, `B=YES`, `C=YES`).
- Broader independent visual agents twice exceeded the repository's 10-minute limit and were terminated. Their missing output is not reported as PASS.

## 7. Contract Blocker

The Option D plan requires a raw visual track against Master geometry, surfaces, spacing, type, color and state components, with whole-page changed-pixel ratio at or below 1%. The current G3 reference is a frozen capture of the same implementation, so its zero diff is only a determinism result.

G3 also inherits the already-frozen G2 Product Shell. That shell visibly differs from the raw D02-D23 Masters because it retains the current route/navigation contract. The plan forbids masking Shell, deleting real functions, lowering thresholds or privately inventing a fallback reference. No approved, versioned rule currently defines how a frozen G2 Shell authority is composed with each raw G3 page Master.

Closing this blocker requires explicit Product/UX and QA/Release approval for one of these contract-level choices:

1. Reopen the affected G2/G3 visual work and require raw whole-page Master parity while preserving every route/capability through an approved relocation or progressive-disclosure map.
2. Freeze a versioned multi-authority reference that uses the accepted G2 Shell for the Shell region and the raw Option D Master for the G3 capability region, with exact crop boundaries, thresholds and no structural masks.

If route placement or capability visibility changes, the relevant capability/security owners must also sign the updated disposition. The earlier G0 minimum-closure approval did not authorize either new comparison rule.

Until that decision is signed, implementation may not alter the frozen G2 Shell, create a private composite, relax the 1% limit, mask the Shell or start G4.

## 8. External Evidence Boundaries

| Evidence class | State |
| --- | --- |
| real WebView2 this Gate | NOT_PERFORMED |
| Windows 125% native DPI | NOT_PERFORMED |
| independent no-Node target | NOT_PERFORMED |
| Remote CI | NOT_RUN |
| field Camera/PLC/Station/AI | NOT_PERFORMED |
| production soak/signoff | NOT_PERFORMED |

Chromium DSF2, local builds and historical host evidence do not replace these classes.

## 9. Gate Decision

```text
G3_FUNCTIONAL_IMPLEMENTATION=PASS
G3_FUNCTIONAL_REGRESSION=PASS
G3_OWNER_CLEANUP=PASS
G3_DETERMINISM_TRACK=PASS_6_OF_6_ZERO_DIFF_NO_MASKS
G3_MASTER_PIXEL_GATE=FAIL_0_OF_6
G3_INDEPENDENT_VISUAL_REVIEW=FAIL_P1_MASTER_REFERENCE_MISMATCH
G3_STATE=BLOCKED_BY_DEPENDENCY
G4_STATE=BLOCKED_BY_DEPENDENCY
```

## 10. Signed Contract Resolution And Dependency State

HerverJun signed option 1 on 2026-08-24 on behalf of Product, Security, QA/Release and the affected capability owners: reopen affected G2/G3, use the raw whole-page Option D Master as the sole visual authority, and preserve all real capabilities through approved relocation or progressive disclosure. This clears the former `BLOCKED_BY_CONTRACT` classification.

G3 is now `BLOCKED_BY_DEPENDENCY`, not ready for implementation: the reopened G2 Shell/Auth Gate must first pass its raw whole-page Master, functional, admission, accessibility, Owner cleanup and independent-review checks. G4 remains blocked and has no start authorization. The six raw G3 failures above remain valid baseline evidence until G2 is reclosed and G3 is explicitly resumed.
