import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import {
  ApiConflictError,
  ApiForbiddenError,
  ApiNetworkError,
  ApiServerError
} from '@/platform/api';
import type { FlowCanvasOwner } from '@/capabilities/project-workspace/flow';
import {
  createWorkspaceLifecycleDiagnosticsOwner,
  decodeWorkspaceProjectV1,
  type WorkspaceProjectUpdatePayloadV1,
  type WorkspaceProjectV1
} from '@/capabilities/project-workspace';
import {
  createWorkspacePersistenceOwner,
  type WorkspaceProjectPersistencePort
} from '@/capabilities/project-workspace/persistence';
import { createWorkspaceGlobalVariablesOwner } from '@/capabilities/project-workspace/global-variables';
import type { ApiTransport } from '@/platform/api';

const projectId = '11111111-1111-4111-8111-111111111111';
const flowId = '22222222-2222-4222-8222-222222222222';
const operatorId = '33333333-3333-4333-8333-333333333333';
const parameterId = '44444444-4444-4444-8444-444444444444';

function rawProject(revision = 3, parameterValue: unknown = 10) {
  return {
    id: projectId,
    name: 'Workspace Persistence',
    description: null,
    version: '1.0.0',
    persistenceRevision: revision,
    flow: {
      id: flowId,
      name: 'Main',
      futureFlowField: { keep: true },
      operators: [{
        id: operatorId,
        name: 'Threshold',
        type: 'Thresholding',
        metadata: { lifecycle: 'Stable' },
        x: 20,
        y: 30,
        inputPorts: [],
        outputPorts: [],
        parameters: [{
          id: parameterId,
          name: 'Threshold',
          displayName: 'Threshold',
          description: null,
          dataType: 'int',
          value: parameterValue,
          defaultValue: 0,
          minValue: 0,
          maxValue: 255,
          isRequired: false,
          options: null,
          futureParameterField: 'keep'
        }],
        isEnabled: true,
        executionStatus: 'NotExecuted',
        executionTimeMs: null,
        errorMessage: null,
        futureOperatorField: 'keep'
      }],
      connections: [],
      decisionConfiguration: null
    },
    globalSettings: {},
    globalVariables: { schemaVersion: '1.0', variables: [], sourceBindings: [], targetBindings: [] },
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] },
    createdAt: '2026-07-17T00:00:00Z',
    modifiedAt: '2026-07-17T00:00:00Z',
    lastOpenedAt: null
  };
}

function baseline(revision = 3, parameterValue: unknown = 10): WorkspaceProjectV1 {
  return decodeWorkspaceProjectV1(rawProject(revision, parameterValue));
}

function responseFromPayload(payload: WorkspaceProjectUpdatePayloadV1, revision: number): WorkspaceProjectV1 {
  const raw = structuredClone(rawProject(revision)) as Record<string, unknown>;
  raw.name = payload.name;
  raw.description = payload.description;
  const flow = structuredClone(payload.flow) as Record<string, unknown>;
  raw.flow = flow;
  for (const operator of flow.operators as Array<Record<string, unknown>>) {
    operator.executionStatus = 'NotExecuted';
    operator.executionTimeMs = null;
    operator.errorMessage = null;
  }
  return decodeWorkspaceProjectV1(raw);
}

