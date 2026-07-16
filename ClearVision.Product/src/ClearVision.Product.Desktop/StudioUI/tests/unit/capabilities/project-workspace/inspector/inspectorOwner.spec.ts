import { nextTick, reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import { decodeOperatorCatalogItem } from '@/capabilities/operators-read/operatorContracts';
import {
  createInspectorOwner
} from '@/capabilities/project-workspace/inspector';
import type {
  FlowCanvasOwner,
  FlowCanvasOwnerProjection,
  FlowNodeParameterPatch,
  FlowNodePropertiesPatch
} from '@/capabilities/project-workspace/flow';
import {
  decodeWorkspaceProjectV1,
  encodeWorkspaceFlowUpdateV1,
  type WorkspaceProjectV1
} from '@/capabilities/project-workspace/workspaceContracts';
import { createWorkspaceLifecycleDiagnosticsOwner } from '@/capabilities/project-workspace/workspaceLifecycleDiagnostics';
import type {
  CanonicalCanvasRuntimeSnapshot,
  CanonicalFlowCommandResult,
  CanonicalFlowDraft,
  FlowMutationGate
} from '@/platform/canvas';

const projectId = '11111111-1111-4111-8111-111111111111';
const flowId = '22222222-2222-4222-8222-222222222222';
const sourceId = '33333333-3333-4333-8333-333333333333';
const targetId = '44444444-4444-4444-8444-444444444444';
const sourcePortId = '55555555-5555-4555-8555-555555555555';
const targetPortId = '66666666-6666-4666-8666-666666666666';
const connectionId = '77777777-7777-4777-8777-777777777777';

type MutableFlowProjection = {
  -readonly [Key in keyof FlowCanvasOwnerProjection]: FlowCanvasOwnerProjection[Key]
};

function parameter(id: string, name: string, dataType: string, value: unknown, overrides: Record<string, unknown> = {}) {
  return {
    id,
    name,
    displayName: name,
    description: `${name} description`,
    dataType,
    value,
    defaultValue: value,
    minValue: null,
    maxValue: null,
    isRequired: false,
    options: null,
    ...overrides
  };
}

function projectPayload() {
  return {
    id: projectId,
    name: 'G3 Inspector fixture',
    description: 'Inspector owner tests',
    version: '1.0.0',
    persistenceRevision: 4,
    flow: {
      id: flowId,
      name: 'Inspector flow',
      futureFlowField: 'keep-flow',
      operators: [{
        id: sourceId,
        name: 'Source node',
        type: 20,
        metadata: null,
        x: 10,
        y: 20,
        inputPorts: [],
        outputPorts: [{
          id: sourcePortId, name: 'Binary', direction: 1, dataType: 0,
          isRequired: false, futurePortField: 'keep-port'
        }],
        parameters: [
          parameter('88888888-8888-4888-8888-888888888881', 'Text', 'string', ''),
          parameter('88888888-8888-4888-8888-888888888882', 'Count', 'int', 0, { minValue: 0, maxValue: 10 }),
          parameter('88888888-8888-4888-8888-888888888883', 'Enabled', 'bool', false),
          parameter('88888888-8888-4888-8888-888888888884', 'Mode', 'enum', 'Auto', {
            options: [{ label: '自动', value: 'Auto' }, { label: '手动', value: 'Manual' }]
          }),
          parameter('88888888-8888-4888-8888-888888888885', 'Gain', 'double', 0, {
            minValue: 0, maxValue: 5, showSlider: true
          }),
          parameter('88888888-8888-4888-8888-888888888886', 'OptionalCount', 'int', null, {
            nullable: true
          })
        ],
        isEnabled: true,
        executionStatus: 2,
        executionTimeMs: 12,
        errorMessage: null,
        futureOperatorField: 'keep-operator'
      }, {
        id: targetId,
        name: 'Target node',
        type: 20,
        metadata: null,
        x: 300,
        y: 20,
        inputPorts: [{ id: targetPortId, name: 'Image', direction: 0, dataType: 0, isRequired: true }],
        outputPorts: [],
        parameters: [],
        isEnabled: true,
        executionStatus: 0,
        executionTimeMs: null,
        errorMessage: null
      }],
      connections: [{
        id: connectionId,
        sourceOperatorId: sourceId,
        sourcePortId,
        targetOperatorId: targetId,
        targetPortId,
        futureConnectionField: 'keep-connection'
      }],
      decisionConfiguration: null
    },
    globalSettings: {},
    globalVariables: { schemaVersion: '1.0', variables: [], sourceBindings: [], targetBindings: [] },
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] },
    createdAt: '2026-07-16T00:00:00Z',
    modifiedAt: '2026-07-16T01:00:00Z',
    lastOpenedAt: null
  };
}

