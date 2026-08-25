# Option D G0 Evidence Manifest

```text
EVIDENCE_ID=option-d-g0-cdd1140-g0-audit-01
GATE=G0
STATE=PASS
RUN_ID=g0-close-20260823
AUDIT_DATE=2026-08-23
TIMEZONE=Asia/Shanghai
SOURCE_HEAD=cdd114082821bbe750fb7945a0c3a4e89002d67c
OWNER_APPROVAL=APPROVED_HERVERJUN_2026_08_23
G1_STARTED=false
```

## 1. Authority And Scope

| Item | Result | Evidence |
| --- | --- | --- |
| Visual authority | PASS | `_visual_master/option_D/screens/` only; no image regenerated, resampled, cropped, overwritten, or replaced |
| Functional/architecture authority | PASS | current code + `F10_ContractAndProductionPlan.md`; screenshots did not add/rename/remove/reinterpret capability |
| G0 implementation scope | PASS | ledger/ADR/plan/evidence plus one approved deterministic E2E fixture and its test; no StudioUI page, C#, backend contract, canonical Canvas or `_visual_master` file changed |
| G1 authorization | READY | G0 exit evidence is complete; no Design System/page implementation started, and `READY` is not evidence that G1 has started |
| Pixel viewport contract | FROZEN | `1920x1080` CSS viewport, 2x output (`3840x2160`), deterministic fixture and dual-track comparison required for later gates |

## 2. 24/24 PNG Freeze

All files were independently enumerated and decoded as `3840x2160`. Computed SHA-256 matched the audited plan for 24/24 files.

| ID | File | SHA-256 | Dimensions | Result |
| --- | --- | --- | --- | --- |
| D01 | `01_login.png` | `bf3adebb2451161ca76902d531f9953c38bd6c6f1484145f4b4818935b50a241` | 3840x2160 | PASS |
| D02 | `02_overview.png` | `a6a902196b5486817f80c094d469fa4d96e8c934fb2a36c5e7947fc3d5f24769` | 3840x2160 | PASS |
| D03 | `03_projects_data.png` | `fe6d5e6c368573de83d0a6a0ed46148a2f0e6f01d9e002565c63c6d4047c5e94` | 3840x2160 | PASS |
| D04 | `04_projects_empty.png` | `a0117dcd2b62a5cef6c499f4e0f658a4c3de255ae3da783640f6d6952f030087` | 3840x2160 | PASS |
| D05 | `05_flow_editor.png` | `247efff95e87fdd626f36dfae2dced6d94465d0c697408dca62e69d6ccacedc3` | 3840x2160 | PASS |
| D06 | `06_flow_validation_error.png` | `779422ebda60af052314108ccad147de36b621eecf4924dc9046e40e6c3c0d16` | 3840x2160 | PASS |
| D07 | `07_flow_preview_roi.png` | `51e856548b1b4cc67d2737f287ca9a0de8056a6e8c5fc77a3a5f785028dd5957` | 3840x2160 | PASS |
| D08 | `08_run_ng_modal.png` | `8793b0983eda3caa25a652f28c900ab1d04a190fc9a2c39ef80bce9a199efa8b` | 3840x2160 | PASS |
| D09 | `09_results_investigation.png` | `8d716e0ab1fdffaef82075975c34219565fc8b79b973a7b44c80150252f0201a` | 3840x2160 | PASS |
| D10 | `10_stations_list.png` | `939080aed9eaa4102702e5da3ccbe2e9f1b9f18cd8ca6184cfc6c6c228b374eb` | 3840x2160 | PASS |
| D11 | `11_station_detail.png` | `2ebb9f191210fa8ac76bb37270e630ea6f715e1ac6501b3817196b9fe097829a` | 3840x2160 | PASS |
| D12 | `12_inspection.png` | `280c7fbaf8561bd4e8bd662fbdc1eb4852180161bcb1537311a22a809eb6cc60` | 3840x2160 | PASS |
| D13 | `13_ai_workspace.png` | `0e2875749de6fc6d1971517a530f6a9daae4f935456f035482dc85bf6cf91b1d` | 3840x2160 | PASS |
| D14 | `14_ai_failure_recovery.png` | `cf4540d7e4a25c8d928462dee9186e5b4b6d569db783d0e41a20ccf1ecaef6d3` | 3840x2160 | PASS |
| D15 | `15_operator_catalog.png` | `a01ee6cfbcd1344c2340ce18ced5eb2cfce66450dd0509f5e87ccedead3a2d1f` | 3840x2160 | PASS |
| D16 | `16_system_settings.png` | `525b960075f34db309f2a4871afef54fb100474f2d6479f2447262a2ad98a35e` | 3840x2160 | PASS |
| D17 | `17_camera_settings.png` | `0768b51c32225de4804d1e2d67e65b13a0cc4c53b13a6329afc61daa245dbdf0` | 3840x2160 | PASS |
| D18 | `18_plc_settings.png` | `8f1bac13706adb645d5d03191917e1cbc47cc110ce3a6ab7c9e5600914d746c9` | 3840x2160 | PASS |
| D19 | `19_tcp_settings.png` | `d08f03523572cf2f976d5f90081903cfd46f264ea0263c213bdf095288b1df80` | 3840x2160 | PASS |
| D20 | `20_station_communication.png` | `e1a660e9e64ff17184cda8ff7341fcfaa2660934164bbf93f73bf67ad0e65cd1` | 3840x2160 | PASS |
| D21 | `21_ai_model_settings.png` | `a3fbdefa534c897daa40630308e8d2cc5672decdb4ca16fe3af1b648c744b993` | 3840x2160 | PASS |
| D22 | `22_diagnostics.png` | `0415729663e19fb6b2527956eec74d9f64284cefa8b1ba7be7f8c4d5c9e6ee97` | 3840x2160 | PASS |
| D23 | `23_about.png` | `4b085e25511b6fcffc72d6d6af33c39574baa9bfb7106729485b4035a77c8e84` | 3840x2160 | PASS |
| D24 | `24_forbidden.png` | `e6171bedda03d2c06ae5bb6c66241a8993b08f360657ff35c8245ad7eeb208ca` | 3840x2160 | PASS |

