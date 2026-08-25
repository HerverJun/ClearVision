# ClearVision UI Visual Master

This directory contains auditable visual-design references. It does not contain or authorize production frontend implementation.

## Repository Preservation Boundary

The repository keeps the text contracts and the minimum Option D screening assets needed to resume work after a machine migration. Checked-in raw PNGs are historical screening inputs, not current whole-page pixel authority. The current Shell decision and Gate state remain defined by `docs/进行中/StudioUINext/F10_ContractAndProductionPlan.md`.

Generated iterations, alternate-option bitmaps, comparison crops, audit screenshots, temporary reference boards, logs, and Python cache files are not source assets and do not need to be retained in Git. Regenerating any of them must create new versioned evidence; it must not overwrite the preserved inputs or silently promote them to product authority.

## Workflow

```powershell
$python = 'C:\Users\HerverJun\Desktop\ppt\.venv\Scripts\python.exe'
& $python .\_visual_master\workflow\visual_options.py validate
& $python .\_visual_master\workflow\visual_options.py contract-probes
& $python .\_visual_master\workflow\visual_options.py readiness
& $python .\_visual_master\workflow\visual_options.py research-sheet
$env:CLEARVISION_VISUAL_UPLOAD_APPROVED_HOST = '<explicitly-approved-host>'
$env:CLEARVISION_VISUAL_UPLOAD_APPROVED_SCOPE = 'models-and-clearvision-composite-reference-boards'
& $python .\_visual_master\workflow\visual_options.py preflight
& $python .\_visual_master\workflow\visual_options.py generate --ids D_05_flow_editor --concurrency 1
& $python .\_visual_master\workflow\visual_options.py review --ids D_05_flow_editor --decision pass --note '<audited findings>'
& $python .\_visual_master\workflow\visual_options.py promote --ids D_05_flow_editor
```

Repeat the same inspected sequence for `E_05_flow_editor`, then each option's AI and Settings Masters. Generate local screens only after their same-option `master_references` exist. A candidate must pass all applicable Reject Gates before promotion.

A single generation batch may contain independent siblings, but it must never contain an entry together with any direct or transitive Master dependency. The workflow rejects that combination before preflight so a forced regeneration cannot send a downstream page against an older Master.

```powershell
& $python .\_visual_master\workflow\visual_options.py generate --option D --role local
& $python .\_visual_master\workflow\visual_options.py generate --option E --role local
& $python .\_visual_master\workflow\visual_options.py validate --require-masters --require-outputs --require-reviews
& $python .\_visual_master\workflow\visual_options.py audit
& $python .\_visual_master\workflow\build_cde_comparison.py
```

The frozen active D/E scope is `24` real screens/states per option (`48` entries total). Archived A/B/C manifests are not inputs to D/E generation. The product-owner comparison at `audit/comparison_CDE/` separately retains audited Option C beside the complete D/E sets, with identical 24-screen filenames and explicit evidence boundaries.

Both active options request exact `gpt-image-2` `4K / 3840x2160 / 16:9 / high` and deliver `3840x2160`, while preserving a `1920x1080` logical workstation composition. When the reference-image endpoint returns a smaller near-16:9 source, the workflow records the provider dimensions and hash before audited deterministic Lanczos normalization; such delivery is not reported as model-native 4K.

## Safety And Status

- Credentials resolve from the current process, supported PPT Master `.env` locations, then Codex global provider/auth storage.
- Credentials are injected only into the image-generation subprocess and are never written into this directory.
- External upload approval is accepted only from the two current-process variables shown above. It is checked before credential resolution and is never loaded from `.env` or persisted configuration. The approved value must be one exact bare host and the scope must match exactly.
- `GET <api-root>/models` must contain exact model id `gpt-image-2`; no fallback model is allowed.
- The existing PPT Master OpenAI backend performs the actual image request. Each request uploads one composite reference board to the resolved OpenAI-compatible endpoint. Obtain explicit approval for that endpoint and upload scope before generation.
- Official Roboflow screenshots are retained only on Option D's human research board. Model-bound D references use the ClearVision-only structural blueprint to prevent third-party labels or capabilities from leaking into candidates.
- This wrapper adds per-item reference boards, exact model preflight, atomic status updates, retryable failures, five-part Reject Gates, and CURRENT/D/E audit sheets.
- `validate` proves frozen contracts only unless strict flags are supplied. `manifest.json` and `audit/audit_index.md` report honest readiness separately and remain `NOT_READY` until preflight, all six Masters, all 48 outputs, and all 48 manual Reject Gates pass.
- `Approved-Candidate` means selected as an internal Master Reference for chain consistency. It does not mean product-owner approval.
