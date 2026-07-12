# ClearVision Quiet Precision runtime, Owner, and CSS report

## Formal runtime

- Desktop configuration keeps `WorkspaceV2Enabled` set to `false` in `ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json`.
- `WebView2Host` therefore resolves the formal startup page to the legacy Studio shell: `wwwroot/index.html` plus `wwwroot/src/app.js` and the registered view managers.
- The Vue `FrontendV2` tree is inactive for this configuration and was not modified or enabled.
- No feature-flag default was changed during the visual convergence.

## Formal page Owners

| Surface | Formal Owner | Governed implementation | Inactive or excluded implementation |
| --- | --- | --- | --- |
| Shell and navigation | `index.html` + `app.js` | Legacy desktop shell | `FrontendV2` shell |
| Project | `ProjectPageCapabilityOwner` (`project-page-capability-v2`) | Capability project list in the legacy shell | Legacy project renderer and Vue project page |
| Flow canvas | `FlowCanvas` | Existing canvas, serialization, ports, nodes, and connections | Vue canvas and inactive alternate editors |
| Operator rail and flyout | `OperatorPaletteShell` | Existing formal operator library | Inactive alternate palette implementations |
| Property inspector | `PropertyPanelCapabilityOwner` | Capability property panel | Excluded legacy/property alternatives |
| Preview workbench | `PreviewPanelCapabilityOwner` | Capability preview panel | Excluded legacy preview alternatives |
| Final decision | `FinalDecisionPanel` | Existing decision configuration UI | No parallel decision UI enabled |
| Global variables | `GlobalVariablesCapabilityOwner` | Existing manager and property bindings | No alternate manager enabled |
| Inspection | Legacy `InspectionPanel` | Legacy panel in the formal shell because the extra experimental gate is absent | `InspectionCapabilityOwner` path remains inactive |
| Results and traceability | `ResultsReviewCapabilityOwner` | Capability review list hosted by the existing production-summary shell | Legacy review-card owner and Vue results page |
| Station monitoring | `StationMonitorView` | Existing station monitor and result workbench | No parallel monitor enabled |
| Settings | Legacy `SettingsView` | Full settings navigation and tab modules | `Studio2.Settings` capability path remains fail-closed |
| AI | Legacy `AiPanel` | Existing reducer, projection, clarification, readiness, Build, and Apply lifecycle | `AiPanelCapabilityOwner` path remains fail-closed |
| Login | `login.html` + authentication storage/HTTP flow | Existing login and initial-admin setup | No alternate login page |

## CSS authority and load order

The formal load chain now starts with the only global visual authorities:

1. `variables.css` — brand, industrial status colors, light/dark surfaces, type, spacing, radii, focus, motion, and elevation tokens.
2. `main.css` — shell geometry, navigation, status bar, view containers, loading, and onboarding.
3. `ui-components.css` — shared buttons, inputs, selects, modals, badges, tables, empty/loading/error/disabled/focus states.

Page and component styles load after those authorities and are limited to their formal layout or specialized workbench needs. The current `index.html` load order is the source of truth.

## Retired visual layers

- `visual-upgrade.css` was removed after the live load chain and repository reference scan showed no formal runtime consumer.
- `global-enhancements.css` was removed. Its live toolbar, status, loading, and onboarding rules were consolidated into `main.css`; unused keyframes and tooltip rules were discarded.
- `settings-view-override.css` was merged into a single cleaned `settings.css`, removed from the load chain, and deleted.
- Results no longer define an independent light/dark color system.
- Login no longer contains the animated perspective grid, glass blur, animated border, logo motion, or decorative glow/gradient layer.

`property-panel-enhancements.css` and `sprint-c-enhancements.css` remain loaded because reference scans confirm formal consumers such as `calibrationDraftWorkbench`, `nodePreviewOverlay`, and `templateSelector`. They are retained component debt, not inactive parallel pages.

## Migration summary

