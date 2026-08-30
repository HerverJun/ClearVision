import { describe, expect, it } from 'vitest';
import type {
  HostedFlowCanvasViewState,
  LegacyFlowCanvasAdapter,
  LegacyFlowCanvasSnapshot,
  LegacyHttpClient
} from '@/adapters/legacyModules';
import {
  createStudioFlowEditorPort,
  type StudioFlowEditorPort
} from '@/flowEditor/studioFlowEditorPort';
import {
  createStudioProjectPersistencePort,
  type StudioProjectPersistencePort
} from '@/project/studioProjectPersistencePort';

describe('StudioProjectPersistencePort', () => {
  it('ignores a late project A open after project B wins and allows an explicit newer reopen', async () => {
    const fixture = createPersistenceFixture();
    const projectAOpen = fixture.persistencePort.openProject('project-a');
    const projectBOpen = fixture.persistencePort.openProject('project-b');

    getCall(fixture.http, 1).deferred.resolve(createProjectDto('project-b', 7, createFlowFixture('project-b')));
    const projectB = await projectBOpen;
    getCall(fixture.http, 0).deferred.resolve(createProjectDto('project-a', 3, createFlowFixture('project-a')));
    const lateProjectA = await projectAOpen;

    expect(projectB.disposition).toBe('accepted');
    expect(lateProjectA.disposition).toBe('stale_request');
    expect(fixture.persistencePort.getSnapshot().projectId).toBe('project-b');

    const reopenedProjectA = fixture.persistencePort.openProject('project-a');
    getCall(fixture.http, 2).deferred.resolve(createProjectDto('project-a', 4, createFlowFixture('project-a')));
    const reopened = await reopenedProjectA;

    expect(reopened.disposition).toBe('accepted');
    expect(fixture.persistencePort.getSnapshot().projectId).toBe('project-a');
  });

  it('does not let an earlier open success pollute the current snapshot after a newer open fails', async () => {
    const fixture = createPersistenceFixture();
    await openProject(fixture, createProjectDto('project-c', 1, createFlowFixture('project-c')));

    const projectAOpen = fixture.persistencePort.openProject('project-a');
    const projectBOpen = fixture.persistencePort.openProject('project-b');
    getCall(fixture.http, 2).deferred.reject(new Error('network down'));
    const projectB = await projectBOpen;
    const snapshotAfterProjectBFailure = fixture.persistencePort.getSnapshot();

    getCall(fixture.http, 1).deferred.resolve(createProjectDto('project-a', 3, createFlowFixture('project-a')));
    const lateProjectA = await projectAOpen;

    expect(projectB.disposition).toBe('network_error');
    expect(snapshotAfterProjectBFailure.projectId).toBe('project-c');
    expect(snapshotAfterProjectBFailure.lastDisposition).toBe('network_error');
    expect(lateProjectA.disposition).toBe('stale_request');
    expect(fixture.persistencePort.getSnapshot()).toEqual(snapshotAfterProjectBFailure);
    expect(fixture.flowPort.getSnapshot().projectId).toBe('project-c');
  });

  it('leaves the loaded project snapshot unchanged when an older open returns after a newer success', async () => {
    const fixture = createPersistenceFixture();
    const projectAOpen = fixture.persistencePort.openProject('project-a');
    const projectBOpen = fixture.persistencePort.openProject('project-b');

    getCall(fixture.http, 1).deferred.resolve(createProjectDto('project-b', 7, createFlowFixture('project-b')));
    const projectB = await projectBOpen;
    const snapshotAfterProjectB = fixture.persistencePort.getSnapshot();

    getCall(fixture.http, 0).deferred.resolve(createProjectDto('project-a', 3, createFlowFixture('project-a')));
    const lateProjectA = await projectAOpen;

    expect(projectB.disposition).toBe('accepted');
    expect(lateProjectA.disposition).toBe('stale_request');
    expect(lateProjectA.snapshot).toEqual(snapshotAfterProjectB);
    expect(fixture.persistencePort.getSnapshot()).toEqual(snapshotAfterProjectB);
    expect(fixture.persistencePort.getSnapshot().lastDisposition).toBe('accepted');
    expect(fixture.persistencePort.getSnapshot().error).toBe('');
  });

  it('saves project metadata and flow without resubmitting authoritative global variables', async () => {
    const fixture = createPersistenceFixture();
    await openProject(fixture, createProjectDto('project-a', 5));
    patchThreshold(fixture.flowPort, 'project-a', 'node-a', 21);

    const saveTask = fixture.persistencePort.save();
    const put = putCall(fixture.http, 0);
    const payload = put.data as ProjectSavePayloadFixture;

    expect(put.url).toBe('/projects/project-a');
    expect(fixture.http.putCalls).toHaveLength(1);
    expect(fixture.http.putCalls.some((call) => call.url.includes('/flow'))).toBe(false);
    expect(fixture.http.putCalls.some((call) => call.url.includes('/global-variables'))).toBe(false);
    expect(payload.name).toBe('Project project-a');
    expect(payload.description).toBe('Description project-a');
    expect(payload.expectedPersistenceRevision).toBe(5);
    expect(payload).not.toHaveProperty('globalVariables');
    expect(getParameterValue(payload.flow, 'node-a', 'Threshold')).toBe(21);

    put.deferred.resolve(createProjectDto('project-a', 6, payload.flow));
    const saved = await saveTask;

    expect(saved.disposition).toBe('accepted');
    expect(fixture.persistencePort.getSnapshot().persistenceRevision).toBe(6);
    expect(fixture.persistencePort.getSnapshot().dirty).toBe(false);
  });

  it('maps PSV011 to stale_persistence_revision and keeps the draft dirty', async () => {
    const fixture = createPersistenceFixture();
    await openProject(fixture, createProjectDto('project-a', 5));
    patchThreshold(fixture.flowPort, 'project-a', 'node-a', 22);

    const saveTask = fixture.persistencePort.save();
    putCall(fixture.http, 0).deferred.reject(createHttpError(409, {
      code: 'PSV011',
      error: 'stale revision'
    }));
    const result = await saveTask;

    expect(result.disposition).toBe('stale_persistence_revision');
    expect(result.httpStatus).toBe(409);
    expect(result.errorCode).toBe('PSV011');
    expect(result.snapshot.dirty).toBe(true);
  });

  it('maps GV031 to runtime_busy without writing a second authority', async () => {
    const fixture = createPersistenceFixture();
    await openProject(fixture, createProjectDto('project-a', 5));

    const saveTask = fixture.persistencePort.save();
    putCall(fixture.http, 0).deferred.reject(createHttpError(409, {
      code: 'GV031',
      error: 'Project is currently running.'
    }));
    const result = await saveTask;

    expect(result.disposition).toBe('runtime_busy');
    expect(result.errorCode).toBe('GV031');
  });

  it('preserves a newer local flow draft when save response returns after another edit', async () => {
    const fixture = createPersistenceFixture();
    await openProject(fixture, createProjectDto('project-a', 5));
    patchThreshold(fixture.flowPort, 'project-a', 'node-a', 21);

    const saveTask = fixture.persistencePort.save();
    const payload = putCall(fixture.http, 0).data as ProjectSavePayloadFixture;
    patchThreshold(fixture.flowPort, 'project-a', 'node-a', 22);
    putCall(fixture.http, 0).deferred.resolve(createProjectDto('project-a', 6, payload.flow));
    const result = await saveTask;

    expect(result.disposition).toBe('accepted');
    expect(result.snapshot.persistenceRevision).toBe(6);
    expect(result.snapshot.dirty).toBe(true);
    expect(getParameterValue(fixture.flowPort.getSnapshot().flow, 'node-a', 'Threshold')).toBe(22);
  });

  it('does not let an old project save response pollute the current project after switching', async () => {
    const fixture = createPersistenceFixture();
    await openProject(fixture, createProjectDto('project-a', 5));
    patchThreshold(fixture.flowPort, 'project-a', 'node-a', 21);
    const saveProjectA = fixture.persistencePort.save();

    const openProjectB = fixture.persistencePort.openProject('project-b');
    getCall(fixture.http, 1).deferred.resolve(createProjectDto('project-b', 10, createFlowFixture('project-b')));
    await openProjectB;

    putCall(fixture.http, 0).deferred.resolve(createProjectDto('project-a', 6));
    const oldSave = await saveProjectA;

    expect(oldSave.disposition).toBe('stale_request');
    expect(fixture.persistencePort.getSnapshot().projectId).toBe('project-b');
    expect(fixture.persistencePort.getSnapshot().persistenceRevision).toBe(10);
  });

  it('rejects a save response for another project without writing it into the current projection', async () => {
    const fixture = createPersistenceFixture();
    await openProject(fixture, createProjectDto('project-a', 5));
    patchThreshold(fixture.flowPort, 'project-a', 'node-a', 21);

    const saveTask = fixture.persistencePort.save();
    putCall(fixture.http, 0).deferred.resolve(createProjectDto('project-b', 6, createFlowFixture('project-b')));
    const result = await saveTask;
    const snapshot = fixture.persistencePort.getSnapshot();

    expect(result.disposition).toBe('project_mismatch');
    expect(snapshot.projectId).toBe('project-a');
    expect(snapshot.name).toBe('Project project-a');
    expect(snapshot.persistenceRevision).toBe(5);
    expect(snapshot.project?.id).toBe('project-a');
    expect(snapshot.lastDisposition).toBe('project_mismatch');
    expect(fixture.flowPort.getSnapshot().projectId).toBe('project-a');
  });

  it('rejects same-project duplicate saves while a save is in flight', async () => {
    const fixture = createPersistenceFixture();
    await openProject(fixture, createProjectDto('project-a', 5));

    const firstSave = fixture.persistencePort.save();
    const secondSave = await fixture.persistencePort.save();

    expect(secondSave.disposition).toBe('in_flight');
    putCall(fixture.http, 0).deferred.resolve(createProjectDto('project-a', 6));
    expect((await firstSave).disposition).toBe('accepted');
    expect(fixture.http.putCalls).toHaveLength(1);
  });

  it('aborts pending requests on dispose and leaves the port disposed', async () => {
    const fixture = createPersistenceFixture();
    const openTask = fixture.persistencePort.openProject('project-a');

    fixture.persistencePort.dispose();
    const result = await openTask;

    expect(result.disposition).toBe('disposed');
    expect(fixture.persistencePort.getSnapshot().status).toBe('disposed');
  });
});