const metadata = decodeOperatorCatalogItem({
  type: 20,
  displayName: 'Threshold',
  description: 'Metadata description',
  categoryId: 1,
  category: 'Image',
  lifecycle: 0,
  lifecycleNote: null,
  defaultHidden: false,
  iconName: null,
  keywords: [],
  tags: [],
  version: '1.0.0',
  qualityState: null,
  inputPorts: [{ name: 'Image', displayName: 'Image', dataType: 0, isRequired: true, description: null }],
  outputPorts: [{ name: 'Binary', displayName: 'Binary', dataType: 0, isRequired: false, description: null }],
  parameters: [
    { name: 'Text', displayName: 'Text', description: null, dataType: 'string', defaultValue: '', minValue: null, maxValue: null, isRequired: false, options: null },
    { name: 'Count', displayName: 'Count', description: null, dataType: 'int', defaultValue: 0, minValue: 0, maxValue: 10, isRequired: true, options: null },
    { name: 'Enabled', displayName: 'Enabled', description: null, dataType: 'bool', defaultValue: false, minValue: null, maxValue: null, isRequired: false, options: null },
    { name: 'Mode', displayName: 'Mode', description: null, dataType: 'enum', defaultValue: 'Auto', minValue: null, maxValue: null, isRequired: false, options: [{ label: '自动', value: 'Auto' }, { label: '手动', value: 'Manual' }] },
    { name: 'Gain', displayName: 'Gain', description: null, dataType: 'double', defaultValue: 0, minValue: 0, maxValue: 5, isRequired: false, options: null },
    { name: 'OptionalCount', displayName: 'OptionalCount', description: null, dataType: 'int', defaultValue: null, minValue: null, maxValue: null, isRequired: false, options: null }
  ],
  parameterConstraints: [{
    parameter: 'Count', requiredPolicy: 'required', requiredWhen: null, enabledWhen: null,
    disabledWhen: null, visibleWhen: null, hiddenWhen: null, ignoredWhen: null,
    atLeastOneGroup: null, mutuallyExclusiveGroup: null, aliasFor: null,
    deprecated: false, resourceKind: null, reasonCode: 'COUNT_REQUIRED', satisfiedByInputPorts: []
  }],
  outputAvailabilityRules: [{
    output: 'Binary', availableWhen: { all: [{ parameter: 'Enabled', comparison: 'equals', value: true }] },
    reasonCode: 'BINARY_DISABLED'
  }],
  imageInputContracts: [],
  imageInputContractPresentations: []
});

function runtime(overrides: Partial<CanonicalCanvasRuntimeSnapshot> = {}): CanonicalCanvasRuntimeSnapshot {
  return Object.freeze({
    nodeCount: 2,
    connectionCount: 1,
    flowRevision: 0,
    selectionRevision: 0,
    selectedNodeId: null,
    selectedNodeIds: Object.freeze([]),
    selectedConnectionId: null,
    multiSelectionCount: 0,
    scale: 1,
    offsetX: 0,
    offsetY: 0,
    logicalWidth: 800,
    logicalHeight: 600,
    backingWidth: 800,
    backingHeight: 600,
    dpr: 1,
    isConnecting: false,
    isDraggingNodes: false,
    isPanning: false,
    isSelecting: false,
    canUndo: false,
    canRedo: false,
    mutationGate: 'editable',
    nodes: Object.freeze([]),
    resources: Object.freeze({
      adapterDisposed: false, canvasDestroyed: false, interactionDisposed: false,
      resizeObserverActive: true, themeObserverActive: true, drawFramePending: false,
      resizeFramePending: false, interactionFramePending: false, contextMenuTimerActive: false,
      structureListenerCount: 1, viewListenerCount: 1, selectionListenerCount: 1,
      interactionCleanupCount: 1, facadeListenerCount: 1
    }),
    ...overrides
  });
}

