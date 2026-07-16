import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import type { FlowCanvasOwner } from '@/capabilities/project-workspace/flow';
import type { ImageCanvasOwner } from '@/capabilities/project-workspace/image/imageCanvasOwner';
import type { InspectorOwner } from '@/capabilities/project-workspace/inspector';
import type { PreviewOwner } from '@/capabilities/project-workspace/preview/previewOwner';
import { createRoiInteractionOwner } from '@/capabilities/project-workspace/roi/roiInteractionOwner';
import { createWorkspaceLifecycleDiagnosticsOwner } from '@/capabilities/project-workspace/workspaceLifecycleDiagnostics';

const projectId = '11111111-1111-4111-8111-111111111111';
const nodeId = '22222222-2222-4222-8222-222222222222';

function roiNode() {
  return {
    id: nodeId,
    name: 'ROI',
    type: 'RoiManager',
    inputPorts: [],
    outputPorts: [],
    parameters: [
      { id: 'shape', name: 'Shape', value: 'Rectangle', defaultValue: 'Rectangle' },
      { id: 'x', name: 'X', value: 1, defaultValue: 0 },
      { id: 'y', name: 'Y', value: 2, defaultValue: 0 },
      { id: 'w', name: 'Width', value: 10, defaultValue: 1 },
      { id: 'h', name: 'Height', value: 20, defaultValue: 1 }
    ]
  };
}

function flowOwnerFor(node: Record<string, unknown>, gate: 'editable' | 'readonly' | 'running' = 'editable') {
  return {
    projection: reactive({
      mutationGate: gate,
      draft: { operators: [node], connections: [] },
      runtime: {
        selectedNodeId: nodeId,
        selectionRevision: 4,
        flowRevision: 7
      }
    })
  } as unknown as FlowCanvasOwner;
}

function previewOwner(stale = false) {
  return {
    projection: reactive({
      requestIdentity: { requestKey: 'preview-key' },
      isStale: stale
    })
  } as unknown as PreviewOwner;
}

function imageOwner() {
  let changed: ((geometry: unknown, phase: string) => void) | undefined;
  const begin = vi.fn((_geometry: unknown, callback: (geometry: unknown, phase: string) => void) => {
    changed = callback;
    return true;
  });
  const end = vi.fn();
  const owner = {
    projection: reactive({
      phase: 'ready',
      imageIdentity: 'preview-key:output',
      imageGeneration: 2,
      width: 640,
      height: 480
    }),
    roi: {
      begin,
      replace: vi.fn(() => true),
      cancelInteraction: vi.fn(),
      undo: vi.fn(() => null),
      redo: vi.fn(() => null),
      end,
      showStatistics: vi.fn()
    }
  } as unknown as ImageCanvasOwner;
  return { owner, begin, end, emit: (geometry: unknown, phase = 'commit') => changed?.(geometry, phase) };
}

function inspectorOwner() {
  const commitImageBacked = vi.fn(() => ({
    ok: true,
    code: 'node-parameters-patched',
    message: 'updated',
    flowRevision: 8,
    validationErrors: []
  }));
  const setDraftActive = vi.fn();
  return {
    owner: { commitImageBacked, setDraftActive } as unknown as InspectorOwner,
    commitImageBacked,
    setDraftActive
  };
}

describe('G4 ROI interaction owner', () => {
  it('keeps drag commits local and confirms one atomic multi-parameter command', () => {
    const flow = flowOwnerFor(roiNode());
    const image = imageOwner();
    const inspector = inspectorOwner();
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const owner = createRoiInteractionOwner({
      projectId,
      flowOwner: flow,
      inspectorOwner: inspector.owner,
      previewOwner: previewOwner(),
      imageOwner: image.owner,
      diagnostics,
      startupFlags: {}
    });

    expect(owner.projection).toMatchObject({ phase: 'ready', canStart: true });
    expect(owner.start()).toBe(true);
    image.emit({ kind: 'rectangle', x: 11, y: 12, width: 30, height: 40 });
    expect(inspector.commitImageBacked).not.toHaveBeenCalled();
    expect(owner.projection).toMatchObject({ phase: 'editing', dirty: true, canConfirm: true });

    expect(owner.confirm()).toMatchObject({ ok: true, flowRevision: 8 });
    expect(inspector.commitImageBacked).toHaveBeenCalledTimes(1);
    expect(inspector.commitImageBacked).toHaveBeenCalledWith({
      nodeId,
      selectionRevision: 4,
      flowRevision: 7,
      mode: 'parameters',
      values: { X: 11, Y: 12, Width: 30, Height: 40 }
    });
    expect(image.end).toHaveBeenCalledTimes(1);
    owner.dispose();
    expect(diagnostics.diagnostics.roiOwnerCount).toBe(0);
    diagnostics.dispose();
  });

  it('cancels without a Flow command and blocks readonly/stale sessions', () => {
    const image = imageOwner();
    const inspector = inspectorOwner();
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const readonly = createRoiInteractionOwner({
      projectId,
      flowOwner: flowOwnerFor(roiNode(), 'readonly'),
      inspectorOwner: inspector.owner,
      previewOwner: previewOwner(),
      imageOwner: image.owner,
      diagnostics,
      startupFlags: {}
    });
    expect(readonly.projection).toMatchObject({ phase: 'readonly', canStart: false });
    readonly.dispose();

    const staleDiagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const stale = createRoiInteractionOwner({
      projectId,
      flowOwner: flowOwnerFor(roiNode()),
      inspectorOwner: inspector.owner,
      previewOwner: previewOwner(true),
      imageOwner: image.owner,
      diagnostics: staleDiagnostics,
      startupFlags: {}
    });
    expect(stale.projection).toMatchObject({ phase: 'stale', canStart: false });
    expect(inspector.commitImageBacked).not.toHaveBeenCalled();
    stale.dispose();
    diagnostics.dispose();
    staleDiagnostics.dispose();
  });

  it('routes Caliper geometry through the typed structural command', () => {
    const caliper = {
      id: nodeId,
      name: 'Caliper',
      type: 'CaliperTool',
      inputPorts: [{ id: 'search-region', name: 'SearchRegion', dataType: 'Rectangle' }],
      outputPorts: [],
      parameters: []
    };
    const image = imageOwner();
    const inspector = inspectorOwner();
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const owner = createRoiInteractionOwner({
      projectId,
      flowOwner: flowOwnerFor(caliper),
      inspectorOwner: inspector.owner,
      previewOwner: previewOwner(),
      imageOwner: image.owner,
      diagnostics,
      startupFlags: {}
    });
    expect(owner.start()).toBe(true);
    image.emit({ kind: 'rectangle', x: 3, y: 4, width: 50, height: 60 });
    owner.confirm();
    expect(inspector.commitImageBacked).toHaveBeenCalledWith(expect.objectContaining({
      nodeId,
      mode: 'caliper-search-region',
      values: { X: 3, Y: 4, Width: 50, Height: 60 }
    }));
    owner.dispose();
    diagnostics.dispose();
  });
});
