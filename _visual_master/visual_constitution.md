# ClearVision Visual Constitution

Version: `1.0`  
Scope: Visual reference images only. This document does not authorize frontend implementation.

## 1. Product Character

ClearVision is an industrial vision engineering workstation, not a generic SaaS dashboard. The interface must feel precise, restrained, trustworthy, information-dense, and comfortable for long sessions. It should combine the credibility of installed engineering software with the refinement of a modern AI-assisted workstation.

The visual system must communicate four things immediately:

1. The current engineering context: project, flow, device, station, or run.
2. The current authority and state: editable/read-only, connected/disconnected, ready/running/completed, OK/NG/warning/error.
3. The next valid action, without turning every action into a primary button.
4. The relationship between configuration, execution evidence, and results.

## 2. Source Of Truth

- Product semantics, routes, fields, workflow names, state machines, and relationships come from the running ClearVision UI and current backend contracts.
- Generated images may refine layout, hierarchy, typography, surfaces, borders, spacing, icons, and visual emphasis.
- Generated copy, numbers, graphs, device readings, workflow labels, and business data are never authoritative.
- A generated image is a visual design reference. It cannot add, remove, rename, or redefine a product capability.

## 3. Core Visual Language

- Desktop-first workstation composition at `1920 x 1080`, compact density, stable regions, and crisp 1 px boundaries.
- Graphite application chrome (`#171c22`, `#222932`) may frame light neutral work surfaces; avoid a one-note all-dark or all-light palette.
- ClearVision cinnabar (`#b6453c`, `#9f3932`) is reserved for brand/selection emphasis, not routine primary actions.
- Action blue `#166f9f`; OK `#16866f`; NG `#d12f3f`; warning `#9b6a13`.
- Neutral surfaces should separate shell, canvas, inspector, evidence, and data regions through value and border changes rather than shadows or decorative cards.
- Corners stay in the `3-8 px` range. Compact controls may be `3-5 px`; panels should rarely exceed `6 px`.
- Typography uses a Windows/system UI family, clear numeric alignment, compact labels, and restrained headings. No viewport-scaled type or poster headlines.
- Icons are simple, familiar, consistent, and subordinate to engineering meaning.

## 4. Composition Rules

- The app shell is stable across pages. Page identity is visible without consuming a large hero region.
- Workspaces use explicit regions: navigation, command bar, primary work surface, inspector/evidence, and status/result areas.
- Lists prioritize scanability: aligned columns, compact rows, persistent filters, visible state, and row-level actions that do not dominate.
- Forms group by engineering task and dependency, not by decorative card count. Related labels, values, units, status, and diagnostics stay close.
- Canvas, preview, ROI, and evidence imagery receive the largest usable area on their respective screens.
- Empty, loading, warning, error, forbidden, running, and completed states preserve the surrounding layout to prevent context loss.
- Modal and drawer content must clearly identify scope, consequence, and exit. They are not full-page marketing cards.

## 5. Density And Rhythm

- Compact desktop rhythm is the default: 4/8 px base spacing, 28-34 px controls, 32-40 px rows, and 40-48 px major bars where practical.
- Dense does not mean cramped. Use alignment, grouping, hierarchy, and whitespace between semantic regions, not large empty canvases around small content islands.
- Fixed-format regions use stable widths/heights and explicit split relationships so later UI implementation can reproduce them accurately.

## 6. State Expression

- Color is never the only state signal. Pair it with icon, label, shape, or position.
- OK/NG semantics are unambiguous and consistent across Flow, Inspection, Results, and Station views.
- Running state shows activity and current phase without shifting the layout.
- Stale, offline, partial, forbidden, and validation-error states remain visually distinct.
- Destructive actions are visually restrained until the user reaches the actual confirmation boundary.

## 7. Prohibited Directions

- Generic SaaS dashboard templates, card mosaics, oversized KPI tiles, or marketing hero layouts.
- Large pill shapes, excessive rounded containers, nested cards, floating page sections, or ornamental badges.
- Glassmorphism, neon cyberpunk, game HUD styling, decorative gradients, glow, bokeh, or cinematic concept-art framing.
- Poster-scale headings, invented diagrams, fake technical telemetry, fictional devices, or fabricated workflow capabilities.
- Decorative complexity that reduces scan speed, precision, or implementation feasibility.
- Logos, watermarks, third-party brand marks, or generated brand-name variants.

## 8. Acceptance Gate

A candidate can become a Master Reference only when it:

- preserves the real page role and functional hierarchy;
- has implementable regions, controls, spacing, and state treatment;
- is visually coherent with existing Masters;
- avoids authoritative-looking invented copy or data;
- remains useful as a pixel-level reconstruction target at `1920 x 1080`;
- is explicitly labeled `Approved-Candidate`, which means selected for the reference chain, not approved by the product owner.