Sorted `filename + SHA-256` screen manifest SHA-256: `f65ddb0fbc6f6075bea90ec326c8a7a8220f331e9846744fdf61648c3aacab95`.

## 3. Master Chain And Constitution

Master chain: `D_FLOW_MASTER -> D_AI_MASTER -> D_SETTINGS_MASTER -> D_FULL_SET`.

| Master | Current file / SHA-256 | `selected_for_master_chain_at` | Metadata approval scope | Product/UX production signoff |
| --- | --- | --- | --- | --- |
| D Flow / D05 | `masters/05_flow_editor.png` / `247efff95e87fdd626f36dfae2dced6d94465d0c697408dca62e69d6ccacedc3` | `2026-08-22T04:49:47.702891Z` | `selected-for-chain-not-product-owner-approved` | NOT_PERFORMED |
| D AI / D13 | `masters/13_ai_workspace.png` / `0e2875749de6fc6d1971517a530f6a9daae4f935456f035482dc85bf6cf91b1d` | `2026-08-20T22:20:57.852810Z` | `selected-for-chain-not-product-owner-approved` | NOT_PERFORMED |
| D Settings / D16 | `masters/16_system_settings.png` / `525b960075f34db309f2a4871afef54fb100474f2d6479f2447262a2ad98a35e` | `2026-08-20T23:08:57.078438Z` | `selected-for-chain-not-product-owner-approved` | NOT_PERFORMED |

These three states are deliberately separate:

1. `selected-for-chain`: historical generation workflow metadata; it is not Product Owner approval.
2. `USER_VISUAL_DIRECTION_ACCEPTED=YES`: `HerverJun` explicitly selected Option D as the sole visual direction and, on 2026-08-23, stated authority to represent Product and the other G0 approval roles.
3. `PRODUCT_OWNER_PRODUCTION_SIGNOFF=NOT_PERFORMED`: no named Product Owner has granted production acceptance.

| Artifact | SHA-256 | Result |
| --- | --- | --- |
| `_visual_master/option_D/visual_constitution.md` | `6a007027ac722acabd23407109e4d2fd7411165256f9efc9c09bc84289731b31` | PASS |
| `_visual_master/image_prompts.json` | `45bca52f4401d85794936e1f4d30e3ce03959f39267a8a8c119dd55bc5b660db` | PASS |
| `_visual_master/functional_remapping.json` | `5c5aa759bfe7763f05c6163fc224822f74d5beee066540cacec4f077cc5f300b` | PASS |
| Option D contact sheet | `5ae78e478a9866f41a4e8d6f2b66c2b61ec00b01799d16a5f6d1fa218467972f` | PASS |

## 4. Canonical FlowCanvas Restore Audit

Source: `_visual_master/audit/d_canonical_flowcanvas_node_restore_2026-08-22.json`.

| Check | Result |
| --- | --- |
| D05/D06/D07 changes confined to approved bounded node regions | PASS |
| D05/D06/D07 out-of-bounds changed pixels | `0` |
| Remaining 21 Option D screens byte-identical during canonical restore | PASS |
| UI implementation authorization to redesign nodes/ports/edges/selection/state | DENIED |

The audit proves the visual source was repaired; it does not authorize a new Canvas kernel or a screenshot-derived node model.

## 5. Functional Mapping Evidence

