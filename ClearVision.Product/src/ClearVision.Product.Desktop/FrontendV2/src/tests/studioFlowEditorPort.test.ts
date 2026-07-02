import { describe, expect, it } from 'vitest';
import type {
  HostedFlowCanvasViewState,
  LegacyFlowCanvasAdapter,
  LegacyFlowCanvasSnapshot
} from '@/adapters/legacyModules';
import {
  createStudioFlowEditorPort,
  type StudioFlowEditorSnapshot
} from '@/flowEditor/studioFlowEditorPort';

describe('StudioFlowEditorPort', () => {
  it('returns immutable flow and selected node snapshots', () => {
    const adapter = createLegacyAdapterFixture();
    const port = createStudioFlowEditorPort(adapter);

    port.replaceFlow({
      projectId: 'project-a',
      requestSequence: 1,
      flow: createFlowFixture()
    });
    port.selectNode({
      projectId: 'project-a',
      requestSequence: 2,
      nodeId: 'node-a'
    });

    const snapshot = port.getSnapshot();
    const flow = snapshot.flow as FlowFixture;
    const flowParameter = flow.operators[0]?.parameters[0];
    const selectedParameter = snapshot.selectedNode?.parameters[0] as { value: unknown } | undefined;
    expect(flowParameter).toBeTruthy();
    expect(selectedParameter).toBeTruthy();
    if (!flowParameter || !selectedParameter) {
      throw new Error('Expected selected test parameter to exist.');
    }
    flowParameter.value = 999;
    selectedParameter.value = 888;

    const nextSnapshot = port.getSnapshot();
    expect(getParameterValue(nextSnapshot, 'Threshold')).toBe(10);
  });

  it('rejects mismatched projects and stale request sequences without mutating the flow', () => {
    const adapter = createLegacyAdapterFixture();
    const port = createStudioFlowEditorPort(adapter);

    port.replaceFlow({
      projectId: 'project-a',
      requestSequence: 1,
      flow: createFlowFixture()
    });
    port.selectNode({
      projectId: 'project-a',
      requestSequence: 2,
      nodeId: 'node-a'
    });

    const projectMismatch = port.patchParameters({
      projectId: 'project-b',
      requestSequence: 3,
      expectedFlowRevision: port.getSnapshot().flowRevision,
      nodeId: 'node-a',
      parameters: { Threshold: 20 }
    });
    const staleRequest = port.patchParameters({
      projectId: 'project-a',
      requestSequence: 2,
      expectedFlowRevision: port.getSnapshot().flowRevision,
      nodeId: 'node-a',
      parameters: { Threshold: 21 }
    });

    expect(projectMismatch.disposition).toBe('project_mismatch');
    expect(staleRequest.disposition).toBe('stale_request');
    expect(getParameterValue(port.getSnapshot(), 'Threshold')).toBe(10);
  });

  it('rejects stale flow revisions, stale selections, missing nodes and missing parameters with typed dispositions', () => {
    const adapter = createLegacyAdapterFixture();
    const port = createStudioFlowEditorPort(adapter);

    port.replaceFlow({
      projectId: 'project-a',
      requestSequence: 1,
      flow: createFlowFixture()
    });
    port.selectNode({
      projectId: 'project-a',
      requestSequence: 2,
      nodeId: 'node-a'
    });
    const draftSnapshot = port.getSnapshot();

    const staleFlow = port.patchParameters({
      projectId: 'project-a',
      requestSequence: 3,
      expectedFlowRevision: draftSnapshot.flowRevision - 1,
      nodeId: 'node-a',
      parameters: { Threshold: 21 }
    });

    port.selectNode({
      projectId: 'project-a',
      requestSequence: 4,
      nodeId: 'node-b'
    });
    const staleSelection = port.patchParameters({
      projectId: 'project-a',
      requestSequence: 5,
      expectedFlowRevision: draftSnapshot.flowRevision,
      expectedSelectionRevision: draftSnapshot.selectionRevision,
      nodeId: 'node-a',
      parameters: { Threshold: 22 }
    });
    const nodeNotFound = port.selectNode({
      projectId: 'project-a',
      requestSequence: 6,
      nodeId: 'missing-node'
    });
    port.selectNode({
      projectId: 'project-a',
      requestSequence: 7,
      nodeId: 'node-a'
    });
    const parameterNotFound = port.patchParameters({
      projectId: 'project-a',
      requestSequence: 8,
      expectedFlowRevision: port.getSnapshot().flowRevision,
      expectedSelectionRevision: port.getSnapshot().selectionRevision,
      nodeId: 'node-a',
      parameters: { Missing: 23 }
    });

    expect(staleFlow.disposition).toBe('stale_flow_revision');
    expect(staleSelection.disposition).toBe('stale_selection');
    expect(nodeNotFound.disposition).toBe('node_not_found');
    expect(parameterNotFound.disposition).toBe('parameter_not_found');
    expect(parameterNotFound.missingParameters).toEqual(['Missing']);
    expect(getParameterValue(port.getSnapshot(), 'Threshold')).toBe(10);
  });

  it('advances the project max observed sequence even when a high-sequence command fails later validation', () => {
    const adapter = createLegacyAdapterFixture();
    const port = createStudioFlowEditorPort(adapter);

    port.replaceFlow({
      projectId: 'project-a',
      requestSequence: 1,
      flow: createFlowFixture()
    });
    port.selectNode({
      projectId: 'project-a',
      requestSequence: 2,
      nodeId: 'node-a'
    });

    const staleRevision = port.patchParameters({
      projectId: 'project-a',
      requestSequence: 10,
      expectedFlowRevision: port.getSnapshot().flowRevision - 1,
      nodeId: 'node-a',
      parameters: { Threshold: 20 }
    });
    const lateLowerSequence = port.patchParameters({
      projectId: 'project-a',
      requestSequence: 5,
      expectedFlowRevision: port.getSnapshot().flowRevision,
      nodeId: 'node-a',
      parameters: { Threshold: 21 }
    });

    expect(staleRevision.disposition).toBe('stale_flow_revision');
    expect(lateLowerSequence.disposition).toBe('stale_request');
    expect(getParameterValue(port.getSnapshot(), 'Threshold')).toBe(10);
  });

  it('keeps request sequence authority isolated by project and ignores project mismatches', () => {
    const adapter = createLegacyAdapterFixture();
    const port = createStudioFlowEditorPort(adapter);

    port.replaceFlow({
      projectId: 'project-a',
      requestSequence: 1,
      flow: createFlowFixture('project-a')
    });
    port.selectNode({
      projectId: 'project-a',
      requestSequence: 2,
      nodeId: 'node-a'
    });

    const mismatchedProject = port.patchParameters({
      projectId: 'project-b',
      requestSequence: 100,
      expectedFlowRevision: port.getSnapshot().flowRevision,
      nodeId: 'node-b',
      parameters: { Threshold: 20 }
    });
    const currentProjectAccepted = port.patchParameters({
      projectId: 'project-a',
      requestSequence: 3,
      expectedFlowRevision: port.getSnapshot().flowRevision,
      expectedSelectionRevision: port.getSnapshot().selectionRevision,
      nodeId: 'node-a',
      parameters: { Threshold: 21 }
    });

    expect(mismatchedProject.disposition).toBe('project_mismatch');
    expect(currentProjectAccepted.disposition).toBe('accepted');
    expect(getParameterValue(port.getSnapshot(), 'Threshold')).toBe(21);
  });

  it('rejects invalid request sequences without advancing the project max observed sequence', () => {
    const adapter = createLegacyAdapterFixture();
    const port = createStudioFlowEditorPort(adapter);

    port.replaceFlow({
      projectId: 'project-a',
      requestSequence: 1,
      flow: createFlowFixture()
    });
    port.selectNode({
      projectId: 'project-a',
      requestSequence: 2,
      nodeId: 'node-a'
    });

    for (const requestSequence of [0, -1, Number.NaN, Number.POSITIVE_INFINITY, 1.1, Number.MAX_SAFE_INTEGER + 1]) {
      const result = port.patchParameters({
        projectId: 'project-a',
        requestSequence,
        expectedFlowRevision: port.getSnapshot().flowRevision,
        nodeId: 'node-a',
        parameters: { Threshold: 30 }
      });
      expect(result.disposition).toBe('stale_request');
    }

    const accepted = port.patchParameters({
      projectId: 'project-a',
      requestSequence: 3,
      expectedFlowRevision: port.getSnapshot().flowRevision,
      expectedSelectionRevision: port.getSnapshot().selectionRevision,
      nodeId: 'node-a',
      parameters: { Threshold: 31 }
    });

    expect(accepted.disposition).toBe('accepted');
    expect(getParameterValue(port.getSnapshot(), 'Threshold')).toBe(31);
  });

  it('allocates globally monotonic non-duplicate request sequences for multiple callers', () => {
    const adapter = createLegacyAdapterFixture();
    const port = createStudioFlowEditorPort(adapter);

    const firstCaller = port.nextRequestSequence('project-a');
    const secondCaller = port.nextRequestSequence('project-a');

    expect(secondCaller).toBeGreaterThan(firstCaller);
    expect(new Set([firstCaller, secondCaller]).size).toBe(2);

    port.replaceFlow({
      projectId: 'project-a',
      requestSequence: secondCaller,
      flow: createFlowFixture()
    });
    const failedHighSequence = port.selectNode({
      projectId: 'project-a',
      requestSequence: 10,
      nodeId: 'missing-node'
    });
    const projectBCaller = port.nextRequestSequence('project-b');

    expect(failedHighSequence.disposition).toBe('node_not_found');
    expect(projectBCaller).toBe(11);
  });

  it('commits node parameter patches and advances the frontend flow revision', () => {
    const adapter = createLegacyAdapterFixture();
    const port = createStudioFlowEditorPort(adapter);

    port.replaceFlow({
      projectId: 'project-a',
      requestSequence: 1,
      flow: createFlowFixture()
    });
    port.selectNode({
      projectId: 'project-a',
      requestSequence: 2,
      nodeId: 'node-a'
    });
    const before = port.getSnapshot();

    const result = port.patchParameters({
      projectId: 'project-a',
      requestSequence: 3,
      expectedFlowRevision: before.flowRevision,
      expectedSelectionRevision: before.selectionRevision,
      nodeId: 'node-a',
      parameters: { Threshold: 21 }
    });

    expect(result.accepted).toBe(true);
    expect(result.disposition).toBe('accepted');
    expect(result.snapshot.flowRevision).toBe(before.flowRevision + 1);
    expect(getParameterValue(result.snapshot, 'Threshold')).toBe(21);
  });

  it('emits one structure or selection notification per adapter event and detaches on dispose', () => {
    const adapter = createLegacyAdapterFixture();
    const port = createStudioFlowEditorPort(adapter);
    let structureNotifications = 0;
    let selectionNotifications = 0;

    port.subscribeStructure(() => {
      structureNotifications += 1;
    });
    port.subscribeSelection(() => {
      selectionNotifications += 1;
    });

    expect(structureNotifications).toBe(1);
    expect(selectionNotifications).toBe(1);

    port.replaceFlow({
      projectId: 'project-a',
      requestSequence: 1,
      flow: createFlowFixture()
    });
    expect(structureNotifications).toBe(2);
    expect(selectionNotifications).toBe(2);

    port.selectNode({
      projectId: 'project-a',
      requestSequence: 2,
      nodeId: 'node-a'
    });
    expect(structureNotifications).toBe(2);
    expect(selectionNotifications).toBe(3);

    port.dispose();
    adapter.replaceFlow(createFlowFixture('project-b'));
    adapter.selectNode('node-b');

    expect(structureNotifications).toBe(2);
    expect(selectionNotifications).toBe(3);
    expect(port.patchParameters({
      projectId: 'project-a',
      requestSequence: 3,
      expectedFlowRevision: port.getSnapshot().flowRevision,
      nodeId: 'node-a',
      parameters: { Threshold: 30 }
    }).disposition).toBe('disposed');
  });

  it('rejects a late project A replace after project B has become current', () => {
    const adapter = createLegacyAdapterFixture();
    const port = createStudioFlowEditorPort(adapter);

    const projectA = port.replaceFlow({
      projectId: 'project-a',
      requestSequence: 1,
      flow: createFlowFixture('project-a')
    });
    const projectB = port.replaceFlow({
      projectId: 'project-b',
      requestSequence: 2,
      flow: createFlowFixture('project-b')
    });
    const lateProjectA = port.replaceFlow({
      projectId: 'project-a',
      requestSequence: 1,
      flow: createFlowFixture('project-a-late')
    });

    expect(projectA.accepted).toBe(true);
    expect(projectB.accepted).toBe(true);
    expect(lateProjectA.disposition).toBe('stale_request');
    expect(port.getSnapshot().projectId).toBe('project-b');
    const currentFlow = port.getSnapshot().flow as FlowFixture;
    expect(currentFlow.operators[0]?.id).toBe('node-b');
  });
});

