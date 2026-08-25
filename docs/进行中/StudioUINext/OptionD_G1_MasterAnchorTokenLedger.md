# Option D G1 Master Anchor / Token Ledger

```text
DOCUMENT_STATUS=FROZEN_G1_PASS
GATE=G1
GATE_STATE=PASS
AUDIT_DATE=2026-08-24
TIMEZONE=Asia/Shanghai
SOURCE_HEAD=cdd114082821bbe750fb7945a0c3a4e89002d67c
VISUAL_AUTHORITY=_visual_master/option_D/masters
MEASUREMENT_FIXTURE=option-d-g1-master-measurements.v2
MEASUREMENT_ASSERTION=PASS
MEASUREMENT_MANIFEST_SHA256=0815b823d2c8d8ad73024dd2ab1fc287c1ad5351a97e9cbc21f5f4ada76595f1
G1_EVIDENCE_MANIFEST=OptionD_G1_EvidenceManifest.md
```

## 1. Scope And Rules

- D05, D13 and D16 are measured at `3840x2160` output and converted to the `1920x1080` CSS grid with scale `2`.
- The hard gate asserts each Master SHA-256, exact edge output pixel, minimum edge response and exact RGBA sample. It uses no resampling and no masks.
- `KEEP` means the current semantic token is already correct. `CALIBRATE@Gx` freezes a later consuming Gate decision; it does not authorize G1 to alter a blocked page. `ADD_ALIAS_PENDING` permits one shared semantic alias only after sibling Masters resolve to one value.
- A cumulative pane boundary is not converted into a one-off token. It remains a layout primitive responsibility at its consuming Gate.
- D05-D07 canonical node, port, connection, selection and state styling is not reinterpreted. D19/D20 dark fixtures remain global theme projections only.

Machine evidence: `.tmp/studio-ui-next/option-d-g1/master-measurements.json`, SHA-256
`0815b823d2c8d8ad73024dd2ab1fc287c1ad5351a97e9cbc21f5f4ada76595f1`.

## 2. Geometry Anchors

| Master | Anchor | CSS px | Existing semantic | G1 disposition |
| --- | --- | ---: | --- | --- |
| D05 | product rail end | 100 | `--cv-workspace-product-rail-width` is currently 56/60 by density | `CALIBRATE@G2/G4`; do not resize current Workspace in G1 |
| D05 | operator pane end | 382 | cumulative operator rail/pane boundary | `CALIBRATE@G4`; no one-off alias |
| D05 | inspector pane start | 1504.5 | cumulative split workbench boundary | `CALIBRATE@G4`; splitter primitive owns min/max |
| D05 | global header end | 69 | product/workspace headers have distinct semantics | `KEEP_SEMANTIC_SPLIT`; no universal header token |
| D05 | project context end | 124.5 | Workspace context band | `CALIBRATE@G4` |
| D05 | workspace command end | 201.5 | Workspace command band | `CALIBRATE@G4` |
| D05 | status strip start | 1024.5 | `--cv-workspace-status-height` expresses height, not coordinate | `CALIBRATE@G4`; retain height semantic |
| D13 | product rail end | 98 | shared Product Shell rail family | `CALIBRATE@G2/G6`; reconcile with D05/D16 around 100 px |
| D13 | readiness pane end | 493 | AI readiness pane boundary | `CALIBRATE@G6`; no page-local global token |
| D13 | handoff pane start | 1406.5 | AI handoff split boundary | `CALIBRATE@G6` |
| D13 | global header end | 83 | Product Shell plus local AI structure | `KEEP_SEMANTIC_SPLIT` |
| D13 | local title end | 172 | AI local title band | `CALIBRATE@G6` |
| D13 | session strip start | 999.5 | AI session/status boundary | `CALIBRATE@G6` |
| D16 | product rail end | 100.5 | shared Product Shell rail family | `CALIBRATE@G2/G6` |
| D16 | settings rail end | 467 | Settings Master rail boundary | `CALIBRATE@G6` |
| D16 | global header end | 87 | Product Shell plus Settings local structure | `KEEP_SEMANTIC_SPLIT` |
| D16 | save footer start | 925 | Settings save footer boundary | `CALIBRATE@G6`; one `settingsWriteCoordinator` remains |

## 3. Color Anchors

