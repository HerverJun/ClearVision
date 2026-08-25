# ClearVision Option D Visual Constitution

## Design Direction

**Roboflow Workflow Engineering, translated into ClearVision's industrial product truth.**

The physical scene is a vision engineer working for hours at a 1920x1080 Windows workstation under neutral factory-office lighting, repeatedly moving between topology, parameters, image evidence, and run state. The interface therefore uses a light, low-noise canvas and compact contextual tools rather than a dark panel wall.

CURRENT screenshots and current ClearVision code are the functional source of truth. They are not a layout template. D may relocate, regroup, resize, collapse, or progressively disclose verified functions, but it may not add, remove, rename, reinterpret, or imply any capability.

## Architecture DNA

1. **Canvas first.** The FlowCanvas is the dominant editor surface and should normally occupy about 65-75% of the usable viewport.
2. **Progressive disclosure.** Full parameters, Preview, ROI, Result, and run detail appear when their task context requires them instead of remaining permanently open.
3. **Search first.** The existing ClearVision operator search, categories, recent/favorites, compatibility filter, click-to-add, and drag-to-add capabilities become an on-demand block picker. No Roboflow block category is imported.
4. **Contextual UI.** Nothing selected is quiet; node selection opens the real Inspector; connection selection exposes connection facts; ROI editing exposes real ROI controls; Preview/Result opens its corresponding workspace.
5. **Compact topology.** Nodes express operator identity, ports, essential state, and a very small summary. Full parameters live in the verified Inspector.
6. **One workspace continuum.** Build, configure, preview, run, and inspect remain in the same ClearVision project context. Run does not become a dashboard.
7. **Low permanent chrome.** Product navigation, project context, commands, status, and canvas controls are compact and separated by task frequency.

## Flow Workspace

- Use a very light neutral canvas with an extremely subtle dotted spatial reference.
- Use compact white or near-white nodes, thin borders, minimal shadow, tiny explicit ports, restrained category accents, and fine connections.
- Default connections are quiet. Selected, running, warning, error, and failed states may increase weight only where those states are already real in ClearVision.
- The operator library is a floating or dockable contextual picker, not a permanently wide category wall.
- The selected-node Inspector is a resizable contextual right dock. It retains real identity, lifecycle, ports, resource binding, common parameters, advanced parameters, special workbenches, and validation feedback.
- Preview/Result is an on-demand resizable workspace. ROI editing gives the image usable area and keeps undo, redo, cancel, and apply adjacent to the image.
- Verified canvas commands are undo, redo, copy, paste, duplicate, enable/disable, delete, zoom out, reset view, and zoom in. **Fit and Auto Layout are forbidden because current ClearVision does not expose them.**
- Save, formal run/stop, result, readiness, run details, project/version, and status remain available through the real shell context.

## Product-Wide Translation

- Shell and list pages use continuous work surfaces, dense tables, compact command bars, and contextual details instead of card mosaics.
- Results and Station pages use investigation/operations split views with evidence and current object dominant.
- AI uses the same workflow-engineering shell grammar but never invents a canvas or chat surface. Candidate, plan/build, difference, validation, rehearsal, handoff, history, and recovery remain the real stages.
- Settings use a slim grouped navigation rail plus a task-specific workbench. Device pages may use verified list/editor/preview or profile/editor/traffic relationships.
- Empty, forbidden, and About states remain product surfaces, not marketing pages.

## Visual Language

- Color strategy: restrained neutral system with one ClearVision action accent under 10% of the surface.
- Canvas: near-white neutral; surfaces: white and cool neutral; borders: thin cool gray; app frame: graphite only where it clarifies shell ownership.
- ClearVision cinnabar `#b6453c` is identity plus active Product Shell / navigation / grouped-rail selection emphasis, not a large background.
- Action blue is reserved for commands, keyboard focus, and selected canvas objects. OK, NG, warning, execution error, offline, unknown, and disabled remain distinct semantic roles.
- Typography: Segoe UI Variable / Microsoft YaHei UI / system sans; compact 12-14px UI scale; no display type in controls; letter spacing 0.
- Spacing: strict 4/8px rhythm with controlled 12/16/24px section cadence.
- Radius: 3-8px. No large pills or soft consumer cards.
- Shadow: only floating pickers, contextual docks, modal, and real elevation; never border plus broad decorative shadow.
- Icons: compact, familiar, consistent stroke language; no invented toolbar affordance.