interface FlowFixture {
  operators: Array<{
    id: string;
    type: string;
    title: string;
    parameters: Array<{
      name: string;
      displayName: string;
      value: unknown;
      dataType: string;
    }>;
  }>;
  connections: [];
}

interface LegacyAdapterFixture extends LegacyFlowCanvasAdapter {
  readonly structureListeners: Set<(event: unknown) => void>;
  readonly selectionListeners: Set<(event: unknown) => void>;
}

function createLegacyAdapterFixture(): LegacyAdapterFixture {
  let flowRevision = 0;
  let selectionRevision = 0;
  let selectedNodeId: string | null = null;
  let flow: FlowFixture = { operators: [], connections: [] };
  const structureListeners = new Set<(event: unknown) => void>();
  const selectionListeners = new Set<(event: unknown) => void>();

  const adapter: LegacyAdapterFixture = {
    structureListeners,
    selectionListeners,
    resize: () => undefined,
    render: () => undefined,
    dispose: () => {
      structureListeners.clear();
      selectionListeners.clear();
    },
    getViewState: (): HostedFlowCanvasViewState => ({
      selectedNode: selectedNodeId,
      selectedConnection: null,
      scale: 1,
      offset: { x: 0, y: 0 },
      nodeCount: flow.operators.length,
      connectionCount: flow.connections.length
    }),
    getSnapshot: (): LegacyFlowCanvasSnapshot => ({
      flowRevision,
      selectionRevision,
      selectedNodeId,
      flow: deepClone(flow),
      selectedNode: selectedNodeId
        ? deepClone(flow.operators.find((operator) => operator.id === selectedNodeId) ?? null)
        : null
    }),
    replaceFlow: (nextFlow) => {
      flow = deepClone(nextFlow) as FlowFixture;
      flowRevision += 1;
      selectedNodeId = null;
      emitStructure();
      emitSelection();
    },
    selectNode: (nodeId) => {
      if (nodeId && !flow.operators.some((operator) => operator.id === nodeId)) {
        return false;
      }

      selectedNodeId = nodeId;
      selectionRevision += 1;
      emitSelection();
      return true;
    },
    patchNodeParameters: (nodeId, parameterPatch) => {
      const node = flow.operators.find((operator) => operator.id === nodeId);
      if (!node) {
        return {
          updated: false,
          reason: 'node_not_found',
          missingParameters: []
        };
      }

      const entries = Object.entries(parameterPatch);
      const missingParameters = entries
        .map(([name]) => name)
        .filter((name) => !node.parameters.some((parameter) => parameter.name.toLowerCase() === name.toLowerCase()));
      if (missingParameters.length > 0) {
        return {
          updated: false,
          reason: 'parameter_not_found',
          missingParameters
        };
      }

      for (const [name, value] of entries) {
        const parameter = node.parameters.find((item) => item.name.toLowerCase() === name.toLowerCase());
        if (parameter) {
          parameter.value = deepClone(value);
        }
      }
      flowRevision += 1;
      selectionRevision += 1;
      emitStructure();
      emitSelection();
      return {
        updated: true,
        reason: 'updated',
        missingParameters: []
      };
    },
    subscribeStructure: (listener) => {
      structureListeners.add(listener);
      listener({ flowRevision, reason: 'initial' });
      return () => {
        structureListeners.delete(listener);
      };
    },
    subscribeSelection: (listener) => {
      selectionListeners.add(listener);
      listener({ selectionRevision, reason: 'initial' });
      return () => {
        selectionListeners.delete(listener);
      };
    }
  };

  function emitStructure(): void {
    structureListeners.forEach((listener) => {
      listener({ flowRevision, reason: 'fixture' });
    });
  }

  function emitSelection(): void {
    selectionListeners.forEach((listener) => {
      listener({ selectionRevision, reason: 'fixture' });
    });
  }

  return adapter;
}

function createFlowFixture(projectId = 'project-a'): FlowFixture {
  const suffix = projectId.endsWith('b') ? 'b' : 'a';
  return {
    operators: [
      {
        id: `node-${suffix}`,
        type: 'Thresholding',
        title: 'Threshold',
        parameters: [
          {
            name: 'Threshold',
            displayName: 'Threshold',
            value: 10,
            dataType: 'int'
          }
        ]
      },
      {
        id: `node-${suffix === 'a' ? 'b' : 'a'}`,
        type: 'Blur',
        title: 'Blur',
        parameters: [
          {
            name: 'Sigma',
            displayName: 'Sigma',
            value: 1,
            dataType: 'float'
          }
        ]
      }
    ],
    connections: []
  };
}

function getParameterValue(snapshot: StudioFlowEditorSnapshot, name: string): unknown {
  return snapshot.selectedNode?.parameters.find((parameter) => parameter.name === name)?.value;
}

function deepClone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}