interface PersistenceFixture {
  readonly http: FakeLegacyHttpClient;
  readonly flowPort: StudioFlowEditorPort;
  readonly persistencePort: StudioProjectPersistencePort;
}

interface ProjectSavePayloadFixture {
  readonly name: string;
  readonly description: string | null;
  readonly flow: unknown;
  readonly expectedPersistenceRevision: number;
}

interface FlowFixture {
  readonly operators: Array<{
    readonly id: string;
    readonly type: string;
    readonly title: string;
    readonly parameters: Array<{
      readonly name: string;
      readonly displayName: string;
      value: unknown;
      readonly dataType: string;
    }>;
  }>;
  readonly connections: [];
}

interface HttpCallFixture {
  readonly method: 'GET' | 'PUT';
  readonly url: string;
  readonly data: unknown;
  readonly deferred: Deferred<unknown>;
}

class FakeLegacyHttpClient implements LegacyHttpClient {
  readonly getCalls: HttpCallFixture[] = [];
  readonly putCalls: HttpCallFixture[] = [];

  get<T = unknown>(
    url: string,
    _params?: Record<string, string> | null,
    options?: { readonly signal?: AbortSignal }
  ): Promise<T> {
    const call = this.createCall('GET', url, null, options);
    this.getCalls.push(call);
    return call.deferred.promise as Promise<T>;
  }