## Final Full-Set Consistency Lock

- Request exact `gpt-image-2` `3840x2160` (`4K`, `16:9`, high quality) for every Option D call and deliver every PNG at `3840x2160`. Compose on a `1920x1080` logical desktop grid rendered at 2x density; 4K delivery must not shrink text, controls, icons, hit targets, or spacing. If the reference-image endpoint returns a smaller near-16:9 source, retain its dimensions and hash, apply only the audited deterministic Lanczos normalization, and never describe that result as model-native 4K.
- Authenticated standard pages share one Product Shell: the full `ClearVision STUDIO` lockup, one stable top-bar height and baseline, the verified navigation order, one cinnabar selection treatment, and one fixed service / appearance / more / account utility cluster. Page content may change; shell geometry and component shape may not.
- Flow states `05-08` share the D Flow Master geometry exactly: the same product rail width, project context bar, command layer, canvas origin, contextual Inspector/Preview edges, and bottom status region. Validation, ROI, and run details change only the task layer; `08` is an overlay on the same workspace, never a new shell.
- AI states `13-14` share the D AI Master rail, header, columns, and utility cluster. Failure recovery is a state of the same AI workbench and may not switch to a horizontal-navigation shell.
- Settings screens `16-21` share the D Settings Master primary rail, grouped settings rail, header baseline, field rhythm, save boundary, and cinnabar selected-row grammar. Camera/TCP source lists and diagnostic rails are task-specific additions. The verified dark theme on `19-20` changes tokens only, never geometry or selection semantics.
- Project populated/empty states `03-04` keep the same command bar, search/sort controls, table frame, columns, padding, and create-action component. The empty state replaces table rows inside that frame; it does not create a different page shell or visual vocabulary.
- Use action blue only for commands, focus, and selected canvas objects; cinnabar only for identity plus active Product Shell/navigation/grouped-rail selection; green only for OK/online/success; red only for NG/error/destructive boundaries; and amber only for warning/recovery. Never use blue as an alternate navigation selection or red as a normal object-selection color.
- Across the set, keep Windows-native 12-14px logical UI typography, 28-34px logical controls, 32-40px rows, 4/8px spacing, 3-8px radii, crisp 1px boundaries, and shadows only for actual floating layers. Chinese user-facing terminology and repeated component labels must not drift between sibling states.
- Login `01` and Forbidden `24` are deliberate minimal-shell exceptions. About `23` remains inside the authenticated Product Shell. Task-specific columns, tables, previews, consoles, and modal geometry may differ when their verified workflows require it; those differences must not alter the shared component grammar.

## Roboflow Reference Boundary

Official Roboflow material is used only to study canvas dominance, compact topology, contextual testing, and the relationship between an optional AI assistant and the canvas. Roboflow navigation, purple branding, block types, Builder Assist, Deploy, Publish, Use, Test Workflow, caching, sinks, models, notifications, and other Roboflow product functions must never appear in ClearVision artwork unless independently verified as an existing ClearVision function.

## Reject Gate

Reject a screen when any condition is true:

1. It invents a function, control, tab, navigation entry, data fact, device ability, AI ability, or workflow ability.
2. It removes or hides a confirmed function with no discoverable entry.
3. It still reads as the old ClearVision panel geometry with a cosmetic skin.
4. It drifts from D's canvas-first, search-first, contextual, low-noise system.
5. A D Flow state does not leave the canvas as the visual center when the current task permits it.

## Master Chain

`D_FLOW_MASTER -> D_AI_MASTER -> D_SETTINGS_MASTER -> D_FULL_SET`

No downstream D screen is generated until its required D master exists. D never references an A, B, C, or E master.
