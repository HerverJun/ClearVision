# AI Flow State Repair Verification

The original audit fixtures in `../ai-flow-state-audit-20260905/` remain unchanged.
They assert defects in the baseline, not acceptance of this repair.

## Regression Tests

- `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/unit/ai-flow-recovery-regression.test.mjs`: 26 behavior tests for new Plan ownership, transient save failures, lifecycle restoration, live replay completion, cancellation, delayed responses, draft mode and production renderer recovery actions.
- `AgentRunEndpointsTests`: adds three terminal-to-new-Plan persistence cases and a controlled concurrent Build test. The latter continues through disk restore, another Plan, saved answers and another Build.
- Existing Product and UI tests cover the surrounding gates, application behavior, entry parity and session handling.

Recorded outputs under `test_results/ai-flow-repair-20260905/`:

| Output | Result |
| --- | --- |
| `product-regression.trx` | 202 passed, no failures or skips |
| `desktop.trx` | 110 passed, no failures or skips |
| `frontend.tap` | 504 passed, no failures or skips |

Run .NET projects serially using the repository wrapper:

```powershell
& './scripts/run-dotnet-test-serial.ps1' `
  -Project 'ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/ClearVision.Product.Desktop.Tests.csproj' `
  -FullyQualifiedName AgentRunEndpointsTests,AiModelEndpointsTests

& './scripts/run-dotnet-test-serial.ps1' `
  -Project 'ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj' `
  -FullyQualifiedName ConversationalFlowServiceTests,BuildFromPlanEntryParityTests,VisionAgentBuildOrchestratorTests,VisionAgentPlanReadinessEvaluatorTests,VisionAgentPlanPlannerTests,VisionAgentPlanFidelityValidatorTests

node --test ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/unit/ai-flow-recovery-regression.test.mjs
```

Use `-NoBuild -NoRestore` for subsequent unchanged .NET runs after that project has built.

## Browser Verification

The harness loads the production `AiPanel` and styles. Successful snapshot fixtures
come from the original isolated HTTP audit. For the writable Plan scenarios, the
fixture's old Build association is explicitly cleared to represent the newly tested
backend transition. Outbound model, history and readiness I/O is substituted.
The test readiness response permits Build to verify the retry-to-ready UI transition.
This is not an end-to-end model or hardware test.

Verified with the built-in browser on 2026-09-05:

- Readiness timeout: Build disabled; `重试校验` enabled. Clicking retry issued one
  request for the same Plan and changed readiness to ready and Build to enabled.
- Restored draft: the actual preview request and effective panel mode both remained draft.
- Successful Build snapshot: revision remained 2, with Build authority retained rather
  than a degraded revision 0. Complete result preservation is checked separately by regression tests.
- The new recovery button was visually inspected in the production action area.

To open the harness, serve the repo root on an unused loopback port:

```powershell
node ClearVision.Product/tests/ClearVision.Product.UI.Tests/node_modules/http-server/bin/http-server . -p 5023 -a 127.0.0.1 -c-1
```

Open `/quality/evidence/ai-flow-state-repair-20260905/browser-harness.html` on that server.
The verification server was stopped after inspection. No production session store,
model credentials, cameras, PLCs or Station resources were accessed.
