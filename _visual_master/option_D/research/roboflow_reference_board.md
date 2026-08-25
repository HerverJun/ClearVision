# Option D Roboflow Reference Board

Captured on `2026-08-16` from Roboflow-owned official sources. This research informs interaction architecture and visual relationships only. ClearVision screenshots and code remain the sole functional authority.

## Official Sources

- [Workflows overview](https://docs.roboflow.com/workflows)
- [Create a Workflow](https://docs.roboflow.com/workflows/build/create-a-workflow)
- [Build a Workflow](https://docs.roboflow.com/workflows/build/build-a-workflow)
- [Test a Workflow](https://docs.roboflow.com/workflows/build/test-a-workflow)
- [Workflows AI Assistant](https://docs.roboflow.com/workflows/build/workflows-ai-assistant)
- [Workflow blocks](https://docs.roboflow.com/workflows/blocks/blocks)

Markdown endpoints were used for semantic verification. Signed GitBook image endpoints were used for the local reference assets listed in `../references/source_manifest.json`.

## Verified Roboflow Facts

1. A Workflow is made of connected blocks that perform specific tasks. The official Build guide emphasizes block connection topology and parallel pathways.
2. Roboflow's editor has Builder Assist modes and an Auto Layout command. These are Roboflow facts only. ClearVision currently has neither command, so both are prohibited in D artwork.
3. The official Test guide states that testing opens a pane from the editor, takes inputs, runs the workflow, and shows output in the same testing interface.
4. The official AI Assistant guide states that its chat panel sits alongside the workflow canvas, can collapse, and keeps the canvas visually anchored.
5. The official AI Assistant guide distinguishes draft saving from publishing. ClearVision has its own existing save/run authority; Roboflow's Save/Publish/Use model is not transferable.
6. Official imagery shows low-noise light canvases, compact light nodes, thin connections, small ports, narrow category accents, and strong topology readability.

## Design Translation Into ClearVision

| Roboflow pattern studied | ClearVision D translation | Functional boundary |
| --- | --- | --- |
| Canvas-centered connected blocks | Give canonical FlowCanvas dominant area | Never create a second canvas or replace the current canvas owner |
| Compact nodes and thin edges | Reduce node visual weight and move full parameters to the real Inspector | Keep only current operator identity, ports, and states |
| Contextual test pane | Open current Preview/ROI/Result as an on-demand workspace | No new test mode, cache, sink, batch job, or input type |
| Assistant beside canvas | Make the existing AI workbench feel like the same engineering system | No chat composer, live canvas editing, or Roboflow agent ability |
| Progressive editor help | Use current selection/task to decide which verified panel appears | No Builder Assist, Auto Layout, or inferred automation |
| Light low-noise material | Use ClearVision's neutral surfaces and restrained action/state colors | Do not copy Roboflow purple, navigation, logo, or business vocabulary |

## Local Reference Assets

- `01_build_workflow_overview.png`: official compact node/topology example; primary node-style reference.
- `02_create_workflow.jpg`: official product context image; not used as a ClearVision layout template.
- `03_test_button_editor.png`: official editor command crop; confirms test is entered from editor chrome.
- `04_test_pane.png`: official contextual testing pane; used only for the on-demand workspace relationship.
- `05_workflows_build_card.png`, `06_workflows_test_card.png`, `07_workflows_ai_assistant_card.png`: official overview-card icon assets; retained for provenance, not used as functional references.

## Explicit Non-Transfer List

Do not generate Roboflow logo/brand, purple theme, Projects/Monitoring/Deployments/Universe navigation, Create Workflow, Templates, Builder Assist, Auto Layout, Deploy, Publish, Use, Test Workflow, Debug Mode, Batch Job, block cache, sinks, model marketplace, notifications, Roboflow blocks, Input/Output nodes, or AI chat behavior unless the same capability is independently verified in current ClearVision.