function createFlowOwner(project: WorkspaceProjectV1) {
  const projection = reactive({
    phase: 'mounted',
    projectId,
    mutationGate: 'editable',
    draft: {
      id: project.flow!.id,
      name: project.flow!.name,
      operators: structuredClone(project.flow!.operators.map(operator => ({
        ...operator.opaquePassthrough,
        id: operator.id,
        name: operator.name,
        type: operator.type.persistenceValue,
        metadata: operator.metadata,
        x: operator.x,
        y: operator.y,
        inputPorts: operator.inputPorts,
        outputPorts: operator.outputPorts,
        parameters: operator.parameters.map(parameter => ({
          ...parameter.opaquePassthrough,
          id: parameter.id,
          name: parameter.name,
          displayName: parameter.displayName,
          description: parameter.description,
          dataType: parameter.dataType,
          value: parameter.value,
          defaultValue: parameter.defaultValue,
          minValue: parameter.minValue,
          maxValue: parameter.maxValue,
          isRequired: parameter.isRequired,
          options: parameter.options
        })),
        isEnabled: operator.isEnabled
      }))),
      connections: [],
      decisionConfiguration: null,
      opaquePassthrough: project.flow!.opaquePassthrough
    },
    runtime: { flowRevision: 0 },
    feedback: null,
    catalog: { phase: 'success', operators: [], isRefreshing: false, message: null },
    error: null
  });
  const owner = {
    projectId,
    projection,
    commands: {},
    mountCanvas: vi.fn(),
    replaceFlow: vi.fn((flow: Readonly<Record<string, unknown>> | null, projectName: string) => {
      const source = flow ?? { id: null, name: `${projectName} Flow`, operators: [], connections: [], decisionConfiguration: null };
      projection.draft = reactive({
        id: typeof source.id === 'string' ? source.id : null,
        name: typeof source.name === 'string' ? source.name : `${projectName} Flow`,
        operators: structuredClone(Array.isArray(source.operators) ? source.operators : []),
        connections: structuredClone(Array.isArray(source.connections) ? source.connections : []),
        decisionConfiguration: source.decisionConfiguration ?? null,
        opaquePassthrough: Object.fromEntries(Object.entries(source).filter(([key]) =>
          !['id', 'name', 'operators', 'connections', 'decisionConfiguration'].includes(key)))
      }) as typeof projection.draft;
      projection.runtime.flowRevision = 0;
    }),
    openInspector: vi.fn(),
    openPreviewWorkbench: vi.fn(),
    refreshOperators: vi.fn(),
    setMutationGate: vi.fn((gate: 'editable' | 'readonly' | 'running') => {
      projection.mutationGate = gate;
    }),
    dispose: vi.fn()
  } as unknown as FlowCanvasOwner;
  const editParameter = (value: unknown) => {
    const operators = JSON.parse(JSON.stringify(projection.draft.operators)) as Array<Record<string, unknown>>;
    const parameters = operators[0]!.parameters as Array<Record<string, unknown>>;
    parameters[0]!.value = value;
    projection.draft = { ...projection.draft, operators } as unknown as typeof projection.draft;
    projection.runtime.flowRevision += 1;
  };
  const emitNonRevisionDraftRefresh = () => {
    const operators = JSON.parse(JSON.stringify(projection.draft.operators)) as Array<Record<string, unknown>>;
    operators[0]!.x = Number(operators[0]!.x) + 0.25;
    projection.draft = { ...projection.draft, operators } as unknown as typeof projection.draft;
  };
  return { owner, projection, editParameter, emitNonRevisionDraftRefresh };
}

function createHarness(portOverrides: Partial<WorkspaceProjectPersistencePort> = {}) {
  const project = baseline();
  const flow = createFlowOwner(project);
  const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
  const putProject = vi.fn(async (payload: WorkspaceProjectUpdatePayloadV1) => responseFromPayload(payload, 4));
  const port: WorkspaceProjectPersistencePort = {
    projectId,
    getProject: vi.fn(async () => baseline(4)),
    putProject,
    ...portOverrides
  };
  let latestBaseline = project;
  const owner = createWorkspacePersistenceOwner({
    baseline: project,
    flowOwner: flow.owner,
    globalVariablesOwner: createWorkspaceGlobalVariablesOwner({
      projectId,
      baseline: project.globalVariables,
      api: { apiBaseUrl: 'http://127.0.0.1/api' } as ApiTransport
    }),
    port,
    diagnostics,
    onBaselineChanged(value) { latestBaseline = value; }
  });
  return { owner, flow, diagnostics, port, putProject, get latestBaseline() { return latestBaseline; } };
}