  put<T = unknown>(
    url: string,
    data?: unknown,
    options?: { readonly signal?: AbortSignal }
  ): Promise<T> {
    const call = this.createCall('PUT', url, data, options);
    this.putCalls.push(call);
    return call.deferred.promise as Promise<T>;
  }

  getRoot<T = unknown>(): Promise<T> {
    return Promise.resolve({} as T);
  }

  private createCall(
    method: 'GET' | 'PUT',
    url: string,
    data: unknown,
    options?: { readonly signal?: AbortSignal }
  ): HttpCallFixture {
    const deferred = new Deferred<unknown>();
    const signal = options?.signal;
    if (signal?.aborted) {
      deferred.reject(createAbortError());
    } else if (signal) {
      signal.addEventListener('abort', () => {
        deferred.reject(createAbortError());
      }, { once: true });
    }

    return {
      method,
      url,
      data,
      deferred
    };
  }
}

class Deferred<T> {
  readonly promise: Promise<T>;
  private resolvePromise: ((value: T | PromiseLike<T>) => void) | null = null;
  private rejectPromise: ((reason?: unknown) => void) | null = null;

  constructor() {
    this.promise = new Promise<T>((resolve, reject) => {
      this.resolvePromise = resolve;
      this.rejectPromise = reject;
    });
  }