| Master | Sample | Exact value | Existing semantic | G1 disposition |
| --- | --- | --- | --- | --- |
| D05 | rail surface | `#f9fafc` | light Workspace rail family | `CALIBRATE@G4`; do not borrow Canvas node color |
| D05 | operator surface | `#fefefe` | `--cv-surface-raised` | `CALIBRATE@G4` |
| D05 | canvas surface | `#fefdfd` | `--flow-canvas-background` | `CALIBRATE_BACKGROUND@G4`; node/port/edge tokens frozen |
| D05 | inspector surface | `#fafcfd` | `--cv-surface-tool` | `CALIBRATE@G4` |
| D05 | primary action | `#035ea8` | current action token also feeds canonical connection | `ADD_ALIAS_PENDING`; action fill must not change connection |
| D05 | canvas connection | `#166f9f` | `--flow-canvas-connection: var(--cv-color-action)` | `KEEP`; exact match |
| D13 | rail surface | `#0a1422` | Product Shell sidebar family | `CALIBRATE@G2/G6`; reconcile with D16 |
| D13 | page surface | `#fefefe` | page/raised surface hierarchy | `CALIBRATE@G6`; no route-specific white alias |
| D13 | readiness surface | `#f4f4f5` | tool/section surface | `CALIBRATE@G6` |
| D13 | handoff surface | `#ffffff` | `--cv-surface-raised/floating` | `KEEP` |
| D13 | warning surface | `#fef9ef` | warning-soft semantic | `CALIBRATE@G6`; retain status meaning |
| D13 | primary action | `#0651ac` | shared primary command semantic | `ADD_ALIAS_PENDING`; conflicts with D05/D16 values |
| D16 | rail surface | `#0a1622` | Product Shell sidebar family | `CALIBRATE@G2/G6`; no page selector |
| D16 | settings rail surface | `#fafbfc` | page/secondary surface | `CALIBRATE@G6` |
| D16 | page surface | `#fafbfc` | page/secondary surface | `CALIBRATE@G6` |
| D16 | selected nav surface | `#fbe3e3` | cinnabar brand-soft selection | `CALIBRATE@G2/G6` |
| D16 | field surface | `#fefefe` | `--cv-surface-raised` | `CALIBRATE@G6` |
| D16 | primary action | `#044ca3` | shared primary command semantic | `ADD_ALIAS_PENDING`; do not create a D16-only token |

The three primary-action samples are not equal. G1 therefore does not mutate `--cv-color-action` and does not create three page aliases. The consuming Gates must resolve one shared command semantic while preserving D05 connection `#166f9f`.

## 4. G1 Shared Foundation Decisions

| Area | Decision / evidence |
| --- | --- |
| Generic geometry | Added shared thin-border, focus-ring, scrollbar, content-measure and label-column tokens; base surfaces consume them instead of repeating literals |
| Typography | Windows/system stacks only; Chromium platform evidence uses Microsoft YaHei UI + Segoe UI for headings and Cascadia Code for numeric evidence; no custom font downloaded |
| Theme | Light/dark values remain in global token blocks; G1 added no route-specific dark selector |
| Density | Compact/comfortable remain global density projections; Design Lab exercises both without replacing capability state |
| Motion | media-query and explicit `data-reduced-motion` projections both remove Lab transitions |
| Primitives | Design Lab covers button/icon button, field/select/search/toggle, data table/pagination, menu/tooltip/modal/toast/splitter/tabs/status and page patterns |
| Canvas | Lab mounts `createCanonicalFlowCanvasHost` once, keeps one subscription set and disposes interaction then adapter; it does not define node/port/connection CSS |
| Action color | Canonical connection remains exact; a future primary-fill alias cannot reuse or silently repurpose that technical color |

## 5. Deferred Evidence Boundaries

- Real WebView2 font raster/fallback: `NOT_PERFORMED` in G1; belongs to G9 host evidence and is not replaced by Chromium.
- Windows 125%: `NOT_PERFORMED`; Chromium DSF evidence is not native DPI evidence.
- D19/D20 pixel comparison: `NOT_PERFORMED` in G1 because their pages are blocked until G6. Architectural evidence confirms global dark tokens only.
- Complete keyboard authoring for visual Canvas node navigation/movement/connection: not claimed by G1. Existing scoped copy/paste/delete/undo/redo/select-all/Escape commands remain; the broader keyboard-only audit stays in G7.
