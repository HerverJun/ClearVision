# AI Flow State Audit Evidence

Baseline: `a0b172dbb87e60ae4febd530a487866bcdb01350` (2026-09-05).

These are diagnostic reproductions of known defects, not passing product regression tests. Product source was not changed. No live LLM, camera, PLC or Station was used.

## Files

- `frontend-probes.mjs`: production mixins/reducer with controlled HTTP failures and delayed responses. Assertions confirm the buggy behavior.
- `frontend-observations.json`: eight observations, including two late-create variants of one root cause.
- `BackendProbes.csproj` / `BackendProbes.cs`: reuse the existing isolated AgentRunEndpointTestHost via reflection. Real HTTP endpoints, event persistence, conversation persistence and terminal projector; controlled build/model execution.
- `backend-observations.json`: actual HTTP/state results. The overlapping-builds entry records two simultaneously running Builds and the overwritten association. The final entry is a new Plan still associated with the previous Build.
- `browser-harness.html` / `.mjs`: real AiPanel and CSS, backend-generated snapshots, isolated external I/O. Browser observations confirmed disabled new-Plan controls, degraded success restore, draft-to-strict mode change and disabled retry text. This does not simulate an entire desktop session.

The original suite outputs are under `test_results/ai-flow-audit-20260905/`: Product 1020 passed, Desktop 106 passed, frontend 491 passed, zero failures/skips.

## Reproduce

From the repository root, with the same SDK/runtime and UI dependencies as the main project:

```powershell
node quality/evidence/ai-flow-state-audit-20260905/frontend-probes.mjs

# Run only after other .NET builds/tests sharing these outputs have finished.
& './scripts/dotnet.ps1' run `
  --project 'quality/evidence/ai-flow-state-audit-20260905/BackendProbes.csproj' `
  -- "$((Get-Location).Path)/quality/evidence/ai-flow-state-audit-20260905"

node ClearVision.Product/tests/ClearVision.Product.UI.Tests/node_modules/http-server/bin/http-server `
  . -p 5018 -a 127.0.0.1 -c-1
```

Open `http://127.0.0.1:5018/quality/evidence/ai-flow-state-audit-20260905/browser-harness.html`, select a scenario and click Load Scenario. Use an unused port if needed and stop the server after inspecting it. The root server is bound to localhost only. Backend probes create and clean isolated temporary session/event storage. Probe build outputs are disposable.

The detailed Chinese report is [AI state and recovery audit](../../../docs/审计资料/报告/AI生成流程状态与恢复深度审计-2026-09-05.md).