| Evidence | Result |
| --- | --- |
| `image_prompts.json` D01-D24 `functional_audit.status` | 24/24 `passed` |
| `page_exists` | 24/24 `true` |
| regions imported | 24/24 |
| controls imported | 24/24; D23 correctly has an empty control array |
| tabs imported | 24/24; 21 pages correctly have an empty tab array |
| navigation imported | 24/24; D01/D24 correctly have empty navigation arrays |
| forbidden additions imported | 24/24, all non-empty |
| `functional_remapping.json` cross-check | 24/24 screen/current-function/target-location/must-not fields consistent |
| current named routes | 24/24 mapped |
| anonymous boundary/layout/redirect records | mapped |
| Option D-unpictured routes | setup/change-password/not-found/project detail/operator detail/inspection selector/Labs mapped |
| capability/owner/write/fallback ledger | `OptionD_G0_CapabilityLedger.md`, C01-C75 |
| exit predicate counters | all zero; see ledger section 1 |

## 6. Deterministic Fixture Coverage

Frozen fixture: `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/option-d-g0-deterministic-fixture.ts`.
Fixture identity: `option-d-g0-deterministic.v1`; approval identity: `HerverJun / 2026-08-23`.

| Required evidence | Frozen seed / exercised path | G0 disposition |
| --- | --- | --- |
| ordinary nodes/connections | fixed source, ROI and judge nodes plus two canonical connections | COVERED |
| Preview node/artifact | fixed ROI target; response factory preserves debug identity and temporary artifact authority | COVERED |
| ROI | fixed rectangle parameters and actual ImageCanvas/ROI owner projection | COVERED |
| global variable/binding | one fixed `Double` variable, one source binding and one target binding | COVERED |
| formal decision | fixed judge output, comparator and threshold | COVERED |
| Formal Run | admission and execute factories share Project, snapshot, PersistenceRevision and flow/decision hashes | COVERED |
| formal result/evidence | Results summary/detail/manifest share fixed result/run/session identities | COVERED |
| subgraph | explicitly excluded by signed G0-01 disposition | NOT_APPLICABLE |

Authority separation is asserted as Preview=`DEBUG_PROJECTION`, Formal Run=`AUTHENTICATED_HTTP`, formal result=`RESULTS_READ`,
project save=`PROJECT_SAVE_COORDINATOR`. Formal evidence remains a Results response seed and is not embedded in the Project payload.

Actual verification:

- focused G0 fixture Playwright: `1 passed / 0 failed` in 5.8s;
- affected serial Playwright regression: `6 passed / 0 failed` in 27.3s, including the G0 fixture, Formal Run handoff,
  1920x1080 and 1366x768 golden journeys, login-through-Results journey, and 20-cycle Formal Run/project/route cleanup;
- final G0 route leave asserted Workspace, FlowCanvas, Inspector, Preview, ImageCanvas, ROI, Persistence and Run owner counts,
  active subscriptions, timers, requests, controllers and owner conflicts all equal `0`.

`ODD-G0-06=PASS`.

## 7. Owner Approval Packet

ADR authority: `ADR-ParityAlignment-Wave0-ContractFreeze.md`; ADR state is `APPROVED`.

| Approval | Required owners | Decision | Evidence |
| --- | --- | --- | --- |
| G0-01 Canvas run-to-node/active-node/subgraph | FlowCanvas; Preview/Run; Product | APPROVED | `HerverJun / 2026-08-23`; run-to-node/active-node DEFERRED, subgraph NOT_APPLICABLE |
| G0-02 Inspector recommendation | Inspector; ParameterRecommender backend; Product | DEFERRED | `HerverJun / 2026-08-23`; retain current editing/validation only |
| G0-03 Station high-risk confirmation | Station command; Security; Product | APPROVED_RETAIN_CURRENT | `HerverJun / 2026-08-23`; no new command/modal/entry |
| G0-04 product disposition | capability owner; Product; Security where token/cleanup applies | APPROVED | `HerverJun / 2026-08-23`; six signed dispositions in ADR/ledger |
| G0-05 fixture/evidence | QA/Release; affected capability owners | PASS | `HerverJun / 2026-08-23`; frozen fixture plus Playwright/cleanup evidence |

Known contract dispositions are recorded instead of being misclassified as mapping gaps:

- run-to-node / active node: `DEFERRED`; subgraph: `NOT_APPLICABLE`
- Inspector recommendation: `DEFERRED`
- Station high-risk confirmation: `APPROVED_RETAIN_CURRENT`
- Storage cleanup: `RETIRE_WITH_APPROVAL`; no destructive entry this round
- Station token: `RETAIN_CURRENT_REGENERATE_ONLY`; preserve/replace and plaintext reveal are excluded
- Demo, local image and Runtime Preview Pilot: `RETAIN_LEGACY_FALLBACK`; Pilot remains default-off/internal-only
- persistent project/version/FPS status: `RETAIN_LEGACY_FALLBACK`, awaiting DPI budget