- Shell: Save is secondary; Run is the single global primary action. Header and status surfaces are neutral and non-floating.
- Project: continuous document-style list, stable metadata, and always-visible Open/Delete actions.
- Flow: canvas remains visually dominant; rail, flyout, inspector, preview, and toolbars use neutral shared surfaces.
- AI: DOM and lifecycle are unchanged; surfaces, typography, buttons, and focus treatment consume global tokens.
- Global variables: continuous list, compact detail form, and one brand selection indicator.
- Inspection: image workspace is primary, controls and counters are compact, and normal diagnostics are visually quiet.
- Results: total/yield/NG/execution failure lead the metric order; trend/defect charts remain; review records are continuous rows.
- Stations: normal stations are quiet; offline, alarm, and invalid states carry the stronger colors; detail and event data stay secondary.
- Settings: stable left navigation, controlled content width, grouped settings sections, tables for complex mappings, and explicit save areas.
- Login: static, clear, and trustworthy with a single bordered authentication surface.

## Baseline evidence

- Initial screenshots: `artifacts/quiet-precision/initial`
- Final screenshots: `artifacts/quiet-precision/final`
- Reproducible evidence spec: `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/quiet-precision-evidence.spec.ts`
- Initial Playwright baseline: 190 total, 173 passed, 16 failed, 1 skipped.
- Initial UI unit baseline: 947/947 passed.
- Initial Desktop endpoint TRX: 311/311 passed. The wrapper returned exit code 1 because xUnit stderr was treated as terminating, while the completed TRX recorded zero failures.

Known initial failure families were AI visual/overflow/semantic cases, Flow preview/layout/pixel-probe/prerequisite cases, Settings/PLC save cases, and one Station result-filter timeout. Final validation must compare against these families rather than treating them as new regressions or updating screenshots blindly.

## Preserved engineering boundaries

This change does not modify backend APIs, DTOs, databases, project/inspection/results/station data semantics, runtime or station behavior, final-decision semantics, FlowCanvas persistence contracts, feature-flag defaults, or the AI reducer/projection/clarification/readiness/Apply safety lifecycle.

## Final validation

- Full Playwright: 193 total, 181 passed, 11 failed, 1 skipped. The remaining failures are two existing AI semantic/timing cases, five Flow mock/pixel-probe/camera-prerequisite cases, three Settings/PLC persistence cases, and one Station result-filter timeout. The six editor regressions introduced during the Flow CSS rewrite were fixed before this run; all nine `editor.spec.ts` cases and both reviewed Build responsive snapshots pass.
- Reviewed AI and formal-page visual rerun after the final CSS audit: 65/65 passed, including 1024 and 390 widths, reduced motion, 150% scale, all reviewed AI snapshots, and the final evidence capture.
- Final evidence capture: 3/3 passed. Initial and final screenshots are stored under `artifacts/quiet-precision/initial` and `artifacts/quiet-precision/final`.
- UI unit suite: 947/947 passed.
- Agent UI contract suite: 394/394 passed.
- Desktop endpoint suite: 311 executed, 309 passed, 2 failed. Both failures reproduced together with `--no-build --no-restore`: the Agent Plan scenario-specific recommendation contract and the RuntimePackage asset-revision error-code expectation. They are new observations relative to the recorded 311/311 initial TRX, but they are outside this frontend-only diff and no backend, DTO, database, runtime, decision, or Agent state source changed.
- Real WinForms + WebView2/CDP evidence is under `quality/evidence/ai-webview2-release`: the formal `desktop-webview2` host and `AiPanel` Owner passed light/dark zero-overflow checks at 100%, 125%, and 150% scale, IME composition, modal focus/inert/Escape behavior, Apply/Undo/rollback recovery, `WM_CLOSE` flush, and process reopen restoration. Native `OpenFileDialog` selection and a vendor-specific Windows IME candidate window remain explicit manual risks because direct screen control was prohibited.
- The WebView2 smoke runner health probe was corrected from the nonexistent `/health` path to the formal `/api/health` endpoint; this is validation-harness-only and does not change product runtime behavior.

## Remaining visual debt

- The formal shared component authority no longer uses `transition: all`, and unused compatibility keyframes plus the continuous AI stepper pulse were removed.
- The retained legacy AI stylesheet and confirmed live component layers (`operator-library.css`, `property-panel-enhancements.css`, `sprint-c-enhancements.css`, and `analysisCards.css`) still contain localized gradients, shadows, `transition: all`, and compatibility `!important` rules. They were not broadly rewritten because their formal consumers and engineering contracts are still active; future cleanup should be component-scoped with dedicated visual regression coverage.