function createFakeFlow(project: WorkspaceProjectV1) {
  const encoded = encodeWorkspaceFlowUpdateV1(project)!;
  const state = reactive<MutableFlowProjection>({
    phase: 'mounted',
    projectId,
    mutationGate: 'editable',
    draft: Object.freeze({
      id: flowId,
      name: 'Inspector flow',
      operators: encoded.operators as CanonicalFlowDraft['operators'],
      connections: encoded.connections as CanonicalFlowDraft['connections'],
      decisionConfiguration: null,
      opaquePassthrough: project.flow?.opaquePassthrough ?? Object.freeze({})
    }),
    runtime: runtime(),
    feedback: null,
    catalog: Object.freeze({ phase: 'success', operators: Object.freeze([metadata]), isRefreshing: false, message: null }),
    error: null
  });

  function result(ok: boolean, code: string, message: string): CanonicalFlowCommandResult {
    return Object.freeze({ ok, code, message, flowRevision: state.runtime?.flowRevision ?? 0 });
  }

  function commitDraft(draft: CanonicalFlowDraft): void {
    state.draft = draft;
    state.runtime = runtime({
      ...state.runtime,
      flowRevision: (state.runtime?.flowRevision ?? 0) + 1,
      selectedNodeId: state.runtime?.selectedNodeId ?? null,
      selectedNodeIds: state.runtime?.selectedNodeIds ?? Object.freeze([]),
      selectedConnectionId: state.runtime?.selectedConnectionId ?? null,
      selectionRevision: state.runtime?.selectionRevision ?? 0
    });
  }

  const owner = {
    projectId,
    projection: state,
    commands: {
      patchNodeParameter(command: FlowNodeParameterPatch) {
        const operators = state.draft.operators.map(operator => {
          if (operator.id !== command.nodeId) return operator;
          return Object.freeze({
            ...operator,
            parameters: (operator.parameters as readonly Readonly<Record<string, unknown>>[]).map(parameter =>
              String(parameter.name).toLowerCase() === command.parameterName.toLowerCase()
                ? Object.freeze({ ...parameter, value: command.value })
                : parameter)
          });
        });
        const current = state.draft.operators.find(operator => operator.id === command.nodeId)
          ?.parameters as readonly Readonly<Record<string, unknown>>[];
        const value = current.find(parameter => String(parameter.name).toLowerCase() === command.parameterName.toLowerCase())?.value;
        if (Object.is(value, command.value)) return result(false, 'no-change', 'no change');
        commitDraft(Object.freeze({ ...state.draft, operators: Object.freeze(operators) }));
        return result(true, 'node-parameter-patched', 'patched');
      },
      patchNodeProperties(command: FlowNodePropertiesPatch) {
        const operators = state.draft.operators.map(operator => operator.id === command.nodeId
          ? Object.freeze({
              ...operator,
              ...(Object.prototype.hasOwnProperty.call(command, 'name') ? { name: command.name } : {}),
              ...(Object.prototype.hasOwnProperty.call(command, 'isEnabled') ? { isEnabled: command.isEnabled } : {})
            })
          : operator);
        commitDraft(Object.freeze({ ...state.draft, operators: Object.freeze(operators) }));
        return result(true, 'node-properties-patched', 'patched');
      },
      disconnect(id: string) {
        commitDraft(Object.freeze({
          ...state.draft,
          connections: Object.freeze(state.draft.connections.filter(connection => connection.id !== id))
        }));
        state.runtime = runtime({ ...state.runtime, flowRevision: state.runtime?.flowRevision ?? 0, selectedConnectionId: null });
        return result(true, 'connection-disconnected', 'disconnected');
      },
      selectNode(id: string) {
        state.runtime = runtime({
          ...state.runtime,
          selectedNodeId: id,
          selectedNodeIds: Object.freeze([id]),
          selectedConnectionId: null,
          selectionRevision: (state.runtime?.selectionRevision ?? 0) + 1,
          flowRevision: state.runtime?.flowRevision ?? 0
        });
        return result(true, 'node-selected', 'selected');
      }
    }
  } as unknown as FlowCanvasOwner;
  return { owner, state };
}

