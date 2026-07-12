# ClearVision AI release validation round 2

Validation date: 2026-07-12

The evidence in this directory was produced by `scripts/run-ai-webview2-release-smoke.ps1`. The runner builds and starts the real WinForms Desktop executable, enables a private WebView2 CDP port, authenticates against the embedded Desktop API, drives the existing `AiPanel`, requests WinForms shutdown through `WM_CLOSE`, and reopens the process.

## Fault injection matrix

| Chain | Injection | Expected invariant | Evidence |
| --- | --- | --- | --- |
| AgentRun stream | Fetch stream returns 503, replay returns duplicate sequence 1 and one terminal sequence 2 | Events are unique and terminal appears once | `webview2-full-dpi-1.json` → `transport.replay` |
| AgentRun replacement | Start a non-terminal transport, immediately start another Run, then close | Both old transports close, no active owner or pending replay delay remains | `transport.replacement` |
| Session switching | Reverse workspace-flush completion, mismatched SessionId, send exception and stale navigation identity | Only the last selection can load; pending timeout ownership always finishes | `ai-agent-ui-contract.test.mjs` session fault tests |
| Snapshot restore | Missing/future version, invalid lifecycle, damaged Plan, Applied + Ready authority | Dangerous Build/Readiness/Apply authority is removed | `ai-panel-resilience.test.mjs` snapshot cases |
| Apply partial write | First real canvas deserialize writes one node and throws | Pre-Apply snapshot restores the exact canvas | `canvas.rollbackRestored` |
| Apply success | Apply a two-node/one-connection result through the real FlowCanvas adapter | Complete shape is applied and Undo restores the baseline | `canvas.applied*`, `canvas.undo*` |
| Apply rollback failure | Apply writes one real node and throws; automatic rollback also throws; the partial canvas is left untouched until process close | Workbench becomes Failed, Apply is disabled, session/result-scoped safety marker is persisted, lifecycle is not Applied | `rollbackFailurePersistence` |
| Process restart | Close immediately through WinForms `WM_CLOSE`, then reopen and restore the same session/result through `_handleGetAiSessionResult()` | Flush handshake completes, the safe old canvas returns, the Marker is restored, Apply stays disabled and lifecycle remains Build/Failed | `webview2-reopen-dpi-1.json` |
| Preview drift | Gate/Result/canvas identity changes while Preview is open | Old Preview cannot confirm | `ai-build-workspace.spec.ts` and resilience unit tests |

## State consistency matrix

The data-driven `Build release state matrix` test derives the existing Build presentation without introducing a second state machine.

| Canonical state | Expected presentation / action |
| --- | --- |
| Idle / Router | No Build Apply authority; existing Router/Plan contract tests |
| Plan running / clarification / ready | Existing canonical Plan projection and readiness tests |
| Build generating | `building`, Apply unavailable |
| Validating / DryRun running | `validating`, Apply unavailable |
| Pending parameters | `needs_input`, blocking action present |
| Missing resources | `needs_input`, blocking action present |
| Static validation failure | `validation_failed`, Apply unavailable |
| DryRun failure | `validation_failed`, Apply unavailable |
| ApplyGate blocked | `gate_blocked`, Apply unavailable |
| Apply Ready | `ready_to_apply`, no blocking action, Apply enabled |
| Applying | `applying`, duplicate execution unavailable |
| Failed | `failed`, Apply unavailable |
| Applied | `applied`, Apply unavailable |
| History / damaged recovery | Safe Plan or Build downgrade; no recovered authority |
| Cancel / new Run replacement | Old transport and late events cannot remain active |

The real WebView2 ready-state sample also records the same state in the workbench, Build presentation, Action Queue, Apply button and persisted lifecycle projection under `stateConsistency`.

## Real Host and lifecycle results

- Formal resource loaded: `/src/features/ai/aiPanel.js`; runtime instance: `AiPanel`; host: `desktop-webview2`.
- A fresh `GetAiSession` WebMessage probe recorded its generated `requestId`, `sessionId` and `navigationEpoch`; the real Host response returned the same identity and completed `pendingSessionLoad`.
- CDP `Input.imeSetComposition` preserved Chinese composing text and focus across an AiPanel rerender.
- Dialog role, modal semantics, background `inert`, Escape and focus return passed.
- Twelve Flow/AI view switches retained the same panel and ten Preview cycles left zero overlays.
- WebMessage subscriptions stayed `10 → 10`; owned Timer and RAF counts stayed `0 → 0`.
- DPI scale factors 1.0, 1.25 and 1.5 were applied to the real WebView2 process. Light/dark layouts have zero document, body and AI overflow after the 150% toolbar fix.
- WinForms close completed the real `host_close` workspace-flush handshake before exit.
- The rollback failure stage retained a one-node partial canvas and did not repair it in test code before `WM_CLOSE`. After restart, the canvas was the safe empty baseline rather than the temporary partial write; Workspace lifecycle was `build`, not `applied`.
- The reopened WebView2 then exercised the complete production session handler with the same session/result fixture. `_applySafetyBlockReason` returned as `apply_rollback_failed`, Apply stayed disabled, and the UI exposed the explicit safety-recovery warning. The matching and mismatch cases are also covered by the Agent UI contract test.
- When no release credential is supplied, the runner uses an isolated SQLite database under `.tmp` and creates a one-time test administrator through the formal setup endpoint. It does not read or modify the user's authentication database.

## Evidence files

- `webview2-full-dpi-1.json`: full fault, IME, Dialog, Apply and lifecycle run.
- `webview2-reopen-dpi-1.json`: close-flush and process-reopen result.
- `webview2-layout-dpi-1.25.json`, `webview2-layout-dpi-1.5.json`: real WebView2 DPI layout measurements.
- `dpi-*.png`: light/dark screenshots captured from the WebView2 target.

## Explicit remaining manual risk

The native WinForms `OpenFileDialog` was not operated because the task prohibits computer-use and direct screen control. Automated coverage proves the AI `PickFileCommand`/`FilePickedEvent` contract and the real Host WebMessage bridge, but choosing a file inside the operating-system dialog still requires one manual release check.

`Input.imeSetComposition` exercises Chromium/WebView2's native composition protocol, but it does not select a specific installed third-party Windows IME. A vendor-specific IME candidate-window check remains manual.

## Whole-product Playwright status

The complete 185-test Chromium run was executed as an additional product-wide signal. It reached 167 passed, 1 skipped and 17 failed before returning a failing gate. The AI-focused set was then rerun independently and passed 72/72 with one existing skipped test.

The 17 product-wide failures are outside the changed AI files and include unmocked HTTP-server API calls returning 404/405, unrelated Settings/PLC payload expectations, preview timing, ROI timing and a canceled download. They were not changed because this task explicitly prohibits opportunistic remediation of other pages. Consequently the AI release evidence is green, while the repository-wide Playwright release gate remains red and must not be reported as fully passing.
