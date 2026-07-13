# AI Plan → Build Readiness P1 Evidence

Initial task HEAD: `4c56b6705074399c25b29c0743482408221e6367`

## Before

- `before/real-interface-workspace-projection.json`: real Plan/Readiness response, Workspace snapshot, and projection showing one camera requirement entering as three unrelated display records.
- `before/plan-resource-duplicates-*-1920x1080.png`: real WinForms + WebView2/CDP reproduction in light and dark themes.
- `before/webview2-release-baseline/`: general release-smoke baseline retained for boundary comparison.

The three visible records came from Readiness (`camera_binding`), Build Result (`op_acq.CameraBindingId`), and persisted Workspace (`image_source`). They represented the same station-camera binding but had no shared identity and used the same generic copy.

## After

- `after/real-interface-workspace-projection.json`: real interface responses and Workspace state for Strict blocked, Draft-authorized, and Strict-after-binding ready states.
- `after/strict-resource-blocked-*-1920x1080.png`: one canonical, identifiable camera task with operator, parameter, status, scope, sources, destination, and real binding action.
- `after/draft-authorized-deploy-blocked-*-1920x1080.png`: backend-authorized editable Draft while deploy/run remains blocked.
- `after/strict-bound-ready-*-1920x1080.png`: real camera binding decision followed by authoritative Strict Readiness release.

Reproduce the final scenario from the repository root:

```powershell
& "./scripts/run-ai-plan-build-readiness-p1-webview2.ps1"
```

The runner uses an isolated database, the real rule-fallback Plan path for deterministic evidence, real HTTP endpoints, the existing camera-binding API, and the actual WinForms WebView2 surface over CDP. It does not use computer-use or a parallel frontend admission decision.