  resolve(value: T): void {
    if (!this.resolvePromise) {
      throw new Error('Deferred resolve is not ready.');
    }

    this.resolvePromise(value);
  }

  reject(reason: unknown): void {
    if (!this.rejectPromise) {
      throw new Error('Deferred reject is not ready.');
    }

    this.rejectPromise(reason);
  }
}

async function openProject(
  fixture: PersistenceFixture,
  project: ReturnType<typeof createProjectDto>
): Promise<void> {
  const openTask = fixture.persistencePort.openProject(project.id);
  lastGetCall(fixture.http).deferred.resolve(project);
  const result = await openTask;
  expect(result.disposition).toBe('accepted');
}

function getCall(http: FakeLegacyHttpClient, index: number): HttpCallFixture {
  const call = http.getCalls[index];
  if (!call) {
    throw new Error(`Missing GET call at index ${String(index)}.`);
  }

  return call;
}

function lastGetCall(http: FakeLegacyHttpClient): HttpCallFixture {
  const call = http.getCalls[http.getCalls.length - 1];
  if (!call) {
    throw new Error('Missing last GET call.');
  }

  return call;
}

function putCall(http: FakeLegacyHttpClient, index: number): HttpCallFixture {
  const call = http.putCalls[index];
  if (!call) {
    throw new Error(`Missing PUT call at index ${String(index)}.`);
  }

  return call;
}

function createPersistenceFixture(): PersistenceFixture {
  const http = new FakeLegacyHttpClient();
  const flowPort = createStudioFlowEditorPort(createLegacyAdapterFixture());
  const persistencePort = createStudioProjectPersistencePort(http, flowPort);
  return {
    http,
    flowPort,
    persistencePort
  };
}

function patchThreshold(
  flowPort: StudioFlowEditorPort,
  projectId: string,
  nodeId: string,
  value: unknown
): void {
  const select = flowPort.selectNode({
    projectId,
    requestSequence: flowPort.nextRequestSequence(projectId),
    nodeId
  });
  expect(select.disposition).toBe('accepted');

  const snapshot = flowPort.getSnapshot();
  const patch = flowPort.patchParameters({
    projectId,
    requestSequence: flowPort.nextRequestSequence(projectId),
    expectedFlowRevision: snapshot.flowRevision,
    expectedSelectionRevision: snapshot.selectionRevision,
    nodeId,
    parameters: { Threshold: value }
  });
  expect(patch.disposition).toBe('accepted');
}

function createProjectDto(
  projectId: string,
  persistenceRevision: number,
  flow: unknown = createFlowFixture(projectId),
  globalVariables: unknown = createGlobalVariablesFixture()
) {
  return {
    id: projectId,
    name: `Project ${projectId}`,
    description: `Description ${projectId}`,
    persistenceRevision,
    flow,
    globalVariables
  };
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
      }
    ],
    connections: []
  };
}

function createGlobalVariablesFixture(): unknown {
  return {
    schemaVersion: '1.0',
    variables: [
      {
        id: 'variable-a',
        name: 'stats.count',
        valueType: 'Int64',
        initialValue: 1
      }
    ],
    sourceBindings: [],
    targetBindings: []
  };
}

function createLegacyAdapterFixture(): LegacyFlowCanvasAdapter {
  let flowRevision = 0;
  let selectionRevision = 0;
  let selectedNodeId: string | null = null;
  let flow: FlowFixture = { operators: [], connections: [] };
  const structureListeners = new Set<(event: unknown) => void>();
  const selectionListeners = new Set<(event: unknown) => void>();

  return {
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
}

function getParameterValue(flow: unknown, nodeId: string, parameterName: string): unknown {
  const flowFixture = flow as FlowFixture;
  return flowFixture.operators
    .find((operator) => operator.id === nodeId)
    ?.parameters
    .find((parameter) => parameter.name === parameterName)
    ?.value;
}

function createHttpError(status: number, payload: unknown): Error & {
  readonly status: number;
  readonly payload: unknown;
} {
  const error = new Error(isRecord(payload) && typeof payload.error === 'string' ? payload.error : `HTTP ${String(status)}`) as Error & {
    status: number;
    payload: unknown;
  };
  error.status = status;
  error.payload = payload;
  return error;
}

function createAbortError(): Error {
  const error = new Error('Aborted');
  error.name = 'AbortError';
  return error;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === 'object' && !Array.isArray(value));
}

function deepClone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}