## 8. Worktree Baseline

| Item | Value |
| --- | --- |
| branch | `studio-ui-next...origin/studio-ui-next` |
| HEAD | `cdd114082821bbe750fb7945a0c3a4e89002d67c` |
| tracked diff before G0 edits | empty |
| tracked diff SHA-256 before G0 edits | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| untracked filename-list SHA-256 before G0 edits | `3e426d6476dc019ef206fab5494c87ff6ab316c9142d3fca1c19717c0cc1045a` |
| user dirty baseline | `_visual_master/`, Option D plan, and anomalously named root items were already untracked |
| destructive worktree actions | NOT_PERFORMED |
| final machine-readable evidence | `.tmp/studio-ui-next/option-d/g0/cdd114082821bbe750fb7945a0c3a4e89002d67c/g0-close-20260823/manifest.json`; exact eight-file G0 whitelist identity and verifier SHA frozen |

No existing untracked item was deleted, renamed, reset, cleaned, stashed, or overwritten. G0 edits are limited to the approved
ADR/plan/F10/TODO/ledger/evidence set, the deterministic fixture and its E2E test, plus the ignored `.tmp` evidence manifest.

## 9. Verification Matrix

| Verification | State | Notes |
| --- | --- | --- |
| PNG existence/dimensions/SHA | PASS | 24/24, see section 2 |
| Master chain/current source SHA | PASS | 3/3 current masters match screen files |
| canonical FlowCanvas restore audit | PASS | zero out-of-bounds pixels; other 21 byte-identical |
| route/capability/owner audit | PASS | ledger C01-C75 + 24 named/anonymous routes |
| StudioUI Vitest | PASS | one serial `npm run test:unit`: 144 files / 946 tests, 0 failed; duration 44.50s |
| focused fixture Playwright | PASS | `CV_UI_SCENARIO=studio-ui-next`, `--workers=1`; 1/1 passed in 5.8s |
| affected regression | PASS | one serial worker; 6/6 passed in 27.3s: Formal Run handoff, G0 fixture, 1920/1366 golden journeys, full Results journey, 20-cycle cleanup |
| visual reference capture | PASS | frozen Option D PNG set only |
| visual candidate capture | NOT_APPLICABLE | G0 did not change page/UI rendering; generating an unchanged or static-image candidate would not be valid product evidence |
| visual diff/overlay | NOT_APPLICABLE | no UI candidate exists in G0; source/hash/Master/canonical-restore integrity is the applicable visual track and no mask was used |
| real WebView2 | NOT_PERFORMED | separate evidence class |
| Windows 125% | NOT_PERFORMED | separate evidence class |
| independent no-Node target | NOT_PERFORMED | separate evidence class |
| Remote CI | BLOCKED_BY_ENVIRONMENT | no current authenticated remote run |
| field Camera/PLC/Station/AI | NOT_PERFORMED | no field hardware/environment |
| latest web-design-guidelines fetched | PASS | 2026-08-23 rules fetched read-only; no product UI implementation file changed in G0 |
| independent fixture/contract review | PASS | `g0_fixture_review_short`; no P0-P3 finding; authority separation, fixture scope and route-leave cleanup verified |
| independent documentation review | PASS | `g0_status_review_short`; no P0-P3 finding; plan/F10/TODO/evidence status and external-boundary honesty verified |
| independent visual/hash review | PASS | `g0_visual_review_short`; no P0-P3 finding; 24/24 PNG, 3/3 Master and canonical restore verified without image modification |
| supplemental disposition/contract review | PASS | `g0_disposition_review_short`; no P0-P3 finding; G0-01..05 approvals and deferred/retained/retired dispositions verified |
| `git diff --check` | PASS | final tracked diff check after G0 document edits |
| untracked G0 no-index whitespace | PASS | plan, ledger, evidence, deterministic fixture and machine verifier/manifest checked individually |
| targeted G0 status | PASS | approved G0 whitelist only; unrelated user dirty and untracked items preserved |

## 10. Gate Decision

```text
ODD-G0-01=PASS
ODD-G0-01A=PASS_WITH_PRODUCTION_SIGNOFF_NOT_PERFORMED
ODD-G0-02=PASS
ODD-G0-03=PASS
ODD-G0-04=PASS
ODD-G0-05=PASS
ODD-G0-06=PASS
ODD-G0-07=PASS
G0_STATE=PASS
G1_STATE=READY
PRODUCTION_ACCEPTANCE=NOT_GRANTED
LEGACY_RETIREMENT=NOT_APPROVED
```

G0 is closed with the signed dispositions, deterministic fixture, local regression, owner cleanup, visual-reference integrity,
four independent read-only reviews and final hash/whitespace/status evidence. `G1_STATE=READY` only removes the G0 dependency; G1 page implementation did not start in this Goal.