describe('F03 G5 Workspace persistence owner', () => {
  it('blocks no-op PUT, saves one payload, clears dirty, then becomes dirty after the next edit', async () => {
    const harness = createHarness();
    await expect(harness.owner.save()).resolves.toMatchObject({ status: 'no-op' });
    expect(harness.putProject).not.toHaveBeenCalled();

    harness.flow.editParameter(null);
    expect(harness.owner.projection).toMatchObject({ phase: 'dirty', dirty: true, canSave: true });
    await expect(harness.owner.save()).resolves.toMatchObject({ status: 'saved' });

    expect(harness.putProject).toHaveBeenCalledTimes(1);
    expect(harness.putProject.mock.calls[0]?.[0]).toMatchObject({
      expectedPersistenceRevision: 3,
      globalVariables: {
        schemaVersion: '1.0',
        variables: [],
        sourceBindings: [],
        targetBindings: []
      },
      flow: {
        futureFlowField: { keep: true },
        operators: [{
          futureOperatorField: 'keep',
          parameters: [{ value: null, futureParameterField: 'keep' }]
        }]
      }
    });
    expect(harness.owner.projection).toMatchObject({
      phase: 'saved', dirty: false, persistenceRevision: 4
    });
    harness.flow.editParameter(0);
    expect(harness.owner.projection).toMatchObject({ phase: 'dirty', dirty: true });
    harness.owner.dispose();
    await harness.owner.settle();
    harness.diagnostics.dispose();
  });

  it('ignores non-edit Canvas draft refreshes when flowRevision does not advance', async () => {
    const harness = createHarness();
    harness.flow.editParameter(11);
    await expect(harness.owner.save()).resolves.toMatchObject({ status: 'saved' });
    expect(harness.owner.projection).toMatchObject({ phase: 'saved', dirty: false });

    harness.flow.emitNonRevisionDraftRefresh();
    expect(harness.flow.projection.runtime.flowRevision).toBe(0);
    expect(harness.owner.projection).toMatchObject({ phase: 'saved', dirty: false, canSave: false });

    harness.flow.editParameter(12);
    expect(harness.owner.projection).toMatchObject({ phase: 'dirty', dirty: true, canSave: true });
    harness.owner.dispose();
    await harness.owner.settle();
    harness.diagnostics.dispose();
  });

  it('updates the server revision but keeps newer edits dirty when editing continues during save', async () => {
    let resolveSave: ((project: WorkspaceProjectV1) => void) | undefined;
    const putProject = vi.fn((payload: WorkspaceProjectUpdatePayloadV1) =>
      new Promise<WorkspaceProjectV1>(resolve => {
        resolveSave = () => resolve(responseFromPayload(payload, 4));
      }));
    const harness = createHarness({ putProject });
    harness.flow.editParameter(20);
    const saving = harness.owner.save();
    expect(harness.owner.projection.phase).toBe('saving');
    harness.flow.editParameter(21);
    resolveSave?.(baseline(4));
    await saving;

    expect(harness.owner.projection).toMatchObject({
      phase: 'dirty', dirty: true, persistenceRevision: 4
    });
    const parameter = (harness.flow.projection.draft.operators[0]!.parameters as Array<Record<string, unknown>>)[0]!;
    expect(parameter.value).toBe(21);
    harness.owner.dispose();
    await harness.owner.settle();
    harness.diagnostics.dispose();
  });

  it('preserves the draft across failure and retries only after an explicit retry command', async () => {
    const putProject = vi.fn()
      .mockRejectedValueOnce(new ApiServerError({
        url: `http://localhost/api/projects/${projectId}`,
        status: 500,
        statusText: 'failure',
        payload: { code: 'PSV999' },
        responseBody: ''
      }))
      .mockImplementationOnce(async (payload: WorkspaceProjectUpdatePayloadV1) => responseFromPayload(payload, 4));
    const harness = createHarness({ putProject });
    harness.flow.editParameter(false);

    await expect(harness.owner.save()).resolves.toMatchObject({ status: 'failed' });
    expect(harness.owner.projection).toMatchObject({ phase: 'error', dirty: true, canRetry: true });
    expect(putProject).toHaveBeenCalledTimes(1);
    await expect(harness.owner.retry()).resolves.toMatchObject({ status: 'saved' });
    expect(putProject).toHaveBeenCalledTimes(2);
    harness.owner.dispose();
    await harness.owner.settle();
    harness.diagnostics.dispose();
  });

  it('fails closed on PSV011, GET reconciles, preserves the draft and requires explicit reapply before retry', async () => {
    const putProject = vi.fn()
      .mockRejectedValueOnce(new ApiConflictError({
        url: `http://localhost/api/projects/${projectId}`,
        status: 409,
        statusText: 'Conflict',
        payload: { code: 'PSV011' },
        responseBody: ''
      }))
      .mockImplementationOnce(async (payload: WorkspaceProjectUpdatePayloadV1) => responseFromPayload(payload, 5));
    const getProject = vi.fn(async () => baseline(4, 12));
    const harness = createHarness({ putProject, getProject });
    harness.flow.editParameter(42);

    await expect(harness.owner.save()).resolves.toMatchObject({ status: 'conflict' });
    expect(getProject).toHaveBeenCalledTimes(1);
    expect(harness.owner.projection).toMatchObject({
      phase: 'conflict', dirty: true, conflictServerRevision: 4, canSave: false
    });
    const conflicted = (harness.flow.projection.draft.operators[0]!.parameters as Array<Record<string, unknown>>)[0]!;
    expect(conflicted.value).toBe(42);

    harness.owner.reapplyConflict();
    expect(harness.owner.projection).toMatchObject({ phase: 'dirty', persistenceRevision: 4, canSave: true });
    await harness.owner.save();
    expect(putProject.mock.calls[1]?.[0]).toMatchObject({ expectedPersistenceRevision: 4 });
    harness.owner.dispose();
    await harness.owner.settle();
    harness.diagnostics.dispose();
  });

  it('requires GET reconcile after an unknown outcome before enabling explicit retry', async () => {
    const putProject = vi.fn()
      .mockRejectedValueOnce(new ApiNetworkError(`http://localhost/api/projects/${projectId}`, new Error('lost')))
      .mockImplementationOnce(async (payload: WorkspaceProjectUpdatePayloadV1) => responseFromPayload(payload, 4));
    const getProject = vi.fn(async () => baseline(3));
    const harness = createHarness({ putProject, getProject });
    harness.flow.editParameter('');

    await expect(harness.owner.save()).resolves.toMatchObject({ status: 'unknown-outcome' });
    expect(harness.owner.projection).toMatchObject({
      phase: 'unknown-outcome', dirty: true, canRetry: false, canReconcile: true
    });
    await expect(harness.owner.reconcile()).resolves.toMatchObject({ status: 'failed' });
    expect(harness.owner.projection).toMatchObject({ phase: 'error', canRetry: true });
    await harness.owner.retry();
    expect(putProject).toHaveBeenCalledTimes(2);
    harness.owner.dispose();
    await harness.owner.settle();
    harness.diagnostics.dispose();
  });

  it.each([
    ['readonly', new ApiForbiddenError({
      url: `http://localhost/api/projects/${projectId}`,
      status: 403,
      statusText: 'Forbidden',
      payload: { code: 'AUTH403' },
      responseBody: ''
    })],
    ['running', new ApiConflictError({
      url: `http://localhost/api/projects/${projectId}`,
      status: 409,
      statusText: 'Conflict',
      payload: { code: 'GV031' },
      responseBody: ''
    })]
  ] as const)('disables saving after the backend enters %s state', async (phase, error) => {
    const harness = createHarness({ putProject: vi.fn(async () => { throw error; }) });
    harness.flow.editParameter(25);
    await harness.owner.save();
    expect(harness.owner.projection).toMatchObject({ phase, dirty: true, canSave: false });
    expect(harness.flow.owner.setMutationGate).toHaveBeenCalledWith(phase);
    harness.owner.dispose();
    await harness.owner.settle();
    harness.diagnostics.dispose();
  });

  it('silently drops a reconcile response that arrives after the owner is disposed', async () => {
    let resolveRead: ((project: WorkspaceProjectV1) => void) | undefined;
    const getProject = vi.fn(() => new Promise<WorkspaceProjectV1>(resolve => {
      resolveRead = resolve;
    }));
    const harness = createHarness({
      putProject: vi.fn(async () => {
        throw new ApiNetworkError('http://localhost/api/projects/test', new Error('lost'));
      }),
      getProject
    });
    harness.flow.editParameter(77);
    await expect(harness.owner.save()).resolves.toMatchObject({ status: 'unknown-outcome' });

    const reconciling = harness.owner.reconcile();
    expect(harness.owner.projection.phase).toBe('unknown-outcome');
    harness.owner.dispose('route-leave');
    resolveRead?.(baseline(4));

    await expect(reconciling).resolves.toMatchObject({ status: 'disposed', project: null });
    expect(harness.owner.projection.phase).toBe('disposed');
    expect(harness.flow.owner.setMutationGate).not.toHaveBeenCalledWith('readonly');
    await harness.owner.settle();
    expect(harness.diagnostics.diagnostics).toMatchObject({
      persistenceOwnerCount: 0,
      inFlightReads: 0,
      inFlightWrites: 0
    });
    harness.diagnostics.dispose();
  });
});