function mountInspector() {
  const project = decodeWorkspaceProjectV1(projectPayload());
  const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
  const workspace = diagnostics.reserveWorkspaceOwner(projectId);
  const flowLease = diagnostics.reserveFlowCanvas(projectId);
  const { owner: flowOwner, state } = createFakeFlow(project);
  const inspector = createInspectorOwner({ project, flowOwner, diagnostics });
  return { project, diagnostics, workspace, flowLease, flowOwner, state, inspector };
}

function select(
  state: MutableFlowProjection,
  overrides: Partial<CanonicalCanvasRuntimeSnapshot>
): void {
  state.runtime = runtime({
    ...state.runtime,
    flowRevision: state.runtime?.flowRevision ?? 0,
    selectionRevision: (state.runtime?.selectionRevision ?? 0) + 1,
    ...overrides
  });
}

describe('G3 Inspector owner', () => {
  it('projects empty, node, multi-node and connection selection from the one Flow projection', async () => {
    const context = mountInspector();
    expect(context.inspector.projection.mode).toBe('empty');

    select(context.state, { selectedNodeId: sourceId, selectedNodeIds: [sourceId], selectedConnectionId: null });
    await nextTick();
    expect(context.inspector.projection).toMatchObject({ mode: 'node', node: { id: sourceId, executionStatus: 'Success', executionTimeMs: 12, metadataPhase: 'ready' } });
    expect(context.inspector.projection.node?.parameters.map(item => item.editorKind))
      .toEqual(['text', 'number', 'boolean', 'enum', 'slider', 'number']);
    expect(context.inspector.projection.node?.outputPorts[0]).toMatchObject({ available: false });

    select(context.state, { selectedNodeId: targetId, selectedNodeIds: [sourceId, targetId], selectedConnectionId: null });
    await nextTick();
    expect(context.inspector.projection).toMatchObject({ mode: 'multi-node' });
    expect(context.inspector.projection.nodes).toHaveLength(2);

    select(context.state, { selectedNodeId: null, selectedNodeIds: [], selectedConnectionId: connectionId });
    await nextTick();
    expect(context.inspector.projection).toMatchObject({
      mode: 'connection',
      connection: { source: { nodeId: sourceId, portId: sourcePortId }, target: { nodeId: targetId, portId: targetPortId } }
    });

    context.inspector.dispose();
    context.flowLease.dispose();
    context.workspace.dispose();
    context.diagnostics.dispose();
  });

  it('commits valid primitive/null values, blocks invalid edits, and increments revision only on changes', async () => {
    const context = mountInspector();
    select(context.state, { selectedNodeId: sourceId, selectedNodeIds: [sourceId], selectedConnectionId: null });
    await nextTick();

    expect(context.inspector.patchNodeParameter('Text', '')).toMatchObject({ ok: false, code: 'no-change', flowRevision: 0 });
    expect(context.inspector.patchNodeParameter('Count', 0)).toMatchObject({ ok: false, code: 'no-change', flowRevision: 0 });
    expect(context.inspector.patchNodeParameter('Enabled', false)).toMatchObject({ ok: false, code: 'no-change', flowRevision: 0 });
    expect(context.inspector.patchNodeParameter('Mode', 'Manual')).toMatchObject({ ok: true, flowRevision: 1 });
    expect(context.inspector.patchNodeParameter('Count', 11)).toMatchObject({ ok: false, code: 'validation-failed', flowRevision: 1 });
    expect(context.inspector.patchNodeParameter('OptionalCount', null)).toMatchObject({ ok: false, code: 'no-change', flowRevision: 1 });
    expect(context.inspector.patchNodeParameter('OptionalCount', 0)).toMatchObject({ ok: true, flowRevision: 2 });
    expect(context.inspector.projection.validationErrors[0]?.code).toBe('range');
    expect(context.inspector.patchNodeParameter('Count', 10)).toMatchObject({ ok: true, flowRevision: 3 });
    expect(context.inspector.projection.validationErrors).toEqual([]);

    context.inspector.dispose();
    context.flowLease.dispose();
    context.workspace.dispose();
    context.diagnostics.dispose();
  });

  it('blocks readonly/running mutations while preserving view state and releases drafts/subscriptions', async () => {
    const context = mountInspector();
    select(context.state, { selectedNodeId: sourceId, selectedNodeIds: [sourceId], selectedConnectionId: null });
    await nextTick();
    context.inspector.setDraftActive('parameter:Text', true);
    expect(context.diagnostics.diagnostics).toMatchObject({ inspectorOwnerCount: 1, activeInspectorDrafts: 1 });

    (context.state as { mutationGate: FlowMutationGate }).mutationGate = 'readonly';
    await nextTick();
    expect(context.inspector.patchNodeParameter('Text', 'blocked')).toMatchObject({ ok: false, code: 'readonly', flowRevision: 0 });
    (context.state as { mutationGate: FlowMutationGate }).mutationGate = 'running';
    await nextTick();
    expect(context.inspector.patchNodeProperties({ name: 'blocked' })).toMatchObject({ ok: false, code: 'running', flowRevision: 0 });
    expect(context.inspector.projection.node?.name).toBe('Source node');

    context.inspector.dispose('test-dispose');
    expect(context.diagnostics.diagnostics).toMatchObject({ inspectorOwnerCount: 0, activeInspectorDrafts: 0 });
    context.flowLease.dispose();
    context.workspace.dispose();
    context.diagnostics.dispose();
  });

  it('shows metadata missing/error states and refuses parameter writes without trusted metadata', async () => {
    const context = mountInspector();
    context.state.catalog = Object.freeze({
      phase: 'success', operators: Object.freeze([]), isRefreshing: false, message: null
    });
    select(context.state, { selectedNodeId: sourceId, selectedNodeIds: [sourceId], selectedConnectionId: null });
    await nextTick();
    expect(context.inspector.projection.node?.metadataPhase).toBe('missing');
    expect(context.inspector.patchNodeParameter('Text', 'blocked')).toMatchObject({ ok: false, code: 'metadata-unavailable', flowRevision: 0 });

    context.inspector.dispose();
    context.flowLease.dispose();
    context.workspace.dispose();
    context.diagnostics.dispose();
  });

  it('uses typed disconnect and endpoint selection commands', async () => {
    const context = mountInspector();
    select(context.state, { selectedNodeId: null, selectedNodeIds: [], selectedConnectionId: connectionId });
    await nextTick();
    const selected = vi.spyOn(context.flowOwner.commands, 'selectNode');
    context.inspector.selectNode(sourceId);
    expect(selected).toHaveBeenCalledWith(sourceId);

    select(context.state, { selectedNodeId: null, selectedNodeIds: [], selectedConnectionId: connectionId });
    await nextTick();
    expect(context.inspector.disconnectConnection()).toMatchObject({ ok: true, flowRevision: 1 });
    expect(context.state.draft.connections).toHaveLength(0);

    context.inspector.dispose();
    context.flowLease.dispose();
    context.workspace.dispose();
    context.diagnostics.dispose();
  });
});
